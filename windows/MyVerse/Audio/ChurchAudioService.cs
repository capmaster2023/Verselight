using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Le son de MyChurch : le moteur d'OBS Studio (libobs), livré dans le dossier « obs » à côté de MyChurch.exe.
/// Cette classe garde les réglages, démarre le moteur et lui transmet chaque changement ; tout le traitement est fait par OBS.
/// MyChurch 4.0 : réglages audio d'OBS 32 (fréquence, canaux, périphériques globaux, vumètres, avancé, raccourcis)
/// et entrées branchées ajoutées toutes seules au mélangeur.
/// </summary>
public sealed class ChurchAudioService : IDisposable
{
    readonly ObsMixer mixer = new();
    readonly CaptureEntrees entrees;
    readonly System.Windows.Threading.DispatcherTimer sauvegarde = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly System.Windows.Threading.DispatcherTimer veille = new() { Interval = TimeSpan.FromMilliseconds(500) };
    /// <summary>Relevé des périphériques toutes les 2 s : un micro branché apparaît dans le mélangeur, un débranché devient inactif.</summary>
    readonly System.Windows.Threading.DispatcherTimer releve = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>« Appuyer pour parler » et « appuyer pour couper le son » : les touches sont lues 50 fois par seconde.</summary>
    readonly System.Windows.Threading.DispatcherTimer sonde = new(System.Windows.Threading.DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
    readonly Dictionary<string, EtatRaccourcis> raccourcis = new();
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
    /// <summary>Sources coupées en ce moment par un enregistrement ou une sortie qui « coupe les autres sources ».</summary>
    public IReadOnlySet<string> SourdinesForcees { get; private set; } = new HashSet<string>();
    /// <summary>Faux s'il faudrait plus de 6 mélanges différents en même temps (6 pistes d'OBS).</summary>
    public bool PistesSuffisantes { get; private set; } = true;
    /// <summary>ffmpeg (FLAC et vidéo MKV) : celui choisi dans les Paramètres, sinon celui livré avec MyChurch.</summary>
    public string Ffmpeg => Config.FfmpegPath.Length > 0 && File.Exists(Config.FfmpegPath) ? Config.FfmpegPath : FfmpegInclus;
    public static string FfmpegInclus => Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
    public bool Running => mixer.Running;
    public bool TracksActive => mixer.TracksActive;
    public string Version => mixer.Version;
    public event Action? Changed;
    /// <summary>Un périphérique a été branché ou débranché (bandes actives ou inactives, listes des réglages).</summary>
    public event Action? PeripheriquesChange;
    /// <summary>Des entrées viennent d'être branchées et ajoutées au mélangeur.</summary>
    public event Action<IReadOnlyList<AudioSource>>? EntreesAjoutees;
    /// <summary>Un raccourci (appuyer pour parler / pour couper) vient de couper ou de rétablir une source.</summary>
    public event Action? RaccourcisChange;

    /// <summary>Format du moteur en marche, comme Réglages › Audio › Général d'OBS : « 48 kHz · stéréo ».</summary>
    public string Format
    {
        get
        {
            int rate = mixer.Running ? mixer.SampleRate : AudioBlock.Rate;
            int hp = mixer.Running ? mixer.Speakers : Config.HautParleurs;
            string canaux = AudioConfiguration.Dispositions.FirstOrDefault(d => d.Speakers == hp).Nom ?? "Stéréo";
            return (rate == 44100 ? "44,1 kHz" : "48 kHz") + " · " + canaux.ToLowerInvariant() + (mixer.Running && mixer.LowLatency ? " · faible latence" : "");
        }
    }
    /// <summary>Comme OBS : la fréquence, les canaux ou la faible latence choisis ne s'appliquent qu'au prochain démarrage de MyChurch.</summary>
    public bool RedemarrageRequis => Config.SampleRate != AudioBlock.Rate
        || (mixer.Running && (Config.HautParleurs != mixer.Speakers || Config.LowLatency != mixer.LowLatency));

    public static string DossierObs => Path.Combine(AppContext.BaseDirectory, "obs");

    public ChurchAudioService(AudioConfiguration config)
    {
        Config = config;
        Config.Normalize();
        entrees = new CaptureEntrees(mixer.Pousser);
        Recorder = new(this);
        Sorties = new(this);
        // la fréquence est fixée pour toute la session (enregistrements, MyVirtualSound), comme dans OBS
        AudioBlock.Configurer(Config.SampleRate);
        releve.Tick += (_, _) => Relever();
        sonde.Tick += (_, _) => Sonder();
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
        // premier démarrage, comme OBS : « Audio du bureau » et « Micro/Aux » sur les périphériques par défaut (l'autotest règle son propre mélangeur)
        if (App.AutoTest == null)
        {
            if (Config.InitialiserGlobaux()) { Journal.Info("audio : périphériques globaux créés (Audio du bureau, Micro/Aux)"); sauvegarde.Stop(); sauvegarde.Start(); }
            Relever();
            releve.Start();
        }
        if (Config.DisableDucking && !AttenuationWindows.Desactiver(true)) Journal.Info("audio : atténuation de Windows non désactivée (aucune sortie audio ?)");
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
            mixer.Start(chemins, Config.SampleRate, Config.HautParleurs, Config.LowLatency);
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
        // une piste d'OBS par mélange différent en cours (enregistrements qui tournent, sorties MyVirtualSound actives)
        var reserves = Recorder.Reserves;
        PistesSuffisantes = Config.AttribuerPistes(reserves);
        var coupees = Config.SourcesCoupees(reserves);
        SourdinesForcees = coupees;
        sauvegarde.Stop(); sauvegarde.Start();
        try { mixer.Apply(coupees.Count == 0 ? Config : Config.AvecSourdines(coupees)); }
        catch (Exception e) { Diagnostic = "OBS : " + e.Message; Journal.Erreur("réglages audio OBS", e); }
        Sorties.Apply(Config.VirtualSounds);
        // entrées séparées : MyChurch écoute la carte son et donne à chaque bande son canal
        try { entrees.Apply(mixer.EntreesSeparees ? Config.Sources : Enumerable.Empty<AudioSource>()); }
        catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        Recorder.SuivrePistes();
        MajRaccourcis();
        Vumetre.Retombee = Config.RetombeeDbParSeconde;
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

    // ------------------------------------------------------------ périphériques branchés et débranchés (4.0)
    /// <summary>
    /// Inactive comme dans le mélangeur d'OBS 32.1 : son périphérique est débranché (ou il n'y en a aucun), ou son fichier manque.
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
            Traiter(e, s, false);
            erreurReleve = false;
        }
        catch (Exception x)
        {
            if (!erreurReleve) Journal.Erreur("relevé des périphériques audio", x);
            erreurReleve = true;
        }
        finally { releveEnCours = false; }
    }

    /// <summary>
    /// Un relevé des périphériques : une entrée branchée pour la première fois s'ajoute au mélangeur (option des réglages),
    /// les bandes d'un périphérique débranché deviennent inactives et reviennent quand il est rebranché.
    /// <paramref name="simule"/> : relevé fait par l'autotest (qui n'a pas de vrai micro à brancher).
    /// </summary>
    internal void Traiter(List<AudioDevice> entreesTrouvees, List<AudioDevice> sortiesTrouvees, bool simule)
    {
        bool premier = !releveFait;
        bool change = premier || !PeripheriquesAudio.Memes(entreesTrouvees, micros) || !PeripheriquesAudio.Memes(sortiesTrouvees, sortiesAudio);
        micros = entreesTrouvees;
        sortiesAudio = sortiesTrouvees;
        releveFait = true;
        bool pret = Config.KnownInputsReady;
        var nouvelles = Config.NouvellesEntrees(entreesTrouvees);
        var ajoutees = new List<AudioSource>();
        if (Config.AutoAddInputs && (App.AutoTest == null || simule))
            foreach (var d in nouvelles)
                if (Config.AjouterEntree(d) is { } source)
                {
                    ajoutees.Add(source);
                    Journal.Info("audio : entrée branchée, ajoutée au mélangeur : " + d.Name);
                }
        // une bande d'entrées séparées retrouve sa carte son rebranchée ; les entrées connues sont enregistrées
        if (ajoutees.Count > 0 || nouvelles.Count > 0 || !pret) Apply();
        else if (change && !premier) Apply(false);
        if (change || ajoutees.Count > 0) PeripheriquesChange?.Invoke();
        if (ajoutees.Count > 0) EntreesAjoutees?.Invoke(ajoutees);
    }

    /// <summary>Oublie le dernier relevé (test automatique, après un relevé simulé) : plus rien n'est inactif jusqu'au prochain.</summary>
    internal void OublierReleve()
    {
        releveFait = false;
        micros = new();
        sortiesAudio = new();
        PeripheriquesChange?.Invoke();
    }

    /// <summary>« Désactiver l'atténuation audio de Windows » (Réglages › Audio › Avancé), appliqué tout de suite comme dans OBS.</summary>
    public void Attenuation(bool desactiver)
    {
        Config.DisableDucking = desactiver;
        AttenuationWindows.Desactiver(desactiver);
        Apply();
    }

    // ------------------------------------------------------------ appuyer pour parler / pour couper le son (4.0)
    static bool AvecRaccourci(AudioSource s) => (s.PushToTalk && s.PushToTalkKey.Length > 0) || (s.PushToMute && s.PushToMuteKey.Length > 0);

    void MajRaccourcis()
    {
        bool besoin = Config.Sources.Any(AvecRaccourci);
        if (besoin && !sonde.IsEnabled) sonde.Start();
        else if (!besoin && sonde.IsEnabled) { sonde.Stop(); Sonder(); }
    }

    /// <summary>Lit les touches et coupe ou rétablit les sources comme libobs (délai après relâchement), sans toucher à leur sourdine.</summary>
    void Sonder()
    {
        long ms = Environment.TickCount64;
        bool change = false;
        foreach (var s in Config.Sources)
        {
            bool parler = s.PushToTalk && s.PushToTalkKey.Length > 0, couper = s.PushToMute && s.PushToMuteKey.Length > 0;
            if (!parler && !couper)
            {
                if (raccourcis.Remove(s.Id) && mixer.CoupeeParRaccourci(s.Id)) { mixer.Raccourci(s.Id, false); change = true; }
                continue;
            }
            if (!raccourcis.TryGetValue(s.Id, out var etat)) raccourcis[s.Id] = etat = new EtatRaccourcis();
            bool coupee = etat.Coupee(parler, s.PushToTalkDelayMs, parler && Touches.Enfoncee(s.PushToTalkKey), couper, s.PushToMuteDelayMs, couper && Touches.Enfoncee(s.PushToMuteKey), ms);
            if (coupee != mixer.CoupeeParRaccourci(s.Id)) { mixer.Raccourci(s.Id, coupee); change = true; }
        }
        foreach (var id in raccourcis.Keys.Where(id => Config.Sources.All(x => x.Id != id)).ToList())
        {
            raccourcis.Remove(id);
            if (mixer.CoupeeParRaccourci(id)) { mixer.Raccourci(id, false); change = true; }
        }
        if (change) RaccourcisChange?.Invoke();
    }

    /// <summary>La source est coupée en ce moment par « appuyer pour parler » ou « appuyer pour couper le son ».</summary>
    public bool CoupeeParRaccourci(string id) => mixer.CoupeeParRaccourci(id);
    public List<AudioDevice> MonitoringDevices() { try { return mixer.MonitoringDevices(); } catch (Exception e) { Journal.Erreur("liste des écoutes", e); return new(); } }
    public (float L, float R) Level(string id) => mixer.Level(id);
    public float TrackLevel(int piste) => mixer.TrackLevel(piste);
    public bool InputReady(string id) => mixer.Has(id);
    /// <summary>Une bande peut écouter une seule entrée d'une carte son (moteur OBS démarré, type « entrée » accepté).</summary>
    public bool EntreesSeparees => mixer.EntreesSeparees;
    /// <summary>Cette bande est une entrée séparée dans OBS.</summary>
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
        sauvegarde.Stop(); veille.Stop(); releve.Stop(); sonde.Stop();
        try { Sorties.Dispose(); } catch (Exception e) { Journal.Erreur("MyVirtualSound", e); }
        try { Recorder.Dispose(); } catch (Exception e) { Journal.Erreur("fin d'enregistrement", e); }
        try { entrees.Dispose(); } catch (Exception e) { Journal.Erreur("entrées séparées", e); }
        try { mixer.Dispose(); } catch (Exception e) { Journal.Erreur("arrêt d'OBS", e); }
    }
}
