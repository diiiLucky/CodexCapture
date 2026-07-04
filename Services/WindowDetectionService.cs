using System.Text;
using System.Windows;
using CodexCapture.Interop;
using WpfPoint = System.Windows.Point;

namespace CodexCapture.Services;

public sealed class WindowDetectionService
{
    internal const int FullscreenTolerancePixels = 3;

    public DetectedWindow? Detect(WpfPoint screenPoint)
    {
        var hwnd = FindWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (root == IntPtr.Zero || !NativeMethods.IsWindowVisible(root))
        {
            return null;
        }

        var rect = GetWindowBounds(root);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var titleBuilder = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(root, titleBuilder, titleBuilder.Capacity);
        return new DetectedWindow(root, rect, titleBuilder.ToString());
    }

    public bool IsForegroundFullscreenLikeWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return IsFullscreenLikeWindow(root == IntPtr.Zero ? hwnd : root);
    }

    internal static bool ShouldAutoHideFloatingWidget(
        Int32Rect windowBounds,
        Int32Rect monitorBounds,
        Int32Rect workAreaBounds,
        long style,
        bool isZoomed,
        int tolerance = FullscreenTolerancePixels)
    {
        if (!IsUsableRect(windowBounds) || !IsUsableRect(monitorBounds) || !IsUsableRect(workAreaBounds))
        {
            return false;
        }

        if (!Covers(windowBounds, monitorBounds, tolerance))
        {
            return false;
        }

        return !IsOrdinaryMaximizedWindow(windowBounds, workAreaBounds, style, isZoomed, tolerance);
    }

    private static bool IsFullscreenLikeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero ||
            !NativeMethods.IsWindowVisible(hwnd) ||
            NativeMethods.IsIconic(hwnd) ||
            NativeMethods.IsCloaked(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out int processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (IsShellWindowClass(NativeMethods.GetClassName(hwnd)))
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        if ((exStyle & NativeMethods.WsExToolWindow) != 0)
        {
            return false;
        }

        var bounds = GetWindowBounds(hwnd);
        if (!IsUsableRect(bounds) || !TryGetMonitorBounds(bounds, out var monitorBounds, out var workAreaBounds))
        {
            return false;
        }

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlStyle);
        return ShouldAutoHideFloatingWidget(
            bounds,
            monitorBounds,
            workAreaBounds,
            style,
            NativeMethods.IsZoomed(hwnd));
    }

    private static IntPtr FindWindowUnderPoint(WpfPoint screenPoint)
    {
        var x = (int)screenPoint.X;
        var y = (int)screenPoint.Y;
        var currentProcessId = Environment.ProcessId;
        var result = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out int processId);
            if (processId == currentProcessId)
            {
                return true;
            }

            if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsCloaked(hwnd))
            {
                return true;
            }

            if (IsShellWindowClass(NativeMethods.GetClassName(hwnd)))
            {
                return true;
            }

            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
            if ((exStyle & NativeMethods.WsExToolWindow) != 0)
            {
                return true;
            }

            var bounds = GetWindowBounds(hwnd);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return true;
            }

            if (x >= bounds.X && x < bounds.X + bounds.Width &&
                y >= bounds.Y && y < bounds.Y + bounds.Height)
            {
                result = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool TryGetMonitorBounds(
        Int32Rect windowBounds,
        out Int32Rect monitorBounds,
        out Int32Rect workAreaBounds)
    {
        monitorBounds = Int32Rect.Empty;
        workAreaBounds = Int32Rect.Empty;

        var rect = ToNativeRect(windowBounds);
        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = NativeMethods.MONITORINFO.Create();
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        monitorBounds = ToInt32Rect(info.rcMonitor);
        workAreaBounds = ToInt32Rect(info.rcWork);
        return true;
    }

    private static Int32Rect GetWindowBounds(IntPtr hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaExtendedFrameBounds,
                out var dwmRect,
                MarshalSizeOfRect()) == 0)
        {
            return new Int32Rect(dwmRect.Left, dwmRect.Top, dwmRect.Width, dwmRect.Height);
        }

        return NativeMethods.GetWindowRect(hwnd, out var rect)
            ? new Int32Rect(rect.Left, rect.Top, rect.Width, rect.Height)
            : Int32Rect.Empty;
    }

    private static bool IsShellWindowClass(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static bool IsOrdinaryMaximizedWindow(
        Int32Rect windowBounds,
        Int32Rect workAreaBounds,
        long style,
        bool isZoomed,
        int tolerance)
    {
        if (!isZoomed || !HasStandardFrame(style))
        {
            return false;
        }

        return Covers(windowBounds, workAreaBounds, tolerance);
    }

    private static bool HasStandardFrame(long style) =>
        (style & NativeMethods.WsCaption) == NativeMethods.WsCaption &&
        (style & NativeMethods.WsThickFrame) != 0;

    private static bool Covers(Int32Rect rect, Int32Rect bounds, int tolerance)
    {
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        var boundsRight = bounds.X + bounds.Width;
        var boundsBottom = bounds.Y + bounds.Height;

        return rect.X <= bounds.X + tolerance &&
               rect.Y <= bounds.Y + tolerance &&
               right >= boundsRight - tolerance &&
               bottom >= boundsBottom - tolerance;
    }

    private static bool IsUsableRect(Int32Rect rect) => rect.Width > 0 && rect.Height > 0;

    private static NativeMethods.RECT ToNativeRect(Int32Rect rect) => new()
    {
        Left = rect.X,
        Top = rect.Y,
        Right = rect.X + rect.Width,
        Bottom = rect.Y + rect.Height
    };

    private static Int32Rect ToInt32Rect(NativeMethods.RECT rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static int MarshalSizeOfRect() => System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>();
}

public sealed record DetectedWindow(IntPtr Handle, Int32Rect Bounds, string? Title);
