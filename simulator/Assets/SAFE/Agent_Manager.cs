using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class AgentManager : MonoBehaviour
{
    public enum TargetType { Agent, Rescuee, Obj }

    [Header("UI 연결")]
    
    public TMP_Dropdown AgentDropdown;
    public TMP_Dropdown RescueeDropdown;
    public TMP_Dropdown ObjDropdown;

    [Header("PoseManager")]
    public PoseManager PoseManage;


    // 캐싱된 리스트 - 메모리 재할당 최소화
    public List<GameObject> spawnedAgents = new List<GameObject>();
    public List<GameObject> spawnedRescuees = new List<GameObject>();
    public List<GameObject> spawnedObj = new List<GameObject>(); // spawnedAgents + spawnedRescuees 합친 리스트

    // 선택된 객체들
    public GameObject selectedRescuee;
    public GameObject selectedAgent;
    public GameObject selectedObj;

    void Start()
    {
        InitializeDropdowns();
    }

    private void InitializeDropdowns()
    {
        if (AgentDropdown != null)
        {
            AgentDropdown.onValueChanged.AddListener(index => OnDropdownChanged(index, TargetType.Agent));
            UpdateDropdown(TargetType.Agent);
        }

        if (RescueeDropdown != null)
        {
            RescueeDropdown.onValueChanged.AddListener(index => OnDropdownChanged(index, TargetType.Rescuee));
            UpdateDropdown(TargetType.Rescuee);
        }

        if (ObjDropdown != null)
        {
            ObjDropdown.onValueChanged.AddListener(index => OnDropdownChanged(index, TargetType.Obj));
            UpdateDropdown(TargetType.Obj);
        }
    }

    /// <summary>
    /// 제네릭 드롭다운 업데이트 함수 - 중복 코드 제거
    /// </summary>
    private void UpdateDropdown(TargetType targetType)
    {
        TMP_Dropdown dropdown;
        List<GameObject> sourceList;

        // 타입에 따른 참조 설정
        if (targetType == TargetType.Agent)
        {
            dropdown = AgentDropdown;
            sourceList = spawnedAgents;
        }
        else if (targetType == TargetType.Rescuee)
        {
            dropdown = RescueeDropdown;
            sourceList = spawnedRescuees;
        }
        else // TargetType.Obj
        {
            dropdown = ObjDropdown;
            sourceList = spawnedObj;
        }

        if (dropdown == null) return;

        // 기존 옵션 모두 삭제
        dropdown.options.Clear();

        // "선택안함" 기본 옵션 추가
        dropdown.options.Add(new TMP_Dropdown.OptionData("Not selected"));

        // 유효한 객체들만 드롭다운에 추가
        foreach (var obj in sourceList)
        {
            if (obj != null)
            {
                dropdown.options.Add(new TMP_Dropdown.OptionData(obj.name));
            }
        }

        // 기본값 맨 위(선택안함)로 맞추고 UI 갱신
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
    }

    /// <summary>
    /// spawnedObj 리스트 업데이트 - spawnedAgents + spawnedRescuees
    /// </summary>
    private void UpdateObjList()
    {
        spawnedObj.Clear();

        // spawnedAgents 추가
        foreach (var agent in spawnedAgents)
        {
            if (agent != null)
                spawnedObj.Add(agent);
        }

        // spawnedRescuees 추가
        foreach (var rescuee in spawnedRescuees)
        {
            if (rescuee != null)
                spawnedObj.Add(rescuee);
        }

        // Obj 드롭다운 업데이트
        UpdateDropdown(TargetType.Obj);
    }

    /// <summary>
    /// 드롭다운 선택 변경 처리 - 개선된 경계값 검사
    /// </summary>
    private void OnDropdownChanged(int dropdownIndex, TargetType targetType)
    {
        if (dropdownIndex <= 0)
        {
            HandleDeselection(targetType);
            return;
        }

        int objectIndex = dropdownIndex - 1;
        GameObject selectedObject = GetObjectByIndex(targetType, objectIndex);

        if (selectedObject != null)
        {
            SetSelectedObject(targetType, selectedObject);
            LogSelection(targetType, selectedObject.name);
        }
        else
        {
            HandleDeselection(targetType);
            Debug.LogWarning($"Invalid {targetType} index: {objectIndex}");
        }
    }

    private void HandleDeselection(TargetType targetType)
    {
        if (targetType == TargetType.Agent)
        {
            selectedAgent = null;
            Debug.Log("Agent 선택 해제");
        }
        else if (targetType == TargetType.Rescuee)
        {
            selectedRescuee = null;
            Debug.Log("Rescuee 선택 해제");
        }
        else // TargetType.Obj
        {
            selectedObj = null;
            Debug.Log("Obj 선택 해제");
        }
    }

    private GameObject GetObjectByIndex(TargetType targetType, int index)
    {
        List<GameObject> sourceList;

        if (targetType == TargetType.Agent)
            sourceList = spawnedAgents;
        else if (targetType == TargetType.Rescuee)
            sourceList = spawnedRescuees;
        else // TargetType.Obj
            sourceList = spawnedObj;

        return (index >= 0 && index < sourceList.Count) ? sourceList[index] : null;
    }

    private void SetSelectedObject(TargetType targetType, GameObject obj)
    {
        if (targetType == TargetType.Agent)
            selectedAgent = obj;
        else if (targetType == TargetType.Rescuee)
            selectedRescuee = obj;
        else // TargetType.Obj
            selectedObj = obj;
    }

    private void LogSelection(TargetType targetType, string objectName)
    {
        Debug.Log($"선택된 {targetType}: {objectName}");
    }

    // Public API 메서드들
    public void AddAgent(GameObject go)
    {
        if (AddToList(go, spawnedAgents))
        {
            UpdateDropdown(TargetType.Agent);
            UpdateObjList(); // Obj 리스트도 업데이트
        }
    }

    public void RemoveAgent(GameObject go)
    {
        if (RemoveFromList(go, spawnedAgents))
        {
            UpdateDropdown(TargetType.Agent);
            UpdateObjList(); // Obj 리스트도 업데이트
        }
    }

    public void AddRescuee(GameObject go)
    {
        if (AddToList(go, spawnedRescuees))
        {
            UpdateDropdown(TargetType.Rescuee);
            UpdateObjList(); // Obj 리스트도 업데이트
        }
    }

    public void RemoveRescuee(GameObject go)
    {
        if (RemoveFromList(go, spawnedRescuees))
        {
            UpdateDropdown(TargetType.Rescuee);
            UpdateObjList(); // Obj 리스트도 업데이트
        }
    }

    /// <summary>
    /// 안전한 리스트 추가 - null 체크 포함
    /// </summary>
    private bool AddToList(GameObject go, List<GameObject> list)
    {
        if (go == null) return false;

        list.Add(go);
        CleanupNulls(list);
        return true;
    }

    /// <summary>
    /// 안전한 리스트 제거
    /// </summary>
    private bool RemoveFromList(GameObject go, List<GameObject> list)
    {
        if (go == null) return false;

        bool removed = list.Remove(go);
        CleanupNulls(list);
        return removed;
    }

    /// <summary>
    /// null 참조 정리 - 메모리 누수 방지
    /// </summary>
    private void CleanupNulls(List<GameObject> list)
    {
        // RemoveAll은 내부적으로 효율적인 알고리즘 사용
        list.RemoveAll(obj => obj == null);
    }

    // 접근자 메서드들 - 캡슐화 개선
    public GameObject GetSelectedAgent() => selectedAgent;
    public GameObject GetSelectedRescuee() => selectedRescuee;
    public GameObject GetSelectedObj() => selectedObj;
    public int GetAgentCount() => spawnedAgents.Count;
    public int GetRescueeCount() => spawnedRescuees.Count;
    public int GetObjCount() => spawnedObj.Count;
}