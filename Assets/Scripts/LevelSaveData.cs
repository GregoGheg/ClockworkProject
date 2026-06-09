using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stato salvato di un livello — pezzi piazzati, rotazioni, lunghezze.
/// Serializzabile per ScriptableObject o JSON.
/// </summary>
[System.Serializable]
public class LevelSaveData
{
    [System.Serializable]
    public class PlacedPieceData
    {
        public string     pieceDataName; // nome del PieceData asset
        public Vector2Int gridPosition;
        public int        rotation;
        public int        runtimeLength;
    }

    public bool                  solved = false;
    public List<PlacedPieceData> pieces = new();
}
