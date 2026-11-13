using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse.Audio;

/// <summary>Réglages audio de MyChurch : le mélange est fait par libobs (OBS Studio), ces classes ne décrivent que ce qu'on lui demande.</summary>
public enum AudioInputKind { Microphone, Sortie, Media }
public enum AudioFilterKind { NoiseSuppression, NoiseGate, Compressor, Limiter }
/// <summary>Comme dans OBS : monitoring désactivé, monitoring uniquement (sortie coupée), monitoring et sortie.</summary>
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

/// <summary>
/// Une source du mélangeur, comme une source audio d'OBS : capture audio (entrée), capture audio (sortie) ou média.
/// Ses réglages sont ceux des propriétés de la source (périphérique, horodatage) et des propriétés audio avancées
/// (volume, mono, balance, décalage de synchronisation, monitoring, pistes).
/// </summary>
public sealed class AudioSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Source";
    public AudioInputKind Kind { get; set; }
    /// <summary>Identifiant du périphérique pour OBS ; « default » = « Par défaut » (le périphérique choisi dans Windows).</summary>
    public string DeviceId { get; set; } = "default";
    public string DeviceName { get; set; } = "";
    public string MediaPath { get; set; } = "";
    /// <summary>Média : joue le son de la vidéo projetée depuis Display (au lieu d'un fichier fixe).</summary>
    public bool FollowProjection { get; set; }
    public bool Loop { get; set; }
    /// <summary>
    /// Capture audio (entrée) : l'entrée du périphérique écoutée par cette source. 0 = toutes ses entrées ensemble, comme Windows les donne (stéréo) ;
    /// 1, 2, 3… = cette entrée seule, des deux côtés. Une carte son à 4 entrées donne ainsi 4 sources, une par micro (MyChurch 3.1).
    /// </summary>
    public int Channel { get; set; }
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public bool Mono { get; set; }
    /// <summary>Pistes 1 à 6 où va la source (bit 0 = piste 1), cochées dans les propriétés audio avancées comme dans OBS : toutes par défaut.</summary>
    public int Tracks { get; set; } = AudioConfiguration.ToutesLesPistes;
    /// <summary>Monitoring audio d'OBS (casque de la régie) : désactivé, uniquement (la source ne va plus dans les pistes) ou monitoring et sortie.</summary>
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();
    /// <summary>Balance comme dans les propriétés audio avancées d'OBS : 0 = tout à gauche, 0,5 = centre, 1 = tout à droite.</summary>
    public double Balance { get; set; } = 0.5;
    /// <summary>Décalage de synchronisation (ms) des propriétés audio avancées d'OBS, de −950 à 20 000 : retarde le son de la source.</summary>
    public int SyncOffsetMs { get; set; }
    /// <summary>« Utiliser l'horodatage du périphérique » des propriétés d'une capture audio d'OBS. Null : la valeur par défaut d'OBS.</summary>
    public bool? UseDeviceTiming { get; set; }

    /// <summary>L'horodatage du périphérique est utilisé : choisi, sinon comme OBS par défaut (non pour une entrée, oui pour une sortie).</summary>
    [JsonIgnore]
    public bool Horodatage => UseDeviceTiming ?? AudioConfiguration.HorodatageParDefaut(Kind);
    /// <summary>La source va dans cette piste (1 à 6).</summary>
    public bool DansPiste(int piste) => piste is >= 1 and <= AudioConfiguration.Pistes && (Tracks & (1 << (piste - 1))) != 0;
}

/// <summary>Ce qui reçoit une piste d'OBS : un enregistrement ou une sortie MyVirtualSound. Comme dans OBS, chacun choisit sa piste (1 à 6) ;
/// les sources de la piste se cochent dans les propriétés audio avancées.</summary>
public interface IDestinationAudio
{
    string Id { get; }
    string Name { get; }
    /// <summary>La piste d'OBS reçue (1 à 6).</summary>
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
    /// <summary>La piste d'OBS enregistrée (1 à 6), comme « Piste audio » dans les paramètres d'enregistrement d'OBS.</summary>
    public int Track { get; set; } = 1;
    /// <summary>Avant la 4.2 : les sources choisies pour cet enregistrement. Reprises en pistes une fois (<see cref="AudioConfiguration.Normalize"/>), puis oubliées.</summary>
    public List<string>? SourceIds { get; set; }
    /// <summary>Sources Display filmées, dans l'ordre (la 1re en grand) : identifiants de la bibliothèque Display, ou <see cref="Projection"/>.</summary>
    public List<string> DisplayIds { get; set; } = new();
    /// <summary>Ce qui est à l'écran de projection (Bible, chants, Display, Duo).</summary>
    public const string Projection = "projection";
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

/// <summary>Une sortie MyVirtualSound : une piste d'OBS envoyée vers un périphérique de sortie
/// (par exemple « CABLE Input » de VB-CABLE, que Zoom reçoit comme micro). Plusieurs sorties peuvent être actives en même temps.</summary>
public sealed class VirtualSoundSettings : IDestinationAudio
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Zoom";
    public bool Enabled { get; set; }
    /// <summary>« default » = sortie Windows par défaut ; vide = le câble virtuel installé (VB-CABLE…).</summary>
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    /// <summary>La piste d'OBS envoyée (1 à 6).</summary>
    public int Track { get; set; } = 1;
    /// <summary>Avant la 4.2 : les sources envoyées dans cette sortie. Reprises en pistes une fois, puis oubliées.</summary>
    public List<string>? SourceIds { get; set; }
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
}

public sealed class AudioConfiguration
{
    /// <summary>6 : MyChurch 4.2, l'audio simplifié comme OBS (chaque source coche ses pistes 1 à 6, chaque destination prend une piste).
    /// 5 : réglages d'OBS 32 (4.0). 4 : enregistrements choisis par l'utilisateur (3.0.1).</summary>
    public const int Actuelle = 6;
    public const int Pistes = 6;
    /// <summary>Les pistes 1 à 6 cochées, comme une nouvelle source dans OBS.</summary>
    public const int ToutesLesPistes = (1 << Pistes) - 1;
    public const int MaxEnregistrements = 5;
    public const int MaxSortiesVirtuelles = 4;
    /// <summary>Sources du mélangeur au plus.</summary>
    public const int MaxSources = 32;
    /// <summary>Entrées d'un périphérique qu'une source peut écouter seule (cartes son jusqu'à 32 canaux).</summary>
    public const int MaxEntrees = 32;
    public int Version { get; set; } = Actuelle;

    /// <summary>Périphérique de monitoring audio (casque de la régie) ; « default » = sortie Windows par défaut.</summary>
    public string MonitorDeviceId { get; set; } = "default";
    public string MonitorDeviceName { get; set; } = "";
    public bool PanelVisible { get; set; } = true;
    /// <summary>Les sorties MyVirtualSound (4 au plus), actives en même temps si besoin.</summary>
    [JsonPropertyName("sortiesVirtuelles")]
    public List<VirtualSoundSettings> VirtualSounds { get; set; } = new();
    /// <summary>3.0.0 : une seule sortie MyVirtualSound ; reprise dans <see cref="VirtualSounds"/> puis oubliée.</summary>
    [JsonPropertyName("virtualSound")]
    public VirtualSoundSettings? AncienneSortie { get; set; }
    /// <summary>
    /// Sources du mélangeur OBS, dans l'ordre du mélangeur. Le nom JSON « obsSources » est voulu : la liste « sources » de la 1.2 bêta
    /// (types « Capture »/« Loopback », filtres « Gain », « Equalizer »…) est ainsi ignorée au lieu de faire échouer la lecture des réglages.
    /// </summary>
    [JsonPropertyName("obsSources")]
    public List<AudioSource> Sources { get; set; } = new();
    /// <summary>Les enregistrements créés par l'utilisateur (5 au plus). Nom JSON « enregistrements » : le profil automatique de la 3.0.0 n'est pas repris.</summary>
    [JsonPropertyName("enregistrements")]
    public List<RecordingProfile> Profiles { get; set; } = new();

    /// <summary>Comme OBS : une capture audio (entrée) n'utilise pas l'horodatage du périphérique par défaut, une capture audio (sortie) oui.</summary>
    public static bool HorodatageParDefaut(AudioInputKind k) => k == AudioInputKind.Sortie;

    /// <summary>Le type de la source, avec les noms d'OBS : « Capture audio (entrée) », « Capture audio (sortie) », « Média ».</summary>
    public static string NomType(AudioSource s) => s.Kind switch
    {
        AudioInputKind.Microphone => "Capture audio (entrée)",
        AudioInputKind.Sortie => "Capture audio (sortie)",
        _ when s.FollowProjection => "Vidéo projetée (Display)",
        _ => "Média",
    };

    /// <summary>Les sources qui vont dans une piste (1 à 6), sans celles en « monitoring uniquement ».</summary>
    public List<AudioSource> SourcesDeLaPiste(int piste) => Sources.Where(s => s.DansPiste(piste) && s.Monitoring != AudioMonitoring.Only).ToList();

    /// <summary>Retire une source du mélangeur.</summary>
    public void Retirer(AudioSource s) => Sources.Remove(s);

    /// <summary>« Microphone (Focusrite USB) » → « Focusrite USB » : un nom court pour une source.</summary>
    public static string NomCourt(string nom)
    {
        nom ??= "";
        int a = nom.IndexOf('('), b = nom.LastIndexOf(')');
        return a >= 0 && b > a + 1 ? nom[(a + 1)..b].Trim() : nom.Trim();
    }

    /// <summary>Un nom de source pas encore pris, comme OBS : « Capture audio (entrée) », « Capture audio (entrée) 2 »…</summary>
    public string NomLibre(string nom)
    {
        if (string.IsNullOrWhiteSpace(nom)) nom = "Source";
        if (Sources.All(s => s.Name != nom)) return nom;
        for (int i = 2; ; i++)
            if (Sources.All(s => s.Name != $"{nom} {i}")) return $"{nom} {i}";
    }

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

    static List<string>? AnciennesSources(IDestinationAudio d) => d switch
    {
        RecordingProfile p => p.SourceIds,
        VirtualSoundSettings v => v.SourceIds,
        _ => null,
    };

    /// <summary>
    /// Réglages d'avant la 4.2 : chaque enregistrement et chaque sortie MyVirtualSound choisissaient leurs sources, et MyChurch leur donnait
    /// une piste d'OBS tout seul. Maintenant, comme dans OBS, chaque source coche ses pistes et chaque destination prend une piste.
    /// Les mélanges déjà faits sont gardés : un mélange différent par piste (6 au plus, dans l'ordre), et les pistes restées libres
    /// reçoivent toutes les sources, comme dans OBS.
    /// </summary>
    void MigrerPistes()
    {
        var connues = Sources.Select(s => s.Id).ToHashSet();
        var pisteDe = new Dictionary<string, int>();
        var contenu = new Dictionary<int, HashSet<string>>();
        foreach (var d in Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds))
        {
            var ids = (AnciennesSources(d) ?? new()).Where(connues.Contains).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            string cle = string.Join("|", ids);
            if (!pisteDe.TryGetValue(cle, out int t) && pisteDe.Count < Pistes)
            {
                t = pisteDe.Count + 1;
                pisteDe[cle] = t;
                contenu[t] = ids.ToHashSet();
            }
            d.Track = t > 0 ? t : 1;
        }
        foreach (var s in Sources)
        {
            int bits = 0;
            for (int t = 1; t <= Pistes; t++)
                if (!contenu.TryGetValue(t, out var ids) || ids.Contains(s.Id)) bits |= 1 << (t - 1);
            s.Tracks = bits;
        }
        foreach (var p in Profiles) p.SourceIds = null;
        foreach (var v in VirtualSounds) v.SourceIds = null;
    }

    /// <summary>Remet les valeurs dans leurs bornes et reprend les réglages des anciennes versions.
    /// Les sources du mixeur local de la 1.2 bêta ne sont pas reprises (autre nom JSON) : le mélangeur repart vide.</summary>
    public void Normalize()
    {
        Sources ??= new(); Profiles ??= new(); VirtualSounds ??= new();
        Sources.RemoveAll(s => s == null);
        Profiles.RemoveAll(p => p == null);
        VirtualSounds.RemoveAll(v => v == null);
        if (AncienneSortie is { } ancienne)
        {
            // 3.0.0 : une seule sortie, qui recevait une piste choisie à la main ; ses sources deviennent celles de la sortie « Zoom »
            if (Version < 4 && (ancienne.Enabled || !string.IsNullOrEmpty(ancienne.DeviceId)))
            {
                int piste = Math.Clamp(ancienne.Track, 1, Pistes);
                ancienne.SourceIds = Sources.Where(x => (x.Tracks & (1 << (piste - 1))) != 0).Select(x => x.Id).ToList();
                ancienne.Name = "Zoom";
                ancienne.Id = Guid.NewGuid().ToString("N");
                VirtualSounds.Insert(0, ancienne);
            }
            AncienneSortie = null;
        }
        var ids = new HashSet<string>();
        Sources.RemoveAll(s => string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id));
        if (Sources.Count > MaxSources) Sources.RemoveRange(MaxSources, Sources.Count - MaxSources);
        var vids = new HashSet<string>();
        VirtualSounds.RemoveAll(v => string.IsNullOrEmpty(v.Id) || !vids.Add(v.Id));
        if (VirtualSounds.Count > MaxSortiesVirtuelles) VirtualSounds.RemoveRange(MaxSortiesVirtuelles, VirtualSounds.Count - MaxSortiesVirtuelles);
        var pids = new HashSet<string>();
        Profiles.RemoveAll(p => string.IsNullOrEmpty(p.Id) || !pids.Add(p.Id));
        if (Profiles.Count > MaxEnregistrements) Profiles.RemoveRange(MaxEnregistrements, Profiles.Count - MaxEnregistrements);
        // 4.2 : les sources choisies par destination deviennent des pistes cochées, comme dans OBS
        if (Version < 6) MigrerPistes();
        Version = Actuelle;
        if (string.IsNullOrEmpty(MonitorDeviceId)) MonitorDeviceId = "default";
        MonitorDeviceName ??= "";
        foreach (var s in Sources)
        {
            s.Name = string.IsNullOrWhiteSpace(s.Name) ? "Source" : s.Name.Trim();
            if (string.IsNullOrEmpty(s.DeviceId)) s.DeviceId = "default";
            s.DeviceName ??= ""; s.MediaPath ??= ""; s.Filters ??= new();
            if (!Enum.IsDefined(s.Kind)) s.Kind = AudioInputKind.Microphone;
            if (!Enum.IsDefined(s.Monitoring)) s.Monitoring = AudioMonitoring.Off;
            s.Channel = s.Kind == AudioInputKind.Microphone ? Math.Clamp(s.Channel, 0, MaxEntrees) : 0;
            if (s.Kind != AudioInputKind.Media) s.FollowProjection = false;
            if (s.Kind == AudioInputKind.Media) s.UseDeviceTiming = null;
            s.VolumeDb = Bound(s.VolumeDb, -60, 6);
            s.Balance = Bound(s.Balance, 0, 1, 0.5);
            s.SyncOffsetMs = Math.Clamp(s.SyncOffsetMs, -950, 20000);
            s.Tracks &= ToutesLesPistes;
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
        for (int i = 0; i < VirtualSounds.Count; i++)
        {
            var v = VirtualSounds[i];
            v.Name = string.IsNullOrWhiteSpace(v.Name) ? "Sortie " + (char)('A' + i) : v.Name.Trim();
            v.DeviceId ??= ""; v.DeviceName ??= "";
            v.SourceIds = null;
            v.VolumeDb = Bound(v.VolumeDb, -60, 6);
            if (v.Track is < 1 or > Pistes) v.Track = 1;
        }
        for (int i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            if (p.Track is < 1 or > Pistes) p.Track = 1;
            p.SourceIds = null;
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "Enregistrement " + (i + 1) : p.Name.Trim();
            p.Color ??= "#B3261E"; p.Visuals ??= new(); p.Encoder ??= "auto"; p.Layout = p.Layout == "cote" ? "cote" : "incrustation";
            p.DisplayIds = (p.DisplayIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(6).ToList();
            if (p.DisplayIds.Count > 0) { p.AudioOnly = false; p.Format = "mkv"; }
            else { p.AudioOnly = true; if (p.Format is not ("wav" or "flac")) p.Format = "wav"; }
            p.AudioDelayMs = Math.Clamp(p.AudioDelayMs, -500, 1000);
            if (string.IsNullOrWhiteSpace(p.Directory)) p.Directory = Path.Combine(DossierEnregistrements, RecordingFiles.SafeName(p.Name));
        }
    }
}

/// <summary>
/// Blocs remis à l'enregistrement et aux sorties : 10 ms, stéréo entrelacée, à la fréquence du moteur (48 kHz, comme OBS par défaut).
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
