using System.Diagnostics;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public enum ChurchRecordingState { Ready, Preparing, Recording, Finalizing, Error }
public sealed class ChurchRecordingService : IDisposable
{
    readonly ChurchAudioService audio;
    readonly SemaphoreSlim operation = new(1, 1);
    RecordingSession? session;
    RecordingCompositor? compositor;
    RecordingProfile? active;
    public RecordingProfile? ActiveProfile => active;
    public string? SelectedProfileId { get; private set; }
    public ChurchRecordingState State { get; private set; }
    public string Message { get; private set; } = "Prêt";
    public string? LastFile { get; private set; }
    public bool Active => session != null;
    public bool Busy => State is ChurchRecordingState.Preparing or ChurchRecordingState.Finalizing;
    public TimeSpan Duration => TimeSpan.FromSeconds((session?.AcceptedFrames ?? 0) / 48000d);
    public event Action? Changed;
    readonly Action<float[]> consommateur;
    public ChurchRecordingService(ChurchAudioService audio) { this.audio = audio; consommateur = block => Volatile.Read(ref session)?.Accept(block); }
    void Status(ChurchRecordingState state, string message) { State = state; Message = message; Changed?.Invoke(); }
    public async Task ToggleAsync(RecordingProfile profile)
    {
        if (!await operation.WaitAsync(0)) return;
        try
        {
            if (active?.Id == profile.Id) { await StopCore(); return; }
            Status(ChurchRecordingState.Preparing, "Vérification du profil…");
            // Validate the candidate before stopping the old recording.
            var selected = AudioConfiguration.Copy(profile); RecordingFiles.Validate(selected);
            if (!audio.Running) throw new IOException("Le moteur audio OBS n’est pas démarré.");
            if (!audio.TracksActive) throw new IOException("Les pistes d’OBS sont indisponibles sur cet ordinateur (carte graphique) : enregistrement impossible.");
            if (!audio.Config.Sources.Any(s => (s.Tracks & (1 << (selected.Track - 1))) != 0)) throw new IOException($"Aucune source n’est envoyée dans la piste {selected.Track}.");
            Directory.CreateDirectory(selected.Directory);
            var probe = Path.Combine(selected.Directory, ".mychurch-write-" + Guid.NewGuid().ToString("N")); using (File.Create(probe)) { } File.Delete(probe);
            if (RecordingFiles.FreeSpace(selected.Directory) < RecordingFiles.ReserveBytes) throw new IOException("Au moins 512 Mio libres sont nécessaires.");
            string encoder = "PCM 24 bits";
            if (!selected.AudioOnly || selected.Format != "wav") encoder = await ProbeEncoderAsync(audio.Config.FfmpegPath, selected);
            // Prepare a separate composition before modifying the current session.
            RecordingCompositor? nextCompositor = null;
            try
            {
                if (!selected.AudioOnly) { nextCompositor = new(selected); await nextCompositor.ValidateAsync(); }
                if (session != null) await StopCore();
                SelectedProfileId = selected.Id; Status(ChurchRecordingState.Preparing, "Préparation de l’enregistrement…");
                string path = RecordingFiles.ReservePath(selected.Directory, selected.Name, selected.Format); LastFile = path;
                var prepared = new RecordingSession(selected, path, audio.Config.FfmpegPath, encoder, nextCompositor);
                try
                {
                    await prepared.PrepareAsync();
                    nextCompositor?.Start();
                    compositor = nextCompositor; nextCompositor = null; active = selected; Volatile.Write(ref session, prepared);
                    // la piste choisie d'OBS alimente le fichier, bloc par bloc, depuis le fil audio d'OBS
                    audio.AddTrackConsumer(selected.Track - 1, consommateur);
                    Status(ChurchRecordingState.Recording, $"Enregistrement · piste {selected.Track} · " + encoder + (!selected.AudioOnly && encoder != selected.Encoder ? " (repli : encodeur demandé indisponible)" : ""));
                }
                catch { prepared.Dispose(); active = null; throw; }
            }
            finally { nextCompositor?.Dispose(); }
        }
        catch (Exception e)
        {
            if (session != null && active != null) Status(ChurchRecordingState.Recording, "Profil refusé, enregistrement précédent maintenu : " + e.Message);
            else { Status(ChurchRecordingState.Error, "Aucun enregistrement actif : " + e.Message); }
            Journal.Erreur("enregistrement", e);
        }
        finally { operation.Release(); }
    }
    static async Task<string> ProbeEncoderAsync(string executable, RecordingProfile profile)
    {
        if (!File.Exists(executable)) throw new IOException("Sélectionnez un exécutable FFmpeg dans les réglages avancés. Aucun codec ni pilote n’est installé automatiquement.");
        if (profile.AudioOnly)
        {
            await TestAsync(executable, new[] { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "0.1", "-c:a", "flac", "-f", "null", "-" }); return "flac";
        }
        var allowed = new[] { "libx264", "h264_nvenc", "h264_qsv", "h264_amf", "mjpeg" };
        var candidates = new[] { allowed.Contains(profile.Encoder) ? profile.Encoder : "mjpeg", "mjpeg" }.Distinct();
        foreach (string candidate in candidates)
        {
            try
            {
                await TestAsync(executable, new[] { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", $"color=c=black:s={profile.Width}x{profile.Height}:r={profile.Fps}", "-frames:v", "3", "-c:v", candidate, "-pix_fmt", candidate == "mjpeg" ? "yuvj420p" : "yuv420p", "-f", "null", "-" });
                return candidate;
            }
            catch (Exception) when (candidate != "mjpeg") { }
        }
        throw new IOException("Aucun encodeur compatible n’a réussi le test réel (y compris MJPEG).");
    }
    public static async Task TestAsync(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("FFmpeg indisponible");
        var errors = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); }
        catch { try { process.Kill(true); } catch (Exception) { } throw; }
        await output; string text = await errors;
        if (process.ExitCode != 0) throw new IOException(text.Length > 2000 ? text[^2000..] : text);
    }
    public void Fail(string message) => Volatile.Read(ref session)?.Fail(message);
    public async Task PollAsync()
    {
        if (session?.Error == null && compositor?.Error == null) return;
        if (!await operation.WaitAsync(0)) return;
        try { if (compositor?.Error is { } error) session?.Fail(error); await StopCore(); }
        finally { operation.Release(); }
    }
    async Task StopCore()
    {
        if (active != null) audio.RemoveTrackConsumer(active.Track - 1, consommateur);
        var current = Interlocked.Exchange(ref session, null);
        if (current == null) return;
        Status(ChurchRecordingState.Finalizing, "Finalisation…");
        try { await current.StopAsync(); Status(ChurchRecordingState.Ready, "Terminé : " + current.Path); }
        catch (Exception e) { Status(ChurchRecordingState.Error, "Fichier partiel conservé : " + e.Message); Journal.Erreur("finalisation", e); }
        finally { current.Dispose(); compositor?.Dispose(); compositor = null; active = null; }
    }
    public async Task StopAsync() { await operation.WaitAsync(); try { await StopCore(); } finally { operation.Release(); } }
    public void Dispose()
    {
        if (active != null) audio.RemoveTrackConsumer(active.Track - 1, consommateur);
        var current = Interlocked.Exchange(ref session, null);
        if (current != null) { try { current.StopAsync().GetAwaiter().GetResult(); } catch (Exception e) { Journal.Erreur("finalisation à la fermeture", e); } current.Dispose(); }
        compositor?.Dispose(); active = null;
    }
}
