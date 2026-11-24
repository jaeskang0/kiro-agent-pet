namespace KiroPet;

public sealed record DisplayArea(int Left, int Top, int Width, int Height, double Scale, bool Primary = false);
public sealed record DesktopPoint(int X, int Y);

public static class PetPosition
{
    public static DesktopPoint Restore(double? left, double? top, double width, double height, IReadOnlyList<DisplayArea> displays)
    {
        if (left.HasValue && top.HasValue)
        {
            foreach (var display in displays)
            {
                double x = left.Value * display.Scale, y = top.Value * display.Scale;
                if (x < display.Left || x >= display.Left + display.Width ||
                    y < display.Top || y >= display.Top + display.Height || !double.IsFinite(x) || !double.IsFinite(y)) continue;
                return new DesktopPoint(
                    Math.Clamp((int)x, display.Left, Math.Max(display.Left, display.Left + display.Width - (int)Math.Ceiling(width * display.Scale))),
                    Math.Clamp((int)y, display.Top, Math.Max(display.Top, display.Top + display.Height - (int)Math.Ceiling(height * display.Scale))));
            }
        }
        var fallback = displays.FirstOrDefault(display => display.Primary) ?? displays.FirstOrDefault() ??
            new DisplayArea(0, 0, 1920, 1080, 1, true);
        return new DesktopPoint(
            Math.Max(fallback.Left, fallback.Left + fallback.Width - (int)Math.Ceiling(width * fallback.Scale) - 24),
            Math.Max(fallback.Top, fallback.Top + fallback.Height - (int)Math.Ceiling(height * fallback.Scale) - 24));
    }
}
