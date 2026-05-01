namespace StreamShell;

using System.Runtime.InteropServices;

[Obsolete]
internal static class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint hMem);

    public static string? GetText()
    {
        if (!OpenClipboard(0))
            return null;

        try
        {
            nint handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
                return null;

            nint pointer = GlobalLock(handle);
            if (pointer == 0)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
