using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

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

    [Header("Depth Detection")]
    [SerializeField] private float maxVerticalDot = 0.4f;

    private List<StrokeInstance> strokeInstances = new List<StrokeInstance>();
    private StrokeInstance currentStroke;
    private List<Vector3> currentLocalPoints = new List<Vector3>();
    private bool isDrawing;
    private bool isEraserMode;

    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private ARPlaneHighlight lastHighlighted;

    // Crosshair state for depth-based wall detection
    private bool isCrosshairOnWall;
    public bool IsCrosshairOnWall => isCrosshairOnWall;

    private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    private void Start()
    {
        EnhancedTouchSupport.Enable();
        LoadDrawing();
    }

    private void OnDestroy()
    {
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        UpdateWallDetection();

        if (Touch.activeTouches.Count == 0)
        {
            if (isDrawing) StopDrawing();
            return;
        }

        var touch = Touch.activeTouches[0];
        Vector2 screenPos = touch.screenPosition;

        if (IsPointerOverUI(screenPos)) return;

        if (Touch.activeTouches.Count > 1)
        {
            if (isDrawing) StopDrawing();
            return;
        }

        // Try depth raycast first (more accurate), fall back to plane raycast
        if (TryDepthRaycast(screenPos, out Vector3 worldPoint, out Vector3 normal))
        {
            Vector3 offsetPoint = worldPoint + normal * wallOffset;
            Quaternion wallRotation = Quaternion.LookRotation(-normal, Vector3.up);
            HandleDrawInput(touch, offsetPoint, wallRotation);
        }
        else if (TryPlaneRaycast(screenPos, out worldPoint, out normal))
        {
            Vector3 offsetPoint = worldPoint + normal * wallOffset;
            Quaternion wallRotation = Quaternion.LookRotation(-normal, Vector3.up);
            HandleDrawInput(touch, offsetPoint, wallRotation);
        }
    }

    private bool TryDepthRaycast(Vector2 screenPos, out Vector3 worldPoint, out Vector3 normal)
    {
        worldPoint = Vector3.zero;
        normal = Vector3.forward;

        if (!arRaycastManager.Raycast(screenPos, hits, TrackableType.Depth))
            return false;

        var hitPose = hits[0].pose;
        Vector3 surfaceNormal = hitPose.up;

        // Make sure normal points towards camera
        Vector3 toCamera = (Camera.main.transform.position - hitPose.position).normalized;
        if (Vector3.Dot(surfaceNormal, toCamera) < 0)
            surfaceNormal = -surfaceNormal;

        // Check if surface is vertical (normal is mostly horizontal)
        float verticalDot = Mathf.Abs(Vector3.Dot(surfaceNormal, Vector3.up));
        if (verticalDot > maxVerticalDot)
            return false;

        worldPoint = hitPose.position;
        normal = surfaceNormal;
        return true;
    }

    private bool TryPlaneRaycast(Vector2 screenPos, out Vector3 worldPoint, out Vector3 normal)
    {
        worldPoint = Vector3.zero;
        normal = Vector3.forward;

        if (!arRaycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            return false;

        ARPlane plane = arPlaneManager.GetPlane(hits[0].trackableId);
        if (!ARPlaneHighlight.IsWall(plane))
            return false;

        worldPoint = hits[0].pose.position;
        normal = plane.normal;
        return true;
    }

    private void HandleDrawInput(UnityEngine.InputSystem.EnhancedTouch.Touch touch, Vector3 worldPoint, Quaternion wallRotation)
    {
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

    private void UpdateWallDetection()
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        // Check depth first
        bool wallFound = TryDepthRaycast(screenCenter, out _, out _);

        // Fall back to plane check
        if (!wallFound)
        {
            if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
            {
                ARPlane plane = arPlaneManager.GetPlane(hits[0].trackableId);
                wallFound = ARPlaneHighlight.IsWall(plane);
            }
        }

        isCrosshairOnWall = wallFound;

        // Also update plane highlighting
        UpdatePlaneHighlight(screenCenter);
    }

    private void UpdatePlaneHighlight(Vector2 screenCenter)
    {
        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            ARPlane plane = arPlaneManager.GetPlane(hits[0].trackableId);
            if (plane != null && ARPlaneHighlight.IsWall(plane))
            {
                var highlight = plane.GetComponent<ARPlaneHighlight>();
                if (highlight != null && highlight != lastHighlighted)
                {
                    if (lastHighlighted != null) lastHighlighted.SetHighlighted(false);
                    highlight.SetHighlighted(true);
                    lastHighlighted = highlight;
                }
                return;
            }
        }

        if (lastHighlighted != null)
        {
            lastHighlighted.SetHighlighted(false);
            lastHighlighted = null;
        }
    }

    private void StartDrawing(Vector3 worldPoint, Quaternion wallRotation)
    {
        isDrawing = true;
        currentLocalPoints.Clear();

        var anchorGo = new GameObject("StrokeAnchor_" + strokeInstances.Count);
        anchorGo.transform.SetPositionAndRotation(worldPoint, wallRotation);
        var anchor = anchorGo.AddComponent<ARAnchor>();

        var strokeGo = new GameObject("Stroke");
        strokeGo.transform.SetParent(anchor.transform, false);

        var lr = strokeGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        ConfigureLineRenderer(lr);

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
            strokeInstances.Remove(currentStroke);
            if (currentStroke.anchor != null) Destroy(currentStroke.anchor.gameObject);
        }

        isDrawing = false;
        currentStroke = null;
        currentLocalPoints.Clear();
    }

    private Material CreateLineMaterial()
    {
        if (lineMaterial != null) return new Material(lineMaterial);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.SetFloat("_Surface", 0);
        mat.SetInt("_ZWrite", 1);
        // Disable depth test so strokes are fully visible (not partially hidden by wall depth)
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        return mat;
    }

    private void ConfigureLineRenderer(LineRenderer lr)
    {
        lr.material = CreateLineMaterial();
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

        foreach (var s in strokeInstances)
        {
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        }
        strokeInstances.Clear();

        for (int i = 0; i < saveData.strokes.Count; i++)
        {
            var data = saveData.strokes[i];
            if (data.points == null || data.points.Count < 2) continue;

            Vector3 anchorPos = new Vector3(data.anchorPosX, data.anchorPosY, data.anchorPosZ);
            Quaternion anchorRot = new Quaternion(data.anchorRotX, data.anchorRotY, data.anchorRotZ, data.anchorRotW);
            Color color = new Color(data.colorR, data.colorG, data.colorB, data.colorA);

            var anchorGo = new GameObject("StrokeAnchor_" + i);
            anchorGo.transform.SetPositionAndRotation(anchorPos, anchorRot);
            var anchor = anchorGo.AddComponent<ARAnchor>();

            var strokeGo = new GameObject("Stroke");
            strokeGo.transform.SetParent(anchor.transform, false);

            var lr = strokeGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = CreateLineMaterial();
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
