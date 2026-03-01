using UnityEngine;
using UnityEngine.UI;  // 이 줄을 추가

public class RemoveObj : MonoBehaviour
{


    [Header("스폰 설정")]
    public Button removeButton;              // remove 버튼

    private GameObject selectedObj;
    [Header("매니저 연결")]
    public AgentManager agentManager;       // AgentManager 참조

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (agentManager == null)
        {
            agentManager = FindFirstObjectByType<AgentManager>();
        }

        // 버튼 클릭 이벤트 연결
        if (removeButton != null)
        {
            removeButton.onClick.AddListener(removeObject);
        }
    }

    void removeObject()
    {
        if (agentManager == null)
        {
            Debug.LogWarning("AgentManager가 연결되지 않았습니다.");
            return;
        }

        // AgentManager에서 선택된 Obj 가져오기
        selectedObj = agentManager.selectedObj;

        if (selectedObj == null)
        {
            Debug.LogWarning("선택된 객체가 없습니다.");
            return;
        }

        // 선택된 객체가 어느 리스트에 속하는지 확인하고 해당 리스트에서 삭제
        if (agentManager.spawnedAgents.Contains(selectedObj))
        {
            Debug.Log($"Agent '{selectedObj.name}'를 spawnedAgents 리스트에서 삭제합니다.");
            agentManager.RemoveAgent(selectedObj);
        }
        else if (agentManager.spawnedRescuees.Contains(selectedObj))
        {
            Debug.Log($"Rescuee '{selectedObj.name}'를 spawnedRescuees 리스트에서 삭제합니다.");
            agentManager.RemoveRescuee(selectedObj);
        }
        else
        {
            Debug.LogWarning($"객체 '{selectedObj.name}'가 어느 리스트에도 속하지 않습니다.");
            return;
        }

        // 실제 게임 오브젝트를 씬에서 삭제
        DestroyImmediate(selectedObj);
        Debug.Log("객체가 성공적으로 삭제되었습니다.");
    }

    void OnDestroy()
    {
        // 메모리 누수 방지를 위한 이벤트 해제
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(removeObject);
        }
    }
}
