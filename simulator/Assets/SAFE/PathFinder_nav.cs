//before

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PathFinder_nav : MonoBehaviour
{
    [Header("Target Settings")]
    public GameObject target;

    [Header("Debug Settings")]
    public bool enableDebugLogs = true;
    public bool showNavMeshStatus = true;

    private NavMeshAgent agent;
    private LineRenderer lr;
    private Vector3 firemanNextPosition;
    private Vector3 offset = new Vector3(0, 1.5f, 0);

    // 경로 업데이트 최적화를 위한 변수들
    private float lastPathUpdateTime = 0f;
    private const float pathUpdateInterval = 0.1f; // 0.1초마다 경로 업데이트

    void Start()
    {
        InitializeComponents();
        DebugNavMeshSetup();
    }

    void InitializeComponents()
    {
        // NavMeshAgent 컴포넌트 확인
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("NavMeshAgent component not found! Adding one...");
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        // LineRenderer 설정
        lr = GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = gameObject.AddComponent<LineRenderer>();
        }

        SetupLineRenderer();

        // NavMeshAgent 기본 설정
        if (agent != null)
        {
            agent.speed = 3.5f;
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            agent.stoppingDistance = 0.5f;
            agent.autoBraking = true;
        }
    }

    void SetupLineRenderer()
    {
        lr.startWidth = lr.endWidth = 0.5f;
        lr.useWorldSpace = true;
        lr.numCapVertices = 5;
        lr.numCornerVertices = 5;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.material.color = Color.blue;
        lr.enabled = false;
    }

    void DebugNavMeshSetup()
    {
        if (!enableDebugLogs) return;

        Debug.Log("=== NavMesh Debug Info ===");

        // 1. NavMeshAgent 상태 확인
        if (agent != null)
        {
            Debug.Log($"NavMeshAgent enabled: {agent.enabled}");
            Debug.Log($"NavMeshAgent isOnNavMesh: {agent.isOnNavMesh}");
            Debug.Log($"NavMeshAgent speed: {agent.speed}");
            Debug.Log($"Agent position: {transform.position}");
        }
        else
        {
            Debug.LogError("NavMeshAgent is null!");
            return;
        }

        // 2. NavMesh 존재 여부 확인
        NavMeshHit hit;
        bool hasNavMesh = NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas);
        Debug.Log($"NavMesh found near agent: {hasNavMesh}");
        if (hasNavMesh)
        {
            Debug.Log($"Closest NavMesh point: {hit.position}");
            Debug.Log($"Distance to NavMesh: {Vector3.Distance(transform.position, hit.position)}");
        }

        // 3. 타겟 확인
        if (target != null)
        {
            Debug.Log($"Target position: {target.transform.position}");
            bool targetOnNavMesh = NavMesh.SamplePosition(target.transform.position, out NavMeshHit targetHit, 10.0f, NavMesh.AllAreas);
            Debug.Log($"Target on NavMesh: {targetOnNavMesh}");

            if (targetOnNavMesh)
            {
                Debug.Log($"Target NavMesh position: {targetHit.position}");
            }
        }
        else
        {
            Debug.LogError("Target is not assigned!");
        }

        // 4. 경로 계산 테스트
        if (target != null && agent.isOnNavMesh)
        {
            NavMeshPath testPath = new NavMeshPath();
            bool pathExists = NavMesh.CalculatePath(transform.position, target.transform.position, NavMesh.AllAreas, testPath);
            Debug.Log($"Path calculation successful: {pathExists}");
            Debug.Log($"Path status: {testPath.status}");
            Debug.Log($"Path corners count: {testPath.corners.Length}");
        }
    }

    void Update()
    {
        if (showNavMeshStatus && agent != null)
        {
            // 실시간 상태 모니터링
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log($"Agent Status - HasPath: {agent.hasPath}, PathPending: {agent.pathPending}, RemainingDistance: {agent.remainingDistance:F2}");
            }
        }

        // 키보드 단축키로 네비게이션 시작
        if (Input.GetKeyDown(KeyCode.N))
        {
            Debug.Log("Starting navigation with N key...");
            MakePath();
        }

        // 키보드 단축키로 직접 네비게이션 테스트
        if (Input.GetKeyDown(KeyCode.M))
        {
            Debug.Log("Testing direct navigation with M key...");
            TestDirectNavigation();
        }
    }

    public void MakePath()
    {
        if (target == null)
        {
            Debug.LogError("Target is not assigned!");
            return;
        }

        if (agent == null)
        {
            Debug.LogError("NavMeshAgent is null!");
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError("Agent is not on NavMesh! Trying to place on NavMesh...");

            // NavMesh 위에 Agent 배치 시도
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 10.0f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                Debug.Log($"Agent moved to NavMesh position: {hit.position}");
            }
            else
            {
                Debug.LogError("Cannot place agent on NavMesh! Check if NavMesh is baked.");
                return;
            }
        }

        Debug.Log("Starting navigation...");
        lr.enabled = true;
        StartCoroutine(MakePathCoroutine());
    }

    // 즉시 이동 테스트 (디버깅용)
    public void TestDirectNavigation()
    {
        if (target == null || agent == null) return;

        Debug.Log("Testing direct navigation...");
        bool success = agent.SetDestination(target.transform.position);
        Debug.Log($"SetDestination result: {success}");

        if (success)
        {
            Debug.Log($"Destination set to: {target.transform.position}");
            Debug.Log($"Agent will move to target");
        }
    }

    // 개선된 경로 그리기 - Agent의 현재 경로를 사용
    void DrawCurrentPath()
    {
        if (agent == null)
        {
            lr.positionCount = 0;
            return;
        }

        // Case 1: Agent가 활성 경로를 가지고 있는 경우
        if (agent.hasPath && agent.path.corners.Length > 0)
        {
            NavMeshPath currentPath = agent.path;
            lr.positionCount = currentPath.corners.Length;

            for (int i = 0; i < currentPath.corners.Length; i++)
            {
                lr.SetPosition(i, currentPath.corners[i] + offset);
            }

            if (enableDebugLogs && Time.time - lastPathUpdateTime > 2f)
            {
                Debug.Log($"Drawing active path with {currentPath.corners.Length} corners");
            }
        }
        // Case 2: Agent가 경로는 없지만 여전히 목적지로 이동 중인 경우
        else if (!agent.hasPath && Vector3.Distance(transform.position, firemanNextPosition) > 0.5f)
        {
            // 현재 위치에서 목적지까지 직선으로 연결
            lr.positionCount = 2;
            lr.SetPosition(0, transform.position + offset);
            lr.SetPosition(1, firemanNextPosition + offset);

            if (enableDebugLogs && Time.time - lastPathUpdateTime > 2f)
            {
                Debug.Log($"Drawing direct line to destination (no active path)");
            }
        }
        // Case 3: 목적지에 도착했거나 Agent가 정지한 경우
        else
        {
            // 라인을 완전히 숨기지 말고 현재 위치만 표시
            lr.positionCount = 1;
            lr.SetPosition(0, transform.position + offset);
        }

        // 로깅 타이머 업데이트
        if (Time.time - lastPathUpdateTime > 2f)
        {
            lastPathUpdateTime = Time.time;
        }
    }

    IEnumerator MakePathCoroutine()
    {
        firemanNextPosition = target.transform.position;

        // 목적지 설정
        bool destinationSet = agent.SetDestination(firemanNextPosition);
        if (!destinationSet)
        {
            Debug.LogError("Failed to set destination!");
            yield break;
        }

        Debug.Log($"Destination set to: {firemanNextPosition}");
        Debug.Log($"Starting distance: {Vector3.Distance(transform.position, firemanNextPosition):F2}m");

        float timeout = 120f; // 타임아웃 시간 늘림
        float timer = 0f;
        float logTimer = 0f;

        // 개선된 도달 조건 체크
        while (!HasReachedDestination())
        {
            timer += Time.deltaTime;
            logTimer += Time.deltaTime;

            // 타임아웃 체크
            if (timer > timeout)
            {
                Debug.LogError($"Navigation timeout! Current distance: {Vector3.Distance(transform.position, firemanNextPosition):F2}m");
                Debug.LogError($"Agent status - HasPath: {agent.hasPath}, PathPending: {agent.pathPending}, RemainingDistance: {agent.remainingDistance:F2}");
                break;
            }

            // 에이전트가 멈춘 경우 체크
            if (agent.velocity.magnitude < 0.1f && agent.hasPath && !agent.pathPending)
            {
                if (logTimer > 1f) // 1초마다 한번씩만 로깅
                {
                    Debug.LogWarning($"Agent seems stuck. Velocity: {agent.velocity.magnitude:F3}, RemainingDistance: {agent.remainingDistance:F2}");
                    logTimer = 0f;
                }
            }

            // 경로 그리기 - 최적화된 버전
            if (Time.time - lastPathUpdateTime > pathUpdateInterval)
            {
                DrawCurrentPath();
                lastPathUpdateTime = Time.time;
            }

            // 진행 상황 로깅 (5초마다)
            if (enableDebugLogs && Mathf.FloorToInt(timer) % 5 == 0 && timer > 0 && logTimer > 4.9f)
            {
                float remainingDistance = Vector3.Distance(transform.position, firemanNextPosition);
                Debug.Log($"Navigation progress - Direct: {remainingDistance:F2}m, Agent: {agent.remainingDistance:F2}m, Velocity: {agent.velocity.magnitude:F2}, HasPath: {agent.hasPath}, Time: {timer:F1}s");
                logTimer = 0f;
            }

            yield return null;
        }

        Debug.Log($"Navigation completed! Final direct distance: {Vector3.Distance(transform.position, firemanNextPosition):F2}m");
        Debug.Log($"Final agent status - HasPath: {agent.hasPath}, RemainingDistance: {agent.remainingDistance:F2}m, Velocity: {agent.velocity.magnitude:F3}");
        lr.enabled = false;
    }

    // 목적지 도달 여부를 더 정확하게 체크
    private bool HasReachedDestination()
    {
        if (agent == null) return true;

        // 직선 거리 우선 체크 (가장 신뢰할 수 있는 방법)
        float directDistance = Vector3.Distance(transform.position, firemanNextPosition);

        // 직선 거리가 충분히 가까우면 도달한 것으로 판단
        if (directDistance < 0.5f)
        {
            if (enableDebugLogs)
                Debug.Log($"Reached destination by direct distance: {directDistance:F2}m");
            return true;
        }

        // Agent가 경로를 잃었거나 더 이상 움직이지 않는 경우 체크
        if (!agent.hasPath && !agent.pathPending)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"Agent has no path. Direct distance: {directDistance:F2}m");

            // 경로가 없지만 목적지와 가까우면 성공으로 간주
            if (directDistance < 2.0f)
            {
                Debug.Log("Close enough to destination despite no path");
                return true;
            }

            // 경로가 없고 거리도 멀면 경로 재계산 시도
            Debug.LogWarning("Attempting to recalculate path...");
            agent.SetDestination(firemanNextPosition);
            return false;
        }

        // Agent가 오랫동안 정지해 있는 경우 (속도가 매우 낮음)
        if (agent.velocity.magnitude < 0.01f && agent.hasPath)
        {
            // 정지 시간을 측정하기 위해 정적 변수 사용 (실제로는 클래스 멤버 변수로 만드는 것이 좋음)
            if (!hasStuckTimer)
            {
                stuckStartTime = Time.time;
                hasStuckTimer = true;
            }
            else if (Time.time - stuckStartTime > 3.0f) // 3초 이상 정지
            {
                Debug.LogWarning($"Agent stuck for 3+ seconds. Distance: {directDistance:F2}m");
                hasStuckTimer = false;

                // 가까우면 성공으로 간주, 멀면 경로 재계산
                if (directDistance < 2.0f)
                {
                    Debug.Log("Close enough despite being stuck");
                    return true;
                }
                else
                {
                    Debug.LogWarning("Recalculating path due to stuck agent");
                    agent.SetDestination(firemanNextPosition);
                }
            }
        }
        else
        {
            hasStuckTimer = false; // 움직이고 있으면 타이머 리셋
        }

        return false;
    }

    // 정지 상태 추적을 위한 변수들
    private bool hasStuckTimer = false;
    private float stuckStartTime = 0f;

    // GUI에서 버튼으로 테스트할 수 있도록
    void OnGUI()
    {
        if (!enableDebugLogs) return;

        GUILayout.BeginArea(new Rect(10, 10, 250, 200));
        GUILayout.Label("Navigation Debug");

        if (GUILayout.Button("Start Navigation"))
        {
            MakePath();
        }

        if (GUILayout.Button("Test Direct Navigation"))
        {
            TestDirectNavigation();
        }

        if (GUILayout.Button("Debug NavMesh Info"))
        {
            DebugNavMeshSetup();
        }

        if (GUILayout.Button("Stop Navigation"))
        {
            StopAllCoroutines();
            if (agent != null) agent.ResetPath();
            lr.enabled = false;
        }

        // 상태 정보 표시
        if (agent != null)
        {
            GUILayout.Label($"On NavMesh: {agent.isOnNavMesh}");
            GUILayout.Label($"Has Path: {agent.hasPath}");
            GUILayout.Label($"Velocity: {agent.velocity.magnitude:F2}");
            GUILayout.Label($"Remaining: {agent.remainingDistance:F2}");
            if (target != null)
            {
                GUILayout.Label($"Distance: {Vector3.Distance(transform.position, target.transform.position):F2}m");
            }
        }

        GUILayout.EndArea();
    }
}