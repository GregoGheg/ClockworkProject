using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(GridManager))]
public class SourceDebugger : MonoBehaviour
{
    GridManager grid;
    void Awake() => grid = GetComponent<GridManager>();

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.sKey.wasPressedThisFrame) return;
        Debug.Log("═══ SOURCE DEBUG ═══");

        var map = CircuitSolver.BuildConductMap(grid);
        foreach (var s in grid.level.GetSources())
        {
            Debug.Log($"[Src] pos={s.position} inMap={map.ContainsKey(s.position)} " +
                      $"emitsMech={s.Emits(EnergyType.Mechanical)} emitsElec={s.Emits(EnergyType.Electric)} emitsHydr={s.Emits(EnergyType.Hydraulic)}");
            if (map.TryGetValue(s.position, out var ch))
                foreach (var c in ch)
                    Debug.Log($"    canale type={c.type} in={c.conductIn} out={c.conductOut}");

            // Vicini
            foreach (var dir in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
            {
                var nb = s.position + dir;
                var cell = grid.GetCell(nb);
                Debug.Log($"    vicino {dir}: {nb} occupant={cell?.occupant?.data?.name ?? "null"} inMap={map.ContainsKey(nb)}");
                if (map.TryGetValue(nb, out var nbch))
                    foreach (var c in nbch)
                        Debug.Log($"        canale type={c.type} in={c.conductIn} out={c.conductOut}");
            }

            foreach (var type in new[]{EnergyType.Mechanical,EnergyType.Electric,EnergyType.Hydraulic})
            {
                var reached = CircuitSolver.GetReachedCells(grid, s.position, type);
                Debug.Log($"    reached[{type}] = {reached.Count} celle: {string.Join(",", reached)}");
            }
        }
        Debug.Log("════════════════════");
    }
}
