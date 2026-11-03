using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OWExtractor.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string? Option(string key) { var i = Array.IndexOf(e.Args, key); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var profile = Option("--profile") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OWExtractor", "settings.json");
        var render = Option("--render");
        var bindingErrors = new List<string>();
        if (render is not null)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener(bindingErrors));
        }
        try
        {
            var vm = new MainViewModel(profile);
            if (e.Args.Contains("--offline"))
            {
                vm.LockOffline();
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (System.ComponentModel.Win32Exception) { /* Offline lock does not depend on priority. */ }
            }
            await vm.InitializeAsync();
            var forceSoftware = render is not null || e.Args.Contains("--software") || e.Args.Contains("--verify-ui") || e.Args.Contains("--benchmark-preview");
            var renderMode = OWExtractor.Core.PreviewRenderPolicy.Resolve(vm.Settings.PreviewRenderingMode, forceSoftware);
            // Initial startup and all offscreen verification remain software-only. Hardware is
            // enabled only by a saved explicit preference on a normal future application launch.
            RenderOptions.ProcessRenderMode = renderMode == "hardware" ? RenderMode.Default : RenderMode.SoftwareOnly;
            vm.ConfigurePreviewRendering(renderMode, forceSoftware);
            if (Option("--import-heroes") is { } heroes) await vm.ImportHeroesAsync(heroes, false);
            if (Option("--localized-heroes") is { } localized) await vm.ImportHeroesAsync(localized, true);
            if (Option("--import-export") is { } export) await vm.ImportExportsAsync(export);
            if (Option("--preview-front") is { } front) await vm.ImportPreviewAsync(front, false);
            if (Option("--preview-back") is { } back) await vm.ImportPreviewAsync(back, true);
            if (Option("--preview-model") is { } model) await vm.ImportModelAsync(model);
            if (Option("--audio-summary") is { } audio) await vm.ImportAudioSummaryAsync(audio);
            if (e.Args.Contains("--render-performance")) vm.ShowPreviewPerformance = true;
            vm.ActivatePage("models");
            var verifiedWorkflow = false;
            if (e.Args.Contains("--verify-ui"))
            {
                if (vm.SelectedHero is not { Skins.Count: > 0 } hero) throw new InvalidOperationException("离线界面检查需要至少一个既有皮肤。");
                vm.HeroSearch = hero.SelectorName;
                if (vm.Heroes.Count != 1) throw new InvalidOperationException("英雄搜索未正确过滤。");
                vm.SkinSearch = hero.Skins[0].SelectorName;
                if (vm.VisibleSkins.Count == 0) throw new InvalidOperationException("皮肤搜索未正确过滤。");
                await vm.AddJobsAsync([vm.VisibleSkins[0]]);
                if (vm.Jobs.Count == 0 || vm.CanRun) throw new InvalidOperationException("离线队列状态不正确。");
                try { await vm.RunJobsAsync(); throw new InvalidOperationException("离线状态错误地允许提取。"); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("离线模式")) { }
                if (OWExtractor.DataTool.EngineRunner.StartedProcessCount != 0) throw new InvalidOperationException("离线检查启动了引擎。");
                vm.HeroSearch = ""; vm.SkinSearch = ""; vm.SelectedHero = hero;
                verifiedWorkflow = true;
            }
            var window = new MainWindow(vm);
            MainWindow = window;
            if (render is not null)
            {
                await vm.SceneLoadTask;
                // Render WPF content off-screen: no native window, focus change, game read, or DataTool launch.
                var width = int.TryParse(Option("--render-width"), out var w) ? Math.Clamp(w, 1080, 2560) : 1360;
                var height = int.TryParse(Option("--render-height"), out var h) ? Math.Clamp(h, 720, 1600) : 960;
                window.Width = width; window.Height = height;
                vm.CompactLayout = height < 840 || width < 1200;
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                if (verifiedWorkflow) { await window.VerifyOfflineViewAsync(); content.UpdateLayout(); }
                object? benchmark = e.Args.Contains("--benchmark-preview") ? window.MainModelViewport.BenchmarkSoftwareFrames() : null;
                if (Option("--render-page") is { } page) vm.Page = page;
                if (e.Args.Contains("--render-settings")) vm.Page = "settings";
                if (e.Args.Contains("--render-export-options")) vm.ExportOptionsSelected = true;
                if (Option("--render-search") is { } query) { vm.LibrarySearch = query; vm.ApplyLibrarySearch(); }
                if (e.Args.Contains("--render-rear")) vm.RearView = true;
                if (e.Args.Contains("--render-side")) vm.Camera.Orbit(65, 8);
                if (e.Args.Contains("--render-detail")) { vm.Camera.Scale(3.6); vm.Camera.Pan(e.Args.Contains("--render-side") ? -.4 : 0, .52); }
                if (e.Args.Contains("--render-expanded")) vm.PreviewExpanded = true;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                if (e.Args.Contains("--render-interaction"))
                {
                    vm.ShowPreviewPerformance = true;
                    (vm.PreviewExpanded ? window.ExpandedModelViewport : window.MainModelViewport).PrepareInteractiveScreenshot();
                    content.UpdateLayout();
                }
                var renderDpi = int.TryParse(Option("--render-dpi"), out var dpi) ? Math.Clamp(dpi, 96, 192) : 96;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * renderDpi / 96d), (int)Math.Ceiling(height * renderDpi / 96d), renderDpi, renderDpi, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var context = background.RenderOpen()) context.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 246, 247)), null, new Rect(0, 0, width, height));
                bitmap.Render(background);
                bitmap.Render(content);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(render))!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(render)) encoder.Save(file);
                if (bindingErrors.Count > 0) throw new InvalidOperationException("界面数据绑定失败：" + string.Join("; ", bindingErrors));
                await File.WriteAllTextAsync(render + ".json", JsonSerializer.Serialize(new { heroes = vm.Heroes.Count, skins = vm.VisibleSkins.Count, pendingJobs = vm.Jobs.Count, offline = !vm.LiveEnabled, offlineLocked = vm.OfflineLocked, engineStarted = OWExtractor.DataTool.EngineRunner.StartedProcessCount != 0, verifiedWorkflow, hasPreview = vm.HasPreview, has3D = vm.HasModel, modelTriangles = vm.Scene?.Triangles, modelVertices = vm.Scene?.Vertices, cutoutMeshes = vm.Scene?.CutoutMeshes, renderTriangles = vm.Scene?.RenderTriangles, interactionTriangles = vm.Scene?.InteractionTriangles, modelTextures = vm.Scene?.Textures, missingMaterials = vm.Scene?.MissingMaterials, modelMessage = vm.ModelMessage, cameraYaw = vm.Camera.Yaw, cameraZoom = vm.Camera.Zoom, hasBackPreview = vm.HasBackPreview, audioCollections = vm.Settings.AudioSummaries.Count, page = vm.Page, previewWidth = window.PreviewCanvas.ActualWidth, previewHeight = window.PreviewCanvas.ActualHeight, softwareRendering = vm.SoftwareRendering, activeRenderMode = vm.ActiveRenderMode, targetFps = vm.PreviewTargetFps, benchmark, nativeWindowCreated = new WindowInteropHelper(window).Handle != IntPtr.Zero, bindingErrors, width, height, renderDpi, inspector = vm.ExportOptionsSelected ? "export" : "details", previewWarning = vm.ShowPreviewWarning, bodyFontSize = window.FontSize }));
                Shutdown(0); return;
            }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, SystemParameters.WorkArea.Width));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, SystemParameters.WorkArea.Height));
            window.ShowActivated = !e.Args.Contains("--no-activate");
            window.Show();
        }
        catch (Exception ex)
        {
            if (render is not null) { await File.WriteAllTextAsync(render + ".error.txt", ex.ToString()); Shutdown(1); }
            else { MessageBox.Show("启动失败：" + ex.Message, "OWExtractor"); Shutdown(1); }
        }
    }
}

internal sealed class BindingErrorListener(List<string> errors) : TraceListener
{
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
}
