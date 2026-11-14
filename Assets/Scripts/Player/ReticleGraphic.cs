using UnityEngine;

/// <summary>Resolution-independent white disc/ring with feathered mesh edges; no texture assets.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ReticleGraphic : UnityEngine.UI.MaskableGraphic
{
    float radius = 2f, thickness = 4f;

    public void SetShape(float newRadius, float newThickness, float opacity)
    {
        if (Mathf.Abs(radius - newRadius) < .001f && Mathf.Abs(thickness - newThickness) < .001f
            && Mathf.Abs(color.a - opacity) < .001f) return;
        radius = newRadius;
        thickness = newThickness;
        color = new Color(1, 1, 1, opacity);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if (radius <= 0 || color.a <= 0) return;
        const int segments = 64;
        float feather = .65f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1);
        float inner = Mathf.Max(0, radius - thickness);
        // Two solid radial bands and two transparent edges also work for a filled centre disc.
        for (int band = 0; band < 4; band++)
        {
            float r = band == 0 ? Mathf.Max(0, inner - feather) : band == 1 ? inner + feather
                : band == 2 ? Mathf.Max(inner + feather, radius - feather) : radius + feather;
            Color tint = color;
            if (band == 3 || (band == 0 && inner > 0)) tint.a = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * (Mathf.PI * 2 / segments);
                mesh.AddVert(new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0), tint, Vector2.zero);
                if (band == 0 || i == 0) continue;
                int b = band * (segments + 1) + i, a = b - (segments + 1);
                mesh.AddTriangle(a - 1, a, b);
                mesh.AddTriangle(a - 1, b, b - 1);
            }
        }
    }
}
