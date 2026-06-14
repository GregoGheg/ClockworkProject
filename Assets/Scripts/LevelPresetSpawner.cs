using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawna sulla griglia i pezzi predefiniti del livello, definiti nella lista
/// "presetPieces" del LevelData (nuovo campo aggiunto).
///
/// COME USARLO:
/// 1. Attacca questo script sul PREFAB del GameManager (lo stesso GameObject
///    che ha il componente GameManager). Viene istanziato da LevelViewController
///    per ogni livello, quindi ogni livello spawna i propri preset.
/// 2. Nel LevelData del livello, compila la lista "Preset Pieces":
///    - data: il PieceData del pezzo
///    - position: cella della griglia
///    - rotation: 0-3
///    - runtimeLength: solo per pezzi resizable (0 = default)
///    - locked: se true il giocatore non può spostarlo né rimuoverlo
///    - beltEnd: SOLO per cinghie — gridPosition del secondo ingranaggio
///      (lascia (-1,-1) per una cinghia solo ancorata).
///
/// NOTE:
/// - I pezzi preset sono istanze EXTRA: non consumano l'inventario globale
///   (grazie al flag piece.isPreset, escluso da CountPlaced) e non vengono
///   salvati nel LevelSaveData (vengono rispawnati a ogni inizializzazione).
/// - Per le cinghie preset: metti nella lista PRIMA gli ingranaggi e POI la
///   cinghia che li collega, così quando la cinghia viene spawnata gli
///   ingranaggi sono già sulla griglia.
/// </summary>
[RequireComponent(typeof(GameManager))]
public class LevelPresetSpawner : MonoBehaviour
{
    GameManager gm;
    bool spawned = false;

    void Awake() => gm = GetComponent<GameManager>();

    // Fallback: se per qualche motivo SpawnNow non è stato chiamato
    // esplicitamente da LevelViewController, prova comunque allo Start.
    IEnumerator Start()
    {
        yield return null;
        yield return null;
        if (!spawned) SpawnNow();
    }

    /// <summary>
    /// Spawna i preset. Idempotente: chiamabile più volte, esegue una sola volta.
    /// Se la griglia non è ancora pronta, ritenta al frame successivo.
    /// </summary>
    public void SpawnNow()
    {
        if (spawned) return;
        if (gm == null) gm = GetComponent<GameManager>();

        // La griglia deve essere inizializzata (Awake girato, celle allocate)
        if (gm == null || gm.gridManager == null || !gm.gridManager.IsReady)
        {
            if (gameObject.activeInHierarchy)
                StartCoroutine(SpawnWhenReady());
            return;
        }
        SpawnPresets();
    }

    IEnumerator SpawnWhenReady()
    {
        int safety = 120;
        while (safety-- > 0)
        {
            if (gm != null && gm.gridManager != null && gm.gridManager.IsReady)
            {
                SpawnPresets();
                yield break;
            }
            yield return null;
        }
        Debug.LogWarning("[LevelPresetSpawner] Griglia mai pronta — preset non spawnati.");
    }

    void SpawnPresets()
    {
        var level = gm != null ? gm.currentLevel : null;
        var grid = gm != null ? gm.gridManager : null;

        if (level == null || grid == null)
        {
            Debug.LogWarning($"[LevelPresetSpawner] level o grid null (level={level}, grid={grid}) — preset non spawnati. Verrà ritentato.");
            return;
        }
        if (level.presetPieces == null || level.presetPieces.Count == 0)
        {
            spawned = true; // niente da fare, ma marca come fatto
            return;
        }
        if (gm.piecePrefab == null)
        {
            Debug.LogWarning("[LevelPresetSpawner] GameManager.piecePrefab non assegnato — impossibile spawnare i preset.");
            return;
        }

        spawned = true;
        Debug.Log($"[LevelPresetSpawner] Spawno {level.presetPieces.Count} preset per il livello.");

        foreach (var preset in level.presetPieces)
        {
            if (preset.data == null) continue;

            var piece = new Piece
            {
                data = preset.data,
                rotation = Mathf.Clamp(preset.rotation, 0, 3),
                gridPosition = new Vector2Int(-1, -1),
                isPreset = true,
            };
            if (preset.runtimeLength > 0 && preset.data.resizable)
                piece.runtimeLength = preset.runtimeLength;

            var dragger = Instantiate(gm.piecePrefab, grid.transform);
            dragger.name = $"Preset_{preset.data.name}_{preset.position.x}_{preset.position.y}";
            dragger.Setup(piece, grid);
            dragger.isBelt = preset.data.isBelt;
            dragger.everMoved = true;

            bool ok;
            if (preset.data.isBelt)
                ok = SpawnBelt(dragger, preset, grid);
            else
                ok = SpawnNormal(dragger, preset, grid);

            if (!ok)
            {
                Debug.LogWarning($"[LevelPresetSpawner] Impossibile piazzare il preset " +
                    $"'{preset.data.name}' in {preset.position} — cella occupata, inattiva o fuori griglia.");
                Destroy(dragger.gameObject);
                continue;
            }

            if (preset.locked) LockDragger(dragger);
        }

        grid.OnGridChanged?.Invoke();
    }

    bool SpawnNormal(PieceDragger dragger, LevelData.PresetPiece preset, GridManager grid)
    {
        if (!grid.TryPlace(dragger.piece, preset.position)) return false;
        dragger.SnapToGridPublic(preset.position);
        dragger.canvasGroup.alpha = 1f;
        dragger.canvasGroup.blocksRaycasts = true;
        return true;
    }

    bool SpawnBelt(PieceDragger dragger, LevelData.PresetPiece preset, GridManager grid)
    {
        // La cinghia si ancora su un ingranaggio già piazzato
        var cell = grid.GetCell(preset.position);
        if (cell?.occupant == null || !cell.occupant.data.isGear)
        {
            Debug.LogWarning($"[LevelPresetSpawner] Cinghia preset in {preset.position}: " +
                "nessun ingranaggio su quella cella. Elenca gli ingranaggi PRIMA della cinghia nella lista presetPieces.");
            return false;
        }

        dragger.piece.gridPosition = cell.occupant.gridPosition;
        dragger.canvasGroup.alpha = 1f;
        dragger.canvasGroup.blocksRaycasts = true;

        if (preset.beltEnd.x >= 0)
        {
            var endCellState = grid.GetCell(preset.beltEnd);
            if (endCellState?.occupant != null && endCellState.occupant.data.isGear)
            {
                dragger.beltEndCell = endCellState.occupant.gridPosition;
                dragger.StretchBetween(dragger.piece.gridPosition, dragger.beltEndCell);
                return true;
            }
            Debug.LogWarning($"[LevelPresetSpawner] Cinghia preset: nessun ingranaggio in beltEnd {preset.beltEnd} — la cinghia resta solo ancorata.");
        }

        dragger.beltEndCell = new Vector2Int(-1, -1);
        dragger.ShowAnchoredVisual();
        return true;
    }

    void LockDragger(PieceDragger dragger)
    {
        // Niente interazioni: il root Image non riceve più raycast
        // e i gestori di eventi del dragger vengono disattivati.
        var img = dragger.GetComponent<Image>();
        if (img != null) img.raycastTarget = false;

        if (dragger.canvasGroup != null)
            dragger.canvasGroup.blocksRaycasts = false;

        dragger.enabled = false;

        // Disattiva anche eventuali componenti accessori di input
        var tracker = dragger.GetComponent<PreDragPositionTracker>();
        if (tracker != null) tracker.enabled = false;
        var swap = dragger.GetComponent<PieceSwapHandler>();
        if (swap != null) swap.enabled = false;
    }
}