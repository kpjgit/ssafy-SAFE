# unified_stream.py
# 표준화된 비디오 프레임 소스 엔트리포인트 (파일/웹캠/네트워크 공통)
# - Python >=3.9
# - pip install opencv-python-headless (또는 opencv-python)

from __future__ import annotations
import argparse
import queue
import threading
import time
from dataclasses import dataclass
from typing import Generator, Optional, Tuple, Dict, Any, Union

import cv2
import numpy as np


@dataclass(frozen=True)
class Frame:
    """통일된 프레임 컨테이너"""
    data: np.ndarray            # H x W x C (BGR 기본, 옵션으로 RGB)
    ts_us: int                  # 마이크로초 단위 타임스탬프 (수신 시각)
    idx: int                    # 프레임 인덱스(소스 내에서 증가)
    source_meta: Dict[str, Any] # 예: {"uri": "...", "size": (w,h), "fps": 30.0, "bgr": True}


class FrameSource:
    """모든 소스를 공통으로 다루는 베이스 클래스 (이터러블)"""
    def __iter__(self) -> Generator[Frame, None, None]:
        raise NotImplementedError

    def close(self) -> None:
        raise NotImplementedError

    # with 컨텍스트 지원
    def __enter__(self) -> "FrameSource":
        return self

    def __exit__(self, exc_type, exc, tb):
        self.close()


class OpenCVSource(FrameSource):
    """OpenCV VideoCapture 기반 표준 소스 (파일/웹캠/네트워크 공통)
    - 백그라운드 스레드에서 프레임 수신
    - 최신 프레임 위주 저지연 처리(큐 길이 제한; 가득 차면 오래된 것 드롭)
    """

    def __init__(
        self,
        uri: str,
        *,
        convert_to_rgb: bool = False,
        resize: Optional[Tuple[int, int]] = None,  # (width, height)
        queue_len: int = 4,
        camera_index: Optional[int] = None,
        cv_open_flags: Optional[int] = None,
        props: Optional[Dict[int, Union[float, int]]] = None,  # cv2.CAP_PROP_* 설정
        poll_sleep_s: float = 0.001,
    ):
        """
        Args:
            uri: "file:/path.mp4", "webcam:0", "rtsp://...", "http(s)://..." 등
                 파일 경로는 그냥 "video.mp4" 로도 허용.
            convert_to_rgb: True면 BGR->RGB 변환
            resize: (w, h) 로 리사이즈
            queue_len: 내부 프레임 큐 길이(저지연 권장: 2~8)
            camera_index: "webcam:" 접두어 대신 직접 인덱스 지정 가능
            cv_open_flags: cv2.VideoCapture 플래그 (기본 None)
            props: VideoCapture 프로퍼티 사전 (노출/프레임레이트 등)
            poll_sleep_s: 캡처 실패/대기 시 짧게 sleep
        """
        self._uri = uri
        self._convert_to_rgb = convert_to_rgb
        self._resize = resize
        self._q: "queue.Queue[Frame]" = queue.Queue(maxsize=max(1, queue_len))
        self._stop = threading.Event()
        self._poll_sleep_s = poll_sleep_s
        self._idx = 0

        # 소스 열기
        self._cap = self._open_capture(uri, camera_index, cv_open_flags)
        if props:
            for k, v in props.items():
                self._cap.set(k, v)

        # 메타 수집
        w = int(self._cap.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
        h = int(self._cap.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)
        fps = float(self._cap.get(cv2.CAP_PROP_FPS) or 0.0)
        self._meta = {
            "uri": uri,
            "size": (w, h),
            "fps": fps,
            "bgr": not convert_to_rgb,
        }

        # 백그라운드 수신 스레드 시작
        self._t = threading.Thread(target=self._rx_loop, name="OpenCVSourceRx", daemon=True)
        self._t.start()

    def _open_capture(
        self, uri: str, camera_index: Optional[int], flags: Optional[int]
    ) -> cv2.VideoCapture:
        # webcam:<index> 또는 camera_index 직접 지정
        if camera_index is not None:
            cap = cv2.VideoCapture(camera_index, flags if flags is not None else cv2.CAP_ANY)
            return cap

        if uri.startswith("webcam:"):
            idx_str = uri.split("webcam:", 1)[1]
            idx = int(idx_str.strip() or "0")
            cap = cv2.VideoCapture(idx, flags if flags is not None else cv2.CAP_ANY)
            return cap

        # file:/… → 경로만 추출
        if uri.startswith("file:"):
            path = uri[5:]
            return cv2.VideoCapture(path, flags if flags is not None else cv2.CAP_ANY)

        # 그 외(rtsp/http/https 또는 그냥 경로)
        return cv2.VideoCapture(uri, flags if flags is not None else cv2.CAP_ANY)

    def _rx_loop(self):
        while not self._stop.is_set():
            ok, frame = self._cap.read()
            if not ok:
                time.sleep(self._poll_sleep_s)
                continue

            # 옵션 처리(BGR->RGB, 리사이즈)
            if self._resize is not None:
                frame = cv2.resize(frame, self._resize, interpolation=cv2.INTER_AREA)
            if self._convert_to_rgb:
                frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

            self._idx += 1
            f = Frame(
                data=frame,
                ts_us=int(time.time() * 1e6),
                idx=self._idx,
                source_meta=self._meta,
            )

            # 저지연: 큐가 가득이면 오래된 프레임 하나 버리고 새 프레임 넣기
            while True:
                try:
                    self._q.put(f, timeout=0.001)
                    break
                except queue.Full:
                    try:
                        _ = self._q.get_nowait()
                    except queue.Empty:
                        pass

        # 종료 시 리소스 정리
        self._cap.release()

    def __iter__(self) -> Generator[Frame, None, None]:
        while not self._stop.is_set():
            try:
                f = self._q.get(timeout=0.1)
                yield f
            except queue.Empty:
                continue

    def close(self) -> None:
        self._stop.set()
        if self._t.is_alive():
            self._t.join(timeout=1.0)
        # VideoCapture는 rx_loop에서 release 호출

    @property
    def meta(self) -> Dict[str, Any]:
        return dict(self._meta)


# --------- 공용 팩토리 ---------
def open_source(
    uri: str,
    *,
    convert_to_rgb: bool = False,
    resize: Optional[Tuple[int, int]] = None,
    queue_len: int = 4,
    camera_index: Optional[int] = None,
    cv_open_flags: Optional[int] = None,
    props: Optional[Dict[int, Union[float, int]]] = None,
    poll_sleep_s: float = 0.001,
) -> FrameSource:
    """단일 엔트리 포인트: 어떤 URI든 FrameSource 반환"""
    return OpenCVSource(
        uri,
        convert_to_rgb=convert_to_rgb,
        resize=resize,
        queue_len=queue_len,
        camera_index=camera_index,
        cv_open_flags=cv_open_flags,
        props=props,
        poll_sleep_s=poll_sleep_s,
    )


# --------- CLI 디버깅 ---------
def _parse_resize(s: str) -> Tuple[int, int]:
    # "640x360" → (640, 360)
    w, h = s.lower().split("x")
    return (int(w), int(h))

def main():
    ap = argparse.ArgumentParser(description="Unified video frame source (file/webcam/rtsp/http)")
    ap.add_argument("uri", help="file:/path.mp4 | webcam:0 | rtsp://... | http(s)://... | plain path")
    ap.add_argument("--rgb", action="store_true", help="Convert BGR->RGB")
    ap.add_argument("--resize", type=_parse_resize, help="Resize e.g., 640x360")
    ap.add_argument("--limit", type=int, default=0, help="Stop after N frames (0=run forever)")
    ap.add_argument("--print-every", type=int, default=30, help="Print FPS every N frames")
    args = ap.parse_args()

    with open_source(args.uri, convert_to_rgb=args.rgb, resize=args.resize) as src:
        cnt = 0
        t0 = time.time()
        for f in src:
            cnt += 1

            # ------- 프레임 출력 부분 추가 -------
            # RGB 모드라면 다시 BGR로 변환 후 표시
            frame_to_show = f.data
            if args.rgb:
                frame_to_show = cv2.cvtColor(frame_to_show, cv2.COLOR_RGB2BGR)

            cv2.imshow("UnifiedStream", frame_to_show)
            # q를 누르면 종료
            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
            # -----------------------------------

            if args.print_every and cnt % args.print_every == 0:
                elapsed = time.time() - t0
                fps = cnt / max(1e-6, elapsed)
                meta = src.meta
                print(f"[{cnt} frames] ~{fps:.1f} FPS | "
                      f"size={f.data.shape[1]}x{f.data.shape[0]} | "
                      f"uri={meta.get('uri')}")

            if args.limit and cnt >= args.limit:
                break

    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()
