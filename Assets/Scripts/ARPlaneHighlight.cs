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

    private MeshRenderer meshRenderer;
    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private ARPlane arPlane;

    private static readonly Color wallColor = new Color(0.2f, 0.6f, 1f, 0.15f);
    private static readonly Color highlightColor = new Color(0.2f, 1f, 0.4f, 0.35f);

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

    public static bool IsWall(ARPlane plane)
    {
        if (plane == null) return false;
        if (plane.alignment == PlaneAlignment.Vertical) return true;

        if (plane.alignment == PlaneAlignment.NotAxisAligned)
        {
            float verticalDot = Mathf.Abs(Vector3.Dot(plane.normal, Vector3.up));
            return verticalDot < 0.4f;
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

        if (isHighlighted)
        {
            meshRenderer.material.color = highlightColor;
            lineRenderer.startColor = Color.green;
            lineRenderer.endColor = Color.green;
        }
        else
        {
            meshRenderer.material.color = wallColor;
            lineRenderer.startColor = new Color(0.2f, 0.6f, 1f, 0.5f);
            lineRenderer.endColor = new Color(0.2f, 0.6f, 1f, 0.5f);
        }
    }

    private void SetupLineRenderer()
    {
        lineRenderer.startWidth = 0.005f;
        lineRenderer.endWidth = 0.005f;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
    }

    private void OffsetMeshAlongNormal()
    {
        if (meshFilter == null || meshFilter.mesh == null) return;

        var mesh = meshFilter.mesh;
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            // In ARPlane local space, Y is the normal direction
            vertices[i].y = planeOffset;
        }
        mesh.vertices = vertices;
    }

    private void UpdateBoundary()
    {
        if (arPlane == null || arPlane.boundary.Length < 3) return;

        var boundary = arPlane.boundary;
        lineRenderer.positionCount = boundary.Length;
        for (int i = 0; i < boundary.Length; i++)
        {
            // Offset boundary along Y (normal) to match mesh offset
            lineRenderer.SetPosition(i, new Vector3(boundary[i].x, planeOffset, boundary[i].y));
        }
    }
}
