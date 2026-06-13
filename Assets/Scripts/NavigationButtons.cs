using UnityEngine;
using UnityEngine.UI;

public class NavigationButtons : MonoBehaviour
{
    public WorldNavigator navigator;

    public Button btnLeft;
    public Button btnRight;
    public Button btnUp;
    public Button btnDown;

    void Start()
    {
        if (btnLeft) btnLeft.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.left));
        if (btnRight) btnRight.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.right));
        if (btnUp) btnUp.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.up));
        if (btnDown) btnDown.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.down));
    }

    void Update()
    {
        if (navigator == null || navigator.config == null) return;

        int cur = navigator.CurrentIndex;
        var curPos = navigator.config.levels[cur].mapPosition;

        // Circuito attualmente attivo (live)
        bool currentlyActive = navigator.IsCurrentLevelSolved();

        if (btnLeft) btnLeft.gameObject.SetActive(CanNavigate(curPos, Vector2Int.left, currentlyActive));
        if (btnRight) btnRight.gameObject.SetActive(CanNavigate(curPos, Vector2Int.right, currentlyActive));
        if (btnUp) btnUp.gameObject.SetActive(CanNavigate(curPos, Vector2Int.up, currentlyActive));
        if (btnDown) btnDown.gameObject.SetActive(CanNavigate(curPos, Vector2Int.down, currentlyActive));
    }

    bool CanNavigate(Vector2Int curPos, Vector2Int dir, bool currentlyActive)
    {
        int cur = navigator.CurrentIndex;
        var targetPos = curPos + dir;

        for (int i = 0; i < navigator.config.levels.Length; i++)
        {
            if (navigator.config.levels[i].mapPosition != targetPos) continue;

            // Indietro (verso il parent nel tree) = sempre visibile
            bool isBack = navigator.IsParent(i, cur);
            if (isBack) return true;

            // Avanti = visibile se la zona è stata sbloccata da una destinazione
            // soddisfatta, oppure (fallback legacy) se il circuito è attivo ora.
            if (navigator.IsZoneUnlocked(targetPos)) return true;
            return currentlyActive;
        }
        return false;
    }
}