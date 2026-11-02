using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OWExtractor.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string? Option(string key) { var i = Array.IndexOf(e.Args, key); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var profile = Option("--profile") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OWExtractor", "settings.json");
        var render = Option("--render");
        try
        {
            var vm = new MainViewModel(profile);
            await vm.InitializeAsync();
            if (Option("--import-heroes") is { } heroes) await vm.ImportHeroesAsync(heroes, false);
            if (Option("--localized-heroes") is { } localized) await vm.ImportHeroesAsync(localized, true);
            if (Option("--import-export") is { } export) await vm.ImportExportsAsync(export);
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
                // Render WPF content off-screen: no native window, focus change, game read, or DataTool launch.
                var width = int.TryParse(Option("--render-width"), out var w) ? Math.Clamp(w, 1080, 2560) : 1360;
                var height = int.TryParse(Option("--render-height"), out var h) ? Math.Clamp(h, 720, 1600) : 960;
                window.Width = width; window.Height = height;
                vm.CompactLayout = height < 840 || width < 1200;
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                if (verifiedWorkflow) { window.VerifyOfflineView(); content.UpdateLayout(); }
                if (e.Args.Contains("--render-settings")) { window.SettingsExpander.IsExpanded = true; content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); }
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var context = background.RenderOpen()) context.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 246, 247)), null, new Rect(0, 0, width, height));
                bitmap.Render(background);
                bitmap.Render(content);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(render))!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(render)) encoder.Save(file);
                await File.WriteAllTextAsync(render + ".json", JsonSerializer.Serialize(new { heroes = vm.Heroes.Count, skins = vm.VisibleSkins.Count, pendingJobs = vm.Jobs.Count, offline = !vm.LiveEnabled, engineStarted = OWExtractor.DataTool.EngineRunner.StartedProcessCount != 0, verifiedWorkflow, width, height }));
                Shutdown(0); return;
            }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, SystemParameters.WorkArea.Width));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, SystemParameters.WorkArea.Height));
            window.Show();
        }
        catch (Exception ex)
        {
            if (render is not null) { await File.WriteAllTextAsync(render + ".error.txt", ex.ToString()); Shutdown(1); }
            else { MessageBox.Show("启动失败：" + ex.Message, "OWExtractor"); Shutdown(1); }
        }
    }
}
