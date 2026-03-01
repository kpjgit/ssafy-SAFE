# sender.py
# 사용:  python -u sender.py            (-u: 버퍼링 끔)
# 또는:  python sender.py               (내부 flush로 즉시 전송)

import json, sys, time, os, random

def send(msg: dict):
    sys.stdout.write(json.dumps(msg, ensure_ascii=False) + "\n")
    sys.stdout.flush()  # 중요: 즉시 부모로 전달

def main():
    parent = os.getppid()
    i = 0
    while True:
        i += 1
        payload = {
            "type": "heartbeat",
            "seq": i,
            "parent_pid": parent,
            "timestamp": time.time(),
            # TODO: 여기 자리에 YOLO/VideoPose3D 결과 구조(JSON)를 그대로 넣으면 됩니다.
            "test": f"hello from python #{i}",
            "rand": round(random.random(), 3),
        }
        send(payload)
        time.sleep(1.0)

if __name__ == "__main__":
    main()
