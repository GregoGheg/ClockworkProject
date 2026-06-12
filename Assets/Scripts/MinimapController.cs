using UnityEngine;

/// <summary>
/// Minimappa e AreaMappa possono stare in parent diversi.
/// I nodi (es. "Petto", "BraccioSin") sono figli di Minimappa.
/// AreaMappa viene spostata convertendo la posizione world del nodo.
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("Riferimenti")]
    public RectTransform indicator;   // AreaMappa
    public WorldNavigator navigator;

    int lastIndex = -1;

    void Start() => Refresh();

    void Update()
    {
        if (navigator == null) return;
        int idx = navigator.CurrentIndex;
        if (idx != lastIndex) { lastIndex = idx; Refresh(); }
    }

    void Refresh()
    {
        if (indicator == null || navigator == null) return;
        var config = navigator.config;
        if (config == null || config.levels == null) return;

        int idx = navigator.CurrentIndex;
        if (idx < 0 || idx >= config.levels.Length) return;

        string nodeName = config.levels[idx].displayName;
        if (string.IsNullOrEmpty(nodeName)) return;

        var node = transform.Find(nodeName);
        if (node == null)
        {
            Debug.LogWarning($"[Minimap] Nodo '{nodeName}' non trovato come figlio di {gameObject.name}");
            return;
        }

        // Converti posizione world del nodo in posizione locale al parent di AreaMappa
        var nodeRt = node.GetComponent<RectTransform>();
        var indicatorParent = indicator.parent as RectTransform;

        Vector2 worldPos = nodeRt.position;
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            indicatorParent,
            RectTransformUtility.WorldToScreenPoint(null, worldPos),
            null,
            out localPos
        );

        indicator.anchoredPosition = localPos;
    }
}