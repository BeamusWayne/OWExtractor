using System.Numerics;
using System.Text;

namespace OWExtractor.Core;

public sealed record OfflineMesh(string Name, ulong MaterialKey, Vector3[] Positions, Vector3[] Normals, Vector2[] UV, int[] Indices)
{
    public Vector2[]? SecondaryUV { get; init; }
}
public sealed record OfflineModel(string Name, string MaterialLook, IReadOnlyList<OfflineMesh> Meshes)
{
    public int TriangleCount => Meshes.Sum(m => m.Indices.Length / 3);
    public int VertexCount => Meshes.Sum(m => m.Positions.Length);
}

// OWLib's exported OWMDL 2.0 mesh data. No CASC, game client, skeleton evaluation or engine dependency.
public static class OfflineModelReader
{
    public static string ReadString(BinaryReader reader)
    {
        var length = reader.Read7BitEncodedInt();
        if (length is < 0 or > 4096) throw new InvalidDataException("模型中的文本长度无效。");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(bytes);
    }
    private static void Skip(BinaryReader reader, long count)
    {
        if (count < 0 || reader.BaseStream.Length - reader.BaseStream.Position < count) throw new EndOfStreamException("模型文件不完整。");
        reader.BaseStream.Seek(count, SeekOrigin.Current);
    }
    private static float Number(BinaryReader reader)
    {
        var value = reader.ReadSingle();
        if (!float.IsFinite(value) || Math.Abs(value) > 100000) throw new InvalidDataException("模型包含无效坐标。");
        return value;
    }
    public static OfflineModel Read(Stream stream, CancellationToken token = default)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt16() != 2 || reader.ReadUInt16() != 0) throw new InvalidDataException("目前支持 OWMDL 2.0 导出模型。");
        var look = ReadString(reader); var name = ReadString(reader);
        reader.ReadUInt32(); var bones = reader.ReadUInt16(); var meshes = reader.ReadUInt32(); reader.ReadUInt32();
        if (bones > 4096 || meshes is 0 or > 128) throw new InvalidDataException("模型规模超过轻量预览上限。");
        for (var i = 0; i < bones; i++) { token.ThrowIfCancellationRequested(); ReadString(reader); Skip(reader, 38); }
        List<OfflineMesh> result = []; long totalVertices = 0, totalTriangles = 0;
        for (var mesh = 0; mesh < meshes; mesh++)
        {
            token.ThrowIfCancellationRequested();
            var meshName = ReadString(reader); var key = reader.ReadUInt64(); var channels = reader.ReadByte();
            var vertices = reader.ReadUInt32(); var triangles = reader.ReadUInt32(); var influences = reader.ReadSByte();
            totalVertices += vertices; totalTriangles += triangles;
            if (channels > 8 || influences is < 0 or > 16 || totalVertices > 200000 || totalTriangles > 300000)
                throw new InvalidDataException("模型规模超过轻量预览上限（20 万顶点 / 30 万三角面）。");
            // Check the complete payload before allocating any mesh arrays.
            var bytes = (long)vertices * (72 + channels * 8 + influences * 6) + (long)triangles * 12;
            if (stream.Length - stream.Position < bytes) throw new EndOfStreamException("模型文件不完整。");
            var positions = new Vector3[vertices]; var normals = new Vector3[vertices]; var uv = new Vector2[vertices];
            var secondaryUV = channels > 1 ? new Vector2[vertices] : null;
            for (var i = 0; i < vertices; i++) positions[i] = new(Number(reader), Number(reader), Number(reader));
            for (var i = 0; i < vertices; i++) normals[i] = new(Number(reader), Number(reader), Number(reader));
            Skip(reader, vertices * 16L);
            for (var c = 0; c < channels; c++)
            for (var i = 0; i < vertices; i++) { var pair = new Vector2(Number(reader), Number(reader)); if (c == 0) uv[i] = pair; if (c == 1) secondaryUV![i] = pair; }
            Skip(reader, vertices * (influences * 6L + 32));
            var indices = new int[triangles * 3];
            for (var i = 0; i < indices.Length; i++)
            {
                var index = reader.ReadUInt32();
                if (index >= vertices) throw new InvalidDataException("模型面索引超出顶点范围。");
                indices[i] = (int)index;
            }
            result.Add(new(meshName, key, positions, normals, uv, indices) { SecondaryUV = secondaryUV });
        }
        if (totalVertices == 0 || totalTriangles == 0) throw new InvalidDataException("这个文件没有可预览的三角网格。");
        return new(name, look, result);
    }
}

public static class ExportFileAccess
{
    public static bool IsWithin(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string Validate(string file, string root, string gamePath, string extension, long maximumBytes)
    {
        // All lexical checks precede filesystem access, including metadata and existence checks.
        if (!Path.IsPathFullyQualified(file) || file.StartsWith(@"\\") || file.StartsWith("//")) throw new InvalidDataException("只接受本机已有导出文件。");
        var full = Path.GetFullPath(file);
        if (full[2..].Contains(':') || !IsWithin(full, root)) throw new InvalidDataException("模型引用超出所选导出目录。");
        if (!string.IsNullOrWhiteSpace(gamePath) && (IsWithin(full, gamePath) || full.Equals(Path.GetFullPath(gamePath), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("离线预览不读取游戏目录。");
        if (!Path.GetExtension(full).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不支持这个预览文件格式。");
        CatalogService.EnsureExportPath(Path.GetDirectoryName(full)!);
        var info = new FileInfo(full);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > maximumBytes) throw new InvalidDataException("预览文件不能是链接或超过大小上限。");
        return full;
    }
}
