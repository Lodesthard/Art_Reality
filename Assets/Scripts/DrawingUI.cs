using UnityEngine;
using UnityEngine.UI;

public class DrawingUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WallDrawing wallDrawing;

    [Header("Color Menu")]
    [SerializeField] private Button colorMenuToggle;
    [SerializeField] private GameObject colorMenuPanel;
    [SerializeField] private Button redButton;
    [SerializeField] private Button blueButton;
    [SerializeField] private Button greenButton;
    [SerializeField] private Button yellowButton;
    [SerializeField] private Button whiteButton;
    [SerializeField] private Button blackButton;

    [Header("Tools")]
    [SerializeField] private Button drawButton;
    [SerializeField] private Button eraserButton;
    [SerializeField] private Button moveButton;
    [SerializeField] private Slider brushSizeSlider;

    [Header("Actions")]
    [SerializeField] private Button undoButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;

    [Header("Confirmation Dialog")]
    [SerializeField] private GameObject confirmDialog;
    [SerializeField] private Button confirmYesButton;
    [SerializeField] private Button confirmNoButton;
    [SerializeField] private Text confirmText;

    [Header("Feedback")]
    [SerializeField] private Image selectedColorPreview;
    [SerializeField] private Text gpsStatusText;
    [SerializeField] private Text modeStatusText;

    private Button currentSelectedColorButton;
    private Color lastColor;
    private string lastGpsStatus;
    private DrawingMode lastDisplayedMode = (DrawingMode)(-1);

    private static readonly Color ToolActiveColor = new Color(0.3f, 0.7f, 1f);
    private static readonly Color ToolIdleColor = new Color(0.85f, 0.85f, 0.85f);

    private void Start()
    {
        lastColor = wallDrawing.GetBrushColor();

        if (colorMenuPanel != null) colorMenuPanel.SetActive(false);
        if (confirmDialog != null) confirmDialog.SetActive(false);

        if (colorMenuToggle != null)
            colorMenuToggle.onClick.AddListener(ToggleColorMenu);

        SetupColorButton(redButton, Color.red);
        SetupColorButton(blueButton, Color.blue);
        SetupColorButton(greenButton, Color.green);
        SetupColorButton(yellowButton, Color.yellow);
        SetupColorButton(whiteButton, Color.white);
        SetupColorButton(blackButton, Color.black);

        if (drawButton != null)
            drawButton.onClick.AddListener(() => SetMode(DrawingMode.Draw));
        if (eraserButton != null)
            eraserButton.onClick.AddListener(() => SetMode(DrawingMode.Erase));
        if (moveButton != null)
            moveButton.onClick.AddListener(() => SetMode(DrawingMode.Move));

        if (brushSizeSlider != null)
        {
            brushSizeSlider.minValue = 0.002f;
            brushSizeSlider.maxValue = 0.05f;
            brushSizeSlider.value = wallDrawing.GetBrushSize();
            brushSizeSlider.onValueChanged.AddListener(v => wallDrawing.SetBrushSize(v));
        }

        if (undoButton != null) undoButton.onClick.AddListener(() => wallDrawing.Undo());
        if (clearButton != null) clearButton.onClick.AddListener(ShowClearConfirmation);
        if (saveButton != null) saveButton.onClick.AddListener(() => wallDrawing.SaveDrawing());
        if (loadButton != null) loadButton.onClick.AddListener(() => wallDrawing.LoadDrawing());

        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmClear);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(HideConfirmation);

        UpdateColorPreview(lastColor);
        RefreshModeButtons();
    }

    private void Update()
    {
        if (gpsStatusText != null)
        {
            var status = wallDrawing.GpsStatus;
            if (status != lastGpsStatus)
            {
                gpsStatusText.text = status;
                lastGpsStatus = status;
            }
        }

        if (modeStatusText != null)
        {
            var current = wallDrawing.GetMode();
            if (current != lastDisplayedMode)
            {
                modeStatusText.text = current switch
                {
                    DrawingMode.Draw => "Mode: Dessin",
                    DrawingMode.Erase => "Mode: Gomme",
                    DrawingMode.Move => "Mode: Deplacer",
                    _ => modeStatusText.text
                };
                lastDisplayedMode = current;
            }
        }
    }

    private void SetMode(DrawingMode mode)
    {
        wallDrawing.SetMode(mode);
        RefreshModeButtons();
    }

    private void RefreshModeButtons()
    {
        var current = wallDrawing.GetMode();
        Tint(drawButton, current == DrawingMode.Draw);
        Tint(eraserButton, current == DrawingMode.Erase);
        Tint(moveButton, current == DrawingMode.Move);
    }

    private static void Tint(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? ToolActiveColor : ToolIdleColor;
    }

    private void ShowClearConfirmation()
    {
        if (confirmDialog != null)
        {
            if (confirmText != null) confirmText.text = "Effacer tous les dessins ?";
            confirmDialog.SetActive(true);
        }
        else
        {
            wallDrawing.ClearAll();
        }
    }

    private void OnConfirmClear()
    {
        wallDrawing.ClearAll();
        HideConfirmation();
    }

    private void HideConfirmation()
    {
        if (confirmDialog != null) confirmDialog.SetActive(false);
    }

    private void ToggleColorMenu()
    {
        if (colorMenuPanel == null) return;
        colorMenuPanel.SetActive(!colorMenuPanel.activeSelf);
    }

    private void SetupColorButton(Button button, Color color)
    {
        if (button == null) return;

        Image img = button.GetComponent<Image>();
        if (img != null) img.color = color;

        button.onClick.AddListener(() =>
        {
            if (wallDrawing.GetMode() != DrawingMode.Draw) SetMode(DrawingMode.Draw);

            wallDrawing.SetBrushColor(color);
            lastColor = color;
            UpdateColorPreview(color);
            HighlightSelected(button);

            if (colorMenuPanel != null) colorMenuPanel.SetActive(false);
        });
    }

    private void UpdateColorPreview(Color color)
    {
        if (selectedColorPreview != null) selectedColorPreview.color = color;
    }

    private void HighlightSelected(Button button)
    {
        if (currentSelectedColorButton != null)
        {
            var o = currentSelectedColorButton.GetComponent<Outline>();
            if (o != null) o.enabled = false;
        }

        currentSelectedColorButton = button;
        var outline = button.GetComponent<Outline>();
        if (outline == null) outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(3, 3);
        outline.enabled = true;
    }
}
