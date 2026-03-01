import { useEffect, useRef, useState } from "react";

interface VideoStreamProps {
  streamUrl?: string;
  src?: string; // 비디오 파일 경로
  aspect?: number;
  className?: string;
  fallbackText?: string;
  streamType?: "file" | "url" | "python"; // 스트림 타입 추가
  pythonPort?: number; // Python 스트림 포트
}

export default function VideoStream({
  streamUrl,
  src,
  aspect = 16 / 9,
  className = "",
  fallbackText = "비디오 스트림 연결 중...",
  streamType = "url",
  pythonPort = 8082
}: VideoStreamProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const pythonImageRef = useRef<HTMLImageElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [connectionStatus, setConnectionStatus] = useState<"connecting" | "connected" | "disconnected" | "error">("disconnected");
  const [fps, setFps] = useState(0);
  const [lastFpsUpdate, setLastFpsUpdate] = useState(Date.now());
  const intervalRef = useRef<number | null>(null);

  // Python 스트림 처리 (MJPEG 이미지 스트림 + 포즈 오버레이)
  const handlePythonStream = async () => {
    const image = pythonImageRef.current;
    const canvas = canvasRef.current;
    if (!image || !canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    try {
      console.log("🔍 Python MJPEG 이미지 스트림 연결 시작...");
      
      const handleImageLoad = () => {
        console.log("🎬 Python MJPEG 이미지 로드!");
        setConnectionStatus("connected");
        setIsLoading(false);
        setError(null);
        
        // FPS 업데이트
        const now = Date.now();
        setFps(30);
        setLastFpsUpdate(now);
      };

      const handleImageError = (event: Event) => {
        console.warn("❌ Python MJPEG 이미지 에러:", event);
        // 이미지 로드 실패해도 다음 프레임으로 자연 복구되도록 유지
      };

      // MJPEG는 <img src=".../stream.mjpg"> 방식으로만 안정 동작
      const img = pythonImageRef.current;
      if (img) {
        img.addEventListener("load", handleImageLoad);
        img.addEventListener("error", handleImageError);
        img.src = "http://localhost:8082/stream.mjpg";

        // cleanup 함수
        const originalCleanup = () => {
          console.log("🧹 Python 스트림 정리 중...");
          if (img) {
            img.removeEventListener("load", handleImageLoad);
            img.removeEventListener("error", handleImageError);
          }
          if (intervalRef.current) {
            clearInterval(intervalRef.current);
          }
        };
        return originalCleanup;
      }

      // 포즈 데이터를 주기적으로 요청하고 오버레이 그리기
      const requestPoseData = async () => {
        try {
          console.log("🔍 포즈 데이터 요청 중...");
          const response = await fetch(`http://localhost:8081/python-video/data`, {
            method: 'GET',
            cache: 'no-cache',
            signal: AbortSignal.timeout(3000)
          });
          
          console.log("📡 응답 상태:", response.status);
          console.log("📋 Content-Type:", response.headers.get('content-type'));
          
          if (!response.ok) {
            if (response.status === 204) {
              console.log("ℹ️ 데이터 없음 (204)");
              return; // 데이터 없음
            }
            throw new Error(`HTTP ${response.status}`);
          }
          
          // Content-Type 확인
          const contentType = response.headers.get('content-type');
          if (!contentType || !contentType.includes('application/json')) {
            console.log("⚠️ JSON이 아닌 응답:", contentType);
            return;
          }
          
          const text = await response.text();
          console.log("📝 응답 텍스트 길이:", text.length);
          console.log("📝 응답 텍스트 시작:", text.substring(0, 100));
          
          if (!text.trim()) {
            console.log("⚠️ 빈 응답");
            return;
          }
          
          const jsonData = JSON.parse(text);
          console.log("✅ JSON 파싱 성공:", jsonData.type);
          
          if (jsonData && jsonData.type === "pose2d_frame" && jsonData.detections) {
            // 캔버스 크기 설정
            canvas.width = jsonData.img_w || 1280;
            canvas.height = jsonData.img_h || 720;
            
            // 캔버스 초기화
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            
            // 포즈 데이터 오버레이 그리기
            ctx.strokeStyle = "#00ff00";
            ctx.fillStyle = "#00ff00";
            ctx.lineWidth = 2;
            ctx.font = "12px Arial";
            
            jsonData.detections.forEach((detection: any) => {
              // 바운딩 박스 그리기
              const [x1, y1, x2, y2] = detection.bbox_xyxy;
              ctx.strokeRect(x1, y1, x2 - x1, y2 - y1);
              
              // 트랙 ID 표시
              ctx.fillText(`ID: ${detection.track_id}`, x1, y1 - 5);
              
              // 키포인트 그리기
              if (detection.kps2d && detection.kps2d.length > 0) {
                detection.kps2d.forEach((kp: any) => {
                  if (kp[2] > 0.5) { // 신뢰도가 0.5 이상인 경우만
                    ctx.beginPath();
                    ctx.arc(kp[0], kp[1], 3, 0, 2 * Math.PI);
                    ctx.fill();
                  }
                });
              }
            });
            
            // FPS 계산 (이미지 갱신 속도)
            const now = Date.now();
            if (now - lastFpsUpdate >= 1000) {
              setFps(120); // 이미지 갱신 주기 (16ms = 60fps)
              setLastFpsUpdate(now);
            }
          }
        } catch (error) {
          if (error instanceof Error && error.name !== 'AbortError') {
            console.warn("❌ 포즈 데이터 요청 실패:", error.message);
            console.warn("🔍 에러 상세:", error);
          }
        }
      };

      // 포즈 데이터를 주기적으로 요청 (5fps)
      intervalRef.current = setInterval(requestPoseData, 200);

      setConnectionStatus("connecting");

      // cleanup 함수는 이미 위에서 정의됨
      return () => {
        console.log("🧹 Python 스트림 정리 중...");
        if (intervalRef.current) {
          clearInterval(intervalRef.current);
        }
      };
      
    } catch (error) {
      console.error("❌ Python 스트림 연결 실패:", error);
      setConnectionStatus("error");
      
      let errorMessage = "Python 스트림 연결에 실패했습니다";
      
      if (error instanceof Error) {
        console.log("🔍 에러 타입:", error.name);
        console.log("📝 에러 메시지:", error.message);
        
        if (error.message.includes("ERR_CONNECTION_REFUSED")) {
          errorMessage = "Unity 서버에 연결할 수 없습니다. Unity가 실행 중인지 확인해주세요.";
        } else if (error.message.includes("ERR_NETWORK")) {
          errorMessage = "네트워크 연결에 문제가 있습니다.";
        } else {
          errorMessage = error.message || errorMessage;
        }
      }
      
      console.log("💬 사용자에게 표시할 에러 메시지:", errorMessage);
      setError(errorMessage);
    }
  };

  // 일반 비디오 스트림 처리
  const handleVideoStream = () => {
    const video = videoRef.current;
    if (!video) return;

    const handleLoadStart = () => {
      setIsLoading(true);
      setError(null);
      setConnectionStatus("connecting");
    };

    const handleLoadedData = () => {
      setIsLoading(false);
      setError(null);
      setConnectionStatus("connected");
    };

    const handleError = () => {
      setIsLoading(false);
      setError("비디오 스트림을 불러올 수 없습니다.");
      setConnectionStatus("error");
    };

    video.addEventListener("loadstart", handleLoadStart);
    video.addEventListener("loadeddata", handleLoadedData);
    video.addEventListener("error", handleError);

    // 비디오 소스 설정
    const videoSrc = src || streamUrl;
    if (videoSrc) {
      video.src = videoSrc;
    video.load();
    }

    return () => {
      video.removeEventListener("loadstart", handleLoadStart);
      video.removeEventListener("loadeddata", handleLoadedData);
      video.removeEventListener("error", handleError);
    };
  };

  useEffect(() => {
    let cleanup: (() => void) | undefined;
    
    if (streamType === "python") {
      handlePythonStream().then(cleanupFn => {
        cleanup = cleanupFn;
      });
    } else {
      cleanup = handleVideoStream();
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
      if (cleanup) {
        cleanup();
      }
      // 추가 안전장치: 컴포넌트 언마운트 시 정리
    };
  }, [streamUrl, src, streamType, pythonPort, lastFpsUpdate]);

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
      {/* Python 스트림의 경우 이미지(MJPEG) + 캔버스 오버레이 */}
      {streamType === "python" ? (
        <>
          <img
            ref={pythonImageRef}
            className="w-full h-full object-cover"
            alt="Python MJPEG"
          />
          <canvas
            ref={canvasRef}
            className="absolute inset-0 w-full h-full object-cover pointer-events-none"
            style={{ imageRendering: "pixelated" }}
          />
        </>
      ) : (
      <video
        ref={videoRef}
        className="w-full h-full object-cover"
        autoPlay
        muted
        playsInline
        controls={false}
      />
      )}
      
      {/* 연결 상태 및 FPS 표시 */}
      <div className="absolute top-2 left-2 flex items-center gap-2">
        <div className={`w-2 h-2 rounded-full ${
          connectionStatus === "connected" ? "bg-green-400" :
          connectionStatus === "connecting" ? "bg-yellow-400 animate-pulse" :
          connectionStatus === "error" ? "bg-red-400" : "bg-gray-400"
        }`} />
        <span className={`text-xs ${getStatusColor()}`}>
          {streamType === "python" ? "Python" : "Video"} {getStatusText()}
        </span>
        {connectionStatus === "connected" && streamType === "python" && (
          <span className="text-xs text-green-400">
            {fps} FPS
          </span>
        )}
      </div>
      
      {/* 로딩 오버레이 */}
      {isLoading && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/50">
          <div className="text-white text-sm">
            <div className="animate-spin w-6 h-6 border-2 border-white border-t-transparent rounded-full mx-auto mb-2"></div>
            {fallbackText}
          </div>
        </div>
      )}

      {/* 에러 오버레이 */}
      {error && (
        <div className="absolute inset-0 flex items-center justify-center bg-red-900/50">
          <div className="text-white text-sm text-center max-w-md px-4">
            <div className="text-red-400 mb-2">⚠️</div>
            <div className="mb-3">{error}</div>
            {streamType === "python" && (
              <button
                onClick={() => {
                  setError(null);
                  setConnectionStatus("connecting");
                  handlePythonStream();
                }}
                className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white rounded text-sm font-medium"
              >
                재시도
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
