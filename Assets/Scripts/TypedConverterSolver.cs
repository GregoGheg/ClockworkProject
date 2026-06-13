using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gestisce i convertitori tipizzati: pezzi con isTypedConverter = true
/// che trasformano un'energia specifica (converterInputType) in un'altra
/// (converterOutputType).
///
/// Schema EnergyChannel da configurare nel PieceData:
///   Cella 0:
///     Channel 0: type = converterInputType,  conductIn  = lati di ingresso
///     Channel 1: type = converterOutputType, conductOut = lati di uscita
///
/// Tutte e 6 le combinazioni (M→E, M→H, E→M, E→H, H→M, H→E)
/// usano lo stesso schema — basta cambiare i due campi nel PieceData.
/// </summary>
public static class TypedConverterSolver
{
    static readonly (Vector2Int dir,
                     PieceData.ConnectionSides outS,
                     PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    /// <summary>
    /// Restituisce tutte le celle raggiungibili attraverso i convertitori tipizzati,
    /// con il tipo di energia in uscita.
    /// Key = coordinata cella, Value = tipo di energia che la raggiunge.
    /// </summary>
    public static Dictionary<Vector2Int, EnergyType> GetFlow(
        GridManager grid, Vector2Int source)
    {
        var result = new Dictionary<Vector2Int, EnergyType>();
        if (grid == null) return result;

        // Trova tutti i convertitori tipizzati piazzati
        var converters = new List<(Piece piece, EnergyType inType, EnergyType outType)>();
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isTypedConverter) continue;
            converters.Add((piece, piece.data.converterInputType, piece.data.converterOutputType));
        }
        if (converters.Count == 0) return result;

        var conductMap = CircuitSolver.BuildConductMap(grid);

        // Esegui più passate finché non ci sono nuove celle
        // (un convertitore potrebbe ricevere energia da un altro convertitore)
        bool anyNew = true;
        int safetyLimit = converters.Count + 2;
        while (anyNew && safetyLimit-- > 0)
        {
            anyNew = false;
            foreach (var (piece, inType, outType) in converters)
                if (TryActivate(piece, inType, outType, source, conductMap, result, grid))
                    anyNew = true;
        }
        return result;
    }

    static bool TryActivate(
        Piece piece, EnergyType inType, EnergyType outType,
        Vector2Int source,
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Dictionary<Vector2Int, EnergyType> result,
        GridManager grid)
    {
        bool activated = false;

        // Calcola celle già raggiunte dal tipo di input (senza ricorsione sui tipizzati)
        var inputReached = GetBaseReached(grid, source, inType, conductMap);
        // Aggiungi anche celle dell'output prodotto da altri convertitori già attivi
        // così una catena A→B→C funziona
        foreach (var kv in result)
            if (kv.Value == inType) inputReached.Add(kv.Key);

        foreach (var cell in piece.WorldCells())
        {
            if (cell.energyChannels == null) continue;

            // Controlla se questa cella ha un canale IN del tipo di input
            bool hasInput = false;
            foreach (var ch in cell.energyChannels)
                if (ch.type == inType && ch.conductIn != PieceData.ConnectionSides.None)
                { hasInput = true; break; }
            if (!hasInput) continue;

            var worldCoord = cell.localCoord; // WorldCells() già in spazio mondo

            // La cella è raggiunta dall'energia di input? (cella stessa o vicino)
            bool reached = inputReached.Contains(worldCoord);
            if (!reached)
            {
                // Controlla se un vicino emette verso questa cella
                foreach (var (dir, outS, inS) in Dirs)
                {
                    var neighbor = worldCoord + dir;
                    if (!inputReached.Contains(neighbor)) continue;
                    if (!conductMap.TryGetValue(neighbor, out var nbCh)) continue;
                    if (!HasSide(nbCh, inType, outS, isOut: true)) continue;
                    // Controlla che la cella del convertitore accetti su questo lato
                    if (!conductMap.TryGetValue(worldCoord, out var myCh)) continue;
                    if (!HasSide(myCh, inType, inS, isOut: false)) continue;
                    reached = true;
                    break;
                }
            }
            if (!reached) continue;

            // Il convertitore è attivato: propaga l'energia di output
            // da tutte le celle OUT del pezzo
            foreach (var outCell in piece.WorldCells())
            {
                if (outCell.energyChannels == null) continue;
                foreach (var ch in outCell.energyChannels)
                {
                    if (ch.type != outType) continue;
                    if (ch.conductOut == PieceData.ConnectionSides.None) continue;

                    // Propaga BFS dal lato di uscita
                    activated |= PropagateOutput(outCell.localCoord, ch.conductOut,
                        outType, conductMap, result, grid);
                    break;
                }
            }
            break;
        }
        return activated;
    }

    static bool PropagateOutput(
        Vector2Int startCoord,
        PieceData.ConnectionSides outSides,
        EnergyType outType,
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Dictionary<Vector2Int, EnergyType> result,
        GridManager grid)
    {
        bool anyNew = false;
        var queue = new Queue<Vector2Int>();

        void TryAdd(Vector2Int c)
        {
            if (result.ContainsKey(c)) return;
            if (!grid.IsInBounds(c)) return;
            result[c] = outType;
            anyNew = true;
            queue.Enqueue(c);
        }

        // Seed: le celle adiacenti al lato di uscita
        foreach (var (dir, outS, inS) in Dirs)
        {
            if ((outSides & outS) == 0) continue;
            var neighbor = startCoord + dir;
            if (!grid.IsInBounds(neighbor)) continue;
            if (!conductMap.TryGetValue(neighbor, out var nbCh)) continue;
            if (!HasSide(nbCh, outType, inS, isOut: false)) continue;
            TryAdd(neighbor);
        }

        // BFS standard del tipo di output
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (result.ContainsKey(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;
                if (!HasSide(curCh, outType, outS, isOut: true)) continue;
                if (!HasSide(nextCh, outType, inS, isOut: false)) continue;
                TryAdd(next);
            }
        }
        return anyNew;
    }

    /// <summary>
    /// Calcola le celle raggiunte dal tipo base (senza invocare TypedConverterSolver
    /// per evitare ricorsione infinita).
    /// </summary>
    static HashSet<Vector2Int> GetBaseReached(
        GridManager grid, Vector2Int source, EnergyType type,
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap)
    {
        if (!grid.level.SourceEmits(type)) return new HashSet<Vector2Int>();

        if (type == EnergyType.Mechanical)
        {
            var map2 = new Dictionary<Vector2Int, List<PieceData.EnergyChannel>>(conductMap);
            MechanicalSolver.AddBeltGearsToMap(map2, grid);
            var r = MechanicalSolver.GetReachedCells(map2, source);
            BeltSolver.PropagateReached(r, grid);
            return r;
        }
        if (type == EnergyType.Electric)
            return ElectricSolver.GetReachedCells(conductMap, source);
        if (type == EnergyType.Hydraulic)
            return HydraulicSolver.GetReachedCells(conductMap, source,
                grid.Width, grid.Height, grid);
        return new HashSet<Vector2Int>();
    }

    static bool HasSide(List<PieceData.EnergyChannel> channels,
                        EnergyType type, PieceData.ConnectionSides side, bool isOut)
    {
        foreach (var ch in channels)
        {
            if (ch.type != type) continue;
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }
}
