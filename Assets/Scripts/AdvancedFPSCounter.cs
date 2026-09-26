using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

public class AdvancedFPSCounter : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Ссылка на TextMeshProUGUI для отображения FPS")]
    public TextMeshProUGUI fpsText;

    [Header("Настройки")]
    [Tooltip("Частота обновления текста (сек)")]
    public float updateInterval = 0.5f;

    [Tooltip("Как часто писать в консоль (сек)")]
    public float logInterval = 3f;

    [Tooltip("Порог в мс — системы медленнее этого будут логироваться")]
    public float heavyThresholdMs = 2f;

    [Tooltip("Писать ли в консоль автоматически")]
    public bool autoLog = true;

    // Внутренние
    private float _accum;
    private int _frames;
    private float _updateTimer;
    private float _logTimer;

    private class MarkerInfo
    {
        public string Name;
        public ProfilerRecorder Recorder;
    }

    private readonly List<MarkerInfo> _markers = new List<MarkerInfo>();
    private readonly List<(string name, double ms)> _sorted = new List<(string, double)>();
    private readonly StringBuilder _sb = new StringBuilder();

    // Имена стандартных маркеров Unity (Internal категория)
    private static readonly string[] MarkerNames = new string[]
    {
        "PlayerLoop",
        "Update.ScriptRunBehaviourUpdate",
        "BehaviourUpdate",
        "FixedUpdate.ScriptRunBehaviourFixedUpdate",
        "FixedBehaviourUpdate",
        "Camera.Render",
        "Render.OpaqueGeometry",
        "Render.TransparentGeometry",
        "Render.Mesh",
        "Physics.Processing",
        "Physics.Simulate",
        "GarbageCollector.CollectIncremental",
        "GC.Collect",
        "Audio.Update",
        "ParticleSystem.Update",
        "Canvas.SendWillRenderCanvases",
        "Canvas.BuildBatch",
        "UI Canvas.UpdateBatchedVisuals",
        "Animators.Update",
        "Skinning",
        "Shadows.RenderJob",
        "PostLateUpdate.PlayerUpdateCanvases",
        "PostLateUpdate.FinishFrameRendering",
    };

    private void OnEnable()
    {
        foreach (var name in MarkerNames)
        {
            var rec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, name, 30);
            if (rec.Valid)
                _markers.Add(new MarkerInfo { Name = name, Recorder = rec });
            else
                rec.Dispose();
        }

        if (_markers.Count == 0)
            Debug.LogWarning("[FPSCounter] ProfilerRecorder не доступен. " +
                             "Требуется Unity 2020.2+ и Development Build/Editor.");
    }

    private void OnDisable()
    {
        foreach (var m in _markers)
            m.Recorder.Dispose();
        _markers.Clear();
    }

    private void Update()
    {
        _accum += Time.unscaledDeltaTime;
        _frames++;
        _updateTimer -= Time.unscaledDeltaTime;
        _logTimer -= Time.unscaledDeltaTime;

        if (_updateTimer <= 0f)
        {
            float fps = _frames / _accum;
            float ms = (_accum / _frames) * 1000f;

            if (fpsText != null)
                fpsText.text = $"FPS: {fps:F0}  |  {ms:F2} ms";

            _accum = 0f;
            _frames = 0;
            _updateTimer = updateInterval;
        }

        if (autoLog && _logTimer <= 0f)
        {
            _logTimer = logInterval;
            AnalyzeAndLog();
        }
    }

    /// <summary>Ручной вызов анализа (можно привязать к кнопке).</summary>
    [ContextMenu("Проанализировать производительность")]
    public void AnalyzeAndLog()
    {
        _sorted.Clear();

        double total = 0;
        foreach (var m in _markers)
        {
            if (!m.Recorder.Valid) continue;
            double msVal = m.Recorder.LastValue * 1e-6; // нс -> мс
            if (msVal <= 0) continue;
            _sorted.Add((m.Name, msVal));
            if (m.Name == "PlayerLoop") total = msVal;
        }

        _sorted.Sort((a, b) => b.ms.CompareTo(a.ms));

        float fpsNow = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f);

        _sb.Clear();
        _sb.AppendLine($"===== [PROFILER] FPS: {fpsNow:F0}  ({1000f / Mathf.Max(fpsNow, 0.01f):F2} ms/кадр) =====");

        if (total > 0)
            _sb.AppendLine($"PlayerLoop (всего): {total:F2} ms");

        bool anyHeavy = false;
        int shown = 0;
        foreach (var item in _sorted)
        {
            if (item.name == "PlayerLoop") continue;
            if (item.ms < heavyThresholdMs) break;

            double percent = total > 0 ? (item.ms / total) * 100.0 : 0;
            _sb.AppendLine($"  → {item.name}: {item.ms:F2} ms  ({percent:F1}% от PlayerLoop)");
            anyHeavy = true;
            if (++shown >= 8) break;
        }

        if (!anyHeavy)
            _sb.AppendLine("  Все системы в пределах нормы (нет тяжёлых подсистем).");

        // Дополнительная статистика по памяти
        long monoUsed = Profiler.GetMonoUsedSizeLong();
        long totalAlloc = Profiler.GetTotalAllocatedMemoryLong();
        _sb.AppendLine($"  Memory: Mono={monoUsed / 1024 / 1024} MB, " +
                       $"Total Alloc={totalAlloc / 1024 / 1024} MB");

        Debug.Log(_sb.ToString());
    }
}