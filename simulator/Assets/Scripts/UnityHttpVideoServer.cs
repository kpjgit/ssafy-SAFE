using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

/// <summary>
/// Unity에서 프론트엔드로 실시간 비디오 스트리밍을 제공하는 HTTP 서버
/// </summary>
public class UnityHttpVideoServer : MonoBehaviour
{
    [Header("HTTP Video Streaming Server")]
    public int port = 8081;
    public Camera videoCamera;
    public bool useMainCamera = true;
    
    [Header("Camera Switching")]
    public bool enableCameraSwitching = false;
    public Camera[] availableCameras;
    public int currentCameraIndex = 0;
    
    [Header("Video Settings")]
    public int videoWidth = 1280;
    public int videoHeight = 720;
    public int videoFPS = 60; // 30에서 60으로 증가
    public int jpegQuality = 70; // 80에서 70으로 조정 (더 빠른 전송)
    
    [Header("Python Integration")]
    public bool enablePythonIntegration = true;
    public int pythonPort = 8082;
    public PythonBridge pythonBridge;
    
    private HttpListener httpListener;
    private bool isServerRunning = false;
    private RenderTexture videoTexture;
    private Texture2D frameTexture;
    private byte[] frameData;
    
    // Python 데이터 저장
    private string lastPythonData = "";
    private System.DateTime lastPythonDataTime = System.DateTime.MinValue;
    private byte[] lastPythonFrame = null;
    private System.DateTime lastPythonFrameTime = System.DateTime.MinValue;
    
    void Start()
    {
        // 백그라운드에서도 계속 실행되도록 설정
        Application.runInBackground = true;
        
        SetupVideoCamera();
        StartHttpServer();
        StartCoroutine(UpdateVideoFrames());
        
        // PythonBridge 이벤트 구독
        if (pythonBridge != null)
        {
            pythonBridge.OnLine += OnPythonDataReceived;
        }
    }
    
    void SetupVideoCamera()
    {
        if (enableCameraSwitching && availableCameras != null && availableCameras.Length > 0)
        {
            // 카메라 전환 모드
            if (currentCameraIndex >= 0 && currentCameraIndex < availableCameras.Length)
            {
                videoCamera = availableCameras[currentCameraIndex];
            }
        }
        else if (useMainCamera)
        {
            // 메인 카메라 사용
            videoCamera = Camera.main;
        }
            
        if (videoCamera == null)
        {
            Debug.LogError("비디오 카메라가 설정되지 않았습니다!");
            return;
        }
        
        // 비디오 텍스처 생성
        videoTexture = new RenderTexture(videoWidth, videoHeight, 24);
        videoTexture.Create();
        
        // 카메라에 비디오 텍스처 할당
        videoCamera.targetTexture = videoTexture;
        
        // 프레임 텍스처 생성
        frameTexture = new Texture2D(videoWidth, videoHeight, TextureFormat.RGB24, false);
        
        Debug.Log($"Unity 비디오 카메라 설정 완료: {videoWidth}x{videoHeight} @ {videoFPS}fps");
    }
    
    void StartHttpServer()
    {
        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}/");
            httpListener.Start();
            
            isServerRunning = true;
            Debug.Log($"Unity HTTP 비디오 서버 시작됨: http://localhost:{port}/");
            Debug.Log($"비디오 스트림 엔드포인트: http://localhost:{port}/unity-video/frame");
            Debug.Log($"MJPEG 스트림 엔드포인트: http://localhost:{port}/unity-video/stream.mjpg");
            Debug.Log($"서버 상태 엔드포인트: http://localhost:{port}/unity-video/status");
            
            // 비동기로 요청 처리
            HandleRequests();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unity HTTP 서버 시작 오류: {ex.Message}");
        }
    }
    
    async void HandleRequests()
    {
        while (isServerRunning)
        {
            try
            {
                var context = await httpListener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;
                
                // CORS 헤더 추가 (웹 브라우저 호환성)
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                
                // OPTIONS 요청 처리 (CORS preflight)
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    continue;
                }
                
                // 라우팅 처리
                if (request.Url.AbsolutePath.StartsWith("/unity-video/frame"))
                {
                    // 비디오 프레임 요청
                    await HandleVideoFrameRequest(response);
                }
                else if (request.Url.AbsolutePath.StartsWith("/unity-video/stream.mjpg"))
                {
                    // Unity MJPEG 스트림 요청
                    await HandleMJPEGStreamRequest(response);
                }
                else if (request.Url.AbsolutePath == "/unity-video/status")
                {
                    // 서버 상태 요청
                    await HandleStatusRequest(response);
                }
                else if (request.Url.AbsolutePath.StartsWith("/python-video/frame"))
                {
                    // Python 데이터 수신 (POST)
                    if (request.HttpMethod == "POST")
                    {
                        await HandlePythonDataRequest(request, response);
                    }
                    else
                    {
                        // Python JPEG 프레임 요청 (GET)
                        await HandlePythonImageRequest(response);
                    }
                }
                else if (request.Url.AbsolutePath.StartsWith("/python-video/image"))
                {
                    // Python JPEG 프레임 요청 (GET)
                    await HandlePythonImageRequest(response);
                }
                else if (request.Url.AbsolutePath.StartsWith("/python-video/data"))
                {
                    // Python JSON 데이터 요청 (GET)
                    await HandlePythonFrameRequest(response);
                }
                else
                {
                    // 기본 응답 (서버 정보)
                    await HandleDefaultRequest(response);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"HTTP 요청 처리 오류: {ex.Message}");
            }
        }
    }
    
    async System.Threading.Tasks.Task HandleVideoFrameRequest(HttpListenerResponse response)
    {
        try
        {
            if (frameData == null || frameData.Length == 0)
            {
                response.StatusCode = 204; // No Content
                response.Close();
                return;
            }
            
            response.ContentType = "image/jpeg";
            response.ContentLength64 = frameData.Length;
            response.StatusCode = 200;
            
            await response.OutputStream.WriteAsync(frameData, 0, frameData.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"비디오 프레임 응답 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    async System.Threading.Tasks.Task HandleStatusRequest(HttpListenerResponse response)
    {
        try
        {
            var statusData = new
            {
                server = "Unity HTTP Video Server",
                status = "running",
                port = port,
                videoWidth = videoWidth,
                videoHeight = videoHeight,
                fps = videoFPS,
                quality = jpegQuality,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };
            
            string jsonResponse = JsonUtility.ToJson(statusData, true);
            byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
            
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"상태 응답 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    async System.Threading.Tasks.Task HandlePythonDataRequest(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            // Content-Type 확인
            string contentType = request.ContentType ?? "";
            
            if (contentType.Contains("application/json"))
            {
                // JSON 데이터 처리
                byte[] buffer = new byte[request.ContentLength64];
                await request.InputStream.ReadAsync(buffer, 0, buffer.Length);
                string jsonData = Encoding.UTF8.GetString(buffer);
                
                // Python 데이터 저장
                lastPythonData = jsonData;
                lastPythonDataTime = System.DateTime.Now;
                
                Debug.Log($"Python JSON 데이터 수신: {jsonData.Length} bytes");
            }
            else if (contentType.Contains("image/jpeg"))
            {
                // JPEG 프레임 처리
                byte[] buffer = new byte[request.ContentLength64];
                await request.InputStream.ReadAsync(buffer, 0, buffer.Length);
                
                // Python 프레임 저장
                lastPythonFrame = buffer;
                lastPythonFrameTime = System.DateTime.Now;
                
                Debug.Log($"Python JPEG 프레임 수신: {buffer.Length} bytes");
            }
            else
            {
                Debug.LogWarning($"지원하지 않는 Content-Type: {contentType}");
                response.StatusCode = 400;
                response.Close();
                return;
            }
            
            // 응답
            response.StatusCode = 200;
            response.ContentType = "application/json";
            string responseJson = "{\"status\":\"received\"}";
            byte[] responseBuffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = responseBuffer.Length;
            await response.OutputStream.WriteAsync(responseBuffer, 0, responseBuffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Python 데이터 처리 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    async System.Threading.Tasks.Task HandlePythonFrameRequest(HttpListenerResponse response)
    {
        try
        {
            if (string.IsNullOrEmpty(lastPythonData))
            {
                response.StatusCode = 204; // No Content
                response.Close();
                return;
            }
            
            response.ContentType = "application/json; charset=utf-8";
            byte[] buffer = Encoding.UTF8.GetBytes(lastPythonData);
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Python 프레임 응답 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    async System.Threading.Tasks.Task HandlePythonImageRequest(HttpListenerResponse response)
    {
        try
        {
            if (lastPythonFrame == null || lastPythonFrame.Length == 0)
            {
                response.StatusCode = 204; // No Content
                response.Close();
                return;
            }
            
            response.ContentType = "image/jpeg";
            response.ContentLength64 = lastPythonFrame.Length;
            response.StatusCode = 200;
            
            await response.OutputStream.WriteAsync(lastPythonFrame, 0, lastPythonFrame.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Python 이미지 처리 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    async System.Threading.Tasks.Task HandleDefaultRequest(HttpListenerResponse response)
    {
        try
        {
            string responseString = @"Unity HTTP Video Server is running!

Available Endpoints:
- GET /unity-video/frame - Get current video frame (JPEG)
- GET /unity-video/status - Get server status (JSON)
- POST /python-video/frame - Receive Python pose data (JSON/JPEG)
- GET /python-video/frame - Get latest Python frame (JPEG)
- GET /python-video/image - Get latest Python frame (JPEG)
- GET /python-video/data - Get latest Python pose data (JSON)

Video Settings:
- Resolution: " + videoWidth + "x" + videoHeight + @"
- FPS: " + videoFPS + @"
- Quality: " + jpegQuality + @"%

Python Integration:
- Python data endpoint: http://localhost:" + port + @"/python-video/frame
- Python image endpoint: http://localhost:" + port + @"/python-video/image
- Python pose data endpoint: http://localhost:" + port + @"/python-video/data
- Last data time: " + (lastPythonDataTime != System.DateTime.MinValue ? lastPythonDataTime.ToString("HH:mm:ss") : "None") + @"
- Last frame time: " + (lastPythonFrameTime != System.DateTime.MinValue ? lastPythonFrameTime.ToString("HH:mm:ss") : "None") + @"

Frontend Integration:
React component should connect to: http://localhost:" + port + @"/unity-video/frame";
            
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Debug.LogError($"기본 응답 오류: {ex.Message}");
            response.StatusCode = 500;
            response.Close();
        }
    }
    
    IEnumerator UpdateVideoFrames()
    {
        float frameInterval = 1f / videoFPS;
        
        while (isServerRunning)
        {
            yield return new WaitForSeconds(frameInterval);
            CaptureFrame();
        }
    }
    
    void CaptureFrame()
    {
        if (videoTexture == null || frameTexture == null) return;
        
        try
        {
            // 렌더 텍스처에서 프레임 캡처
            RenderTexture.active = videoTexture;
            frameTexture.ReadPixels(new Rect(0, 0, videoWidth, videoHeight), 0, 0);
            frameTexture.Apply();
            RenderTexture.active = null;
            
            // 텍스처를 JPEG로 인코딩
            frameData = frameTexture.EncodeToJPG(jpegQuality);
        }
        catch (Exception ex)
        {
            Debug.LogError($"프레임 캡처 오류: {ex.Message}");
        }
    }
    
    // 외부에서 호출할 수 있는 공개 메서드들
    public void SetVideoResolution(int width, int height)
    {
        videoWidth = width;
        videoHeight = height;
        
        // 기존 텍스처 정리
        if (videoTexture != null)
        {
            videoTexture.Release();
        }
        if (frameTexture != null)
        {
            DestroyImmediate(frameTexture);
        }
        
        // 새로운 해상도로 재설정
        SetupVideoCamera();
        
        Debug.Log($"비디오 해상도 변경: {width}x{height}");
    }
    
    public void SetVideoFPS(int fps)
    {
        videoFPS = Mathf.Clamp(fps, 1, 60);
        Debug.Log($"비디오 FPS 변경: {videoFPS}");
    }
    
    public void SetJPEGQuality(int quality)
    {
        jpegQuality = Mathf.Clamp(quality, 1, 100);
        Debug.Log($"JPEG 품질 변경: {jpegQuality}%");
    }
    
    public void SetVideoCamera(Camera camera)
    {
        videoCamera = camera;
        SetupVideoCamera();
        Debug.Log($"비디오 카메라 변경: {camera?.name ?? "null"}");
    }
    
    // 카메라 전환 기능
    public void SwitchToCamera(int cameraIndex)
    {
        if (enableCameraSwitching && availableCameras != null && 
            cameraIndex >= 0 && cameraIndex < availableCameras.Length)
        {
            currentCameraIndex = cameraIndex;
            SetupVideoCamera();
            Debug.Log($"카메라 전환: {videoCamera?.name ?? "null"}");
        }
    }
    
    public void NextCamera()
    {
        if (enableCameraSwitching && availableCameras != null && availableCameras.Length > 1)
        {
            currentCameraIndex = (currentCameraIndex + 1) % availableCameras.Length;
            SwitchToCamera(currentCameraIndex);
        }
    }
    
    public void PreviousCamera()
    {
        if (enableCameraSwitching && availableCameras != null && availableCameras.Length > 1)
        {
            currentCameraIndex = (currentCameraIndex - 1 + availableCameras.Length) % availableCameras.Length;
            SwitchToCamera(currentCameraIndex);
        }
    }
    
    void OnDestroy()
    {
        // PythonBridge 이벤트 구독 해제
        if (pythonBridge != null)
        {
            pythonBridge.OnLine -= OnPythonDataReceived;
        }
        
        isServerRunning = false;
        httpListener?.Stop();
        httpListener?.Close();
        
        if (videoTexture != null)
        {
            videoTexture.Release();
            DestroyImmediate(videoTexture);
        }
        if (frameTexture != null)
        {
            DestroyImmediate(frameTexture);
        }
        
        Debug.Log("Unity HTTP 비디오 서버 종료됨");
    }
    
    // 디버그 정보 출력
    [ContextMenu("Print Server Info")]
    public void PrintServerInfo()
    {
        Debug.Log("=== Unity HTTP 비디오 서버 정보 ===");
        Debug.Log($"서버 상태: {(isServerRunning ? "실행 중" : "중지됨")}");
        Debug.Log($"포트: {port}");
        Debug.Log($"해상도: {videoWidth}x{videoHeight}");
        Debug.Log($"FPS: {videoFPS}");
        Debug.Log($"JPEG 품질: {jpegQuality}%");
        Debug.Log($"카메라: {videoCamera?.name ?? "null"}");
        Debug.Log($"비디오 텍스처: {(videoTexture != null ? "생성됨" : "없음")}");
        Debug.Log($"프레임 데이터: {(frameData != null ? $"{frameData.Length} bytes" : "없음")}");
    }
    
    // Python 데이터 수신 처리
    private void OnPythonDataReceived(string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        
        // JSON 데이터인지 확인
        if (data.Trim().StartsWith("{"))
        {
            lastPythonData = data;
            lastPythonDataTime = System.DateTime.Now;
            Debug.Log($"[UnityHttpVideoServer] Python JSON 데이터 수신: {data.Length} bytes");
        }
    }
    
    // Python 프레임 데이터 수신 처리 (POST 요청으로 받은 JPEG)
    public void OnPythonFrameReceived(byte[] frameData)
    {
        lastPythonFrame = frameData;
        lastPythonFrameTime = System.DateTime.Now;
        Debug.Log($"[UnityHttpVideoServer] Python 프레임 수신: {frameData.Length} bytes");
    }
    
    // Python 데이터 가져오기
    public string GetLastPythonData()
    {
        return lastPythonData;
    }
    
    public System.DateTime GetLastPythonDataTime()
    {
        return lastPythonDataTime;
    }
    
    // Python 프레임 가져오기
    public byte[] GetLastPythonFrame()
    {
        return lastPythonFrame;
    }
    
    public System.DateTime GetLastPythonFrameTime()
    {
        return lastPythonFrameTime;
    }
    
    // MJPEG 스트림 핸들러
    async System.Threading.Tasks.Task HandleMJPEGStreamRequest(HttpListenerResponse response)
    {
        try
        {
            // MJPEG 스트림 헤더 설정
            response.StatusCode = 200;
            response.ContentType = "multipart/x-mixed-replace; boundary=frame";
            response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Headers.Add("Pragma", "no-cache");
            response.Headers.Add("Expires", "0");
            response.Headers.Add("Connection", "close");
            
            var output = response.OutputStream;
            
            while (response.OutputStream.CanWrite)
            {
                if (frameData != null && frameData.Length > 0)
                {
                    // MJPEG 프레임 전송
                    var frameHeader = System.Text.Encoding.ASCII.GetBytes($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frameData.Length}\r\n\r\n");
                    await output.WriteAsync(frameHeader, 0, frameHeader.Length);
                    await output.WriteAsync(frameData, 0, frameData.Length);
                    await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes("\r\n"), 0, 2);
                    await output.FlushAsync();
                }
                
                // 프레임 간격 대기 (30fps = 33ms)
                await System.Threading.Tasks.Task.Delay(33);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"MJPEG 스트림 오류: {ex.Message}");
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch { }
        }
    }
}
