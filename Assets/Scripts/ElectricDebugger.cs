using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Premi D = debug elettrico, H = idrico, M = meccanico
/// </summary>
[RequireComponent(typeof(GridManager))]
public class ElectricDebugger : MonoBehaviour
{
    GridManager grid;
    void Awake() => grid = GetComponent<GridManager>();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.dKey.wasPressedThisFrame) DebugEnergy(EnergyType.Electric);
        if (kb.hKey.wasPressedThisFrame) DebugEnergy(EnergyType.Hydraulic);
        if (kb.mKey.wasPressedThisFrame) DebugEnergy(EnergyType.Mechanical);
    }

    void DebugEnergy(EnergyType type)
    {
        string t = type switch { EnergyType.Electric => "ELECTRIC", EnergyType.Hydraulic => "HYDRAULIC", _ => "MECHANICAL" };
        Debug.Log($"═══════ {t} DEBUG ═══════");

        var map = CircuitSolver.BuildConductMap(grid);
        var source = grid.level.circuitSource;
        var dest = grid.level.circuitDestination;

        // Log mappa completa
        Debug.Log($"[{t}] Celle nella conductMap: {map.Count}");
        foreach (var kv in map)
        {
            bool hasType = false;
            foreach (var ch in kv.Value) if (ch.type == type) { hasType = true; break; }
            if (!hasType) continue;

            string info = "";
            foreach (var ch in kv.Value)
            {
                if (ch.type != type) continue;
                info += $"IN:{SidesToString(ch.conductIn)} OUT:{SidesToString(ch.conductOut)}";
                if (type == EnergyType.Electric) info += $" instab:{ch.instability:F2}";
                info += " | ";
            }
            Debug.Log($"  {kv.Key}: {info}");
        }

        if (!map.ContainsKey(source))
        {
            Debug.Log($"[{t}] Sorgente {source} NON nella mappa — nessun canale {type}");
            return;
        }

        // BFS dettagliato
        var visited = new HashSet<Vector2Int>();
        var accumulated = new Dictionary<Vector2Int, float>();
        var queue = new Queue<(Vector2Int coord, Vector2Int from)>();

        visited.Add(source);
        accumulated[source] = 0f;
        queue.Enqueue((source, source));

        var dirs = new (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS, string label)[]
        {
            (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left,  "→"),
            (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right, "←"),
            (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down,  "↑"),
            (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up,    "↓"),
        };

        while (queue.Count > 0)
        {
            var (cur, from) = queue.Dequeue();
            map.TryGetValue(cur, out var curCh);
            float curAcc = accumulated.GetValueOrDefault(cur, 0f);

            string inS = "", outS = "", extra = "";
            if (curCh != null)
                foreach (var ch in curCh)
                {
                    if (ch.type != type) continue;
                    if (ch.conductIn != PieceData.ConnectionSides.None) inS += SidesToString(ch.conductIn) + " ";
                    if (ch.conductOut != PieceData.ConnectionSides.None) outS += SidesToString(ch.conductOut) + " ";
                    if (type == EnergyType.Electric) extra = $" | instab cell:{ch.instability:F2} acc:{curAcc:F2}";
                    if (type == EnergyType.Hydraulic) extra = $" | acc energy:{curAcc:F2}";
                }

            Debug.Log($"[{t[0]}] {cur} (da {from}) IN:{(inS == "" ? "∅" : inS.Trim())} OUT:{(outS == "" ? "∅" : outS.Trim())}{extra}");

            if (curCh == null) continue;

            foreach (var (dir, oS, iS, lbl) in dirs)
            {
                var next = cur + dir;
                if (visited.Contains(next)) continue;
                if (!map.TryGetValue(next, out var nextCh))
                {
                    // La cella adiacente non è nella mappa — non ha pezzi piazzati
                    if (grid.IsInBounds(next))
                        Debug.Log($"  {lbl} {next}: NESSUN PEZZO in mappa");
                    continue;
                }

                bool emits = HasSide(curCh, oS, isOut: true, type);
                bool accepts = HasSide(nextCh, iS, isOut: false, type);

                if (!emits) { Debug.Log($"  {lbl} {next}: NO OUT {SidesToString(oS)} da {cur}"); continue; }
                if (!accepts) { Debug.Log($"  {lbl} {next}: NO IN  {SidesToString(iS)} in {next}"); continue; }

                float nextAcc = curAcc;
                if (type == EnergyType.Electric)
                    foreach (var ch in nextCh) { if (ch.type == type) nextAcc += ch.instability; }
                if (type == EnergyType.Hydraulic)
                    foreach (var ch in nextCh) { if (ch.type == type) nextAcc += 0.3f; }

                if (type == EnergyType.Electric && nextAcc > 10f)
                { Debug.Log($"  {lbl} {next}: INSTABILITÀ TROPPO ALTA {nextAcc:F2}"); continue; }

                Debug.Log($"  {lbl} {next}: ✓ OK");
                visited.Add(next);
                accumulated[next] = nextAcc;
                queue.Enqueue((next, cur));
            }
        }

        Debug.Log($"═══ Celle raggiunte: {visited.Count} | Dest {dest}: {(visited.Contains(dest) ? "RAGGIUNTA ✓" : "NON raggiunta ✗")} ═══");
    }

    static bool HasSide(List<PieceData.EnergyChannel> channels,
                        PieceData.ConnectionSides side, bool isOut, EnergyType type)
    {
        foreach (var ch in channels)
        {
            if (ch.type != type) continue;
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }

    static string SidesToString(PieceData.ConnectionSides s)
    {
        if (s == PieceData.ConnectionSides.None) return "∅";
        var p = new List<string>();
        if ((s & PieceData.ConnectionSides.Right) != 0) p.Add("R");
        if ((s & PieceData.ConnectionSides.Left) != 0) p.Add("L");
        if ((s & PieceData.ConnectionSides.Up) != 0) p.Add("U");
        if ((s & PieceData.ConnectionSides.Down) != 0) p.Add("D");
        return string.Join("|", p);
    }
}