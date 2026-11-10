using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MyVerse.Audio;

public interface IRecordingFrameProvider { void CopyFrame(byte[] destination); }

/// <summary>Bounded producer queue. The audio renderer only copies to a preallocated slot.
/// Encoding, pipes, disk and finalization are handled by dedicated workers.</summary>
public sealed class RecordingSession : IDisposable
{
    const int Slots = 200;
    readonly float[][] blocks = Enumerable.Range(0, Slots).Select(_ => new float[AudioEngine.Samples]).ToArray();
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
    long writtenFrames;
    volatile string? error;
    public string? Error => error;
    public long AcceptedFrames { get; private set; }
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
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NamedPipeServerStream, PipeName> names = new();
    sealed record PipeName(string Value);
    static NamedPipeServerStream NamedPipe()
    {
        string name = "mychurch-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536); names.Add(pipe, new(name)); return pipe;
    }
    static string PipePath(NamedPipeServerStream pipe) => @"\\.\pipe\" + names.GetValue(pipe, _ => throw new InvalidOperationException()).Value;
    public static IReadOnlyList<string> FfmpegArguments(RecordingProfile profile, string audioInput, string? videoInput, string encoder, string output)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostdin", "-y", "-thread_queue_size", "256" };
        if (videoInput != null) args.AddRange(new[] { "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{profile.Width}x{profile.Height}", "-framerate", profile.Fps.ToString(), "-probesize", "32", "-analyzeduration", "0", "-i", videoInput, "-thread_queue_size", "256" });
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
        if (audio.Length != AudioEngine.Samples) throw new ArgumentException("Un bloc stéréo de 480 trames est requis.");
        lock (gate)
        {
            if (finished || error != null) return false;
            if (count == Slots) { error = "L’encodeur ne suit plus : tampon d’enregistrement plein. Le fichier est en cours de finalisation."; finished = true; available.Release(); return false; }
            audio.CopyTo(blocks[(read + count) % Slots]); count++; AcceptedFrames += audio.Length / 2; available.Release(); return true;
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
        var block = new float[AudioEngine.Samples]; var bytes = new byte[AudioEngine.Samples * 4]; PcmRecordingWriter? wav = null;
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
                int primeFrames = videoPipe == null ? 0 : 48000 / profile.Fps;
                if (primeFrames > 0) await audioPipe.WriteAsync(new byte[primeFrames * 8], cancel.Token);
                Interlocked.Exchange(ref writtenFrames, primeFrames);
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
                else { Buffer.BlockCopy(block, 0, bytes, 0, bytes.Length); await audioPipe!.WriteAsync(bytes, cancel.Token); Interlocked.Add(ref writtenFrames, AudioEngine.Frames); }
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
        byte[] frame = new byte[profile.Width * profile.Height * 4];
        long sent = 0;
        // Prime the video input immediately so FFmpeg proceeds to open the audio pipe.
        compositor!.CopyFrame(frame); await videoPipe.WriteAsync(frame, cancel.Token); sent++;
        while (!cancel.IsCancellationRequested)
        {
            long wanted = (Interlocked.Read(ref writtenFrames) * profile.Fps + 47999) / 48000;
            if (wanted - sent > profile.Fps * 2) { Fail("L’encodeur vidéo ne suit plus le son. Arrêt pour préserver la synchronisation."); break; }
            if (sent < wanted) { compositor.CopyFrame(frame); await videoPipe.WriteAsync(frame, cancel.Token); sent++; }
            else { if (audioDone) break; await Task.Delay(2, cancel.Token); }
        }
        videoPipe.Dispose();
    }
    void Kill() { try { if (ffmpeg is { HasExited: false }) ffmpeg.Kill(true); } catch (Exception) { } }
    public void Dispose() { Finish(); cancel.Cancel(); Kill(); audioPipe?.Dispose(); videoPipe?.Dispose(); ffmpeg?.Dispose(); /* Workers own queue wait lifetime. */ }
}
