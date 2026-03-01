#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
YOLOv8-pose + 추적 → NDJSON 송출 (Unity 수신) + 프레임 POST(불신뢰성)
- 웹캠/파일/네트워크 소스 지원
- 파일/네트워크 소스는 --realtime 옵션으로 "스트림처럼" 페이싱 가능 (FPS 기반 sleep + 지연 프레임 드롭)
- CUDA 텐서 → .cpu() 변환 후 tolist() (에러 방지)
- stdout 즉시 flush (NDJSON)
- --display 로 화면 표시(뼈대+바운딩박스) on/off
- 매 프레임을 JPEG로 인코딩해 http://localhost:8081/python-video/frame 으로 파이어-앤-포겟 POST
필요: pip install ultralytics opencv-python numpy requests
"""

import sys
import json
import argparse
import time
import math
import cv2
import numpy as np
import threading
import queue
import requests
from ultralytics import YOLO
from http.server import HTTPServer, BaseHTTPRequestHandler
import socketserver


class HttpFireAndForgetBytesSender:
    """작은 큐 + 전용 스레드로 바이너리(예: JPEG) 비동기 POST (실패/지연 시 드롭)"""
    def __init__(self, url: str, timeout: float = 0.25, max_queue: int = 5, headers=None):
        self.url = url
        self.timeout = timeout
        self.q = queue.Queue(maxsize=max_queue)
        self.headers = headers or {"Content-Type": "image/jpeg"}
        self._stop = threading.Event()
        self._thr = threading.Thread(target=self._run, daemon=True)
        self._session = requests.Session()
        self._thr.start()

    def send(self, blob: bytes):
        try:
            self.q.put_nowait(blob)
        except queue.Full:
            # 큐가 가득차면 드롭 (신뢰성 비보장)
            pass

    def _run(self):
        while not self._stop.is_set():
            try:
                data = self.q.get(timeout=0.1)
            except queue.Empty:
                continue
            try:
                self._session.post(
                    self.url,
                    headers=self.headers,
                    data=data,
                    timeout=self.timeout
                )
            except Exception:
                # 전송 실패 시 조용히 드롭
                pass
            finally:
                self.q.task_done()

    def close(self):
        self._stop.set()
        try:
            while not self.q.empty():
                self.q.get_nowait()
                self.q.task_done()
        except Exception:
            pass
        self._thr.join(timeout=0.3)
        try:
            self._session.close()
        except Exception:
            pass


class MJPEGServer:
    """MJPEG 스트림 서버"""
    def __init__(self, port=8082):
        self.port = port
        self.current_frame = None
        self.frame_lock = threading.Lock()
        self.clients = []
        self.server = None
        self.server_thread = None
        
    def start(self):
        """서버 시작"""
        class MJPEGHandler(BaseHTTPRequestHandler):
            def __init__(self, *args, **kwargs):
                self.mjpeg_server = kwargs.pop('mjpeg_server')
                super().__init__(*args, **kwargs)
                
            def do_GET(self):
                if self.path == '/stream.mjpg':
                    self.send_response(200)
                    self.send_header('Content-Type', 'multipart/x-mixed-replace; boundary=frame')
                    self.send_header('Cache-Control', 'no-cache')
                    self.send_header('Connection', 'close')
                    self.end_headers()
                    
                    try:
                        while True:
                            with self.mjpeg_server.frame_lock:
                                frame = self.mjpeg_server.current_frame
                            
                            if frame is not None:
                                # MJPEG 프레임 전송
                                self.wfile.write(b'--frame\r\n')
                                self.send_header('Content-Type', 'image/jpeg')
                                # 유찬추가
                                self.send_header('Cache-Control', 'no-cache, no-store, must-revalidate')
                                self.send_header('Pragma', 'no-cache')
                                self.send_header('Expires', '0')
                                self.send_header('Content-Length', str(len(frame)))
                                self.end_headers()
                                self.wfile.write(frame)
                                self.wfile.write(b'\r\n')
                                try:
                                    self.wfile.flush()
                                except Exception:
                                    pass
                            else:
                                time.sleep(0.033)  # 30fps 대기
                                
                    except (ConnectionResetError, BrokenPipeError):
                        pass
                else:
                    self.send_response(404)
                    self.end_headers()
                    
            def log_message(self, format, *args):
                pass  # 로그 비활성화
                
        # 서버 설정
        handler = lambda *args, **kwargs: MJPEGHandler(*args, mjpeg_server=self, **kwargs)
        self.server = socketserver.TCPServer(("", self.port), handler)
        self.server_thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.server_thread.start()
        print(f"[INFO] MJPEG 서버 시작: http://localhost:{self.port}/stream.mjpg", flush=True)
        
    def update_frame(self, frame_bytes):
        """새 프레임 업데이트"""
        with self.frame_lock:
            self.current_frame = frame_bytes
            
    def stop(self):
        """서버 중지"""
        if self.server:
            self.server.shutdown()
            self.server.server_close()
            print("[INFO] MJPEG 서버 중지", flush=True)


def open_video_source(src: str):
    """웹캠/파일/네트워크 소스 열기"""
    if src.startswith("webcam:"):
        cam_id = int(src.split(":", 1)[1])
        cap = cv2.VideoCapture(cam_id)
    else:
        cap = cv2.VideoCapture(src)
    if not cap.isOpened():
        print(f"[ERR] cannot open source: {src}", file=sys.stderr, flush=True)
        sys.exit(1)
    return cap


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default="webcam:0", help="webcam:0 / path/to/video.mp4 / rtsp://... 등")
    ap.add_argument("--model", default="yolov8s-pose.pt")
    ap.add_argument("--conf", type=float, default=0.25)
    ap.add_argument("--iou", type=float, default=0.6)
    ap.add_argument("--tracker", default="bytetrack.yaml")
    ap.add_argument("--device", default="", help="'', 'cpu', 'cuda:0' 등")

    # ▶ 파일/네트워크 소스를 실시간 '스트림처럼' 다루기 위한 옵션
    ap.add_argument("--realtime", action="store_true",
                    help="파일/네트워크 소스를 FPS 기준으로 페이싱하여 스트림처럼 처리")
    ap.add_argument("--max_delay", type=float, default=0.25,
                    help="지연 허용치(초). 이보다 늦은 프레임은 드롭")
    ap.add_argument("--fps_override", type=float, default=0.0,
                    help="0이 아니면 이 FPS로 페이싱(영상 메타의 FPS 대신 강제)")

    # ▶ 화면 표시 on/off
    ap.add_argument("--display", action="store_true", default=False,
                    help="켜면 스켈레톤과 바운딩박스를 윈도우에 표시 (q/ESC 종료)")

    # ▶ (신규) 프레임 POST 옵션
    ap.add_argument("--post_frame", action="store_true", default=True,
                    help="매 프레임을 JPEG로 POST (신뢰성 비보장, 큐 초과/지연 시 드롭)")
    ap.add_argument("--post_frame_url", default="http://localhost:8081/python-video/frame",
                    help="프레임 수신 URL")
    ap.add_argument("--post_frame_timeout", type=float, default=0.25,
                    help="POST 타임아웃(초)")
    ap.add_argument("--post_frame_qsize", type=int, default=5,
                    help="비동기 전송 큐 크기")
    ap.add_argument("--post_frame_quality", type=int, default=80,
                    help="POST용 JPEG 품질(1-100)")

    args = ap.parse_args()

    # 모델 로드
    model = YOLO(args.model)

    # 비디오 소스 열기
    cap = open_video_source(args.src)

    # 파일/네트워크 소스 여부
    is_file_like = not args.src.startswith("webcam:")

    # 페이싱 준비 (파일/네트워크 소스 + --realtime 일 때만 사용)
    fps = 0.0
    frame_interval = 0.0
    stream_start = 0.0
    if is_file_like and args.realtime:
        if args.fps_override and args.fps_override > 1e-3:
            fps = float(args.fps_override)
        else:
            fps = cap.get(cv2.CAP_PROP_FPS)
            if (not fps) or math.isnan(fps) or fps <= 1e-3:
                # 일부 코덱/컨테이너는 FPS를 못 주기도 하므로 안전한 기본값 사용
                fps = 30.0
        frame_interval = 1.0 / fps
        stream_start = time.perf_counter()

    # (신규) MJPEG 서버 시작
    mjpeg_server = MJPEGServer(port=8082)
    mjpeg_server.start()
    
    # (신규) 프레임 파이어-앤-포겟 전송기
    frame_pusher = None
    if args.post_frame and args.post_frame_url:
        frame_pusher = HttpFireAndForgetBytesSender(
            url=args.post_frame_url,
            timeout=args.post_frame_timeout,
            max_queue=args.post_frame_qsize,
            headers={"Content-Type": "image/jpeg"}
        )

    seq = 0

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            # ▶ 파일/네트워크 소스 실시간 페이싱 (웹캠은 스킵)
            if is_file_like and args.realtime:
                due = stream_start + seq * frame_interval  # 이 프레임이 "도착해야 할" 시간
                now = time.perf_counter()

                # 너무 늦게 도착한 프레임은 드롭해서 burst/꼬임 방지 (ByteTrack 안정화에 도움)
                if now - due > args.max_delay:
                    seq += 1
                    continue

                # 아직 이르면 대기하여 정시 간격 유지
                if now < due:
                    time.sleep(due - now)

            # YOLO 추론 (GPU/CPU 선택 가능)
            results = model.track(
                frame,
                persist=True,
                conf=args.conf,
                iou=args.iou,
                tracker=args.tracker,
                verbose=False,
                device=args.device if args.device else None
            )
            r = results[0]

            detections = []
            if (r.boxes is not None and hasattr(r.boxes, "id") and r.boxes.id is not None
                    and r.keypoints is not None and r.keypoints.xy is not None):

                num = len(r.boxes)
                for i in range(num):
                    # bbox
                    box_i = r.boxes.xyxy[i]
                    if box_i is None:
                        continue
                    box = box_i.detach().cpu().numpy().tolist()  # [x1, y1, x2, y2]
                    x1, y1, x2, y2 = [float(v) for v in box]

                    # track id / conf
                    if r.boxes.id is None:
                        continue
                    tid = int(r.boxes.id[i].detach().cpu().item())
                    conf_i = float(r.boxes.conf[i].detach().cpu().item()) if r.boxes.conf is not None else 0.0

                    # keypoints (N x 2) + (선택) conf (N,)
                    kps_xy = r.keypoints.xy[i].detach().cpu().numpy().tolist()
                    if hasattr(r.keypoints, "conf") and r.keypoints.conf is not None:
                        kps_cf = r.keypoints.conf[i].detach().cpu().numpy().tolist()
                    else:
                        kps_cf = [1.0] * len(kps_xy)

                    kps2d = []
                    for j in range(len(kps_xy)):
                        x, y = float(kps_xy[j][0]), float(kps_xy[j][1])
                        c = float(kps_cf[j]) if j < len(kps_cf) and kps_cf[j] is not None else 1.0
                        kps2d.append([x, y, c])

                    detections.append({
                        "track_id": tid,
                        "score": conf_i,
                        "bbox_xyxy": [x1, y1, x2, y2],
                        "kps2d": kps2d
                    })

            msg = {
                "type": "pose2d_frame",
                "seq": seq,
                "img_w": int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
                "img_h": int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
                "detections": detections
            }

            # NDJSON 한 줄 출력 + 즉시 flush
            print(json.dumps(msg, ensure_ascii=False), flush=True)

            # (신규) 현재 프레임을 JPEG로 인코딩해 MJPEG 서버와 POST 전송
            enc_ok, enc = cv2.imencode(".jpg", frame,
                                       [int(cv2.IMWRITE_JPEG_QUALITY), int(args.post_frame_quality)])
            if enc_ok:
                frame_bytes = enc.tobytes()
                
                # MJPEG 서버에 프레임 전송
                mjpeg_server.update_frame(frame_bytes)
                
                # Unity 서버에도 POST 전송 (기존 방식)
                if frame_pusher is not None:
                    frame_pusher.send(frame_bytes)

            # ▶ 화면 표시: YOLO가 그린 스켈레톤+박스 오버레이
            if args.display:
                annotated = r.plot()  # OpenCV용 BGR 배열
                cv2.imshow("YOLOv8-Pose Tracking", annotated)
                key = cv2.waitKey(1) & 0xFF
                if key == ord('q') or key == 27:  # 'q' 또는 ESC
                    break

            seq += 1

    except KeyboardInterrupt:
        pass
    finally:
        cap.release()
        if args.display:
            cv2.destroyAllWindows()
        if frame_pusher is not None:
            frame_pusher.close()
        mjpeg_server.stop()


if __name__ == "__main__":
    main()