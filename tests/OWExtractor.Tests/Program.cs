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
    Check(heroes[0].Skins[0].ExtensionCounts.GetValueOrDefault(".png") == 1, "texture inventory missing");
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
await Test("Resource choices survive queue roundtrip and exclude unselected exports", () => Sync(() => {
    var oldJob = JsonSerializer.Deserialize<ExportJob>("""{"HeroSelector":"Test Hero","SkinSelector":"Dress"}""")!;
    Check(oldJob.IncludeTextures && oldJob.IncludeAnimations && oldJob.IncludeSound, "old queue changed meaning");
    var job = new ExportJob { HeroSelector = "Test Hero", SkinSelector = "Dress", OutputDirectory = "synthetic-output", IncludeTextures = false, IncludeAnimations = false, IncludeSound = false };
    var restored = JsonSerializer.Deserialize<ExportJob>(JsonSerializer.Serialize(job))!;
    var args = Commands.Extract(new AppSettings(), restored).Arguments;
    Check(args.Contains("--skip-textures") && args.Contains("--skip-animations") && args.Contains("--skip-animation-effects") && args.Contains("--skip-sound"), "optional resources ignored");
    Check(!args.Contains("--skip-models"), "required models removed");
    Check(!Commands.Extract(new AppSettings(), new ExportJob { HeroSelector = "Test Hero", SkinSelector = "Dress", OutputDirectory = "synthetic-output" }).Arguments.Any(x => x.StartsWith("--skip-")), "complete export changed");
}));
await Test("Historical audio metadata keeps locales separate and rejects unrelated JSON", () => Sync(() => {
    var summaries = CachedAudioSummary.Parse("""{"resource_build":"historical","stats":{"zhCN":{"sections":{"英雄语音":10,"英雄对话":2}},"enUS":{"sections":{"英雄语音":9}},"SFX":{"sections":{"关联音效":3}}}}""");
    Check(summaries.Single(x => x.Language == "zhCN").Total == 12 && summaries.Single(x => x.Language == "enUS").Total == 9 && summaries.Single(x => x.Language == "SFX").SoundEffects == 3, "locale inventories mixed");
    Throws<InvalidDataException>(() => CachedAudioSummary.Parse("{}"));
    Throws<InvalidDataException>(() => CachedAudioSummary.Parse("""{"stats":{"zhCN":{"sections":{"英雄语音":-1}}}}"""));
}));
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

await Test("OWMDL reads actual indexed geometry and skips bone payloads", () => Sync(() => {
    using var model = FixtureModel();
    var data = OfflineModelReader.Read(model);
    Check(data.VertexCount == 3 && data.TriangleCount == 1 && data.Meshes[0].Positions[2].Z == 1, "geometry corrupted");
    Check(data.Meshes[0].UV[2].Y == 1 && data.Meshes[0].Normals[0].Y == -1, "UV or normal arrays misaligned");
}));
await Test("OWMDL rejects truncated, invalid index, huge allocation and nonfinite coordinates", () => Sync(() => {
    using var source = FixtureModel(); var bytes = source.ToArray();
    Throws<EndOfStreamException>(() => OfflineModelReader.Read(new MemoryStream(bytes[..^1])));
    var badIndex = bytes.ToArray(); BitConverter.GetBytes(3u).CopyTo(badIndex, bytes.Length - 4);
    Throws<InvalidDataException>(() => OfflineModelReader.Read(new MemoryStream(badIndex)));
    using var enormous = FixtureModel(200001); Throws<InvalidDataException>(() => OfflineModelReader.Read(enormous));
    using var invalid = FixtureModel(badCoordinate: true); Throws<InvalidDataException>(() => OfflineModelReader.Read(invalid));
    using var cancelled = FixtureModel(); Throws<OperationCanceledException>(() => OfflineModelReader.Read(cancelled, new CancellationToken(true)));
}));
await Test("3D references reject game, network and directory escape before IO", () => Sync(() => {
    var root = Path.Combine(temp, "exports");
    Throws<InvalidDataException>(() => ExportFileAccess.Validate(@"D:\Overwatch\Data\no-read.owmdl", @"D:\Overwatch", "", ".owmdl", 100));
    Throws<InvalidDataException>(() => ExportFileAccess.Validate(@"D:\custom-game\no-read.owmdl", @"D:\custom-game", @"D:\custom-game", ".owmdl", 100));
    Throws<InvalidDataException>(() => ExportFileAccess.Validate(@"\\unavailable\share\no-read.owmdl", root, "", ".owmdl", 100));
    Throws<InvalidDataException>(() => ExportFileAccess.Validate(Path.Combine(root, "..", "outside.png"), root, "", ".png", 100));
}));

await Test("Secondary hair UV survives parsing and controls opacity independently of RGB UV", () => Sync(() => {
    using var fixture = FixtureModel(secondaryUV: true);
    var parsed = OfflineModelReader.Read(fixture).Meshes[0];
    Check(parsed.SecondaryUV is { Length: 3 } && parsed.SecondaryUV[2].X == .75f, "secondary UV lost or payload misaligned");
    var mesh = parsed with { UV = Enumerable.Repeat(new System.Numerics.Vector2(.25f, .5f), 3).ToArray() };
    var mask = new PreviewOpacityMask(2, 1, [0, 255]);
    var correct = PreviewCutout.Clip(mesh, mask, true);
    var wrong = PreviewCutout.Clip(mesh, mask, false);
    Check(correct.Indices.Length == 3 && correct.Positions.SequenceEqual(mesh.Positions), "opaque hair disappeared");
    Check(wrong.Indices.Length == 0, "fixture does not distinguish opacity channels");
    Check(correct.UV.All(v => v.X == .25f), "opacity changed color UV");
}));
await Test("Opacity clipping retains valid geometry and does not mutate exported meshes", () => Sync(() => {
    using var fixture = FixtureModel(); var source = OfflineModelReader.Read(fixture).Meshes[0];
    var mesh = source with { UV = [new(.25f, .4f), new(.75f, .4f), new(.75f, .8f)] };
    var result = PreviewCutout.Clip(mesh, new(2, 1, [0, 255]), false);
    Check(result.Indices.Length > 0 && result.Indices.All(i => i < result.Positions.Length), "invalid clipped mesh");
    Check(result.UV.All(v => v.X >= .49f), "transparent half survived");
    Check(mesh.Positions.SequenceEqual(source.Positions) && mesh.Indices.Length == 3, "source was modified");
    Throws<InvalidDataException>(() => PreviewCutout.Clip(source, new(1, 1, [255]), true));
    Throws<OperationCanceledException>(() => PreviewCutout.Clip(source, new(1, 1, [255]), false, new CancellationToken(true)));
}));

await Test("Render preference defaults safely and offscreen override wins over hardware", async () => {
    Check(PreviewRenderPolicy.Resolve(null, false) == "software" && PreviewRenderPolicy.Resolve("unexpected", false) == "software", "unsafe default");
    Check(PreviewRenderPolicy.Resolve("hardware", true) == "software" && PreviewRenderPolicy.Resolve("hardware", false) == "hardware", "override or hardware preference lost");
    var path = Path.Combine(temp, "preview-settings.json"); var store = new SettingsStore(path);
    await store.SaveAsync(new() { PreviewRenderingMode = "hardware", PreviewTargetFps = 60, ShowPreviewPerformance = true });
    var settings = await store.LoadAsync();
    Check(settings.PreviewRenderingMode == "hardware" && settings.PreviewTargetFps == 60 && settings.ShowPreviewPerformance, "preview settings did not persist");
    Check(PreviewRenderPolicy.FrameRate(999) == 30, "invalid FPS was accepted");
});
await Test("Preview input coalesces bursts into one frame without replaying stale movement", () => Sync(() => {
    var input = new PreviewInputBuffer();
    for (var i = 0; i < 1000; i++) input.Orbit(.5, .25);
    input.Pan(.1, -.2); input.Zoom(2); input.Zoom(-1);
    var frame = input.Take();
    Check(frame.OrbitX == 500 && frame.OrbitY == 250 && frame.PanX == .1 && frame.ZoomSteps == 1, "input was dropped");
    Check(!input.HasPending && input.Take() == default, "stale movement was replayed");
    input.Orbit(10, 10); input.Clear(); Check(!input.HasPending, "hidden preview kept pending movement");
}));
await Test("Frame pacing supports 30 and 60 FPS and skips missed frames", () => Sync(() => {
    var fast = new PreviewFramePacer(); var slow = new PreviewFramePacer(); var fastFrames = 0; var slowFrames = 0;
    for (var i = 0; i < 60; i++) { if (fast.IsDue(i * 1000d / 60, 60)) fastFrames++; if (slow.IsDue(i * 1000d / 60, 30)) slowFrames++; }
    Check(fastFrames == 60 && slowFrames == 30, "frame pacing silently halves the target rate");
    Check(fast.IsDue(5000, 60) && !fast.IsDue(5000, 60), "missed frames were replayed");
    fast.Reset(); Check(fast.IsDue(0, 60), "new gesture was delayed");
}));

await Test("Mesh compaction preserves UV and normal seams while sharing identical vertices", () => Sync(() => {
    var mesh = new OfflineMesh("seams", 1, [new(0,0,0), new(1,0,0), new(0,1,0), new(0,0,0), new(0,0,0)],
        [new(0,0,1),new(0,0,1),new(0,0,1),new(0,0,1),new(0,0,1)], [new(0,0),new(1,0),new(0,1),new(0,0),new(1,1)], [0,1,2,3,1,2,4,1,2]);
    var indexed = PreviewMeshIndexing.Compact(mesh);
    Check(indexed.Positions.Length == 4 && indexed.Indices.Length == mesh.Indices.Length, "incorrect weld count");
    for (var i = 0; i < mesh.Indices.Length; i++)
    { var old = mesh.Indices[i]; var next = indexed.Indices[i]; Check(mesh.Positions[old] == indexed.Positions[next] && mesh.Normals[old] == indexed.Normals[next] && mesh.UV[old] == indexed.UV[next], "render attributes changed"); }
}));
await Test("Interaction LOD preserves small disconnected components and leaves full mesh intact", () => Sync(() => {
    const int n = 80;
    List<System.Numerics.Vector3> positions = [], normals = []; List<System.Numerics.Vector2> uv = []; List<int> indices = [];
    foreach (var size in new[] { 1f, .01f })
    {
        var start = positions.Count; var offset = start == 0 ? 0 : 2;
        for (var y = 0; y <= n; y++) for (var x = 0; x <= n; x++) { positions.Add(new(offset + x * size / n, y * size / n, 0)); normals.Add(new(0,0,1)); uv.Add(new((float)x / n, (float)y / n)); }
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) { var a = start + y * (n + 1) + x; indices.AddRange([a,a+1,a+n+2,a,a+n+2,a+n+1]); }
    }
    var source = new OfflineMesh("components", 1, positions.ToArray(), normals.ToArray(), uv.ToArray(), indices.ToArray());
    var lod = PreviewMeshIndexing.InteractionLod(source);
    Check(lod.Indices.Length < source.Indices.Length && lod.Indices.All(i => i < lod.Positions.Length), "LOD did not reduce valid geometry");
    Check(lod.Positions.Count(p => p.X > 1.5f) > 100, "small head-sized component collapsed");
    Check(source.Positions.Length == 2 * (n + 1) * (n + 1) && source.Indices.Length == 12 * n * n, "full mesh mutated");
}));

MemoryStream FixtureModel(uint vertices = 3, bool badCoordinate = false, bool secondaryUV = false)
{
    var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
    writer.Write((ushort)2); writer.Write((ushort)0); writer.Write(""); writer.Write("synthetic"); writer.Write(0u);
    writer.Write((ushort)1); writer.Write(1u); writer.Write(0u);
    writer.Write("bone"); writer.Write((short)-1); for (var i = 0; i < 9; i++) writer.Write(0f);
    writer.Write("triangle"); writer.Write(1ul); writer.Write((byte)(secondaryUV ? 2 : 1)); writer.Write(vertices); writer.Write(1u); writer.Write((sbyte)1);
    foreach (var n in new float[] { badCoordinate ? float.NaN : -.5f, 0, 0, .5f, 0, 0, 0, 0, 1 }) writer.Write(n);
    for (var i = 0; i < 3; i++) { writer.Write(0f); writer.Write(-1f); writer.Write(0f); }
    for (var i = 0; i < 12; i++) writer.Write(0f);
    foreach (var n in new float[] { 0, 0, 1, 0, .5f, 1 }) writer.Write(n);
    if (secondaryUV) for (var i = 0; i < 3; i++) { writer.Write(.75f); writer.Write(.5f); }
    for (var i = 0; i < 3; i++) writer.Write((short)0);
    for (var i = 0; i < 3; i++) writer.Write(1f);
    for (var i = 0; i < 24; i++) writer.Write(1f);
    writer.Write(0u); writer.Write(1u); writer.Write(2u); writer.Flush(); stream.Position = 0; return stream;
}

var report = new { passed = passed.Count, failed = failures.Count, checks = passed, failures };
if (args.Length == 2 && args[0] == "--report") await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed.Count} passed, {failures.Count} failed. Test artifacts: {temp}");
return failures.Count == 0 ? 0 : 1;

sealed class TestGate : IExecutionGate { public void EnsureAllowed() { } }
