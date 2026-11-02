using System.Text.Json;
using OWExtractor.Core;
using OWExtractor.DataTool;

// Dependency-free integration checks; children are this test executable, never DataTool.
if (args.FirstOrDefault() == "--child")
{
    if (args.Contains("--wait")) { Console.WriteLine("ready"); await Task.Delay(30000); }
    else { Console.WriteLine(JsonSerializer.Serialize(args.Skip(1))); Console.Error.WriteLine("stderr-marker"); }
    return 0;
}

var temp = Path.Combine(Path.GetTempPath(), "OWExtractor-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var passed = new List<string>();
var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed.Add(name); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Task Sync(Action test) { test(); return Task.CompletedTask; }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
}

await Test("Hero JSON and GUID-based localization preserve original selectors", () => Sync(() => {
    var heroes = CatalogService.ParseHeroes("""{"001":{"GUID":"001","Name":"Test Hero","IsHero":true,"Class":"Support"},"002":{"Name":"NPC","IsHero":false}}""");
    CatalogService.ApplyLocalizedNames(heroes, """{"001":{"GUID":"001","Name":"测试英雄","IsHero":true}}""");
    Check(heroes.Count == 1 && heroes[0].Name == "测试英雄" && heroes[0].SelectorName == "Test Hero", "localization corrupted selector");
    Throws<InvalidDataException>(() => CatalogService.ParseHeroes("[]"));
    Throws<InvalidDataException>(() => CatalogService.ParseHeroes("{}"));
}));
await Test("Flat and nested unlock JSON only import skins and deduplicate", () => Sync(() => {
    var heroes = new List<HeroAsset> { new() { Name = "测试英雄", SelectorName = "Test Hero" } };
    CatalogService.ImportUnlocks(heroes, """{"Test Hero":[{"GUID":"skin1","Name":"Dress","Type":"Skin"},{"Name":"Spray","Type":"Spray"}]}""");
    CatalogService.ImportUnlocks(heroes, """{"Test Hero":{"LevelUnlocks":[{"Unlocks":[{"GUID":"skin1","Name":"Dress","Type":"Skin"}]}]}}""");
    Check(heroes[0].Skins.Count == 1 && heroes[0].Skins[0].Key == "skin1", "skin parser mismatch");
}));
await Test("Existing export import counts files without changing content", async () => {
    var folder = Path.Combine(temp, "exports", "Heroes", "Test Hero", "Skin", "Season", "Dress");
    Directory.CreateDirectory(folder);
    await File.WriteAllBytesAsync(Path.Combine(folder, "model.owmdl"), [1, 2, 3]);
    await File.WriteAllBytesAsync(Path.Combine(folder, "texture.png"), [4, 5]);
    var heroes = new List<HeroAsset> { new() { SelectorName = "Test Hero", Name = "测试英雄" } };
    Check(CatalogService.ImportExportFolder(heroes, Path.Combine(temp, "exports")) == 1, "export missing");
    Check(heroes[0].Skins[0].ModelCount == 1 && heroes[0].Skins[0].Bytes == 5, "inventory mismatch");
    Check((await File.ReadAllBytesAsync(Path.Combine(folder, "model.owmdl"))).SequenceEqual(new byte[] { 1, 2, 3 }), "source changed");
    Throws<InvalidDataException>(() => CatalogService.EnsureExportPath(Path.Combine(temp, "Overwatch", "Data")));
    Throws<InvalidDataException>(() => CatalogService.EnsureExportPath(Path.Combine(temp, "game-view-123")));
});
await Test("Selectors and Chinese/space paths remain distinct arguments", () => Sync(() => {
    var config = new AppSettings { EnginePath = Environment.ProcessPath!, GamePath = @"D:\游戏 目录", Language = "enUS" };
    var job = new ExportJob { HeroSelector = "Test Hero", SkinSelector = "Evening Dress", OutputDirectory = @"D:\结果 目录\" };
    var command = Commands.Extract(config, job);
    var info = command.CreateStartInfo();
    Check(info.ArgumentList[0] == config.GamePath && info.ArgumentList[2] == job.OutputDirectory && info.ArgumentList[3] == "Test Hero|skin=Evening Dress", "arguments split");
    Check(info.ArgumentList.Contains("--args-reset") && !info.UseShellExecute && info.CreateNoWindow, "unsafe process settings");
    job.SkinSelector = "*"; Throws<InvalidDataException>(() => Commands.Extract(config, job));
}));
await Test("Offline gate prevents process launch before path access", async () => {
    var before = EngineRunner.StartedProcessCount;
    var runner = new EngineRunner(new LiveAccessGate(() => false, () => throw new Exception("must not query processes in offline mode")));
    try { await runner.RunAsync(new("nonexistent-DataTool.exe", []), null, default); throw new Exception("offline execution permitted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("离线"), "wrong gate"); }
    Check(EngineRunner.StartedProcessCount == before, "engine started");
});
await Test("Running-game and unknown-status gates block launch", () => Sync(() => {
    Throws<InvalidOperationException>(() => new LiveAccessGate(() => true, () => true).EnsureAllowed());
    Throws<InvalidOperationException>(() => new LiveAccessGate(() => true, () => throw new IOException("unknown")).EnsureAllowed());
}));
await Test("Actual child process preserves arguments and drains both streams", async () => {
    var command = new EngineCommand(Environment.ProcessPath!, ["--child", "空格 path", "trailing\\", "quoted\"value"]);
    var result = await new EngineRunner(new TestGate()).RunAsync(command, null, default);
    Check(result.ExitCode == 0 && !result.Cancelled, "child failed");
    var arguments = JsonSerializer.Deserialize<string[]>(result.Tail.Single(x => x.StartsWith('[')))!;
    Check(arguments.SequenceEqual(new[] { "空格 path", "trailing\\", "quoted\"value" }), "roundtrip corrupted");
    Check(result.Tail.Contains("stderr-marker"), "stderr was lost");
});
await Test("Cancellation terminates only the owned test child", async () => {
    using var cancellation = new CancellationTokenSource();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var task = new EngineRunner(new TestGate()).RunAsync(new(Environment.ProcessPath!, ["--child", "--wait"]), line => { if (line == "ready") started.TrySetResult(); }, cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
    var result = await task.WaitAsync(TimeSpan.FromSeconds(10)); Check(result.Cancelled, "cancellation missing");
});
await Test("Settings roundtrip never persists permission to read game", async () => {
    var path = Path.Combine(temp, "profile", "settings.json"); var store = new SettingsStore(path);
    await store.SaveAsync(new AppSettings { Language = "enUS", Heroes = [new() { Guid = "001", Name = "测试" }] });
    Check((await store.LoadAsync()).Heroes.Single().Name == "测试", "settings lost");
    Check(!(await File.ReadAllTextAsync(path)).Contains("LiveEnabled"), "permission persisted");
});
await Test("Known engine errors receive Chinese explanations", () => Sync(() => {
    Check(EngineRunner.Explain(new(1, false, ["This version does not support this version of Overwatch"])).Contains("不兼容"), "version explanation missing");
    Check(EngineRunner.Explain(new(1, true, [])).Contains("取消"), "cancellation explanation missing");
}));

var report = new { passed = passed.Count, failed = failures.Count, checks = passed, failures };
if (args.Length == 2 && args[0] == "--report") await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed.Count} passed, {failures.Count} failed. Test artifacts: {temp}");
return failures.Count == 0 ? 0 : 1;

sealed class TestGate : IExecutionGate { public void EnsureAllowed() { } }
