using System.Runtime.InteropServices;
using System.Text;

namespace StreamShell;

internal class ClipboardService : IClipboardService
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVABLE = 0x0002;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    /// <summary>Read Unicode text from the system clipboard.</summary>
    public string? Paste()
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

    /// <summary>Write Unicode text to the system clipboard.</summary>
    public void Copy(string text)
    {
        if (!OpenClipboard(0))
            return;

        try
        {
            EmptyClipboard();

            // Allocate global memory for the null-terminated Unicode string
            byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
            nint hGlobal = GlobalAlloc(GMEM_MOVABLE, (nint)bytes.Length);

            if (hGlobal == 0)
                return;

            try
            {
                nint pointer = GlobalLock(hGlobal);
                if (pointer == 0)
                    return;

                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                // SetClipboardData takes ownership of hGlobal on success
                nint result = SetClipboardData(CF_UNICODETEXT, hGlobal);
                if (result != 0)
                    hGlobal = nint.Zero; // ownership transferred
            }
            finally
            {
                // Only free if ownership was NOT transferred to the clipboard
                if (hGlobal != nint.Zero)
                    GlobalFree(hGlobal);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
