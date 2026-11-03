using System.Numerics;

namespace OWExtractor.Core;

public sealed class PreviewOpacityMask
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] alpha;
    public PreviewOpacityMask(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height != pixels.Length) throw new ArgumentException("Invalid opacity mask.");
        Width = width; Height = height; alpha = pixels;
    }
    public float Sample(Vector2 uv)
    {
        var x = (uv.X - MathF.Floor(uv.X)) * Width - .5f;
        var y = (1 - uv.Y - MathF.Floor(1 - uv.Y)) * Height - .5f;
        var ix = (int)MathF.Floor(x); var iy = (int)MathF.Floor(y);
        var tx = x - ix; var ty = y - iy;
        float At(int px, int py) => alpha[((py % Height + Height) % Height) * Width + (px % Width + Width) % Width] / 255f;
        return float.Lerp(float.Lerp(At(ix, iy), At(ix + 1, iy), tx), float.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), tx), ty);
    }
}

// Convert masked surfaces into opaque geometry once, on load. WPF's transparent pixels otherwise
// write depth and erase hair layers behind them. No sorting or tessellation happens while orbiting.
public static class PreviewCutout
{
    private readonly record struct Vertex(Vector3 P, Vector3 N, Vector2 ColorUV, Vector2 MaskUV)
    {
        public static Vertex Mix(Vertex a, Vertex b, float t) => new(Vector3.Lerp(a.P, b.P, t), Vector3.Lerp(a.N, b.N, t), Vector2.Lerp(a.ColorUV, b.ColorUV, t), Vector2.Lerp(a.MaskUV, b.MaskUV, t));
    }
    public static OfflineMesh Clip(OfflineMesh mesh, PreviewOpacityMask mask, bool useSecondaryUV, CancellationToken token = default, int subdivisionDepth = 2)
    {
        if (useSecondaryUV && mesh.SecondaryUV is null) throw new InvalidDataException("头发透明遮罩需要第二套 UV。");
        var maskUV = useSecondaryUV ? mesh.SecondaryUV! : mesh.UV;
        List<Vector3> positions = [], normals = []; List<Vector2> uv = []; List<int> indices = [];
        const float threshold = .5f;
        var maxDepth = Math.Min(mesh.Indices.Length / 3 > 10000 ? 1 : 2, Math.Clamp(subdivisionDepth, 0, 2));
        Vertex Get(int i) => new(mesh.Positions[i], mesh.Normals[i], mesh.UV[i], maskUV[i]);
        void Emit(Vertex a, Vertex b, Vertex c)
        {
            if (indices.Count / 3 >= 200000) throw new InvalidDataException("透明边缘网格超过轻量预览上限。");
            foreach (var v in new[] { a, b, c }) { indices.Add(positions.Count); positions.Add(v.P); normals.Add(v.N); uv.Add(v.ColorUV); }
        }
        void Triangle(Vertex a, Vertex b, Vertex c, int depth)
        {
            var aa = mask.Sample(a.MaskUV); var ab = mask.Sample(b.MaskUV); var ac = mask.Sample(c.MaskUV);
            var mAB = Vertex.Mix(a, b, .5f); var mBC = Vertex.Mix(b, c, .5f); var mCA = Vertex.Mix(c, a, .5f);
            var center = (a.MaskUV + b.MaskUV + c.MaskUV) / 3;
            var samples = new[] { aa, ab, ac, mask.Sample(mAB.MaskUV), mask.Sample(mBC.MaskUV), mask.Sample(mCA.MaskUV), mask.Sample(center) };
            if (samples.All(x => x >= threshold)) { Emit(a, b, c); return; }
            if (samples.All(x => x < threshold)) return;
            if (depth < maxDepth)
            {
                Triangle(a, mAB, mCA, depth + 1); Triangle(mAB, b, mBC, depth + 1);
                Triangle(mCA, mBC, c, depth + 1); Triangle(mAB, mBC, mCA, depth + 1); return;
            }
            // Clip the final triangle against linearly interpolated opacity, retaining color UVs.
            List<Vertex> polygon = []; var corners = new[] { a, b, c }; var values = new[] { aa, ab, ac };
            for (var i = 0; i < 3; i++)
            {
                var j = (i + 1) % 3;
                if (values[i] >= threshold) polygon.Add(corners[i]);
                if ((values[i] >= threshold) != (values[j] >= threshold)) polygon.Add(Vertex.Mix(corners[i], corners[j], (threshold - values[i]) / (values[j] - values[i])));
            }
            for (var i = 1; i + 1 < polygon.Count; i++) Emit(polygon[0], polygon[i], polygon[i + 1]);
        }
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            token.ThrowIfCancellationRequested(); Triangle(Get(mesh.Indices[i]), Get(mesh.Indices[i + 1]), Get(mesh.Indices[i + 2]), 0);
        }
        return new(mesh.Name, mesh.MaterialKey, positions.ToArray(), normals.ToArray(), uv.ToArray(), indices.ToArray());
    }
}
