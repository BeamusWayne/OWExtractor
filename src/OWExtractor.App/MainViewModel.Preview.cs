using System.IO;
using OWExtractor.Core;

namespace OWExtractor.App;

public sealed partial class MainViewModel
{
    public PreviewCamera Camera { get; } = new();
    public OfflineScene? Scene { get; private set; }
    public bool HasModel => Scene is not null;
    public bool ShowMainModel => HasModel && !PreviewExpanded;
    public bool ShowCachedPreview => !HasModel && PreviewImage is not null;
    public bool ShowPreviewWarning => ShowCachedPreview && !ModelLoading && ModelMessage.StartsWith("模型未能载入", StringComparison.Ordinal);
    public bool ModelLoading { get; private set; }
    public string ModelMessage { get; private set; } = "关联之前导出的 .owmdl 文件，即可旋转和缩放。";
    public string PreviewKind => HasModel ? "3D · 本地模型" : ModelLoading ? "正在读取导出模型…" : "缓存图像";
    public string PreviewHint => HasModel ? "拖动旋转 · 滚轮缩放 · 右键平移" : "静态缓存预览";
    public string ModelDetails => Scene is { } s ? $"{s.Triangles:N0} 三角面 · {s.Textures} 张颜色贴图" + (s.MissingMaterials > 0 ? $" · {s.MissingMaterials} 个网格使用默认材质" : "") : ModelMessage;
    public string ModelFileName => Path.GetFileName(SelectedSkin?.PreviewModelPath) ?? "尚未关联模型";
    internal Task SceneLoadTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? sceneCancellation;
    private string? loadedModel;

    private void RefreshModel()
    {
        var path = SelectedSkin?.PreviewModelPath;
        var root = SelectedSkin?.ExportDirectory;
        var identity = path + "|" + root + "|" + Settings.GamePath;
        if (loadedModel == identity) return;
        loadedModel = identity;
        sceneCancellation?.Cancel(); sceneCancellation?.Dispose(); sceneCancellation = new();
        var token = sceneCancellation.Token;
        Scene = null; Camera.Reset(); ModelLoading = !string.IsNullOrWhiteSpace(path);
        ModelMessage = ModelLoading ? "正在载入本地网格与贴图…" : "关联之前导出的 .owmdl 文件，即可旋转和缩放。";
        NotifyModel();
        SceneLoadTask = string.IsNullOrWhiteSpace(path) ? Task.CompletedTask : LoadModelAsync(path, root, Settings.GamePath, token);
    }
    private async Task LoadModelAsync(string path, string? root, string gamePath, CancellationToken token)
    {
        try
        {
            // An explicitly selected standalone model may only resolve assets below its own folder.
            var allowedRoot = !string.IsNullOrWhiteSpace(root) && ExportFileAccess.IsWithin(path, root) ? root : Path.GetDirectoryName(Path.GetFullPath(path))!;
            var scene = await Task.Run(() => OfflineSceneLoader.Load(path, allowedRoot, gamePath, token), token);
            if (token.IsCancellationRequested) return;
            Scene = scene; ModelMessage = "已载入本地模型。";
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { if (token.IsCancellationRequested) return; ModelMessage = "模型未能载入：" + ex.Message; }
        if (token.IsCancellationRequested) return;
        ModelLoading = false; NotifyModel();
    }
    private void NotifyModel()
    {
        Notify(nameof(FrontView)); Notify(nameof(RearView));
        foreach (var property in new[] { nameof(Scene), nameof(HasModel), nameof(ShowMainModel), nameof(ShowCachedPreview), nameof(ShowPreviewWarning), nameof(ModelLoading), nameof(ModelMessage), nameof(ModelDetails), nameof(ModelFileName), nameof(PreviewKind), nameof(PreviewHint), nameof(HasPreview), nameof(HasFrontPreview), nameof(HasBackPreview) }) Notify(property);
    }
    public async Task ImportModelAsync(string path)
    {
        if (SelectedSkin is not { } skin) return;
        var root = skin.ExportDirectory;
        var allowedRoot = !string.IsNullOrWhiteSpace(root) && ExportFileAccess.IsWithin(path, root) ? root : Path.GetDirectoryName(Path.GetFullPath(path))!;
        ExportFileAccess.Validate(path, allowedRoot, Settings.GamePath, ".owmdl", 64 * 1024 * 1024);
        skin.PreviewModelPath = Path.GetFullPath(path); loadedModel = null; RefreshModel();
        await SceneLoadTask; await SaveAsync();
        Status = HasModel ? "已关联本地 3D 模型，可拖动旋转或滚轮缩放。" : ModelMessage;
    }
    public void ResetPreview() { Camera.Reset(); rearView = false; Notify(nameof(FrontView)); Notify(nameof(RearView)); Notify(nameof(PreviewImage)); }
}
