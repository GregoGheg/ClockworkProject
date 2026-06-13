using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GearPuzzle/LevelData")]
public class LevelData : ScriptableObject
{
    public int gridWidth = 6;
    public int gridHeight = 6;

    [Tooltip("Queste coordinate sono disabilitate. Tutte le altre sono attive.")]
    public List<Vector2Int> inactiveCells = new();

    public Vector2Int circuitSource;
    public Vector2Int circuitDestination;

    [Tooltip("Tipi di energia che la sorgente emette. Vuoto = tutti e tre.")]
    public List<EnergyType> sourceEnergyTypes = new();

    [Tooltip("Tipi di energia che la destinazione accetta. Vuoto = tutti e tre.")]
    public List<EnergyType> destEnergyTypes = new();

    public bool SourceEmits(EnergyType type) =>
        sourceEnergyTypes.Count == 0 || sourceEnergyTypes.Contains(type);

    public bool DestAccepts(EnergyType type) =>
        destEnergyTypes.Count == 0 || destEnergyTypes.Contains(type);

    [Tooltip("Distanza in pixel tra i nodi della mappa per questo livello")]
    public float nodeSpacing = 1200f;

    [Tooltip("Dimensione in pixel di ogni cella della griglia per questo livello")]
    public float cellSize = 80f;

    [Tooltip("Offset in pixel della griglia rispetto al centro dello schermo. " +
             "X positivo = destra, Y positivo = su. " +
             "Modifica qui per spostare griglia E particle overlay insieme.")]
    public Vector2 gridOffset = Vector2.zero;

    public bool IsCellActive(Vector2Int coord) => !inactiveCells.Contains(coord);

    // ── Pezzi predefiniti del livello ─────────────────────────────────────
    [System.Serializable]
    public class PresetPiece
    {
        [Tooltip("PieceData del pezzo da spawnare")]
        public PieceData data;

        [Tooltip("Cella della griglia in cui piazzarlo")]
        public Vector2Int position;

        [Tooltip("Rotazione: 0=0° 1=90° 2=180° 3=270°")]
        [Range(0, 3)] public int rotation = 0;

        [Tooltip("Lunghezza per pezzi resizable (molla). 0 = lunghezza di default")]
        public int runtimeLength = 0;

        [Tooltip("Se true il pezzo è bloccato: il giocatore non può spostarlo né rimuoverlo")]
        public bool locked = false;

        [Tooltip("Solo per cinghie: gridPosition del secondo ingranaggio da collegare. (-1,-1) = solo ancorata")]
        public Vector2Int beltEnd = new Vector2Int(-1, -1);
    }

    [Tooltip("Pezzi già piazzati sulla griglia all'avvio del livello. Non consumano l'inventario globale. NOTA: elenca prima gli ingranaggi e poi le eventuali cinghie che li collegano.")]
    public List<PresetPiece> presetPieces = new();
}