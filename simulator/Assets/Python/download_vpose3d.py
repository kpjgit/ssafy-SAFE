# download_vpose3d.py
# VideoPose3D의 사전 학습된 checkpoint를 자동 다운로드

import os
import urllib.request

def download_pretrained_vpose3d(out_dir="models"):
    os.makedirs(out_dir, exist_ok=True)
    url = "https://github.com/facebookresearch/VideoPose3D/releases/download/v1.0/checkpoint.pth.tar"
    out_path = os.path.join(out_dir, "videopose3d_h36m.pth.tar")

    if os.path.isfile(out_path):
        print(f"[VideoPose3D] Pretrained model already exists: {out_path}")
        return out_path

    print(f"[VideoPose3D] Downloading pretrained model from {url} ...")
    urllib.request.urlretrieve(url, out_path)
    print(f"[VideoPose3D] Saved to {out_path}")
    return out_path

if __name__ == "__main__":
    download_pretrained_vpose3d()
