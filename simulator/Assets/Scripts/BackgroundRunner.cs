using UnityEngine;

/// <summary>
/// Unity가 백그라운드로 가도 계속 실행되도록 하는 스크립트
/// </summary>
public class BackgroundRunner : MonoBehaviour
{
    [Header("Background Settings")]
    public bool runInBackground = true;
    public bool preventScreenSleep = true;
    
    void Start()
    {
        // 백그라운드에서도 계속 실행되도록 설정
        Application.runInBackground = runInBackground;
        
        // 화면이 꺼지지 않도록 설정 (에디터에서만)
        if (preventScreenSleep)
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
        
        Debug.Log($"BackgroundRunner 설정 완료 - runInBackground: {Application.runInBackground}");
    }
    
    void Update()
    {
        // 백그라운드 상태 확인 (디버그용)
        if (Application.isFocused)
        {
            // 포커스 상태
        }
        else
        {
            // 백그라운드 상태 - 여전히 실행됨
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log($"Unity 포커스 상태 변경: {hasFocus}");
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        Debug.Log($"Unity 일시정지 상태: {pauseStatus}");
    }
    
    // 에디터에서 설정 확인
    [ContextMenu("Check Background Settings")]
    public void CheckBackgroundSettings()
    {
        Debug.Log("=== 백그라운드 설정 확인 ===");
        Debug.Log($"Application.runInBackground: {Application.runInBackground}");
        Debug.Log($"Screen.sleepTimeout: {Screen.sleepTimeout}");
        Debug.Log($"Application.isFocused: {Application.isFocused}");
        Debug.Log($"Application.isPlaying: {Application.isPlaying}");
    }
}
