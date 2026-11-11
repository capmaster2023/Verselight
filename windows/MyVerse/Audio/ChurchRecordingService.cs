using System.Diagnostics;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public enum ChurchRecordingState { Ready, Preparing, Recording, Finalizing, Error }

/// <summary>
/// Les enregistrements créés par l'utilisateur (5 au plus), qui peuvent tourner en même temps.
/// Chacun reçoit le mélange de ses sources (une piste d'OBS) et, s'il a des sources Display, une vidéo composée (MKV).
/// </summary>
public sealed class ChurchRecordingService : IDisposable
{
    /// <summary>L'état d'un enregistrement (en cours ou non).</summary>
    sealed class Run
    {
        public readonly SemaphoreSlim Operation = new(1, 1);
        public RecordingSession? Session;
        public RecordingCompositor? Compositor;
        public RecordingProfile? Profile;
        public Action<float[]>? Consumer;
        public int Piste;
        /// <summary>Compte dans les pistes et les sourdines dès le démarrage, jusqu'à l'arrêt.</summary>
        public bool Reserve;
        public ChurchRecordingState State = ChurchRecordingState.Ready;
        public string Message = "Prêt";
        public string? LastFile;
        public string Encoder = "";
    }

    readonly ChurchAudioService audio;
    readonly Dictionary<string, Run> runs = new();
    static readonly Dictionary<string, string> encodeurs = new();
    public event Action? Changed;

    public ChurchRecordingService(ChurchAudioService audio) { this.audio = audio; }

    Run Get(string id)
    {
        lock (runs)
        {
            if (!runs.TryGetValue(id, out var r)) runs[id] = r = new Run();
            return r;
        }
    }

    /// <summary>Les enregistrements qui tournent ou qui démarrent (ils ont une piste d'OBS et appliquent « couper les autres sources »).</summary>
    public IReadOnlyCollection<string> Reserves { get { lock (runs) return runs.Where(r => r.Value.Reserve).Select(r => r.Key).ToList(); } }
    public bool Active(string id) => Get(id).Session != null;
    public bool AnyActive { get { lock (runs) return runs.Values.Any(r => r.Session != null); } }
    /// <summary>Un enregistrement tourne, démarre ou se finalise (à attendre avant de fermer MyChurch).</summary>
    public bool AnyBusy { get { lock (runs) return runs.Values.Any(r => r.Session != null || r.State is ChurchRecordingState.Preparing or ChurchRecordingState.Finalizing); } }
    public bool Busy(string id) => Get(id).State is ChurchRecordingState.Preparing or ChurchRecordingState.Finalizing;
    public ChurchRecordingState State(string id) => Get(id).State;
    public string Message(string id) => Get(id).Message;
    public string? LastFile(string id) => Get(id).LastFile;
    public string Encoder(string id) => Get(id).Encoder;
    public TimeSpan Duration(string id) => TimeSpan.FromSeconds((Get(id).Session?.AcceptedFrames ?? 0) / 48000d);
    public long Bytes(string id) => Get(id).Session?.Bytes ?? 0;

    void Status(Run r, ChurchRecordingState state, string message) { r.State = state; r.Message = message; Changed?.Invoke(); }

    /// <summary>Démarre ou arrête cet enregistrement (les autres continuent).</summary>
    public async Task ToggleAsync(RecordingProfile profile)
    {
        var run = Get(profile.Id);
        if (!await run.Operation.WaitAsync(0)) return;
        try
        {
            if (run.Session != null) { await StopCore(run); return; }
            Status(run, ChurchRecordingState.Preparing, "Vérification…");
            if (!audio.Running) throw new IOException("Le moteur audio OBS n'est pas démarré.");
            if (!audio.TracksActive) throw new IOException("Les pistes d'OBS sont indisponibles sur cet ordinateur (carte graphique) : enregistrement impossible.");
            if (profile.SourceIds.Count == 0 && profile.DisplayIds.Count == 0) throw new IOException("Cet enregistrement n'a aucune source : « Modifier » pour en choisir.");
            // réserver une piste d'OBS (et couper les autres sources si l'option est choisie)
            run.Reserve = true;
            audio.Apply(false);
            var actuel = audio.Config.Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? throw new IOException("Cet enregistrement a été supprimé.");
            var selected = AudioConfiguration.Copy(actuel);
            if (!selected.AudioOnly) selected.Visuals = selected.VisuelsDisplay();
            RecordingFiles.Validate(selected);
            Directory.CreateDirectory(selected.Directory);
            var probe = Path.Combine(selected.Directory, ".mychurch-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            if (RecordingFiles.FreeSpace(selected.Directory) < RecordingFiles.ReserveBytes) throw new IOException("Au moins 512 Mio libres sont nécessaires.");
            string encoder = "PCM 24 bits";
            if (!selected.AudioOnly || selected.Format != "wav") encoder = await ProbeEncoderAsync(audio.Ffmpeg, selected);
            RecordingCompositor? compositor = null;
            try
            {
                if (!selected.AudioOnly) { compositor = new(selected); await compositor.ValidateAsync(); }
                Status(run, ChurchRecordingState.Preparing, "Préparation de l'enregistrement…");
                string path = RecordingFiles.ReservePath(selected.Directory, selected.Name, selected.Format);
                run.LastFile = path;
                var session = new RecordingSession(selected, path, audio.Ffmpeg, encoder, compositor);
                try
                {
                    await session.PrepareAsync();
                    compositor?.Start();
                    run.Compositor = compositor; compositor = null;
                    run.Profile = selected; run.Encoder = encoder;
                    Volatile.Write(ref run.Session, session);
                    run.Consumer = block => Volatile.Read(ref run.Session)?.Accept(block);
                    run.Piste = selected.Track;
                    // la piste d'OBS de cet enregistrement alimente le fichier, bloc par bloc, depuis le fil audio d'OBS
                    audio.AddTrackConsumer(run.Piste - 1, run.Consumer);
                    Status(run, ChurchRecordingState.Recording, selected.AudioOnly ? $"Enregistrement · {selected.Format.ToUpperInvariant()}" : "Enregistrement vidéo · " + encoder);
                }
                catch { session.Dispose(); throw; }
            }
            finally { compositor?.Dispose(); }
        }
        catch (Exception e)
        {
            if (run.Session == null && run.Reserve) { run.Reserve = false; audio.Apply(false); }
            Status(run, ChurchRecordingState.Error, "Impossible de démarrer : " + e.Message);
            Journal.Erreur("enregistrement", e);
        }
        finally { run.Operation.Release(); }
    }

    /// <summary>Après chaque changement de réglages : un enregistrement en cours suit sa piste d'OBS si elle a changé
    /// (sources ajoutées ou retirées pendant l'enregistrement).</summary>
    public void SuivrePistes()
    {
        List<(Run Run, RecordingProfile Profil)> actifs;
        lock (runs) actifs = runs.Where(r => r.Value.Session != null && r.Value.Consumer != null)
            .Select(r => (r.Value, audio.Config.Profiles.FirstOrDefault(p => p.Id == r.Key)!)).Where(x => x.Item2 != null).ToList();
        foreach (var (run, profil) in actifs)
        {
            if (profil.Track <= 0 || profil.Track == run.Piste) continue;
            audio.RemoveTrackConsumer(run.Piste - 1, run.Consumer!);
            run.Piste = profil.Track;
            audio.AddTrackConsumer(run.Piste - 1, run.Consumer!);
        }
    }

    static async Task<string> ProbeEncoderAsync(string executable, RecordingProfile profile)
    {
        if (!File.Exists(executable)) throw new IOException("ffmpeg est introuvable (dossier « ffmpeg » de MyChurch). Réinstallez MyChurch ou choisissez un ffmpeg.exe dans les Paramètres.");
        if (profile.AudioOnly)
        {
            await TestAsync(executable, new[] { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "0.1", "-c:a", "flac", "-f", "null", "-" });
            return "flac";
        }
        // « auto » : la carte graphique d'abord (NVIDIA, Intel, AMD), puis x264, puis MJPEG ; le résultat est gardé pour la séance
        var allowed = new[] { "h264_nvenc", "h264_qsv", "h264_amf", "libx264", "mjpeg" };
        var candidates = (allowed.Contains(profile.Encoder) ? new[] { profile.Encoder, "mjpeg" } : allowed).Distinct().ToArray();
        string cle = executable + "|" + profile.Width + "x" + profile.Height + "@" + profile.Fps + "|" + string.Join(",", candidates);
        lock (encodeurs) if (encodeurs.TryGetValue(cle, out var connu)) return connu;
        foreach (string candidate in candidates)
        {
            try
            {
                await TestAsync(executable, new[] { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", $"color=c=black:s={profile.Width}x{profile.Height}:r={profile.Fps}", "-frames:v", "3", "-c:v", candidate, "-pix_fmt", candidate == "mjpeg" ? "yuvj420p" : "yuv420p", "-f", "null", "-" });
                lock (encodeurs) encodeurs[cle] = candidate;
                return candidate;
            }
            catch (Exception) when (candidate != "mjpeg") { }
        }
        throw new IOException("Aucun encodeur vidéo n'a réussi le test réel (y compris MJPEG).");
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

    /// <summary>Toutes les demi-secondes : un enregistrement en erreur (disque plein, encodeur, caméra) est arrêté proprement.</summary>
    public async Task PollAsync()
    {
        List<Run> enErreur;
        lock (runs) enErreur = runs.Values.Where(r => r.Session?.Error != null || r.Compositor?.Error != null).ToList();
        foreach (var run in enErreur)
        {
            if (!await run.Operation.WaitAsync(0)) continue;
            try { if (run.Compositor?.Error is { } error) run.Session?.Fail(error); await StopCore(run); }
            finally { run.Operation.Release(); }
        }
    }

    async Task StopCore(Run run)
    {
        if (run.Consumer != null && run.Piste > 0) audio.RemoveTrackConsumer(run.Piste - 1, run.Consumer);
        var current = Interlocked.Exchange(ref run.Session, null);
        run.Reserve = false;
        audio.Apply(false); // la piste se libère, les sources coupées par cet enregistrement reviennent
        if (current == null) return;
        Status(run, ChurchRecordingState.Finalizing, "Finalisation…");
        try { await current.StopAsync(); Status(run, ChurchRecordingState.Ready, "Terminé : " + current.Path); }
        catch (Exception e) { Status(run, ChurchRecordingState.Error, "Fichier partiel conservé : " + e.Message); Journal.Erreur("finalisation", e); }
        finally
        {
            current.Dispose();
            run.Compositor?.Dispose(); run.Compositor = null;
            run.Profile = null; run.Consumer = null; run.Piste = 0;
        }
    }

    /// <summary>Arrête un enregistrement (s'il tourne).</summary>
    public async Task StopAsync(string id)
    {
        var run = Get(id);
        await run.Operation.WaitAsync();
        try { await StopCore(run); } finally { run.Operation.Release(); }
    }

    /// <summary>Arrête tous les enregistrements (fermeture de MyChurch) : chaque fichier est finalisé.</summary>
    public async Task StopAllAsync()
    {
        List<string> ids;
        lock (runs) ids = runs.Keys.ToList();
        foreach (var id in ids) await StopAsync(id);
    }

    public void Dispose()
    {
        List<Run> tous;
        lock (runs) tous = runs.Values.ToList();
        foreach (var run in tous)
        {
            if (run.Consumer != null && run.Piste > 0) audio.RemoveTrackConsumer(run.Piste - 1, run.Consumer);
            var current = Interlocked.Exchange(ref run.Session, null);
            if (current != null)
            {
                try { current.StopAsync().GetAwaiter().GetResult(); }
                catch (Exception e) { Journal.Erreur("finalisation à la fermeture", e); }
                current.Dispose();
            }
            run.Compositor?.Dispose(); run.Compositor = null;
        }
    }
}
