using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OWExtractor.Core;

namespace OWExtractor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private bool savedForClose;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); vm = viewModel; DataContext = vm;
        Closing += OnClosing;
        SizeChanged += (_, e) => vm.CompactLayout = e.NewSize.Height < 840 || e.NewSize.Width < 1200;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (savedForClose) return;
        if (vm.Busy) { e.Cancel = true; vm.Cancel(); vm.AddLog("正在取消当前操作；停止后可以关闭窗口。"); return; }
        e.Cancel = true;
        try { await vm.SaveAsync(); savedForClose = true; Close(); }
        catch (Exception ex) { vm.AddLog("保存失败：" + ex.Message); MessageBox.Show(this, "保存设置失败，请检查配置目录：" + ex.Message, "OWExtractor"); }
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
    private async void ImportExports(object sender, RoutedEventArgs e) { if (vm.Busy) return; if (PickFolder("选择已有 DataTool 导出目录，不选择游戏安装目录") is { } root) await Run(() => vm.ImportExportsAsync(root)); }
    private async void ChooseEngine(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "DataTool|DataTool.exe" }; if (dialog.ShowDialog(this) == true) { vm.Settings.EnginePath = dialog.FileName; await ReloadBindings(); } }
    private async void ChooseGame(object sender, RoutedEventArgs e) { if (PickFolder("选择游戏安装目录；此操作只记录路径") is { } path) { vm.Settings.GamePath = path; await ReloadBindings(); } }
    private async void ChooseOutput(object sender, RoutedEventArgs e) { if (PickFolder("选择导出目录") is { } path) { vm.Settings.OutputPath = path; await ReloadBindings(); } }
    private async Task ReloadBindings() { DataContext = null; DataContext = vm; await Run(vm.SaveAsync); }
    private async void SaveSettings(object sender, RoutedEventArgs e) => await Run(async () => { await vm.SaveAsync(); vm.AddLog("设置已保存。"); });
    private void CloseSettings(object sender, RoutedEventArgs e) => SettingsExpander.IsExpanded = false;
    private async void SaveSettingsAndReturn(object sender, RoutedEventArgs e)
    {
        try { await vm.SaveAsync(); vm.AddLog("设置已保存"); SettingsExpander.IsExpanded = false; }
        catch (Exception ex) { vm.AddLog(ex.Message); MessageBox.Show(this, "保存设置失败：" + ex.Message, "OWExtractor"); }
    }
    private async void RefreshLive(object sender, RoutedEventArgs e) => await Run(vm.RefreshLiveAsync);
    private void FindSkin(object sender, ExecutedRoutedEventArgs e) { SkinSearchBox.Focus(); SkinSearchBox.SelectAll(); e.Handled = true; }
    private void ClearHeroSearch(object sender, RoutedEventArgs e) => vm.HeroSearch = "";
    private void ClearSkinFilter(object sender, RoutedEventArgs e) { vm.SkinSearch = ""; vm.ExistingOnly = false; }
    private void ClearSelection(object sender, RoutedEventArgs e) => SkinGrid.UnselectAll();
    private void SkinSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel model) model.SelectedCount = SkinGrid.SelectedItems.Count;
    }
    private void OpenImportMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void CopyExportPath(object sender, RoutedEventArgs e)
    {
        if (vm.SelectedSkin?.ExportDirectory is { } path)
        {
            try { Clipboard.SetText(path); vm.AddLog("已复制资源路径"); }
            catch (System.Runtime.InteropServices.COMException) { vm.AddLog("剪贴板暂时不可用，请稍后重试"); }
        }
    }
    private void SelectAll(object sender, RoutedEventArgs e) => SkinGrid.SelectAll();
    private async void AddJobs(object sender, RoutedEventArgs e) => await Run(() => vm.AddJobsAsync(SkinGrid.SelectedItems.Cast<SkinAsset>().ToList()));
    private async void RunJobs(object sender, RoutedEventArgs e) => await Run(vm.RunJobsAsync);
    private void CancelJobs(object sender, RoutedEventArgs e) => vm.Cancel();
    private async void RetryJob(object sender, RoutedEventArgs e) => await Run(() => vm.RetryAsync(JobGrid.SelectedItem as ExportJob));
    private async void RemoveJob(object sender, RoutedEventArgs e) => await Run(() => vm.RemoveAsync(JobGrid.SelectedItem as ExportJob));
    private void OpenExport(object sender, RoutedEventArgs e) { if (SkinGrid.SelectedItem is SkinAsset { ExportDirectory: { } path }) OpenFolder(path); }
    private void OpenJob(object sender, RoutedEventArgs e) { if (JobGrid.SelectedItem is ExportJob { OutputDirectory.Length: > 0 } job) OpenFolder(job.OutputDirectory); }
    private void OpenFolder(string path) { if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(path) }, UseShellExecute = false }); }
    private void PreviewCommand(object sender, RoutedEventArgs e)
    {
        if (SkinGrid.SelectedItem is not SkinAsset skin) return;
        try { MessageBox.Show(this, vm.Preview(skin) + "\n\n这里只展示命令，不执行。", "提取命令", MessageBoxButton.OK); }
        catch (Exception ex) { vm.AddLog(ex.Message); MessageBox.Show(this, ex.Message, "OWExtractor"); }
    }

    internal void VerifyOfflineView()
    {
        if (SettingsExpander.IsExpanded) throw new InvalidOperationException("低频设置应默认收起");
        SkinGrid.UnselectAll();
        if (vm.SelectedCount != 0 || vm.CanAdd || AddQueueButton.IsEnabled) throw new InvalidOperationException("无选择时加入队列按钮应禁用");
        SkinGrid.SelectAll();
        if (vm.SelectedCount != vm.VisibleSkins.Count || !vm.CanAdd || !AddQueueButton.IsEnabled) throw new InvalidOperationException("皮肤选择未更新操作状态");
        vm.SkinSearch = "__OWExtractor_no_match__";
        if (vm.HasSkins || vm.CanAdd || !vm.HasSkinFilter) throw new InvalidOperationException("搜索无结果时应显示空状态并禁用加入队列");
        vm.SkinSearch = "";
        vm.ExistingOnly = true;
        if (vm.VisibleSkins.Any(s => s.ExportDirectory is null)) throw new InvalidOperationException("仅已有结果筛选失效");
        vm.ExistingOnly = false;
        SkinGrid.SelectAll();
        if (RunQueueButton.IsEnabled || vm.CanCancel || vm.CanRetry) throw new InvalidOperationException("离线和无所选任务的操作状态错误");
        if (vm.Jobs.FirstOrDefault() is { } job)
        {
            vm.SelectedJob = job;
            if (!vm.CanManageJob || !vm.CanRemove || vm.CanRetry) throw new InvalidOperationException("所选待执行任务的操作状态错误");
            vm.SelectedJob = null;
        }
        if (SkinSearchBox.ActualHeight < 32 || SkinSearchBox.ActualHeight > 40) throw new InvalidOperationException("搜索控件高度超出紧凑尺寸");
        if (SkinGrid.ActualWidth < 160 || SkinGrid.ActualHeight < 44) throw new InvalidOperationException("资源列表可用空间不足");
        SettingsExpander.IsExpanded = true;
        UpdateLayout();
        if (!SettingsPage.IsVisible && SettingsPage.Visibility != Visibility.Visible) throw new InvalidOperationException("设置面板未展开");
        if (LibraryArea.IsEnabled || QueueArea.IsEnabled) throw new InvalidOperationException("设置面板打开时背景操作应禁用");
        SettingsExpander.IsExpanded = false;
        UpdateLayout();
        if (InputBindings.Count < 2) throw new InvalidOperationException("缺少键盘快捷操作");
    }
}
