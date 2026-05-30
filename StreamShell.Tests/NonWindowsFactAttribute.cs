using System.Runtime.InteropServices;

namespace StreamShell.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped on Windows.
///
/// Some tests exercise <see cref="StreamShell.UserInputHandler.ProcessInput"/>'s
/// Linux CSI-batch path (ESC arriving separately from the trailing escape-sequence
/// bytes). That path is only taken on non-Windows platforms — on Windows conhost
/// delivers fully-formed <see cref="ConsoleKeyInfo"/> values, so the simulation
/// doesn't apply. Skipping keeps the suite green on Windows dev machines while
/// still running the assertions on Linux/macOS CI.
/// </summary>
internal sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Linux/macOS-only: exercises the non-Windows CSI escape-sequence batch path.";
    }
}
