using System.Text.Json;

namespace OWExtractor.Core;

// Historical inventory only. Importing this metadata never opens or decodes audio/game files.
public sealed class CachedAudioSummary
{
    public string Language { get; set; } = "";
    public int HeroVoice { get; set; }
    public int Conversations { get; set; }
    public int SoundEffects { get; set; }
    public string ResourceBuild { get; set; } = "";
    public int Total => HeroVoice + Conversations + SoundEffects;

    public static List<CachedAudioSummary> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("请选择之前保存的声音统计 JSON（含 stats 与 sections）。");
        var build = doc.RootElement.TryGetProperty("resource_build", out var version) ? version.GetString() ?? "" : "";
        var result = new List<CachedAudioSummary>();
        foreach (var language in new[] { "zhCN", "enUS", "SFX" })
        {
            if (!stats.TryGetProperty(language, out var item) || !item.TryGetProperty("sections", out var sections)) continue;
            int Count(string name) => sections.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) && count >= 0 && count <= 1_000_000
                ? count : 0;
            result.Add(new() { Language = language, HeroVoice = Count("英雄语音"), Conversations = Count("英雄对话"), SoundEffects = Count("关联音效"), ResourceBuild = build });
        }
        if (!result.Any(x => x.Total > 0)) throw new InvalidDataException("文件中没有可识别的声音统计。");
        return result;
    }
}
