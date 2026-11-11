using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MyVerse.Audio;

public interface IRecordingFrameProvider { void CopyFrame(byte[] destination); }

/// <summary>Bounded producer queue. The audio renderer only copies to a preallocated slot.
/// Encoding, pipes, disk and finalization are handled by dedicated workers.</summary>
public sealed class RecordingSession : IDisposable
{
    // 5 s de son en attente : le temps qu'un encodeur avec retard (x264, carte graphique) sorte ses premières images
    const int Slots = 500;
    readonly float[][] blocks = Enumerable.Range(0, Slots).Select(_ => new float[AudioBlock.Samples]).ToArray();
    readonly object gate = new();
    readonly SemaphoreSlim available = new(0);
    readonly CancellationTokenSource cancel = new();
    int read, count;
    bool finished;
    volatile bool audioDone;
    readonly RecordingProfile profile;
    readonly string executable;
    readonly IRecordingFrameProvider? compositor;
    readonly NamedPipeServerStream? audioPipe, videoPipe;
    Process? ffmpeg;
    Task? worker, videoWorker, stderrWorker;
    readonly StringBuilder stderr = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Trames de son reçues (pas forcément encore écrites pour ffmpeg) : c'est l'horloge de la vidéo.</summary>
    long accepted;
    /// <summary>Silence d'amorçage (une image) avant le premier bloc reçu.</summary>
    readonly int primeFrames;
    volatile string? error;
    public string? Error => error;
    public long AcceptedFrames => Interlocked.Read(ref accepted);
    public long Bytes => File.Exists(Path) ? new FileInfo(Path).Length : 0;
    public string Path { get; }
    public string Encoder { get; }
    public Task Completion => worker ?? Task.CompletedTask;
    public RecordingSession(RecordingProfile profile, string path, string executable, string encoder, IRecordingFrameProvider? compositor)
    {
        this.profile = profile; Path = path; this.executable = executable; Encoder = encoder; this.compositor = compositor;
        if (!(profile.AudioOnly && profile.Format == "wav"))
        {
            audioPipe = NamedPipe(); if (!profile.AudioOnly) videoPipe = NamedPipe();
        }
        primeFrames = videoPipe == null ? 0 : 48000 / profile.Fps;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NamedPipeServerStream, PipeName> names = new();
    sealed record PipeName(string Value);
    static NamedPipeServerStream NamedPipe()
    {
        string name = "mychurch-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536); names.Add(pipe, new(name)); return pipe;
    }
    /// <summary>Le chemin que ffmpeg ouvre : un tube nommé sous Windows, la prise Unix de .NET ailleurs (essais sous Linux).</summary>
    static string PipePath(NamedPipeServerStream pipe)
    {
        string nom = names.GetValue(pipe, _ => throw new InvalidOperationException()).Value;
        return OperatingSystem.IsWindows() ? @"\\.\pipe\" + nom : "unix:" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoreFxPipe_" + nom);
    }
    public static IReadOnlyList<string> FfmpegArguments(RecordingProfile profile, string audioInput, string? videoInput, string encoder, string output)
    {
        // pas de « -thread_queue_size » sur les entrées : les ffmpeg récents le refusent, et la vidéo suit le son reçu
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostdin", "-y" };
        if (videoInput != null) args.AddRange(new[] { "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{profile.Width}x{profile.Height}", "-framerate", profile.Fps.ToString(), "-probesize", "32", "-analyzeduration", "0", "-i", videoInput });
        args.AddRange(new[] { "-f", "f32le", "-ar", "48000", "-ac", "2", "-probesize", "32", "-analyzeduration", "0", "-i", audioInput });
        if (videoInput != null)
        {
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0", "-c:v", encoder, "-b:v", profile.VideoKbps + "k", "-pix_fmt", encoder == "mjpeg" ? "yuvj420p" : "yuv420p", "-c:a", "aac", "-b:a", profile.AudioKbps + "k", "-cluster_time_limit", "1000", "-f", "matroska" });
            if (encoder == "libx264") args.AddRange(new[] { "-preset", "veryfast" });
        }
        else args.AddRange(new[] { "-c:a", "flac", "-f", "flac" });
        args.Add(output); return args;
    }
    public async Task PrepareAsync()
    {
        worker = Task.Run(WriteAsync);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    public bool Accept(ReadOnlySpan<float> audio)
    {
        if (audio.Length != AudioBlock.Samples) throw new ArgumentException("Un bloc stéréo de 480 trames est requis.");
        lock (gate)
        {
            if (finished || error != null) return false;
            if (count == Slots) { error = "L’encodeur ne suit plus : tampon d’enregistrement plein. Le fichier est en cours de finalisation."; finished = true; available.Release(); return false; }
            audio.CopyTo(blocks[(read + count) % Slots]); count++; Interlocked.Add(ref accepted, audio.Length / 2); available.Release(); return true;
        }
    }
    public void Fail(string message) { error = message; Finish(); }
    public void Finish() { lock (gate) { if (finished) return; finished = true; available.Release(); } }
    public async Task StopAsync()
    {
        Finish();
        if (worker != null)
        {
            try { await worker.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { error = "Finalisation trop longue. Le fichier partiel est conservé."; cancel.Cancel(); Kill(); await worker.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        if (error != null) throw new IOException(error);
    }
    async Task WriteAsync()
    {
        var block = new float[AudioBlock.Samples]; var bytes = new byte[AudioBlock.Samples * 4]; PcmRecordingWriter? wav = null;
        try
        {
            if (audioPipe == null) { wav = new(Path); ready.TrySetResult(); }
            else
            {
                var args = FfmpegArguments(profile, PipePath(audioPipe), videoPipe == null ? null : PipePath(videoPipe), Encoder, Path);
                var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var a in args) info.ArgumentList.Add(a);
                ffmpeg = Process.Start(info) ?? throw new IOException("FFmpeg n’a pas démarré.");
                stderrWorker = Task.Run(async () => { string? line; while ((line = await ffmpeg.StandardError.ReadLineAsync()) != null) lock (stderr) { if (stderr.Length > 12000) stderr.Remove(0, 6000); stderr.AppendLine(line); } });
                if (videoPipe != null) videoWorker = Task.Run(WriteVideoAsync);
                await audioPipe.WaitForConnectionAsync(cancel.Token);
                // A silent priming interval lets FFmpeg open both pipes before the allow-list is activated.
                if (primeFrames > 0) await audioPipe.WriteAsync(new byte[primeFrames * 8], cancel.Token);
                // Synchro : le son est retardé de AudioDelayMs (caméra en retard). Ce silence n'avance pas la vidéo,
                // qui reste cadencée par le son réellement reçu : tout le son glisse d'autant par rapport à l'image.
                int delayFrames = videoPipe == null ? 0 : Math.Clamp(profile.AudioDelayMs, 0, 1000) * 48;
                if (delayFrames > 0) await audioPipe.WriteAsync(new byte[delayFrames * 8], cancel.Token);
                ready.TrySetResult();
            }
            long lastDiskCheck = 0;
            while (true)
            {
                await available.WaitAsync(cancel.Token);
                bool got;
                lock (gate)
                {
                    got = count > 0;
                    if (got) { blocks[read].CopyTo(block, 0); read = (read + 1) % Slots; count--; }
                    else if (finished) break;
                }
                if (!got) continue;
                if (wav != null) wav.Write(block);
                else { Buffer.BlockCopy(block, 0, bytes, 0, bytes.Length); await audioPipe!.WriteAsync(bytes, cancel.Token); }
                if (Environment.TickCount64 - lastDiskCheck > 1000)
                {
                    lastDiskCheck = Environment.TickCount64;
                    if (RecordingFiles.FreeSpace(profile.Directory) < RecordingFiles.ReserveBytes) { Fail("Espace disque faible : arrêt et finalisation du fichier."); }
                    if (ffmpeg?.HasExited == true) throw new IOException("FFmpeg s’est arrêté : " + stderr);
                }
            }
            audioDone = true;
            wav?.Dispose(); wav = null;
            audioPipe?.Dispose();
            if (videoWorker != null) await videoWorker.WaitAsync(TimeSpan.FromSeconds(10));
            if (ffmpeg != null)
            {
                await ffmpeg.WaitForExitAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(15));
                if (stderrWorker != null) await stderrWorker;
                if (ffmpeg.ExitCode != 0) throw new IOException("Encodage : " + stderr);
            }
        }
        catch (Exception e) { error ??= e.Message; ready.TrySetException(e); Kill(); }
        finally { audioDone = true; wav?.Dispose(); audioPipe?.Dispose(); videoPipe?.Dispose(); lock (gate) finished = true; }
    }
    async Task WriteVideoAsync()
    {
        await videoPipe!.WaitForConnectionAsync(cancel.Token);
        int size = profile.Width * profile.Height * 4;
        // Synchro négative : l'image est retardée de −AudioDelayMs (son en retard sur l'image) grâce à une file d'images.
        int delay = Math.Clamp(-profile.AudioDelayMs, 0, 500) * profile.Fps / 1000;
        var queue = new Queue<byte[]>();
        var spare = new Stack<byte[]>();
        long sent = 0;
        async Task SendAsync()
        {
            var image = spare.Count > 0 ? spare.Pop() : new byte[size];
            compositor!.CopyFrame(image);
            queue.Enqueue(image);
            // tant que la file n'est pas pleine, la plus ancienne image est répétée
            var output = queue.Count > delay ? queue.Dequeue() : queue.Peek();
            await videoPipe.WriteAsync(output, cancel.Token);
            if (!queue.Contains(output)) spare.Push(output);
            sent++;
        }
        // Prime the video input immediately so FFmpeg proceeds to open the audio pipe.
        await SendAsync();
        // L'image suit le son REÇU, pas le son déjà lu par ffmpeg : ffmpeg peut retenir le son tant qu'un encodeur
        // avec retard (x264, carte graphique) n'a pas sorti ses premières images ; si la vidéo attendait le son écrit,
        // les deux s'attendraient l'un l'autre (fichier vide). Pas de dérive : une image tous les 48000 / ips trames reçues.
        while (!cancel.IsCancellationRequested)
        {
            // lu avant le compte : une fois l'arrêt vu, plus aucun bloc ne s'ajoute, le compte est définitif
            bool fini = Volatile.Read(ref finished) || audioDone;
            long wanted = ((primeFrames + Interlocked.Read(ref accepted)) * profile.Fps + 47999) / 48000;
            if (wanted - sent > profile.Fps * 2) { Fail("L’encodeur vidéo ne suit plus le son. Arrêt pour préserver la synchronisation."); break; }
            if (sent < wanted) await SendAsync();
            // plus aucun son ne sera reçu : la vidéo se termine tout de suite (l'encodeur vide ses images en attente),
            // sans attendre que ffmpeg ait lu tout le son, qu'il retient tant que la vidéo n'a pas fini
            else if (fini) break;
            else await Task.Delay(2, cancel.Token);
        }
        videoPipe.Dispose();
    }
    void Kill() { try { if (ffmpeg is { HasExited: false }) ffmpeg.Kill(true); } catch (Exception) { } }
    public void Dispose() { Finish(); cancel.Cancel(); Kill(); audioPipe?.Dispose(); videoPipe?.Dispose(); ffmpeg?.Dispose(); /* Workers own queue wait lifetime. */ }
}
