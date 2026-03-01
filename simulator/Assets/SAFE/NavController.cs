using UnityEngine;
using UnityEngine.UI;

public class NavigationController : MonoBehaviour
{
    [Header("UI 연결")]
    public Button navButton;  // Nav 버튼
    public Button stopButton; // Stop 버튼 (선택사항)

    [Header("매니저 연결")]
    public AgentManager agentManager;  // AgentManager 참조
    public PathFinder pathFinder;      // PathFinder 참조

    void Start()
    {
        // AgentManager 자동 찾기
        if (agentManager == null)
        {
            agentManager = Object.FindFirstObjectByType<AgentManager>();
        }

        // PathFinder 자동 찾기
        if (pathFinder == null)
        {
            pathFinder = Object.FindFirstObjectByType<PathFinder>();
        }

        // Nav 버튼 클릭 이벤트 연결
        if (navButton != null)
        {
            navButton.onClick.AddListener(StartNavigation);
        }

        // Stop 버튼 클릭 이벤트 연결
        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopNavigation);
        }
    }

    void StartNavigation()
    {
        if (agentManager == null)
        {
            Debug.LogWarning("AgentManager가 연결되지 않았습니다.");
            return;
        }

        if (pathFinder == null)
        {
            Debug.LogWarning("PathFinder가 연결되지 않았습니다.");
            return;
        }

        // 선택된 Agent와 Rescuee 가져오기
        GameObject selectedAgent = agentManager.selectedAgent;
        GameObject selectedRescuee = agentManager.selectedRescuee;

        if (selectedAgent == null)
        {
            Debug.LogWarning("선택된 Agent가 없습니다. Agent를 먼저 선택해주세요.");
            return;
        }

        if (selectedRescuee == null)
        {
            Debug.LogWarning("선택된 Rescuee가 없습니다. Rescuee를 먼저 선택해주세요.");
            return;
        }

        // 새로운 PathFinder의 SaveNavigation 메서드 사용
        pathFinder.SaveNavigation(selectedAgent, selectedRescuee);

        // 추가된 타겟을 선택하여 해당 경로만 표시 (선택사항)
        pathFinder.SelectTarget(selectedRescuee);

        Debug.Log($"네비게이션 시작: {selectedAgent.name} → {selectedRescuee.name}");

        // PathFinder 활성화
        if (!pathFinder.enabled)
        {
            pathFinder.enabled = true;
        }
    }

    // 네비게이션 중지
    public void StopNavigation()
    {
        if (pathFinder != null)
        {
            // 모든 타겟 제거하여 네비게이션 중지
            var targetsToRemove = new System.Collections.Generic.List<GameObject>(pathFinder.targets);
            foreach (GameObject target in targetsToRemove)
            {
                if (target != null)
                {
                    pathFinder.RemoveTarget(target);
                }
            }

            Debug.Log("모든 네비게이션이 중지되었습니다.");
        }
    }

    // 특정 타겟만 제거
    public void RemoveCurrentTarget()
    {
        if (pathFinder != null && agentManager != null)
        {
            GameObject selectedRescuee = agentManager.selectedRescuee;
            if (selectedRescuee != null)
            {
                pathFinder.RemoveTarget(selectedRescuee);
                Debug.Log($"{selectedRescuee.name} 타겟이 제거되었습니다.");
            }
        }
    }

    // 모든 경로 표시
    public void ShowAllPaths()
    {
        if (pathFinder != null)
        {
            pathFinder.SelectAllPaths();
        }
    }

    // 가장 가까운 타겟 선택
    public void SelectClosestTarget()
    {
        if (pathFinder != null)
        {
            GameObject closest = pathFinder.GetClosestTarget();
            if (closest != null)
            {
                pathFinder.SelectTarget(closest);
                Debug.Log($"가장 가까운 타겟 선택: {closest.name}");
            }
            else
            {
                Debug.LogWarning("가까운 타겟이 없습니다.");
            }
        }
    }

    void OnDestroy()
    {
        // 메모리 누수 방지
        if (navButton != null)
        {
            navButton.onClick.RemoveListener(StartNavigation);
        }

        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(StopNavigation);
        }
    }
}