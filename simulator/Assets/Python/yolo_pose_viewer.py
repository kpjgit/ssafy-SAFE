# Assets/Python/yolo_pose_viewer.py
from __future__ import annotations
import argparse, time, collections
from typing import List, Tuple, Optional, Dict
import cv2, numpy as np
from ultralytics import YOLO
from unified_stream import open_source, Frame
from vpose3d_wrapper import VideoPose3DLifter

# 스켈레톤/부모-페어 관계
SKELETON: List[Tuple[int, int]] = [
    (5, 6),(5, 7),(7, 9),(6, 8),(8,10),(11,12),(5,11),(6,12),(11,13),(13,15),(12,14),(14,16),(0,1),(0,2),(1,3),(2,4)
]
PARENTS = {7:5, 9:7, 8:6, 10:8, 13:11, 15:13, 14:12, 16:14}
PAIR_AVG = {7:(5,9), 8:(6,10), 11:(5,12), 12:(6,11), 13:(11,15), 14:(12,16)}
ROOT_JOINT_PAIR = (11,12)

def normalize_2d(points_xy: np.ndarray, w: int, h: int) -> np.ndarray:
    out = points_xy.astype(np.float32).copy()
    out[:, 0] = (out[:, 0] / max(1, w) - 0.5) * 2.0
    out[:, 1] = (out[:, 1] / max(1, h) - 0.5) * 2.0
    np.clip(out, -1.0, 1.0, out=out)
    return out

def in_bounds(pt, W, H): x, y = pt; return 0 <= x < W and 0 <= y < H

def impute_keypoints(pts_px, conf, W, H, prev_px, prev_cf, kp_conf_thr=0.2):
    out = pts_px.copy()
    if conf is None: conf = np.ones(pts_px.shape[0], dtype=np.float32)
    hip_mid = None
    if np.all(np.isfinite(pts_px[11])) and np.all(np.isfinite(pts_px[12])):
        hip_mid = (pts_px[11] + pts_px[12]) * 0.5
    elif prev_px is not None and np.all(np.isfinite(prev_px[11])) and np.all(np.isfinite(prev_px[12])):
        hip_mid = (prev_px[11] + prev_px[12]) * 0.5

    for j in range(pts_px.shape[0]):
        bad = (conf[j] < kp_conf_thr) or (not in_bounds(pts_px[j], W, H)) or (not np.all(np.isfinite(pts_px[j])))
        if not bad: continue
        if prev_px is not None and prev_cf is not None and prev_cf[j] >= kp_conf_thr and np.all(np.isfinite(prev_px[j])):
            out[j] = prev_px[j]; continue
        if j in PARENTS:
            p = PARENTS[j]
            if np.all(np.isfinite(pts_px[p])) and in_bounds(pts_px[p], W, H):
                out[j] = pts_px[p]; continue
        if j in PAIR_AVG:
            a, b = PAIR_AVG[j]
            if np.all(np.isfinite(pts_px[a])) and np.all(np.isfinite(pts_px[b])):
                out[j] = (pts_px[a] + pts_px[b]) * 0.5; continue
        out[j] = hip_mid if hip_mid is not None else np.array([W*0.5, H*0.5], dtype=np.float32)
    return out

def draw_detections(bgr, k_xy, k_conf, boxes_xyxy, scores, ids, pred3d_dict, kp_thr=0.2):
    n = boxes_xyxy.shape[0] if boxes_xyxy is not None else (k_xy.shape[0] if k_xy is not None else 0)
    for i in range(n):
        pid = int(ids[i]) if (ids is not None and i < len(ids) and ids[i] is not None) else -1
        sc  = float(scores[i]) if (scores is not None and i < len(scores)) else 0.0
        label = f"id:{pid if pid>=0 else '-'} {sc:.2f}"

        if boxes_xyxy is not None and i < len(boxes_xyxy):
            x1, y1, x2, y2 = boxes_xyxy[i].astype(int).tolist()
            cv2.rectangle(bgr, (x1, y1), (x2, y2), (0,185,255), 2)
            cv2.putText(bgr, label, (x1, max(0, y1-8)), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0,185,255), 1, cv2.LINE_AA)

        if k_xy is None or i >= len(k_xy): continue
        kps = k_xy[i]; kcs = k_conf[i] if (k_conf is not None and i < len(k_conf)) else np.ones(len(kps))
        for (a,b) in SKELETON:
            if a < len(kps) and b < len(kps) and kcs[a] >= kp_thr and kcs[b] >= kp_thr:
                cv2.line(bgr, (int(kps[a][0]), int(kps[a][1])), (int(kps[b][0]), int(kps[b][1])), (255,128,0), 2)
        pred3d = pred3d_dict.get(pid, None)
        for j, (x, y) in enumerate(kps):
            if kcs[j] >= kp_thr:
                cv2.circle(bgr, (int(x), int(y)), 3, (0,255,0), -1)
                if pred3d is not None:
                    z_cm = pred3d[j, 2]  # mm → cm
                    cv2.putText(bgr, f"z:{z_cm:+.3f}cm", (int(x), int(y)-8),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.4, (20,220,255), 1, cv2.LINE_AA)

def main():
    ap = argparse.ArgumentParser(description="YOLOv8-pose + Tracker + VideoPose3D (COCO-17)")
    ap.add_argument("--src", required=True, help="file:./a.mp4 | webcam:0 | rtsp://... | http(s)://... | path")
    ap.add_argument("--model", default="yolov8s-pose.pt")
    ap.add_argument("--tracker", default="bytetrack.yaml")
    ap.add_argument("--conf", type=float, default=0.25)
    ap.add_argument("--kpconf", type=float, default=0.2)
    ap.add_argument("--show-fps", action="store_true")
    ap.add_argument("--vpose3d_ts", type=str, default="Assets/Python/models/vpose3d/videopose3d_ts.pt")
    ap.add_argument("--seq-len", type=int, default=243)
    args = ap.parse_args()

    lifter = VideoPose3DLifter(args.vpose3d_ts, seq_len=args.seq_len)

    # track별 버퍼/최근 px/conf 저장
    pose2d_buf: Dict[int, "collections.deque[np.ndarray]"] = {}
    last_px: Dict[int, np.ndarray] = {}
    last_cf: Dict[int, np.ndarray] = {}

    with open_source(args.src, convert_to_rgb=True) as src:
        model = YOLO(args.model)
        win = "YOLOv8-Pose + VideoPose3D"
        cv2.namedWindow(win, cv2.WINDOW_NORMAL)

        last_t, frame_count, fps = time.time(), 0, 0.0
        pred3d_latest: Dict[int, np.ndarray] = {}

        for fr in src:
            frame_count += 1
            H, W = fr.data.shape[:2]
            res = model.track(fr.data, verbose=False, conf=args.conf, persist=True, tracker=args.tracker)[0]

            boxes_xyxy = res.boxes.xyxy.cpu().numpy() if res.boxes is not None else None
            scores     = res.boxes.conf.cpu().numpy() if res.boxes is not None else None
            ids        = res.boxes.id.cpu().numpy() if (res.boxes is not None and hasattr(res.boxes, "id") and res.boxes.id is not None) else None
            k_xy       = res.keypoints.xy.cpu().numpy() if res.keypoints is not None else None
            k_conf     = res.keypoints.conf.cpu().numpy() if (res.keypoints is not None and hasattr(res.keypoints, "conf")) else None

            if k_xy is not None and ids is not None:
                for i in range(len(k_xy)):
                    pid = int(ids[i]) if ids[i] is not None else -1
                    if pid < 0: continue
                    px_raw = k_xy[i]
                    cf_raw = k_conf[i] if k_conf is not None else None

                    prev_px = last_px.get(pid); prev_cf = last_cf.get(pid)
                    px_filled = impute_keypoints(px_raw, cf_raw, W, H, prev_px, prev_cf, kp_conf_thr=args.kpconf)
                    last_px[pid] = px_filled.copy()
                    last_cf[pid] = cf_raw.copy() if cf_raw is not None else np.ones(px_filled.shape[0], dtype=np.float32)

                    norm = normalize_2d(px_filled, W, H)  # [-1,1]
                    if pid not in pose2d_buf:
                        pose2d_buf[pid] = collections.deque(maxlen=args.seq_len)
                    pose2d_buf[pid].append(norm)

                    if lifter.ok and len(pose2d_buf[pid]) > 0:
                        seq = np.stack(list(pose2d_buf[pid]), axis=0)  # (T,17,2), T<=seq_len
                        pred3d = lifter.lift(seq)
                        if pred3d is not None:
                            pred3d_latest[pid] = pred3d  # (17,3) mm, root-relative

            bgr = cv2.cvtColor(fr.data, cv2.COLOR_RGB2BGR)
            draw_detections(bgr, k_xy, k_conf, boxes_xyxy, scores, ids, pred3d_latest, kp_thr=args.kpconf)

            if args.show_fps:
                now = time.time(); dt = now - last_t
                if dt >= 0.5: fps = frame_count / dt; frame_count = 0; last_t = now
                cv2.putText(bgr, f"FPS:{fps:.1f}", (8,22), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (50,220,50), 2, cv2.LINE_AA)

            cv2.imshow(win, bgr)
            if (cv2.waitKey(1) & 0xFF) in (27, ord('q')): break

        cv2.destroyAllWindows()

if __name__ == "__main__":
    main()
