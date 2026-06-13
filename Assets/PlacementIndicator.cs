using UnityEngine;

/// <summary>
/// Creates a flat circle mesh at runtime used as placement/selection indicator.
/// No external assets needed.
/// Attach to an empty GameObject and assign it to FurnitureInteraction.
/// </summary>
public class PlacementIndicator : MonoBehaviour
{
    [Range(8, 64)]
    public int   segments   = 32;
    public float radius     = 0.3f;
    public Color color      = new Color(0f, 1f, 0.5f, 0.6f);
    public bool  isRing     = true;   // true = ring outline, false = filled circle
    public float ringWidth  = 0.04f;  // only used when isRing = true

    private MeshRenderer meshRenderer;

    private void Awake()
    {
        BuildMesh();
    }

    private void BuildMesh()
    {
        MeshFilter   mf = gameObject.AddComponent<MeshFilter>();
        meshRenderer    = gameObject.AddComponent<MeshRenderer>();

        meshRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            color = color
        };
        meshRenderer.material.SetFloat("_Surface", 1); // transparent
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows    = false;

        mf.mesh = isRing ? BuildRingMesh() : BuildCircleMesh();
    }

    private Mesh BuildCircleMesh()
    {
        Mesh mesh     = new Mesh();
        Vector3[] verts = new Vector3[segments + 1];
        int[]     tris  = new int[segments * 3];

        verts[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            verts[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < segments; i++)
        {
            tris[i * 3]     = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 2 > segments) ? 1 : i + 2;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private Mesh BuildRingMesh()
    {
        Mesh mesh      = new Mesh();
        int   count    = segments;
        float inner    = radius - ringWidth;
        float outer    = radius;

        Vector3[] verts = new Vector3[count * 2];
        int[]     tris  = new int[count * 6];

        for (int i = 0; i < count; i++)
        {
            float angle = (float)i / count * Mathf.PI * 2f;
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);

            verts[i]         = new Vector3(cos * inner, 0, sin * inner);
            verts[i + count] = new Vector3(cos * outer, 0, sin * outer);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int ti   = i * 6;

            tris[ti]     = i;
            tris[ti + 1] = i + count;
            tris[ti + 2] = next + count;

            tris[ti + 3] = i;
            tris[ti + 4] = next + count;
            tris[ti + 5] = next;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    public void SetColor(Color c)
    {
        color = c;
        if (meshRenderer != null)
            meshRenderer.material.color = c;
    }
}
