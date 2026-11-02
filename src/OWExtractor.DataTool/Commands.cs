using System.Diagnostics;
using OWExtractor.Core;

namespace OWExtractor.DataTool;

public sealed record EngineCommand(string Executable, IReadOnlyList<string> Arguments)
{
    public string Display => string.Join(" ", new[] { Executable }.Concat(Arguments).Select(x => "\"" + x.Replace("\"", "\\\"") + "\""));
    public ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(Executable))! };
        foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        // Never inherit hooks/CLI overrides used by an unrelated local extraction experiment.
        foreach (var name in new[] { "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DATATOOL_OVERWATCH_DIR" }) info.Environment.Remove(name);
        return info;
    }
}

public static class Commands
{
    public static EngineCommand List(AppSettings settings, bool unlocks, string outputFile)
    {
        var args = Base(settings, unlocks ? "list-unlocks" : "list-heroes");
        args.Add("--json"); args.Add("--out=" + outputFile);
        if (unlocks) args.Add("--flatten");
        return new(settings.EnginePath, args);
    }

    public static EngineCommand Extract(AppSettings settings, ExportJob job)
    {
        ValidateSelector(job.HeroSelector); ValidateSelector(job.SkinSelector);
        if (string.IsNullOrWhiteSpace(job.OutputDirectory)) throw new InvalidDataException("任务缺少导出目录。");
        var args = new List<string> { settings.GamePath, "extract-unlocks", job.OutputDirectory, $"{job.HeroSelector}|skin={job.SkinSelector}" };
        AddFlags(settings, args);
        args.Add("--no-parallel");
        return new(settings.EnginePath, args);
    }

    private static List<string> Base(AppSettings settings, string mode)
    {
        var args = new List<string> { settings.GamePath, mode };
        AddFlags(settings, args); return args;
    }
    private static void AddFlags(AppSettings settings, List<string> args)
    {
        if (settings.Language is not ("enUS" or "zhCN" or "zhTW")) throw new InvalidDataException("请选择支持的资源语言。");
        args.Add("--language=" + settings.Language); args.Add("--speech-language=" + settings.Language); args.Add("--disable-language-registry");
        args.Add("--args-reset");
    }
    private static void ValidateSelector(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['|', '*', '!', ',', '(', ')', '"', '\r', '\n', '=']) >= 0)
            throw new InvalidDataException("此名称包含 DataTool 查询保留字符，暂不执行；请通过上游确认可用的筛选标识。");
    }
}
