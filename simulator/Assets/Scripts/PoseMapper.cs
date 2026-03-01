using System;
using UnityEngine;
#if USING_ANIM_RIGGING
using UnityEngine.Animations.Rigging;
#endif

public class PoseMapper : MonoBehaviour
{
    [Header("Input (per frame from PoseManager)")]
    private Vector2[] kp2D;
    private float[] kpConf;
    private Rect bboxPx;
    private int imgW, imgH;

    [Header("Scene / Camera")]
    public UnityEngine.Camera cam;
    public float floorY = 0f;

    [Header("Depth Approximation (bbox → Z)")]
    public float avgHumanHeightM = 1.65f;
    public float fTimesH = 1100f;
    [Range(0f, 1f)] public float offCenterCosFix = 1f;

    [Header("Root Placement / Rotation")]
    public float hipHeightM = 1.0f;
    public bool useHipKeypointForXY = true;
    [Range(0f, 2f)] public float yawGain = 1.0f;
    [Range(0f, 1.5f)] public float pitchGain = 0.6f;
    [Range(0f, 1.5f)] public float rollGain = 1.0f;

    [Header("Axis Fix (Pivot-based reflection)")]
    public bool mirrorImageX = false;
    public bool flipX = false;
    public bool flipY = false;
    public bool flipZ = false;

    [Header("Arms/Legs IK targets")]
    public Transform lHandTarget; public Transform lElbowHint;
    public Transform rHandTarget; public Transform rElbowHint;
    public Transform lFootTarget; public Transform lKneeHint;
    public Transform rFootTarget; public Transform rKneeHint;

    [Header("Forward z-bias (m)")]
    public float wristForwardBiasM = 0.25f;
    public float elbowForwardBiasM = 0.12f;
    public float kneeForwardBiasM = 0.05f;
    public float ankleForwardBiasM = 0.03f;

    [Header("Smoothing (EMA α = keep more past)")]
    [Range(0f, 0.99f)] public float ema2D = 0.7f;
    [Range(0f, 0.99f)] public float emaRootPos = 0.85f;
    [Range(0f, 0.99f)] public float emaRootRot = 0.85f;
    [Range(0f, 0.99f)] public float emaTargets = 0.6f;

    [Header("Confidence Filtering")]
    [Range(0f, 1f)] public float keypointThreshold = 0.30f;

    [Header("Optional")]
    public Animator animator;

    // --- Internal state ---
    private Vector2[] kp2D_ema = new Vector2[17];
    private bool hasPrev2D = false;
    private Vector3 rootPosEMA;
    private Quaternion rootRotEMA = Quaternion.identity;

    private Transform orientationPivot;
    private bool didInitNeutralPose = false;

    // COCO17 index
    private const int NOSE = 0, L_EYE = 1, R_EYE = 2, L_EAR = 3, R_EAR = 4,
                      L_SH = 5, R_SH = 6, L_EL = 7, R_EL = 8, L_WR = 9, R_WR = 10,
                      L_HIP = 11, R_HIP = 12, L_KNEE = 13, R_KNEE = 14, L_ANK = 15, R_ANK = 16;

    void Reset()
    {
        cam = GameObject.Find("CCTV").GetComponent<Camera>();
        animator = GetComponentInChildren<Animator>();
    }

    void Awake()
    {
        if (cam == null) cam = GameObject.Find("CCTV").GetComponent<Camera>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        EnsureOrientationPivot();
    }

    void EnsureOrientationPivot()
    {
        if (transform.parent != null && transform.parent.name == "__PoseOrientPivot")
        {
            orientationPivot = transform.parent;
            return;
        }
        var pivotGO = new GameObject("__PoseOrientPivot");
        orientationPivot = pivotGO.transform;
        orientationPivot.position = transform.position;
        orientationPivot.rotation = transform.rotation;
        orientationPivot.localScale = Vector3.one;
        transform.SetParent(orientationPivot, worldPositionStays: true);
    }

    void OnValidate()
    {
        if (orientationPivot != null) ApplyPivotReflection();
    }

    void ApplyPivotReflection()
    {
        if (orientationPivot == null) return;
        orientationPivot.localScale = new Vector3(
            flipX ? -1f : 1f,
            flipY ? -1f : 1f,
            flipZ ? -1f : 1f
        );
    }

    public void SetDetection(Vector2[] keypoints, float[] conf, Rect bbox, int imageW, int imageH)
    {
        if (keypoints == null || keypoints.Length < 17) return;
        imgW = imageW; imgH = imageH;

        if (mirrorImageX)
        {
            var mirrored = new Vector2[keypoints.Length];
            for (int i = 0; i < keypoints.Length; i++)
                mirrored[i] = new Vector2(imgW - keypoints[i].x, keypoints[i].y);
            kp2D = mirrored;
        }
        else kp2D = keypoints;

        kpConf = conf ?? MakeOnes(17);
        bboxPx = bbox;

        if (!hasPrev2D)
        {
            for (int i = 0; i < 17; i++) kp2D_ema[i] = kp2D[i];
            hasPrev2D = true;
        }
        for (int i = 0; i < 17; i++) kp2D_ema[i] = Ema(kp2D_ema[i], kp2D[i], ema2D);
    }

    void LateUpdate()
    {
        ApplyPivotReflection();

        if (!didInitNeutralPose)
        {
            Quaternion fixZ180 = Quaternion.AngleAxis(180f, Vector3.forward);
            rootRotEMA = fixZ180 * rootRotEMA;
            //SnapTargetsToBonesOnce();
            SetDefaultLimb();
            didInitNeutralPose = true;
        }

        if (kp2D == null || cam == null) return;

        float Z = EstimateDepth(out _);
        Vector3 rootPos = EstimateRootXY(Z);
        Quaternion rootRot = EstimateRootRotation();

        rootPosEMA = Vector3.Lerp(rootPosEMA, rootPos, 1f - emaRootPos);
        rootRotEMA = Quaternion.Slerp(rootRotEMA, rootRot, 1f - emaRootRot);

        float cameraPitch = cam.transform.eulerAngles.x;
        Quaternion pitchCompensation = Quaternion.AngleAxis(-cameraPitch, Vector3.right);
        //rootRotEMA = pitchCompensation * rootRotEMA;

        transform.position = new Vector3(rootPosEMA.x, floorY + hipHeightM, rootPosEMA.z);
        transform.rotation = rootRotEMA;

        UpdateLimbTargets(Z);
    }

    void SetDefaultLimb()
    {
        if (lHandTarget)
        {
            lHandTarget.position = new Vector3(-0.441966027f, 1.06797504f, 0.123494357f);
            lHandTarget.rotation = Quaternion.Euler(356.071777f, 53.1190453f, 129.146744f);
        }
        if (lElbowHint)
        {
            lElbowHint.position = new Vector3(-0.316898376f, 1.18650174f, 0.0120999925f);
            lElbowHint.rotation = Quaternion.Euler(331.224182f, 38.8026886f, 131.702408f);
        }
        if (rHandTarget)
        {
            rHandTarget.position = new Vector3(0.441965908f, 1.06797051f, 0.123493999f);
            rHandTarget.rotation = Quaternion.Euler(3.92821717f, 126.880959f, 309.146729f);
        }
        if (rElbowHint)
        {
            rElbowHint.position = new Vector3(0.3838f, 1.18649995f, 0.0120999999f);
            rElbowHint.rotation = Quaternion.Euler(28.7758121f, 141.197327f, 311.702423f);
        }
        if (lFootTarget)
        {
            lFootTarget.position = new Vector3(-0.115437262f, 1.021133f, -0.0598617345f);
            lFootTarget.rotation = Quaternion.Euler(0.00000181696487f, 174.193527f, 180f);
        }
        if (lKneeHint)
        {
            lKneeHint.position = new Vector3(-0.100555509f, 0.510999858f, 0.0735310987f);
            lKneeHint.rotation = Quaternion.Euler(355.801666f, 179.850586f, 182.040024f);
        }
        if (rFootTarget)
        {
            rFootTarget.position = new Vector3(0.115436986f, 1.021133f, -0.0598617904f);
            rFootTarget.rotation = Quaternion.Euler(0.000000201134711f, 5.80648184f, -0.000000278652408f);
        }
        if (rKneeHint)
        {
            rKneeHint.position = new Vector3(0.101000004f, 0.510999978f, 0.0170000009f);
            rKneeHint.rotation = Quaternion.Euler(4.19833851f, 0.149411112f, 2.04001904f);
        }
    }

    // =========== Core Estimation ===========
    float EstimateDepth(out float cosFix)
    {
        float h = Mathf.Max(1f, bboxPx.height);
        float Z = fTimesH / h;

        float cx = bboxPx.center.x;
        float nx = (cx / imgW - 0.5f) * 2f;
        float hfov = HorizontalFOVDeg(cam.fieldOfView, cam.aspect);
        float theta = Mathf.Abs(nx) * (hfov * 0.5f) * Mathf.Deg2Rad;
        float c = Mathf.Cos(theta);
        cosFix = Mathf.Lerp(1f, c, offCenterCosFix);
        Z /= Mathf.Max(0.3f, cosFix);
        return Z;
    }

    Vector3 EstimateRootXY(float Z)
    {
        bool hipsOk = kpConf[L_HIP] >= keypointThreshold && kpConf[R_HIP] >= keypointThreshold;
        Vector2 px = useHipKeypointForXY && hipsOk
            ? 0.5f * (kp2D_ema[L_HIP] + kp2D_ema[R_HIP])
            : bboxPx.center;

        var ray = cam.ScreenPointToRay(PixelToScreen(px));
        Vector3 world = ray.origin + ray.direction.normalized * Z;
        world.y = floorY + hipHeightM;
        return world;
    }

    Quaternion EstimateRootRotation()
    {
        // 2D 키포인트
        Vector2 shL = kp2D_ema[L_SH], shR = kp2D_ema[R_SH];
        Vector2 hipL = kp2D_ema[L_HIP], hipR = kp2D_ema[R_HIP];

        // 신뢰도 체크 (어깨/엉덩이)
        if (kpConf[L_SH] < keypointThreshold || kpConf[R_SH] < keypointThreshold ||
            kpConf[L_HIP] < keypointThreshold || kpConf[R_HIP] < keypointThreshold)
        {
            return rootRotEMA; // 유지
        }

        // 화면 → 월드(고정 깊이) 투영
        Vector3 shLW = ScreenToWorld(shL);
        Vector3 shRW = ScreenToWorld(shR);
        Vector3 hipLW = ScreenToWorld(hipL);
        Vector3 hipRW = ScreenToWorld(hipR);

        // 몸 축 (up은 이후 절대 재계산하지 않음)
        Vector3 right = (shRW - shLW).normalized;                                  // X: 어깨 좌→우
        Vector3 up = ((hipLW + hipRW) * 0.5f - (shLW + shRW) * 0.5f).normalized; // Y: 어깨중심→엉덩이중심
        Vector3 forward = Vector3.Cross(right, up).normalized;                        // Z: 전방

        // 앞/뒤 판별
        bool frontLikely = kpConf[NOSE] >= keypointThreshold ||
                        (kpConf[L_EYE] >= keypointThreshold && kpConf[R_EYE] >= keypointThreshold);

        bool backLikely = !frontLikely &&
                        (kpConf[L_EAR] >= keypointThreshold && kpConf[R_EAR] >= keypointThreshold);

        if (backLikely)
        {
            forward = -forward; // 뒤면 전방 반전
        }
        else if (frontLikely)
        {
            // -------- yaw만 얼굴에서 추정해 forward에만 반영 --------
            bool hasBothEyes = kpConf[L_EYE] >= keypointThreshold && kpConf[R_EYE] >= keypointThreshold;
            bool hasNose = kpConf[NOSE] >= keypointThreshold;

            if (hasBothEyes && hasNose)
            {
                Vector2 eL = kp2D_ema[L_EYE];
                Vector2 eR = kp2D_ema[R_EYE];
                Vector2 nose = kp2D_ema[NOSE];

                // 눈 중앙과 눈 간격(픽셀)
                Vector2 eyeMid = 0.5f * (eL + eR);
                float eyeDist = Mathf.Max(1e-5f, Vector2.Distance(eL, eR));

                // 코가 눈 중앙 기준으로 좌/우로 얼마나 치우쳤는지 → 정규화
                float yawNorm = Mathf.Clamp((nose.x - eyeMid.x) / eyeDist, -1f, 1f);

                // 스케일/리미트: 환경에 맞게 조정 (예: 최대 ±45°)
                const float maxYawFromFaceDeg = 120f;
                float yawDeg = yawNorm * maxYawFromFaceDeg * Mathf.Clamp01(yawGain);

                // up 축 기준 회전만 forward에 적용 (up은 그대로 유지)
                Quaternion qYaw = Quaternion.AngleAxis(yawDeg, up);
                forward = (qYaw * forward).normalized;
            }
            // 눈/코 부족하면 얼굴 보정 생략(몸통만 사용)
        }

        if (cam != null)
        {
            Vector3 camFwd = cam.transform.forward;
            Vector3 camRight = cam.transform.right;
            Vector3 camFwdHoriz = new Vector3(camFwd.x, 0f, camFwd.z);
            if (camFwdHoriz.sqrMagnitude > 1e-8f)
            {
                camFwdHoriz.Normalize();
                float camPitchDeg = Vector3.SignedAngle(camFwdHoriz, camFwd, camRight);
                Quaternion pitchComp = Quaternion.AngleAxis(-camPitchDeg, camRight);

                // forward와 up 모두 동일한 보정 적용
                forward = (pitchComp * forward).normalized;
                up = (pitchComp * up).normalized;
            }
        }
        Quaternion targetRot = Quaternion.LookRotation(forward, up);

        // 부드럽게 회전 (초당 deg 제한)
        float maxRotSpeed = 200f; // deg/s, 원하는 만큼 조절
        Quaternion smoothRot = Quaternion.RotateTowards(rootRotEMA, targetRot, maxRotSpeed * Time.deltaTime);

        return smoothRot;
        // up은 재계산/보정하지 않음
        // return Quaternion.LookRotation(forward, up);
    }

    // 보조 함수: 2D 픽셀 → 월드 좌표
    Vector3 ScreenToWorld(Vector2 px)
    {
        var ray = cam.ScreenPointToRay(PixelToScreen(px));
        // 대략적인 깊이는 EstimateDepth 로 구해도 되고,
        // 여기서는 멀리 나가도록 normalize 후 hipHeight 에 맞춰줌
        return ray.origin + ray.direction.normalized * 5f; // 깊이 추정은 root 위치에 의해 보정됨
    }

    void UpdateLimbTargets(float depthRoot)
    {
        if (!cam) return;

        Vector2 chest2D = 0.5f * (kp2D_ema[L_SH] + kp2D_ema[R_SH]);
        float diag = Mathf.Sqrt(imgW * imgW + imgH * imgH);

        float WristBias(Vector2 wrist)
        {
            float d = Vector2.Distance(wrist, chest2D);
            float t = 1f - Mathf.Clamp01(d / (0.35f * diag));
            return wristForwardBiasM * t;
        }
        float ElbowBias(float wb) => Mathf.Lerp(0f, elbowForwardBiasM, Mathf.Clamp01(wb / Mathf.Max(1e-6f, wristForwardBiasM)));

        Vector3 Project(Vector2 px, float zBiasM)
        {
            var ray = cam.ScreenPointToRay(PixelToScreen(px));
            Vector3 world = ray.origin + ray.direction.normalized * (depthRoot + zBiasM);
            world.y = Mathf.Max(floorY, world.y);
            return world;
        }

        // Left arm
        if (lHandTarget && lElbowHint &&
            kpConf[L_WR] >= keypointThreshold && kpConf[L_EL] >= keypointThreshold)
        {
            float wb = WristBias(kp2D_ema[L_WR]);
            var hand = Ema(lHandTarget.position, Project(kp2D_ema[L_WR], wb), emaTargets);
            var elbow = Ema(lElbowHint.position, Project(kp2D_ema[L_EL], ElbowBias(wb)), emaTargets);
            lHandTarget.position = hand; lElbowHint.position = elbow;
        }

        // Right arm
        if (rHandTarget && rElbowHint &&
            kpConf[R_WR] >= keypointThreshold && kpConf[R_EL] >= keypointThreshold)
        {
            float wb = WristBias(kp2D_ema[R_WR]);
            var hand = Ema(rHandTarget.position, Project(kp2D_ema[R_WR], wb), emaTargets);
            var elbow = Ema(rElbowHint.position, Project(kp2D_ema[R_EL], ElbowBias(wb)), emaTargets);
            rHandTarget.position = hand; rElbowHint.position = elbow;
        }

        // Left leg
        if (lFootTarget && lKneeHint &&
            kpConf[L_ANK] >= keypointThreshold && kpConf[L_KNEE] >= keypointThreshold)
        {
            var foot = Ema(lFootTarget.position, Project(kp2D_ema[L_ANK], ankleForwardBiasM), emaTargets);
            var knee = Ema(lKneeHint.position, Project(kp2D_ema[L_KNEE], kneeForwardBiasM), emaTargets);
            foot.y = floorY;
            lFootTarget.position = foot; lKneeHint.position = knee;
        }

        // Right leg
        if (rFootTarget && rKneeHint &&
            kpConf[R_ANK] >= keypointThreshold && kpConf[R_KNEE] >= keypointThreshold)
        {
            var foot = Ema(rFootTarget.position, Project(kp2D_ema[R_ANK], ankleForwardBiasM), emaTargets);
            var knee = Ema(rKneeHint.position, Project(kp2D_ema[R_KNEE], kneeForwardBiasM), emaTargets);
            foot.y = floorY;
            rFootTarget.position = foot; rKneeHint.position = knee;
        }
    }

    // =========== Utils ===========
    static Vector2 Ema(Vector2 prev, Vector2 cur, float a) => a * prev + (1f - a) * cur;
    static Vector3 Ema(Vector3 prev, Vector3 cur, float a) => a * prev + (1f - a) * cur;

    Vector2 PixelToScreen(Vector2 px) => new Vector2(px.x, imgH - px.y);

    static float HorizontalFOVDeg(float verticalFOVDeg, float aspect)
    {
        float v = verticalFOVDeg * Mathf.Deg2Rad;
        float h = 2f * Mathf.Atan(Mathf.Tan(v * 0.5f) * aspect);
        return h * Mathf.Rad2Deg;
    }

    static float[] MakeOnes(int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = 1f;
        return a;
    }
}
