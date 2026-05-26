using System.Diagnostics;
using System.IO;
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

            FixWaylandEnv(psi);

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            // Clipboard tools often append a trailing newline — strip it
            result = result.TrimEnd('\n', '\r');
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

            FixWaylandEnv(psi);

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

    /// <summary>
    /// When the app is launched with <c>sudo</c>, Wayland environment variables
    /// (<c>XDG_RUNTIME_DIR</c>, <c>WAYLAND_DISPLAY</c>) are stripped. This
    /// helper reconstructs them from <c>SUDO_UID</c> so that <c>wl-paste</c> and
    /// <c>wl-copy</c> can still connect to the user's compositor.
    /// </summary>
    private static void FixWaylandEnv(ProcessStartInfo psi)
    {
        var sudoUid = Environment.GetEnvironmentVariable("SUDO_UID");
        if (string.IsNullOrEmpty(sudoUid))
            return;

        // If XDG_RUNTIME_DIR is missing or points to root's runtime dir,
        // reconstruct the original user's runtime directory.
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(xdgRuntime) || xdgRuntime.EndsWith("/user/0"))
        {
            var userRuntimeDir = $"/run/user/{sudoUid}";
            if (Directory.Exists(userRuntimeDir))
            {
                psi.EnvironmentVariables["XDG_RUNTIME_DIR"] = userRuntimeDir;
            }
        }

        // If WAYLAND_DISPLAY is missing, try to determine the correct socket name.
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(waylandDisplay))
        {
            var runtimeDir = psi.EnvironmentVariables.ContainsKey("XDG_RUNTIME_DIR")
                ? psi.EnvironmentVariables["XDG_RUNTIME_DIR"]
                : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (!string.IsNullOrEmpty(runtimeDir))
            {
                // The most common socket name is wayland-0, but check a few candidates.
                foreach (var candidate in new[] { "wayland-0", "wayland-1" })
                {
                    if (File.Exists(Path.Combine(runtimeDir, candidate)))
                    {
                        psi.EnvironmentVariables["WAYLAND_DISPLAY"] = candidate;
                        break;
                    }
                }
            }
        }
    }
}
