using OWExtractor.Core;

namespace OWExtractor.App;

public sealed partial class MainViewModel
{
    public int[] PreviewFrameRates { get; } = [30, 60];
    public string ActiveRenderMode { get; private set; } = "software";
    public bool SoftwareRendering => ActiveRenderMode == "software";
    public bool RenderModeForced { get; private set; }
    public bool SoftwarePreferred { get => PreviewRenderPolicy.Normalize(Settings.PreviewRenderingMode) == "software"; set { if (value) SetRenderPreference("software"); } }
    public bool HardwarePreferred { get => !SoftwarePreferred; set { if (value) SetRenderPreference("hardware"); } }
    public int PreviewTargetFps { get => PreviewRenderPolicy.FrameRate(Settings.PreviewTargetFps); set { Settings.PreviewTargetFps = PreviewRenderPolicy.FrameRate(value); Notify(); } }
    public bool ShowPreviewPerformance { get => Settings.ShowPreviewPerformance; set { Settings.ShowPreviewPerformance = value; Notify(); } }
    public string ActiveRenderLabel => SoftwareRendering ? "当前：软件渲染" : "当前：硬件优先 · 不支持时自动回退";
    public string RenderPreferenceHint => PreviewRenderPolicy.Normalize(Settings.PreviewRenderingMode) != ActiveRenderMode
        ? "渲染模式将在下次启动时生效；当前仍为" + (SoftwareRendering ? "软件渲染。" : "硬件优先。")
        : RenderModeForced ? "本次启动固定使用软件渲染。渲染模式设置不会改变离线状态。" : "更换渲染模式需重新启动。硬件预览与游戏共享显卡资源，离线状态不受影响。";
    public void ConfigurePreviewRendering(string activeMode, bool forced)
    {
        ActiveRenderMode = activeMode; RenderModeForced = forced;
        foreach (var name in new[] { nameof(ActiveRenderMode), nameof(SoftwareRendering), nameof(ActiveRenderLabel), nameof(RenderPreferenceHint), nameof(SoftwarePreferred), nameof(HardwarePreferred), nameof(PreviewTargetFps), nameof(ShowPreviewPerformance) }) Notify(name);
    }
    private void SetRenderPreference(string mode)
    {
        Settings.PreviewRenderingMode = mode;
        foreach (var name in new[] { nameof(SoftwarePreferred), nameof(HardwarePreferred), nameof(RenderPreferenceHint) }) Notify(name);
    }
    public async Task SavePreviewSettingsAsync() { await SaveAsync(); Status = "设置已保存。" + RenderPreferenceHint; }
}
