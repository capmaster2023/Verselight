using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MyVerse.Ui;

/// <summary>Always publishes the program output while MyChurch is open. The registered filter stays available when it is closed.</summary>
public sealed class MyVirtualCam : IDisposable
{
    public const string Clsid = "{A9D47A18-C751-4BDE-AB6B-A3D13F090501}";
    public const int Width = 1280, Height = 720, Stride = Width * 3, Bytes = Stride * Height;
    readonly Rendu rendu = new() { Transitions = false, Width = Width, Height = Height, Background = Brushes.Black };
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly MemoryMappedFile fichier;
    readonly MemoryMappedViewAccessor vue;
    readonly Mutex verrou = new(false, @"Local\MyChurch.MyVirtualCam.lock.v1");
    readonly byte[] top = new byte[Bytes], bottom = new byte[Bytes];
    readonly RenderTargetBitmap image = new(Width, Height, 96, 96, PixelFormats.Pbgra32);
    bool sale = true, dispose;
    int seq;
    public static bool Installee => Registry.ClassesRoot.OpenSubKey(@"CLSID\" + Clsid + @"\InprocServer32") is { } k && Fermer(k);
    static bool Fermer(RegistryKey k) { k.Dispose(); return true; }

    public MyVirtualCam()
    {
        fichier = MemoryMappedFile.CreateOrOpen(@"Local\MyChurch.MyVirtualCam.frames.v1", 64 + Bytes, MemoryMappedFileAccess.ReadWrite);
        vue = fichier.CreateViewAccessor();
        timer.Tick += (_, _) => Publier();
        timer.Start();
    }
    public void Afficher(EtatEcran e) { rendu.Afficher(e); sale = true; }
    public void Media(BitmapSource? b) { rendu.ImageDisplay(b); sale = true; }
    void Publier()
    {
        try
        {
            if (sale)
            {
                rendu.Measure(new Size(Width, Height)); rendu.Arrange(new Rect(0, 0, Width, Height)); rendu.UpdateLayout();
                image.Clear(); image.Render(rendu);
                var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
                rgb.CopyPixels(top, Stride, 0);
                for (int y = 0; y < Height; y++) Buffer.BlockCopy(top, y * Stride, bottom, (Height - 1 - y) * Stride, Stride);
                sale = false;
            }
            bool acquis;
            try { acquis = verrou.WaitOne(0); } catch (AbandonedMutexException) { acquis = true; }
            if (!acquis) return;
            try
            {
                vue.Write(0, 0x4D43414D); vue.Write(4, 1);
                vue.Write(8, Environment.TickCount64); vue.Write(16, Width); vue.Write(20, Height); vue.Write(24, Stride); vue.Write(28, ++seq);
                vue.WriteArray(64, bottom, 0, Bytes);
            }
            finally { verrou.ReleaseMutex(); }
        }
        catch (Exception e) { Journal.Erreur("MyVirtualCam", e); timer.Stop(); }
    }
    public static void Installer(Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--virtualcam-install") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception e) { Dialogue.Informer(owner, "Installation interrompue : " + e.Message); }
    }
    public static int Enregistrer(bool installer)
    {
        // Register both architectures; consumers load the matching in-process DirectShow source.
        int resultat = 0;
        foreach (var (exe, dll) in new[] {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "regsvr32.exe"), "MyVirtualCam.dll"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "regsvr32.exe"), "MyVirtualCam32.dll") })
        {
            var path = Path.Combine(AppContext.BaseDirectory, dll);
            if (!File.Exists(path)) { resultat = 2; continue; }
            using var p = Process.Start(new ProcessStartInfo(exe, (installer ? "/s " : "/s /u ") + "\"" + path + "\"") { UseShellExecute = false });
            if (p == null || !p.WaitForExit(30000) || p.ExitCode != 0) resultat = 1;
        }
        return resultat;
    }
    public void Dispose()
    {
        if (dispose) return; dispose = true; timer.Stop();
        try { vue.Write(8, 0L); } catch (Exception) { }
        vue.Dispose(); fichier.Dispose(); verrou.Dispose();
    }
}
