using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

/// <summary>
/// 1) Limita gli FPS (default 60).
/// 2) Profiler leggero: misura quanto tempo prendono i solver pesanti e i
///    visualizer quando piazzi un pezzo. Premi P per stampare un report.
///
/// SETUP: attacca su un GameObject vuoto nella scena (uno solo).
/// </summary>
public class PerfTools : MonoBehaviour
{
    [Header("Limite FPS")]
    public int targetFps = 60;
    [Tooltip("Disattiva il VSync così targetFrameRate ha effetto")]
    public bool disableVSync = true;

    [Header("Profiler")]
    [Tooltip("Logga automaticamente i frame che durano più di questa soglia (ms)")]
    public float spikeThresholdMs = 20f;

    // Misure cumulative per categoria
    static readonly Dictionary<string, double> totalMs = new();
    static readonly Dictionary<string, int> callCount = new();

    void Awake()
    {
        if (disableVSync) QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFps;
    }

    void Update()
    {
        // Rileva frame-spike
        float frameMs = Time.unscaledDeltaTime * 1000f;
        if (frameMs > spikeThresholdMs)
            Debug.Log($"[Perf] FRAME SPIKE: {frameMs:F1}ms ({1000f / frameMs:F0} fps) — " +
                      $"BuildConductMap chiamato {CircuitSolver.BuildConductMapCallsThisFrame}x questo frame");

        var kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) PrintReport();
        if (kb != null && kb.oKey.wasPressedThisFrame) ResetStats();
    }

    /// <summary>Avvolgi una chiamata costosa: PerfTools.Measure("nome", () => ...).</summary>
    public static void Measure(string label, System.Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        totalMs[label] = (totalMs.TryGetValue(label, out var t) ? t : 0) + ms;
        callCount[label] = (callCount.TryGetValue(label, out var c) ? c : 0) + 1;
        if (ms > 5.0)
            Debug.Log($"[Perf] {label}: {ms:F2}ms (singola chiamata)");
    }

    void PrintReport()
    {
        Debug.Log("═══════ PERF REPORT (premi O per azzerare) ═══════");
        var sorted = new List<KeyValuePair<string, double>>(totalMs);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var kv in sorted)
        {
            int n = callCount.TryGetValue(kv.Key, out var c) ? c : 1;
            Debug.Log($"  {kv.Key}: tot {kv.Value:F1}ms su {n} chiamate (media {kv.Value / n:F2}ms)");
        }
        // Conta i PieceDragger in scena (sintomo di troppi FindObjectsByType)
        var draggers = FindObjectsByType<PieceDragger>(FindObjectsSortMode.None);
        Debug.Log($"  [info] PieceDragger totali in scena: {draggers.Length}");
        Debug.Log("══════════════════════════════════════════════════");
    }

    void ResetStats()
    {
        totalMs.Clear();
        callCount.Clear();
        Debug.Log("[Perf] statistiche azzerate");
    }
}
