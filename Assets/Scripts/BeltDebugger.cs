using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attacca su un GameObject vuoto nella scena.
/// Premi B per stampare in Console lo stato completo della cinghia:
/// - Quali PieceDragger hanno isBelt=true
/// - Le loro gridPosition e beltEndCell
/// - Cosa c'è nella conductMap per quelle celle
/// - Lo stato finale di pieceStates
/// </summary>
[RequireComponent(typeof(GridManager))]
public class BeltDebugger : MonoBehaviour
{
    GridManager grid;
    void Awake() => grid = GetComponent<GridManager>();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.bKey.wasPressedThisFrame) return;
        RunDebug();
    }

    void RunDebug()
    {
        Debug.Log("════════ BELT DEBUG ════════");

        // 1) Tutti i PieceDragger con isBelt
        var draggers = FindObjectsByType<PieceDragger>(FindObjectsSortMode.None);
        int beltCount = 0;
        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            beltCount++;
            Debug.Log($"[Belt] dragger={d.name} " +
                      $"gridPos={d.piece.gridPosition} " +
                      $"beltEnd={d.beltEndCell} " +
                      $"piece.data={d.piece?.data?.name ?? "NULL"}");
        }
        if (beltCount == 0) Debug.Log("[Belt] Nessun PieceDragger con isBelt=true trovato!");

        // 2) ConductMap
        var map = CircuitSolver.BuildConductMap(grid);
        MechanicalSolver.AddBeltGearsToMap(map, grid);
        Debug.Log($"[Belt] conductMap ha {map.Count} celle dopo AddBeltGearsToMap");

        // 3) Gear piazzati
        Debug.Log("[Belt] Gear piazzati:");
        foreach (var piece in grid.PlacedPieces)
        {
            if (!piece.data.isGear) continue;
            Debug.Log($"  gear={piece.data.name} gridPos={piece.gridPosition} " +
                      $"inMap={map.ContainsKey(piece.gridPosition)}");
        }

        // 4) Celle della cinghia nella map
        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            var a = d.piece.gridPosition;
            var b = d.beltEndCell;
            if (a.x < 0 || b.x < 0) { Debug.Log($"[Belt] cinghia non connessa"); continue; }
            Debug.Log($"[Belt] cinghia: A={a} inMap={map.ContainsKey(a)}  B={b} inMap={map.ContainsKey(b)}");
            // Celle intermedie
            var cur = a;
            int steps = 0;
            while (cur != b && steps < 20)
            {
                steps++;
                int dx = b.x > cur.x ? 1 : b.x < cur.x ? -1 : 0;
                int dy = b.y > cur.y ? 1 : b.y < cur.y ? -1 : 0;
                cur += new Vector2Int(dx, dy);
                Debug.Log($"  intermedia {cur} inMap={map.ContainsKey(cur)}");
            }
        }

        // 5) GetGearStates
        var map2 = CircuitSolver.BuildConductMap(grid);
        var source = grid.level.circuitSource;
        var states = MechanicalSolver.GetGearStates(map2, source, grid);
        Debug.Log($"[Belt] GetGearStates risultato ({states.Count} entries):");
        foreach (var kv in states)
            Debug.Log($"  pos={kv.Key} on={kv.Value}");

        // 6) GetReachedCells meccanici
        var map3 = CircuitSolver.BuildConductMap(grid);
        MechanicalSolver.AddBeltGearsToMap(map3, grid);
        var reached = MechanicalSolver.GetReachedCells(map3, source);
        BeltSolver.PropagateReached(reached, grid);
        Debug.Log($"[Belt] GetReachedCells meccanici ({reached.Count} celle): {string.Join(", ", reached)}");

        Debug.Log("════════════════════════════");
    }
}
