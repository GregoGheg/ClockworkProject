using System.Collections.Generic;
using UnityEngine;
public static class CircuitSolver
{
    public static HashSet<Vector2Int> GetReachedCells(GridManager grid, Vector2Int source)
    {
        var map = BuildConductMap(grid);
        MechanicalSolver.AddBeltGearsToMap(map, grid);
        var result = new HashSet<Vector2Int>();
        var level = grid.level;
        if (level.SourceEmits(EnergyType.Mechanical))
        {
            var mechReached = MechanicalSolver.GetReachedCells(map, source);
            BeltSolver.PropagateReached(mechReached, grid);
            result.UnionWith(mechReached);
        }
        if (level.SourceEmits(EnergyType.Electric))
            result.UnionWith(ElectricSolver.GetReachedCells(map, source));
        if (level.SourceEmits(EnergyType.Hydraulic))
            result.UnionWith(HydraulicSolver.GetReachedCells(map, source, grid.Width, grid.Height, grid));
        return result;
    }
    public static Dictionary<Vector2Int, HydraulicSolver.FlowCell> GetHydraulicFlowMap(
        GridManager grid, Vector2Int source)
    {
        if (!grid.level.SourceEmits(EnergyType.Hydraulic))
            return new Dictionary<Vector2Int, HydraulicSolver.FlowCell>();
        var map = BuildConductMap(grid);
        return HydraulicSolver.GetFlowMap(map, source, grid.Width, grid.Height, grid);
    }
    public static HashSet<Vector2Int> GetReachedCells(
        GridManager grid, Vector2Int source, EnergyType type)
    {
        if (!grid.level.SourceEmits(type)) return new HashSet<Vector2Int>();
        var map = BuildConductMap(grid);
        if (type == EnergyType.Mechanical)
        {
            MechanicalSolver.AddBeltGearsToMap(map, grid);
            var mechReached = MechanicalSolver.GetReachedCells(map, source);
            BeltSolver.PropagateReached(mechReached, grid);
            return mechReached;
        }
        return type switch
        {
            EnergyType.Electric => ElectricSolver.GetReachedCells(map, source),
            EnergyType.Hydraulic => HydraulicSolver.GetReachedCells(map, source, grid.Width, grid.Height, grid),
            _ => new HashSet<Vector2Int>()
        };
    }
    public static Dictionary<Vector2Int, float> GetElectricInstability(GridManager grid, Vector2Int source)
    {
        if (!grid.level.SourceEmits(EnergyType.Electric))
            return new Dictionary<Vector2Int, float>();
        var map = BuildConductMap(grid);
        return ElectricSolver.GetReachedWithInstability(map, source);
    }
    public static bool Solve(GridManager grid, Vector2Int source, Vector2Int dest)
    {
        foreach (var type in new[] { EnergyType.Mechanical, EnergyType.Electric, EnergyType.Hydraulic })
        {
            if (!grid.level.DestAccepts(type)) continue;
            var typeReached = GetReachedCells(grid, source, type);
            if (typeReached.Contains(dest)) return true;
        }
        // Convertitori
        var gFlow = GetGenericFlow(grid, source);
        if (gFlow.ContainsKey(dest))
        {
            if (gFlow[dest] == EnergyType.Electric)
            {
                var gElecInstab = GetGenericElecInstability(grid, source);
                if (gElecInstab.ContainsKey(dest) && gElecInstab[dest] < 10f) return true;
            }
            else return true;
        }
        // Collettori elettrici
        if (grid.level.DestAccepts(EnergyType.Electric))
        {
            var cFlow = GetCollectorFlow(grid, source);
            if (cFlow.ContainsKey(dest)) return true;
        }
        return false;
    }
    public static List<(Vector2Int from, Vector2Int to)> GetEnergyLinks(
        GridManager grid, Vector2Int source, EnergyType type)
    {
        var map = BuildConductMap(grid);
        var links = new List<(Vector2Int, Vector2Int)>();
        var dirs = new (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[]
        {
            (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
            (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
            (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
            (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
        };
        if (type == EnergyType.Hydraulic)
        {
            var flow = HydraulicSolver.GetFlowMap(map, source, grid.Width, grid.Height, grid);
            var reached = new HashSet<Vector2Int>(flow.Keys);
            foreach (var cell in reached)
            {
                if (!map.TryGetValue(cell, out var curCh)) continue;
                foreach (var (dir, outS, inS) in dirs)
                {
                    var next = cell + dir;
                    if (!reached.Contains(next)) continue;
                    if (!map.TryGetValue(next, out var nextCh)) continue;
                    if (!HasSide(curCh, type, outS, true)) continue;
                    if (!HasSide(nextCh, type, inS, false)) continue;
                    if (cell.x < next.x || (cell.x == next.x && cell.y < next.y))
                        links.Add((cell, next));
                }
            }
            foreach (var kv in flow)
            {
                if (!kv.Value.isCascade) continue;
                var above = kv.Key + Vector2Int.up;
                if (!flow.ContainsKey(above)) continue;
                links.Add((above, kv.Key));
            }
            foreach (var kv in flow)
            {
                var coord = kv.Key;
                var cell = kv.Value;
                if (cell.isCascade) continue;
                if (!map.ContainsKey(coord)) continue;
                foreach (var lateralDir in new[] { Vector2Int.right, Vector2Int.left })
                {
                    var lateralPos = coord + lateralDir;
                    if (!flow.TryGetValue(lateralPos, out var lateralCell)) continue;
                    if (lateralCell.isCascade) continue;
                    if (map.ContainsKey(lateralPos)) continue;
                    links.Add((coord, lateralPos));
                }
            }
            return links;
        }
        HashSet<Vector2Int> simpleReached;
        if (type == EnergyType.Mechanical)
        {
            MechanicalSolver.AddBeltGearsToMap(map, grid);
            simpleReached = MechanicalSolver.GetReachedCells(map, source);
            BeltSolver.PropagateReached(simpleReached, grid);
        }
        else
        {
            simpleReached = type switch
            {
                EnergyType.Electric => ElectricSolver.GetReachedCells(map, source),
                _ => new HashSet<Vector2Int>()
            };
        }
        foreach (var cell in simpleReached)
        {
            if (!map.TryGetValue(cell, out var curCh)) continue;
            foreach (var (dir, outS, inS) in dirs)
            {
                var next = cell + dir;
                if (!simpleReached.Contains(next)) continue;
                if (!map.TryGetValue(next, out var nextCh)) continue;
                if (!HasSide(curCh, type, outS, true)) continue;
                if (!HasSide(nextCh, type, inS, false)) continue;
                if (cell.x < next.x || (cell.x == next.x && cell.y < next.y))
                    links.Add((cell, next));
            }
        }
        return links;
    }
    public static HashSet<Vector2Int> BuildConverterCells(GridManager grid)
    {
        var cells = new HashSet<Vector2Int>();
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isConverter) continue;
            foreach (var cell in piece.WorldCells())
                if (grid.IsInBounds(cell.localCoord))
                    cells.Add(cell.localCoord);
        }
        return cells;
    }
    public static Dictionary<Vector2Int, float> GetGenericElecInstability(
        GridManager grid, Vector2Int source)
    {
        var result = new Dictionary<Vector2Int, float>();
        var map = BuildConductMap(grid);
        var converters = BuildConverterCells(grid);
        if (converters.Count == 0) return result;
        var allReached = new HashSet<Vector2Int>();
        if (grid.level.SourceEmits(EnergyType.Mechanical))
            allReached.UnionWith(MechanicalSolver.GetReachedCells(map, source));
        if (grid.level.SourceEmits(EnergyType.Electric))
            allReached.UnionWith(ElectricSolver.GetReachedCells(map, source));
        if (grid.level.SourceEmits(EnergyType.Hydraulic))
            allReached.UnionWith(HydraulicSolver.GetReachedCells(map, source,
                grid.Width, grid.Height, grid));
        var genericResult = GenericSolver.GetReachedFromConverters(map, allReached, converters);
        var electricSeeds = new HashSet<Vector2Int>();
        foreach (var kv in genericResult)
        {
            if (kv.Value != EnergyType.Electric) continue;
            var seed = kv.Key;
            if (map.ContainsKey(seed))
            {
                bool hasElec = false;
                foreach (var ch in map[seed]) if (ch.type == EnergyType.Electric) { hasElec = true; break; }
                if (hasElec) { electricSeeds.Add(seed); continue; }
            }
            foreach (var dir in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
            {
                var nb = seed + dir;
                if (!map.ContainsKey(nb)) continue;
                bool hasElec = false;
                foreach (var ch in map[nb]) if (ch.type == EnergyType.Electric) { hasElec = true; break; }
                if (hasElec) electricSeeds.Add(nb);
            }
        }
        if (electricSeeds.Count > 0)
        {
            var firstSeed = new List<Vector2Int>(electricSeeds)[0];
            var reached = ElectricSolver.GetReachedWithInstability(map, firstSeed);
            foreach (var ekv in reached)
                if (ekv.Value < 10f)
                    result[ekv.Key] = ekv.Value;
        }
        return result;
    }
    public static Dictionary<Vector2Int, float> GetCollectorFlow(
        GridManager grid, Vector2Int source)
    {
        var map = BuildConductMap(grid);
        var collectors = CollectorSolver.BuildCollectorCells(grid);
        if (collectors.Count == 0) return new Dictionary<Vector2Int, float>();

        // Instabilità base dalla source originale
        var baseInstab = ElectricSolver.GetReachedWithInstability(map, source);

        // Dizionario cumulativo — parte con l'instabilità base
        var cumInstab = new System.Collections.Generic.Dictionary<Vector2Int, float>(baseInstab);
        var result = new System.Collections.Generic.Dictionary<Vector2Int, float>();

        // Loop finché ci sono nuove celle da aggiungere (max iterazioni = numero collector)
        int maxIterations = collectors.Count + 1;
        for (int i = 0; i < maxIterations; i++)
        {
            var flow = CollectorSolver.GetCollectorFlow(map, cumInstab, collectors, grid);
            var propagated = CollectorPropagator.PropagateFromCollector(map, flow, grid);

            bool anyNew = false;
            foreach (var kv in propagated)
            {
                if (!result.ContainsKey(kv.Key))
                {
                    result[kv.Key] = kv.Value;
                    anyNew = true;
                }
                // Aggiorna instabilità cumulativa con il valore migliore
                if (!cumInstab.ContainsKey(kv.Key) || kv.Value < cumInstab[kv.Key])
                    cumInstab[kv.Key] = kv.Value;
            }

            // Se non ci sono nuove celle, la catena è completa
            if (!anyNew) break;
        }

        return result;
    }
    public static Dictionary<Vector2Int, EnergyType> GetGenericFlow(
        GridManager grid, Vector2Int source)
    {
        var map = BuildConductMap(grid);
        var converters = BuildConverterCells(grid);
        if (converters.Count == 0) return new Dictionary<Vector2Int, EnergyType>();
        var pumps = BuildPumpCells(grid);
        var allReached = new HashSet<Vector2Int>();
        if (grid.level.SourceEmits(EnergyType.Mechanical))
            allReached.UnionWith(MechanicalSolver.GetReachedCells(map, source));
        if (grid.level.SourceEmits(EnergyType.Electric))
            allReached.UnionWith(ElectricSolver.GetReachedCells(map, source));
        if (grid.level.SourceEmits(EnergyType.Hydraulic))
            allReached.UnionWith(HydraulicSolver.GetReachedCells(map, source,
                grid.Width, grid.Height, grid));
        var genericResult = GenericSolver.GetReachedFromConverters(map, allReached, converters);
        if (genericResult.Count == 0) return genericResult;
        var expanded = new Dictionary<Vector2Int, EnergyType>(genericResult);
        var byType = new Dictionary<EnergyType, HashSet<Vector2Int>>();
        foreach (var kv in genericResult)
        {
            if (!byType.ContainsKey(kv.Value)) byType[kv.Value] = new HashSet<Vector2Int>();
            byType[kv.Value].Add(kv.Key);
        }
        foreach (var kv in byType)
        {
            var type = kv.Key;
            var seedCells = kv.Value;
            foreach (var seed in seedCells)
            {
                if (converters.Contains(seed)) continue;
                if (type == EnergyType.Electric)
                {
                    if (map.ContainsKey(seed))
                    {
                        var elecFromSeed = ElectricSolver.GetReachedWithInstability(map, seed);
                        foreach (var elecKv in elecFromSeed)
                            if (!expanded.ContainsKey(elecKv.Key) && elecKv.Value <= 10f)
                                expanded[elecKv.Key] = type;
                    }
                    else
                    {
                        var elecDirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
                        foreach (var eDir in elecDirs)
                        {
                            var neighbor = seed + eDir;
                            if (!map.ContainsKey(neighbor)) continue;
                            bool hasElec = false;
                            foreach (var ch in map[neighbor])
                                if (ch.type == EnergyType.Electric) { hasElec = true; break; }
                            if (!hasElec) continue;
                            var elecFromN = ElectricSolver.GetReachedWithInstability(map, neighbor);
                            foreach (var elecKv in elecFromN)
                                if (!expanded.ContainsKey(elecKv.Key) && elecKv.Value <= 10f)
                                    expanded[elecKv.Key] = type;
                        }
                    }
                    if (!expanded.ContainsKey(seed)) expanded[seed] = type;
                }
                else
                {
                    HashSet<Vector2Int> typeReached = null;
                    switch (type)
                    {
                        case EnergyType.Mechanical:
                            typeReached = MechanicalSolver.GetReachedCells(map, seed);
                            break;
                        case EnergyType.Hydraulic:
                            typeReached = HydraulicSolver.GetReachedCells(map, seed,
                                grid.Width, grid.Height, grid);
                            break;
                    }
                    if (typeReached == null) continue;
                    foreach (var reached in typeReached)
                        if (!expanded.ContainsKey(reached))
                            expanded[reached] = type;
                }
            }
        }
        return expanded;
    }
    public static HashSet<Vector2Int> BuildPumpCells(GridManager grid)
    {
        var pumps = new HashSet<Vector2Int>();
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isPump) continue;
            foreach (var cell in piece.WorldCells())
                if (grid.IsInBounds(cell.localCoord))
                    pumps.Add(cell.localCoord);
        }
        return pumps;
    }
    public static Dictionary<Vector2Int, List<PieceData.EnergyChannel>> BuildConductMap(GridManager grid)
    {
        var map = new Dictionary<Vector2Int, List<PieceData.EnergyChannel>>();
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            foreach (var cell in piece.WorldCells())
            {
                if (cell.energyChannels == null || cell.energyChannels.Count == 0) continue;
                if (!grid.IsInBounds(cell.localCoord)) continue;
                if (!map.TryGetValue(cell.localCoord, out var list))
                {
                    list = new List<PieceData.EnergyChannel>();
                    map[cell.localCoord] = list;
                }
                list.AddRange(cell.energyChannels);
            }
        }
        return map;
    }
    static bool HasSide(List<PieceData.EnergyChannel> ch, EnergyType type,
                        PieceData.ConnectionSides side, bool isOut)
    {
        foreach (var c in ch)
        {
            if (c.type != type) continue;
            var s = isOut ? c.conductOut : c.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }
}