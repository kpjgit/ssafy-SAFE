using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpawnManager : MonoBehaviour
{
    [Header("스폰 설정")]
    public GameObject prefabToSpawn;        // 스폰할 프리팹
    public Transform spawnPoint;            // 스폰 위치 (Transform)
    public Button spawnButton;              // 스폰 버튼
    public TMP_InputField nameInputField;       // 이름 입력 필드


    [Header("기본 설정")]
    public string defaultName = "SpawnedObject";  // 기본 이름


    [Header("매니저 연결")]
    public AgentManager agentManager;       // AgentManager 참조
    public AgentManager.TargetType spawnType = AgentManager.TargetType.Agent;  // 스폰할 타입

    void Start()
    {
        if (agentManager == null)
        {
            agentManager = FindFirstObjectByType<AgentManager>();
        }

        // 버튼 클릭 이벤트 연결
        if (spawnButton != null)
        {
            spawnButton.onClick.AddListener(SpawnObject);
        }
    }

    void SpawnObject()
    {
        if (prefabToSpawn == null || spawnPoint == null)
        {
            Debug.LogWarning("프리팹 또는 스폰 포인트가 설정되지 않았습니다.");
            return;
        }

        if (agentManager == null)
        {
            Debug.LogWarning("AgentManager가 연결되지 않았습니다.");
            return;
        }

        // 오브젝트 생성
        GameObject spawnedObject = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);

        // 이름 설정
        string objectName = GetObjectName();
        spawnedObject.name = objectName;

        agentManager.AddAgent(spawnedObject);


        Debug.Log($"'{objectName}' {spawnType}가 스폰되었습니다.");

        // 입력 필드 초기화 (선택사항)
        if (nameInputField != null)
        {
            nameInputField.text = "";
        }
    }

    string GetObjectName()
    {
        // InputField에서 이름 가져오기
        if (nameInputField != null && !string.IsNullOrEmpty(nameInputField.text.Trim()))
        {
            return nameInputField.text.Trim();
        }

        // 입력이 없으면 기본 이름 사용
        return defaultName;
    }

    void OnDestroy()
    {
        // 메모리 누수 방지를 위한 이벤트 해제
        if (spawnButton != null)
        {
            spawnButton.onClick.RemoveListener(SpawnObject);
        }
    }
}