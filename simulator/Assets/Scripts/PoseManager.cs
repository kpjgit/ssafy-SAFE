using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

// ===== Strong types for NDJSON =====
[Serializable]
public class Pose2DFrame {
    public string type;
    public int seq;
    public int img_w, img_h;
    public List<Pose2DDet> detections;
}
[Serializable]
public class Pose2DDet {
    public int track_id;
    public float score;
    public float[] bbox_xyxy;   // [x1,y1,x2,y2]
    public float[][] kps2d;     // [17][3] -> x,y,conf
}

public class PoseManager : MonoBehaviour
{
    [Header("AgentManager")]
    public AgentManager AgentManage;

    [Header("Refs")]
    public PythonBridge pythonBridge;
    public GameObject actorPrefab;

    [Header("Lifecycle")]
    [Tooltip("미검출 후 제거까지 대기(초)")]
    public float actorLifetime = 2.0f;

    [Header("Filtering")]
    [Tooltip("이 값 미만의 detection(score)은 생성/업데이트하지 않음")]
    [Range(0f, 1f)] public float detThreshold = 0.30f;

    // track id -> PoseMapper
    private readonly Dictionary<int, PoseMapper> actors = new();
    private readonly Dictionary<int, float> lastSeenTime = new();

    void Start()
    {
        if (pythonBridge == null) {
            Debug.LogError("[PoseManager] PythonBridge not assigned.");
            enabled = false;
            return;
        }
        if (actorPrefab == null) {
            Debug.LogError("[PoseManager] actorPrefab not assigned.");
            enabled = false;
            return;
        }
        if (actorPrefab.GetComponent<PoseMapper>() == null) {
            Debug.LogError("[PoseManager] actorPrefab must have PoseMapper component.");
            enabled = false;
            return;
        }

        pythonBridge.OnLine += OnNdjsonLine;
    }

    void Update()
    {
        // 오래된 actor 제거
        var toRemove = new List<int>();
        foreach (var kv in lastSeenTime)
            if (Time.time - kv.Value > actorLifetime) toRemove.Add(kv.Key);

        foreach (var id in toRemove)
        {
            if (actors.TryGetValue(id, out var mapper) && mapper != null)
            {
                // AgentManager에서 먼저 제거 (GameObject 참조가 필요하므로 Destroy 전에 호출)
                if (AgentManage != null)
                {
                    AgentManage.RemoveRescuee(mapper.gameObject);
                }

                Destroy(mapper.gameObject);
            }

            actors.Remove(id);
            lastSeenTime.Remove(id);

            Debug.Log($"[PoseManager] Removed actor id={id} (timeout).");
        }
    }

    private void OnNdjsonLine(string line)
    {
        Pose2DFrame frame = null;
        try {
            frame = JsonConvert.DeserializeObject<Pose2DFrame>(line);
        } catch (Exception ex) {
            Debug.LogWarning($"[PoseManager] JSON parse error: {ex.Message}");
            return;
        }
        if (frame == null || frame.type != "pose2d_frame") return;

        int imgW = frame.img_w;
        int imgH = frame.img_h;
        var dets = frame.detections ?? new List<Pose2DDet>();

        #if UNITY_EDITOR
        if (dets.Count > 0) {
            var idsStr = string.Join(",", dets.ConvertAll(d=>d.track_id.ToString()));
            Debug.Log($"[PoseManager] seq={frame.seq} detections={dets.Count} ids=[{idsStr}]");
        }
        #endif

        foreach (var det in dets)
        {
            if (det == null) continue;

            // 1) bbox score 필터
            if (det.score < detThreshold) continue;

            // 2) keypoint 품질 검사
            int validKp = 0;
            int totalKp = det.kps2d.Length;
            foreach (var kp in det.kps2d)
            {
                if (kp.Length > 2 && kp[2] >= 0.3f) validKp++;
            }
            float kpRatio = (float)validKp / Mathf.Max(1, totalKp);

            // 최소한 0.25(=25%) 이상의 keypoint가 검출되지 않으면 무시
            if (kpRatio < 0.25f)
            {
                Debug.Log($"[PoseManager] Skipped id={det.track_id} (low keypoint ratio: {kpRatio:F2})");
                continue;
            }

            // 3) (선택) 결합 점수 활용
            float finalScore = det.score * kpRatio;
            if (finalScore < detThreshold)
            {
                Debug.Log($"[PoseManager] Skipped id={det.track_id} (finalScore={finalScore:F2})");
                continue;
            }

            if (det.bbox_xyxy == null || det.bbox_xyxy.Length < 4) continue;
            if (det.kps2d == null || det.kps2d.Length < 17) continue;

            int id = det.track_id;

            // vector 변환
            var bbox = det.bbox_xyxy;
            var rect = new Rect(bbox[0], bbox[1], bbox[2]-bbox[0], bbox[3]-bbox[1]);

            var kps2d = det.kps2d;
            var keypoints = new Vector2[kps2d.Length];
            var conf = new float[kps2d.Length];
            for (int i = 0; i < kps2d.Length; i++) {
                var row = kps2d[i];
                float x = row.Length > 0 ? row[0] : 0f;
                float y = row.Length > 1 ? row[1] : 0f;
                float c = row.Length > 2 ? row[2] : 0f;
                keypoints[i] = new Vector2(x, y);
                conf[i] = c;
            }

            bool isNewActor = !actors.TryGetValue(id, out var mapper) || mapper == null;

            if (isNewActor)
            {
                var go = Instantiate(actorPrefab);
                go.name = $"Actor_{id}";
                mapper = go.GetComponent<PoseMapper>();
                if (mapper == null)
                {
                    Debug.LogError("[PoseManager] Spawned actor has no PoseMapper. Destroying.");
                    Destroy(go);
                    continue;
                }
                actors[id] = mapper;

                // AgentManager에 새로운 Rescuee로 추가
                if (AgentManage != null)
                {
                    AgentManage.AddRescuee(go);
                }

                Debug.Log($"[PoseManager] Spawned actor id={id}");
            }

            // 업데이트
            try
            {
                mapper.SetDetection(keypoints, conf, rect, imgW, imgH);
                lastSeenTime[id] = Time.time; // 업데이트된 경우에만 생존 갱신
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoseManager] mapper.SetDetection error (id={id}): {ex.Message}");
            }
        }
    }
}
