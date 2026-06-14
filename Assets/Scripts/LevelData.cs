using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GearPuzzle/LevelData")]
public class LevelData : ScriptableObject
{
    public int gridWidth = 6;
    public int gridHeight = 6;

    [Tooltip("Queste coordinate sono disabilitate. Tutte le altre sono attive.")]
    public List<Vector2Int> inactiveCells = new();

    [Header("Sorgente / destinazione (LEGACY - singola)")]
    [Tooltip("LEGACY: usato solo se la lista 'sources' è vuota.")]
    public Vector2Int circuitSource;
    [Tooltip("LEGACY: usato solo se la lista 'destinations' è vuota.")]
    public Vector2Int circuitDestination;

    [Tooltip("LEGACY: tipi emessi dalla sorgente singola. Vuoto = tutti e tre.")]
    public List<EnergyType> sourceEnergyTypes = new();

    [Tooltip("LEGACY: tipi accettati dalla destinazione singola. Vuoto = tutti e tre.")]
    public List<EnergyType> destEnergyTypes = new();

    // ── Multi-sorgente / multi-destinazione ───────────────────────────────
    [System.Serializable]
    public class EnergySource
    {
        [Tooltip("Cella della griglia che emette energia (blocco conduttore, occupa spazio)")]
        public Vector2Int position;
        [Tooltip("Tipi di energia emessi. Vuoto = tutti e tre.")]
        public List<EnergyType> energyTypes = new();

        [Tooltip("OPZIONALE: se impostato, su questa cella di start appare una freccia " +
                 "di RITORNO al livello con questa mapPosition (solo se quel livello è già " +
                 "stato visitato). Lascia (9999,9999) per nessun ritorno.")]
        public Vector2Int returnsToLevelAtMapPosition = new Vector2Int(9999, 9999);

        public bool HasReturn => returnsToLevelAtMapPosition != new Vector2Int(9999, 9999);

        public bool Emits(EnergyType t)
        {
            if (energyTypes == null || energyTypes.Count == 0) return true;
            bool hasBase = energyTypes.Contains(EnergyType.Mechanical)
                        || energyTypes.Contains(EnergyType.Hydraulic)
                        || energyTypes.Contains(EnergyType.Electric);
            if (!hasBase) return true;
            return energyTypes.Contains(t);
        }
    }

    [System.Serializable]
    public class EnergyDestination
    {
        [Tooltip("Cella della griglia che riceve energia (blocco conduttore, occupa spazio)")]
        public Vector2Int position;
        [Tooltip("Tipi di energia accettati. Vuoto = tutti e tre.")]
        public List<EnergyType> energyTypes = new();

        [Tooltip("Quando questa destinazione è soddisfatta, sblocca il livello con questa mapPosition. " +
                 "Lascia (9999,9999) se non sblocca nulla.")]
        public Vector2Int unlocksLevelAtMapPosition = new Vector2Int(9999, 9999);

        public bool Accepts(EnergyType t)
        {
            if (energyTypes == null || energyTypes.Count == 0) return true;
            bool hasBase = energyTypes.Contains(EnergyType.Mechanical)
                        || energyTypes.Contains(EnergyType.Hydraulic)
                        || energyTypes.Contains(EnergyType.Electric);
            if (!hasBase) return true;
            return energyTypes.Contains(t);
        }
        public bool HasUnlock => unlocksLevelAtMapPosition != new Vector2Int(9999, 9999);
    }

    [Tooltip("Sorgenti di energia del livello. Se vuota, usa circuitSource (legacy).")]
    public List<EnergySource> sources = new();

    [Tooltip("Destinazioni di energia del livello. Se vuota, usa circuitDestination (legacy).")]
    public List<EnergyDestination> destinations = new();

    // ── Accessor unificati (legacy + nuovo) ───────────────────────────────
    /// <summary>Tutte le sorgenti, includendo la legacy se la lista è vuota.</summary>
    public List<EnergySource> GetSources()
    {
        if (sources != null && sources.Count > 0) return sources;
        return new List<EnergySource> {
            new EnergySource { position = circuitSource, energyTypes = sourceEnergyTypes }
        };
    }

    /// <summary>Tutte le destinazioni, includendo la legacy se la lista è vuota.</summary>
    public List<EnergyDestination> GetDestinations()
    {
        if (destinations != null && destinations.Count > 0) return destinations;
        return new List<EnergyDestination> {
            new EnergyDestination { position = circuitDestination, energyTypes = destEnergyTypes }
        };
    }

    public bool IsSourceCell(Vector2Int c)
    {
        foreach (var s in GetSources()) if (s.position == c) return true;
        return false;
    }

    public bool IsDestCell(Vector2Int c)
    {
        foreach (var d in GetDestinations()) if (d.position == c) return true;
        return false;
    }

    // LEGACY: alcuni solver chiamano ancora questi. Significano "qualche sorgente
    // emette / qualche destinazione accetta questo tipo".
    public bool SourceEmits(EnergyType type)
    {
        foreach (var s in GetSources()) if (s.Emits(type)) return true;
        return false;
    }

    public bool DestAccepts(EnergyType type)
    {
        foreach (var d in GetDestinations()) if (d.Accepts(type)) return true;
        return false;
    }

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