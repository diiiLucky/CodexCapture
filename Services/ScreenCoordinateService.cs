using System.Windows;
using CodexCapture.Interop;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace CodexCapture.Services;

internal static class ScreenCoordinateService
{
    public static Int32Rect GetPhysicalVirtualScreenBounds()
    {
        return new Int32Rect(
            NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
    }

    public static Int32Rect ClipToPhysicalVirtualScreen(Int32Rect physicalRect)
    {
        return Intersect(physicalRect, GetPhysicalVirtualScreenBounds());
    }

    public static Int32Rect Intersect(Int32Rect rect, Int32Rect bounds)
    {
        var left = Math.Max(rect.X, bounds.X);
        var top = Math.Max(rect.Y, bounds.Y);
        var right = Math.Min(rect.X + rect.Width, bounds.X + bounds.Width);
        var bottom = Math.Min(rect.Y + rect.Height, bounds.Y + bounds.Height);

        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static WpfRect PhysicalRectToOverlayRect(Window overlay, Int32Rect physicalRect)
    {
        var topLeft = overlay.PointFromScreen(new WpfPoint(physicalRect.X, physicalRect.Y));
        var bottomRight = overlay.PointFromScreen(new WpfPoint(
            physicalRect.X + physicalRect.Width,
            physicalRect.Y + physicalRect.Height));

        return new WpfRect(topLeft, bottomRight);
    }

    public static Int32Rect OverlayPointsToPhysicalRect(Window overlay, WpfPoint start, WpfPoint end)
    {
        var screenStart = overlay.PointToScreen(start);
        var screenEnd = overlay.PointToScreen(end);
        return NormalizePhysicalRect(screenStart, screenEnd);
    }

    public static Int32Rect NormalizePhysicalRect(WpfPoint a, WpfPoint b)
    {
        var left = (int)Math.Floor(Math.Min(a.X, b.X));
        var top = (int)Math.Floor(Math.Min(a.Y, b.Y));
        var right = (int)Math.Ceiling(Math.Max(a.X, b.X));
        var bottom = (int)Math.Ceiling(Math.Max(a.Y, b.Y));

        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
