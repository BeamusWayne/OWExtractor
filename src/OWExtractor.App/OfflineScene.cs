using System.IO;
using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using OWExtractor.Core;

namespace OWExtractor.App;

public sealed record OfflineScene(Model3DGroup Model, Rect3D Bounds, int Vertices, int Triangles, int Textures, int MissingMaterials, int CutoutMeshes = 0, int RenderTriangles = 0, Model3DGroup? InteractionModel = null, int InteractionTriangles = 0);

internal static class OfflineSceneLoader
{
    private sealed record PreviewTexture(ImageSource Color, PreviewOpacityMask Alpha);
    private sealed record PreviewMaterial(Material Surface, PreviewOpacityMask? Mask = null, bool SecondaryUV = false, bool Translucent = false);
    public static OfflineScene Load(string file, string root, string gamePath, CancellationToken token)
    {
        file = ExportFileAccess.Validate(file, root, gamePath, ".owmdl", 64 * 1024 * 1024);
        using var stream = File.OpenRead(file);
        var data = OfflineModelReader.Read(stream, token);
        var materials = new Dictionary<ulong, PreviewMaterial>();
        var textureCache = new Dictionary<string, PreviewTexture>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(data.MaterialLook))
        {
            try
            {
                var look = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, data.MaterialLook));
                using var reader = OpenMaterial(look, 1);
                var count = reader.ReadUInt64();
                if (count > 128) throw new InvalidDataException("材质数量过多。");
                for (ulong i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var key = reader.ReadUInt64(); var relative = OfflineModelReader.ReadString(reader);
                    // OWLib ModelLook entries are relative to the look FILE, not its parent directory.
                    try { materials[key] = ReadMaterial(Path.GetFullPath(Path.Combine(look, relative))); }
                    catch (Exception ex) when (IsAssetError(ex)) { /* Keep geometry usable when a material is absent. */ }
                }
            }
            catch (Exception ex) when (IsAssetError(ex)) { /* The model remains usable with neutral materials. */ }
        }
        var neutral = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(185, 194, 191))); neutral.Freeze();
        var group = new Model3DGroup(); var interactionGroup = new Model3DGroup(); var bounds = Rect3D.Empty; var missing = 0; var cutouts = 0; var renderTriangles = 0; var interactionTriangles = 0;
        List<Model3D> translucent = [], interactionTranslucent = [];
        foreach (var sourceMesh in data.Meshes)
        {
            token.ThrowIfCancellationRequested();
            if (!materials.TryGetValue(sourceMesh.MaterialKey, out var material)) { material = new(neutral); missing++; }
            var mesh = sourceMesh;
            if (material.Mask is { } mask) { mesh = PreviewCutout.Clip(mesh, mask, material.SecondaryUV, token); cutouts++; }
            mesh = PreviewMeshIndexing.Compact(mesh, token);
            renderTriangles += mesh.Indices.Length / 3;
            if (renderTriangles > 300000) throw new InvalidDataException("透明边缘处理后的模型超过轻量预览上限。");
            var geometry = BuildGeometry(mesh);
            bounds.Union(geometry.Bounds);
            var model = new GeometryModel3D(geometry, material.Surface) { BackMaterial = material.Surface };
            if (material.Translucent) translucent.Add(model); else group.Children.Add(model);
            var interactionSource = material.Mask is { } interactionMask
                ? PreviewMeshIndexing.Compact(PreviewCutout.Clip(sourceMesh, interactionMask, material.SecondaryUV, token, subdivisionDepth: 0), token) : mesh;
            var lod = PreviewMeshIndexing.InteractionLod(interactionSource, token); interactionTriangles += lod.Indices.Length / 3;
            var interactionModel = new GeometryModel3D(BuildGeometry(lod), material.Surface) { BackMaterial = material.Surface };
            if (material.Translucent) interactionTranslucent.Add(interactionModel); else interactionGroup.Children.Add(interactionModel);
        }
        foreach (var model in translucent) group.Children.Add(model);
        foreach (var model in interactionTranslucent) interactionGroup.Children.Add(model);
        foreach (var scene in new[] { group, interactionGroup })
        {
            scene.Children.Add(new AmbientLight(Color.FromRgb(145, 145, 145)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(155, 150, 142), new(-.4, -.7, -1)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(65, 72, 82), new(.6, -.2, 1)));
            scene.Freeze();
        }
        return new(group, bounds, data.VertexCount, data.TriangleCount, textureCache.Count, missing, cutouts, renderTriangles, interactionGroup, interactionTriangles);

        MeshGeometry3D BuildGeometry(OfflineMesh mesh)
        {
            var geometry = new MeshGeometry3D();
            foreach (var p in mesh.Positions) geometry.Positions.Add(new(p.X, p.Z, -p.Y));
            foreach (var n in mesh.Normals)
            {
                var normal = new Vector3D(n.X, n.Z, -n.Y); if (normal.LengthSquared > .000001) normal.Normalize();
                geometry.Normals.Add(normal);
            }
            foreach (var uv in mesh.UV) geometry.TextureCoordinates.Add(new(uv.X, 1 - uv.Y));
            geometry.TriangleIndices = new Int32Collection(mesh.Indices); geometry.Freeze();
            return geometry;
        }

        BinaryReader OpenMaterial(string path, uint expectedType)
        {
            path = ExportFileAccess.Validate(path, root, gamePath, ".owmat", 2 * 1024 * 1024);
            var reader = new BinaryReader(File.OpenRead(path));
            try
            {
                if (reader.ReadUInt16() != 3 || reader.ReadUInt16() != 0 || reader.ReadUInt32() != expectedType)
                    throw new InvalidDataException("不支持的材质格式。");
                return reader;
            }
            catch { reader.Dispose(); throw; }
        }
        PreviewMaterial ReadMaterial(string materialFile)
        {
            using var reader = OpenMaterial(materialFile, 0);
            var count = reader.ReadUInt64(); reader.ReadUInt64(); var shader = reader.ReadUInt32();
            if (count > 64) throw new InvalidDataException("贴图数量过多。");
            string? albedo = null; uint albedoKey = 0; var keys = new HashSet<uint>();
            for (ulong i = 0; i < count; i++)
            {
                var relative = OfflineModelReader.ReadString(reader); var key = reader.ReadUInt32();
                keys.Add(key);
                if (albedo is null && key is 2903569922 or 1239794147 or 3411049489 or 67369114 or 3989656707 or 1716930793 or 996643046)
                { albedo = relative; albedoKey = key; }
            }
            if (shader is 38 or 54)
            {
                // Corneal/glass shells must never become opaque white or fallback-gray surfaces.
                // Software preview approximates refraction with a faint specular highlight.
                var glass = new SpecularMaterial(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), 64); glass.Freeze();
                return new(glass, Translucent: true);
            }
            if (albedo is null) throw new InvalidDataException("没有基础颜色贴图。");
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(materialFile)!, albedo));
            var texture = ReadTexture(path);
            var brush = new ImageBrush(texture.Color) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new(0, 0, 1, 1), TileMode = TileMode.Tile };
            var surface = new DiffuseMaterial(brush); surface.Freeze();
            // Packed albedo/eye alpha is data, not opacity. OW2 hair uses UVMap2 (the SECOND UV set)
            // for alpha, while its RGB remains on UVMap1; confirmed in the cached io_scene_owm importer.
            var hair = albedoKey == 1239794147;
            return new(surface, hair ? texture.Alpha : null, hair && shader == 51 && keys.Contains(4101268840) && keys.Contains(758934576));
        }
        PreviewTexture ReadTexture(string path)
        {
            path = ExportFileAccess.Validate(path, root, gamePath, ".png", 32 * 1024 * 1024);
            if (textureCache.TryGetValue(path, out var cached)) return cached;
            if (textureCache.Count >= 32) throw new InvalidDataException("已达到轻量预览的贴图数量上限。");
            token.ThrowIfCancellationRequested();
            using var png = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24]; png.ReadExactly(header);
            if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidDataException("无效的 PNG 贴图。");
            var width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
            if (width is 0 or > 16384 || height is 0 or > 16384 || (long)width * height > 16777216) throw new InvalidDataException("贴图尺寸超出预览上限。");
            png.Position = 0;
            var decoded = PreviewTextureDecoder.Decode(png, token);
            var texture = new PreviewTexture(decoded.Color, new(decoded.Width, decoded.Height, decoded.Alpha));
            textureCache.Add(path, texture); return texture;
        }
    }
    private static bool IsAssetError(Exception ex) => ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or System.FormatException;
}
