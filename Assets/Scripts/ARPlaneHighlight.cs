using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneMeshVisualizer))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(LineRenderer))]
public class ARPlaneHighlight : MonoBehaviour
{
    // Offset along plane normal so the visualization sits in front of the real wall
    private const float planeOffset = 0.03f;

    // Very permissive threshold for wall classification
    // 0.6 = ~53 degrees from vertical still counts as wall (catches slanted/rough/featureless walls)
    private const float wallVerticalDotThreshold = 0.6f;

    private MeshRenderer meshRenderer;
    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private ARPlane arPlane;

    private static readonly Color wallColor = new Color(0.2f, 0.6f, 1f, 0.15f);
    private static readonly Color highlightColor = new Color(0.2f, 1f, 0.4f, 0.35f);
    private static readonly Color lineIdleColor = new Color(0.2f, 0.6f, 1f, 0.5f);
    private static readonly List<Vector3> vertexBuffer = new List<Vector3>();
    private static Shader cachedLineShader;

    private Vector3[] boundaryBuffer;
    private bool isHighlighted;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        lineRenderer = GetComponent<LineRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        arPlane = GetComponent<ARPlane>();

        SetupLineRenderer();
    }

    private void OnEnable()
    {
        if (arPlane != null)
            arPlane.boundaryChanged += OnBoundaryChanged;

        UpdateVisibility();
    }

    private void OnDisable()
    {
        if (arPlane != null)
            arPlane.boundaryChanged -= OnBoundaryChanged;
    }

    private void LateUpdate()
    {
        if (!meshRenderer.enabled) return;

        OffsetMeshAlongNormal();
        UpdateBoundary();
    }

    private void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
    {
        UpdateVisibility();
    }

    /// <summary>
    /// Determines if an ARPlane is a wall. Uses multiple criteria:
    /// 1. PlaneAlignment.Vertical is always a wall
    /// 2. NotAxisAligned planes with mostly-horizontal normals are walls
    /// 3. Even HorizontalUp/Down planes can be walls if their actual normal is near-horizontal
    ///    (ARCore sometimes misclassifies featureless walls)
    /// </summary>
    public static bool IsWall(ARPlane plane)
    {
        if (plane == null) return false;

        // Explicitly vertical = wall
        if (plane.alignment == PlaneAlignment.Vertical) return true;

        // Check actual normal direction regardless of classification
        // This catches white/featureless walls that ARCore may misclassify
        float verticalDot = Mathf.Abs(Vector3.Dot(plane.normal, Vector3.up));

        if (plane.alignment == PlaneAlignment.NotAxisAligned)
        {
            return verticalDot < wallVerticalDotThreshold;
        }

        // Even for planes classified as horizontal, if the normal is actually
        // mostly horizontal, treat as wall (ARCore misclassification on featureless surfaces)
        // Threshold 0.4 = ~66 degrees from vertical, catches most misclassified white walls
        if (verticalDot < 0.4f)
        {
            return true;
        }

        return false;
    }

    private void UpdateVisibility()
    {
        bool isWall = IsWall(arPlane);
        meshRenderer.enabled = isWall;
        lineRenderer.enabled = isWall;

        if (isWall) UpdateColor();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (isHighlighted == highlighted) return;
        isHighlighted = highlighted;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (meshRenderer == null || meshRenderer.material == null) return;

        Color fillColor = isHighlighted ? highlightColor : wallColor;
        Color lineColor = isHighlighted ? Color.green : lineIdleColor;
        meshRenderer.material.color = fillColor;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
    }

    private void SetupLineRenderer()
    {
        lineRenderer.startWidth = 0.005f;
        lineRenderer.endWidth = 0.005f;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        if (cachedLineShader == null) cachedLineShader = Shader.Find("Sprites/Default");
        lineRenderer.material = new Material(cachedLineShader);
    }

    private void OffsetMeshAlongNormal()
    {
        if (meshFilter == null || meshFilter.mesh == null) return;

        var mesh = meshFilter.mesh;
        mesh.GetVertices(vertexBuffer);
        for (int i = 0; i < vertexBuffer.Count; i++)
        {
            var v = vertexBuffer[i];
            v.y = planeOffset;
            vertexBuffer[i] = v;
        }
        mesh.SetVertices(vertexBuffer);
    }

    private void UpdateBoundary()
    {
        if (arPlane == null) return;
        var boundary = arPlane.boundary;
        if (boundary.Length < 3) return;

        if (boundaryBuffer == null || boundaryBuffer.Length != boundary.Length)
            boundaryBuffer = new Vector3[boundary.Length];

        for (int i = 0; i < boundary.Length; i++)
            boundaryBuffer[i] = new Vector3(boundary[i].x, planeOffset, boundary[i].y);

        lineRenderer.positionCount = boundary.Length;
        lineRenderer.SetPositions(boundaryBuffer);
    }
}
