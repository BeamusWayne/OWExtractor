using System.Text.Json;
using System.Text.Json.Serialization;

namespace OWExtractor.Core;

public sealed class HeroAsset
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string SelectorName { get; set; } = "";
    public string Role { get; set; } = "";
    public List<SkinAsset> Skins { get; set; } = [];
    [JsonIgnore] public string Summary => $"{Role} · {Skins.Count} 个皮肤记录";
}

public sealed class SkinAsset
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string SelectorName { get; set; } = "";
    public string? ExportDirectory { get; set; }
    public int FileCount { get; set; }
    public int ModelCount { get; set; }
    public long Bytes { get; set; }
    [JsonIgnore] public string State => ExportDirectory is null ? "目录记录" : "已有导出";
    [JsonIgnore] public string LocalFileSummary => ExportDirectory is null ? "尚无本地文件" : $"{FileCount:N0} 个文件 · {Bytes / 1048576d:N1} MB";
    [JsonIgnore] public string SizeLabel => $"{Bytes / 1048576d:N1} MB";
    [JsonIgnore] public string Details => ExportDirectory is null ? "尚无本地导出记录" : $"{ModelCount} 个模型 · {FileCount:N0} 个文件 · {Bytes / 1048576d:N1} MB";
}

public sealed class AppSettings
{
    public string EnginePath { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Language { get; set; } = "enUS";
    public string EngineVersion { get; set; } = "未验证";
    public string GameVersion { get; set; } = "未知";
    public List<HeroAsset> Heroes { get; set; } = [];
}

public sealed class SettingsStore(string path)
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public async Task<AppSettings> LoadAsync() => File.Exists(path)
        ? JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(path), Options) ?? new()
        : new();

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, true);
    }
}

public enum JobState { Pending, Running, Succeeded, Failed, Cancelled }

public sealed class ExportJob
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
    public string HeroName { get; set; } = "";
    public string HeroSelector { get; set; } = "";
    public string SkinName { get; set; } = "";
    public string SkinSelector { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public JobState State { get; set; }
    public string Message { get; set; } = "等待执行";
    [JsonIgnore] public string Title => $"{HeroName} / {SkinName}";
    [JsonIgnore] public string StatusText => State switch {
        JobState.Pending => "待执行", JobState.Running => "提取中", JobState.Succeeded => "已完成",
        JobState.Failed => "失败", JobState.Cancelled => "已取消", _ => "未知"
    };
}
