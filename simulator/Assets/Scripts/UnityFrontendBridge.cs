using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Unity 시뮬레이터와 프론트엔드 간의 통합 브리지
/// - HTTP 비디오 스트리밍
/// - 실시간 데이터 전송
/// - 상태 모니터링
/// </summary>
public class UnityFrontendBridge : MonoBehaviour
{
    [Header("Bridge Settings")]
    public bool enableVideoStreaming = true;
    public bool enableDataStreaming = true;
    public bool autoStartOnPlay = true;
    
    [Header("Components")]
    public UnityHttpVideoServer httpVideoServer;
    public AgentManager agentManager;
    public PathFinder pathFinder;
    public PoseManager poseManager;
    
    [Header("UI References (Optional)")]
    public Text connectionStatusText;
    public Text fpsText;
    public Button reconnectButton;
    
    [Header("Data Streaming")]
    public float dataUpdateInterval = 0.1f; // 10fps
    public bool streamAgentPositions = true;
    public bool streamSensorData = true;
    public bool streamPathData = true;
    
    private float lastDataUpdateTime;
    private Dictionary<string, object> currentData = new Dictionary<string, object>();
    private float fpsCounter = 0f;
    private float fpsTimer = 0f;
    
    void Start()
    {
        InitializeComponents();
        
        if (autoStartOnPlay)
        {
            StartAllServices();
        }
        
        if (reconnectButton != null)
        {
            reconnectButton.onClick.AddListener(ReconnectAllServices);
        }
    }
    
    void InitializeComponents()
    {
        // 컴포넌트 자동 할당
        if (httpVideoServer == null)
            httpVideoServer = FindObjectOfType<UnityHttpVideoServer>();
            
        if (agentManager == null)
            agentManager = FindObjectOfType<AgentManager>();
            
        if (pathFinder == null)
            pathFinder = FindObjectOfType<PathFinder>();
            
        if (poseManager == null)
            poseManager = FindObjectOfType<PoseManager>();
        
        Debug.Log("Unity-Frontend 브리지 초기화 완료");
    }
    
    public void StartAllServices()
    {
        Debug.Log("Unity 서비스 시작 중...");
        
        // HTTP 비디오 서버 시작
        if (enableVideoStreaming && httpVideoServer != null)
        {
            if (!httpVideoServer.enabled)
            {
                httpVideoServer.enabled = true;
                Debug.Log("HTTP 비디오 스트리밍 서버 시작됨");
            }
        }
        
        // 데이터 스트리밍 시작
        if (enableDataStreaming)
        {
            StartCoroutine(StreamDataToFrontend());
        }
        
        UpdateConnectionStatus("모든 서비스 시작됨");
    }
    
    public void StopAllServices()
    {
        Debug.Log("Unity 서비스 중지 중...");
        
        if (httpVideoServer != null)
        {
            httpVideoServer.enabled = false;
        }
        
        StopAllCoroutines();
        UpdateConnectionStatus("모든 서비스 중지됨");
    }
    
    public void ReconnectAllServices()
    {
        Debug.Log("Unity 서비스 재연결 중...");
        StopAllServices();
        
        // 잠시 대기 후 재시작
        StartCoroutine(DelayedRestart());
    }
    
    IEnumerator DelayedRestart()
    {
        yield return new WaitForSeconds(1f);
        StartAllServices();
    }
    
    IEnumerator StreamDataToFrontend()
    {
        while (enableDataStreaming)
        {
            yield return new WaitForSeconds(dataUpdateInterval);
            
            try
            {
                CollectAndStreamData();
            }
            catch (Exception ex)
            {
                Debug.LogError($"데이터 스트리밍 오류: {ex.Message}");
            }
        }
    }
    
    void CollectAndStreamData()
    {
        currentData.Clear();
        
        // 에이전트 위치 데이터
        if (streamAgentPositions && agentManager != null)
        {
            var agentData = CollectAgentData();
            currentData["agents"] = agentData;
        }
        
        // 경로 데이터
        if (streamPathData && pathFinder != null)
        {
            var pathData = CollectPathData();
            currentData["paths"] = pathData;
        }
        
        // 센서 데이터 (PoseManager에서)
        if (streamSensorData && poseManager != null)
        {
            var sensorData = CollectSensorData();
            currentData["sensors"] = sensorData;
        }
        
        // 타임스탬프 추가
        currentData["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        currentData["unityTime"] = Time.time;
        
        // 데이터 로깅 비활성화 (성능 향상)
        // if (currentData.Count > 0)
        // {
        //     string jsonData = JsonUtility.ToJson(currentData, true);
        //     Debug.Log($"Unity 데이터 수집 완료: {jsonData.Length} bytes");
        // }
    }
    
    Dictionary<string, object> CollectAgentData()
    {
        var agentData = new Dictionary<string, object>();
        
        if (agentManager != null)
        {
            agentData["agentCount"] = agentManager.GetAgentCount();
            agentData["rescueeCount"] = agentManager.GetRescueeCount();
            agentData["objCount"] = agentManager.GetObjCount();
            
            // 선택된 객체 정보
            var selectedAgent = agentManager.GetSelectedAgent();
            if (selectedAgent != null)
            {
                agentData["selectedAgent"] = new
                {
                    name = selectedAgent.name,
                    position = selectedAgent.transform.position,
                    rotation = selectedAgent.transform.rotation
                };
            }
        }
        
        return agentData;
    }
    
    Dictionary<string, object> CollectPathData()
    {
        var pathData = new Dictionary<string, object>();
        
        if (pathFinder != null)
        {
            // PathFinder에서 경로 정보 수집
            pathData["hasFireman"] = pathFinder.Fireman != null;
            
            if (pathFinder.Fireman != null)
            {
                pathData["firemanPosition"] = pathFinder.Fireman.transform.position;
            }
            
            // 타겟 정보
            pathData["targetCount"] = pathFinder.targets.Count;
        }
        
        return pathData;
    }
    
    Dictionary<string, object> CollectSensorData()
    {
        var sensorData = new Dictionary<string, object>();
        
        // PoseManager에서 포즈 데이터 수집
        sensorData["poseManagerActive"] = poseManager != null && poseManager.enabled;
        
        return sensorData;
    }
    
    void Update()
    {
        // FPS 계산 및 표시
        fpsCounter++;
        fpsTimer += Time.deltaTime;
        
        if (fpsTimer >= 1f)
        {
            float currentFPS = fpsCounter / fpsTimer;
            
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {currentFPS:F1}";
            }
            
            fpsCounter = 0f;
            fpsTimer = 0f;
        }
        
        // 연결 상태 확인
        CheckConnectionStatus();
    }
    
    void CheckConnectionStatus()
    {
        bool httpActive = httpVideoServer != null && httpVideoServer.enabled;
        
        string status = "";
        if (httpActive)
            status = "HTTP 비디오 서비스 활성";
        else
            status = "서비스 비활성";
        
        UpdateConnectionStatus(status);
    }
    
    void UpdateConnectionStatus(string status)
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = $"Unity 상태: {status}";
        }
        
        // Debug.Log($"Unity-Frontend 브리지 상태: {status}"); // 로그 비활성화
    }
    
    // 외부에서 호출할 수 있는 공개 메서드들
    public void SetVideoQuality(int quality)
    {
        if (httpVideoServer != null)
        {
            httpVideoServer.SetJPEGQuality(quality);
        }
    }
    
    public void SetDataUpdateRate(float interval)
    {
        dataUpdateInterval = interval;
        Debug.Log($"데이터 업데이트 간격 변경: {interval}초");
    }
    
    public void ToggleVideoStreaming(bool enable)
    {
        enableVideoStreaming = enable;
        if (httpVideoServer != null)
        {
            httpVideoServer.enabled = enable;
        }
    }
    
    public void ToggleDataStreaming(bool enable)
    {
        enableDataStreaming = enable;
        if (enable)
        {
            StartCoroutine(StreamDataToFrontend());
        }
        else
        {
            StopAllCoroutines();
        }
    }
    
    // 프론트엔드에서 Unity로 명령 전송 (향후 확장용)
    public void ReceiveCommandFromFrontend(string command, object data)
    {
        Debug.Log($"프론트엔드 명령 수신: {command}");
        
        switch (command)
        {
            case "select_agent":
                // 에이전트 선택 로직
                break;
            case "change_camera":
                // 카메라 변경 로직
                break;
            case "set_video_quality":
                if (data is int quality)
                {
                    SetVideoQuality(quality);
                }
                break;
            default:
                Debug.LogWarning($"알 수 없는 명령: {command}");
                break;
        }
    }
    
    void OnDestroy()
    {
        StopAllServices();
    }
    
    // 디버그 정보 출력
    [ContextMenu("Print Bridge Info")]
    public void PrintBridgeInfo()
    {
        Debug.Log("=== Unity-Frontend 브리지 정보 ===");
        Debug.Log($"비디오 스트리밍 활성: {enableVideoStreaming}");
        Debug.Log($"데이터 스트리밍 활성: {enableDataStreaming}");
        Debug.Log($"데이터 업데이트 간격: {dataUpdateInterval}초");
        Debug.Log($"HTTP 비디오 서버: {httpVideoServer != null}");
        Debug.Log($"에이전트 매니저: {agentManager != null}");
        Debug.Log($"경로 찾기: {pathFinder != null}");
        Debug.Log($"포즈 매니저: {poseManager != null}");
        
        if (httpVideoServer != null)
        {
            httpVideoServer.PrintServerInfo();
        }
    }
}
