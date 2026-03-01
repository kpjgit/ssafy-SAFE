import { useEffect, useRef, useState } from "react";

interface RealtimeVideoStreamProps {
  streamUrl?: string;
  aspect?: number;
  className?: string;
  connectionType?: "webrtc" | "websocket" | "hls" | "rtmp";
}

export default function RealtimeVideoStream({
  streamUrl,
  aspect = 16 / 9,
  className = "",
  connectionType = "webrtc"
}: RealtimeVideoStreamProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [connectionStatus, setConnectionStatus] = useState<"connecting" | "connected" | "disconnected" | "error">("disconnected");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !streamUrl) return;

    const connectStream = async () => {
      try {
        setConnectionStatus("connecting");
        setError(null);

        switch (connectionType) {
          case "webrtc":
            await connectWebRTC(video, streamUrl);
            break;
          case "websocket":
            await connectWebSocket(video, streamUrl);
            break;
          case "hls":
            await connectHLS(video, streamUrl);
            break;
          case "rtmp":
            await connectRTMP(video, streamUrl);
            break;
          default:
            throw new Error(`지원하지 않는 연결 타입: ${connectionType}`);
        }

        setConnectionStatus("connected");
      } catch (err) {
        setConnectionStatus("error");
        setError(err instanceof Error ? err.message : "연결 실패");
      }
    };

    connectStream();

    return () => {
      // 정리 작업
      if (video.srcObject) {
        const stream = video.srcObject as MediaStream;
        stream.getTracks().forEach(track => track.stop());
        video.srcObject = null;
      }
    };
  }, [streamUrl, connectionType]);

  const connectWebRTC = async (video: HTMLVideoElement, url: string) => {
    // WebRTC 연결 로직
    const pc = new RTCPeerConnection({
      iceServers: [{ urls: "stun:stun.l.google.com:19302" }]
    });

    pc.ontrack = (event) => {
      video.srcObject = event.streams[0];
    };

    // 실제 구현에서는 시그널링 서버와 통신
    // 여기서는 예시로 직접 스트림 설정
    const stream = await navigator.mediaDevices.getUserMedia({ video: true });
    video.srcObject = stream;
  };

  const connectWebSocket = async (video: HTMLVideoElement, url: string) => {
    // WebSocket을 통한 비디오 스트림
    const ws = new WebSocket(url);
    
    ws.onopen = () => {
      console.log("WebSocket 연결됨");
    };

    ws.onmessage = (event) => {
      // 비디오 프레임 데이터 처리
      const blob = new Blob([event.data], { type: "video/webm" });
      const url = URL.createObjectURL(blob);
      video.src = url;
    };

    ws.onerror = () => {
      throw new Error("WebSocket 연결 오류");
    };
  };

  const connectHLS = async (video: HTMLVideoElement, url: string) => {
    // HLS 스트림 연결
    if (video.canPlayType("application/vnd.apple.mpegurl")) {
      video.src = url;
    } else {
      // HLS.js 라이브러리 사용 필요
      throw new Error("HLS 스트림을 지원하지 않는 브라우저입니다.");
    }
  };

  const connectRTMP = async (video: HTMLVideoElement, url: string) => {
    // RTMP는 브라우저에서 직접 지원하지 않으므로
    // 서버에서 WebRTC나 HLS로 변환 필요
    throw new Error("RTMP는 브라우저에서 직접 지원하지 않습니다.");
  };

  const getStatusColor = () => {
    switch (connectionStatus) {
      case "connecting": return "text-yellow-400";
      case "connected": return "text-green-400";
      case "disconnected": return "text-gray-400";
      case "error": return "text-red-400";
    }
  };

  const getStatusText = () => {
    switch (connectionStatus) {
      case "connecting": return "연결 중...";
      case "connected": return "연결됨";
      case "disconnected": return "연결 끊김";
      case "error": return "연결 오류";
    }
  };

  return (
    <div
      className={`relative bg-black rounded-lg overflow-hidden ${className}`}
      style={{ aspectRatio: aspect }}
    >
      <video
        ref={videoRef}
        className="w-full h-full object-cover"
        autoPlay
        muted
        playsInline
        controls={false}
      />
      
      {/* 연결 상태 표시 */}
      <div className="absolute top-2 right-2 flex items-center gap-2">
        <div className={`w-2 h-2 rounded-full ${
          connectionStatus === "connected" ? "bg-green-400" :
          connectionStatus === "connecting" ? "bg-yellow-400 animate-pulse" :
          connectionStatus === "error" ? "bg-red-400" : "bg-gray-400"
        }`} />
        <span className={`text-xs ${getStatusColor()}`}>
          {getStatusText()}
        </span>
      </div>

      {/* 에러 메시지 */}
      {error && (
        <div className="absolute inset-0 flex items-center justify-center bg-red-900/50">
          <div className="text-white text-sm text-center">
            <div className="text-red-400 mb-2">⚠️</div>
            {error}
          </div>
        </div>
      )}
    </div>
  );
}
