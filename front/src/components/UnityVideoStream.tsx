import { useEffect, useRef, useState } from "react";

interface UnityVideoStreamProps {
  streamUrl?: string;
  aspect?: number;
  className?: string;
  autoPlay?: boolean;
}

export default function UnityVideoStream({
  streamUrl = "ws://localhost:8080/unity-video",
  aspect = 16 / 9,
  className = "",
  autoPlay = true
}: UnityVideoStreamProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<"connecting" | "connected" | "disconnected" | "error">("disconnected");
  const [fps, setFps] = useState(0);
  const [frameCount, setFrameCount] = useState(0);
  const [lastFpsUpdate, setLastFpsUpdate] = useState(Date.now());

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    // WebSocket 연결
    const connectWebSocket = () => {
      try {
        console.log("WebSocket 연결 시도:", streamUrl);
        wsRef.current = new WebSocket(streamUrl);
        setConnectionStatus("connecting");

        wsRef.current.onopen = () => {
          setConnectionStatus("connected");
          console.log("Unity 비디오 스트림 연결됨");
        };

        wsRef.current.onmessage = (event) => {
          try {
            // Unity에서 전송된 프레임 데이터 처리
            if (event.data instanceof Blob) {
              // 이미지 데이터로 처리
              const img = new Image();
              img.onload = () => {
                canvas.width = img.width;
                canvas.height = img.height;
                ctx.drawImage(img, 0, 0);
                
                // FPS 계산
                setFrameCount(prev => {
                  const newCount = prev + 1;
                  const now = Date.now();
                  if (now - lastFpsUpdate >= 1000) {
                    setFps(newCount);
                    setLastFpsUpdate(now);
                    return 0;
                  }
                  return newCount;
                });
              };
              img.src = URL.createObjectURL(event.data);
            } else {
              // JSON 데이터로 처리 (프레임 정보)
              const data = JSON.parse(event.data);
              if (data.type === "frame") {
                // Base64 이미지 데이터 처리
                const img = new Image();
                img.onload = () => {
                  canvas.width = img.width;
                  canvas.height = img.height;
                  ctx.drawImage(img, 0, 0);
                };
                img.src = `data:image/jpeg;base64,${data.imageData}`;
              }
            }
          } catch (error) {
            console.error("프레임 데이터 처리 오류:", error);
          }
        };

        wsRef.current.onclose = () => {
          setConnectionStatus("disconnected");
          console.log("Unity 비디오 스트림 연결 끊김");
          
          // 자동 재연결
          setTimeout(connectWebSocket, 3000);
        };

        wsRef.current.onerror = (error) => {
          setConnectionStatus("error");
          console.warn("Unity 비디오 스트림 연결 실패:", error);
          console.warn("연결 URL:", streamUrl);
          console.warn("WebSocket 상태:", wsRef.current?.readyState);
        };

      } catch (error) {
        setConnectionStatus("error");
        console.error("WebSocket 연결 오류:", error);
      }
    };

    connectWebSocket();

    return () => {
      if (wsRef.current) {
        wsRef.current.close();
      }
    };
  }, [streamUrl, lastFpsUpdate]);

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
      <canvas
        ref={canvasRef}
        className="w-full h-full object-cover"
        style={{ imageRendering: "pixelated" }}
      />
      
      {/* 연결 상태 및 FPS 표시 */}
      <div className="absolute top-2 left-2 flex items-center gap-2">
        <div className={`w-2 h-2 rounded-full ${
          connectionStatus === "connected" ? "bg-green-400" :
          connectionStatus === "connecting" ? "bg-yellow-400 animate-pulse" :
          connectionStatus === "error" ? "bg-red-400" : "bg-gray-400"
        }`} />
        <span className={`text-xs ${getStatusColor()}`}>
          Unity {getStatusText()}
        </span>
        {connectionStatus === "connected" && (
          <span className="text-xs text-green-400">
            {fps} FPS
          </span>
        )}
      </div>

      {/* 연결 오류 시 재연결 버튼 */}
      {connectionStatus === "error" && (
        <div className="absolute inset-0 flex items-center justify-center bg-red-900/50">
          <button
            onClick={() => window.location.reload()}
            className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white rounded-md text-sm"
          >
            재연결
          </button>
        </div>
      )}

      {/* 로딩 오버레이 */}
      {connectionStatus === "connecting" && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/50">
          <div className="text-white text-sm">
            <div className="animate-spin w-6 h-6 border-2 border-white border-t-transparent rounded-full mx-auto mb-2"></div>
            Unity 스트림 연결 중...
          </div>
        </div>
      )}
    </div>
  );
}
