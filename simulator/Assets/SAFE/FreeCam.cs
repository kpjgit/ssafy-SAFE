using UnityEngine;

public class FreeCam : MonoBehaviour
{
    [Header("UI Settings")]
    public GameObject uiPanel; // Inspector에서 UI Panel을 드래그해서 연결

    [Header("Movement Settings")]
    public float moveSpeed = 1f;
    public float fastMoveSpeed = 2;
    public float slowMoveSpeed = 0.11f;

    [Header("Mouse Settings")]
    public float mouseSensitivity = 100f;
    public bool invertY = false;

    [Header("Zoom Settings")]
    public float zoomSpeed = 2f;
    public float minFOV = 10f;
    public float maxFOV = 120f;

    private Camera cam;
    private float xRotation = 0f;
    private float yRotation = 0f;
    private bool isCursorLocked = true;

    void Start()
    {
        cam = GetComponent<Camera>();

        // 초기 회전값 설정
        Vector3 rot = transform.localRotation.eulerAngles;
        xRotation = rot.x;
        yRotation = rot.y;

        isCursorLocked = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // UI 숨김
        if (uiPanel != null)
            uiPanel.SetActive(false);


    }

    void Update()
    {
        HandleInput();
        HandleMovement();
        HandleMouseLook();
        HandleZoom();
    }

    void HandleInput()
    {
        // F1키로 관전모드 (커서 숨김, UI 숨김)
        if (Input.GetKeyDown(KeyCode.F1))
        {
            isCursorLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // UI 숨김
            if (uiPanel != null)
                uiPanel.SetActive(false);
        }

        // F2키로 UI모드 (커서 보임, UI 보임, 카메라 움직임 정지)
        if (Input.GetKeyDown(KeyCode.F2))
        {
            isCursorLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // UI 보임
            if (uiPanel != null)
                uiPanel.SetActive(true);
        }
    }

    void HandleMovement()
    {
        if (!isCursorLocked) return;

        // 현재 이동 속도 결정
        float currentSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
            currentSpeed = fastMoveSpeed;
        else if (Input.GetKey(KeyCode.LeftControl))
            currentSpeed = slowMoveSpeed;

        // 입력 받기
        float horizontal = Input.GetAxis("Horizontal"); // A, D
        float vertical = Input.GetAxis("Vertical");     // W, S
        float upDown = 0f;

        // Q, E키로 상하 이동
        if (Input.GetKey(KeyCode.Q))
            upDown = -1f;
        else if (Input.GetKey(KeyCode.E))
            upDown = 1f;

        // 이동 벡터 계산
        Vector3 moveDirection = transform.right * horizontal +
                               transform.forward * vertical +
                               transform.up * upDown;

        // 이동 적용
        transform.position += moveDirection * currentSpeed * Time.deltaTime;
    }

    void HandleMouseLook()
    {
        if (!isCursorLocked) return;

        // 마우스 입력
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Y축 반전 옵션
        if (invertY)
            mouseY = -mouseY;

        // 회전 계산
        yRotation += mouseX;
        xRotation -= mouseY;

        // 상하 회전 제한 (위아래 90도)
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // 회전 적용
        transform.localRotation = Quaternion.Euler(xRotation, yRotation, 0f);
    }

    void HandleZoom()
    {
        if (!isCursorLocked) return;

        // 마우스 휠로 줌
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            float newFOV = cam.fieldOfView - scroll * zoomSpeed * 10f;
            cam.fieldOfView = Mathf.Clamp(newFOV, minFOV, maxFOV);
        }
    }

    void ToggleCursorLock()
    {
        isCursorLocked = !isCursorLocked;

        if (isCursorLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // 외부에서 카메라 위치/회전 설정
    public void SetPosition(Vector3 position)
    {
        transform.position = position;
    }

    public void SetRotation(Vector3 rotation)
    {
        xRotation = rotation.x;
        yRotation = rotation.y;
        transform.localRotation = Quaternion.Euler(xRotation, yRotation, 0f);
    }

    public void ResetZoom()
    {
        cam.fieldOfView = 60f; // 기본 FOV로 리셋
    }
}