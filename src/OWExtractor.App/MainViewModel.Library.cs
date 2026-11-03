using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using OWExtractor.Core;

namespace OWExtractor.App;

public sealed partial class MainViewModel
{
    public ObservableCollection<SkinCard> LibraryCards { get; } = [];
    public ObservableCollection<object> LibraryItems { get; } = [];
    public ObservableCollection<AudioInventoryRow> AudioRows { get; } = [];
    public bool OfflineLocked { get; private set; }
    public bool CanToggleLive => !OfflineLocked && CanEdit;
    public void LockOffline() { OfflineLocked = true; LiveEnabled = false; Notify(nameof(OfflineLocked)); Notify(nameof(CanToggleLive)); }

    private string page = "models";
    public string Page
    {
        get => page;
        set
        {
            if (value is not ("all" or "models" or "music" or "voices" or "maps" or "images" or "queue" or "settings")) return;
            page = value; PreviewExpanded = false; Notify(); NotifyPage();
        }
    }
    public bool IsModelsPage => Page == "models";
    public bool IsLibraryPage => Page == "all";
    public bool IsVoicesPage => Page == "voices";
    public bool IsQueuePage => Page == "queue";
    public bool IsSettingsPage => Page == "settings";
    public bool IsEmptyPage => Page is "music" or "maps" or "images";
    public string PageName => Page switch { "all" => "全部资源", "models" => "模型与皮肤", "music" => "音乐", "voices" => "语音与音效", "maps" => "地图", "images" => "图像与贴图", "queue" => "提取队列", _ => "设置" };
    public string PageTitle => IsModelsPage ? SelectedSkin?.Name ?? HeroName : IsLibraryPage && HasAppliedSearch ? "搜索结果" : PageName;
    public string PageSubtitle => IsModelsPage ? SelectedSkin is { } skin ? $"{HeroName} · {skin.SelectorName}" : "选择英雄并导入之前的皮肤记录" : Page switch {
        "all" => HasAppliedSearch ? $"“{appliedLibrarySearch}” · 本地素材库" : "浏览已经导入的模型、声音与创作素材", "voices" => "浏览之前保存的语音、对话与音效统计",
        "queue" => QueueHint, "settings" => "调整浏览、预览与提取设置", _ => "从之前的本地结果开始浏览"
    };
    public string EmptyPageTitle => PageName + "文件浏览尚未接入";
    public string EmptyPageDescription => "此分类的文件浏览仍在开发中，当前不会扫描游戏资源。可先浏览已导入的模型与声音统计。";
    public int QueueCount => Jobs.Count;
    public double PreviewHeight => CompactLayout ? 450 : 600;
    private string librarySearch = "";
    private string appliedLibrarySearch = "";
    public string LibrarySearch
    {
        get => librarySearch;
        set { librarySearch = value; Notify(); }
    }
    public bool HasAppliedSearch => appliedLibrarySearch.Length > 0;
    public string LibraryEmptyTitle => HasAppliedSearch ? "没有匹配的本地资源" : "从本地资源开始";
    public string LibraryEmptyDescription => HasAppliedSearch ? "试试英雄名或皮肤名，也可以清除搜索。" : "导入之前保存的模型导出目录或历史资源清单。";
    public void ApplyLibrarySearch() { appliedLibrarySearch = LibrarySearch.Trim(); RefreshLibraryCards(); Page = "all"; Notify(nameof(HasAppliedSearch)); }
    public void ClearLibrarySearch() { LibrarySearch = ""; appliedLibrarySearch = ""; RefreshLibraryCards(); Notify(nameof(HasAppliedSearch)); NotifyPage(); }
    public bool HasLibraryCards => LibraryCards.Count > 0;
    public bool HasAudioSummaries => Settings.AudioSummaries.Any(x => x.Total > 0);
    public bool ShowAudioCard => HasAudioSummaries && (string.IsNullOrWhiteSpace(appliedLibrarySearch) || new[] { "英雄", "声音", "语音", "音效", "中文", "英文", "voice", "audio" }.Any(x => x.Contains(appliedLibrarySearch, StringComparison.OrdinalIgnoreCase)));
    public bool LibraryEmpty => !HasLibraryCards && !ShowAudioCard;
    public string LibraryCountText => $"{LibraryCards.Count} 个皮肤资源包" + (ShowAudioCard ? " · 英雄声音" : "");
    public void RefreshLibraryCards()
    {
        LibraryCards.Clear(); LibraryItems.Clear();
        foreach (var hero in Settings.Heroes)
        foreach (var skin in hero.Skins)
            if (string.IsNullOrWhiteSpace(appliedLibrarySearch) || new[] { hero.Name, hero.SelectorName, skin.Name, skin.SelectorName }.Any(x => Matches(x, appliedLibrarySearch)))
            { var card = new SkinCard(hero, skin, Settings.GamePath); LibraryCards.Add(card); LibraryItems.Add(card); }
        if (ShowAudioCard) LibraryItems.Add(new AudioCollectionCard(Settings.AudioSummaries.FirstOrDefault(x => x.Language == "zhCN")?.Total ?? 0, Settings.AudioSummaries.FirstOrDefault(x => x.Language == "enUS")?.Total ?? 0, Settings.AudioSummaries.FirstOrDefault(x => x.Language == "SFX")?.Total ?? 0));
        foreach (var name in new[] { nameof(HasLibraryCards), nameof(ShowAudioCard), nameof(LibraryEmpty), nameof(LibraryCountText), nameof(HasAudioSummaries), nameof(LibraryEmptyTitle), nameof(LibraryEmptyDescription) }) Notify(name);
    }
    public void ActivatePage(string target)
    {
        RefreshLibraryCards(); Page = target;
    }
    public void SelectCard(SkinCard card) { SelectedHero = Heroes.FirstOrDefault(x => x.Guid == card.Hero.Guid) ?? card.Hero; SelectedSkin = card.Skin; Page = "models"; }

    private ImageSource? frontPreview, backPreview;
    private bool rearView;
    public bool RearView { get => HasModel ? Math.Abs(Math.Abs(Camera.Yaw % 360) - 180) < .01 && Math.Abs(Camera.Pitch) < .01 : rearView; set { if (!value && HasModel) return; rearView = value && (backPreview is not null || HasModel); if (HasModel) Camera.Face(rearView); Notify(); Notify(nameof(FrontView)); Notify(nameof(PreviewImage)); } }
    public bool FrontView { get => HasModel ? Math.Abs(Camera.Yaw % 360) < .01 && Math.Abs(Camera.Pitch) < .01 : !rearView; set { if (value) { rearView = false; if (HasModel) Camera.Face(false); Notify(); Notify(nameof(RearView)); Notify(nameof(PreviewImage)); } } }
    public ImageSource? PreviewImage => rearView ? backPreview : frontPreview;
    public bool HasPreview => HasModel || PreviewImage is not null;
    public bool HasFrontPreview => HasModel || frontPreview is not null;
    public bool HasBackPreview => HasModel || backPreview is not null;
    private bool previewExpanded;
    public bool PreviewExpanded { get => previewExpanded; set { previewExpanded = value && HasPreview; Notify(); Notify(nameof(ShowMainModel)); } }
    public void RefreshLibrarySelection()
    {
        frontPreview = LibraryPreview.Load(SelectedSkin?.PreviewFrontPath, Settings.GamePath);
        backPreview = LibraryPreview.Load(SelectedSkin?.PreviewBackPath, Settings.GamePath);
        rearView = frontPreview is null && backPreview is not null;
        RefreshModel(); Notify(nameof(ShowCachedPreview));
        foreach (var name in new[] { nameof(PreviewImage), nameof(HasPreview), nameof(HasFrontPreview), nameof(HasBackPreview), nameof(RearView), nameof(FrontView), nameof(ResourceContentSummary), nameof(ModelsDetail), nameof(TexturesDetail), nameof(AnimationsDetail), nameof(SoundsDetail), nameof(AssemblyDetail) }) Notify(name);
        NotifyPage();
    }
    public async Task ImportPreviewAsync(string file, bool rear)
    {
        if (SelectedSkin is not { } skin) return;
        LibraryPreview.EnsureAllowed(file, Settings.GamePath);
        if (LibraryPreview.Load(file, Settings.GamePath) is null) throw new InvalidDataException("无法读取这张缓存预览图。");
        if (rear) skin.PreviewBackPath = Path.GetFullPath(file); else skin.PreviewFrontPath = Path.GetFullPath(file);
        RefreshLibrarySelection(); RefreshLibraryCards(); await SaveAsync(); Status = "已关联缓存预览图，不会解析模型或读取游戏。";
    }
    private int FileCount(params string[] extensions) => extensions.Sum(ext => SelectedSkin?.ExtensionCounts.GetValueOrDefault(ext) ?? 0);
    private string Detail(int count, string unit) => SelectedSkin?.ExtensionCounts.Count > 0 ? $"{count:N0} {unit}" : "尚无分类统计";
    public string ResourceContentSummary => SelectedSkin?.LocalFileSummary ?? "选择皮肤以查看内容";
    public string ModelsDetail => SelectedSkin is { } skin ? skin.ExtensionCounts.ContainsKey(".owmat") ? $"{skin.ModelCount:N0} 个模型 · {FileCount(".owmat"):N0} 个材质" : $"{skin.ModelCount:N0} 个模型" : "随皮肤导出基础模型与材质";
    public string TexturesDetail => Detail(FileCount(".png", ".dds", ".tif", ".tga", ".jpg", ".jpeg"), "张图像");
    public string AnimationsDetail => Detail(FileCount(".owanim", ".owanimclip", ".seanim"), "个动画文件");
    public string SoundsDetail => Detail(FileCount(".ogg", ".wav", ".wem"), "个声音文件");
    public string AssemblyDetail => Detail(FileCount(".owentity"), "个实体 · 自动附带");
    private bool includeTextures = true, includeAnimations = true, includeSound = true;
    public bool IncludeTextures { get => includeTextures; set { includeTextures = value; Notify(); Notify(nameof(SelectedContentSummary)); } }
    public bool IncludeAnimations { get => includeAnimations; set { includeAnimations = value; Notify(); Notify(nameof(SelectedContentSummary)); } }
    public bool IncludeSound { get => includeSound; set { includeSound = value; Notify(); Notify(nameof(SelectedContentSummary)); } }
    public string SelectedContentSummary => $"已选 {2 + (IncludeTextures ? 1 : 0) + (IncludeAnimations ? 1 : 0) + (IncludeSound ? 1 : 0)} 类资源 · {SelectionSummary}";
    public void ResetContent() { IncludeTextures = IncludeAnimations = IncludeSound = true; }

    private string audioLanguage = "zhCN";
    public string AudioLanguage { get => audioLanguage; set { audioLanguage = value; Notify(); RefreshAudioRows(); } }
    public string AudioSourceText => Settings.AudioSummaries.FirstOrDefault(x => x.Language == AudioLanguage) is { } summary ? $"历史资源版本 {summary.ResourceBuild} · 仅统计，尚未导入具体音频文件" : "尚未导入这个语言的历史统计";
    public void RefreshAudioRows()
    {
        AudioRows.Clear();
        if (Settings.AudioSummaries.FirstOrDefault(x => x.Language == AudioLanguage) is { } summary)
        {
            if (summary.HeroVoice > 0) AudioRows.Add(new("英雄语音", AudioLanguage == "zhCN" ? "中文" : "英文", summary.HeroVoice));
            if (summary.Conversations > 0) AudioRows.Add(new("英雄对话", AudioLanguage == "zhCN" ? "中文" : "英文", summary.Conversations));
            if (summary.SoundEffects > 0) AudioRows.Add(new("关联音效", "通用声音资源", summary.SoundEffects));
        }
        Notify(nameof(AudioSourceText)); Notify(nameof(HasAudioSummaries)); RefreshLibraryCards();
    }
    public async Task ImportAudioSummaryAsync(string file)
    {
        LibraryPreview.EnsureMetadataAllowed(file, Settings.GamePath);
        Settings.AudioSummaries = CachedAudioSummary.Parse(await File.ReadAllTextAsync(file));
        RefreshAudioRows(); await SaveAsync(); Page = "voices"; Status = "已导入之前的声音统计；未读取音频或游戏文件。";
    }
    private void NotifyPage()
    {
        foreach (var name in new[] { nameof(IsModelsPage), nameof(IsLibraryPage), nameof(IsVoicesPage), nameof(IsQueuePage), nameof(IsSettingsPage), nameof(IsEmptyPage), nameof(PageName), nameof(PageTitle), nameof(PageSubtitle), nameof(EmptyPageTitle), nameof(EmptyPageDescription) }) Notify(name);
    }
}
