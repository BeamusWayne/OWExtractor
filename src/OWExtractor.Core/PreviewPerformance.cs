namespace OWExtractor.Core;

public static class PreviewRenderPolicy
{
    public static string Normalize(string? mode) => mode == "hardware" ? "hardware" : "software";
    public static string Resolve(string? requested, bool forceSoftware) => forceSoftware ? "software" : Normalize(requested);
    public static int FrameRate(int requested) => requested == 60 ? 60 : 30;
}

public readonly record struct PreviewInput(double OrbitX, double OrbitY, double PanX, double PanY, double ZoomSteps);

// Aggregate pending input, never queue historical frames. Taking a frame clears the buffer.
public sealed class PreviewInputBuffer
{
    private PreviewInput pending;
    public bool HasPending => pending != default;
    public void Orbit(double x, double y) => pending = pending with { OrbitX = pending.OrbitX + x, OrbitY = pending.OrbitY + y };
    public void Pan(double x, double y) => pending = pending with { PanX = pending.PanX + x, PanY = pending.PanY + y };
    public void Zoom(double steps) => pending = pending with { ZoomSteps = Math.Clamp(pending.ZoomSteps + steps, -100, 100) };
    public PreviewInput Take() { var result = pending; pending = default; return result; }
    public void Clear() => pending = default;
}

public sealed class PreviewFramePacer
{
    private double? nextMilliseconds;
    public bool IsDue(double nowMilliseconds, int frameRate)
    {
        var interval = 1000d / PreviewRenderPolicy.FrameRate(frameRate);
        if (nextMilliseconds is { } next && nowMilliseconds + .5 < next) return false;
        // Tolerate fractional vsync intervals; skip missed frames instead of catching up.
        nextMilliseconds = nextMilliseconds is { } prior && nowMilliseconds - prior < interval
            ? prior + interval : nowMilliseconds + interval;
        return true;
    }
    public void Reset() => nextMilliseconds = null;
}
