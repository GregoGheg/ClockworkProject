using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Istanzia bottoni-freccia direttamente sulle celle della griglia:
///
/// - FRECCIA AVANTI: su ogni cella di END (destinazione) che è soddisfatta,
///   appare una freccia. Cliccandola si va al livello che quella destinazione
///   sblocca (EnergyDestination.unlocksLevelAtMapPosition).
///
/// - FRECCIA INDIETRO: su ogni cella di START (sorgente) che ha un collegamento
///   di ritorno (EnergySource.returnsToLevelAtMapPosition) verso un livello GIÀ
///   VISITATO, appare una freccia che riporta a quel livello.
///
/// SETUP:
/// 1. Attacca questo script sullo stesso GameObject del GameManager (prefab).
/// 2. Assegna "Arrow Button Prefab" col tuo Button UI (ci metti solo lo sprite).
/// 3. (Opzionale) Regola scale/offset.
///
/// Il bottone viene istanziato come figlio della griglia, centrato sulla cella,
/// e resta un Button UI premibile.
/// </summary>
[RequireComponent(typeof(GameManager))]
public class GridNavArrows : MonoBehaviour
{
    [Header("Prefab freccia (Button UI)")]
    [Tooltip("Il tuo Button UI con lo sprite della freccia")]
    public Button arrowButtonPrefab;

    [Header("Aspetto")]
    [Tooltip("Dimensione della freccia rispetto alla cella (1 = piena cella)")]
    public float scale = 0.8f;
    [Tooltip("Ruota la freccia avanti per puntare verso l'uscita")]
    public bool rotateForward = true;
    [Tooltip("Ruota la freccia indietro per puntare verso il livello di ritorno")]
    public bool rotateBack = true;

    [Header("Aggiornamento")]
    [Tooltip("Ogni quanti secondi ricontrollare lo stato delle frecce")]
    public float refreshInterval = 0.3f;

    GameManager gm;
    GridManager grid;
    WorldNavigator nav;

    readonly List<GameObject> activeArrows = new();
    float timer;

    void Awake() => gm = GetComponent<GameManager>();

    void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < refreshInterval) return;
        timer = 0f;
        Refresh();
    }

    void Refresh()
    {
        grid = gm != null ? gm.gridManager : null;
        nav  = gm != null ? gm.worldNavigator : null;
        if (grid == null || !grid.IsReady || nav == null || grid.level == null) return;
        if (arrowButtonPrefab == null) return;

        ClearArrows();

        var curPos = nav.config.levels[gm.levelIndex].mapPosition;

        // ── Frecce AVANTI sulle celle di END soddisfatte ──────────────────
        foreach (var dest in grid.level.GetDestinations())
        {
            if (!dest.HasUnlock) continue;
            if (!CircuitSolver.IsDestinationSatisfied(grid, dest)) continue;

            int targetIdx = nav.IndexOfMapPosition(dest.unlocksLevelAtMapPosition);
            if (targetIdx < 0) continue;

            var delta = dest.unlocksLevelAtMapPosition - curPos;
            SpawnArrow(dest.position, DirAngle(delta), rotateForward,
                       () => nav.NavigateTo(targetIdx));
        }

        // ── Frecce INDIETRO sulle celle di START collegate ────────────────
        foreach (var src in grid.level.GetSources())
        {
            if (!src.HasReturn) continue;

            int targetIdx = nav.IndexOfMapPosition(src.returnsToLevelAtMapPosition);
            if (targetIdx < 0) continue;
            if (!nav.HasBeenVisited(targetIdx)) continue; // solo se già visitato

            var delta = src.returnsToLevelAtMapPosition - curPos;
            SpawnArrow(src.position, DirAngle(delta), rotateBack,
                       () => nav.NavigateTo(targetIdx));
        }
    }

    void SpawnArrow(Vector2Int cell, float angle, bool doRotate, UnityEngine.Events.UnityAction onClick)
    {
        var btn = Instantiate(arrowButtonPrefab, grid.transform);
        btn.onClick.AddListener(onClick);

        var rt = btn.GetComponent<RectTransform>();
        float size = grid.cellSize;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.one * size * scale;
        rt.anchoredPosition = new Vector2(
            cell.x * size + size * 0.5f,
            cell.y * size + size * 0.5f);

        if (doRotate) rt.localEulerAngles = new Vector3(0, 0, angle);

        btn.transform.SetAsLastSibling(); // sopra a tutto
        btn.gameObject.SetActive(true);
        activeArrows.Add(btn.gameObject);
    }

    /// <summary>Angolo Z per puntare nella direzione di delta.
    /// Default sprite: assume freccia che punta a DESTRA (0°).</summary>
    static float DirAngle(Vector2Int delta)
    {
        if (delta.x > 0 && delta.y == 0) return 0f;    // destra
        if (delta.x < 0 && delta.y == 0) return 180f;  // sinistra
        if (delta.y > 0 && delta.x == 0) return 90f;   // su
        if (delta.y < 0 && delta.x == 0) return -90f;  // giù
        // diagonale / non allineato: punta verso l'asse dominante
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0 ? 0f : 180f;
        return delta.y >= 0 ? 90f : -90f;
    }

    void ClearArrows()
    {
        foreach (var a in activeArrows)
            if (a != null) Destroy(a);
        activeArrows.Clear();
    }

    void OnDisable() => ClearArrows();
}
