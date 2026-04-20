using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public enum DrawingMode { Draw, Erase, Move }

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

    [Header("Eraser / Move Settings")]
    [SerializeField] private float eraserRadius = 0.03f;
    [SerializeField] private float pickRadius = 0.08f;

    [Header("Wall Detection")]
    [SerializeField] private float maxVerticalDot = 0.6f;
    [SerializeField] private bool useEstimatedPlanes = true;

    [Header("GPS Settings")]
    [SerializeField] private float gpsLoadRadius = 50f;
    [SerializeField] private float gpsAccuracyDesired = 5f;
    [SerializeField] private float gpsUpdateDistance = 1f;

    [Header("Cloud Anchor Settings")]
    [SerializeField] private int cloudAnchorTtlDays = 365;
    [SerializeField] private bool useCloudAnchors = true;

    private List<StrokeInstance> strokeInstances = new List<StrokeInstance>();
    private StrokeInstance currentStroke;
    private List<Vector3> currentLocalPoints = new List<Vector3>();
    private bool isDrawing;
    private DrawingMode mode = DrawingMode.Draw;

    private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private static readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private static readonly TrackableType[] wallPlaneTypes = {
        TrackableType.PlaneWithinPolygon,
        TrackableType.PlaneEstimated,
        TrackableType.PlaneWithinBounds
    };
    private static Shader cachedLineShader;

    private ARPlaneHighlight lastHighlighted;
    private PointerEventData pointerEventData;
    private Camera mainCamera;

    private bool isCrosshairOnWall;
    public bool IsCrosshairOnWall => isCrosshairOnWall;

    // Strokes loaded from disk that haven't materialized yet (cloud anchor still resolving).
    // Keyed by a stable stroke ID so we can preserve them through save cycles.
    private Dictionary<string, StrokeData> pendingResolveData = new Dictionary<string, StrokeData>();

    // GPS state
    private bool gpsReady;
    private double currentLatitude;
    private double currentLongitude;
    public bool GpsReady => gpsReady;
    public string GpsStatus { get; private set; } = "Initialisation GPS...";

    // Cloud anchor hosting queue
    private Queue<StrokeInstance> hostingQueue = new Queue<StrokeInstance>();
    private bool isHosting;

    // Move mode state
    private StrokeInstance movingStroke;
    private Color movingOriginalColor;

    private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    private void Start()
    {
        EnhancedTouchSupport.Enable();
        mainCamera = Camera.main;
        StartCoroutine(InitGPS());
    }

    private void OnDestroy()
    {
        EnhancedTouchSupport.Disable();
        if (Input.location.isEnabledByUser)
            Input.location.Stop();
    }

    private IEnumerator InitGPS()
    {
        if (!Input.location.isEnabledByUser)
        {
            GpsStatus = "GPS desactive";
            Debug.LogWarning("[ArtReality] GPS disabled by user.");
            LoadDrawing();
            yield break;
        }

        Input.location.Start(gpsAccuracyDesired, gpsUpdateDistance);
        GpsStatus = "Demarrage GPS...";

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0 || Input.location.status == LocationServiceStatus.Failed)
        {
            GpsStatus = "GPS indisponible";
            Debug.LogWarning("[ArtReality] GPS failed.");
            LoadDrawing();
            yield break;
        }

        gpsReady = true;
        UpdateGPSPosition();
        GpsStatus = "GPS OK";
        Debug.Log($"[ArtReality] GPS ready: {currentLatitude}, {currentLongitude}");

        LoadDrawing();

        while (true)
        {
            yield return new WaitForSeconds(5f);
            UpdateGPSPosition();
        }
    }

    private void UpdateGPSPosition()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            var data = Input.location.lastData;
            currentLatitude = data.latitude;
            currentLongitude = data.longitude;

            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            double age = now - data.timestamp;
            GpsStatus = age > 60
                ? $"GPS (ancien: {age:F0}s)"
                : $"GPS OK ({data.horizontalAccuracy:F0}m)";
        }
    }

    private void Update()
    {
        UpdateWallDetection();

        if (Touch.activeTouches.Count == 0)
        {
            if (isDrawing) StopDrawing();
            if (movingStroke != null) EndMove(false);
            return;
        }

        var touch = Touch.activeTouches[0];
        Vector2 screenPos = touch.screenPosition;

        if (IsPointerOverUI(screenPos)) return;

        if (Touch.activeTouches.Count > 1)
        {
            if (isDrawing) StopDrawing();
            if (movingStroke != null) EndMove(false);
            return;
        }

        bool hit = TryDepthRaycast(screenPos, out Vector3 worldPoint, out Vector3 normal)
                || TryPlaneRaycast(screenPos, out worldPoint, out normal)
                || TryEstimateWallFromCamera(screenPos, out worldPoint, out normal);

        if (!hit) return;

        Vector3 offsetPoint = worldPoint + normal * wallOffset;
        Quaternion wallRotation = Quaternion.LookRotation(-normal, Vector3.up);

        switch (mode)
        {
            case DrawingMode.Draw:
                HandleDrawInput(touch, offsetPoint, wallRotation);
                break;
            case DrawingMode.Erase:
                if (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved)
                    EraseAtPoint(offsetPoint);
                break;
            case DrawingMode.Move:
                HandleMoveInput(touch, offsetPoint, wallRotation);
                break;
        }
    }

    // --- Wall Detection Raycasts ---

    private bool TryDepthRaycast(Vector2 screenPos, out Vector3 worldPoint, out Vector3 normal)
    {
        worldPoint = Vector3.zero;
        normal = Vector3.forward;

        if (mainCamera == null) return false;
        if (!arRaycastManager.Raycast(screenPos, hits, TrackableType.Depth))
            return false;

        var hitPose = hits[0].pose;
        Vector3 surfaceNormal = hitPose.up;

        Vector3 toCamera = (mainCamera.transform.position - hitPose.position).normalized;
        if (Vector3.Dot(surfaceNormal, toCamera) < 0)
            surfaceNormal = -surfaceNormal;

        if (Mathf.Abs(Vector3.Dot(surfaceNormal, Vector3.up)) > maxVerticalDot)
            return false;

        worldPoint = hitPose.position;
        normal = surfaceNormal;
        return true;
    }

    private bool TryPlaneRaycastOfType(Vector2 screenPos, TrackableType type, out Vector3 worldPoint, out Vector3 normal)
    {
        worldPoint = Vector3.zero;
        normal = Vector3.forward;

        if (!arRaycastManager.Raycast(screenPos, hits, type)) return false;

        ARPlane plane = arPlaneManager.GetPlane(hits[0].trackableId);
        if (plane == null || !ARPlaneHighlight.IsWall(plane)) return false;

        worldPoint = hits[0].pose.position;
        normal = plane.normal;
        return true;
    }

    private bool TryPlaneRaycast(Vector2 screenPos, out Vector3 worldPoint, out Vector3 normal)
    {
        if (TryPlaneRaycastOfType(screenPos, TrackableType.PlaneWithinPolygon, out worldPoint, out normal)) return true;
        if (useEstimatedPlanes && TryPlaneRaycastOfType(screenPos, TrackableType.PlaneEstimated, out worldPoint, out normal)) return true;
        if (useEstimatedPlanes && TryPlaneRaycastOfType(screenPos, TrackableType.PlaneWithinBounds, out worldPoint, out normal)) return true;
        return false;
    }

    private bool TryEstimateWallFromCamera(Vector2 screenPos, out Vector3 worldPoint, out Vector3 normal)
    {
        worldPoint = Vector3.zero;
        normal = Vector3.forward;

        if (mainCamera == null) return false;

        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit physHit, 5f)) return false;

        if (Mathf.Abs(Vector3.Dot(physHit.normal, Vector3.up)) > maxVerticalDot) return false;

        worldPoint = physHit.point;
        normal = physHit.normal;
        return true;
    }

    // --- Drawing ---

    private void HandleDrawInput(Touch touch, Vector3 worldPoint, Quaternion wallRotation)
    {
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

        bool wallFound = TryDepthRaycast(screenCenter, out _, out _)
                      || TryPlaneRaycast(screenCenter, out _, out _)
                      || TryEstimateWallFromCamera(screenCenter, out _, out _);

        isCrosshairOnWall = wallFound;
        UpdatePlaneHighlight(screenCenter);
    }

    private void UpdatePlaneHighlight(Vector2 screenCenter)
    {
        ARPlane foundPlane = null;
        foreach (var planeType in wallPlaneTypes)
        {
            if (!arRaycastManager.Raycast(screenCenter, hits, planeType)) continue;
            var plane = arPlaneManager.GetPlane(hits[0].trackableId);
            if (plane != null && ARPlaneHighlight.IsWall(plane))
            {
                foundPlane = plane;
                break;
            }
        }

        var highlight = foundPlane != null ? foundPlane.GetComponent<ARPlaneHighlight>() : null;
        if (highlight == lastHighlighted) return;

        if (lastHighlighted != null) lastHighlighted.SetHighlighted(false);
        if (highlight != null) highlight.SetHighlighted(true);
        lastHighlighted = highlight;
    }

    private void StartDrawing(Vector3 worldPoint, Quaternion wallRotation)
    {
        isDrawing = true;
        currentLocalPoints.Clear();
        UpdateGPSPosition();

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
            size = brushSize,
            latitude = currentLatitude,
            longitude = currentLongitude,
            localId = Guid.NewGuid().ToString("N")
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

            if (useCloudAnchors)
            {
                hostingQueue.Enqueue(currentStroke);
                if (!isHosting) StartCoroutine(ProcessHostingQueue());
            }

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

    // --- Move Mode ---

    private void HandleMoveInput(Touch touch, Vector3 worldPoint, Quaternion wallRotation)
    {
        switch (touch.phase)
        {
            case TouchPhase.Began:
                StartMove(worldPoint);
                break;
            case TouchPhase.Moved:
                UpdateMove(worldPoint, wallRotation);
                break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                EndMove(touch.phase == TouchPhase.Ended);
                break;
        }
    }

    private void StartMove(Vector3 worldPoint)
    {
        movingStroke = PickStroke(worldPoint);
        if (movingStroke == null) return;

        movingOriginalColor = movingStroke.color;
        if (movingStroke.lineRenderer != null && movingStroke.lineRenderer.material != null)
            movingStroke.lineRenderer.material.color = Color.Lerp(movingOriginalColor, Color.white, 0.4f);
    }

    private void UpdateMove(Vector3 worldPoint, Quaternion wallRotation)
    {
        if (movingStroke == null || movingStroke.anchor == null) return;
        // Live-preview: move the whole anchor GameObject to follow the finger.
        movingStroke.anchor.transform.SetPositionAndRotation(worldPoint, wallRotation);
    }

    private void EndMove(bool commit)
    {
        if (movingStroke == null) return;

        if (movingStroke.lineRenderer != null && movingStroke.lineRenderer.material != null)
            movingStroke.lineRenderer.material.color = movingOriginalColor;

        if (commit && movingStroke.anchor != null)
        {
            // Rebuild anchor at the new pose so ARAnchor tracks from there.
            var pos = movingStroke.anchor.transform.position;
            var rot = movingStroke.anchor.transform.rotation;
            RebuildAnchor(movingStroke, pos, rot);

            // Moving invalidates the cloud anchor — clear ID and re-host.
            if (!string.IsNullOrEmpty(movingStroke.cloudAnchorId))
            {
                pendingResolveData.Remove(movingStroke.cloudAnchorId);
                movingStroke.cloudAnchorId = null;
                if (useCloudAnchors)
                {
                    hostingQueue.Enqueue(movingStroke);
                    if (!isHosting) StartCoroutine(ProcessHostingQueue());
                }
            }

            if (autoSave) SaveDrawing();
        }

        movingStroke = null;
    }

    private StrokeInstance PickStroke(Vector3 worldPoint)
    {
        StrokeInstance best = null;
        float bestDist = pickRadius;

        foreach (var s in strokeInstances)
        {
            if (s.anchor == null || s.localPoints == null) continue;
            foreach (var lp in s.localPoints)
            {
                Vector3 wp = s.anchor.transform.TransformPoint(lp);
                float d = Vector3.Distance(wp, worldPoint);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }
        }
        return best;
    }

    private void RebuildAnchor(StrokeInstance stroke, Vector3 pos, Quaternion rot)
    {
        if (stroke.strokeObject == null) return;

        var newAnchorGo = new GameObject("StrokeAnchor_" + stroke.localId);
        newAnchorGo.transform.SetPositionAndRotation(pos, rot);
        var newAnchor = newAnchorGo.AddComponent<ARAnchor>();

        // Reparent stroke, preserve local points unchanged.
        stroke.strokeObject.transform.SetParent(newAnchor.transform, false);
        stroke.strokeObject.transform.localPosition = Vector3.zero;
        stroke.strokeObject.transform.localRotation = Quaternion.identity;

        if (stroke.anchor != null) Destroy(stroke.anchor.gameObject);
        stroke.anchor = newAnchor;
    }

    // --- Cloud Anchors ---

    private IEnumerator ProcessHostingQueue()
    {
        isHosting = true;

        while (hostingQueue.Count > 0)
        {
            var stroke = hostingQueue.Dequeue();
            if (stroke == null || stroke.anchor == null) continue;
            if (!string.IsNullOrEmpty(stroke.cloudAnchorId)) continue;

            var quality = arAnchorManager.EstimateFeatureMapQualityForHosting(
                new Pose(stroke.anchor.transform.position, stroke.anchor.transform.rotation));

            if (quality == FeatureMapQuality.Insufficient)
            {
                Debug.LogWarning("[ArtReality] Feature map quality insufficient, retrying in 2s...");
                yield return new WaitForSeconds(2f);
                hostingQueue.Enqueue(stroke);
                continue;
            }

            Debug.Log($"[ArtReality] Hosting cloud anchor (quality: {quality})...");
            var hostPromise = arAnchorManager.HostCloudAnchorAsync(stroke.anchor, cloudAnchorTtlDays);
            yield return hostPromise;

            if (hostPromise.State == PromiseState.Cancelled)
            {
                Debug.LogWarning("[ArtReality] Cloud anchor hosting cancelled.");
                continue;
            }

            var result = hostPromise.Result;
            if (result.CloudAnchorState == CloudAnchorState.Success)
            {
                stroke.cloudAnchorId = result.CloudAnchorId;
                Debug.Log($"[ArtReality] Cloud anchor hosted: {stroke.cloudAnchorId}");
                SaveDrawing();
            }
            else
            {
                Debug.LogWarning($"[ArtReality] Cloud anchor hosting failed: {result.CloudAnchorState}");
            }
        }

        isHosting = false;
    }

    private IEnumerator ResolveCloudAnchor(StrokeData data)
    {
        Debug.Log($"[ArtReality] Resolving cloud anchor: {data.cloudAnchorId}");

        var resolvePromise = arAnchorManager.ResolveCloudAnchorAsync(data.cloudAnchorId);
        yield return resolvePromise;

        if (resolvePromise.State == PromiseState.Cancelled)
        {
            Debug.LogWarning($"[ArtReality] Cloud anchor resolve cancelled: {data.cloudAnchorId}");
            pendingResolveData.Remove(data.cloudAnchorId);
            yield break;
        }

        var result = resolvePromise.Result;
        if (result.CloudAnchorState == CloudAnchorState.Success && result.Anchor != null)
        {
            Debug.Log($"[ArtReality] Cloud anchor resolved: {data.cloudAnchorId}");
            CreateStrokeFromCloudAnchor(data, result.Anchor);
        }
        else
        {
            Debug.LogWarning($"[ArtReality] Cloud anchor resolve failed ({result.CloudAnchorState}): {data.cloudAnchorId}");
            CreateStrokeFromLocalData(data);
        }

        pendingResolveData.Remove(data.cloudAnchorId);
    }

    private void CreateStrokeFromCloudAnchor(StrokeData data, ARCloudAnchor cloudAnchor)
    {
        // Create a local ARAnchor at the cloud anchor's resolved pose so the rest of
        // the pipeline (move mode, save, erase) works uniformly with ARAnchor.
        var pose = cloudAnchor.pose;
        var anchorGo = new GameObject("StrokeAnchor_cloud_" + data.cloudAnchorId);
        anchorGo.transform.SetPositionAndRotation(pose.position, pose.rotation);
        var anchor = anchorGo.AddComponent<ARAnchor>();

        var stroke = CreateStrokeGameObject(data, anchor);
        strokeInstances.Add(stroke);
    }

    private void CreateStrokeFromLocalData(StrokeData data)
    {
        Vector3 anchorPos = new Vector3(data.anchorPosX, data.anchorPosY, data.anchorPosZ);
        Quaternion anchorRot = new Quaternion(data.anchorRotX, data.anchorRotY, data.anchorRotZ, data.anchorRotW);

        var anchorGo = new GameObject("StrokeAnchor_fallback_" + (data.localId ?? ""));
        anchorGo.transform.SetPositionAndRotation(anchorPos, anchorRot);
        var anchor = anchorGo.AddComponent<ARAnchor>();

        var stroke = CreateStrokeGameObject(data, anchor);
        strokeInstances.Add(stroke);
    }

    private StrokeInstance CreateStrokeGameObject(StrokeData data, ARAnchor anchor)
    {
        Color color = new Color(data.colorR, data.colorG, data.colorB, data.colorA);

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

        var localPoints = new List<Vector3>(data.points.Count);
        lr.positionCount = data.points.Count;
        for (int j = 0; j < data.points.Count; j++)
        {
            var p = data.points[j];
            var v = new Vector3(p.x, p.y, p.z);
            localPoints.Add(v);
            lr.SetPosition(j, v);
        }

        return new StrokeInstance
        {
            anchor = anchor,
            strokeObject = strokeGo,
            lineRenderer = lr,
            color = color,
            size = data.size,
            localPoints = localPoints,
            latitude = data.latitude,
            longitude = data.longitude,
            cloudAnchorId = data.cloudAnchorId,
            localId = string.IsNullOrEmpty(data.localId) ? Guid.NewGuid().ToString("N") : data.localId
        };
    }

    // --- Materials ---

    private Material CreateLineMaterial()
    {
        if (lineMaterial != null) return new Material(lineMaterial);

        if (cachedLineShader == null)
            cachedLineShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");

        var mat = new Material(cachedLineShader);
        mat.SetFloat("_Surface", 0);
        mat.SetInt("_ZWrite", 1);
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
        var es = EventSystem.current;
        if (es == null) return false;
        if (pointerEventData == null) pointerEventData = new PointerEventData(es);
        pointerEventData.position = screenPosition;
        uiRaycastResults.Clear();
        es.RaycastAll(pointerEventData, uiRaycastResults);
        return uiRaycastResults.Count > 0;
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
                    if (!string.IsNullOrEmpty(stroke.cloudAnchorId))
                        pendingResolveData.Remove(stroke.cloudAnchorId);
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
    public float GetBrushSize() => brushSize;
    public Color GetBrushColor() => brushColor;

    public void SetMode(DrawingMode newMode) => mode = newMode;
    public DrawingMode GetMode() => mode;

    public void Undo()
    {
        if (strokeInstances.Count == 0) return;
        var last = strokeInstances[strokeInstances.Count - 1];
        strokeInstances.RemoveAt(strokeInstances.Count - 1);
        if (!string.IsNullOrEmpty(last.cloudAnchorId))
            pendingResolveData.Remove(last.cloudAnchorId);
        if (last.anchor != null) Destroy(last.anchor.gameObject);
        if (autoSave) SaveDrawing();
    }

    public void ClearAll()
    {
        foreach (var s in strokeInstances)
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        strokeInstances.Clear();
        pendingResolveData.Clear();
        if (autoSave) SaveDrawing();
    }

    // --- GPS Utility ---

    private static double GpsDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    // --- Save / Load ---

    public void SaveDrawing()
    {
        var saveData = new DrawingSaveData();
        var knownIds = new HashSet<string>();

        // 1) Serialize currently loaded strokes (with fresh transforms).
        foreach (var s in strokeInstances)
        {
            if (s.anchor == null || s.localPoints == null || s.localPoints.Count < 2) continue;

            var data = SerializeStroke(s);
            saveData.strokes.Add(data);

            string id = StableId(data);
            if (!string.IsNullOrEmpty(id)) knownIds.Add(id);
        }

        // 2) Preserve strokes still resolving from cloud.
        foreach (var kv in pendingResolveData)
        {
            if (!knownIds.Contains(kv.Key))
            {
                saveData.strokes.Add(kv.Value);
                knownIds.Add(kv.Key);
            }
        }

        // 3) Merge remaining strokes from the existing file (different GPS area, or
        //    strokes we haven't touched this session). This is what makes the save file
        //    monotonically grow while still letting us update in-place.
        if (File.Exists(SavePath))
        {
            try
            {
                var existingJson = File.ReadAllText(SavePath);
                var existing = JsonUtility.FromJson<DrawingSaveData>(existingJson);
                if (existing?.strokes != null)
                {
                    foreach (var s in existing.strokes)
                    {
                        string id = StableId(s);
                        if (string.IsNullOrEmpty(id) || knownIds.Contains(id)) continue;
                        saveData.strokes.Add(s);
                        knownIds.Add(id);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArtReality] Failed to read existing save for merge: {e.Message}");
            }
        }

        File.WriteAllText(SavePath, JsonUtility.ToJson(saveData, true));
        Debug.Log($"[ArtReality] Saved {saveData.strokes.Count} strokes -> {SavePath}");
    }

    private static string StableId(StrokeData data)
    {
        if (!string.IsNullOrEmpty(data.cloudAnchorId)) return "cloud:" + data.cloudAnchorId;
        if (!string.IsNullOrEmpty(data.localId)) return "local:" + data.localId;
        return null;
    }

    private StrokeData SerializeStroke(StrokeInstance s)
    {
        var pos = s.anchor.transform.position;
        var rot = s.anchor.transform.rotation;
        var data = new StrokeData
        {
            anchorPosX = pos.x, anchorPosY = pos.y, anchorPosZ = pos.z,
            anchorRotX = rot.x, anchorRotY = rot.y, anchorRotZ = rot.z, anchorRotW = rot.w,
            colorR = s.color.r, colorG = s.color.g, colorB = s.color.b, colorA = s.color.a,
            size = s.size,
            hasGps = gpsReady || s.latitude != 0 || s.longitude != 0,
            latitude = s.latitude,
            longitude = s.longitude,
            cloudAnchorId = s.cloudAnchorId ?? "",
            localId = s.localId ?? "",
            points = new List<SerializableVector3>(s.localPoints.Count)
        };
        foreach (var p in s.localPoints)
            data.points.Add(new SerializableVector3 { x = p.x, y = p.y, z = p.z });
        return data;
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

        // Clear current session.
        foreach (var s in strokeInstances)
            if (s.anchor != null) Destroy(s.anchor.gameObject);
        strokeInstances.Clear();
        pendingResolveData.Clear();

        int loadedCount = 0, cloudCount = 0, skippedCount = 0;

        foreach (var data in saveData.strokes)
        {
            if (data.points == null || data.points.Count < 2) continue;

            if (gpsReady && data.hasGps)
            {
                double dist = GpsDistance(currentLatitude, currentLongitude, data.latitude, data.longitude);
                if (dist > gpsLoadRadius)
                {
                    skippedCount++;
                    continue;
                }
            }

            if (useCloudAnchors && !string.IsNullOrEmpty(data.cloudAnchorId))
            {
                pendingResolveData[data.cloudAnchorId] = data;
                StartCoroutine(ResolveCloudAnchor(data));
                cloudCount++;
            }
            else
            {
                CreateStrokeFromLocalData(data);
                loadedCount++;
            }
        }

        Debug.Log($"[ArtReality] Load: {loadedCount} local, {cloudCount} cloud resolving, {skippedCount} skipped");
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
    public double latitude;
    public double longitude;
    public string cloudAnchorId;
    public string localId;
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
    public bool hasGps;
    public double latitude;
    public double longitude;
    public string cloudAnchorId;
    public string localId;
    public List<SerializableVector3> points;
}

[Serializable]
public class DrawingSaveData
{
    public List<StrokeData> strokes = new List<StrokeData>();
}
