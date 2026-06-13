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
        public string pieceDataName; // nome del PieceData asset
        public Vector2Int gridPosition;
        public int rotation;
        public int runtimeLength;

        [Tooltip("True se il pezzo è una cinghia")]
        public bool isBelt = false;
        [Tooltip("Secondo ingranaggio collegato dalla cinghia. (-1,-1) = solo ancorata")]
        public Vector2Int beltEndCell = new Vector2Int(-1, -1);
    }

    public bool solved = false;
    public List<PlacedPieceData> pieces = new();
}