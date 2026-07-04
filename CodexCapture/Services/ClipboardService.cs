using System.Runtime.InteropServices;
using System.Windows;
using CodexCapture.Interop;

namespace CodexCapture.Services;

internal interface IClipboardService
{
    bool TrySetText(string text, out string? errorMessage);

    Task<ClipboardWriteResult> TrySetTextAsync(string text, CancellationToken cancellationToken = default);

    bool TrySetDataObject(IDataObject dataObject, out string? errorMessage);
}

internal sealed record ClipboardWriteResult(bool Success, string? ErrorMessage);

internal sealed class ClipboardService : IClipboardService
{
    private const int SyncMaxAttempts = 8;
    private const int AsyncMaxAttempts = 40;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);

    public bool TrySetText(string text, out string? errorMessage) =>
        TryExecute(() => SetUnicodeText(text), SyncMaxAttempts, out errorMessage);

    public Task<ClipboardWriteResult> TrySetTextAsync(string text, CancellationToken cancellationToken = default) =>
        TryExecuteAsync(() => SetUnicodeText(text), AsyncMaxAttempts, cancellationToken);

    public bool TrySetDataObject(IDataObject dataObject, out string? errorMessage) =>
        TryExecute(() => Clipboard.SetDataObject(dataObject, true), SyncMaxAttempts, out errorMessage);

    internal static bool TryExecute(
        Action action,
        int maxAttempts,
        out string? errorMessage,
        Action<TimeSpan>? delay = null)
    {
        Exception? lastException = null;
        delay ??= static duration => Thread.Sleep(duration);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                errorMessage = null;
                return true;
            }
            catch (Exception exception) when (IsClipboardBusy(exception))
            {
                lastException = exception;
                if (attempt < maxAttempts)
                {
                    delay(RetryDelay);
                }
            }
        }

        errorMessage = BuildErrorMessage(lastException);
        return false;
    }

    internal static async Task<ClipboardWriteResult> TryExecuteAsync(
        Action action,
        int maxAttempts,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Exception? lastException = null;
        delay ??= static (duration, token) => Task.Delay(duration, token);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                action();
                return new ClipboardWriteResult(true, null);
            }
            catch (Exception exception) when (IsClipboardBusy(exception))
            {
                lastException = exception;
                if (attempt < maxAttempts)
                {
                    await delay(RetryDelay, cancellationToken).ConfigureAwait(true);
                }
            }
        }

        return new ClipboardWriteResult(false, BuildErrorMessage(lastException));
    }

    private static void SetUnicodeText(string text)
    {
        var handle = CreateUnicodeTextHandle(text);
        var handleTransferred = false;

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            NativeMethods.GlobalFree(handle);
            throw new ClipboardBusyException("OpenClipboard failed.", Marshal.GetLastWin32Error());
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                throw new ClipboardBusyException("EmptyClipboard failed.", Marshal.GetLastWin32Error());
            }

            if (NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, handle) == IntPtr.Zero)
            {
                throw new ClipboardBusyException("SetClipboardData failed.", Marshal.GetLastWin32Error());
            }

            handleTransferred = true;
        }
        finally
        {
            if (!handleTransferred && handle != IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
            }

            NativeMethods.CloseClipboard();
        }
    }

    private static IntPtr CreateUnicodeTextHandle(string text)
    {
        var bytes = checked((text.Length + 1) * 2);
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable, (UIntPtr)bytes);
        if (handle == IntPtr.Zero)
        {
            throw new ClipboardBusyException("GlobalAlloc failed.", Marshal.GetLastWin32Error());
        }

        var target = NativeMethods.GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            throw new ClipboardBusyException("GlobalLock failed.", Marshal.GetLastWin32Error());
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }

    private static bool IsClipboardBusy(Exception exception) =>
        exception is COMException or ExternalException or ClipboardBusyException;

    private static string BuildErrorMessage(Exception? exception)
    {
        var owner = GetClipboardOwnerDescription();
        var reason = exception?.Message ?? "无法写入剪贴板。";
        return string.IsNullOrWhiteSpace(owner)
            ? reason
            : $"{reason} 当前占用：{owner}";
    }

    private static string? GetClipboardOwnerDescription()
    {
        var owner = NativeMethods.GetOpenClipboardWindow();
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(owner, out var processId);
        var processName = TryGetProcessName(processId);
        var title = GetWindowTitle(owner);

        if (!string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(title))
        {
            return $"{processName} - {title}";
        }

        return !string.IsNullOrWhiteSpace(processName)
            ? processName
            : !string.IsNullOrWhiteSpace(title)
                ? title
                : $"PID {processId}";
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}

internal sealed class ClipboardBusyException : Exception
{
    public ClipboardBusyException(string message, int errorCode)
        : base($"{message} Win32={errorCode}")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
