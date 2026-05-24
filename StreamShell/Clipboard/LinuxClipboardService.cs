using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StreamShell;

/// <summary>
/// Linux clipboard service that delegates to <c>xclip</c> (X11),
/// <c>wl-copy</c>/<c>wl-paste</c> (Wayland), or falls back to no-op.
/// Avoids the <see cref="DllNotFoundException"/> from the Windows-only
/// <see cref="WindowsClipboardService"/>.
/// </summary>
internal sealed class LinuxClipboardService : IClipboardService
{
    private readonly string? _copyCmd;
    private readonly string? _pasteCmd;
    private readonly string? _pasteArgs;

    /// <inheritdoc/>
    public bool IsAvailable => _copyCmd is not null;

    public LinuxClipboardService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Prefer Wayland, fall back to X11
            if (HasCommand("wl-copy"))
            {
                _copyCmd = "wl-copy";
                _pasteCmd = "wl-paste";
                _pasteArgs = null;
            }
            else if (HasCommand("xclip"))
            {
                _copyCmd = "xclip";
                _pasteCmd = "xclip";
                _pasteArgs = "-selection clipboard -o";
            }
            else if (HasCommand("xsel"))
            {
                _copyCmd = "xsel";
                _pasteCmd = "xsel";
                _pasteArgs = "--clipboard --output";
            }
            // else: no clipboard tool available — no-op
        }
    }

    /// <inheritdoc/>
    public string? Paste()
    {
        if (_pasteCmd is null)
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pasteCmd,
                Arguments = _pasteArgs ?? "",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Copy(string text)
    {
        if (_copyCmd is null || string.IsNullOrEmpty(text))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _copyCmd,
                Arguments = _pasteArgs is not null ? "-selection clipboard" : "",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return;

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(2000);
        }
        catch
        {
            // clipboard unavailable — silent no-op
        }
    }

    private static bool HasCommand(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = name,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(1000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
