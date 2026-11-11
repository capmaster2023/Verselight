using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyVerse.Audio;
using OpenCvSharp;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace MyVerse.Ui.Audio;

/// <summary>Reference-counted camera captures shared by Display, Duo and recording.</summary>
public static class CameraFrames
{
    static readonly object gate = new();
    static readonly Dictionary<string, Capture> captures = new();
    public sealed class Lease : IDisposable
    {
        readonly Capture capture;
        bool disposed;
        internal Lease(Capture capture) => this.capture = capture;
        public BitmapSource? Image => Volatile.Read(ref capture.Image);
        public string? Error => capture.Error;
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                if (--capture.Users == 0) { captures.Remove(capture.Id); try { capture.Cancel.Cancel(); } catch (ObjectDisposedException) { } }
            }
        }
    }
    internal sealed class Capture
    {
        public string Id = "";
        public int Users;
        public BitmapSource? Image;
        public volatile string? Error;
        public readonly CancellationTokenSource Cancel = new();
    }
    public static Lease Acquire(string id)
    {
        lock (gate)
        {
            if (!captures.TryGetValue(id, out var capture))
            {
                var device = SourcesDisplay.Peripheriques().FirstOrDefault(d => d.Id == id) ?? throw new IOException("Caméra absente.");
                capture = new() { Id = id }; captures[id] = capture;
                var state = capture;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var camera = new VideoCapture(device.Index, VideoCaptureAPIs.DSHOW);
                        if (!camera.IsOpened()) throw new IOException("Caméra occupée ou indisponible.");
                        camera.Set(VideoCaptureProperties.FrameWidth, 1280); camera.Set(VideoCaptureProperties.FrameHeight, 720); camera.Set(VideoCaptureProperties.Fps, 30);
                        using var frame = new Mat(); using var bgra = new Mat();
                        while (!state.Cancel.IsCancellationRequested)
                        {
                            if (!camera.Read(frame) || frame.Empty()) throw new IOException("La caméra ne fournit plus d’image.");
                            Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
                            var bitmap = BitmapSource.Create(bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, bgra.Data, (int)(bgra.Step() * bgra.Height), (int)bgra.Step());
                            bitmap.Freeze(); Volatile.Write(ref state.Image, bitmap);
                            await Task.Delay(5, state.Cancel.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception e) { state.Error = e.Message; Journal.Erreur("caméra partagée", e); }
                    finally { state.Cancel.Dispose(); }
                });
            }
            capture.Users++; return new(capture);
        }
    }
}

/// <summary>Own off-screen WPF composition, independent of projector layout. UI thread renders;
/// recording worker copies latest pixels through a small frame-only lock.</summary>
public sealed class RecordingCompositor : IRecordingFrameProvider, IDisposable
{
    readonly Canvas canvas = new() { Background = Brushes.Black };
    readonly RenderTargetBitmap bitmap;
    readonly byte[] frame;
    readonly object frameGate = new();
    readonly List<CameraFrames.Lease> cameras = new();
    readonly List<(Image Image, CameraFrames.Lease Camera)> cameraViews = new();
    readonly List<(Image Image, VideoCapture Video, Mat Frame, Mat Bgra, double Fps)> videos = new();
    readonly List<Rendu> projections = new();
    /// <summary>Sources Display de la bibliothèque (texte, média, dossier Duo), rendues comme sur l'écran principal ;
    /// leurs médias (caméra, fenêtre, image, vidéo) sont lus par des lecteurs à part, sans toucher à l'écran.</summary>
    readonly List<(Rendu Rendu, Annonce Annonce, SourcesDisplay? Premier, SourcesDisplay? Second)> annonces = new();
    int tours;
    readonly DispatcherTimer timer;
    readonly Stopwatch clock = new();
    readonly int width, height, imagesParSeconde;
    bool disposed;
    public string? Error { get; private set; }
    public RecordingCompositor(RecordingProfile profile)
    {
        width = profile.Width; height = profile.Height; imagesParSeconde = Math.Max(1, profile.Fps);
        canvas.Width = width; canvas.Height = height;
        bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32); frame = new byte[width * height * 4];
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1d / profile.Fps) };
        try
        {
            foreach (var visual in profile.Visuals.Where(v => v.Visible))
            {
                FrameworkElement element;
                if (visual.Kind == "projection") { var rendu = new Rendu { Transitions = false }; projections.Add(rendu); element = rendu; }
                else if (visual.Kind == "annonce")
                {
                    var a = App.Donnees.Annonces.FirstOrDefault(x => x.Id == visual.Annonce) ?? throw new IOException("Une source Display de cet enregistrement a été supprimée : « Modifier » pour la retirer.");
                    var etat = App.Regie.EtatAnnonce(a) ?? throw new IOException($"La source Display « {a.Titre} » est vide.");
                    var rendu = new Rendu { Transitions = false };
                    SourcesDisplay? premier = null, second = null;
                    try
                    {
                        if (etat is { Mode: "media", Media: { } m1 }) { premier = new SourcesDisplay(""); premier.ChoisirSource(m1); }
                        if (etat.Duo?.Etat is { Mode: "media", Media: { } m2 }) { second = new SourcesDisplay(""); second.ChoisirSource(m2); }
                    }
                    catch { premier?.Dispose(); second?.Dispose(); throw; }
                    annonces.Add((rendu, a, premier, second));
                    rendu.Afficher(etat);
                    element = rendu;
                }
                else
                {
                    var image = new Image { Stretch = visual.Crop ? Stretch.UniformToFill : Stretch.Uniform }; element = image;
                    if (visual.Kind == "camera") { var lease = CameraFrames.Acquire(visual.DeviceId); cameras.Add(lease); cameraViews.Add((image, lease)); }
                    else if (visual.Kind == "image")
                    {
                        var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(visual.Path); img.DecodePixelWidth = width; img.EndInit(); img.Freeze(); image.Source = img;
                    }
                    else if (visual.Kind == "video")
                    {
                        var video = new VideoCapture(visual.Path);
                        if (!video.IsOpened()) { video.Dispose(); throw new IOException("Vidéo illisible : " + visual.Path); }
                        double fps = video.Fps; if (!double.IsFinite(fps) || fps <= 0) fps = 30;
                        videos.Add((image, video, new Mat(), new Mat(), fps));
                    }
                    else throw new InvalidDataException("Type de contenu inconnu : " + visual.Kind);
                }
                var clip = new Grid { Width = width * visual.Width, Height = height * visual.Height, ClipToBounds = true }; clip.Children.Add(element);
                Canvas.SetLeft(clip, visual.X * width); Canvas.SetTop(clip, visual.Y * height); canvas.Children.Add(clip);
            }
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
            timer.Tick += (_, _) => Render(); Render();
        }
        catch { Dispose(); throw; }
    }
    public async Task ValidateAsync()
    {
        foreach (var c in cameras)
        {
            var until = Stopwatch.StartNew();
            while (c.Image == null && c.Error == null && until.ElapsedMilliseconds < 8000) await Task.Delay(50);
            if (c.Image == null) throw new IOException(c.Error ?? "La caméra ne fournit pas d’image.");
        }
        foreach (var lecteur in annonces.SelectMany(a => new[] { a.Premier, a.Second }).OfType<SourcesDisplay>())
        {
            var until = Stopwatch.StartNew();
            while (lecteur.Image == null && lecteur.Statut == "Connexion à la source…" && until.ElapsedMilliseconds < 8000) await Task.Delay(50);
            if (lecteur.Image == null) throw new IOException(lecteur.Statut is { Length: > 0 } st && st != "Connexion à la source…" ? st : "Une source Display de l'enregistrement ne fournit pas d’image.");
        }
        Render();
    }
    public void Start() { clock.Restart(); timer.Start(); }
    void Render()
    {
        if (disposed) return;
        try
        {
            foreach (var view in cameraViews) { if (view.Camera.Error != null) throw new IOException(view.Camera.Error); view.Image.Source = view.Camera.Image; }
            foreach (var p in projections) { p.Afficher(App.Regie.Etat); p.ImageDisplay(App.Sources.Image); p.ImageDuo(App.SourcesDuo.Image); }
            // une fois par seconde, la source Display suit ses modifications (texte, thème) ; ses images vivantes à chaque image
            bool relire = ++tours % imagesParSeconde == 0;
            foreach (var a in annonces)
            {
                if (relire && App.Regie.EtatAnnonce(a.Annonce) is { } etat) a.Rendu.Afficher(etat);
                if (a.Premier != null) a.Rendu.ImageDisplay(a.Premier.Image);
                if (a.Second != null) a.Rendu.ImageDuo(a.Second.Image);
            }
            foreach (var v in videos)
            {
                double desired = Math.Floor(clock.Elapsed.TotalSeconds * v.Fps);
                // Sequential decode for normal frames, seek only on significant UI delay.
                if (v.Video.PosFrames > desired) continue;
                if (Math.Abs(v.Video.PosFrames - desired) > 3) v.Video.PosFrames = (int)desired;
                if (v.Video.Read(v.Frame) && !v.Frame.Empty())
                {
                    Cv2.CvtColor(v.Frame, v.Bgra, ColorConversionCodes.BGR2BGRA);
                    var b = BitmapSource.Create(v.Bgra.Width, v.Bgra.Height, 96, 96, PixelFormats.Bgra32, null, v.Bgra.Data, (int)(v.Bgra.Step() * v.Bgra.Height), (int)v.Bgra.Step()); b.Freeze(); v.Image.Source = b;
                }
            }
            canvas.UpdateLayout(); bitmap.Clear(); bitmap.Render(canvas);
            lock (frameGate) bitmap.CopyPixels(frame, width * 4, 0);
        }
        catch (Exception e) { Error = e.Message; timer.Stop(); }
    }
    public void CopyFrame(byte[] destination) { lock (frameGate) Buffer.BlockCopy(frame, 0, destination, 0, frame.Length); }
    public void Dispose()
    {
        disposed = true; timer.Stop(); foreach (var c in cameras) c.Dispose(); cameras.Clear();
        foreach (var v in videos) { v.Video.Dispose(); v.Frame.Dispose(); v.Bgra.Dispose(); } videos.Clear();
        foreach (var a in annonces) { a.Premier?.Dispose(); a.Second?.Dispose(); } annonces.Clear();
    }
}
