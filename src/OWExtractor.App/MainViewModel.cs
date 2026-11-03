using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using OWExtractor.Core;
using OWExtractor.DataTool;

namespace OWExtractor.App;

public sealed partial class MainViewModel(string profile) : INotifyPropertyChanged
{
    private readonly SettingsStore store = new(profile);
    private readonly string jobPath = profile + ".jobs.json";
    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<HeroAsset> Heroes { get; } = [];
    public ObservableCollection<SkinAsset> VisibleSkins { get; } = [];
    public ObservableCollection<ExportJob> Jobs { get; } = [];
    private HeroAsset? selectedHero;
    public HeroAsset? SelectedHero { get => selectedHero; set { selectedHero = value; Notify(); RefreshSkins(); Notify(nameof(HeroTitle)); Notify(nameof(HeroName)); Notify(nameof(HeroOriginalName)); Notify(nameof(HeroResourceSummary)); } }
    private SkinAsset? selectedSkin;
    public SkinAsset? SelectedSkin { get => selectedSkin; set { selectedSkin = value; Notify(); RefreshLibrarySelection(); NotifySelection(); } }
    private ExportJob? selectedJob;
    public ExportJob? SelectedJob { get => selectedJob; set { selectedJob = value; Notify(); NotifyActions(); } }
    private int selectedCount;
    public int SelectedCount { get => selectedCount; set { selectedCount = value; Notify(); Notify(nameof(SelectionSummary)); Notify(nameof(SelectedContentSummary)); NotifyActions(); } }
    private bool existingOnly;
    public bool ExistingOnly { get => existingOnly; set { existingOnly = value; Notify(); RefreshSkins(); } }
    private string heroSearch = "";
    public string HeroSearch { get => heroSearch; set { heroSearch = value; Notify(); Notify(nameof(HasHeroSearch)); RefreshHeroes(); } }
    private string skinSearch = "";
    public string SkinSearch { get => skinSearch; set { skinSearch = value; Notify(); RefreshSkins(); } }
    private bool liveEnabled;
    public bool LiveEnabled { get => liveEnabled; set { liveEnabled = value && !OfflineLocked; Notify(); NotifyActions(); Notify(nameof(ModeText)); Notify(nameof(ModeLabel)); Notify(nameof(QueueHint)); Notify(nameof(QueueActionHint)); } }
    private bool busy;
    public bool Busy { get => busy; private set { busy = value; Notify(); Notify(nameof(CanEdit)); Notify(nameof(CanToggleLive)); NotifyActions(); } }
    public bool CanEdit => !Busy;
    public bool CanReadGame => LiveEnabled && !Busy;
    public bool CanRun => CanReadGame && Jobs.Any(j => j.State == JobState.Pending);
    public bool CanCancel => cancellation is not null;
    public bool CanAdd => !Busy && HasSkins && SelectedCount > 0;
    public bool CanSelectAll => VisibleSkins.Count > 0;
    public bool CanOpenExport => SelectedSkin?.ExportDirectory is { Length: > 0 };
    public bool CanPreview => SelectedSkin is not null;
    public bool CanRetry => !Busy && SelectedJob?.State is JobState.Failed or JobState.Cancelled;
    public bool CanRemove => !Busy && SelectedJob is not null && SelectedJob.State != JobState.Running;
    public bool CanOpenJob => SelectedJob?.OutputDirectory is { Length: > 0 };
    public bool CanManageJob => SelectedJob is not null;
    public bool HasSelectedSkin => SelectedSkin is not null;
    public bool HasSkins => VisibleSkins.Count > 0;
    public bool HasHeroes => Heroes.Count > 0;
    public bool HasHeroSearch => !string.IsNullOrEmpty(HeroSearch);
    public string EmptyHeroTitle => Settings.Heroes.Count == 0 ? "尚未导入英雄列表" : "没有匹配的英雄";
    public bool HasJobs => Jobs.Count > 0;
    public bool HasSkinFilter => !string.IsNullOrEmpty(SkinSearch) || ExistingOnly;
    private bool compactLayout;
    public bool CompactLayout { get => compactLayout; set { if (compactLayout == value) return; compactLayout = value; Notify(); Notify(nameof(PreviewHeight)); } }
    public string ModeLabel => LiveEnabled ? "游戏读取已启用" : "离线浏览";
    public string ModeText => LiveEnabled ? "启动前会检查游戏状态，请在整个提取期间保持游戏退出" : "当前使用已导入的资源记录，可浏览旧结果并安排待执行任务";
    public string QueueHint => LiveEnabled ? "任务按顺序执行，每次使用独立输出目录" : "离线模式只保存队列，退出游戏后可在设置中启用提取";
    public string HeroName => SelectedHero?.Name ?? "资源库";
    public string HeroOriginalName => SelectedHero?.SelectorName ?? "导入之前保存的资源，开始浏览";
    public string HeroResourceSummary => SelectedHero is { } hero ? $"{hero.Skins.Count} 个皮肤记录 · {hero.Skins.Count(s => s.ExportDirectory is not null)} 个已有结果" : "尚未选择英雄";
    public string HeroCountText => $"{Heroes.Count} / {Settings.Heroes.Count}";
    public string SelectionSummary => SelectedCount == 0 ? "选择皮肤以加入队列" : $"已选 {SelectedCount} 项";
    public string QueueSummary => Jobs.Count == 0 ? "暂无任务" : $"{Jobs.Count} 项任务 · {Jobs.Count(j => j.State == JobState.Pending)} 项待执行";
    public string EmptySkinTitle => HasSkinFilter ? "没有匹配的皮肤" : SelectedHero is null ? "从已有结果开始" : "还没有这个英雄的皮肤记录";
    public string EmptySkinDescription => HasSkinFilter ? "试试其他名称，或清除筛选条件" : "导入 DataTool 的旧导出目录，或之前保存的皮肤 JSON";
    public string HeroTitle => SelectedHero is null ? "选择英雄" : $"{SelectedHero.Name}  /  {SelectedHero.SelectorName}";
    public string CatalogSummary => $"{Settings.Heroes.Count} 位英雄  ·  {Settings.Heroes.Sum(x => x.Skins.Count)} 个皮肤记录  ·  {Settings.Heroes.Sum(x => x.Skins.Count(s => s.ExportDirectory is not null))} 个已有导出";
    private string status = "准备就绪";
    public string Status { get => status; private set { status = value; Notify(); } }
    private string log = "";
    public string Log { get => log; private set { log = value; Notify(); } }
    private CancellationTokenSource? cancellation;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void NotifyActions()
    {
        foreach (var name in new[] { nameof(CanReadGame), nameof(CanRun), nameof(CanCancel), nameof(CanAdd), nameof(CanSelectAll), nameof(CanOpenExport), nameof(CanPreview), nameof(CanRetry), nameof(CanRemove), nameof(CanOpenJob), nameof(CanManageJob) }) Notify(name);
    }
    private void NotifySelection() { Notify(nameof(HasSelectedSkin)); NotifyActions(); }
    private void NotifyJobs() { Notify(nameof(QueueSummary)); Notify(nameof(HasJobs)); Notify(nameof(QueueCount)); NotifyActions(); }

    public async Task InitializeAsync()
    {
        Camera.Changed -= PreviewCameraChanged;
        Camera.Changed += PreviewCameraChanged;
        Settings = await store.LoadAsync();
        if (File.Exists(jobPath))
        {
            foreach (var job in JsonSerializer.Deserialize<List<ExportJob>>(await File.ReadAllTextAsync(jobPath), SettingsStore.Options) ?? [])
            { if (job.State == JobState.Running) { job.State = JobState.Failed; job.Message = "上次执行中断，可重试"; } Jobs.Add(job); }
        }
        RefreshHeroes();
        SelectedHero = Heroes.FirstOrDefault(h => h.Skins.Any(s => s.ExportDirectory is not null)) ?? Heroes.FirstOrDefault();
        AddLog("已进入离线模式。此模式不会启动 DataTool，也不会读取游戏安装目录。");
        Status = Settings.Heroes.Count == 0 ? "导入已有英雄 JSON 或已提取目录，开始离线浏览。" : "已载入本地目录缓存。";
        Notify(nameof(Settings));
        RefreshAudioRows();
        NotifyJobs();
    }

    public void RefreshHeroes()
    {
        var prior = SelectedHero;
        Heroes.Clear();
        foreach (var h in Settings.Heroes.Where(x => Matches(x.Name, HeroSearch) || Matches(x.SelectorName, HeroSearch)).OrderByDescending(h => h.Skins.Any(s => s.ExportDirectory is not null)).ThenBy(h => h.Name)) Heroes.Add(h);
        SelectedHero = Heroes.FirstOrDefault(h => h.Guid == prior?.Guid) ?? Heroes.FirstOrDefault();
        Notify(nameof(CatalogSummary));
        Notify(nameof(HeroCountText)); Notify(nameof(HasHeroes)); Notify(nameof(EmptyHeroTitle));
        RefreshLibraryCards();
    }
    private static bool Matches(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
    private void RefreshSkins()
    {
        var prior = SelectedSkin;
        VisibleSkins.Clear();
        foreach (var skin in (SelectedHero?.Skins ?? []).Where(x => (Matches(x.Name, SkinSearch) || Matches(x.SelectorName, SkinSearch)) && (!ExistingOnly || x.ExportDirectory is not null)).OrderByDescending(x => x.ExportDirectory is not null).ThenBy(x => x.Name)) VisibleSkins.Add(skin);
        SelectedSkin = VisibleSkins.FirstOrDefault(s => s == prior) ?? VisibleSkins.FirstOrDefault();
        Notify(nameof(HasSkins)); Notify(nameof(HasSkinFilter)); Notify(nameof(EmptySkinTitle)); Notify(nameof(EmptySkinDescription)); NotifyActions();
    }
    public async Task ImportHeroesAsync(string file, bool localized)
    {
        LibraryPreview.EnsureMetadataAllowed(file, Settings.GamePath);
        var json = await File.ReadAllTextAsync(file);
        if (localized) CatalogService.ApplyLocalizedNames(Settings.Heroes, json);
        else Settings.Heroes = CatalogService.ParseHeroes(json, Settings.Heroes);
        RefreshHeroes(); RefreshSkins(); await SaveAsync();
        Status = localized ? "已合并英雄中文名称。" : "已导入英雄列表；界面显示的是缓存记录。";
        AddLog(Status);
    }
    public async Task ImportUnlocksAsync(string file)
    {
        LibraryPreview.EnsureMetadataAllowed(file, Settings.GamePath);
        var count = CatalogService.ImportUnlocks(Settings.Heroes, await File.ReadAllTextAsync(file));
        RefreshHeroes(); RefreshSkins(); await SaveAsync(); Status = $"已导入 {count} 个皮肤记录。"; AddLog(Status);
    }
    public async Task ImportExportsAsync(string root)
    {
        CatalogService.EnsureExportPath(root);
        Busy = true; Status = "正在读取既有导出目录的文件清单…";
        try
        {
            var count = await Task.Run(() => CatalogService.ImportExportFolder(Settings.Heroes, root));
            RefreshHeroes(); SelectedHero = Heroes.FirstOrDefault(h => h.Skins.Any(s => s.ExportDirectory is not null));
            await SaveAsync(); Status = $"已关联 {count} 个旧皮肤导出，原文件保持不变。"; AddLog(Status);
        }
        finally { Busy = false; }
    }

    public async Task SaveAsync()
    {
        await store.SaveAsync(Settings);
        await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(Jobs, SettingsStore.Options));
    }
    public async Task AddJobsAsync(IEnumerable<SkinAsset> selected)
    {
        if (SelectedHero is null) return;
        var count = 0;
        foreach (var skin in selected)
        {
            if (Jobs.Any(j => j.HeroSelector == SelectedHero.SelectorName && j.SkinSelector == skin.SelectorName && (j.State is JobState.Pending or JobState.Running)
                && j.IncludeTextures == IncludeTextures && j.IncludeAnimations == IncludeAnimations && j.IncludeSound == IncludeSound)) continue;
            Jobs.Add(new ExportJob { HeroName = SelectedHero.Name, HeroSelector = SelectedHero.SelectorName, SkinName = skin.Name, SkinSelector = skin.SelectorName,
                IncludeTextures = IncludeTextures, IncludeAnimations = IncludeAnimations, IncludeSound = IncludeSound }); count++;
        }
        NotifyJobs();
        await SaveAsync(); Status = count == 0 ? "所选皮肤已在待执行队列中" : $"已加入 {count} 个任务" + (LiveEnabled ? "" : " · 离线模式下不执行提取");
        AddLog(Status);
    }
    public string Preview(SkinAsset skin)
    {
        if (SelectedHero is null) throw new InvalidOperationException("请先选择英雄。");
        return Commands.Extract(Settings, new ExportJob { HeroSelector = SelectedHero.SelectorName, SkinSelector = skin.SelectorName,
            OutputDirectory = Path.Combine(string.IsNullOrWhiteSpace(Settings.OutputPath) ? "<导出目录>" : Settings.OutputPath, "<独立任务目录>") }).Display;
    }
    public async Task RefreshLiveAsync()
    {
        var gate = new LiveAccessGate(() => LiveEnabled); gate.EnsureAllowed();
        EnsurePaths(); cancellation = new(); Busy = true;
        try
        {
            var cacheDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profile))!, "catalog", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(cacheDir);
            var runner = new EngineRunner(gate);
            foreach (var unlocks in new[] { false, true })
            {
                Status = unlocks ? "正在读取皮肤目录…" : "正在读取英雄目录…";
                var output = Path.Combine(cacheDir, unlocks ? "unlocks.json" : "heroes.json");
                var result = await runner.RunAsync(Commands.List(Settings, unlocks, output), EngineLog, cancellation.Token);
                if (result.Cancelled || result.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException(EngineRunner.Explain(result));
                if (unlocks) CatalogService.ImportUnlocks(Settings.Heroes, await File.ReadAllTextAsync(output));
                else Settings.Heroes = CatalogService.ParseHeroes(await File.ReadAllTextAsync(output), Settings.Heroes);
            }
            Settings.EngineVersion = FileVersionInfo.GetVersionInfo(Settings.EnginePath).FileVersion ?? "未知";
            RefreshHeroes(); RefreshSkins(); await SaveAsync(); Status = "资源目录已更新。";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
    public async Task RunJobsAsync()
    {
        var gate = new LiveAccessGate(() => LiveEnabled); gate.EnsureAllowed(); EnsurePaths();
        cancellation = new(); Busy = true;
        try
        {
            foreach (var job in Jobs.Where(j => j.State == JobState.Pending).ToList())
            {
                if (cancellation.IsCancellationRequested) break;
                gate.EnsureAllowed();
                job.OutputDirectory = Path.Combine(Settings.OutputPath, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + job.Id + "-" + Guid.NewGuid().ToString("N")[..6]);
                Directory.CreateDirectory(job.OutputDirectory);
                job.State = JobState.Running; job.Message = "DataTool 正在处理"; RefreshJobs(); await SaveAsync();
                Status = "正在提取：" + job.Title;
                try
                {
                    var result = await new EngineRunner(gate).RunAsync(Commands.Extract(Settings, job), EngineLog, cancellation.Token);
                    var hasModel = Directory.EnumerateFiles(job.OutputDirectory, "*.owmdl", SearchOption.AllDirectories).Any(f => new FileInfo(f).Length > 0);
                    job.State = result.Cancelled ? JobState.Cancelled : result.ExitCode == 0 && hasModel ? JobState.Succeeded : JobState.Failed;
                    job.Message = job.State == JobState.Succeeded ? "已找到非空模型文件；材质与 Blender 导入仍需核对" : EngineRunner.Explain(result) + (!hasModel && !result.Cancelled ? " 未发现非空模型文件。" : "");
                    await File.WriteAllLinesAsync(Path.Combine(job.OutputDirectory, "OWExtractor.log"), result.Tail);
                }
                catch (Exception ex) { job.State = JobState.Failed; job.Message = ex.Message; AddLog(ex.Message); }
                await File.WriteAllTextAsync(Path.Combine(job.OutputDirectory, "OWExtractor-task.json"), JsonSerializer.Serialize(new { job, Settings.EngineVersion, Settings.GameVersion, Settings.Language }, SettingsStore.Options));
                RefreshJobs(); await SaveAsync();
            }
            Status = cancellation.IsCancellationRequested ? "已停止队列，未执行任务保留。" : "本轮队列处理结束，请查看任务结果。";
        }
        finally { cancellation.Dispose(); cancellation = null; Busy = false; }
    }
    private void EnsurePaths()
    {
        if (!File.Exists(Settings.EnginePath) || !Path.GetFileName(Settings.EnginePath).Equals("DataTool.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择有效的 DataTool.exe。");
        if (string.IsNullOrWhiteSpace(Settings.GamePath)) throw new InvalidDataException("请设置游戏目录。");
        if (string.IsNullOrWhiteSpace(Settings.OutputPath)) throw new InvalidDataException("请设置导出目录。");
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Settings.GamePath));
        var output = Path.GetFullPath(Settings.OutputPath);
        if (output.Equals(game, StringComparison.OrdinalIgnoreCase) || output.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("导出目录不能位于游戏安装目录内。");
    }
    public async Task RetryAsync(ExportJob? job)
    {
        if (job is null || job.State is JobState.Running or JobState.Succeeded) return;
        job.State = JobState.Pending; job.Message = "等待重试"; RefreshJobs(); await SaveAsync();
    }
    public async Task RemoveAsync(ExportJob? job) { if (job is null || job.State == JobState.Running) return; Jobs.Remove(job); NotifyJobs(); await SaveAsync(); }
    private void RefreshJobs() { System.Windows.Data.CollectionViewSource.GetDefaultView(Jobs).Refresh(); NotifyJobs(); }
    public void Cancel() => cancellation?.Cancel();
    private void EngineLog(string line) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => AddLog(line));
    public void AddLog(string line)
    {
        Log += $"[{DateTime.Now:HH:mm:ss}] {line}\n";
        if (Log.Length > 40000) Log = Log[^30000..];
    }
}
