# SAFE 시뮬레이터 설치 및 사용 가이드

## 개요
SAFE 시뮬레이터는 소방관 구조 작업을 시뮬레이션하는 Unity 기반 프로그램으로, ROS2와 연동하여 스마트폰 IMU 데이터를 실시간으로 활용합니다.

## 시스템 요구사항
- **OS**: Ubuntu 22.04, Windows
- **Unity**: 6000.0.56f1
- **ROS2**: Humble
- **VPN**: Tailscale
- **네트워크**: ROS2 서버와 Unity PC가 동일 네트워크 연결 필수

## 설치 가이드

### Windows 환경 설정
```bash
mkdir Unity
git init
git clone https://lab.ssafy.com/s13-mobility-smarthome-sub1/S13P21A506.git
```

**필수 설치 항목:**
1. **Tailscale**: https://tailscale.com 에서 디바이스 등록
2. **Unity Hub**: https://unity.com/kr/download 에서 설치

**Unity 프로젝트 설정:**
1. Unity Hub에서 Add project from disk → 'simulator' 폴더 열기
2. 상단 메뉴 → Robotics → ROS Setting → ROS2 서버 IP 입력
3. Project → Assets → SAFE → New → new.scene 열기
4. PythonBridge 오브젝트 Inspector → Python Exe Path를 절대경로로 설정
5. File → Build profile → Windows Build

### Ubuntu 22.04 환경 설정
```bash
# 기본 설정
mkdir Unity
git init
git clone https://lab.ssafy.com/s13-mobility-smarthome-sub1/S13P21A506.git
```

**필수 설치 항목:**
1. **Tailscale**: https://tailscale.com 에서 디바이스 등록
2. **ROS2 Humble**: https://docs.ros.org/en/humble/Installation.html

## ROS2 실행 절차

### 터미널 1: IMU 퍼블리셔
```bash
cd Unity/S13P21A506/ros2_ws/extract_pose_topic_using_smartphone/imu_publisher/
source ./install/setup.bash
ros2 launch mobile_sensor mobile_sensors.launch.py
```

### 터미널 2: PDR 도구
```bash
cd Unity/S13P21A506/ros2_ws/extract_pose_topic_using_smartphone/pdr_ws/
source ./install/setup.bash
ros2 launch pdr_tools mobile_pose.launch.py
```

### 터미널 3: Unity 연동 서버
```bash
cd Unity/S13P21A506/ros2_ws/extract_pose_topic_using_smartphone/unity_integration/
source ./install/setup.bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=100.83.207.83
```

### 터미널 4: Tailscale VPN
```bash
sudo tailscale up
```

### 스마트폰 WebXR 설정
1. Tailscale 앱 실행 및 VPN 연결
2. 브라우저에서 `https://localhost:4000` 접속
3. **3D Position 옵션만 체크** 후 Start 실행
4. **카메라가 정면을 바라보도록 스마트폰 고정**

## 시뮬레이터 사용법

### 프로그램 실행
`simulator.exe` 파일 실행

### 기본 조작
**모드 전환:**
- **F1**: 캠 모드 (3D 뷰어)
- **F2**: UI 모드 (제어 패널)

**캠 모드 조작:**
- **W/A/S/D**: 수평 이동 (북/서/남/동)
- **Q/E**: 수직 이동 (위/아래)

### UI 모드 기능

#### 왼쪽 패널: 기체 농도 모니터링
위험 가스 농도를 입력하여 환경 위험도를 실시간 계산합니다.

**입력 항목:**
- **CO**: 일산화탄소 농도 (ppm)
- **CO2**: 이산화탄소 농도 (%)
- **O2**: 산소 농도 (%)
- **HCN**: 시안화수소 농도 (%)
- **Fire Duration**: 화재 지속 시간 (분)

**출력:**
- **FED**: 질식성 가스 노출 지수 (자동 계산)

#### 중간 패널: 소방관 관리
**소방관 생성:**
1. "Enter Fighter's name..." 필드에 고유한 이름 입력
2. **Spawn** 버튼으로 spawn 지점에 agent 생성

**객체 제어:**
- **왼쪽 드롭다운**: 소방관 agent 선택
- **오른쪽 드롭다운**: 피구조자 선택
- **Start Nav**: 선택된 소방관의 네비게이션 시작
- **하단 드롭다운 + Remove**: 불필요한 객체 제거

#### 오른쪽 패널: 네비게이션 관리
**경로 정보:**
- **All Paths**: 생성된 네비게이션 목록
- **Nav Name**: 선택된 경로 이름
- **Length Left**: 남은 경로 거리
- **Time Left**: 예상 소요 시간
- **Nav Erase**: 선택된 경로 삭제

## 사용 워크플로우

1. **환경 설정**
   - ROS2 서버 4개 터미널 모두 실행
   - 스마트폰 WebXR 연결 확인

2. **시뮬레이션 준비**
   - `simulator.exe` 실행 후 F2로 UI 모드 전환
   - 기체 농도 값 입력으로 환경 위험도 설정

3. **시나리오 구성**
   - 소방관 생성 (고유한 이름 사용)
   - 피구조자 선택

4. **시뮬레이션 실행**
   - **Start Nav** 클릭으로 구조 작업 시작
   - 스마트폰 IMU 데이터 수신 시 Navigation Line 생성
   - F1로 캠 모드 전환하여 시뮬레이션 관찰

5. **시뮬레이션 관리**
   - 경로 및 객체 제거로 필요시 리셋
   - 다양한 시나리오 반복 테스트

## 주의사항

**기술적 제약:**
- 모든 농도 값은 지정된 단위로 입력
- 소방관 이름은 반드시 고유해야 함
- 네비게이션 시작 전 소방관과 피구조자 모두 선택 필수

**네트워크 요구사항:**
- ROS2 서버와 Unity PC는 동일 네트워크 연결
- Tailscale VPN을 통한 안정적인 연결 유지
- Tailscale VPN 사용 시 같은 구글 계정으로 로그인 필수

**스마트폰 사용:**
- 카메라가 정면을 바라보는 방향으로 고정
- WebXR에서 3D Position 옵션만 활성화
- Navigation Line은 IMU 데이터 수신 시에만 표시

## 트러블슈팅

**연결 문제:**
- Tailscale VPN 연결 상태 확인
- ROS2 서버 IP 설정 재확인
- 방화벽 설정 점검

**시뮬레이션 문제:**
- 모든 ROS2 터미널이 정상 실행 중인지 확인
- 스마트폰 브라우저에서 WebXR 정상 작동 확인
- Unity 콘솔에서 에러 메시지 확인