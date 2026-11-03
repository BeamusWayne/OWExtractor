using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OWExtractor.Core;
using OWExtractor.DataTool;

namespace OWExtractor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private bool savedForClose;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); vm = viewModel; DataContext = vm;
        Title = "OWExtractor · 素材库 · v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        Closing += OnClosing;
        SizeChanged += (_, e) => vm.CompactLayout = e.NewSize.Height < 840 || e.NewSize.Width < 1200;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (savedForClose) return;
        if (vm.Busy) { e.Cancel = true; vm.Cancel(); vm.AddLog("正在取消当前操作；停止后可以关闭窗口。"); return; }
        e.Cancel = true;
        try { await vm.SaveAsync(); savedForClose = true; Close(); }
        catch (Exception ex) { vm.AddLog("保存失败：" + ex.Message); MessageBox.Show(this, "保存设置失败：" + ex.Message, "OWExtractor"); }
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { vm.AddLog(ex.Message); MessageBox.Show(this, ex.Message, "OWExtractor", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private string? PickJson() { var dialog = new OpenFileDialog { Filter = "JSON 文件|*.json", CheckFileExists = true }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    private string? PickFolder(string title) { var dialog = new OpenFolderDialog { Title = title }; return dialog.ShowDialog(this) == true ? dialog.FolderName : null; }
    private async void ImportHeroes(object sender, RoutedEventArgs e) { if (PickJson() is { } file) await Run(() => vm.ImportHeroesAsync(file, false)); }
    private async void ImportLocalized(object sender, RoutedEventArgs e) { if (PickJson() is { } file) await Run(() => vm.ImportHeroesAsync(file, true)); }
    private async void ImportUnlocks(object sender, RoutedEventArgs e) { if (PickJson() is { } file) await Run(() => vm.ImportUnlocksAsync(file)); }
    private async void ImportAudioSummary(object sender, RoutedEventArgs e) { if (PickJson() is { } file) await Run(() => vm.ImportAudioSummaryAsync(file)); }
    private async void ImportExports(object sender, RoutedEventArgs e) { if (vm.Busy || vm.PreviewExpanded) return; if (PickFolder("选择已有 DataTool 导出目录，不选择游戏安装目录") is { } root) await Run(() => vm.ImportExportsAsync(root)); }
    private async Task PickPreview(bool rear)
    {
        if (vm.SelectedSkin is null) return;
        var dialog = new OpenFileDialog { Title = "关联之前生成的" + (rear ? "背面" : "正面") + "缓存图", Filter = "缓存预览图片|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await Run(() => vm.ImportPreviewAsync(dialog.FileName, rear));
    }
    private async void ImportFrontPreview(object sender, RoutedEventArgs e) => await PickPreview(false);
    private async void ImportBackPreview(object sender, RoutedEventArgs e) => await PickPreview(true);
    private async void ImportModel(object sender, RoutedEventArgs e)
    {
        if (vm.SelectedSkin is null) return;
        var dialog = new OpenFileDialog { Title = "选择之前导出的模型文件", Filter = "已导出的 OWMDL 模型|*.owmdl", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await Run(() => vm.ImportModelAsync(dialog.FileName));
    }
    private void FrontModel(object sender, RoutedEventArgs e) => vm.FrontView = true;
    private void RearModel(object sender, RoutedEventArgs e) => vm.RearView = true;
    private void ZoomInModel(object sender, RoutedEventArgs e) => vm.Camera.Scale(1.15);
    private void ZoomOutModel(object sender, RoutedEventArgs e) => vm.Camera.Scale(1 / 1.15);
    private void ResetModel(object sender, RoutedEventArgs e) => vm.ResetPreview();
    private void FitModel(object sender, RoutedEventArgs e) => vm.Camera.Fit();
    private void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { vm.ApplyLibrarySearch(); e.Handled = true; } }
    private void SubmitSearch(object sender, RoutedEventArgs e) => vm.ApplyLibrarySearch();
    private void ClearLibrarySearch(object sender, RoutedEventArgs e) => vm.ClearLibrarySearch();
    private void DismissQueueNotice(object sender, RoutedEventArgs e) => vm.QueueNotice = false;
    private void ShowModelDetails(object sender, RoutedEventArgs e) { vm.DetailsSelected = true; ModelInfoExpander.IsExpanded = true; ModelInfoExpander.BringIntoView(); }
    private async void ChooseEngine(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "DataTool|DataTool.exe" }; if (dialog.ShowDialog(this) == true) { vm.Settings.EnginePath = dialog.FileName; await ReloadBindings(); } }
    private async void ChooseGame(object sender, RoutedEventArgs e) { if (vm.CanToggleLive && PickFolder("选择游戏安装目录；此操作只记录路径") is { } path) { vm.Settings.GamePath = path; await ReloadBindings(); } }
    private async void ChooseOutput(object sender, RoutedEventArgs e) { if (PickFolder("选择导出目录") is { } path) { vm.Settings.OutputPath = path; await ReloadBindings(); } }
    private async Task ReloadBindings() { DataContext = null; DataContext = vm; await Run(vm.SaveAsync); }
    private async void SaveSettingsAndReturn(object sender, RoutedEventArgs e) => await Run(async () => { await vm.SaveAsync(); vm.ActivatePage("models"); });
    private async void SavePreviewSettings(object sender, RoutedEventArgs e) => await Run(vm.SavePreviewSettingsAsync);
    private async void RefreshLive(object sender, RoutedEventArgs e) => await Run(vm.RefreshLiveAsync);
    private void FindSkin(object sender, ExecutedRoutedEventArgs e) { if (!vm.PreviewExpanded) { SkinSearchBox.Focus(); SkinSearchBox.SelectAll(); } e.Handled = true; }
    private void Navigate(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string page }) vm.ActivatePage(page); }
    private void OpenSkinCard(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: SkinCard card }) vm.SelectCard(card); }
    private void ChangeAudioLanguage(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string language }) vm.AudioLanguage = language; }
    private void ResetContent(object sender, RoutedEventArgs e) => vm.ResetContent();
    private void ExpandPreview(object sender, RoutedEventArgs e) { vm.PreviewExpanded = true; if (IsVisible) ClosePreviewButton.Focus(); }
    private void ClosePreview(object sender, RoutedEventArgs e) { vm.PreviewExpanded = false; if (IsVisible) ExpandPreviewButton.Focus(); }
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && vm.PreviewExpanded) { ClosePreview(sender, e); e.Handled = true; } }
    private void ClearSelection(object sender, RoutedEventArgs e) => SkinGrid.UnselectAll();
    private void SkinSelectionChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is MainViewModel model) model.SelectedCount = SkinGrid.SelectedItems.Count; }
    private void OpenImportMenu(object sender, RoutedEventArgs e) { if (sender is Button { ContextMenu: { } menu } button) { menu.DataContext = vm; menu.PlacementTarget = button; menu.IsOpen = true; } }
    private void SelectAll(object sender, RoutedEventArgs e) => SkinGrid.SelectAll();
    private async void AddJobs(object sender, RoutedEventArgs e) => await Run(() => vm.AddSelectedJobsAsync(SkinGrid.SelectedItems.Cast<SkinAsset>().ToList()));
    private async void RunJobs(object sender, RoutedEventArgs e) => await Run(vm.RunJobsAsync);
    private void CancelJobs(object sender, RoutedEventArgs e) => vm.Cancel();
    private async void RetryJob(object sender, RoutedEventArgs e) => await Run(() => vm.RetryAsync(JobGrid.SelectedItem as ExportJob));
    private async void RemoveJob(object sender, RoutedEventArgs e) => await Run(() => vm.RemoveAsync(JobGrid.SelectedItem as ExportJob));
    private void OpenExport(object sender, RoutedEventArgs e)
    {
        if (vm.SelectedSkin?.ExportDirectory is not { } path) return;
        try { CatalogService.EnsureExportPath(path); if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(path) }, UseShellExecute = false }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { vm.AddLog(ex.Message); }
    }

    internal async Task VerifyOfflineViewAsync()
    {
        VerifyPackedTextureChannels();
        if (vm.Page != "models" || vm.LiveEnabled) throw new InvalidOperationException("应默认进入离线模型素材库。");
        if (Icon is null) throw new InvalidOperationException("应用图标未载入。");
        foreach (var (file, game) in new[] { (@"D:\Overwatch\Data\blocked.png", ""), (@"D:\synthetic-game-location\blocked.png", @"D:\synthetic-game-location") })
        {
            try { LibraryPreview.EnsureAllowed(file, game); throw new InvalidOperationException("预览错误地允许了游戏路径。"); }
            catch (InvalidDataException) { }
        }
        if (vm.OfflineLocked) { vm.LiveEnabled = true; if (vm.LiveEnabled || vm.CanToggleLive) throw new InvalidOperationException("离线启动锁定失效。"); }
        SkinGrid.UnselectAll();
        if (vm.SelectedCount != 0 || vm.CanAdd || AddQueueButton.IsEnabled) throw new InvalidOperationException("无选择时不应允许加入队列。");
        SkinGrid.SelectAll();
        if (vm.SelectedCount != vm.VisibleSkins.Count || !vm.CanAdd || !AddQueueButton.IsEnabled) throw new InvalidOperationException("卡片选择没有更新操作状态。");
        vm.LibrarySearch = "__OWExtractor_no_match__";
        if (vm.Page != "models") throw new InvalidOperationException("输入搜索时不应跳页。");
        vm.ApplyLibrarySearch();
        if (!vm.LibraryEmpty || vm.LibraryCards.Count != 0 || vm.Page != "all") throw new InvalidOperationException("全局搜索空状态失效。");
        vm.ActivatePage("models"); vm.ActivatePage("all");
        if (!vm.HasAppliedSearch || !vm.LibraryEmpty) throw new InvalidOperationException("返回搜索时丢失了筛选条件。");
        vm.ClearLibrarySearch(); vm.ActivatePage("models");
        vm.ExistingOnly = true;
        if (vm.VisibleSkins.Any(s => s.ExportDirectory is null)) throw new InvalidOperationException("已有结果筛选失效。");
        vm.ExistingOnly = false; SkinGrid.SelectAll();
        if (RunQueueButton.IsEnabled || vm.CanCancel) throw new InvalidOperationException("离线不应允许执行任务。");
        var before = vm.Jobs.Count; vm.IncludeSound = false;
        await vm.AddSelectedJobsAsync(SkinGrid.SelectedItems.Cast<SkinAsset>().ToList());
        if (!vm.QueueNotice || !vm.QueueNoticeText.Contains("已加入")) throw new InvalidOperationException("加入队列后缺少可见反馈。");
        ((FrameworkElement)Content).UpdateLayout();
        var noticeActionBounds = AddQueueButton.TransformToAncestor(PageArea).TransformBounds(new Rect(AddQueueButton.RenderSize));
        if (noticeActionBounds.Bottom > PageArea.ActualHeight) throw new InvalidOperationException("队列反馈挤出了操作按钮。");
        if (vm.Jobs.Count != before + 1 || vm.Jobs.Last().IncludeSound) throw new InvalidOperationException("资源内容选项未写入队列。");
        var job = vm.Jobs.Last();
        if (!Commands.Extract(vm.Settings, new ExportJob { HeroSelector = job.HeroSelector, SkinSelector = job.SkinSelector, OutputDirectory = "test-output", IncludeSound = false }).Arguments.Contains("--skip-sound")) throw new InvalidOperationException("音效选项未传递到参数。");
        await vm.RemoveAsync(job); vm.ResetContent(); vm.QueueNotice = false;
        vm.ExportOptionsSelected = true; ((FrameworkElement)Content).UpdateLayout();
        if (ExportOptionsPanel.Visibility != Visibility.Visible || ExportTab.IsChecked != true || DetailsTab.IsChecked == true) throw new InvalidOperationException("导出选项未独立切换。");
        vm.DetailsSelected = true;
        foreach (var category in new[] { "all", "voices", "music", "maps", "images", "queue", "settings", "models" }) { vm.Page = category; ((FrameworkElement)Content).UpdateLayout(); if (vm.PageName.Length == 0) throw new InvalidOperationException("分类导航失败。"); }
        await vm.SceneLoadTask; ((FrameworkElement)Content).UpdateLayout();
        if (vm.HasPreview)
        {
            if ((!vm.HasModel && ModelPreviewImage.Source is null) || !ExpandPreviewButton.IsEnabled) throw new InvalidOperationException("预览未绑定。");
            if (vm.HasBackPreview) { vm.RearView = true; if (vm.PreviewImage is null) throw new InvalidOperationException("背面缓存未载入。"); vm.FrontView = true; }
            vm.PreviewExpanded = true; ((FrameworkElement)Content).UpdateLayout();
            if (ExpandedPreview.Visibility != Visibility.Visible) throw new InvalidOperationException("放大预览未打开。");
            vm.PreviewExpanded = false;
        }
        if (vm.HasModel)
        {
            if (MainModelViewport.Scene != vm.Scene || ExpandedModelViewport.Orbit != vm.Camera || vm.Scene!.Triangles <= 0) throw new InvalidOperationException("3D 场景未绑定。");
            vm.Camera.Orbit(45, 20);
            if (vm.Camera.Yaw != 45 || vm.Camera.Pitch != 20) throw new InvalidOperationException("旋转失败。");
            if (vm.FrontView || vm.RearView) throw new InvalidOperationException("自由视角不应高亮正面或背面。");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent };
            MainModelViewport.RaiseEvent(wheel);
            MainModelViewport.SubmitPendingFrame(); MainModelViewport.FinishInteraction();
            if (vm.Camera.Zoom <= 1 || !wheel.Handled) throw new InvalidOperationException("滚轮缩放未处理。");
            vm.Camera.Pan(.2, -.1); vm.PreviewExpanded = true;
            if (ExpandedModelViewport.Orbit!.PanX != .2 || ExpandedModelViewport.Orbit.Yaw != 45 || vm.ShowMainModel) throw new InvalidOperationException("展开视图未保留相机。");
            vm.Camera.Fit();
            if (vm.Camera.Yaw != 45 || vm.Camera.Pitch != 20 || vm.Camera.Zoom != 1 || vm.Camera.PanX != 0) throw new InvalidOperationException("适应窗口改变了视角。");
            vm.Camera.Scale(1000); if (vm.Camera.Zoom != 5) throw new InvalidOperationException("缩放上限失效。");
            vm.Camera.Scale(.00001); if (vm.Camera.Zoom != .25) throw new InvalidOperationException("缩放下限失效。");
            vm.Camera.Orbit(0, 999); if (vm.Camera.Pitch != 85) throw new InvalidOperationException("俯仰边界失效。");
            vm.ResetPreview(); vm.PreviewExpanded = false;
            if (vm.Camera.Zoom != 1 || vm.Camera.PanX != 0 || vm.Camera.Yaw != 0) throw new InvalidOperationException("相机复位失败。");
            MainModelViewport.VerifyFramePipeline();
            vm.PreviewExpanded = true; ((FrameworkElement)Content).UpdateLayout();
            ExpandedModelViewport.VerifyFramePipeline();
            vm.PreviewExpanded = false; ((FrameworkElement)Content).UpdateLayout();
        }
        var originalMode = vm.Settings.PreviewRenderingMode; var originalFps = vm.PreviewTargetFps;
        vm.HardwarePreferred = true; vm.PreviewTargetFps = 60;
        if (!vm.SoftwareRendering || MainModelViewport.TargetFps != 60 || !vm.RenderPreferenceHint.Contains("下次启动")) throw new InvalidOperationException("硬件设置错误地影响了本次软件检查。");
        if (originalMode == "hardware") vm.HardwarePreferred = true; else vm.SoftwarePreferred = true;
        vm.PreviewTargetFps = originalFps;
        if (System.Windows.Media.RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly) throw new InvalidOperationException("检查过程启用了硬件渲染。");
        if (PreviewCanvas.ActualWidth < 360 || PreviewCanvas.ActualHeight < 300) throw new InvalidOperationException("大预览可用空间不足。");
        var actionBounds = AddQueueButton.TransformToAncestor(PageArea).TransformBounds(new Rect(AddQueueButton.RenderSize));
        if (actionBounds.Bottom > PageArea.ActualHeight || actionBounds.Right > PageArea.ActualWidth) throw new InvalidOperationException("队列操作被挤出了可见区域。");
        if (FontSize != 14 || AddQueueButton.FontSize != 14 || SkinSearchBox.FontSize != 14) throw new InvalidOperationException("正文与操作字号不一致。");
        if (SkinSearchBox.ActualHeight < 32 || InputBindings.Count < 2) throw new InvalidOperationException("搜索或键盘操作异常。");
        if (EngineRunner.StartedProcessCount != 0) throw new InvalidOperationException("离屏检查意外启动引擎。");
    }

    private static void VerifyPackedTextureChannels()
    {
        // This fixture exposes the black patches caused by scaling premultiplied packed alpha.
        var raw = new byte[1024 * 4];
        for (var x = 0; x < 1024; x++) { raw[x * 4] = 30; raw[x * 4 + 1] = 110; raw[x * 4 + 2] = 200; raw[x * 4 + 3] = (byte)(x < 512 ? 0 : 255); }
        var source = System.Windows.Media.Imaging.BitmapSource.Create(1024, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, raw, 4096);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        var decoded = PreviewTextureDecoder.Decode(stream, default); var pixels = new byte[decoded.Width * 4]; decoded.Color.CopyPixels(pixels, decoded.Width * 4, 0);
        if (decoded.Width != 512 || pixels[0] != 30 || pixels[1] != 110 || pixels[2] != 200 || decoded.Alpha[0] != 0 || decoded.Alpha[^1] != 255)
            throw new InvalidOperationException("颜色或透明遮罩在解码缩放中丢失。");
    }
}
