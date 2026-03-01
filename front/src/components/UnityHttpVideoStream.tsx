import { useEffect, useRef, useState } from "react";

interface UnityHttpVideoStreamProps {
  streamUrl?: string;
  className?: string;
  autoPlay?: boolean;
  showCameraControls?: boolean;
}

export default function UnityHttpVideoStream({
  streamUrl = "http://localhost:8081/unity-video",
  className = "",
  autoPlay = true,
  showCameraControls = true
}: UnityHttpVideoStreamProps) {
  const imageRef = useRef<HTMLImageElement>(null);
  const [connectionStatus, setConnectionStatus] = useState<"connecting" | "connected" | "disconnected" | "error">("disconnected");
  const [fps, setFps] = useState(0);
  const [lastFpsUpdate, setLastFpsUpdate] = useState(Date.now());
  const [availableCameras, setAvailableCameras] = useState<Array<{index: number, name: string, isActive: boolean}>>([]);
  const [currentCameraIndex, setCurrentCameraIndex] = useState(0);
  const [isSwitchingCamera, setIsSwitchingCamera] = useState(false);

  // 카메라 목록 가져오기
  const fetchCameraList = async () => {
    try {
      const response = await fetch(`${streamUrl}/camera/list`);
      if (response.ok) {
        const data = await response.json();
        if (data.success && data.cameras) {
          setAvailableCameras(data.cameras);
          setCurrentCameraIndex(data.currentCameraIndex || 0);
        }
      }
    } catch (error) {
      console.warn("카메라 목록 가져오기 실패:", error);
    }
  };

  // 카메라 전환
  const switchCamera = async (cameraIndex: number) => {
    if (isSwitchingCamera) return;
    
    setIsSwitchingCamera(true);
    try {
      const response = await fetch(`${streamUrl}/camera/switch`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ cameraIndex })
      });
      
      if (response.ok) {
        const data = await response.json();
        if (data.success) {
          setCurrentCameraIndex(cameraIndex);
          console.log(`카메라 전환됨: ${data.currentCamera}`);
        }
      }
    } catch (error) {
      console.warn("카메라 전환 실패:", error);
    } finally {
      setIsSwitchingCamera(false);
    }
  };

  useEffect(() => {
    const img = imageRef.current;
    if (!img) return;

    const handleImageLoad = () => {
      console.log("🎬 Unity MJPEG 스트림 로드 완료!");
      setConnectionStatus("connected");
      
      // FPS 업데이트
      const now = Date.now();
      if (now - lastFpsUpdate >= 1000) {
        setFps(30); // MJPEG는 30fps
        setLastFpsUpdate(now);
      }
    };

    const handleImageError = (event: Event) => {
      console.warn("❌ Unity MJPEG 스트림 에러:", event);
      setConnectionStatus("error");
    };

    if (autoPlay) {
      setConnectionStatus("connecting");
      img.addEventListener("load", handleImageLoad);
      img.addEventListener("error", handleImageError);
      
      // MJPEG 스트림 URL로 직접 연결
      img.src = `${streamUrl}/stream.mjpg`;
    }

    return () => {
      if (img) {
        img.removeEventListener("load", handleImageLoad);
        img.removeEventListener("error", handleImageError);
      }
    };
  }, [streamUrl, autoPlay, lastFpsUpdate]);

  // 카메라 목록 가져오기
  useEffect(() => {
    if (showCameraControls) {
      fetchCameraList();
    }
  }, [streamUrl, showCameraControls]);

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

  const handleReconnect = () => {
    setConnectionStatus("connecting");
    window.location.reload();
  };

  return (
    <div
      className={`relative bg-black rounded-lg overflow-hidden ${className}`}
    >
      <img
        ref={imageRef}
        className="w-full h-full object-cover"
        alt="Unity MJPEG Stream"
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

      {/* 카메라 전환 버튼들 */}
      {showCameraControls && availableCameras.length > 1 && (
        <div className="absolute top-2 right-2 flex gap-1">
          {availableCameras.map((camera) => (
            <button
              key={camera.index}
              onClick={() => switchCamera(camera.index)}
              disabled={isSwitchingCamera}
              className={`px-2 py-1 text-xs rounded transition-colors ${
                currentCameraIndex === camera.index
                  ? "bg-blue-600 text-white"
                  : "bg-gray-700 hover:bg-gray-600 text-gray-300"
              } ${isSwitchingCamera ? "opacity-50 cursor-not-allowed" : ""}`}
            >
              {camera.name}
            </button>
          ))}
        </div>
      )}

      {/* 카메라 전환 중 표시 */}
      {isSwitchingCamera && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/50">
          <div className="text-white text-sm">
            <div className="animate-spin w-6 h-6 border-2 border-white border-t-transparent rounded-full mx-auto mb-2"></div>
            카메라 전환 중...
          </div>
        </div>
      )}

      {/* 연결 오류 시 재연결 버튼 */}
      {connectionStatus === "error" && (
        <div className="absolute inset-0 flex items-center justify-center bg-red-900/50">
          <button
            onClick={handleReconnect}
            className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white rounded-md text-sm"
          >
            재연결
          </button>
        </div>
      )}

    </div>
  );
}
