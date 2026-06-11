using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject che descrive la mappa del mondo:
/// ogni livello ha una posizione nella mappa e i suoi dati.
/// </summary>
[CreateAssetMenu(menuName = "GearPuzzle/WorldLevelConfig")]
public class WorldLevelConfig : ScriptableObject
{
    [System.Serializable]
    public class LevelEntry
    {
        [Tooltip("Dati del livello (griglia, source/dest) — NON include pezzi, sono globali")]
        public LevelData levelData;

        [Tooltip("Posizione nella mappa (in unità griglia, es. 0,0 = centro, 1,0 = a destra)")]
        public Vector2Int mapPosition;

        [Tooltip("Nome visualizzato (es. 'Torso', 'Testa')")]
        public string displayName;

        [Tooltip("Colore di sfondo del nodo nella mappa")]
        public Color nodeColor = new Color(0.2f, 0.3f, 0.4f, 1f);
    }

    [System.Serializable]
    public class GlobalPieceEntry
    {
        public PieceData data;
        [Min(1)] public int quantity;
    }

    public LevelEntry[] levels;

    [Tooltip("Inventario globale condiviso tra tutti i livelli")]
    public List<GlobalPieceEntry> globalPieces = new();

    [Tooltip("Indice del livello iniziale")]
    public int startLevelIndex = 0;

    [Tooltip("Velocità di scroll del tray (globale per tutti i livelli)")]
    public float trayScrollSpeed = 200f;
}