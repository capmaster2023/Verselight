using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Le son de MyChurch : le moteur d'OBS Studio (libobs), livré dans le dossier « obs » à côté de MyChurch.exe.
/// Cette classe garde les réglages, démarre le moteur et lui transmet chaque changement ; tout le traitement est fait par OBS.
/// MyChurch 4.2 : l'audio simplifié comme OBS, avec ses réglages par défaut (48 kHz, stéréo) ; chaque source coche ses pistes 1 à 6.
/// </summary>
public sealed class ChurchAudioService : IDisposable
{
    readonly ObsMixer mixer = new();
    readonly CaptureEntrees entrees;
    readonly System.Windows.Threading.DispatcherTimer sauvegarde = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly System.Windows.Threading.DispatcherTimer veille = new() { Interval = TimeSpan.FromMilliseconds(500) };
    /// <summary>Relevé des périphériques toutes les 2 s : une source dont le périphérique est débranché devient inactive, puis revient.</summary>
    readonly System.Windows.Threading.DispatcherTimer releve = new() { Interval = TimeSpan.FromSeconds(2) };
    List<AudioDevice> micros = new(), sortiesAudio = new();
    bool releveFait, releveEnCours, erreurReleve;
    string videoProjetee = "";
    public AudioConfiguration Config { get; }
    public ChurchRecordingService Recorder { get; }
    /// <summary>Les sorties MyVirtualSound (Zoom…).</summary>
    public SortiesVirtuelles Sorties { get; }
    public string Diagnostic { get; private set; } = "Moteur audio OBS : démarrage…";
    /// <summary>Le dossier « obs » manque ou est incomplet : le panneau propose de réparer (réinstaller MyChurch).</summary>
    public bool ObsAbsent { get; private set; }
    /// <summary>ffmpeg (FLAC et vidéo MKV), livré avec MyChurch.</summary>
    public string Ffmpeg => FfmpegInclus;
    public static string FfmpegInclus => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    public bool Running => mixer.Running;
    public bool TracksActive => mixer.TracksActive;
    public string Version => mixer.Version;
    public event Action? Changed;
    /// <summary>Un périphérique a été branché ou débranché (sources actives ou inactives, listes des propriétés).</summary>
    public event Action? PeripheriquesChange;

    /// <summary>Format du moteur, les réglages par défaut d'OBS.</summary>
    public string Format => "48 kHz · stéréo";

    public static string DossierObs => Path.Combine(AppContext.BaseDirectory, "obs");

    public ChurchAudioService(AudioConfiguration config)
    {
        Config = config;
        Config.Normalize();
        entrees = new CaptureEntrees(mixer.Pousser);
        Recorder = new(this);
        Sorties = new(this);
        releve.Tick += (_, _) => Relever();
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
        if (App.AutoTest == null)
        {
            Relever();
            releve.Start();
        }
        // MyChurch n'est jamais baissé par Windows pendant un appel (Zoom, Teams) : le son envoyé à Zoom reste au même niveau
        if (!AttenuationWindows.Desactiver(true)) Journal.Info("audio : atténuation de Windows non désactivée (aucune sortie audio ?)");
        var chemins = ObsPaths.Windows(DossierObs);
        if (!chemins.Present)
        {
            ObsAbsent = true;
            Diagnostic = "Moteur audio absent : le dossier « obs » de MyChurch manque ou est incomplet. « Réparer » réinstalle MyChurch (chants et réglages gardés).";
            Journal.Info("audio : fichier absent " + chemins.Library);
            Changed?.Invoke();
            return;
        }
        ObsAbsent = false;
        try
        {
            mixer.Start(chemins);
            Apply(false);
            Diagnostic = $"Moteur OBS {mixer.Version}" + (mixer.TracksActive ? "" : " · pistes indisponibles (carte graphique)");
            Journal.Info($"audio : libobs {mixer.Version}, {Format}, modules {string.Join(", ", mixer.Modules)}, pistes {(mixer.TracksActive ? "actives" : "inactives")}");
        }
        catch (Exception e)
        {
            Diagnostic = "Moteur audio OBS indisponible : " + (e is DllNotFoundException || e.Message.Contains("0x8007007E")
                ? "une bibliothèque du dossier « obs » ne se charge pas (fichier manquant ou bloqué par l'antivirus). « Réparer » réinstalle MyChurch."
                : e.Message);
            ObsAbsent = e is DllNotFoundException or BadImageFormatException;
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
        Sorties.Apply(Config.VirtualSounds);
        // entrées séparées : MyChurch écoute la carte son et donne à chaque source son canal
        try { entrees.Apply(mixer.EntreesSeparees ? Config.Sources : Enumerable.Empty<AudioSource>()); }
        catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        Recorder.SuivrePistes();
        Changed?.Invoke();
    }

    /// <summary>Micros et entrées (sans « par défaut ») : le dernier relevé de Windows, sinon la liste d'OBS.</summary>
    public List<AudioDevice> Microphones()
    {
        if (releveFait) return micros.ToList();
        try { return mixer.Microphones(); } catch (Exception e) { Journal.Erreur("liste des micros", e); return new(); }
    }
    public List<AudioDevice> Outputs()
    {
        if (releveFait) return sortiesAudio.ToList();
        try { return mixer.Outputs(); } catch (Exception e) { Journal.Erreur("liste des sorties", e); return new(); }
    }

    // ------------------------------------------------------------ périphériques branchés et débranchés
    /// <summary>
    /// Inactive (« Inactif » dans les propriétés audio avancées) : son périphérique est débranché (ou il n'y en a aucun), ou son fichier manque.
    /// Tant qu'aucun relevé n'est fait, rien n'est inactif.
    /// </summary>
    public bool Inactive(AudioSource s)
    {
        if (!releveFait) return false;
        return s.Kind switch
        {
            AudioInputKind.Microphone => s.DeviceId == "default" ? micros.Count == 0 : micros.All(d => d.Id != s.DeviceId),
            AudioInputKind.Sortie => s.DeviceId == "default" ? sortiesAudio.Count == 0 : sortiesAudio.All(d => d.Id != s.DeviceId),
            _ => !s.FollowProjection && (s.MediaPath.Length == 0 || !File.Exists(s.MediaPath)),
        };
    }

    async void Relever()
    {
        if (releveEnCours) return;
        releveEnCours = true;
        try
        {
            var (e, s) = await Task.Run(() => PeripheriquesAudio.Lister());
            Traiter(e, s);
            erreurReleve = false;
        }
        catch (Exception x)
        {
            if (!erreurReleve) Journal.Erreur("relevé des périphériques audio", x);
            erreurReleve = true;
        }
        finally { releveEnCours = false; }
    }

    /// <summary>Un relevé des périphériques : les sources d'un périphérique débranché deviennent inactives et reviennent quand il est rebranché.
    /// Rien n'est ajouté au mélangeur tout seul : comme dans OBS, on ajoute ses sources avec « + Source audio ».</summary>
    internal void Traiter(List<AudioDevice> entreesTrouvees, List<AudioDevice> sortiesTrouvees)
    {
        bool premier = !releveFait;
        bool change = premier || !PeripheriquesAudio.Memes(entreesTrouvees, micros) || !PeripheriquesAudio.Memes(sortiesTrouvees, sortiesAudio);
        micros = entreesTrouvees;
        sortiesAudio = sortiesTrouvees;
        releveFait = true;
        // une source d'entrée séparée retrouve sa carte son rebranchée
        if (change && !premier) Apply(false);
        if (change) PeripheriquesChange?.Invoke();
    }

    /// <summary>Oublie le dernier relevé (test automatique, après un relevé simulé) : plus rien n'est inactif jusqu'au prochain.</summary>
    internal void OublierReleve()
    {
        releveFait = false;
        micros = new();
        sortiesAudio = new();
        PeripheriquesChange?.Invoke();
    }

    public List<AudioDevice> MonitoringDevices() { try { return mixer.MonitoringDevices(); } catch (Exception e) { Journal.Erreur("liste des périphériques de monitoring", e); return new(); } }
    public (float L, float R) Level(string id) => mixer.Level(id);
    public float TrackLevel(int piste) => mixer.TrackLevel(piste);
    public bool InputReady(string id) => mixer.Has(id);
    /// <summary>Une source peut écouter une seule entrée d'une carte son (moteur OBS démarré, type « entrée » accepté).</summary>
    public bool EntreesSeparees => mixer.EntreesSeparees;
    /// <summary>Cette source est une entrée séparée dans OBS.</summary>
    public bool EstEntree(string id) => mixer.EstEntree(id);
    /// <summary>Nombre d'entrées d'un micro (0 s'il est absent).</summary>
    public static int Entrees(string deviceId) => CaptureEntrees.Entrees(deviceId);
    /// <summary>Problème d'écoute d'un périphérique (vide si tout va bien).</summary>
    public string StatutEntrees(string deviceId) => entrees.Statut(deviceId);
    public void RestartMedia(AudioSource s) => mixer.RestartMedia(s.Id);
    internal void AddTrackConsumer(int piste, Action<float[]> c) => mixer.AddTrackConsumer(piste, c);
    internal void RemoveTrackConsumer(int piste, Action<float[]> c) => mixer.RemoveTrackConsumer(piste, c);

    public void Dispose()
    {
        sauvegarde.Stop(); veille.Stop(); releve.Stop();
        try { Sorties.Dispose(); } catch (Exception e) { Journal.Erreur("MyVirtualSound", e); }
        try { Recorder.Dispose(); } catch (Exception e) { Journal.Erreur("fin d'enregistrement", e); }
        try { entrees.Dispose(); } catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        try { mixer.Dispose(); } catch (Exception e) { Journal.Erreur("arrêt d'OBS", e); }
    }
}
