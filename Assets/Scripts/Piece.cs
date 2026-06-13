using System.Collections.Generic;
using UnityEngine;

public class Piece
{
    public PieceData data;
    public int rotation = 0; // 0=0° 1=90° 2=180° 3=270°
    public Vector2Int gridPosition = new(-1, -1);
    public int? runtimeLength = null;
    public bool everMoved = false;
    /// <summary>Pezzo pre-piazzato dal livello (LevelPresetSpawner):
    /// non conta nell'inventario globale e non viene salvato/ripristinato.</summary>
    public bool isPreset = false;

    public Piece Clone() => new Piece
    {
        data = data,
        rotation = rotation,
        gridPosition = gridPosition,
        runtimeLength = runtimeLength,
        everMoved = everMoved,
        isPreset = isPreset,
    };

    /// <summary>Celle con coordinate già ruotate in spazio locale.</summary>
    public List<PieceData.CellDef> CurrentCells()
    {
        var source = BuildSourceCells();
        var result = new List<PieceData.CellDef>();
        foreach (var cell in source)
        {
            var rotated = cell;
            rotated.localCoord = RotateCoord(cell.localCoord, rotation);
            // RotateChannels crea sempre una nuova lista — safe
            rotated.energyChannels = RotateChannels(cell.energyChannels, rotation);
            result.Add(rotated);
        }
        return result;
    }

    /// <summary>Celle con coordinate mondo (gridPosition + localCoord ruotato).</summary>
    public List<PieceData.CellDef> WorldCells()
    {
        var local = CurrentCells();
        var result = new List<PieceData.CellDef>();
        foreach (var cell in local)
        {
            var w = cell;
            w.localCoord = cell.localCoord + gridPosition;
            result.Add(w);
        }
        return result;
    }

    // ── Rotazione coordinate ──────────────────────────────────────────────
    static Vector2Int RotateCoord(Vector2Int v, int rot) => rot switch
    {
        1 => new Vector2Int(v.y, -v.x),
        2 => new Vector2Int(-v.x, -v.y),
        3 => new Vector2Int(-v.y, v.x),
        _ => v
    };

    // ── Rotazione canali energetici ───────────────────────────────────────
    static List<PieceData.EnergyChannel> RotateChannels(
        List<PieceData.EnergyChannel> channels, int rot)
    {
        if (channels == null) return null;
        var result = new List<PieceData.EnergyChannel>();
        foreach (var ch in channels)
        {
            var r = ch;
            r.conductIn = RotateSides(ch.conductIn, rot);
            r.conductOut = RotateSides(ch.conductOut, rot);
            result.Add(r);
        }
        return result;
    }

    static PieceData.ConnectionSides RotateSides(PieceData.ConnectionSides s, int rot)
    {
        var r = s;
        for (int i = 0; i < rot; i++) r = RotateOnce(r); // ruota r, non s
        return r;
    }

    static PieceData.ConnectionSides RotateOnce(PieceData.ConnectionSides s)
    {
        var r = PieceData.ConnectionSides.None;
        if ((s & PieceData.ConnectionSides.Right) != 0) r |= PieceData.ConnectionSides.Down;
        if ((s & PieceData.ConnectionSides.Down) != 0) r |= PieceData.ConnectionSides.Left;
        if ((s & PieceData.ConnectionSides.Left) != 0) r |= PieceData.ConnectionSides.Up;
        if ((s & PieceData.ConnectionSides.Up) != 0) r |= PieceData.ConnectionSides.Right;
        return r;
    }

    // ── Build celle sorgente (molla) ──────────────────────────────────────
    List<PieceData.CellDef> BuildSourceCells()
    {
        if (runtimeLength == null || !data.resizable)
        {
            // Deep copy per evitare mutazione del PieceData originale
            var copy = new List<PieceData.CellDef>();
            foreach (var c in data.cells)
            {
                var cc = c;
                cc.energyChannels = c.energyChannels != null
                    ? new List<PieceData.EnergyChannel>(c.energyChannels)
                    : null;
                copy.Add(cc);
            }
            return copy;
        }

        int len = runtimeLength.Value;
        var template = data.cells;
        var result = new List<PieceData.CellDef>();

        var head = template[0];
        var body = template.Count > 2 ? template[1] : template[0];
        var tail = template[template.Count - 1];

        for (int i = 0; i < len; i++)
        {
            PieceData.CellDef cell = i == 0 ? head : (i == len - 1 ? tail : body);
            cell.localCoord = new Vector2Int(i, 0);

            // Deep copy dei canali — evita che la mutazione in RotateChannels
            // modifichi la lista originale nel PieceData (List è reference type)
            if (cell.energyChannels != null && cell.energyChannels.Count > 0)
                cell.energyChannels = new System.Collections.Generic.List<PieceData.EnergyChannel>(cell.energyChannels);
            else if (body.energyChannels != null && body.energyChannels.Count > 0)
                cell.energyChannels = new System.Collections.Generic.List<PieceData.EnergyChannel>(body.energyChannels);

            result.Add(cell);
        }
        return result;
    }
}