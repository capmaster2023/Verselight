using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace MyVerse.Ui;

/// <summary>Only the explicitly selected device/window is captured. No audio is recorded.
/// Une instance par contenu vivant : App.Sources (écran principal) et App.SourcesDuo (contenu épinglé du Duo).</summary>
public sealed class SourcesDisplay : IDisposable
{
    /// <summary>Nom de l'image écrite pour la page OBS : display.jpg (écran principal) ou display-duo.jpg (Duo) ;
    /// vide pour un lecteur qui n'écrit rien (vidéo des enregistrements).</summary>
    readonly string export;
    public SourcesDisplay(string export = "display.jpg") { this.export = export; }

    public sealed record Peripherique(int Index, string Nom, string Id);
    public sealed record Fenetre(long Handle, int Processus, string Titre);
    public BitmapSource? Image { get; private set; }
    public string Statut { get; private set; } = "";
    public event Action? Change;
    CancellationTokenSource? fin;
    Task travail = Task.CompletedTask;
    SourceDisplay? source;
    int generation;
    long dernierExport;

    public void Choisir(EtatEcran etat) => ChoisirSource(etat.Mode == "media" ? etat.Media : null);

    /// <summary>La source à capturer (null : aucune). Sans effet si c'est déjà elle.</summary>
    public void ChoisirSource(SourceDisplay? nouvelle)
    {
        if (nouvelle == source) return;
        source = nouvelle;
        try { fin?.Cancel(); } catch (ObjectDisposedException) { }
        var avant = travail;
        var token = new CancellationTokenSource();
        fin = token;
        int g = ++generation;
        Image = null;
        ViderExport();
        Statut = nouvelle == null ? "" : "Connexion à la source…";
        Change?.Invoke();
        travail = Task.Run(async () =>
        {
            try
            {
                await avant.ConfigureAwait(false);
                token.Token.ThrowIfCancellationRequested();
                if (nouvelle == null) return;
                if (nouvelle.Type == "image")
                {
                    var image = new BitmapImage();
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1920;
                    image.UriSource = new Uri(nouvelle.Chemin, UriKind.Absolute);
                    image.EndInit(); image.Freeze();
                    await Publier(image, g, token.Token);
                }
                else if (nouvelle.Type == "camera")
                {
                    using var lease = Audio.CameraFrames.Acquire(nouvelle.Peripherique);
                    BitmapSource? previous = null;
                    while (!token.IsCancellationRequested)
                    {
                        if (lease.Error is { } error) throw new IOException(error);
                        if (lease.Image is { } b && !ReferenceEquals(b, previous)) { await Publier(b, g, token.Token); previous = b; }
                        await Task.Delay(15, token.Token);
                    }
                }
                else if (nouvelle.Type == "mycam")
                {
                    // MyCam : les images JPEG du téléphone arrivent par sa connexion ; on montre toujours la plus récente
                    var cams = App.Telephone?.Cameras ?? throw new IOException("MyCam indisponible : la connexion des téléphones est fermée.");
                    var signal = new SemaphoreSlim(0, 1);
                    void Recue(string id)
                    {
                        if (id != nouvelle.Peripherique) return;
                        try { signal.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                    }
                    cams.NouveauCadre += Recue;
                    try
                    {
                        long dernier = -1;
                        bool attente = false, erreurSignalee = false;
                        var depuis = Stopwatch.StartNew();
                        while (!token.IsCancellationRequested)
                        {
                            if (cams.Dernier(nouvelle.Peripherique) is { } c && c.Numero != dernier)
                            {
                                dernier = c.Numero;
                                // une image ratée ne coupe pas la caméra : l'image suivante arrive dans quelques millisecondes
                                try
                                {
                                    if (DecoderMyCam(c) is { } b) { await Publier(b, g, token.Token); attente = false; }
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    if (!erreurSignalee) { erreurSignalee = true; Journal.Erreur("MyCam (image sautée)", ex); }
                                }
                                depuis.Restart();
                            }
                            else if (!attente && depuis.ElapsedMilliseconds > (dernier < 0 ? 300 : 2500))
                            {
                                attente = true;
                                var info = cams.InfoDe(nouvelle.Peripherique);
                                var texte = info == null ? "En attente de MyCam : sur le téléphone, MyChurch › MyCam › Diffuser."
                                    : !info.Diffuse ? $"« {info.Nom} » ne diffuse pas : touchez Diffuser dans MyCam sur le téléphone."
                                    : $"« {info.Nom} » : images interrompues (Wi-Fi ?). Reprise automatique.";
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (g != generation) return;
                                    Statut = texte; Change?.Invoke();
                                });
                            }
                            await signal.WaitAsync(500, token.Token);
                        }
                    }
                    finally
                    {
                        cams.NouveauCadre -= Recue;
                        signal.Dispose();
                    }
                }
                else if (nouvelle.Type == "video")
                {
                    using var video = new VideoCapture(nouvelle.Chemin);
                    if (!video.IsOpened()) throw new IOException("Vidéo illisible.");
                    using var frame = new Mat(); using var bgra = new Mat();
                    double fps = video.Fps; if (!double.IsFinite(fps) || fps <= 0) fps = 30;
                    var clock = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested)
                    {
                        double wanted = clock.Elapsed.TotalSeconds * fps;
                        if (video.PosFrames > wanted) { await Task.Delay(5, token.Token); continue; }
                        if (wanted - video.PosFrames > 3) video.PosFrames = (int)Math.Floor(wanted);
                        if (!video.Read(frame) || frame.Empty()) break;
                        Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
                        var b = BitmapSource.Create(bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, bgra.Data, (int)(bgra.Step() * bgra.Height), (int)bgra.Step()); b.Freeze();
                        await Publier(b, g, token.Token);
                    }
                }
                else if (nouvelle.Type == "fenetre")
                {
                    while (!token.IsCancellationRequested)
                    {
                        var h = new IntPtr(nouvelle.Fenetre);
                        GetWindowThreadProcessId(h, out uint pid);
                        if (!IsWindow(h) || pid != nouvelle.Processus) throw new IOException("La fenêtre a été fermée. Choisissez à nouveau la fenêtre à capturer.");
                        if (IsIconic(h)) throw new IOException("Restaurez la fenêtre à capturer, puis réaffichez cette source.");
                        await Publier(Capturer(h), g, token.Token);
                        await Task.Delay(66, token.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Journal.Erreur("Display", ex);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (g != generation) return;
                    Image = null; ViderExport(); Statut = ex.Message; Change?.Invoke();
                    if (Application.Current.MainWindow is MainWindow w) w.Toast(ex.Message);
                });
            }
            finally { token.Dispose(); }
        });
    }

    /// <summary>Image MyCam : le JPEG décodé, tourné d'un quart de tour au besoin (téléphone en hauteur ou à l'horizontale), en miroir au besoin.</summary>
    public static BitmapSource? DecoderMyCam(MyCams.Cadre c, int largeur = 0)
    {
        try
        {
            using var flux = new MemoryStream(c.Jpeg, false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = flux;
            if (largeur > 0) image.DecodePixelWidth = largeur;
            image.Rotation = c.Rotation switch { 1 => Rotation.Rotate90, 2 => Rotation.Rotate180, 3 => Rotation.Rotate270, _ => Rotation.Rotate0 };
            image.EndInit();
            BitmapSource b = image;
            if (c.Miroir) b = new TransformedBitmap(b, new ScaleTransform(-1, 1));
            b.Freeze();
            return b;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    async Task Publier(BitmapSource b, int g, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (g != generation) return;
            Image = b; Statut = "En direct"; Change?.Invoke();
        });
        if (export.Length == 0 || g != generation || !App.SortieObs.Prete || Environment.TickCount64 - dernierExport < 100) return;
        dernierExport = Environment.TickCount64;
        await Exporter(b, g);
    }

    /// <summary>
    /// L'image de la page OBS (display.jpg), écrite à côté puis mise à la place. OBS (ou l'antivirus) tient parfois le fichier ouvert
    /// au moment du remplacement : Windows répond « accès refusé ». On réessaie un instant, sinon cette image est sautée ;
    /// la source, elle, continue (avant la 4.0.1, cette erreur arrêtait la caméra : écran noir ou image figée).
    /// </summary>
    async Task Exporter(BitmapSource b, int g)
    {
        try
        {
            BitmapSource sortie = b.PixelWidth > 1280 ? new TransformedBitmap(b, new ScaleTransform(1280.0 / b.PixelWidth, 1280.0 / b.PixelWidth)) : b;
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(sortie));
            var path = Path.Combine(App.SortieObs.Dossier, export);
            using (var file = File.Create(path + ".tmp")) encoder.Save(file);
            for (int essai = 0; essai < 3 && g == generation; essai++)
            {
                try { File.Move(path + ".tmp", path, true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(10); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException) { }
    }

    void ViderExport()
    {
        if (export.Length == 0 || !App.SortieObs.Prete) return;
        try
        {
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3)));
            var path = Path.Combine(App.SortieObs.Dossier, export);
            // Distinct temp path: an in-flight old source may still finish encoding its last frame.
            using (var f = File.Create(path + ".blank")) encoder.Save(f);
            File.Move(path + ".blank", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException) { }
    }

    public void Reconnecter(EtatEcran e) { source = null; Choisir(e); }
    public void ReconnecterSource(SourceDisplay? s) { source = null; ChoisirSource(s); }
    public void Dispose() { ++generation; try { fin?.Cancel(); } catch (ObjectDisposedException) { } }

    public static List<Peripherique> Peripheriques()
    {
        var liste = new List<Peripherique>();
        object? obj = null;
        IEnumMoniker? enumeration = null;
        try
        {
            obj = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86"))!);
            var categorie = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
            if (((ICreateDevEnum)obj!).CreateClassEnumerator(ref categorie, out enumeration, 0) != 0 || enumeration == null) return liste;
            var monikers = new IMoniker[1];
            int index = 0;
            while (enumeration.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var m = monikers[0]; object? sac = null;
                try
                {
                    var iid = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
                    m.BindToStorage(null!, null!, ref iid, out sac);
                    ((IPropertyBag)sac).Read("FriendlyName", out var nom, IntPtr.Zero);
                    m.GetDisplayName(null!, null!, out var id);
                    if (!string.Equals(nom?.ToString(), "MyVirtualCam", StringComparison.OrdinalIgnoreCase))
                        liste.Add(new Peripherique(index, nom?.ToString() ?? $"Caméra {index + 1}", id));
                }
                finally { if (sac != null) Marshal.ReleaseComObject(sac); Marshal.ReleaseComObject(m); index++; }
            }
        }
        finally { if (enumeration != null) Marshal.ReleaseComObject(enumeration); if (obj != null) Marshal.ReleaseComObject(obj); }
        return liste;
    }

    public static List<Fenetre> Fenetres()
    {
        var list = new List<Fenetre>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId) return true;
            var titre = new StringBuilder(512); GetWindowText(h, titre, titre.Capacity);
            if (titre.Length > 0) list.Add(new Fenetre(h.ToInt64(), (int)pid, titre.ToString()));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(f => f.Titre).ToList();
    }

    static BitmapSource Capturer(IntPtr h)
    {
        if (!GetWindowRect(h, out var r)) throw new IOException("Fenêtre inaccessible.");
        int w = r.Right - r.Left, height = r.Bottom - r.Top;
        if (w <= 0 || height <= 0 || w > 7680 || height > 4320) throw new IOException("Dimensions de fenêtre non prises en charge.");
        var dc = GetDC(IntPtr.Zero); var mem = CreateCompatibleDC(dc); IntPtr bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = w, Height = -height, Planes = 1, Bits = 32 };
            bmp = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bmp == IntPtr.Zero) throw new IOException("Capture impossible (mémoire graphique).");
            old = SelectObject(mem, bmp);
            if (!PrintWindow(h, mem, 2)) throw new IOException("Cette fenêtre refuse la capture. Essayez une fenêtre classique, non protégée.");
            var b = BitmapSource.Create(w, height, 96, 96, PixelFormats.Bgr32, null, bits, w * height * 4, w * 4);
            b.Freeze(); return b;
        }
        finally { if (old != IntPtr.Zero) SelectObject(mem, old); if (bmp != IntPtr.Zero) DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, dc); }
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum { [PreserveSig] int CreateClassEnumerator(ref Guid type, out IEnumMoniker result, int flags); }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr log);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }
    delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Used, Important; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rectangle r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
}
