using System.Collections.Generic;
using UnityEngine;

public static class BeltSolver
{
    public static void ApplyBelts(
        Dictionary<Vector2Int, bool> states,
        GridManager grid)
    {
        var draggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);

        // Più passate: una catena di cinghie si propaga passo per passo
        bool changed = true;
        int safety = 10;
        while (changed && safety-- > 0)
        {
            changed = false;
            foreach (var d in draggers)
            {
                if (!d.isBelt) continue;
                if (d.piece.gridPosition.x < 0 || d.beltEndCell.x < 0) continue;

                var gearA = GetGearAt(d.piece.gridPosition, grid);
                var gearB = GetGearAt(d.beltEndCell, grid);
                if (gearA == null || gearB == null) continue;

                bool hasA = states.ContainsKey(gearA.Value);
                bool hasB = states.ContainsKey(gearB.Value);
                if (!hasA && !hasB) continue;

                // La cinghia forza lo STESSO stato (stesso verso di rotazione)
                bool shared = hasA ? states[gearA.Value] : states[gearB.Value];

                if (!hasA) { states[gearA.Value] = shared; changed = true; }
                if (!hasB) { states[gearB.Value] = shared; changed = true; }

                // Entrambi presenti ma diversi → A (anchor) vince su B (end)
                if (hasA && hasB && states[gearA.Value] != states[gearB.Value])
                {
                    states[gearB.Value] = states[gearA.Value];
                    changed = true;
                }
            }
        }
    }

    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] _dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    /// <summary>
    /// Propaga il "reached" attraverso le cinghie E poi ri-propaga
    /// ortogonalmente dai gear appena raggiunti. In questo modo un gear
    /// che riceve energia via cinghia la trasmette ai pezzi adiacenti
    /// (e così di seguito a tutta la catena), non solo ad altre cinghie.
    /// </summary>
    public static void PropagateReached(HashSet<Vector2Int> reached, GridManager grid)
    {
        if (grid == null) return;

        var draggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);

        // conductMap con i gear (allSides) per la ri-propagazione ortogonale
        var map = CircuitSolver.BuildConductMap(grid);
        MechanicalSolver.AddBeltGearsToMap(map, grid);

        bool changed = true;
        int safety = 50;
        while (changed && safety-- > 0)
        {
            changed = false;

            // 1) Propaga via cinghia (gear ↔ gear, anche diagonale)
            foreach (var d in draggers)
            {
                if (!d.isBelt) continue;
                if (d.piece.gridPosition.x < 0 || d.beltEndCell.x < 0) continue;

                var gearA = GetGearAt(d.piece.gridPosition, grid);
                var gearB = GetGearAt(d.beltEndCell, grid);
                if (gearA == null || gearB == null) continue;

                bool aReached = GearIsReached(reached, gearA.Value, grid);
                bool bReached = GearIsReached(reached, gearB.Value, grid);

                if (aReached && !bReached) { AddGearToReached(reached, gearB.Value, grid); changed = true; }
                if (bReached && !aReached) { AddGearToReached(reached, gearA.Value, grid); changed = true; }
            }

            // 2) Ri-propaga ORTOGONALMENTE dai gear appena raggiunti.
            //    Questo è ciò che permette al gear collegato via cinghia di
            //    trasmettere l'energia ai pezzi fisicamente adiacenti.
            foreach (var cur in new List<Vector2Int>(reached))
            {
                if (!map.TryGetValue(cur, out var curCh)) continue;
                foreach (var (dir, outS, inS) in _dirs)
                {
                    var next = cur + dir;
                    if (reached.Contains(next)) continue;
                    if (!map.TryGetValue(next, out var nextCh)) continue;
                    if (!MechHasSideOrAll(curCh, outS, true)) continue;
                    if (!MechHasSideOrAll(nextCh, inS, false)) continue;
                    reached.Add(next);
                    changed = true;
                }
            }
        }
    }

    static bool MechHasSideOrAll(List<PieceData.EnergyChannel> channels,
                                  PieceData.ConnectionSides side, bool isOut)
    {
        var all = PieceData.ConnectionSides.Up | PieceData.ConnectionSides.Down
                | PieceData.ConnectionSides.Left | PieceData.ConnectionSides.Right;
        foreach (var ch in channels)
        {
            if (ch.type != EnergyType.Mechanical) continue;
            if (ch.conductIn == all && ch.conductOut == all) return true; // allSides
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }

    static bool GearIsReached(HashSet<Vector2Int> reached, Vector2Int gearPos, GridManager grid)
    {
        if (reached.Contains(gearPos)) return true;
        var cs = grid?.GetCell(gearPos.x, gearPos.y);
        if (cs?.occupant == null) return false;
        foreach (var cell in cs.occupant.CurrentCells())
            if (reached.Contains(gearPos + cell.localCoord)) return true;
        return false;
    }

    static void AddGearToReached(HashSet<Vector2Int> reached, Vector2Int gearPos, GridManager grid)
    {
        reached.Add(gearPos);
        var cs = grid?.GetCell(gearPos.x, gearPos.y);
        if (cs?.occupant == null) return;
        foreach (var cell in cs.occupant.CurrentCells())
            reached.Add(gearPos + cell.localCoord);
    }

    static Vector2Int? FindGearGridPos(Vector2Int coord, GridManager grid)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var c = coord + new Vector2Int(dx, dy);
                if (!grid.IsInBounds(c)) continue;
                var state = grid.GetCell(c.x, c.y);
                if (state?.occupant != null && state.occupant.data.isGear)
                    return state.occupant.gridPosition;
            }
        return null;
    }

    /// <summary>Cerca un gear esattamente su questa cella o sulle sue celle (per ingranaggi grandi).</summary>
    static Vector2Int? GetGearAt(Vector2Int coord, GridManager grid)
    {
        if (!grid.IsInBounds(coord)) return null;
        var st = grid.GetCell(coord.x, coord.y);
        if (st?.occupant != null && st.occupant.data.isGear)
            return st.occupant.gridPosition;
        // Per ingranaggi grandi che occupano più celle
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var c = coord + new Vector2Int(dx, dy);
                if (!grid.IsInBounds(c)) continue;
                var s = grid.GetCell(c.x, c.y);
                if (s?.occupant != null && s.occupant.data.isGear)
                    return s.occupant.gridPosition;
            }
        return null;
    }
}