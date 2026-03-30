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
    [SerializeField] private Button eraserButton;
    [SerializeField] private Slider brushSizeSlider;

    [Header("Actions")]
    [SerializeField] private Button undoButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;

    [Header("Feedback")]
    [SerializeField] private Image selectedColorPreview;

    private Button currentSelectedButton;
    private bool isEraserActive;
    private Color lastColor;
    private Image eraserButtonImage;

    private void Start()
    {
        lastColor = wallDrawing.GetBrushColor();

        // Color menu starts hidden
        if (colorMenuPanel != null)
            colorMenuPanel.SetActive(false);

        // Toggle color menu
        if (colorMenuToggle != null)
            colorMenuToggle.onClick.AddListener(ToggleColorMenu);

        // Color buttons
        SetupColorButton(redButton, Color.red);
        SetupColorButton(blueButton, Color.blue);
        SetupColorButton(greenButton, Color.green);
        SetupColorButton(yellowButton, Color.yellow);
        SetupColorButton(whiteButton, Color.white);
        SetupColorButton(blackButton, Color.black);

        // Eraser
        if (eraserButton != null)
        {
            eraserButtonImage = eraserButton.GetComponent<Image>();
            eraserButton.onClick.AddListener(ToggleEraser);
        }

        // Brush size
        if (brushSizeSlider != null)
        {
            brushSizeSlider.minValue = 0.002f;
            brushSizeSlider.maxValue = 0.05f;
            brushSizeSlider.value = wallDrawing.GetBrushSize();
            brushSizeSlider.onValueChanged.AddListener(v => wallDrawing.SetBrushSize(v));
        }

        // Action buttons
        if (undoButton != null)
            undoButton.onClick.AddListener(() => wallDrawing.Undo());
        if (clearButton != null)
            clearButton.onClick.AddListener(() => wallDrawing.ClearAll());
        if (saveButton != null)
            saveButton.onClick.AddListener(() => wallDrawing.SaveDrawing());
        if (loadButton != null)
            loadButton.onClick.AddListener(() => wallDrawing.LoadDrawing());

        UpdateColorPreview(lastColor);
    }

    private void ToggleColorMenu()
    {
        if (colorMenuPanel == null) return;
        bool show = !colorMenuPanel.activeSelf;
        colorMenuPanel.SetActive(show);
    }

    private void SetupColorButton(Button button, Color color)
    {
        if (button == null) return;

        Image img = button.GetComponent<Image>();
        if (img != null) img.color = color;

        button.onClick.AddListener(() =>
        {
            // Exit eraser mode when picking a color
            if (isEraserActive) DeactivateEraser();

            wallDrawing.SetBrushColor(color);
            lastColor = color;
            UpdateColorPreview(color);
            HighlightSelected(button);

            // Close menu after picking
            if (colorMenuPanel != null)
                colorMenuPanel.SetActive(false);
        });
    }

    private void ToggleEraser()
    {
        if (isEraserActive)
            DeactivateEraser();
        else
            ActivateEraser();
    }

    private void ActivateEraser()
    {
        isEraserActive = true;
        wallDrawing.SetEraserMode(true);

        if (eraserButtonImage != null)
            eraserButtonImage.color = new Color(1f, 0.4f, 0.4f);

        // Reset color button highlight
        if (currentSelectedButton != null)
        {
            var outline = currentSelectedButton.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            currentSelectedButton = null;
        }
    }

    private void DeactivateEraser()
    {
        isEraserActive = false;
        wallDrawing.SetEraserMode(false);

        if (eraserButtonImage != null)
            eraserButtonImage.color = new Color(0.85f, 0.85f, 0.85f);
    }

    private void UpdateColorPreview(Color color)
    {
        if (selectedColorPreview != null)
            selectedColorPreview.color = color;
    }

    private void HighlightSelected(Button button)
    {
        if (currentSelectedButton != null)
        {
            var outline = currentSelectedButton.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        currentSelectedButton = button;
        var newOutline = button.GetComponent<Outline>();
        if (newOutline == null)
            newOutline = button.gameObject.AddComponent<Outline>();
        newOutline.effectColor = Color.white;
        newOutline.effectDistance = new Vector2(3, 3);
        newOutline.enabled = true;
    }
}
