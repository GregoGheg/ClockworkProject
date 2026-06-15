using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anima l'apertura di un baule:
/// 1. Cambia lo sprite del baule (chiuso → aperto, da PieceData.chestOpenSprite).
/// 2. Fa apparire i pezzi-reward sopra il baule.
/// 3. Dopo qualche secondo il baule si rimpicciolisce e viene rimosso.
/// 4. I reward volano verso la posizione root del tray; quando arrivano
///    vengono disattivati e il counter del tray aumenta (illusione assorbimento).
///
/// SETUP: attacca sullo stesso GameObject del GameManager (prefab).
/// </summary>
[RequireComponent(typeof(GameManager))]
public class ChestAnimator : MonoBehaviour
{
    [Header("Tempistiche")]
    [Tooltip("Secondi che il baule aperto resta visibile prima di rimpicciolirsi")]
    public float openHold = 1.2f;
    [Tooltip("Durata del rimpicciolimento del baule")]
    public float shrinkDuration = 0.4f;
    [Tooltip("Durata del volo di ogni reward verso il tray")]
    public float flyDuration = 0.6f;
    [Tooltip("Ritardo tra il volo di un reward e il successivo")]
    public float rewardStagger = 0.15f;

    [Header("Aspetto reward volante")]
    [Tooltip("Dimensione in pixel del pezzo volante")]
    public float rewardSize = 70f;
    [Tooltip("Curva di scala durante il volo (1 = costante)")]
    public AnimationCurve flyScale = AnimationCurve.EaseInOut(0, 1f, 1, 0.5f);

    GameManager gm;
    Canvas rootCanvas;

    void Awake()
    {
        gm = GetComponent<GameManager>();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();
    }

    public void PlayChestOpen(Piece chestPiece, PieceDragger chestDragger, Transform trayRoot)
    {
        Debug.Log($"[ChestAnim] PlayChestOpen baule={chestPiece?.gridPosition} dragger={(chestDragger != null ? chestDragger.name : "NULL")} tray={(trayRoot != null ? trayRoot.name : "NULL")} openSprite={(chestPiece?.data?.chestOpenSprite != null ? "OK" : "MANCANTE")} rewards={chestPiece?.data?.chestRewards?.Count}");
        StartCoroutine(ChestRoutine(chestPiece, chestDragger, trayRoot));
    }

    IEnumerator ChestRoutine(Piece chestPiece, PieceDragger chestDragger, Transform trayRoot)
    {
        var data = chestPiece.data;

        // 1) Cambia sprite del baule → aperto
        Image chestImg = null;
        if (chestDragger != null)
        {
            var spriteT = chestDragger.transform.Find("piece_sprite");
            if (spriteT != null) chestImg = spriteT.GetComponent<Image>();
        }
        if (chestImg != null && data.chestOpenSprite != null)
            chestImg.sprite = data.chestOpenSprite;

        // 2) Attesa col baule aperto
        yield return new WaitForSeconds(openHold);

        // 3) Posizione di partenza dei reward = posizione del baule sullo schermo
        Vector2 startScreen = chestDragger != null
            ? (Vector2)chestDragger.transform.position
            : (Vector2)(Camera.main != null ? Camera.main.WorldToScreenPoint(transform.position) : Vector3.zero);

        // Posizione di arrivo = root del tray
        Vector2 trayScreen = trayRoot != null
            ? (Vector2)trayRoot.position
            : new Vector2(Screen.width * 0.5f, 40f);

        // 4) Lancia i reward in volo (staggered)
        var flying = new List<Coroutine>();
        float delay = 0f;
        foreach (var reward in data.chestRewards)
        {
            if (reward.data == null) continue;
            StartCoroutine(FlyReward(reward.data, reward.quantity, startScreen, trayScreen, delay));
            delay += rewardStagger;
        }

        // 5) Rimpicciolisci e rimuovi il baule (in parallelo)
        yield return StartCoroutine(ShrinkChest(chestDragger));

        // Il baule viene rimosso dalla griglia
        if (chestDragger != null)
        {
            gm.gridManager.Remove(chestPiece);
            Destroy(chestDragger.gameObject);
        }
    }

    IEnumerator ShrinkChest(PieceDragger chestDragger)
    {
        if (chestDragger == null) yield break;
        var t = chestDragger.transform;
        Vector3 start = t.localScale;
        float elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / shrinkDuration);
            t.localScale = Vector3.Lerp(start, Vector3.zero, k);
            yield return null;
        }
        t.localScale = Vector3.zero;
    }

    IEnumerator FlyReward(PieceData rewardData, int quantity,
                          Vector2 startScreen, Vector2 trayScreen, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // Crea l'icona volante come figlia del canvas root (sopra a tutto)
        var go = new GameObject($"RewardFly_{rewardData.name}");
        go.transform.SetParent(rootCanvas.transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = rewardData.pieceSprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = Vector2.one * rewardSize;
        rt.position = startScreen;

        // Badge quantità (se > 1)
        if (quantity > 1)
        {
            var badgeGo = new GameObject("xN");
            badgeGo.transform.SetParent(go.transform, false);
            var txt = badgeGo.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.text = $"×{quantity}";
            txt.fontSize = 22;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.LowerRight;
            txt.raycastTarget = false;
            var brt = badgeGo.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
        }

        // Volo con arco leggero
        Vector2 ctrl = (startScreen + trayScreen) * 0.5f + Vector2.up * 120f;
        float elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / flyDuration);
            float ease = 1f - (1f - k) * (1f - k); // ease-out
            // Bezier quadratica per l'arco
            Vector2 a = Vector2.Lerp(startScreen, ctrl, ease);
            Vector2 b = Vector2.Lerp(ctrl, trayScreen, ease);
            rt.position = Vector2.Lerp(a, b, ease);
            rt.localScale = Vector3.one * flyScale.Evaluate(k);
            yield return null;
        }

        // Arrivato al tray: assorbi → assegna il reward e distruggi l'icona
        gm.worldNavigator?.GrantReward(rewardData, quantity);
        Destroy(go);
    }
}