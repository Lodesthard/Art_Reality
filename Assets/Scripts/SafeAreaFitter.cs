using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private ScreenOrientation lastOrientation;
    private Vector2Int lastScreenSize;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafeArea
            || Screen.orientation != lastOrientation
            || Screen.width != lastScreenSize.x
            || Screen.height != lastScreenSize.y)
        {
            Apply();
        }
    }

    private void Apply()
    {
        var safe = Screen.safeArea;
        Vector2 anchorMin = safe.position;
        Vector2 anchorMax = safe.position + safe.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        lastSafeArea = safe;
        lastOrientation = Screen.orientation;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }
}
