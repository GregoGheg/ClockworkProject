using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anima gli ingranaggi meccanici:
/// - ON (sorgente): ruota in senso orario
/// - OFF (adiacente): ruota in senso antiorario
/// - Velocità dipende dal tag "large" nel nome del PieceData
/// </summary>
[RequireComponent(typeof(GridManager))]
public class GearVisualizer : MonoBehaviour
{
    [Header("Velocità rotazione")]
    public float speedNormal = 90f;  // gradi/secondo ingranaggio normale
    public float speedLarge = 45f;  // gradi/secondo ingranaggio grande

    [Header("Gameplay")]
    [Tooltip("La destinazione è raggiunta solo se l'ultimo ingranaggio è ON")]
    public bool requireOnAtDest = true;

    GridManager grid;

    // coord → (transform sprite, isLarge, isOn)
    class GearEntry
    {
        public Transform spriteTransform;
        public bool isLarge;
        public bool isOn;
    }

    Dictionary<Vector2Int, GearEntry> activeGears = new();

    void Awake() => grid = GetComponent<GridManager>();

    void Start()
    {
        grid.OnGridChanged += () => StartCoroutine(RefreshNextFrame());
    }

    void OnDestroy()
    {
        if (grid != null) grid.OnGridChanged -= () => StartCoroutine(RefreshNextFrame());
    }

    System.Collections.IEnumerator RefreshNextFrame()
    {
        yield return null;
        yield return null;
        RefreshGears();
    }

    void RefreshGears()
    {
        var map = CircuitSolver.BuildConductMap(grid);
        var source = grid.level.circuitSource;

        // GetGearStates include solo le celle raggiute dall'energia meccanica
        var states = MechanicalSolver.GetGearStates(map, source, grid);

        activeGears.Clear();

        var allDraggers = GetComponentsInChildren<PieceDragger>(true);

        // Usa un set per non processare lo stesso PieceDragger più volte
        var processedDraggers = new HashSet<PieceDragger>();

        foreach (var d in allDraggers)
        {
            if (processedDraggers.Contains(d)) continue;
            if (d.piece.data == null || !d.piece.data.isGear) continue;

            var coord = d.piece.gridPosition;
            if (coord.x < 0) continue;

            // Determina lo stato del pezzo dalla sua cella principale (gridPosition)
            // Un pezzo multi-cella ha UN solo stato on/off per tutto il pezzo
            bool reached = false;
            bool isOn = true;

            // Cerca lo stato nella cella principale prima, poi nelle altre
            if (states.ContainsKey(coord))
            {
                reached = true;
                isOn = states[coord];
            }
            else
            {
                // Cerca in tutte le celle del pezzo
                foreach (var cell in d.piece.CurrentCells())
                {
                    var worldCoord = coord + cell.localCoord;
                    if (!states.ContainsKey(worldCoord)) continue;
                    reached = true;
                    isOn = states[worldCoord];
                    break; // usa solo la prima cella trovata — stato unico per tutto il pezzo
                }
            }

            if (!reached) continue;

            Transform spriteT = null;
            foreach (Transform child in d.transform)
                if (child.name == "piece_sprite") { spriteT = child; break; }
            if (spriteT == null) continue;

            processedDraggers.Add(d);
            activeGears[coord] = new GearEntry
            {
                spriteTransform = spriteT,
                isLarge = d.piece.data.isLarge,
                isOn = isOn
            };
        }
    }

    void Update()
    {
        foreach (var kv in activeGears)
        {
            var gear = kv.Value;
            if (gear.spriteTransform == null) continue;

            float speed = gear.isLarge ? speedLarge : speedNormal;
            float dir = gear.isOn ? -1f : 1f; // ON = orario (negativo in Unity UI), OFF = antiorario

            gear.spriteTransform.Rotate(0f, 0f, dir * speed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Verifica se la destinazione è raggiunta con stato ON.
    /// Usato da GameManager per il check vittoria.
    /// </summary>
    public bool IsDestinationOn(Vector2Int dest)
    {
        if (!requireOnAtDest) return true;
        if (activeGears.TryGetValue(dest, out var gear)) return gear.isOn;
        return false;
    }
}