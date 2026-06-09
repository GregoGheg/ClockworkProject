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
        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            if (d.piece.gridPosition.x < 0) continue;
            if (d.beltEndCell.x < 0) continue;

            var gearA = GetGearAt(d.piece.gridPosition, grid);
            var gearB = GetGearAt(d.beltEndCell, grid);

            if (gearA == null || gearB == null) continue;

            bool hasA = states.ContainsKey(gearA.Value);
            bool hasB = states.ContainsKey(gearB.Value);

            // Se nessuno dei due è nella mappa, non possiamo determinare lo stato
            if (!hasA && !hasB) continue;

            // Lo stato condiviso è quello del gear già connesso energeticamente
            bool shared = hasA ? states[gearA.Value] : states[gearB.Value];
            states[gearA.Value] = shared;
            states[gearB.Value] = shared; // aggiunge anche gear non connesso meccanicamente
        }
    }

    public static void PropagateReached(HashSet<Vector2Int> reached, GridManager grid)
    {
        var draggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);
        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            if (d.piece.gridPosition.x < 0 || d.beltEndCell.x < 0) continue;

            var gearA = GetGearAt(d.piece.gridPosition, grid);
            var gearB = GetGearAt(d.beltEndCell, grid);
            if (gearA == null || gearB == null) continue;

            bool aReached = GearIsReached(reached, gearA.Value, grid);
            bool bReached = GearIsReached(reached, gearB.Value, grid);

            if (aReached && !bReached) AddGearToReached(reached, gearB.Value, grid);
            if (bReached && !aReached) AddGearToReached(reached, gearA.Value, grid);
        }
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