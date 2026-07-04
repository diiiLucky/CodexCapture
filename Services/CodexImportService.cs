using System.Diagnostics;
using System.Windows;
using CodexCapture.Interop;
using CodexCapture.Models;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Forms = System.Windows.Forms;

namespace CodexCapture.Services;

public sealed class CodexImportService : IDisposable
{
    private const int SwRestore = 9;
    private static readonly TimeSpan PasteReadyTimeout = TimeSpan.FromSeconds(8);
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly IClipboardService _clipboardService;
    private readonly LogService _logService;
    private readonly UIA3Automation _automation = new();

    internal CodexImportService(ScreenCaptureService screenCaptureService, IClipboardService clipboardService, LogService logService)
    {
        _screenCaptureService = screenCaptureService;
        _clipboardService = clipboardService;
        _logService = logService;
    }

    public void Dispose()
    {
        _automation.Dispose();
    }

    public async Task<ImportOutcome> ImportAsync(CodexImportRequest request)
    {
        try
        {
            _screenCaptureService.CopyToClipboard(request.Capture);

            var process = await StartOrFindCodexAsync(request.Options);
            if (process is null)
            {
                return await FallbackAsync(request, "StartCodex", "Could not start or find Codex.");
            }

            var handle = await WaitForMainWindowAsync(process, TimeSpan.FromSeconds(20));
            if (handle == IntPtr.Zero)
            {
                return await FallbackAsync(request, "FindWindow", "Codex did not expose a main window.");
            }

            NativeMethods.ShowWindow(handle, SwRestore);
            if (!await BringWindowToForegroundAsync(handle, TimeSpan.FromSeconds(3)))
            {
                return await FallbackAsync(request, "ActivateWindow", "Codex window could not be activated.");
            }

            await Task.Delay(Math.Max(150, request.Options.PasteDelayMs));

            _logService.Info($"Codex import: sending new chat shortcut '{request.Options.NewChatShortcut}'.");
            Forms.SendKeys.SendWait(request.Options.NewChatShortcut);
            await Task.Delay(Math.Max(250, request.Options.PasteDelayMs));

            var pasteOutcome = await TryPasteIntoCodexAsync(handle, request.Options);
            if (pasteOutcome is not null)
            {
                return pasteOutcome;
            }

            return await FallbackAsync(request, "FindInput", "Codex 输入框未准备好，图片路径提示已复制到剪贴板。");
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Codex import failed.");
            return await FallbackAsync(request, "Exception", exception.Message);
        }
    }

    private async Task<Process?> StartOrFindCodexAsync(CodexImportOptions options)
    {
        var running = FindLikelyCodexProcess();
        if (running is not null)
        {
            return running;
        }

        if (!string.IsNullOrWhiteSpace(options.CodexExePath) && File.Exists(options.CodexExePath))
        {
            var startInfo = new ProcessStartInfo(options.CodexExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(options.PreferredWorkspacePath)
                    ? options.PreferredWorkspacePath
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            return Process.Start(startInfo);
        }

        _ = Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App")
        {
            UseShellExecute = true
        });
        return await WaitForCodexProcessAsync(TimeSpan.FromSeconds(12));
    }

    private bool IsLikelyCodexDesktopProcess(Process process)
    {
        if (process.Id == Environment.ProcessId)
        {
            return false;
        }

        if (process.ProcessName.Equals("CodexCapture", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return string.IsNullOrWhiteSpace(process.MainModule?.FileName)
                   || process.MainModule.FileName.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase)
                   || process.MainModule.FileName.EndsWith("\\Codex.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var started = DateTimeOffset.Now;
        while (DateTimeOffset.Now - started < timeout)
        {
            try
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }

            await Task.Delay(250);
        }

        return IntPtr.Zero;
    }

    private async Task<Process?> WaitForCodexProcessAsync(TimeSpan timeout)
    {
        var started = DateTimeOffset.Now;
        while (DateTimeOffset.Now - started < timeout)
        {
            var process = FindLikelyCodexProcess();
            if (process is not null)
            {
                return process;
            }

            await Task.Delay(250);
        }

        return null;
    }

    private Process? FindLikelyCodexProcess()
    {
        var candidates = Process.GetProcesses()
            .Where(IsLikelyCodexDesktopProcess)
            .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
            .ThenByDescending(p => p.StartTimeSafe())
            .ToList();

        var selected = candidates.FirstOrDefault();
        foreach (var candidate in candidates)
        {
            if (!ReferenceEquals(candidate, selected))
            {
                candidate.Dispose();
            }
        }

        return selected;
    }

    private static async Task<bool> BringWindowToForegroundAsync(IntPtr handle, TimeSpan timeout)
    {
        var started = DateTimeOffset.Now;
        while (DateTimeOffset.Now - started < timeout)
        {
            NativeMethods.ShowWindow(handle, SwRestore);
            NativeMethods.SetForegroundWindow(handle);
            await Task.Delay(100);

            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == handle || NativeMethods.IsChild(handle, foreground))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ImportOutcome?> TryPasteIntoCodexAsync(IntPtr handle, CodexImportOptions options)
    {
        var started = DateTimeOffset.Now;
        var attempts = 0;
        var minAttempts = Math.Max(1, options.PasteRetryCount);
        var delay = TimeSpan.FromMilliseconds(Math.Max(250, options.PasteDelayMs));

        while (attempts < minAttempts || DateTimeOffset.Now - started < PasteReadyTimeout)
        {
            attempts++;
            if (!await BringWindowToForegroundAsync(handle, TimeSpan.FromSeconds(1)))
            {
                _logService.Info($"Codex import paste attempt {attempts}: foreground not ready.");
                await Task.Delay(delay);
                continue;
            }

            var readiness = DetectInputReadiness(handle);
            _logService.Info($"Codex import paste attempt {attempts}: {readiness}");
            if (readiness.ShouldPaste)
            {
                if (readiness.Candidate is not null)
                {
                    TryFocusCandidate(readiness.Candidate);
                }

                Forms.SendKeys.SendWait("^v");
                await Task.Delay(500);
                return new ImportOutcome
                {
                    Success = true,
                    Stage = readiness.Verified ? "NewChatPasteImage" : "NewChatPasteImageUnverified"
                };
            }

            await Task.Delay(delay);
        }

        return null;
    }

    private InputReadiness DetectInputReadiness(IntPtr handle)
    {
        try
        {
            var window = _automation.FromHandle(handle);
            var focused = _automation.FocusedElement();
            if (focused is null)
            {
                return InputReadiness.GuardedPaste("no focused element");
            }

            var focusedInfo = ReadElementInfo(focused);
            var focusedDecision = EvaluateElementForPaste(focusedInfo, expectedProcessId: focusedInfo.ProcessId, requireProcessMatch: false);
            if (focusedDecision.IsInputCandidate)
            {
                return InputReadiness.VerifiedInput(focused, $"focused input: {focusedInfo}; decision={focusedDecision.Reason}");
            }

            var candidate = FindInputCandidate(window, focusedInfo.ProcessId);
            if (candidate is not null)
            {
                return InputReadiness.VerifiedInput(candidate, $"descendant input: {ReadElementInfo(candidate)}; focused={focusedInfo}");
            }

            return InputReadiness.GuardedPaste($"no explicit input candidate; focused={focusedInfo}; decision={focusedDecision.Reason}");
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "FlaUI input detection failed.");
            return InputReadiness.GuardedPaste($"UIA detection failed: {exception.Message}");
        }
    }

    private FlaUI.Core.AutomationElements.AutomationElement? FindInputCandidate(
        FlaUI.Core.AutomationElements.AutomationElement window,
        int expectedProcessId)
    {
        try
        {
            var candidates = new[]
            {
                window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)),
                window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document)),
                window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Custom)),
                window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Pane)),
                window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Group))
            };

            foreach (var candidate in candidates)
            {
                if (candidate is null)
                {
                    continue;
                }

                var info = ReadElementInfo(candidate);
                if (EvaluateElementForPaste(info, expectedProcessId, requireProcessMatch: true).IsInputCandidate)
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Codex input candidate search failed.");
        }

        return null;
    }

    private void TryFocusCandidate(FlaUI.Core.AutomationElements.AutomationElement candidate)
    {
        try
        {
            candidate.Focus();
            _logService.Info($"Codex import: focus attempted on {ReadElementInfo(candidate)}");
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Codex input candidate focus failed.");
        }
    }

    private static CodexElementInfo ReadElementInfo(FlaUI.Core.AutomationElements.AutomationElement element)
    {
        return new CodexElementInfo(
            element.ControlType.ToString(),
            element.Properties.Name.ValueOrDefault ?? string.Empty,
            element.Properties.AutomationId.ValueOrDefault ?? string.Empty,
            element.Properties.ClassName.ValueOrDefault ?? string.Empty,
            element.Properties.FrameworkId.ValueOrDefault ?? string.Empty,
            element.Properties.NativeWindowHandle.ValueOrDefault,
            element.Properties.ProcessId.ValueOrDefault,
            element.Properties.IsKeyboardFocusable.ValueOrDefault);
    }

    internal static PasteElementDecision EvaluateElementForPaste(
        CodexElementInfo element,
        int expectedProcessId,
        bool requireProcessMatch)
    {
        if (requireProcessMatch && element.ProcessId != 0 && expectedProcessId != 0 && element.ProcessId != expectedProcessId)
        {
            return new PasteElementDecision(false, "process mismatch");
        }

        if (element.ControlType is "Edit" or "Document")
        {
            return new PasteElementDecision(true, "text control");
        }

        if (element.ControlType is "Custom" or "Pane" or "Group" && element.IsKeyboardFocusable)
        {
            return new PasteElementDecision(true, "keyboard-focusable web control");
        }

        return new PasteElementDecision(false, "not an input-like control");
    }

    private async Task<ImportOutcome> FallbackAsync(CodexImportRequest request, string stage, string error)
    {
        var prompt = request.Options.OptionalPromptTemplate
            .Replace("{ImagePath}", request.Capture.ImagePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{Width}", request.Capture.WidthPx.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{Height}", request.Capture.HeightPx.ToString(), StringComparison.OrdinalIgnoreCase);

        try
        {
            var copied = _clipboardService.TrySetText(prompt, out var clipboardError);
            await Task.Delay(100);
            if (request.Options.FallbackMode == CodexFallbackMode.OpenFolderOnly)
            {
                var dir = Path.GetDirectoryName(request.Capture.ImagePath);
                if (dir is not null)
                {
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }

            return new ImportOutcome
            {
                Success = false,
                Stage = stage,
                UsedFallback = true,
                Error = copied ? error : $"{error}；自动复制提示词失败：{clipboardError}",
                RecoveryAction = copied
                    ? "图片路径提示已复制到剪贴板，可直接粘贴到 Codex。"
                    : "请手动复制图片路径，或直接打开截图目录后再粘贴到 Codex。"
            };
        }
        catch (Exception exception)
        {
            _logService.Error(exception, "Codex fallback failed.");
            return new ImportOutcome
            {
                Success = false,
                Stage = stage,
                UsedFallback = true,
                Error = $"{error}; fallback failed: {exception.Message}"
            };
        }
    }
}

internal static class ProcessExtensions
{
    public static DateTime StartTimeSafe(this Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}

internal sealed record CodexElementInfo(
    string ControlType,
    string Name,
    string AutomationId,
    string ClassName,
    string FrameworkId,
    nint NativeWindowHandle,
    int ProcessId,
    bool IsKeyboardFocusable)
{
    public override string ToString() =>
        $"type={ControlType}, name='{Name}', automationId='{AutomationId}', class='{ClassName}', " +
        $"framework='{FrameworkId}', hwnd={NativeWindowHandle}, pid={ProcessId}, focusable={IsKeyboardFocusable}";
}

internal sealed record PasteElementDecision(bool IsInputCandidate, string Reason);

internal sealed record InputReadiness(
    bool ShouldPaste,
    bool Verified,
    FlaUI.Core.AutomationElements.AutomationElement? Candidate,
    string Reason)
{
    public static InputReadiness VerifiedInput(FlaUI.Core.AutomationElements.AutomationElement candidate, string reason) =>
        new(true, true, candidate, reason);

    public static InputReadiness GuardedPaste(string reason) =>
        new(true, false, null, reason);

    public override string ToString() =>
        $"shouldPaste={ShouldPaste}, verified={Verified}, reason={Reason}";
}
