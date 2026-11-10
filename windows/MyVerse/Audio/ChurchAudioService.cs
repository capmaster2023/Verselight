using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Le son de MyChurch 3 : le moteur d'OBS Studio (libobs), livré dans le dossier « obs » à côté de MyChurch.exe.
/// Cette classe garde les réglages, démarre le moteur et lui transmet chaque changement ; tout le traitement est fait par OBS.
/// </summary>
public sealed class ChurchAudioService : IDisposable
{
    readonly ObsMixer mixer = new();
    readonly System.Windows.Threading.DispatcherTimer sauvegarde = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly System.Windows.Threading.DispatcherTimer veille = new() { Interval = TimeSpan.FromMilliseconds(500) };
    string videoProjetee = "";
    public AudioConfiguration Config { get; }
    public ChurchRecordingService Recorder { get; }
    public VirtualSound VirtualSound { get; }
    public string Diagnostic { get; private set; } = "Moteur audio OBS : démarrage…";
    public bool Running => mixer.Running;
    public bool TracksActive => mixer.TracksActive;
    public string Version => mixer.Version;
    public event Action? Changed;

    public static string DossierObs => Path.Combine(AppContext.BaseDirectory, "obs");

    public ChurchAudioService(AudioConfiguration config)
    {
        Config = config;
        Config.Normalize();
        Recorder = new(this);
        VirtualSound = new(this);
        sauvegarde.Tick += (_, _) => { sauvegarde.Stop(); try { App.Donnees.SauverReglages(); } catch (Exception e) { Journal.Erreur("réglages audio", e); } };
        veille.Tick += async (_, _) => { try { await Recorder.PollAsync(); } catch (Exception e) { Journal.Erreur("enregistrement", e); } };
        veille.Start();
        App.Regie.Change += ProjectionChange;
        ObsMixer.Log += (niveau, texte) =>
        {
            if (niveau <= 100) Journal.Info("OBS (erreur) : " + texte.Trim());
            else if (niveau <= 200) Journal.Info("OBS : " + texte.Trim());
        };
    }

    /// <summary>Démarre le moteur OBS puis recrée les sources enregistrées. Sans OBS, le reste de MyChurch fonctionne normalement.</summary>
    public void Start()
    {
        if (mixer.Running) return;
        try
        {
            mixer.Start(ObsPaths.Windows(DossierObs));
            Apply(false);
            Diagnostic = $"Moteur OBS {mixer.Version}" + (mixer.TracksActive ? "" : " · pistes indisponibles (carte graphique)");
            Journal.Info($"audio : libobs {mixer.Version}, modules {string.Join(", ", mixer.Modules)}, pistes {(mixer.TracksActive ? "actives" : "inactives")}");
        }
        catch (Exception e)
        {
            Diagnostic = "Moteur audio OBS indisponible : " + e.Message;
            Journal.Erreur("moteur audio OBS", e);
        }
        Changed?.Invoke();
    }

    /// <summary>Le son des vidéos de Display : les sources « vidéo projetée » suivent ce qui part à l'écran.</summary>
    void ProjectionChange()
    {
        var e = App.Regie.Etat;
        string chemin = e.Mode == "media" && e.Media?.Type == "video" ? e.Media.Chemin ?? "" : "";
        if (chemin == videoProjetee) return;
        videoProjetee = chemin;
        if (Config.Sources.Any(s => s.Kind == AudioInputKind.Media && s.FollowProjection)) Apply(false);
    }

    /// <summary>Transmet les réglages à OBS tout de suite ; l'écriture sur disque suit un instant plus tard.</summary>
    public void Apply(bool sauverMaintenant = true)
    {
        Config.Normalize();
        foreach (var s in Config.Sources.Where(s => s.Kind == AudioInputKind.Media && s.FollowProjection)) s.MediaPath = videoProjetee;
        sauvegarde.Stop(); sauvegarde.Start();
        try { mixer.Apply(Config); }
        catch (Exception e) { Diagnostic = "OBS : " + e.Message; Journal.Erreur("réglages audio OBS", e); }
        VirtualSound.Apply(Config.VirtualSound);
        Changed?.Invoke();
    }

    public List<AudioDevice> Microphones() { try { return mixer.Microphones(); } catch (Exception e) { Journal.Erreur("liste des micros", e); return new(); } }
    public List<AudioDevice> Outputs() { try { return mixer.Outputs(); } catch (Exception e) { Journal.Erreur("liste des sorties", e); return new(); } }
    public List<AudioDevice> MonitoringDevices() { try { return mixer.MonitoringDevices(); } catch (Exception e) { Journal.Erreur("liste des écoutes", e); return new(); } }
    public (float L, float R) Level(string id) => mixer.Level(id);
    public float TrackLevel(int piste) => mixer.TrackLevel(piste);
    public bool InputReady(string id) => mixer.Has(id);
    public void RestartMedia(AudioSource s) => mixer.RestartMedia(s.Id);
    internal void AddTrackConsumer(int piste, Action<float[]> c) => mixer.AddTrackConsumer(piste, c);
    internal void RemoveTrackConsumer(int piste, Action<float[]> c) => mixer.RemoveTrackConsumer(piste, c);

    public void Dispose()
    {
        sauvegarde.Stop(); veille.Stop();
        try { VirtualSound.Dispose(); } catch (Exception e) { Journal.Erreur("MyVirtualSound", e); }
        try { Recorder.Dispose(); } catch (Exception e) { Journal.Erreur("fin d'enregistrement", e); }
        try { mixer.Dispose(); } catch (Exception e) { Journal.Erreur("arrêt d'OBS", e); }
    }
}
