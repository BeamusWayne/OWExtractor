using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using OWExtractor.Core;

namespace OWExtractor.App;

// Camera state is shared by inline and expanded previews; queued input emits one change per frame.
public sealed class PreviewCamera
{
    public event Action? Changed;
    public double Yaw { get; private set; }
    public double Pitch { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public void Reset() { Yaw = Pitch = PanX = PanY = 0; Zoom = 1; Changed?.Invoke(); }
    public void Fit() { PanX = PanY = 0; Zoom = 1; Changed?.Invoke(); }
    public void Face(bool rear) { ResetValues(); Yaw = rear ? 180 : 0; Changed?.Invoke(); }
    private void ResetValues() { Pitch = PanX = PanY = 0; Zoom = 1; }
    public void Orbit(double x, double y) { Yaw = (Yaw + x) % 360; Pitch = Math.Clamp(Pitch + y, -85, 85); Changed?.Invoke(); }
    public void Scale(double factor) { if (!double.IsFinite(factor) || factor <= 0) return; Zoom = Math.Clamp(Zoom * factor, .25, 5); Changed?.Invoke(); }
    public void Pan(double x, double y) { PanX = Math.Clamp(PanX + x, -3, 3); PanY = Math.Clamp(PanY + y, -3, 3); Changed?.Invoke(); }
    public void Apply(PreviewInput input)
    {
        Yaw = (Yaw + input.OrbitX) % 360; Pitch = Math.Clamp(Pitch + input.OrbitY, -85, 85);
        PanX = Math.Clamp(PanX + input.PanX, -3, 3); PanY = Math.Clamp(PanY + input.PanY, -3, 3);
        Zoom = Math.Clamp(Zoom * Math.Pow(1.15, input.ZoomSteps), .25, 5); Changed?.Invoke();
    }
}

public sealed partial class ModelViewport : Grid
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(OfflineScene), typeof(ModelViewport), new PropertyMetadata(null, SceneChanged));
    public static readonly DependencyProperty OrbitProperty = DependencyProperty.Register(nameof(Orbit), typeof(PreviewCamera), typeof(ModelViewport), new PropertyMetadata(null, OrbitChanged));
    public OfflineScene? Scene { get => (OfflineScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public PreviewCamera? Orbit { get => (PreviewCamera?)GetValue(OrbitProperty); set => SetValue(OrbitProperty, value); }
    private readonly Viewport3D viewport = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly ModelVisual3D visual = new();
    private readonly PerspectiveCamera camera = new() { FieldOfView = 32 };
    private Size viewportSize = new(640, 480);
    private Point lastPoint;
    private bool panning;
    public ModelViewport()
    {
        Background = Brushes.Transparent; Focusable = true; ClipToBounds = true;
        SetResourceReference(FocusVisualStyleProperty, "KeyboardFocus");
        Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(this, "三维模型：拖动旋转，滚轮缩放，右键平移；方向键旋转，Home 复位");
        viewport.Camera = camera; viewport.Children.Add(visual); Children.Add(viewport);
        InitializePerformance();
        SizeChanged += (_, _) => UpdateCamera();
        // The inline viewport is collapsed by binding while the expanded viewport is visible.
        IsVisibleChanged += (_, _) => { if (!IsVisible) SuspendInteraction(); else UpdateCamera(); };
    }
    private static void SceneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    { var view = (ModelViewport)d; view.SuspendInteraction(); view.visual.Content = view.Scene?.Model; view.rasterVisual.Content = view.Scene?.Model; view.UpdateCamera(); }
    private static void OrbitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ModelViewport)d;
        if (e.OldValue is PreviewCamera old) old.Changed -= view.OnOrbitChanged;
        if (e.NewValue is PreviewCamera next) next.Changed += view.OnOrbitChanged;
        view.UpdateCamera();
    }
    private void UpdateCamera()
    {
        if (Scene is null || Orbit is null || (IsLoaded && !IsVisible)) return;
        var b = Scene.Bounds;
        var radius = Math.Max(.01, Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ) / 2);
        var yaw = Orbit.Yaw * Math.PI / 180; var pitch = Orbit.Pitch * Math.PI / 180;
        var offset = new Vector3D(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch));
        var right = new Vector3D(Math.Cos(yaw), 0, -Math.Sin(yaw));
        var up = Vector3D.CrossProduct(offset, right);
        var center = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2) + (right * Orbit.PanX + up * Orbit.PanY) * radius;
        var aspect = Math.Max(.1, viewportSize.Width / Math.Max(1, viewportSize.Height));
        // WPF FOV is horizontal; use the narrower axis to keep the whole model in frame.
        var horizontalTan = Math.Tan(camera.FieldOfView * Math.PI / 360);
        var distance = Math.Max(.01, (Math.Max(b.SizeX / (2 * horizontalTan), b.SizeY * aspect / (2 * horizontalTan)) + b.SizeZ / 2) * 1.1) / Orbit.Zoom;
        camera.Position = center + offset * distance; camera.LookDirection = -offset * distance; camera.UpDirection = up;
        camera.NearPlaneDistance = radius * .005; camera.FarPlaneDistance = radius * 100;
    }
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        if (arrangeSize.Width > 0 && arrangeSize.Height > 0) viewportSize = arrangeSize;
        UpdateCamera(); return result;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); if (Scene is null || Orbit is null) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
        Focus();
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) { Orbit.Reset(); e.Handled = true; return; }
        panning = e.ChangedButton != MouseButton.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        lastPoint = e.GetPosition(this); dragging = true; CaptureMouse(); Cursor = Cursors.SizeAll; e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) { QueueDrag(e.GetPosition(this)); e.Handled = true; }
    }
    private void QueueDrag(Point point)
    {
        var delta = point - lastPoint; lastPoint = point;
        if (panning) pendingInput.Pan(-delta.X / Math.Max(1, viewportSize.Height) * 3, delta.Y / Math.Max(1, viewportSize.Height) * 3);
        else pendingInput.Orbit(-delta.X * .4, delta.Y * .4);
        RequestFrame();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    { base.OnMouseUp(e); if (IsMouseCaptured) { QueueDrag(e.GetPosition(this)); FinishInteraction(); ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); FinishInteraction(); Cursor = Cursors.Hand; }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e); if (Scene is null) return;
        pendingInput.Zoom(e.Delta / 120d); RequestFrame();
        if (CanScheduleFrames) { settleTimer.Stop(); settleTimer.Start(); }
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (Scene is null || Orbit is null) return;
        switch (e.Key)
        {
            case Key.Left: Orbit.Orbit(-10, 0); break; case Key.Right: Orbit.Orbit(10, 0); break;
            case Key.Up: Orbit.Orbit(0, -10); break; case Key.Down: Orbit.Orbit(0, 10); break;
            case Key.Home: Orbit.Reset(); break;
            case Key.F: Orbit.Fit(); break;
            case Key.Add: case Key.OemPlus: Orbit.Scale(1.15); break;
            case Key.Subtract: case Key.OemMinus: Orbit.Scale(1 / 1.15); break;
            default: return;
        }
        e.Handled = true;
    }
}
