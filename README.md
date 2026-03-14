# ssafy-SAFE — Unity 기반 화재 대응 지휘·관제 시뮬레이터 (YOLO-Pose × Unity IPC)

SSAFY 건물 맵을 기반으로, **화재 상황에서의 인원(Rescuee) 위치/상태를 Unity에서 시각화**하는 디지털 트윈 시뮬레이터입니다.  
내가 맡은 파트는 **Unity 내부에서 Python 프로세스를 구동해 YOLO-Pose를 실행**하고, **Pipe(표준입출력) 기반 IPC**로 포즈 데이터를 전달하여 **Unity 맵 상에 에이전트를 생성/갱신하고 포즈를 적용**하는 전체 파이프라인입니다.

> 담당 코드:  
> - `simulator/Assets/Scripts/` : `PythonBridge.cs`, `PoseManager.cs`, `PoseMapper.cs`  
> - `simulator/Assets/Python/` : `yolo_pose_viewer_25d.py`, `unified_stream.py`, `sender.py` 등  
> - 보조 README: `README_e.md`, `README_p.md`

---

## 맡은 파트 (Portfolio Focus)

### 1) Unity ↔ Python 멀티프로세스 IPC 브릿지
- Unity에서 **Python 프로세스를 직접 실행**하고(`System.Diagnostics.Process`)
- Python의 **stdout을 line 단위로 읽어** Unity 메인 스레드로 전달하는 구조
- 표준 출력 버퍼링으로 지연이 생기지 않도록 **Python에서 매 라인 강제 flush**  
  (테스트 스크립트 `sender.py`에서도 `sys.stdout.flush()`를 강조)

**관련 코드**
- `simulator/Assets/Scripts/PythonBridge.cs`
  - 파이썬 실행: `RedirectStandardOutput = true`, `ReadLine()` 기반 수신 스레드
  - 이벤트 전달: `ConcurrentQueue<string>` → `OnLine` 이벤트로 메인 스레드에서 처리
  - 인자 구성: `--src`, `--model`, `--conf`, `--iou`, `--tracker`, `--device`, `--realtime`, `--max_delay`, `--fps_override`

---

### 2) YOLO-Pose 실행 파이프라인 + 멀티 인원 추적(NDJSON 송출)
- Python에서 `ultralytics` YOLOv8 Pose를 로드해 **multi-person pose**를 추론
- **ByteTrack 기반 tracking**을 통해 `track_id`를 부여 → Unity에서 동일 인원으로 연속 추적 가능
- 결과를 **NDJSON(Newline Delimited JSON)** 로 1줄씩 송출 → Unity에서 `ReadLine()`로 안정적으로 파싱

**관련 코드**
- `simulator/Assets/Python/yolo_pose_viewer_25d.py`
  - `type="pose2d_frame"`, `seq`, `img_w`, `img_h`, `detections[]` 구조로 송출
  - `print(json.dumps(...), flush=True)`로 즉시 전달
- `simulator/Assets/Python/unified_stream.py`
  - 웹캠/파일/네트워크 등 입력 소스를 표준화하고, 저지연을 위해 **큐 제한 + 프레임 드롭** 전략을 제공
- (테스트) `simulator/Assets/Python/sender.py`
  - IPC 파이프/버퍼링 확인용 heartbeat NDJSON 송출

---

### 3) Unity에서 에이전트 생성/갱신 및 포즈 적용
- Python에서 들어오는 NDJSON을 **strong typing**으로 역직렬화
- 검출 품질 필터링:
  - detection score가 낮으면 무시
  - keypoint 신뢰도가 낮으면 무시(유효 keypoint 비율 기반)
- `track_id`를 key로 **actor 생성/업데이트/타임아웃 제거**를 수행
- 맵 상 좌표 배치 + 회전 + IK 타겟 업데이트로 “포즈”를 시각적으로 반영

**관련 코드**
- `simulator/Assets/Scripts/PoseManager.cs`
  - NDJSON → `Pose2DFrame`(프레임) / `Pose2DDet`(개별 인원) 구조로 파싱
  - `track_id → PoseMapper` 딕셔너리로 actor 관리
  - `actorLifetime` 기반 미검출 actor 제거
- `simulator/Assets/Scripts/PoseMapper.cs`
  - 2D keypoint + bbox로 **월드 좌표계 위치/방향을 추정**하고 캐릭터에 적용
  - bbox 높이 기반 **깊이(Z) 근사** + hip 기반 root 위치 추정 + 어깨/엉덩이 기반 방향(yaw/pitch/roll) 추정
  - EMA(지수이동평균) 기반 **스무딩**으로 지터 감소
  - 손/발/팔꿈치/무릎 IK target을 업데이트해 자연스러운 포즈 반영

---

## 핵심 의사결정 & 트레이드오프

### (1) Barracuda 대신 “Unity ↔ Python 멀티프로세스”를 택한 이유
Unity 내 추론(Barracuda)로 통합할 수도 있었지만, **7주라는 짧은 기간** 안에
- 모델 선택/교체,
- 추론 성능 튜닝,
- 멀티 인원 처리,
- 결과 시각화/맵 연동
까지 빠르게 완성해야 했습니다.

그래서 “검증된 Python 생태계(ultralytics)에서 바로 돌리고”  
Unity는 시각화/시뮬레이션에 집중하는 **분리 구조**를 선택했습니다.

- 장점: 빠른 프로토타입, 모델 교체/튜닝 용이, 멀티 인원 추적 구현 용이
- 단점: 배포 시 Python 런타임 포함, 프로세스 관리/경로 설정 필요

---

### (2) MediaPipe Pose 대신 YOLO-Pose를 선택한 이유
MediaPipe Pose는 일반적으로 **1인**(single-person) 시나리오를 주로 전제로 하며,  
CCTV 기반 상황(동시에 여러 명 검출 가능)에서는 **multi-person + tracking**이 중요했습니다.

따라서:
- multi-person pose 추론이 기본 지원되고,
- `track_id` 기반 연속 추적이 가능한
**YOLOv8 Pose + ByteTrack** 조합을 채택했습니다.

---

### (3) IPC로 NDJSON + flush를 사용한 이유
Unity에서 Python 결과를 “실시간”으로 반영하려면 **버퍼링 지연**이 치명적입니다.  
`ReadLine()` 기반 수신 구조에서 가장 단순하고 견고한 방식이 **NDJSON + flush** 였습니다.

- Python: `print(json, flush=True)`  
- Unity: stdout 라인을 읽어 큐에 넣고, 메인 스레드에서 이벤트로 처리

---

## 데이터 계약(IPC 메시지 스키마)

Python → Unity로 전달되는 메시지는 아래 형태(요약)입니다.

```json
{
  "type": "pose2d_frame",
  "seq": 123,
  "img_w": 1280,
  "img_h": 720,
  "detections": [
    {
      "track_id": 7,
      "score": 0.83,
      "bbox_xyxy": [x1, y1, x2, y2],
      "kps2d": [
        [x, y, conf],  // COCO17 keypoints
        ...
      ]
    }
  ]
}
```

---

## 리포지토리 구조 (GitHub 기준)

> 루트(Top-level): `exec/`, `front/`, `simulator/`, `README_e.md`, `README_p.md`, `SSAFY_Map.zip` 등  
> - `exec/`: 설치/사용 가이드, 웹 구현 가이드, 시연 시나리오 등 문서  
> - `front/`: React + TypeScript + Vite 기반 대시보드(UI)  
> - `simulator/`: Unity 프로젝트(맵/에셋/모델/스크립트 포함)

내 담당 코드는 `simulator` 내부에 집중되어 있습니다.

---

## 실행 방법 (내 담당 파트 기준)

### 1) Python 환경 준비
```bash
pip install ultralytics opencv-python numpy requests
```

### 2) Unity에서 Python 경로/스크립트 경로 설정
Unity Inspector에서 `PythonBridge` 컴포넌트의 다음 값을 환경에 맞게 설정:
- `pythonExePath`: python 실행 파일 경로
- `scriptPath`: `Assets/Python/yolo_pose_viewer_25d.py` 경로
- `modelName`: `yolov8s-pose.pt` (경로/파일명)

### 3) 실행 흐름
1. Unity 실행 → `PythonBridge.Start()`에서 Python 프로세스 실행
2. Python이 매 프레임 NDJSON을 stdout으로 송출(flush)
3. Unity가 라인을 수신 → `PoseManager`가 파싱/필터링 후 actor 생성/업데이트
4. `PoseMapper`가 맵 상 위치/회전/IK 타겟을 갱신하여 포즈 반영

---

## 한계 & 개선 아이디어

- 현재는 bbox 기반 깊이 근사 등 “가벼운 3D 추정”을 사용 → 실제 3D 정합/보정(캘리브레이션) 도입 시 정확도 향상 가능
- 배포 단계에서는
  - Python 환경 패키징(venv/embedded python),
  - IPC를 named pipe/ZeroMQ/WebSocket 등으로 표준화
  - (장기적으로) Barracuda/ONNX Runtime 등 Unity 내 추론 통합
  을 통해 운영성을 개선할 수 있습니다.
