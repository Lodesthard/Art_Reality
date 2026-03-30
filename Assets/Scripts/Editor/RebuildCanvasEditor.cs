using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.Reflection;

public class RebuildCanvasEditor
{
    [MenuItem("ArtReality/Rebuild Drawing Canvas")]
    public static void RebuildCanvas()
    {
        // Destroy existing DrawingCanvas
        var oldCanvas = GameObject.Find("DrawingCanvas");
        if (oldCanvas != null) Object.DestroyImmediate(oldCanvas);

        // --- Create Canvas ---
        var canvasGo = new GameObject("DrawingCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // =============================================
        // TOOLBAR (bottom bar)
        // =============================================
        var toolbar = MakePanel(canvasGo.transform, "Toolbar",
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
            Vector2.zero, new Vector2(0, 180));
        toolbar.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.88f);

        var toolbarHLG = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarHLG.spacing = 12;
        toolbarHLG.padding = new RectOffset(20, 20, 15, 15);
        toolbarHLG.childAlignment = TextAnchor.MiddleCenter;
        toolbarHLG.childForceExpandWidth = false;
        toolbarHLG.childForceExpandHeight = false;

        // Color menu toggle (shows current color)
        var colorToggle = MakeButton(toolbar.transform, "ColorMenuToggle", "", new Color(0.3f, 0.3f, 0.3f), 80, 80);
        var previewGo = new GameObject("SelectedColorPreview");
        previewGo.transform.SetParent(colorToggle.transform, false);
        var previewRect = previewGo.AddComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(8, 8);
        previewRect.offsetMax = new Vector2(-8, -8);
        var previewImg = previewGo.AddComponent<Image>();
        previewImg.color = Color.red;
        previewImg.raycastTarget = false;

        // Eraser button
        var eraserBtn = MakeButton(toolbar.transform, "EraserButton", "Gomme", new Color(0.85f, 0.85f, 0.85f), 140, 80);

        // Spacer
        MakeSpacer(toolbar.transform, 10);

        // Brush size slider
        var sliderGo = MakeSlider(toolbar.transform, "BrushSizeSlider", 300, 60);

        // Spacer
        MakeSpacer(toolbar.transform, 10);

        // Undo
        MakeButton(toolbar.transform, "UndoButton", "Undo", new Color(0.9f, 0.6f, 0.1f), 120, 70);
        // Clear
        MakeButton(toolbar.transform, "ClearButton", "Clear", new Color(0.8f, 0.2f, 0.2f), 120, 70);
        // Save
        MakeButton(toolbar.transform, "SaveButton", "Save", new Color(0.2f, 0.7f, 0.3f), 110, 70);
        // Load
        MakeButton(toolbar.transform, "LoadButton", "Load", new Color(0.2f, 0.5f, 0.8f), 110, 70);

        // =============================================
        // COLOR MENU PANEL (above toolbar, hidden)
        // =============================================
        var colorPanel = MakePanel(canvasGo.transform, "ColorMenuPanel",
            new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(20, 190), new Vector2(550, 110));
        colorPanel.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.92f);
        colorPanel.SetActive(false);

        var colorHLG = colorPanel.AddComponent<HorizontalLayoutGroup>();
        colorHLG.spacing = 12;
        colorHLG.padding = new RectOffset(15, 15, 12, 12);
        colorHLG.childAlignment = TextAnchor.MiddleCenter;
        colorHLG.childForceExpandWidth = false;
        colorHLG.childForceExpandHeight = false;

        Color[] colors = { Color.red, Color.blue, Color.green, Color.yellow, Color.white, Color.black };
        string[] names = { "RedButton", "BlueButton", "GreenButton", "YellowButton", "WhiteButton", "BlackButton" };
        for (int i = 0; i < colors.Length; i++)
            MakeColorButton(colorPanel.transform, names[i], colors[i], 75);

        // =============================================
        // Wire DrawingUI
        // =============================================
        var drawingUI = canvasGo.AddComponent<DrawingUI>();
        var wallDrawing = Object.FindObjectOfType<WallDrawing>();

        var type = typeof(DrawingUI);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        Set(type, drawingUI, "wallDrawing", wallDrawing, flags);
        Set(type, drawingUI, "colorMenuToggle", colorToggle.GetComponent<Button>(), flags);
        Set(type, drawingUI, "colorMenuPanel", colorPanel, flags);
        Set(type, drawingUI, "eraserButton", eraserBtn.GetComponent<Button>(), flags);
        Set(type, drawingUI, "brushSizeSlider", sliderGo.GetComponent<Slider>(), flags);
        Set(type, drawingUI, "selectedColorPreview", previewImg, flags);

        // Find and wire all buttons by name
        var allBtns = canvasGo.GetComponentsInChildren<Button>(true);
        foreach (var btn in allBtns)
        {
            switch (btn.gameObject.name)
            {
                case "RedButton": Set(type, drawingUI, "redButton", btn, flags); break;
                case "BlueButton": Set(type, drawingUI, "blueButton", btn, flags); break;
                case "GreenButton": Set(type, drawingUI, "greenButton", btn, flags); break;
                case "YellowButton": Set(type, drawingUI, "yellowButton", btn, flags); break;
                case "WhiteButton": Set(type, drawingUI, "whiteButton", btn, flags); break;
                case "BlackButton": Set(type, drawingUI, "blackButton", btn, flags); break;
                case "UndoButton": Set(type, drawingUI, "undoButton", btn, flags); break;
                case "ClearButton": Set(type, drawingUI, "clearButton", btn, flags); break;
                case "SaveButton": Set(type, drawingUI, "saveButton", btn, flags); break;
                case "LoadButton": Set(type, drawingUI, "loadButton", btn, flags); break;
            }
        }

        EditorUtility.SetDirty(canvasGo);
        Debug.Log("[ArtReality] DrawingCanvas rebuilt with color menu + eraser.");
    }

    static GameObject MakePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
        r.anchoredPosition = pos; r.sizeDelta = size;
        go.AddComponent<Image>();
        return go;
    }

    static GameObject MakeButton(Transform parent, string name, string label, Color bg, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>();
        img.color = bg;
        go.AddComponent<Button>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h;

        if (!string.IsNullOrEmpty(label))
        {
            var tGo = new GameObject("Text");
            tGo.transform.SetParent(go.transform, false);
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one;
            tR.offsetMin = Vector2.zero; tR.offsetMax = Vector2.zero;
            var txt = tGo.AddComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 24;
            txt.color = (label == "Gomme") ? Color.black : Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
        }
        return go;
    }

    static void MakeColorButton(Transform parent, string name, Color color, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.color = color;
        go.AddComponent<Button>();
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0.5f, 0.5f, 0.5f);
        ol.effectDistance = new Vector2(2, 2);
        ol.enabled = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.preferredHeight = size;
    }

    static void MakeSpacer(Transform parent, float width)
    {
        var go = new GameObject("Spacer");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
    }

    static GameObject MakeSlider(Transform parent, string name, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h;

        var bg = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgR = bg.AddComponent<RectTransform>();
        bgR.anchorMin = new Vector2(0, 0.25f); bgR.anchorMax = new Vector2(1, 0.75f);
        bgR.offsetMin = Vector2.zero; bgR.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        var faR = fillArea.AddComponent<RectTransform>();
        faR.anchorMin = new Vector2(0, 0.25f); faR.anchorMax = new Vector2(1, 0.75f);
        faR.offsetMin = new Vector2(10, 0); faR.offsetMax = new Vector2(-10, 0);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fR = fill.AddComponent<RectTransform>();
        fR.anchorMin = Vector2.zero; fR.anchorMax = new Vector2(0.5f, 1);
        fR.offsetMin = Vector2.zero; fR.offsetMax = Vector2.zero;
        fill.AddComponent<Image>().color = new Color(0.3f, 0.7f, 1f);

        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(go.transform, false);
        var haR = handleArea.AddComponent<RectTransform>();
        haR.anchorMin = Vector2.zero; haR.anchorMax = Vector2.one;
        haR.offsetMin = new Vector2(10, 0); haR.offsetMax = new Vector2(-10, 0);

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var hR = handle.AddComponent<RectTransform>();
        hR.sizeDelta = new Vector2(30, 0);
        hR.anchorMin = new Vector2(0.5f, 0); hR.anchorMax = new Vector2(0.5f, 1);
        var hImg = handle.AddComponent<Image>();
        hImg.color = Color.white;

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fR;
        slider.handleRect = hR;
        slider.targetGraphic = hImg;
        slider.minValue = 0.002f;
        slider.maxValue = 0.05f;
        slider.value = 0.005f;

        return go;
    }

    static void Set(System.Type type, object obj, string name, object value, BindingFlags flags)
    {
        var f = type.GetField(name, flags);
        if (f != null) f.SetValue(obj, value);
    }
}
