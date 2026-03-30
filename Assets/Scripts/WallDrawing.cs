using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class WallDrawing : MonoBehaviour
{
    [Header("AR References")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private ARPlaneManager arPlaneManager;
    [SerializeField] private ARAnchorManager arAnchorManager;

    [Header("Drawing Settings")]
    [SerializeField] private float brushSize = 0.005f;
    [SerializeField] private Color brushColor = Color.red;
    [SerializeField] private Material lineMaterial;

    [Header("Line Settings")]
    [SerializeField] private float minDistanceBetweenPoints = 0.005f;
    [SerializeField] private float wallOffset = 0.001f;

    [Header("Save Settings")]
    [SerializeField] private bool autoSave = true;
    [SerializeField] private string saveFileName = "art_reality_save.json";

    [Header("Eraser Settings")]
    [SerializeField] private float eraserRadius = 0.03f;

    private List<StrokeInstance> strokeInstances = new List<StrokeInstance>();
    private StrokeInstance currentStroke;
    private List<Vector3> currentLocalPoints = new List<Vector3>();
    private bool isDrawing;
    private bool isEraserMode;

    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    private void Start()
    {
        LoadDrawing();
    }

    private void Update()
    {
        if (Input.touchCount == 0)
        {
            if (isDrawing) StopDrawing();
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (IsPointerOverUI(touch.position)) return;

        if (Input.touchCount > 1)
        {
            if (isDrawing) StopDrawing();
            return;
        }

        if (arRaycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
        {
            ARRaycastHit hit = hits[0];
            ARPlane plane = arPlaneManager.GetPlane(hit.trackableId);

            if (plane == null || plane.alignment != PlaneAlignment.Vertical)
                return;

            Vector3 worldPoint = hit.pose.position + plane.normal * wallOffset;
            Quaternion wallRotation = Quaternion.LookRotation(-plane.normal, Vector3.up);

            if (isEraserMode)
            {
                if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved)
                    EraseAtPoint(worldPoint);
                return;
            }

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    StartDrawing(worldPoint, wallRotation);
                    break;
                case TouchPhase.Moved:
                    ContinueDrawing(worldPoint);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    StopDrawing();
                    break;
            }
        }
    }

    private void StartDrawing(Vector3 worldPoint, Quaternion wallRotation)
    {
        isDrawing = true;
        currentLocalPoints.Clear();

        // Create an AR Anchor at the start point, oriented to face away from the wall
        var anchorGo = new GameObject("StrokeAnchor_" + strokeInstances.Count);
        anchorGo.transform.SetPositionAndRotation(worldPoint, wallRotation);
        var anchor = anchorGo.AddComponent<ARAnchor>();

        // Create stroke as child of anchor -> moves with the anchor
        var strokeGo = new GameObject("Stroke");
        strokeGo.transform.SetParent(anchor.transform, false);

        var lr = strokeGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; // Points are in anchor-local space
        ConfigureLineRenderer(lr);

        // First point at local origin (anchor is at worldPoint)
        Vector3 localPoint = Vector3.zero;
        currentLocalPoints.Add(localPoint);
        lr.positionCount = 1;
        lr.SetPosition(0, localPoint);

        currentStroke = new StrokeInstance
        {
            anchor = anchor,
            strokeObject = strokeGo,
            lineRenderer = lr,
            color = brushColor,
            size = brushSize
        };
        strokeInstances.Add(currentStroke);
    }

    private void ContinueDrawing(Vector3 worldPoint)
    {
        if (!isDrawing || currentStroke == null || currentStroke.anchor == null) return;

        // Convert world point to anchor-local space
        Vector3 localPoint = currentStroke.anchor.transform.InverseTransformPoint(worldPoint);

        if (currentLocalPoints.Count > 0)
        {
            float dist = Vector3.Distance(localPoint, currentLocalPoints[currentLocalPoints.Count - 1]);
            if (dist < minDistanceBetweenPoints) return;
        }

        currentLocalPoints.Add(localPoint);
        currentStroke.lineRenderer.positionCount = currentLocalPoints.Count;
        currentStroke.lineRenderer.SetPosition(currentLocalPoints.Count - 1, localPoint);
    }

    private void StopDrawing()
    {
        if (isDrawing && currentLocalPoints.Count > 1 && currentStroke != null)
        {
            currentStroke.localPoints = new List<Vector3>(currentLocalPoints);
            if (autoSave) SaveDrawing();
        }
        else if (isDrawing && currentLocalPoints.Count <= 1 && currentStroke != null)
        {
            // Remove single-point strokes (just a tap, no line)
            strokeInstances.Remove(currentStroke);
            if (currentStroke.anchor != null) Destroy(currentStroke.anchor.gameObject);
        }

        isDrawing = false;
        currentStroke = null;
        currentLocalPoints.Clear();
    }

    private void ConfigureLineRenderer(LineRenderer lr)
    {
        lr.material = lineMaterial != null ? new Material(lineMaterial) : new Material(Shader.Find("Sprites/Default"));
        lr.material.color = brushColor;
        lr.startWidth = brushSize;
        lr.endWidth = brushSize;
        lr.numCornerVertices = 5;
        lr.numCapVertices = 5;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = screenPosition;
        List<RaycastResult> results = new List<RaycastResult>();
        if (EventSystem.current != null)
            EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }

    // --- Eraser ---

    private void EraseAtPoint(Vector3 worldPoint)
    {
        for (int i = strokeInstances.Count - 1; i >= 0; i--)
        {
            var stroke = strokeInstances[i];
            if (stroke.anchor == null || stroke.localPoints == null) continue;

            foreach (var localPt in stroke.localPoints)
            {
                Vector3 worldPt = stroke.anchor.transform.TransformPoint(localPt);
                if (Vector3.Distance(worldPt, worldPoint) < eraserRadius)
                {
                    strokeInstances.RemoveAt(i);
                    Destroy(stroke.anchor.gameObject);
                    if (autoSave) SaveDrawing();
                    break;
                }
            }
        }
    }

    // --- Public API for UI ---

    public void SetBrushColor(Color color) => brushColor = color;

    public void SetBrushSize(float size) => brushSize = Mathf.Clamp(size, 0.002f, 0.05f);

    public void SetEraserMode(bool enabled) => isEraserMode = enabled;
    public bool GetEraserMode() => isEraserMode;

    public float GetBrushSize() => brushSize;
    public Color GetBrushColor() => brushColor;

    public void Undo()
    {
        if (strokeInstances.Count == 0) return;
        var last = strokeInstances[strokeInstances.Count - 1];
        strokeInstances.RemoveAt(strokeInstances.Count - 1);
        if (last.anchor != null) Destroy(last.anchor.gameObject);
        if (autoSave) SaveDrawing();
    }

    public void ClearAll()
    {
        foreach (var s in strokeInstances)
        {
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        }
        strokeInstances.Clear();
        if (autoSave) SaveDrawing();
    }

    // --- Save / Load ---

    public void SaveDrawing()
    {
        var saveData = new DrawingSaveData();
        foreach (var s in strokeInstances)
        {
            if (s.anchor == null || s.localPoints == null || s.localPoints.Count < 2) continue;

            var data = new StrokeData
            {
                anchorPosX = s.anchor.transform.position.x,
                anchorPosY = s.anchor.transform.position.y,
                anchorPosZ = s.anchor.transform.position.z,
                anchorRotX = s.anchor.transform.rotation.x,
                anchorRotY = s.anchor.transform.rotation.y,
                anchorRotZ = s.anchor.transform.rotation.z,
                anchorRotW = s.anchor.transform.rotation.w,
                colorR = s.color.r,
                colorG = s.color.g,
                colorB = s.color.b,
                colorA = s.color.a,
                size = s.size,
                points = new List<SerializableVector3>()
            };

            foreach (var p in s.localPoints)
                data.points.Add(new SerializableVector3 { x = p.x, y = p.y, z = p.z });

            saveData.strokes.Add(data);
        }

        string json = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"[ArtReality] Saved {saveData.strokes.Count} strokes -> {SavePath}");
    }

    public void LoadDrawing()
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[ArtReality] No save file found.");
            return;
        }

        string json = File.ReadAllText(SavePath);
        var saveData = JsonUtility.FromJson<DrawingSaveData>(json);
        if (saveData == null || saveData.strokes == null || saveData.strokes.Count == 0) return;

        // Clear existing
        foreach (var s in strokeInstances)
        {
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        }
        strokeInstances.Clear();

        // Recreate each stroke with its anchor
        for (int i = 0; i < saveData.strokes.Count; i++)
        {
            var data = saveData.strokes[i];
            if (data.points == null || data.points.Count < 2) continue;

            Vector3 anchorPos = new Vector3(data.anchorPosX, data.anchorPosY, data.anchorPosZ);
            Quaternion anchorRot = new Quaternion(data.anchorRotX, data.anchorRotY, data.anchorRotZ, data.anchorRotW);
            Color color = new Color(data.colorR, data.colorG, data.colorB, data.colorA);

            // Recreate anchor
            var anchorGo = new GameObject("StrokeAnchor_" + i);
            anchorGo.transform.SetPositionAndRotation(anchorPos, anchorRot);
            var anchor = anchorGo.AddComponent<ARAnchor>();

            // Recreate stroke as child
            var strokeGo = new GameObject("Stroke");
            strokeGo.transform.SetParent(anchor.transform, false);

            var lr = strokeGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = lineMaterial != null ? new Material(lineMaterial) : new Material(Shader.Find("Sprites/Default"));
            lr.material.color = color;
            lr.startWidth = data.size;
            lr.endWidth = data.size;
            lr.numCornerVertices = 5;
            lr.numCapVertices = 5;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var localPoints = new List<Vector3>();
            lr.positionCount = data.points.Count;
            for (int j = 0; j < data.points.Count; j++)
            {
                var p = data.points[j];
                var v = new Vector3(p.x, p.y, p.z);
                localPoints.Add(v);
                lr.SetPosition(j, v);
            }

            strokeInstances.Add(new StrokeInstance
            {
                anchor = anchor,
                strokeObject = strokeGo,
                lineRenderer = lr,
                color = color,
                size = data.size,
                localPoints = localPoints
            });
        }

        Debug.Log($"[ArtReality] Loaded {strokeInstances.Count} strokes");
    }

    public bool HasSaveFile() => File.Exists(SavePath);

    public void DeleteSave()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log("[ArtReality] Save file deleted.");
        }
    }
}

// --- Data classes ---

public class StrokeInstance
{
    public ARAnchor anchor;
    public GameObject strokeObject;
    public LineRenderer lineRenderer;
    public Color color;
    public float size;
    public List<Vector3> localPoints;
}

[Serializable]
public class SerializableVector3
{
    public float x, y, z;
}

[Serializable]
public class StrokeData
{
    public float anchorPosX, anchorPosY, anchorPosZ;
    public float anchorRotX, anchorRotY, anchorRotZ, anchorRotW;
    public float colorR, colorG, colorB, colorA;
    public float size;
    public List<SerializableVector3> points;
}

[Serializable]
public class DrawingSaveData
{
    public List<StrokeData> strokes = new List<StrokeData>();
}
