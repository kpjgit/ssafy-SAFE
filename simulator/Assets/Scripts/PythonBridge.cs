using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

public class PythonBridge : MonoBehaviour
{
    public enum SourceType { Webcam, VideoFile }

    [Header("Python Settings")]
    [Tooltip("파이썬 실행 파일 경로")]
    public string pythonExePath = @"C:\Users\SSAFY\AppData\Local\Programs\Python\Python311\python.exe";

    [Tooltip("yolo_pose_viewer_25d.py 스크립트 경로")]
    public string scriptPath = @"C:\Users\SSAFY\Desktop\dev_yuchan\S13P21A506\simulator\Assets\Python\yolo_pose_viewer_25d.py";

    [Header("Input Source")]
    public SourceType sourceType = SourceType.Webcam;
    public int webcamIndex = 0;
    public string videoFilePath = "";

    [Header("YOLO Options")]
    public string modelName = "yolov8s-pose.pt";
    [Range(0f, 1f)] public float conf = 0.25f;
    [Range(0f, 1f)] public float iou = 0.6f;
    public string tracker = "bytetrack.yaml";
    public string device = "";

    [Header("Streaming Options (mp4 등)")]
    [Tooltip("mp4를 스트림처럼 다루기 (FPS 기반 페이싱 + 프레임 드롭)")]
    public bool realtime = true;

    [Tooltip("지연 허용 시간 (초). 초과 시 프레임 드롭")]
    [Range(0.0f, 1.0f)] public float maxDelay = 0.25f;

    [Tooltip("FPS 강제 지정 (0이면 영상 메타데이터 사용)")]
    public float fpsOverride = 0.0f;

    private Process process;
    private Thread outputThread;
    private Thread errorThread;
    private readonly ConcurrentQueue<string> lineQueue = new ConcurrentQueue<string>();

    public event Action<string> OnLine;

    void Start()
    {
        StartPython();
    }

    void Update()
    {
        while (lineQueue.TryDequeue(out string line))
        {
            OnLine?.Invoke(line);
        }
    }

    string BuildArguments()
    {
        var ci = CultureInfo.InvariantCulture;

        // --src
        string srcArg = sourceType == SourceType.Webcam
            ? $"webcam:{webcamIndex}"
            : QuoteIfNeeded(videoFilePath);

        // 경로/공백 감싸기
        string script = QuoteIfNeeded(scriptPath);
        string model = QuoteIfNeeded(modelName);
        string trackerArg = string.IsNullOrWhiteSpace(tracker) ? "" : QuoteIfNeeded(tracker);
        string deviceArg = device?.Trim() ?? "";

        var sb = new StringBuilder();
        sb.Append(script);
        sb.Append($" --src {srcArg}");
        sb.Append($" --model {model}");
        sb.Append($" --conf {conf.ToString(ci)}");
        sb.Append($" --iou {iou.ToString(ci)}");
        if (!string.IsNullOrEmpty(trackerArg)) sb.Append($" --tracker {trackerArg}");
        if (!string.IsNullOrEmpty(deviceArg)) sb.Append($" --device {deviceArg}");

        // ✅ 스트리밍 관련 옵션 추가
        if (realtime)
            sb.Append(" --realtime");

        sb.Append($" --max_delay {maxDelay.ToString(ci)}");

        if (fpsOverride > 0.0f)
            sb.Append($" --fps_override {fpsOverride.ToString(ci)}");

        return sb.ToString();
    }

    static string QuoteIfNeeded(string token)
    {
        if (string.IsNullOrEmpty(token)) return "\"\"";
        if (token.StartsWith("\"") && token.EndsWith("\"")) return token;
        if (token.IndexOfAny(new[] { ' ', '\t', '\n', '\r' }) >= 0) return $"\"{token}\"";
        return token;
    }

    void StartPython()
    {
        StopPython();

        process = new Process();
        process.StartInfo.FileName = pythonExePath;
        process.StartInfo.Arguments = BuildArguments();
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        UnityEngine.Debug.Log("[PythonBridge] Run Args: " + process.StartInfo.Arguments);

        process.Start();

        // 표준 출력 읽기 스레드
        outputThread = new Thread(() =>
        {
            try
            {
                using (var reader = process.StandardOutput)
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineQueue.Enqueue(line);
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[PythonBridge] Output thread error: " + e);
            }
        });
        outputThread.IsBackground = true;
        outputThread.Start();

        // 표준 에러 읽기 스레드
        errorThread = new Thread(() =>
        {
            try
            {
                using (var err = process.StandardError)
                {
                    string line;
                    while ((line = err.ReadLine()) != null)
                    {
                        UnityEngine.Debug.LogError("[Python] " + line);
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[PythonBridge] Error thread error: " + e);
            }
        });
        errorThread.IsBackground = true;
        errorThread.Start();
    }

    public void RestartPython()
    {
        StartPython();
    }

    void StopPython()
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                try { process.CloseMainWindow(); } catch { }
                if (!process.WaitForExit(300))
                    process.Kill();
            }
        }
        catch { }

        try { outputThread?.Join(100); } catch { }
        try { errorThread?.Join(100); } catch { }
        process = null;
        outputThread = null;
        errorThread = null;

        while (lineQueue.TryDequeue(out _)) { }
    }

    void OnApplicationQuit()
    {
        StopPython();
    }
}
