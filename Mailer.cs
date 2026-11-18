using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Tempo;

/// <summary>
/// Opens a new email in the user's mail app (Outlook, Thunderbird, the Windows Mail app with MAPI…) with the PDF attached.
/// The user always reviews and presses Send themselves: Tempo never sends mail on its own and never sees any password.
/// </summary>
public static class Mailer
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MapiMessageW
    {
        public int Reserved; public string? Subject; public string? NoteText; public string? MessageType; public string? DateReceived;
        public string? ConversationID; public int Flags; public IntPtr Originator; public int RecipCount; public IntPtr Recips;
        public int FileCount; public IntPtr Files;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MapiRecipDescW { public int Reserved; public int RecipClass; public string? Name; public string? Address; public int EIDSize; public IntPtr EntryID; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MapiFileDescW { public int Reserved; public int Flags; public int Position; public string? PathName; public string? FileName; public IntPtr FileType; }

    [DllImport("mapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "MAPISendMailW")]
    static extern int MAPISendMailW(IntPtr session, IntPtr hwnd, ref MapiMessageW msg, int flags, int reserved);

    const int MAPI_TO = 1, MAPI_LOGON_UI = 0x1, MAPI_DIALOG = 0x8, MAPI_USER_ABORT = 1;

    /// <summary>True if a compose window opened (or the user cancelled it). False means no MAPI mail app: use <see cref="Fallback"/>.</summary>
    public static bool Compose(string to, string subject, string body, string? attachment)
    {
        var allocs = new List<(IntPtr Ptr, Type Type)>();
        try
        {
            var msg = new MapiMessageW { Subject = subject, NoteText = body };
            if (!string.IsNullOrWhiteSpace(to))
            {
                var r = new MapiRecipDescW { RecipClass = MAPI_TO, Name = to, Address = "SMTP:" + to };
                var p = Marshal.AllocHGlobal(Marshal.SizeOf<MapiRecipDescW>()); allocs.Add((p, typeof(MapiRecipDescW)));
                Marshal.StructureToPtr(r, p, false);
                msg.RecipCount = 1; msg.Recips = p;
            }
            if (attachment != null && File.Exists(attachment))
            {
                var f = new MapiFileDescW { Position = -1, PathName = attachment, FileName = Path.GetFileName(attachment) };
                var p = Marshal.AllocHGlobal(Marshal.SizeOf<MapiFileDescW>()); allocs.Add((p, typeof(MapiFileDescW)));
                Marshal.StructureToPtr(f, p, false);
                msg.FileCount = 1; msg.Files = p;
            }
            var hwnd = new System.Windows.Interop.WindowInteropHelper(MainWindow.Instance).Handle;
            var rc = MAPISendMailW(IntPtr.Zero, hwnd, ref msg, MAPI_DIALOG | MAPI_LOGON_UI, 0);
            return rc == 0 || rc == MAPI_USER_ABORT;
        }
        catch { return false; }   // no mapi32 / no default mail client
        finally
        {
            // free the strings StructureToPtr copied, then the blocks themselves
            foreach (var (p, t) in allocs) { Marshal.DestroyStructure(p, t); Marshal.FreeHGlobal(p); }
        }
    }

    /// <summary>No MAPI mail app: open a mailto: draft (no attachment possible) and show the PDF in Explorer so it can be dragged in.</summary>
    public static void Fallback(string to, string subject, string body, string? attachment)
    {
        static string E(string s) => Uri.EscapeDataString(s);
        var url = $"mailto:{E(to)}?subject={E(subject)}&body={E(body)}";
        if (url.Length > 1900) url = $"mailto:{E(to)}?subject={E(subject)}";   // mailto links get cut off by some apps
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        if (attachment != null && SafeOpen.IsAllowed(attachment, null, ".pdf") && File.Exists(attachment))
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(attachment)}\"") { UseShellExecute = false }); } catch { }
    }
}
