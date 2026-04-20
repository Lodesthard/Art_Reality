using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.Reflection;

public class RebuildCanvasEditor
{
    [MenuItem("ArtReality/Rebuild Drawing Canvas")]
    public static void RebuildCanvas()
    {
        var oldCanvas = GameObject.Find("DrawingCanvas");
        if (oldCanvas != null) Object.DestroyImmediate(oldCanvas);

        // --- Canvas ---
        var canvasGo = new GameObject("DrawingCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f; // match width so portrait phones scale cleanly
        canvasGo.AddComponent<GraphicRaycaster>();

        // Safe-area wrapper: everything lives under this so we respect notch / gesture bar.
        var safeArea = new GameObject("SafeArea");
        safeArea.transform.SetParent(canvasGo.transform, false);
        var saRect = safeArea.AddComponent<RectTransform>();
        saRect.anchorMin = Vector2.zero;
        saRect.anchorMax = Vector2.one;
        saRect.offsetMin = Vector2.zero;
        saRect.offsetMax = Vector2.zero;
        safeArea.AddComponent<SafeAreaFitter>();

        // =============================================
        // TOP BAR (GPS + mode)
        // =============================================
        var topBar = MakePanel(safeArea.transform, "TopBar",
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            Vector2.zero, new Vector2(0, 90));
        topBar.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);

        var gpsText = MakeText(topBar.transform, "GpsStatusText", "GPS...", TextAnchor.MiddleLeft, 28);
        var gpsRect = gpsText.GetComponent<RectTransform>();
        gpsRect.anchorMin = new Vector2(0, 0); gpsRect.anchorMax = new Vector2(0.5f, 1);
        gpsRect.offsetMin = new Vector2(24, 0); gpsRect.offsetMax = new Vector2(0, 0);

        var modeText = MakeText(topBar.transform, "ModeStatusText", "Mode: Dessin", TextAnchor.MiddleRight, 28);
        var modeRect = modeText.GetComponent<RectTransform>();
        modeRect.anchorMin = new Vector2(0.5f, 0); modeRect.anchorMax = new Vector2(1, 1);
        modeRect.offsetMin = new Vector2(0, 0); modeRect.offsetMax = new Vector2(-24, 0);

        // =============================================
        // TOOLBAR (bottom) — two rows, responsive
        // =============================================
        var toolbar = MakePanel(safeArea.transform, "Toolbar",
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
            Vector2.zero, new Vector2(0, 320));
        toolbar.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

        var toolbarVLG = toolbar.AddComponent<VerticalLayoutGroup>();
        toolbarVLG.spacing = 10;
        toolbarVLG.padding = new RectOffset(18, 18, 14, 14);
        toolbarVLG.childAlignment = TextAnchor.MiddleCenter;
        toolbarVLG.childControlWidth = true;
        toolbarVLG.childControlHeight = false;
        toolbarVLG.childForceExpandWidth = true;
        toolbarVLG.childForceExpandHeight = false;

        // Row 1: mode tools + brush
        var row1 = MakeRow(toolbar.transform, "ModeRow");

        // Color toggle (current color swatch)
        var colorToggle = MakeToolButton(row1.transform, "ColorMenuToggle", "", new Color(0.3f, 0.3f, 0.3f));
        var previewGo = new GameObject("SelectedColorPreview");
        previewGo.transform.SetParent(colorToggle.transform, false);
        var previewRect = previewGo.AddComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(10, 10);
        previewRect.offsetMax = new Vector2(-10, -10);
        var previewImg = previewGo.AddComponent<Image>();
        previewImg.color = Color.red;
        previewImg.raycastTarget = false;

        var drawBtn = MakeToolButton(row1.transform, "DrawButton", "Dessin", new Color(0.3f, 0.7f, 1f));
        var eraserBtn = MakeToolButton(row1.transform, "EraserButton", "Gomme", new Color(0.85f, 0.85f, 0.85f));
        var moveBtn = MakeToolButton(row1.transform, "MoveButton", "Deplacer", new Color(0.85f, 0.85f, 0.85f));

        // Brush slider takes more flexible space
        var sliderGo = MakeSlider(row1.transform, "BrushSizeSlider");
        var sliderLE = sliderGo.GetComponent<LayoutElement>();
        sliderLE.flexibleWidth = 2f;

        // Row 2: action buttons
        var row2 = MakeRow(toolbar.transform, "ActionRow");
        MakeActionButton(row2.transform, "UndoButton", "Annuler", new Color(0.9f, 0.6f, 0.1f));
        MakeActionButton(row2.transform, "ClearButton", "Effacer tout", new Color(0.8f, 0.2f, 0.2f));
        MakeActionButton(row2.transform, "SaveButton", "Sauver", new Color(0.2f, 0.7f, 0.3f));
        MakeActionButton(row2.transform, "LoadButton", "Charger", new Color(0.2f, 0.5f, 0.8f));

        // =============================================
        // COLOR MENU PANEL (floating above toolbar)
        // =============================================
        var colorPanel = MakePanel(safeArea.transform, "ColorMenuPanel",
            new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
            new Vector2(0, 340), new Vector2(720, 140));
        colorPanel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        colorPanel.SetActive(false);

        var colorHLG = colorPanel.AddComponent<HorizontalLayoutGroup>();
        colorHLG.spacing = 18;
        colorHLG.padding = new RectOffset(18, 18, 14, 14);
        colorHLG.childAlignment = TextAnchor.MiddleCenter;
        colorHLG.childControlWidth = true;
        colorHLG.childControlHeight = true;
        colorHLG.childForceExpandWidth = true;
        colorHLG.childForceExpandHeight = true;

        Color[] colors = { Color.red, Color.blue, Color.green, Color.yellow, Color.white, Color.black };
        string[] names = { "RedButton", "BlueButton", "GreenButton", "YellowButton", "WhiteButton", "BlackButton" };
        for (int i = 0; i < colors.Length; i++)
            MakeColorButton(colorPanel.transform, names[i], colors[i]);

        // =============================================
        // CONFIRM DIALOG (centered, responsive)
        // =============================================
        var confirm = MakePanel(safeArea.transform, "ConfirmDialog",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(820, 340));
        confirm.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 0.97f);
        confirm.SetActive(false);

        var confirmText = MakeText(confirm.transform, "ConfirmText", "Effacer tous les dessins ?", TextAnchor.MiddleCenter, 40);
        var ctRect = confirmText.GetComponent<RectTransform>();
        ctRect.anchorMin = new Vector2(0, 0.45f); ctRect.anchorMax = new Vector2(1, 1);
        ctRect.offsetMin = new Vector2(30, 0); ctRect.offsetMax = new Vector2(-30, -20);

        var yesBtn = MakeActionButton(confirm.transform, "ConfirmYesButton", "Oui", new Color(0.8f, 0.2f, 0.2f));
        var yR = yesBtn.GetComponent<RectTransform>();
        yR.anchorMin = new Vector2(0.08f, 0.1f); yR.anchorMax = new Vector2(0.48f, 0.4f);
        yR.offsetMin = Vector2.zero; yR.offsetMax = Vector2.zero;
        Object.DestroyImmediate(yesBtn.GetComponent<LayoutElement>());

        var noBtn = MakeActionButton(confirm.transform, "ConfirmNoButton", "Non", new Color(0.3f, 0.3f, 0.3f));
        var nR = noBtn.GetComponent<RectTransform>();
        nR.anchorMin = new Vector2(0.52f, 0.1f); nR.anchorMax = new Vector2(0.92f, 0.4f);
        nR.offsetMin = Vector2.zero; nR.offsetMax = Vector2.zero;
        Object.DestroyImmediate(noBtn.GetComponent<LayoutElement>());

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
        Set(type, drawingUI, "drawButton", drawBtn.GetComponent<Button>(), flags);
        Set(type, drawingUI, "eraserButton", eraserBtn.GetComponent<Button>(), flags);
        Set(type, drawingUI, "moveButton", moveBtn.GetComponent<Button>(), flags);
        Set(type, drawingUI, "brushSizeSlider", sliderGo.GetComponent<Slider>(), flags);
        Set(type, drawingUI, "selectedColorPreview", previewImg, flags);
        Set(type, drawingUI, "gpsStatusText", gpsText, flags);
        Set(type, drawingUI, "modeStatusText", modeText, flags);
        Set(type, drawingUI, "confirmDialog", confirm, flags);
        Set(type, drawingUI, "confirmText", confirmText, flags);

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
                case "ConfirmYesButton": Set(type, drawingUI, "confirmYesButton", btn, flags); break;
                case "ConfirmNoButton": Set(type, drawingUI, "confirmNoButton", btn, flags); break;
            }
        }

        EditorUtility.SetDirty(canvasGo);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Debug.Log("[ArtReality] DrawingCanvas rebuilt (responsive, Move mode, safe-area).");
    }

    // --- Helpers ---

    static GameObject MakeRow(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 130;
        le.flexibleHeight = 0;
        return go;
    }

    static GameObject MakePanel(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = aMin; r.anchorMax = aMax; r.pivot = pivot;
        r.anchoredPosition = pos; r.sizeDelta = size;
        go.AddComponent<Image>();
        return go;
    }

    static GameObject MakeToolButton(Transform parent, string name, string label, Color bg)
    {
        var go = MakeButtonBase(parent, name, label, bg);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 100; le.preferredWidth = 140; le.flexibleWidth = 1;
        le.minHeight = 100; le.preferredHeight = 120; le.flexibleHeight = 0;
        return go;
    }

    static GameObject MakeActionButton(Transform parent, string name, string label, Color bg)
    {
        var go = MakeButtonBase(parent, name, label, bg);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 100; le.preferredWidth = 180; le.flexibleWidth = 1;
        le.minHeight = 90; le.preferredHeight = 110; le.flexibleHeight = 0;
        return go;
    }

    static GameObject MakeButtonBase(Transform parent, string name, string label, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = bg;
        go.AddComponent<Button>();
        go.AddComponent<LayoutElement>();

        if (!string.IsNullOrEmpty(label))
        {
            var tGo = new GameObject("Text");
            tGo.transform.SetParent(go.transform, false);
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one;
            tR.offsetMin = new Vector2(6, 4); tR.offsetMax = new Vector2(-6, -4);
            var txt = tGo.AddComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 28;
            txt.color = (bg.r + bg.g + bg.b) / 3f > 0.65f ? Color.black : Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 14;
            txt.resizeTextMaxSize = 36;
            txt.raycastTarget = false;
        }
        return go;
    }

    static void MakeColorButton(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        go.AddComponent<Button>();
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0.5f, 0.5f, 0.5f);
        ol.effectDistance = new Vector2(2, 2);
        ol.enabled = false;
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 80; le.preferredWidth = 110; le.flexibleWidth = 1;
        le.minHeight = 80; le.preferredHeight = 110; le.flexibleHeight = 1;
    }

    static Text MakeText(Transform parent, string name, string content, TextAnchor align, int fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var txt = go.AddComponent<Text>();
        txt.text = content;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.color = Color.white;
        txt.alignment = align;
        txt.raycastTarget = false;
        return txt;
    }

    static GameObject MakeSlider(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 200; le.preferredWidth = 360; le.flexibleWidth = 2;
        le.minHeight = 80; le.preferredHeight = 100; le.flexibleHeight = 0;

        var bg = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgR = bg.AddComponent<RectTransform>();
        bgR.anchorMin = new Vector2(0, 0.35f); bgR.anchorMax = new Vector2(1, 0.65f);
        bgR.offsetMin = Vector2.zero; bgR.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(go.transform, false);
        var faR = fillArea.AddComponent<RectTransform>();
        faR.anchorMin = new Vector2(0, 0.35f); faR.anchorMax = new Vector2(1, 0.65f);
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
        hR.sizeDelta = new Vector2(40, 0);
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
