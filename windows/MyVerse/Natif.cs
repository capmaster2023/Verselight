using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MyVerse.Ui;

/// <summary>Un écran branché au PC (coordonnées en pixels réels).</summary>
public sealed record Moniteur(string Nom, int X, int Y, int W, int H, bool Principal, string Peripherique);

/// <summary>Appels à Windows : liste des écrans (avec leur vrai nom), position des fenêtres, barre de titre sombre.</summary>
public static class Natif
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_FRAMECHANGED = 0x0020;

    // ------------------------------------------------------------ noms des écrans (comme dans Paramètres › Affichage)
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }
    [StructLayout(LayoutKind.Sequential)] struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint nPaths, out uint nModes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint nPaths, [Out] PATH_INFO[] paths, ref uint nModes, [Out] MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME packet);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME packet);

    /// <summary>Nom réel des écrans (« DELL U2419H », « EPSON PJ ») par nom de périphérique (\\.\DISPLAY1).</summary>
    static Dictionary<string, string> NomsReels()
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const uint QDC_ONLY_ACTIVE_PATHS = 2;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return o;
            var paths = new PATH_INFO[np];
            var modes = new MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return o;
            for (int i = 0; i < np; i++)
            {
                var src = new SOURCE_DEVICE_NAME { viewGdiDeviceName = "" };
                src.header.type = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                src.header.size = (uint)Marshal.SizeOf<SOURCE_DEVICE_NAME>();
                src.header.adapterId = paths[i].sourceInfo.adapterId;
                src.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                var tgt = new TARGET_DEVICE_NAME { monitorFriendlyDeviceName = "", monitorDevicePath = "" };
                tgt.header.type = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                tgt.header.size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>();
                tgt.header.adapterId = paths[i].targetInfo.adapterId;
                tgt.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;
                if (!string.IsNullOrWhiteSpace(tgt.monitorFriendlyDeviceName)) o[src.viewGdiDeviceName] = tgt.monitorFriendlyDeviceName.Trim();
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException) { }
        return o;
    }

    /// <summary>Les écrans branchés : l'écran principal d'abord.</summary>
    public static List<Moniteur> Ecrans()
    {
        var liste = new List<Moniteur>();
        var noms = NomsReels();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
                if (GetMonitorInfo(h, ref mi))
                {
                    var b = mi.rcMonitor;
                    liste.Add(new Moniteur(noms.TryGetValue(mi.szDevice, out var n) ? n : "", b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top, (mi.dwFlags & 1) != 0, mi.szDevice));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        if (liste.Count == 0)
        {
            var w = (int)SystemParameters.PrimaryScreenWidth;
            var h = (int)SystemParameters.PrimaryScreenHeight;
            liste.Add(new Moniteur("", 0, 0, w, h, true, ""));
        }
        liste = liste.OrderByDescending(m => m.Principal).ThenBy(m => m.X).ThenBy(m => m.Y).ToList();
        for (int i = 0; i < liste.Count; i++)
            if (liste[i].Nom.Length == 0) liste[i] = liste[i] with { Nom = liste[i].Principal ? "Écran principal" : $"Écran {i + 1}" };
        return liste;
    }

    /// <summary>Barre de titre sombre (projection) ou claire (régie, dialogues) — Windows 10 20H1 et plus, Windows 11.</summary>
    public static void TitreSombre(Window w, bool sombre = true)
    {
        try
        {
            var h = new WindowInteropHelper(w).EnsureHandle();
            int oui = sombre ? 1 : 0;
            if (DwmSetWindowAttribute(h, 20, ref oui, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref oui, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Ramène une fenêtre au premier plan (même réduite).</summary>
    public static void Devant(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }
}
