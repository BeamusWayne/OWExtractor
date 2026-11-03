using System.Numerics;

namespace OWExtractor.Core;

public static class PreviewMeshIndexing
{
    // A transient interaction LOD. Shared spatial cell centers keep UV/normal seams together;
    // distinct UV and normal buckets keep their appearance separate. The full mesh is untouched.
    public static OfflineMesh InteractionLod(OfflineMesh mesh, CancellationToken token = default)
    {
        if (mesh.Indices.Length < 6000 || mesh.Positions.Length == 0) return mesh;
        // Skin meshes often contain disconnected head, hands and feet. Size each connected
        // component independently so a whole-body bounding box cannot collapse facial features.
        var parents = Enumerable.Range(0, mesh.Positions.Length).ToArray();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[b] = a; }
        Dictionary<Vector3, int> seams = [];
        for (var i = 0; i < parents.Length; i++) { if (seams.TryGetValue(mesh.Positions[i], out var other)) Join(i, other); else seams[mesh.Positions[i]] = i; }
        for (var i = 0; i < mesh.Indices.Length; i += 3) { Join(mesh.Indices[i], mesh.Indices[i + 1]); Join(mesh.Indices[i], mesh.Indices[i + 2]); }
        Dictionary<int, (Vector3 min, Vector3 max)> bounds = [];
        for (var i = 0; i < parents.Length; i++)
        {
            parents[i] = Root(i); var p = mesh.Positions[i];
            if (bounds.TryGetValue(parents[i], out var b)) bounds[parents[i]] = (Vector3.Min(b.min, p), Vector3.Max(b.max, p)); else bounds[parents[i]] = (p, p);
        }
        var cells = new (int, int, int, int)[parents.Length];
        Dictionary<(int, int, int, int), (Vector3 sum, int count)> centers = [];
        for (var i = 0; i < parents.Length; i++)
        {
            var b = bounds[parents[i]]; var extent = b.max - b.min;
            var cell = Math.Max(.00001f, Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) / 40); var p = mesh.Positions[i];
            var key = (parents[i], (int)MathF.Floor((p.X - b.min.X) / cell), (int)MathF.Floor((p.Y - b.min.Y) / cell), (int)MathF.Floor((p.Z - b.min.Z) / cell));
            cells[i] = key; var value = centers.GetValueOrDefault(key); centers[key] = (value.sum + p, value.count + 1);
        }
        Dictionary<((int, int, int, int), int, int, int, int, int), int> groups = [];
        List<Vector3> positions = [], normalSums = []; List<Vector2> uvSums = []; List<int> counts = [];
        var map = new int[mesh.Positions.Length];
        for (var i = 0; i < map.Length; i++)
        {
            if (i % 4096 == 0) token.ThrowIfCancellationRequested();
            var spatial = cells[i]; var n = mesh.Normals[i]; var uv = mesh.UV[i];
            var key = (spatial, (int)MathF.Round(n.X * 3), (int)MathF.Round(n.Y * 3), (int)MathF.Round(n.Z * 3), (int)MathF.Floor(uv.X * 32), (int)MathF.Floor(uv.Y * 32));
            if (!groups.TryGetValue(key, out var group))
            {
                group = positions.Count; groups[key] = group; var center = centers[spatial];
                positions.Add(center.sum / center.count); normalSums.Add(default); uvSums.Add(default); counts.Add(0);
            }
            map[i] = group; normalSums[group] += n; uvSums[group] += uv; counts[group]++;
        }
        for (var i = 0; i < positions.Count; i++) { normalSums[i] /= counts[i]; uvSums[i] /= counts[i]; }
        List<int> indices = []; HashSet<(int, int, int)> unique = [];
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = map[mesh.Indices[i]]; var b = map[mesh.Indices[i + 1]]; var c = map[mesh.Indices[i + 2]];
            if (a == b || b == c || c == a || Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).LengthSquared() < 1e-16f) continue;
            var key = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
            if (unique.Add(key)) { indices.Add(a); indices.Add(b); indices.Add(c); }
        }
        return Compact(new(mesh.Name, mesh.MaterialKey, positions.ToArray(), normalSums.ToArray(), uvSums.ToArray(), indices.ToArray()), token);
    }
    // Rendering only uses position, normal and color UV. Preserve seams while sharing identical
    // vertices produced by cutout tessellation; never weld by position alone.
    public static OfflineMesh Compact(OfflineMesh mesh, CancellationToken token = default)
    {
        Dictionary<(Vector3 position, Vector3 normal, Vector2 uv), int> lookup = [];
        List<Vector3> positions = [], normals = []; List<Vector2> uv = [];
        var indices = new int[mesh.Indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            if (i % 4096 == 0) token.ThrowIfCancellationRequested();
            var old = mesh.Indices[i]; var key = (mesh.Positions[old], mesh.Normals[old], mesh.UV[old]);
            if (!lookup.TryGetValue(key, out var index))
            { index = positions.Count; lookup[key] = index; positions.Add(key.Item1); normals.Add(key.Item2); uv.Add(key.Item3); }
            indices[i] = index;
        }
        return new(mesh.Name, mesh.MaterialKey, positions.ToArray(), normals.ToArray(), uv.ToArray(), indices);
    }
}
