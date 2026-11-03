using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using OWExtractor.Core;

namespace OWExtractor.App;

public sealed partial class ModelViewport
{
    public static readonly DependencyProperty SoftwareRenderingProperty = DependencyProperty.Register(nameof(SoftwareRendering), typeof(bool), typeof(ModelViewport), new PropertyMetadata(true, PerformanceChanged));
    public static readonly DependencyProperty TargetFpsProperty = DependencyProperty.Register(nameof(TargetFps), typeof(int), typeof(ModelViewport), new PropertyMetadata(30, PerformanceChanged));
    public static readonly DependencyProperty ShowPerformanceProperty = DependencyProperty.Register(nameof(ShowPerformance), typeof(bool), typeof(ModelViewport), new PropertyMetadata(false, PerformanceChanged));
    public bool SoftwareRendering { get => (bool)GetValue(SoftwareRenderingProperty); set => SetValue(SoftwareRenderingProperty, value); }
    public int TargetFps { get => (int)GetValue(TargetFpsProperty); set => SetValue(TargetFpsProperty, value); }
    public bool ShowPerformance { get => (bool)GetValue(ShowPerformanceProperty); set => SetValue(ShowPerformanceProperty, value); }
    private readonly PreviewInputBuffer pendingInput = new();
    private readonly PreviewFramePacer framePacer = new();
    private readonly Stopwatch interactionClock = Stopwatch.StartNew();
    private readonly Viewport3D rasterViewport = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly ModelVisual3D rasterVisual = new();
    private readonly Image interactionImage = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false, Stretch = Stretch.Fill };
    private readonly TextBlock performanceText = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(70, 87, 78)), TextAlignment = TextAlignment.Right };
    private Border performanceBadge = null!;
    private readonly DispatcherTimer settleTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
    private RenderTargetBitmap? interactionBitmap;
    private Window? owner;
    private bool frameSubscribed, dragging, submittingInput;
    private double lastSampleTime;
    private readonly Queue<(double time, double cost)> samples = new();
    internal int SubmittedFrames { get; private set; }
    internal bool LowResolutionActive => interactionImage.Visibility == Visibility.Visible;
    internal bool FrameScheduled => frameSubscribed;
    private bool CanScheduleFrames => IsVisible && IsEnabled && Scene is not null && owner?.WindowState != WindowState.Minimized;

    private void InitializePerformance()
    {
        rasterViewport.Camera = camera; rasterViewport.Children.Add(rasterVisual);
        RenderOptions.SetBitmapScalingMode(interactionImage, BitmapScalingMode.LowQuality);
        Children.Add(interactionImage);
        performanceBadge = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), CornerRadius = new(5), Padding = new(8, 5, 8, 5), Margin = new(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = performanceText };
        Children.Add(performanceBadge);
        ToolTipService.SetToolTip(performanceBadge, "更新速率为交互帧提交频率；软件绘制耗时包含离屏绘制。硬件模式不报告 GPU 耗时。");
        settleTimer.Tick += (_, _) => { settleTimer.Stop(); if (!dragging) FinishInteraction(); };
        Loaded += (_, _) => { owner = Window.GetWindow(this); if (owner is not null) owner.StateChanged += OwnerStateChanged; UpdateCamera(); UpdatePerformanceText(); };
        Unloaded += (_, _) => { SuspendInteraction(); if (owner is not null) owner.StateChanged -= OwnerStateChanged; owner = null; };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) SuspendInteraction(); };
    }
    private void OwnerStateChanged(object? sender, EventArgs e) { if (owner?.WindowState == WindowState.Minimized) SuspendInteraction(); else UpdateCamera(); }
    private static void PerformanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ModelViewport)d;
        if (view.performanceBadge is null) return;
        if (e.Property == SoftwareRenderingProperty) view.SuspendInteraction();
        if (e.Property == TargetFpsProperty) view.framePacer.Reset();
        view.UpdatePerformanceText();
    }
    private void RequestFrame()
    {
        if (!pendingInput.HasPending || frameSubscribed || !CanScheduleFrames) return;
        CompositionTarget.Rendering += OnCompositionFrame; frameSubscribed = true;
    }
    private void StopFrames()
    {
        if (frameSubscribed) CompositionTarget.Rendering -= OnCompositionFrame;
        frameSubscribed = false;
    }
    private void OnOrbitChanged()
    {
        // Toolbar/keyboard resets supersede queued motion and stale interaction snapshots.
        if (!submittingInput)
        {
            pendingInput.Clear(); StopFrames(); settleTimer.Stop();
            interactionImage.Visibility = Visibility.Collapsed; interactionImage.Source = null; viewport.Visibility = Visibility.Visible;
            UpdatePerformanceText();
        }
        UpdateCamera();
    }
    private void OnCompositionFrame(object? sender, EventArgs e)
    {
        if (!CanScheduleFrames) { SuspendInteraction(); return; }
        if (!pendingInput.HasPending) { StopFrames(); return; }
        if (!framePacer.IsDue(interactionClock.Elapsed.TotalMilliseconds, TargetFps)) return;
        SubmitPendingFrame();
        if (!pendingInput.HasPending) StopFrames();
    }
    internal void SubmitPendingFrame()
    {
        if (!pendingInput.HasPending || Scene is null || Orbit is null) return;
        var started = Stopwatch.GetTimestamp();
        if (SoftwareRendering) viewport.Visibility = Visibility.Hidden;
        submittingInput = true;
        try { Orbit.Apply(pendingInput.Take()); } finally { submittingInput = false; }
        if (SoftwareRendering)
        {
            interactionBitmap = DrawSoftwareFrame(.5, interactionBitmap);
            interactionImage.Source = interactionBitmap; interactionImage.Visibility = Visibility.Visible;
        }
        SubmittedFrames++;
        var now = interactionClock.Elapsed.TotalMilliseconds;
        if (now - lastSampleTime > 350) samples.Clear();
        samples.Enqueue((now, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        while (samples.Count > 30) samples.Dequeue();
        lastSampleTime = now; UpdatePerformanceText();
    }
    private RenderTargetBitmap DrawSoftwareFrame(double resolution, RenderTargetBitmap? reuse = null)
    {
        rasterVisual.Content = resolution < 1 ? Scene?.InteractionModel ?? Scene?.Model : Scene?.Model;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var factor = resolution * dpi;
        if (resolution < 1) factor = Math.Min(factor, 768 / Math.Max(viewportSize.Width, viewportSize.Height));
        var width = Math.Max(1, (int)Math.Ceiling(viewportSize.Width * factor));
        var height = Math.Max(1, (int)Math.Ceiling(viewportSize.Height * factor));
        rasterViewport.Measure(new(width, height)); rasterViewport.Arrange(new(0, 0, width, height)); rasterViewport.UpdateLayout();
        var bitmap = reuse is not null && reuse.PixelWidth == width && reuse.PixelHeight == height ? reuse : new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Clear(); bitmap.Render(rasterViewport); return bitmap;
    }
    internal void FinishInteraction()
    {
        StopFrames(); settleTimer.Stop(); dragging = false;
        submittingInput = true;
        try { if (pendingInput.HasPending && Scene is not null) Orbit?.Apply(pendingInput.Take()); else pendingInput.Clear(); }
        finally { submittingInput = false; }
        interactionImage.Visibility = Visibility.Collapsed; interactionImage.Source = null; viewport.Visibility = Visibility.Visible;
        UpdatePerformanceText();
    }
    private void SuspendInteraction()
    {
        pendingInput.Clear(); FinishInteraction(); framePacer.Reset(); interactionBitmap = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    private void UpdatePerformanceText()
    {
        if (performanceBadge is null) return;
        performanceBadge.Visibility = ShowPerformance ? Visibility.Visible : Visibility.Collapsed;
        if (!ShowPerformance) return;
        var mode = SoftwareRendering ? "省资源" : "硬件优先";
        var speed = samples.Count > 1 ? (samples.Count - 1) * 1000 / Math.Max(1, samples.Last().time - samples.First().time) : 0;
        var cost = samples.Count > 0 ? samples.Average(x => x.cost) : 0;
        performanceText.Text = $"{mode} · 目标 {TargetFps} FPS\n" + (samples.Count > 1 ? $"更新 {speed:N1}/s · " : "")
            + (samples.Count > 0 && SoftwareRendering ? $"绘制 {cost:N1} ms\n" : "")
            + (LowResolutionActive ? "轻量交互 · 松手恢复清晰" : "完整清晰度 · 等待交互");
    }

    internal void VerifyFramePipeline()
    {
        if (Scene is null || Orbit is null) return;
        Orbit.Reset(); var before = SubmittedFrames;
        lastPoint = new(10, 10); panning = false; dragging = true;
        QueueDrag(new(30, 20)); QueueDrag(new(50, 35));
        if (Orbit.Yaw != 0) throw new InvalidOperationException("鼠标输入没有合并。");
        SubmitPendingFrame();
        if (SubmittedFrames != before + 1 || Orbit.Yaw != -16 || Orbit.Pitch != 10 || !LowResolutionActive || interactionBitmap is null)
            throw new InvalidOperationException("交互帧没有应用最新输入或低分辨率图像。");
        var pixels = new byte[interactionBitmap.PixelWidth * interactionBitmap.PixelHeight * 4];
        interactionBitmap.CopyPixels(pixels, interactionBitmap.PixelWidth * 4, 0);
        if (!Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 0)) throw new InvalidOperationException("交互预览没有绘制模型。");
        SubmitPendingFrame(); if (SubmittedFrames != before + 1) throw new InvalidOperationException("空闲时仍在绘制。");
        QueueDrag(new(60, 40)); FinishInteraction();
        if (Orbit.Yaw != -20 || LowResolutionActive || FrameScheduled || pendingInput.HasPending || settleTimer.IsEnabled)
            throw new InvalidOperationException("松手未提交最终位移并恢复完整预览。");
        pendingInput.Orbit(100, 0); SuspendInteraction();
        if (pendingInput.HasPending || FrameScheduled || LowResolutionActive) throw new InvalidOperationException("隐藏预览时仍有后台交互任务。");
        Orbit.Reset();
        pendingInput.Zoom(10); Orbit.Reset(); FinishInteraction();
        if (Orbit.Zoom != 1 || pendingInput.HasPending) throw new InvalidOperationException("复位后又应用了过期输入。");
    }
    internal void PrepareInteractiveScreenshot()
    {
        lastPoint = new(0, 0); panning = false; dragging = true;
        QueueDrag(new(-80, 12)); SubmitPendingFrame();
    }

    internal object BenchmarkSoftwareFrames()
    {
        if (!SoftwareRendering || Scene is null || Orbit is null) throw new InvalidOperationException("此检查只用于离屏软件预览。");
        FinishInteraction(); List<double> full = [], reduced = [];
        RenderTargetBitmap? fullBitmap = null, reducedBitmap = null;
        for (var i = 0; i < 10; i++)
        {
            Orbit.Face(false); Orbit.Orbit(i * 5, 0);
            var start = Stopwatch.GetTimestamp(); fullBitmap = DrawSoftwareFrame(1, fullBitmap);
            var fullTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp(); reducedBitmap = DrawSoftwareFrame(.5, reducedBitmap);
            var reducedTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (i > 1) { full.Add(fullTime); reduced.Add(reducedTime); }
        }
        Orbit.Reset();
        double Median(List<double> x) { var a = x.Order().ToArray(); return (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }
        return new { kind = "offscreen-software-draw-only", samples = full.Count, fullMedianMs = Median(full), interactiveMedianMs = Median(reduced), fullWidth = fullBitmap!.PixelWidth, fullHeight = fullBitmap.PixelHeight, interactiveWidth = reducedBitmap!.PixelWidth, interactiveHeight = reducedBitmap.PixelHeight, fullTimesMs = full, interactiveTimesMs = reduced, gpuTested = false };
    }
}
