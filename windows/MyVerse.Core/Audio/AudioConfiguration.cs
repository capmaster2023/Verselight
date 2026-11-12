using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse.Audio;

/// <summary>Réglages audio de MyChurch 3 : le mélange est fait par libobs (OBS Studio), ces classes ne décrivent que ce qu'on lui demande.</summary>
public enum AudioInputKind { Microphone, Sortie, Media }
public enum AudioFilterKind { NoiseSuppression, NoiseGate, Compressor, Limiter }
/// <summary>Comme dans OBS : écoute coupée, écoute seule (sortie coupée), écoute et sortie.</summary>
public enum AudioMonitoring { Off, Only, AndOutput }

/// <summary>Un filtre OBS (obs-filters) posé sur une source.</summary>
public sealed class AudioFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AudioFilterKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    // compresseur
    public double Ratio { get; set; } = 4;
    public double ThresholdDb { get; set; } = -18;
    public double AttackMs { get; set; } = 6;
    public double ReleaseMs { get; set; } = 60;
    public double OutputGainDb { get; set; }
    // noise gate
    public double OpenDb { get; set; } = -26;
    public double CloseDb { get; set; } = -32;
    public double HoldMs { get; set; } = 200;
    // réduction du bruit : « rnnoise » ou « speex »
    public string Method { get; set; } = "rnnoise";
    public double SuppressDb { get; set; } = -30;

    public static string Nom(AudioFilterKind k) => k switch
    {
        AudioFilterKind.NoiseSuppression => "Réduction du bruit",
        AudioFilterKind.NoiseGate => "Noise gate",
        AudioFilterKind.Compressor => "Compresseur",
        _ => "Limiteur",
    };
    public string Resume() => Kind switch
    {
        AudioFilterKind.NoiseSuppression => Method == "speex" ? $"Speex · {SuppressDb:0} dB" : "RNNoise",
        AudioFilterKind.NoiseGate => $"Ouverture {OpenDb:0} dB · fermeture {CloseDb:0} dB",
        AudioFilterKind.Compressor => $"Ratio {Ratio:0.#}:1 · seuil {ThresholdDb:0} dB",
        _ => $"Seuil {ThresholdDb:0} dB",
    };
}

public sealed class AudioSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Source";
    public AudioInputKind Kind { get; set; }
    /// <summary>Identifiant du périphérique pour OBS ; « default » = périphérique Windows par défaut.</summary>
    public string DeviceId { get; set; } = "default";
    public string DeviceName { get; set; } = "";
    public string MediaPath { get; set; } = "";
    /// <summary>Média : joue le son de la vidéo projetée depuis Display (au lieu d'un fichier fixe).</summary>
    public bool FollowProjection { get; set; }
    public bool Loop { get; set; }
    /// <summary>
    /// Micro : l'entrée du périphérique écoutée par cette bande. 0 = toutes ses entrées ensemble, comme Windows les donne (stéréo) ;
    /// 1, 2, 3… = cette entrée seule, sur les deux côtés. Une carte son à 4 entrées donne ainsi 4 bandes, comme 4 sources dans OBS (MyChurch 3.1).
    /// </summary>
    public int Channel { get; set; }
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public bool Mono { get; set; }
    /// <summary>Pistes 1 à 6 où la source est envoyée (bit 0 = piste 1). Calculé par <see cref="AudioConfiguration.AttribuerPistes"/>
    /// d'après les enregistrements en cours et les sorties MyVirtualSound actives qui contiennent la source.</summary>
    public int Tracks { get; set; } = 1;
    /// <summary>Écoute au casque (monitoring OBS) : coupée, écoute seule (la source ne va plus dans les pistes) ou écoute et sortie.</summary>
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();

    // ---------------- MyChurch 4.0 : comme OBS 32
    /// <summary>Périphérique audio global d'OBS (Réglages › Audio) : « bureau1 », « bureau2 », « micro1 » à « micro4 ». Vide pour une source ajoutée au mélangeur.</summary>
    public string Global { get; set; } = "";
    /// <summary>Épinglée (OBS 32.1) : toujours affichée dans le mélangeur, à gauche, avec les sources globales.</summary>
    public bool Pinned { get; set; }
    /// <summary>Masquée dans le mélangeur (OBS : « Masquer ») ; l'option « Afficher les sources masquées » ou « Tout réafficher » la fait revenir.</summary>
    public bool Hidden { get; set; }
    /// <summary>Balance comme dans les propriétés audio avancées d'OBS : 0 = tout à gauche, 0,5 = centre, 1 = tout à droite.</summary>
    public double Balance { get; set; } = 0.5;
    /// <summary>Décalage de synchronisation (ms) des propriétés audio avancées d'OBS, de −950 à 20 000 : retarde le son de la source.</summary>
    public int SyncOffsetMs { get; set; }
    /// <summary>Raccourcis d'OBS : « appuyer pour couper le son » et « appuyer pour parler », leur délai après relâchement (ms) et leur touche (ex. « Ctrl+F13 »).</summary>
    public bool PushToMute { get; set; }
    public int PushToMuteDelayMs { get; set; }
    public string PushToMuteKey { get; set; } = "";
    public bool PushToTalk { get; set; }
    public int PushToTalkDelayMs { get; set; }
    public string PushToTalkKey { get; set; } = "";
}

/// <summary>Un des six périphériques audio globaux d'OBS (Réglages › Audio).</summary>
public sealed record AudioGlobal(string Slot, string Nom, AudioInputKind Kind);

/// <summary>Comment OBS 32.1 étiquette une source dans le mélangeur.</summary>
public enum EtatMelangeur { Globale, Epinglee, Active, Inactive, Masquee }

/// <summary>
/// « Appuyer pour parler » et « appuyer pour couper le son » d'OBS pour une source : la touche enfoncée, puis le délai après relâchement.
/// Même règle que libobs : coupée si l'appui pour couper est enfoncé (ou dans son délai), ou si l'appui pour parler n'est ni enfoncé ni dans son délai.
/// </summary>
public sealed class EtatRaccourcis
{
    long parlerJusqua = long.MinValue, couperJusqua = long.MinValue;

    /// <summary>Met à jour l'état d'après les touches à l'instant <paramref name="ms"/> ; vrai si la source doit être coupée par ses raccourcis.</summary>
    public bool Coupee(AudioSource s, bool parlerEnfonce, bool couperEnfonce, long ms) =>
        Coupee(s.PushToTalk, s.PushToTalkDelayMs, parlerEnfonce, s.PushToMute, s.PushToMuteDelayMs, couperEnfonce, ms);

    /// <summary>Idem, raccourcis donnés un par un (MyChurch n'active un raccourci que s'il a une touche).</summary>
    public bool Coupee(bool parler, int delaiParler, bool parlerEnfonce, bool couper, int delaiCouper, bool couperEnfonce, long ms)
    {
        bool coupee = false;
        if (parler)
        {
            if (parlerEnfonce) parlerJusqua = long.MaxValue;
            else if (parlerJusqua == long.MaxValue) parlerJusqua = ms + Math.Max(0, delaiParler);
            if (ms >= parlerJusqua) coupee = true;
        }
        else parlerJusqua = long.MinValue;
        if (couper)
        {
            if (couperEnfonce) couperJusqua = long.MaxValue;
            else if (couperJusqua == long.MaxValue) couperJusqua = ms + Math.Max(0, delaiCouper);
            if (ms < couperJusqua) coupee = true;
        }
        else couperJusqua = long.MinValue;
        return coupee;
    }
}

/// <summary>Ce qui reçoit un mélange d'OBS : un enregistrement ou une sortie MyVirtualSound. Chacun choisit ses sources ;
/// ceux qui ont exactement les mêmes sources partagent la même piste d'OBS (6 pistes en tout).</summary>
public interface IDestinationAudio
{
    string Id { get; }
    string Name { get; }
    List<string> SourceIds { get; }
    bool MuteOthers { get; }
    /// <summary>Piste d'OBS attribuée (1 à 6) tant qu'il est actif ; 0 sinon.</summary>
    int Track { get; set; }
}

public sealed class RecordingVisual
{
    public string Kind { get; set; } = "projection"; // projection, camera, image, video
    public string Path { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool Visible { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public bool Crop { get; set; }
    /// <summary>Kind « annonce » : l'identifiant de la source Display (bibliothèque).</summary>
    public string Annonce { get; set; } = "";
}
public sealed class RecordingProfile : IDestinationAudio
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Enregistrement";
    public string Color { get; set; } = "#B3261E";
    public string Directory { get; set; } = "";
    public bool AudioOnly { get; set; } = true;
    public string Format { get; set; } = "wav"; // wav, flac or mkv (video)
    /// <summary>Piste OBS enregistrée (1 à 6), attribuée au démarrage.</summary>
    public int Track { get; set; } = 1;
    /// <summary>Sources du mélangeur enregistrées (identifiants).</summary>
    public List<string> SourceIds { get; set; } = new();
    /// <summary>Sources Display filmées, dans l'ordre (la 1re en grand) : identifiants de la bibliothèque Display, ou <see cref="Projection"/>.</summary>
    public List<string> DisplayIds { get; set; } = new();
    /// <summary>Ce qui est à l'écran de projection (Bible, chants, Display, Duo).</summary>
    public const string Projection = "projection";
    /// <summary>Tant que cet enregistrement tourne, les autres sources du mélangeur sont coupées partout (Zoom, casque, autres enregistrements).</summary>
    public bool MuteOthers { get; set; }
    /// <summary>Plusieurs sources Display : « incrustation » (la 1re en grand, les autres en petit) ou « cote » (côte à côte).</summary>
    public string Layout { get; set; } = "incrustation";
    /// <summary>Synchro de la vidéo : positif = le son est retardé (caméra en retard sur le son), négatif = l'image est retardée. De −500 à +1000 ms.</summary>
    public int AudioDelayMs { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 30;
    public int VideoKbps { get; set; } = 5000;
    public int AudioKbps { get; set; } = 192;
    /// <summary>« auto » : encodeur de la carte graphique (NVIDIA, Intel, AMD) s'il marche, sinon x264, sinon MJPEG.</summary>
    public string Encoder { get; set; } = "auto";
    public List<RecordingVisual> Visuals { get; set; } = new() { new() };

    /// <summary>Les cadres (proportions de l'image) de n sources Display : incrustation (la 1re en plein écran, les autres en petit en bas à droite) ou côte à côte (colonnes, puis grille, chaque cadre à la forme de l'écran).</summary>
    public static List<(double X, double Y, double W, double H)> Cadres(int n, string layout)
    {
        var cadres = new List<(double, double, double, double)>();
        if (n <= 0) return cadres;
        if (layout == "cote")
        {
            // chaque source garde la forme de l'écran (16:9) : en colonnes, puis en grille, centrées en hauteur (comme le Duo côte à côte)
            int colonnes = n <= 3 ? n : (int)Math.Ceiling(Math.Sqrt(n));
            int lignes = (int)Math.Ceiling(n / (double)colonnes);
            double t = 1.0 / colonnes, y0 = (1 - lignes * t) / 2;
            for (int i = 0; i < n; i++)
                cadres.Add((i % colonnes * t, y0 + i / colonnes * t, t, t));
            return cadres;
        }
        const double petit = 0.3, marge = 0.025;
        cadres.Add((0, 0, 1, 1));
        for (int k = 0; k < n - 1; k++)
        {
            int colonne = k % 3, rang = k / 3;
            double x = 1 - marge - (colonne + 1) * petit - colonne * marge, y = 1 - marge - (rang + 1) * petit - rang * marge;
            cadres.Add((Math.Max(0, x), Math.Max(0, y), petit, petit));
        }
        return cadres;
    }

    /// <summary>Les contenus visuels de la vidéo, d'après les sources Display choisies et la disposition.</summary>
    public List<RecordingVisual> VisuelsDisplay()
    {
        var cadres = Cadres(DisplayIds.Count, Layout);
        return DisplayIds.Select((id, i) => new RecordingVisual
        {
            Kind = id == Projection ? "projection" : "annonce", Annonce = id == Projection ? "" : id,
            X = cadres[i].X, Y = cadres[i].Y, Width = cadres[i].W, Height = cadres[i].H,
        }).ToList();
    }
}

/// <summary>Une sortie MyVirtualSound : le mélange de ses sources envoyé vers un périphérique de sortie
/// (par exemple « CABLE Input » de VB-CABLE, que Zoom reçoit comme micro). Plusieurs sorties peuvent être actives en même temps.</summary>
public sealed class VirtualSoundSettings : IDestinationAudio
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Zoom";
    public bool Enabled { get; set; }
    /// <summary>Tant que la sortie est active, les autres sources du mélangeur sont coupées partout.</summary>
    public bool MuteOthers { get; set; }
    /// <summary>« default » = sortie Windows par défaut.</summary>
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    /// <summary>Piste d'OBS attribuée tant que la sortie est active (0 sinon).</summary>
    public int Track { get; set; }
    /// <summary>Sources du mélangeur envoyées dans cette sortie (boutons A, B… des bandes).</summary>
    public List<string> SourceIds { get; set; } = new();
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
}

public sealed class AudioConfiguration
{
    /// <summary>5 : réglages audio et périphériques globaux d'OBS 32 (MyChurch 4.0). 4 : enregistrements choisis par l'utilisateur (3.0.1).</summary>
    public const int Actuelle = 5;
    public const int Pistes = 6;
    public const int MaxEnregistrements = 5;
    public const int MaxSortiesVirtuelles = 4;
    /// <summary>Bandes du mélangeur au plus.</summary>
    public const int MaxSources = 32;
    /// <summary>Entrées d'un périphérique qu'une bande peut écouter seule (cartes son jusqu'à 32 canaux).</summary>
    public const int MaxEntrees = 32;
    public int Version { get; set; } = Actuelle;

    // ---------------- Réglages › Audio, comme OBS 32
    /// <summary>Général : fréquence d'échantillonnage du moteur, 44 100 ou 48 000 Hz. Comme dans OBS, elle s'applique au prochain démarrage.</summary>
    public int SampleRate { get; set; } = 48000;
    /// <summary>Général : canaux du moteur (mono, stereo, 2.1, 4.0, 4.1, 5.1, 7.1), au prochain démarrage. Enregistrements et sorties restent en stéréo.</summary>
    public string Channels { get; set; } = "stereo";
    /// <summary>Vumètres : vitesse de retombée « rapide », « moyenne » (PPM de type I) ou « lente » (PPM de type II).</summary>
    public string MeterDecay { get; set; } = "rapide";
    /// <summary>Vumètres : « echantillon » (crête d'échantillon) ou « reelle » (crête réelle, plus de processeur).</summary>
    public string PeakMeter { get; set; } = "echantillon";
    /// <summary>Avancé : MyChurch n'est plus baissé par Windows pendant un appel (atténuation audio de Windows désactivée).</summary>
    public bool DisableDucking { get; set; }
    /// <summary>Avancé : mise en mémoire tampon audio à faible latence (pour les sorties Decklink/NDI), au prochain démarrage.</summary>
    public bool LowLatency { get; set; }
    /// <summary>Un micro ou une carte son branché pour la première fois s'ajoute tout seul au mélangeur.</summary>
    public bool AutoAddInputs { get; set; } = true;
    /// <summary>Les entrées audio déjà vues (identifiants Windows) : seules les nouvelles s'ajoutent toutes seules.</summary>
    public List<string> KnownInputs { get; set; } = new();
    /// <summary>Le premier relevé des entrées est fait (il note ce qui est déjà branché sans rien ajouter).</summary>
    public bool KnownInputsReady { get; set; }
    /// <summary>« Audio du bureau » et « Micro/Aux » ont été créés au premier démarrage, comme dans OBS (une seule fois).</summary>
    public bool GlobalsInitialized { get; set; }
    /// <summary>Mélangeur (options d'OBS 32.1) : afficher les sources masquées, les inactives, et les garder à droite.</summary>
    public bool ShowHidden { get; set; }
    public bool ShowInactive { get; set; }
    public bool HiddenRight { get; set; }
    public bool InactiveRight { get; set; }
    /// <summary>Mélangeur vertical (par défaut depuis OBS 32.1) ou horizontal.</summary>
    public bool VerticalLayout { get; set; } = true;
    public string FfmpegPath { get; set; } = "";
    /// <summary>Périphérique d'écoute au casque (monitoring OBS) ; « default » = sortie Windows par défaut.</summary>
    public string MonitorDeviceId { get; set; } = "default";
    public string MonitorDeviceName { get; set; } = "";
    /// <summary>Microphone proposé en premier dans « + Source audio » (Paramètres) ; « default » = périphérique Windows par défaut.</summary>
    public string MicrophoneId { get; set; } = "default";
    public string MicrophoneName { get; set; } = "";
    /// <summary>Sortie audio proposée en premier pour « Son d'une sortie audio » (Paramètres).</summary>
    public string OutputId { get; set; } = "default";
    public string OutputName { get; set; } = "";
    public bool PanelVisible { get; set; } = true;
    /// <summary>Les sorties MyVirtualSound (4 au plus), actives en même temps si besoin.</summary>
    [JsonPropertyName("sortiesVirtuelles")]
    public List<VirtualSoundSettings> VirtualSounds { get; set; } = new();
    /// <summary>3.0.0 : une seule sortie MyVirtualSound ; reprise dans <see cref="VirtualSounds"/> puis oubliée.</summary>
    [JsonPropertyName("virtualSound")]
    public VirtualSoundSettings? AncienneSortie { get; set; }
    /// <summary>
    /// Sources du mélangeur OBS. Le nom JSON « obsSources » est voulu : la liste « sources » de la 1.2 bêta
    /// (types « Capture »/« Loopback », filtres « Gain », « Equalizer »…) est ainsi ignorée au lieu de faire échouer la lecture des réglages.
    /// </summary>
    [JsonPropertyName("obsSources")]
    public List<AudioSource> Sources { get; set; } = new();
    /// <summary>Les enregistrements créés par l'utilisateur (5 au plus). Nom JSON « enregistrements » : le profil automatique de la 3.0.0 n'est pas repris.</summary>
    [JsonPropertyName("enregistrements")]
    public List<RecordingProfile> Profiles { get; set; } = new();

    // ---------------- les choix des Réglages › Audio d'OBS 32
    /// <summary>Les six périphériques audio globaux d'OBS, dans l'ordre de ses réglages.</summary>
    public static readonly IReadOnlyList<AudioGlobal> Globaux = new AudioGlobal[]
    {
        new("bureau1", "Audio du bureau", AudioInputKind.Sortie),
        new("bureau2", "Audio du bureau 2", AudioInputKind.Sortie),
        new("micro1", "Micro/Aux", AudioInputKind.Microphone),
        new("micro2", "Micro/Aux 2", AudioInputKind.Microphone),
        new("micro3", "Micro/Aux 3", AudioInputKind.Microphone),
        new("micro4", "Micro/Aux 4", AudioInputKind.Microphone),
    };
    /// <summary>Canaux d'OBS : identifiant, nom, « speaker_layout » de libobs.</summary>
    public static readonly IReadOnlyList<(string Id, string Nom, int Speakers)> Dispositions = new[]
    {
        ("mono", "Mono", 1), ("stereo", "Stéréo", 2), ("2.1", "2.1", 3), ("4.0", "4.0", 4), ("4.1", "4.1", 5), ("5.1", "5.1", 6), ("7.1", "7.1", 8),
    };
    /// <summary>Vitesses de retombée des vumètres d'OBS (dB par seconde).</summary>
    public static readonly IReadOnlyList<(string Id, string Nom, double DbParSeconde)> Retombees = new[]
    {
        ("rapide", "Rapide", 23.53), ("moyenne", "Moyenne (PPM de type I)", 11.76), ("lente", "Lente (PPM de type II)", 8.57),
    };
    /// <summary>Types de crête des vumètres d'OBS (obs_peak_meter_type).</summary>
    public static readonly IReadOnlyList<(string Id, string Nom, int Type)> Cretes = new[]
    {
        ("echantillon", "Crête d'échantillon", 0), ("reelle", "Crête réelle (utilise plus le processeur)", 1),
    };
    public static AudioGlobal? InfoGlobal(string? slot) => Globaux.FirstOrDefault(g => g.Slot == slot);
    /// <summary>Rang d'un périphérique global dans les réglages (0 à 5), −1 sinon.</summary>
    public static int RangGlobal(string? slot)
    {
        for (int i = 0; i < Globaux.Count; i++) if (Globaux[i].Slot == slot) return i;
        return -1;
    }
    /// <summary>Le « speaker_layout » de libobs pour les canaux choisis.</summary>
    public int HautParleurs
    {
        get
        {
            foreach (var d in Dispositions) if (d.Id == Channels) return d.Speakers;
            return 2;
        }
    }
    public double RetombeeDbParSeconde
    {
        get
        {
            foreach (var r in Retombees) if (r.Id == MeterDecay) return r.DbParSeconde;
            return 23.53;
        }
    }
    public int TypeCrete => PeakMeter == "reelle" ? 1 : 0;

    /// <summary>La source d'un périphérique global, s'il n'est pas désactivé.</summary>
    public AudioSource? Globale(string slot) => Sources.FirstOrDefault(s => s.Global == slot);

    /// <summary>
    /// Règle un périphérique global comme dans OBS : null = « Désactivé » (la source quitte le mélangeur), « default » = « Par défaut »
    /// (suit le périphérique Windows par défaut), sinon le périphérique choisi. Les globales restent en tête du mélangeur.
    /// </summary>
    public AudioSource? RegleGlobal(string slot, string? deviceId, string deviceName = "")
    {
        var info = InfoGlobal(slot) ?? throw new ArgumentException("Périphérique global inconnu : " + slot);
        var s = Globale(slot);
        if (deviceId == null)
        {
            if (s != null) Retirer(s);
            return null;
        }
        if (s == null)
        {
            s = new AudioSource { Name = info.Nom, Kind = info.Kind, Global = slot };
            int rang = RangGlobal(slot);
            int ou = Sources.TakeWhile(x => x.Global.Length > 0 && RangGlobal(x.Global) < rang).Count();
            Sources.Insert(ou, s);
        }
        s.DeviceId = deviceId.Length == 0 ? "default" : deviceId;
        s.DeviceName = s.DeviceId == "default" ? "" : deviceName ?? "";
        return s;
    }

    /// <summary>Retire une source du mélangeur, de ses enregistrements et de ses sorties MyVirtualSound.</summary>
    public void Retirer(AudioSource s)
    {
        Sources.Remove(s);
        foreach (var d in Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds)) d.SourceIds.Remove(s.Id);
    }

    /// <summary>Premier démarrage, comme OBS : « Audio du bureau » et « Micro/Aux » sur les périphériques Windows par défaut, si le mélangeur est vide. Une seule fois.</summary>
    public bool InitialiserGlobaux()
    {
        if (GlobalsInitialized) return false;
        GlobalsInitialized = true;
        if (Sources.Count > 0) return false;
        RegleGlobal("bureau1", "default");
        RegleGlobal("micro1", "default");
        return true;
    }

    /// <summary>« Microphone (Focusrite USB) » → « Focusrite USB » : un nom court pour une bande.</summary>
    public static string NomCourt(string nom)
    {
        nom ??= "";
        int a = nom.IndexOf('('), b = nom.LastIndexOf(')');
        return a >= 0 && b > a + 1 ? nom[(a + 1)..b].Trim() : nom.Trim();
    }

    /// <summary>Un nom de bande pas encore pris : « Micro », « Micro 2 »…</summary>
    public string NomLibre(string nom)
    {
        if (string.IsNullOrWhiteSpace(nom)) nom = "Source";
        if (Sources.All(s => s.Name != nom)) return nom;
        for (int i = 2; ; i++)
            if (Sources.All(s => s.Name != $"{nom} {i}")) return $"{nom} {i}";
    }

    /// <summary>
    /// Les entrées audio branchées pour la première fois (jamais vues par MyChurch). Le tout premier relevé ne fait que noter
    /// ce qui est déjà branché : seules les entrées branchées ensuite s'ajoutent toutes seules au mélangeur.
    /// </summary>
    public List<AudioDevice> NouvellesEntrees(IEnumerable<AudioDevice> presentes)
    {
        KnownInputs ??= new();
        var liste = presentes.Where(d => !string.IsNullOrEmpty(d.Id) && d.Id != "default").GroupBy(d => d.Id).Select(g => g.First()).ToList();
        if (!KnownInputsReady)
        {
            KnownInputs = KnownInputs.Concat(liste.Select(d => d.Id)).Distinct().ToList();
            KnownInputsReady = true;
            return new();
        }
        var nouvelles = liste.Where(d => !KnownInputs.Contains(d.Id)).ToList();
        KnownInputs.AddRange(nouvelles.Select(d => d.Id));
        if (KnownInputs.Count > 200) KnownInputs.RemoveRange(0, KnownInputs.Count - 200);
        return nouvelles;
    }

    /// <summary>Ajoute au mélangeur une entrée qui vient d'être branchée : une bande stéréo, comme une capture d'entrée audio d'OBS.
    /// Null si le périphérique a déjà sa bande ou si le mélangeur est plein.</summary>
    public AudioSource? AjouterEntree(AudioDevice d)
    {
        if (Sources.Count >= MaxSources || Sources.Any(s => s.Kind == AudioInputKind.Microphone && s.DeviceId == d.Id)) return null;
        var s = new AudioSource { Name = NomLibre(NomCourt(d.Name)), Kind = AudioInputKind.Microphone, DeviceId = d.Id, DeviceName = d.Name };
        Sources.Add(s);
        return s;
    }

    /// <summary>L'étiquette d'OBS 32.1 : Globale, Épinglée, Masquée, Inactive ou Active.</summary>
    public static EtatMelangeur Etat(AudioSource s, bool inactive) =>
        s.Global.Length > 0 ? EtatMelangeur.Globale : s.Pinned ? EtatMelangeur.Epinglee : s.Hidden ? EtatMelangeur.Masquee : inactive ? EtatMelangeur.Inactive : EtatMelangeur.Active;

    /// <summary>Visible dans le mélangeur : les globales et les épinglées toujours ; les masquées et les inactives selon les options.</summary>
    public bool Visible(AudioSource s, bool inactive) =>
        s.Global.Length > 0 || s.Pinned || ((!s.Hidden || ShowHidden) && (!inactive || ShowInactive));

    /// <summary>
    /// Les bandes du mélangeur dans l'ordre d'OBS 32.1 : les globales (ordre des réglages), les épinglées, puis les autres dans l'ordre choisi ;
    /// les masquées et les inactives seulement si les options les montrent, et en dernier si « garder à droite » est coché.
    /// </summary>
    public List<AudioSource> Affichage(Func<AudioSource, bool> inactive) =>
        Sources.Where(s => Visible(s, inactive(s))).OrderBy(s => Groupe(s, inactive(s))).ThenBy(s => s.Global.Length > 0 ? RangGlobal(s.Global) : 0).ToList();

    /// <summary>Le groupe d'une bande dans le mélangeur : 0 globales, 1 épinglées, 2 les autres, 3 masquées gardées à droite, 4 inactives gardées à droite.</summary>
    public int Groupe(AudioSource s, bool inactive) =>
        s.Global.Length > 0 ? 0 : s.Pinned ? 1 : s.Hidden && HiddenRight ? 3 : inactive && InactiveRight ? 4 : 2;

    /// <summary>Dossier des enregistrements par défaut : Vidéos\MyChurch.</summary>
    public static string DossierEnregistrements
    {
        get
        {
            string v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(v)) v = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            return Path.Combine(v, "MyChurch");
        }
    }

    public static T Copy<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Donnees.Json), Donnees.Json)!;
    public static double Bound(double x, double min, double max, double fallback = 0) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;

    /// <summary>Remet les valeurs dans leurs bornes. Les sources du mixeur local de la 1.2 bêta ne sont pas reprises (autre nom JSON) : le mélangeur repart vide.</summary>
    public void Normalize()
    {
        Sources ??= new(); Profiles ??= new(); FfmpegPath ??= ""; VirtualSounds ??= new();
        if (AncienneSortie is { } ancienne)
        {
            // 3.0.0 : une seule sortie, qui recevait une piste choisie à la main ; ses sources deviennent celles de la sortie « Zoom »
            if (Version < 4 && (ancienne.Enabled || !string.IsNullOrEmpty(ancienne.DeviceId)))
            {
                int piste = Math.Clamp(ancienne.Track, 1, Pistes);
                ancienne.SourceIds = Sources.Where(x => x != null && (x.Tracks & (1 << (piste - 1))) != 0).Select(x => x.Id).ToList();
                ancienne.Name = "Zoom";
                ancienne.Id = Guid.NewGuid().ToString("N");
                VirtualSounds.Insert(0, ancienne);
            }
            AncienneSortie = null;
        }
        // 4.0 : la « sampleRate » de la 1.2 bêta (son ancien mélangeur) n'est pas reprise
        if (Version < 5) { SampleRate = 48000; Channels = "stereo"; }
        Version = Actuelle;
        if (SampleRate != 44100) SampleRate = 48000;
        if (Dispositions.All(d => d.Id != Channels)) Channels = "stereo";
        if (Retombees.All(r => r.Id != MeterDecay)) MeterDecay = "rapide";
        if (PeakMeter != "reelle") PeakMeter = "echantillon";
        KnownInputs = (KnownInputs ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (KnownInputs.Count > 200) KnownInputs.RemoveRange(0, KnownInputs.Count - 200);
        if (string.IsNullOrEmpty(MonitorDeviceId)) MonitorDeviceId = "default";
        MonitorDeviceName ??= "";
        if (string.IsNullOrEmpty(MicrophoneId)) MicrophoneId = "default";
        if (string.IsNullOrEmpty(OutputId)) OutputId = "default";
        MicrophoneName ??= ""; OutputName ??= "";
        var ids = new HashSet<string>();
        Sources.RemoveAll(s => s == null || string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id));
        if (Sources.Count > MaxSources) Sources.RemoveRange(MaxSources, Sources.Count - MaxSources);
        var emplacements = new HashSet<string>();
        foreach (var s in Sources)
        {
            // périphérique global : un seul par emplacement, du genre d'OBS (sortie ou micro), le périphérique entier, jamais masqué
            s.Global ??= "";
            var g = InfoGlobal(s.Global);
            if (g == null || !emplacements.Add(g.Slot)) s.Global = "";
            else { s.Kind = g.Kind; s.Channel = 0; s.Hidden = false; s.Pinned = false; s.FollowProjection = false; }
            if (s.Pinned) s.Hidden = false;
            s.Balance = Bound(s.Balance, 0, 1, 0.5);
            s.SyncOffsetMs = Math.Clamp(s.SyncOffsetMs, -950, 20000);
            s.PushToMuteDelayMs = Math.Clamp(s.PushToMuteDelayMs, 0, 10000);
            s.PushToTalkDelayMs = Math.Clamp(s.PushToTalkDelayMs, 0, 10000);
            s.PushToMuteKey = (s.PushToMuteKey ?? "").Trim();
            s.PushToTalkKey = (s.PushToTalkKey ?? "").Trim();
            if (s.PushToMuteKey.Length > 64) s.PushToMuteKey = "";
            if (s.PushToTalkKey.Length > 64) s.PushToTalkKey = "";
            s.Name = string.IsNullOrWhiteSpace(s.Name) ? "Source" : s.Name.Trim();
            if (string.IsNullOrEmpty(s.DeviceId)) s.DeviceId = "default";
            s.DeviceName ??= ""; s.MediaPath ??= ""; s.Filters ??= new();
            if (!Enum.IsDefined(s.Kind)) s.Kind = AudioInputKind.Microphone;
            if (!Enum.IsDefined(s.Monitoring)) s.Monitoring = AudioMonitoring.Off;
            s.Channel = s.Kind == AudioInputKind.Microphone ? Math.Clamp(s.Channel, 0, MaxEntrees) : 0;
            s.VolumeDb = Bound(s.VolumeDb, -60, 6);
            s.Tracks &= (1 << Pistes) - 1;
            var fids = new HashSet<string>();
            s.Filters.RemoveAll(f => f == null || string.IsNullOrEmpty(f.Id) || !Enum.IsDefined(f.Kind) || !fids.Add(f.Id));
            foreach (var f in s.Filters)
            {
                f.Ratio = Bound(f.Ratio, 1, 32, 4); f.ThresholdDb = Bound(f.ThresholdDb, -60, 0, -18);
                f.AttackMs = Bound(f.AttackMs, 1, 500, 6); f.ReleaseMs = Bound(f.ReleaseMs, 1, 1000, 60);
                f.OutputGainDb = Bound(f.OutputGainDb, -32, 32);
                f.OpenDb = Bound(f.OpenDb, -96, 0, -26); f.CloseDb = Bound(f.CloseDb, -96, 0, -32); f.HoldMs = Bound(f.HoldMs, 0, 3000, 200);
                if (f.CloseDb > f.OpenDb) f.CloseDb = f.OpenDb;
                f.SuppressDb = Bound(f.SuppressDb, -60, 0, -30);
                if (f.Method != "speex") f.Method = "rnnoise";
            }
        }
        var sourcesConnues = Sources.Select(x => x.Id).ToHashSet();
        var vids = new HashSet<string>();
        VirtualSounds.RemoveAll(v => v == null || string.IsNullOrEmpty(v.Id) || !vids.Add(v.Id));
        if (VirtualSounds.Count > MaxSortiesVirtuelles) VirtualSounds.RemoveRange(MaxSortiesVirtuelles, VirtualSounds.Count - MaxSortiesVirtuelles);
        for (int i = 0; i < VirtualSounds.Count; i++)
        {
            var v = VirtualSounds[i];
            v.Name = string.IsNullOrWhiteSpace(v.Name) ? "Sortie " + (char)('A' + i) : v.Name.Trim();
            v.DeviceId ??= ""; v.DeviceName ??= "";
            v.SourceIds = (v.SourceIds ?? new()).Where(sourcesConnues.Contains).Distinct().ToList();
            v.VolumeDb = Bound(v.VolumeDb, -60, 6);
            if (v.Track is < 0 or > Pistes) v.Track = 0;
        }
        // enregistrements : 5 au plus
        var pids = new HashSet<string>();
        Profiles.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id) || !pids.Add(p.Id));
        if (Profiles.Count > MaxEnregistrements) Profiles.RemoveRange(MaxEnregistrements, Profiles.Count - MaxEnregistrements);
        for (int i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            if (p.Track is < 0 or > Pistes) p.Track = 0;
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "Enregistrement " + (i + 1) : p.Name.Trim();
            p.Color ??= "#B3261E"; p.Visuals ??= new(); p.Encoder ??= "auto"; p.Layout = p.Layout == "cote" ? "cote" : "incrustation";
            p.SourceIds = (p.SourceIds ?? new()).Where(sourcesConnues.Contains).Distinct().ToList();
            p.DisplayIds = (p.DisplayIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(6).ToList();
            if (p.DisplayIds.Count > 0) { p.AudioOnly = false; p.Format = "mkv"; }
            else { p.AudioOnly = true; if (p.Format is not ("wav" or "flac")) p.Format = "wav"; }
            p.AudioDelayMs = Math.Clamp(p.AudioDelayMs, -500, 1000);
            if (string.IsNullOrWhiteSpace(p.Directory)) p.Directory = Path.Combine(DossierEnregistrements, RecordingFiles.SafeName(p.Name));
        }
    }

    /// <summary>Les destinations actives : les enregistrements en cours (ou qui démarrent) et les sorties MyVirtualSound activées.</summary>
    public List<IDestinationAudio> DestinationsActives(IEnumerable<string> enregistrementsActifs)
    {
        var actifs = enregistrementsActifs.ToHashSet();
        return Profiles.Where(p => actifs.Contains(p.Id)).Cast<IDestinationAudio>().Concat(VirtualSounds.Where(v => v.Enabled)).ToList();
    }

    static string Cle(IDestinationAudio d) => string.Join("|", d.SourceIds.OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>
    /// Donne une piste d'OBS à chaque destination active : une piste par mélange différent (mêmes sources = même piste),
    /// en gardant autant que possible la piste qu'elle avait. Puis chaque source est envoyée dans les pistes de ses destinations.
    /// Renvoie faux s'il faudrait plus de 6 mélanges différents (les destinations en trop restent sans piste).
    /// </summary>
    public bool AttribuerPistes(IEnumerable<string> enregistrementsActifs)
    {
        var actives = DestinationsActives(enregistrementsActifs);
        foreach (var d in Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds).Except(actives)) d.Track = 0;
        var groupes = actives.GroupBy(Cle).ToList();
        var prises = new HashSet<int>();
        var pisteDe = new Dictionary<string, int>();
        // d'abord garder les pistes déjà données (un enregistrement en cours ne change pas de piste sans raison)
        foreach (var g in groupes)
        {
            int garde = g.Select(d => d.Track).Where(t => t is >= 1 and <= Pistes && !prises.Contains(t))
                .GroupBy(t => t).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Select(x => x.Key).FirstOrDefault();
            if (garde > 0) { prises.Add(garde); pisteDe[g.Key] = garde; }
        }
        bool assez = true;
        foreach (var g in groupes)
        {
            if (pisteDe.ContainsKey(g.Key)) continue;
            int libre = Enumerable.Range(1, Pistes).FirstOrDefault(t => !prises.Contains(t));
            if (libre == 0) { assez = false; continue; }
            prises.Add(libre); pisteDe[g.Key] = libre;
        }
        foreach (var g in groupes)
            foreach (var d in g) d.Track = pisteDe.TryGetValue(g.Key, out int t) ? t : 0;
        foreach (var s in Sources)
        {
            int bits = 0;
            foreach (var d in actives) if (d.Track > 0 && d.SourceIds.Contains(s.Id)) bits |= 1 << (d.Track - 1);
            s.Tracks = bits;
        }
        return assez;
    }

    /// <summary>Combien de mélanges différents il faudrait (6 au plus) si ces enregistrements tournaient avec les sorties activées.</summary>
    public int Melanges(IEnumerable<string> enregistrementsActifs) => DestinationsActives(enregistrementsActifs).Select(Cle).Distinct().Count();

    /// <summary>Les sources coupées partout par les destinations actives qui ont l'option « couper les autres sources ».</summary>
    public HashSet<string> SourcesCoupees(IEnumerable<string> enregistrementsActifs)
    {
        var coupees = new HashSet<string>();
        foreach (var d in DestinationsActives(enregistrementsActifs).Where(d => d.MuteOthers))
            foreach (var s in Sources)
                if (!d.SourceIds.Contains(s.Id)) coupees.Add(s.Id);
        return coupees;
    }

    /// <summary>Une copie où les sources données sont en sourdine (ce qu'OBS reçoit pendant un enregistrement qui coupe les autres).</summary>
    public AudioConfiguration AvecSourdines(IReadOnlySet<string> coupees)
    {
        var c = Copy(this);
        foreach (var s in c.Sources) if (coupees.Contains(s.Id)) s.Muted = true;
        return c;
    }
}

/// <summary>
/// Blocs remis à l'enregistrement et aux sorties : 10 ms, stéréo entrelacée, à la fréquence du moteur (48 kHz, ou 44,1 kHz choisi
/// dans Réglages › Audio). Comme dans OBS, elle est fixée au démarrage du moteur et ne change qu'au prochain démarrage.
/// </summary>
public static class AudioBlock
{
    public static int Rate { get; private set; } = 48000;
    public static int Frames => Rate / 100;
    public static int Samples => Frames * 2;
    public static void Configurer(int rate) => Rate = rate == 44100 ? 44100 : 48000;
}
public static class AudioMath
{
    public static double Linear(double db) => db <= -60 ? 0 : Math.Pow(10, db / 20);
    public static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));
}
