using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

[System.Serializable]
public class NavData
{
    public GameObject target;
    public NavMeshPath path;
    public LineRenderer lineRenderer;
    public float distance;
    public bool isValid;

    public NavData(GameObject target)
    {
        this.target = target;
        this.path = new NavMeshPath();
        this.distance = 0f;
        this.isValid = false;
    }
}

public class PathFinder : MonoBehaviour
{
    /*
     targets : 피구조자들
     agent : 소방관
     
    원하는 것 : 여러 target들에 대한 drawline, 토픽을 통한 소방관 위치 업데이트
     */
    public GameObject Fireman;
    public TextMeshProUGUI remainDts;
    public TextMeshProUGUI remainTm;

    [Header("시간 예측 설정")]
    public float defaultWalkingSpeed = 1.4f; // m/s (평균 걷기 속도)
    public int velocityHistorySize = 10;     // 속도 이력 저장 개수

    // 속도 계산을 위한 변수들
    private Queue<Vector3> positionHistory = new Queue<Vector3>();
    private Queue<float> timeHistory = new Queue<float>();
    private Vector3 lastPosition;
    private float lastUpdateTime;
    public List<GameObject> targets = new List<GameObject>(); // 여러 타겟 지원

    [Header("UI 설정")]
    public TMP_Dropdown pathDropdown;     // 경로 선택 드롭다운

    [Header("Spawn 설정")]
    public GameObject spawn; // 미리 생성된 스폰 GameObject

    [Header("NavMesh 보정 설정")]
    public float navMeshSearchRange = 3.0f;  // NavMesh 검색 범위
    public bool enableNavMeshCorrection = true;  // NavMesh 보정 활성화/비활성화

    [Header("지면 감지 설정")]
    public LayerMask groundLayerMask = ~0;  // 지면으로 인식할 레이어 (기본: 모든 레이어)
    public float raycastDistance = 20f;     // Raycast 최대 거리
    public float sphereRadius = 0.3f;       // SphereCast 반지름
    public bool useNavMeshFirst = true;     // NavMesh 우선 사용 여부
    public bool enableGroundCorrection = true; // 지면 보정 활성화/비활성화
    public bool ignoreRosYCoordinate = true;   // ROS Y좌표 무시 여부

    // 각 타겟별 네비게이션 데이터 저장
    private Dictionary<GameObject, NavData> navDataContainer = new Dictionary<GameObject, NavData>();

    public string topic = "/unity/pose";
    ROSConnection ros;
    Vector3 offset = new Vector3(0, 1.0f, 0); // 라인을 위로 1.5m 올리기

    // LineRenderer 설정용 색상들
    public Color[] pathColors = { Color.red, Color.blue, Color.green, Color.yellow, Color.magenta, Color.cyan };

    // 드롭다운 제어 변수
    private int selectedPathIndex = 0;

    void Start()
    {
        Debug.Log("Multi-Target PathFinder started");
        InitializeTargets();
        InitializeUI();

        if (spawn == null) Debug.Log("Dont set spawn point");
    }

    void InitializeUI()
    {
        // 드롭다운 이벤트 연결
        if (pathDropdown != null)
        {
            pathDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            UpdateDropdownOptions();
        }
    }

    void UpdateDropdownOptions()
    {
        if (pathDropdown == null) return;

        pathDropdown.ClearOptions();
        List<string> options = new List<string>();

        // "모든 경로" 옵션 추가
        options.Add("All Paths");

        // 각 타겟 이름과 거리 추가
        foreach (GameObject target in targets)
        {
            if (target != null)
            {
                float distance = GetDistanceToTarget(target);
                string distanceText = distance > 0 ? $" ({distance:F1}m)" : " (calc...)";
                options.Add($"{target.name}{distanceText}");
            }
        }

        pathDropdown.AddOptions(options);
        pathDropdown.value = selectedPathIndex;
    }

    void OnDropdownValueChanged(int index)
    {
        selectedPathIndex = index;
        UpdatePathVisibility();

        string selectedName = (index == 0) ? "All paths" : targets[index - 1].name;
        Debug.Log($"경로 선택 변경: {selectedName}");
    }

    void UpdatePathVisibility()
    {
        if (selectedPathIndex == 0)
        {
            // "모든 경로" 선택 - 모든 경로 표시
            foreach (var kvp in navDataContainer)
            {
                if (kvp.Value.lineRenderer != null)
                {
                    kvp.Value.lineRenderer.enabled = kvp.Value.isValid;
                }
            }

            if (remainDts != null) remainDts.text = "-";
            if (remainTm != null) remainTm.text = "-";
        }
        else
        {
            // 특정 경로 선택 - 선택된 경로만 표시
            int targetIndex = selectedPathIndex - 1;

            // 모든 경로 숨김
            foreach (var kvp in navDataContainer)
            {
                if (kvp.Value.lineRenderer != null)
                {
                    kvp.Value.lineRenderer.enabled = false;
                }
            }

            // 선택된 타겟만 표시
            if (targetIndex >= 0 && targetIndex < targets.Count && targets[targetIndex] != null)
            {
                GameObject selectedTarget = targets[targetIndex];

                if (navDataContainer.ContainsKey(selectedTarget))
                {
                    NavData selectedNavData = navDataContainer[selectedTarget];
                    if (selectedNavData.lineRenderer != null && selectedNavData.isValid)
                    {
                        selectedNavData.lineRenderer.enabled = true;
                    }

                    // 거리와 예상 시간 업데이트
                    if (remainDts != null)
                        remainDts.text = $"{selectedNavData.distance:F1}m";

                    if (remainTm != null)
                    {
                        float estimatedTime = EstimateArrivalTime(selectedNavData.distance);
                        remainTm.text = FormatTime(estimatedTime);
                    }
                }
            }
        }
    }

    // 위치 이력 업데이트
    void UpdatePositionHistory(Vector3 newPosition)
    {
        float currentTime = Time.time;

        // 이력에 추가
        positionHistory.Enqueue(newPosition);
        timeHistory.Enqueue(currentTime);

        // 최대 크기 유지
        while (positionHistory.Count > velocityHistorySize)
        {
            positionHistory.Dequeue();
            timeHistory.Dequeue();
        }

        lastPosition = newPosition;
        lastUpdateTime = currentTime;
    }

    // 현재 속도 계산 (m/s)
    float CalculateCurrentSpeed()
    {
        if (positionHistory.Count < 2) return 0f;

        var positions = positionHistory.ToArray();
        var times = timeHistory.ToArray();

        float totalDistance = 0f;
        float totalTime = 0f;

        // 최근 몇 개 샘플의 평균 속도 계산
        for (int i = 1; i < positions.Length; i++)
        {
            float deltaTime = times[i] - times[i - 1];
            if (deltaTime > 0.01f) // 너무 작은 시간 간격 제외
            {
                float distance = Vector3.Distance(positions[i], positions[i - 1]);
                totalDistance += distance;
                totalTime += deltaTime;
            }
        }

        return totalTime > 0 ? totalDistance / totalTime : 0f;
    }

    // 속도의 신뢰성 판단
    bool IsCurrentSpeedReliable(float speed)
    {
        return speed > 0.2f && speed < 3.0f && positionHistory.Count >= 3;
    }

    // 남은 시간 예측 (초 단위)
    float EstimateArrivalTime(float distance)
    {
        if (distance <= 0) return 0f;

        float currentSpeed = CalculateCurrentSpeed();

        if (IsCurrentSpeedReliable(currentSpeed))
        {
            // 현재 속도와 기본 속도의 가중 평균 사용
            float weightedSpeed = currentSpeed * 0.7f + defaultWalkingSpeed * 0.3f;
            return distance / weightedSpeed;
        }
        else
        {
            // 현재 속도가 신뢰할 수 없으면 기본 걷기 속도 사용
            return distance / defaultWalkingSpeed;
        }
    }

    // 시간을 "분:초" 형식으로 포맷
    string FormatTime(float timeInSeconds)
    {
        if (timeInSeconds <= 0) return "0:00";
        if (float.IsInfinity(timeInSeconds) || float.IsNaN(timeInSeconds)) return "--:--";

        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);

        return $"{minutes}:{seconds:D2}";
    }

    void InitializeTargets()
    {
        // 기존 타겟들 초기화
        foreach (GameObject target in targets)
        {
            if (target != null && !navDataContainer.ContainsKey(target))
            {
                AddTarget(target);
            }
        }
    }

    // 새로운 타겟 추가 함수
    public void AddTarget(GameObject newTarget)
    {
        if (newTarget == null || navDataContainer.ContainsKey(newTarget)) return;

        NavData navData = new NavData(newTarget);

        // 새로운 LineRenderer 생성
        GameObject lineObj = new GameObject($"PathLine_{newTarget.name}");
        lineObj.transform.SetParent(this.transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.startWidth = lr.endWidth = 0.2f;
        lr.useWorldSpace = true;
        lr.numCapVertices = 5;
        lr.numCornerVertices = 5;
        lr.material = new Material(Shader.Find("Sprites/Default"));

        // 색상 할당 (순환)
        int colorIndex = navDataContainer.Count % pathColors.Length;
        lr.material.color = pathColors[colorIndex];
        lr.enabled = false;

        navData.lineRenderer = lr;
        navDataContainer[newTarget] = navData;

        if (!targets.Contains(newTarget))
        {
            targets.Add(newTarget);
        }

        // UI 업데이트
        UpdateDropdownOptions();

        Debug.Log($"타겟 추가: {newTarget.name}, 총 타겟 수: {navDataContainer.Count}");
    }

    // 타겟 제거 함수
    public void RemoveTarget(GameObject targetToRemove)
    {
        if (navDataContainer.ContainsKey(targetToRemove))
        {
            // LineRenderer 제거
            if (navDataContainer[targetToRemove].lineRenderer != null)
            {
                DestroyImmediate(navDataContainer[targetToRemove].lineRenderer.gameObject);
            }

            navDataContainer.Remove(targetToRemove);
            targets.Remove(targetToRemove);

            // 선택된 인덱스 조정
            if (selectedPathIndex > targets.Count)
            {
                selectedPathIndex = 0; // "모든 경로"로 리셋
            }

            // UI 업데이트
            UpdateDropdownOptions();
            UpdatePathVisibility();

            Debug.Log($"타겟 제거: {targetToRemove.name}, 남은 타겟 수: {navDataContainer.Count}");
        }
    }

    void OnEnable()
    {
        ros = ROSConnection.GetOrCreateInstance();
        Debug.Log("ROS connected");
        ros.Subscribe<PoseStampedMsg>(topic, OnPose);
    }

    void OnDisable()
    {
        if (ros != null) ros.Unsubscribe(topic);
        Debug.Log("ROS unsubscribed: " + topic);
    }

    void OnPose(PoseStampedMsg m)
    {
        Debug.Log($"Get message: {m.pose.position.x}, {m.pose.position.y}, {m.pose.position.z}");

        GameObject targetFireman = Fireman != null ? Fireman : this.gameObject;

        Vector3 rosPosition;

        if (ignoreRosYCoordinate)
        {
            // Y좌표 무시 - X, Z만 사용
            rosPosition = new Vector3(
                (float)m.pose.position.x * -1,
                0f, // Y는 일단 0으로 설정
                (float)m.pose.position.z * -1
            );
        }
        else
        {
            // 기존 방식 - 모든 좌표 사용
            rosPosition = new Vector3(
                (float)m.pose.position.x * -1,
                (float)m.pose.position.y,
                (float)m.pose.position.z * -1
            );
        }

        Vector3 finalPosition;

        if (spawn != null)
        {
            if (ignoreRosYCoordinate)
            {
                // spawn 위치를 기준으로 X, Z만 적용
                Vector3 basePosition = spawn.transform.position;
                finalPosition = new Vector3(
                    basePosition.x + rosPosition.x,
                    basePosition.y, // spawn의 Y를 초기값으로 사용
                    basePosition.z + rosPosition.z
                );
                Debug.Log($"Using spawn position (Y ignored): {spawn.transform.position} + ROS XZ: {rosPosition} = {finalPosition}");
            }
            else
            {
                // 기존 방식
                finalPosition = spawn.transform.position + rosPosition;
                Debug.Log($"Using spawn position: {spawn.transform.position} + ROS: {rosPosition} = {finalPosition}");
            }
        }
        else
        {
            finalPosition = rosPosition;
            Debug.Log($"No spawn set, using ROS position: {finalPosition}");
        }

        // 지면 보정 적용 (Y좌표 자동 계산)
        if (enableGroundCorrection)
        {
            finalPosition = CorrectPositionToGround(finalPosition);
        }

        // NavMesh 위치 보정도 함께 적용 (선택사항)
        if (enableNavMeshCorrection)
        {
            finalPosition = CorrectPositionToNavMesh(finalPosition);
        }

        // 위치 이력 업데이트
        UpdatePositionHistory(finalPosition);

        // 소방관 위치 및 회전 업데이트
        targetFireman.transform.position = finalPosition;
        targetFireman.transform.rotation = new Quaternion(
            (float)m.pose.orientation.x,
            (float)m.pose.orientation.y,
            (float)m.pose.orientation.z,
            (float)m.pose.orientation.w
        );

        // 모든 타겟에 대한 경로 계산
        DrawAllPaths();

        // 거리 정보가 업데이트되었으므로 드롭다운 갱신
        UpdateDropdownOptions();
    }

    // 지면 보정 함수
    Vector3 CorrectPositionToGround(Vector3 originalPosition)
    {
        Vector3 correctedPosition;

        if (useNavMeshFirst)
        {
            // NavMesh 우선 시도
            correctedPosition = GetOptimalGroundPosition(
                originalPosition, navMeshSearchRange, raycastDistance, groundLayerMask);
        }
        else
        {
            // Raycast 우선 시도
            correctedPosition = GetGroundPositionRaycast(
                originalPosition, raycastDistance, groundLayerMask);

            // Raycast 실패시 NavMesh 시도
            if (correctedPosition == originalPosition)
            {
                correctedPosition = GetNavMeshPosition(
                    originalPosition, navMeshSearchRange);
            }
        }

        // 보정이 일어났는지 확인하고 로그 출력
        float yDifference = Mathf.Abs(originalPosition.y - correctedPosition.y);
        if (yDifference > 0.1f) // 10cm 이상 차이날 때만 로그
        {
            Debug.Log($"지면 보정: Y {originalPosition.y:F2} → {correctedPosition.y:F2} (차이: {yDifference:F2}m)");
        }

        return correctedPosition;
    }

    // Raycast를 이용한 지면 감지
    Vector3 GetGroundPositionRaycast(Vector3 xzPosition, float maxRayDistance = 50f, LayerMask groundLayer = default)
    {
        // LayerMask가 설정되지 않았으면 모든 레이어 대상
        if (groundLayer == default)
            groundLayer = ~0; // 모든 레이어

        // 충분히 높은 위치에서 아래로 Ray 발사
        Vector3 rayStart = new Vector3(xzPosition.x, xzPosition.y + maxRayDistance / 2, xzPosition.z);
        Vector3 rayDirection = Vector3.down;

        if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, maxRayDistance, groundLayer))
        {
            return hit.point;
        }

        // 지면을 찾지 못한 경우 원래 Y값 유지
        Debug.LogWarning($"Raycast로 지면을 찾을 수 없음: {xzPosition}");
        return xzPosition;
    }

    // SphereCast를 이용한 더 안정적인 감지
    Vector3 GetGroundPositionSphere(Vector3 xzPosition, float sphereRadius = 0.5f, float maxRayDistance = 50f, LayerMask groundLayer = default)
    {
        if (groundLayer == default)
            groundLayer = ~0;

        Vector3 rayStart = new Vector3(xzPosition.x, xzPosition.y + maxRayDistance / 2, xzPosition.z);

        if (Physics.SphereCast(rayStart, sphereRadius, Vector3.down, out RaycastHit hit, maxRayDistance, groundLayer))
        {
            // 구체의 중심이 아닌 실제 접촉점 반환
            return hit.point + Vector3.up * sphereRadius;
        }

        Debug.LogWarning($"SphereCast로 지면을 찾을 수 없음: {xzPosition}");
        return xzPosition;
    }

    // NavMesh를 이용한 감지
    Vector3 GetNavMeshPosition(Vector3 xzPosition, float searchRange = 5f)
    {
        // NavMesh에서 가장 가까운 점 찾기
        if (NavMesh.SamplePosition(xzPosition, out NavMeshHit hit, searchRange, NavMesh.AllAreas))
        {
            return hit.position;
        }

        Debug.LogWarning($"NavMesh에서 위치를 찾을 수 없음: {xzPosition}");
        return xzPosition;
    }

    // 복합적 접근 (NavMesh + Raycast)
    Vector3 GetOptimalGroundPosition(Vector3 xzPosition, float navMeshRange = 3f, float raycastDistance = 20f, LayerMask groundLayer = default)
    {
        // 1차: NavMesh 시도 (계단/경사에서 안정적)
        if (NavMesh.SamplePosition(xzPosition, out NavMeshHit navHit, navMeshRange, NavMesh.AllAreas))
        {
            return navHit.position;
        }

        // 2차: Raycast 시도 (NavMesh가 없는 영역)
        Vector3 raycastResult = GetGroundPositionRaycast(xzPosition, raycastDistance, groundLayer);
        if (raycastResult != xzPosition) // 성공적으로 지면을 찾았다면
        {
            return raycastResult;
        }

        // 3차: 원래 위치 반환
        Debug.LogWarning($"모든 방법으로 지면을 찾을 수 없음: {xzPosition}");
        return xzPosition;
    }

    // NavMesh 위치 보정 함수
    Vector3 CorrectPositionToNavMesh(Vector3 originalPosition)
    {
        // NavMesh에서 가장 가까운 유효한 위치 찾기
        if (NavMesh.SamplePosition(originalPosition, out NavMeshHit hit, navMeshSearchRange, NavMesh.AllAreas))
        {
            Vector3 correctedPosition = hit.position;

            // 보정이 일어났는지 확인하고 로그 출력
            float distance = Vector3.Distance(originalPosition, correctedPosition);
            if (distance > 0.1f) // 10cm 이상 차이날 때만 로그
            {
                Debug.Log($"NavMesh 위치 보정: {originalPosition} → {correctedPosition} (거리: {distance:F2}m)");
            }

            return correctedPosition;
        }
        else
        {
            // NavMesh를 찾을 수 없는 경우 경고
            Debug.LogWarning($"NavMesh를 찾을 수 없습니다. 검색 범위: {navMeshSearchRange}m, 위치: {originalPosition}");
            return originalPosition; // 원래 위치 사용
        }
    }

    // Agent가 NavMesh에서 벗어났는지 확인하고 복원하는 추가 안전장치
    void Update()
    {
        if (enableNavMeshCorrection && Fireman != null)
        {
            NavMeshAgent agent = Fireman.GetComponent<NavMeshAgent>();
            if (agent != null && !agent.isOnNavMesh)
            {
                // Agent가 NavMesh에서 벗어난 경우 가장 가까운 NavMesh로 이동
                Vector3 correctedPosition = CorrectPositionToNavMesh(Fireman.transform.position);
                Fireman.transform.position = correctedPosition;

                Debug.LogWarning($"{Fireman.name}이 NavMesh에서 벗어나 {correctedPosition}으로 복원되었습니다.");
            }
        }
    }

    void DrawAllPaths()
    {
        if (Fireman == null) return;

        Vector3 firemanPos = Fireman.transform.position;

        // 각 타겟별로 경로 계산 및 그리기
        foreach (var kvp in navDataContainer)
        {
            GameObject target = kvp.Key;
            NavData navData = kvp.Value;

            if (target == null)
            {
                // null 타겟은 제거 대기열에 추가
                continue;
            }

            DrawPathToTarget(firemanPos, target, navData);
        }

        // null 타겟들 정리
        CleanupNullTargets();

        // 가시성 업데이트
        UpdatePathVisibility();
    }

    void DrawPathToTarget(Vector3 startPos, GameObject target, NavData navData)
    {
        try
        {
            Vector3 targetPos = target.transform.position;

            // NavMesh 경로 계산
            if (NavMesh.CalculatePath(startPos, targetPos, NavMesh.AllAreas, navData.path))
            {
                if (navData.path.corners.Length > 0)
                {
                    // 거리 계산 및 저장
                    navData.distance = CalculatePathDistance(navData.path.corners, startPos);
                    navData.isValid = true;

                    Debug.Log($"{target.name}까지 거리: {navData.distance:F2}m");

                    // 라인 렌더러 위치 업데이트 (가시성은 별도로 제어)
                    navData.lineRenderer.positionCount = navData.path.corners.Length;

                    for (int i = 0; i < navData.path.corners.Length; i++)
                    {
                        navData.lineRenderer.SetPosition(i, navData.path.corners[i] + offset);
                    }
                }
                else
                {
                    navData.isValid = false;
                }
            }
            else
            {
                navData.isValid = false;
            }
        }
        catch (MissingReferenceException)
        {
            navData.isValid = false;
        }
    }

    void CleanupNullTargets()
    {
        List<GameObject> toRemove = new List<GameObject>();

        foreach (var kvp in navDataContainer)
        {
            if (kvp.Key == null)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (GameObject nullTarget in toRemove)
        {
            RemoveTarget(nullTarget);
        }
    }

    float CalculatePathDistance(Vector3[] corners, Vector3 currentPosition)
    {
        if (corners.Length == 0) return 0f;

        float totalDistance = 0f;

        // 현재 위치에서 첫 번째 코너까지
        totalDistance += Vector3.Distance(currentPosition, corners[0]);

        // 각 코너 간 거리 합산
        for (int i = 0; i < corners.Length - 1; i++)
        {
            totalDistance += Vector3.Distance(corners[i], corners[i + 1]);
        }

        return totalDistance;
    }

    // 특정 타겟까지의 거리 조회
    public float GetDistanceToTarget(GameObject target)
    {
        if (navDataContainer.ContainsKey(target) && navDataContainer[target].isValid)
        {
            return navDataContainer[target].distance;
        }
        return 0f; // 유효하지 않은 경우
    }

    // 가장 가까운 타겟 찾기
    public GameObject GetClosestTarget()
    {
        GameObject closest = null;
        float minDistance = float.MaxValue;

        foreach (var kvp in navDataContainer)
        {
            if (kvp.Value.isValid && kvp.Value.distance < minDistance)
            {
                minDistance = kvp.Value.distance;
                closest = kvp.Key;
            }
        }

        return closest;
    }

    // 현재 선택된 타겟 가져오기
    public GameObject GetSelectedTarget()
    {
        if (selectedPathIndex == 0) return null; // "모든 경로" 선택됨

        int targetIndex = selectedPathIndex - 1;
        if (targetIndex >= 0 && targetIndex < targets.Count)
        {
            return targets[targetIndex];
        }
        return null;
    }

    // 외부에서 특정 타겟 선택하기
    public void SelectTargetByName(string targetName)
    {
        GameObject target = targets.Find(t => t != null && t.name == targetName);
        if (target != null)
        {
            SelectTarget(target);
        }
    }

    // 외부에서 특정 타겟 선택하기
    public void SelectTarget(GameObject target)
    {
        if (target == null) return;

        int index = targets.IndexOf(target);
        if (index >= 0 && pathDropdown != null)
        {
            pathDropdown.value = index + 1; // +1은 "모든 경로" 옵션 때문
            OnDropdownValueChanged(index + 1);
        }
    }

    // "모든 경로" 선택
    public void SelectAllPaths()
    {
        if (pathDropdown != null)
        {
            pathDropdown.value = 0;
            OnDropdownValueChanged(0);
        }
    }

    public void SaveNavigation(GameObject selectedAgent, GameObject selectedRescuee)
    {
        if (selectedAgent == null || selectedRescuee == null) return;

        // Fireman을 selectedAgent로 설정
        Fireman = selectedAgent;

        // selectedRescuee를 targets 리스트에 추가 (중복 방지)
        if (!targets.Contains(selectedRescuee))
        {
            targets.Add(selectedRescuee);
        }

        // NavData 추가
        AddTarget(selectedRescuee);

        Debug.Log($"네비게이션 저장: {selectedAgent.name} → {selectedRescuee.name}");
    }

    // Inspector에서 타겟 리스트 변경 감지 (에디터에서만)
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            // 새로 추가된 타겟들 초기화
            foreach (GameObject target in targets)
            {
                if (target != null && !navDataContainer.ContainsKey(target))
                {
                    AddTarget(target);
                }
            }
        }
    }

    void OnDestroy()
    {
        // 이벤트 정리
        if (pathDropdown != null)
            pathDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
    }
}