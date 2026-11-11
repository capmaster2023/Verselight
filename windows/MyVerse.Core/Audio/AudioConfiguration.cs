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
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public bool Mono { get; set; }
    /// <summary>Pistes 1 à 6 où la source est envoyée (bit 0 = piste 1). Calculé par <see cref="AudioConfiguration.AttribuerPistes"/>
    /// d'après les enregistrements en cours et les sorties MyVirtualSound actives qui contiennent la source.</summary>
    public int Tracks { get; set; } = 1;
    /// <summary>Écoute au casque (monitoring OBS) : coupée, écoute seule (la source ne va plus dans les pistes) ou écoute et sortie.</summary>
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();
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
    /// <summary>4 : enregistrements choisis par l'utilisateur (MyChurch 3.0.1).</summary>
    public const int Actuelle = 4;
    public const int Pistes = 6;
    public const int MaxEnregistrements = 5;
    public const int MaxSortiesVirtuelles = 4;
    public int Version { get; set; } = Actuelle;
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
        Version = Actuelle;
        if (string.IsNullOrEmpty(MonitorDeviceId)) MonitorDeviceId = "default";
        MonitorDeviceName ??= "";
        if (string.IsNullOrEmpty(MicrophoneId)) MicrophoneId = "default";
        if (string.IsNullOrEmpty(OutputId)) OutputId = "default";
        MicrophoneName ??= ""; OutputName ??= "";
        var ids = new HashSet<string>();
        Sources.RemoveAll(s => s == null || string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id));
        if (Sources.Count > 32) Sources.RemoveRange(32, Sources.Count - 32);
        foreach (var s in Sources)
        {
            s.Name = string.IsNullOrWhiteSpace(s.Name) ? "Source" : s.Name.Trim();
            if (string.IsNullOrEmpty(s.DeviceId)) s.DeviceId = "default";
            s.DeviceName ??= ""; s.MediaPath ??= ""; s.Filters ??= new();
            if (!Enum.IsDefined(s.Kind)) s.Kind = AudioInputKind.Microphone;
            if (!Enum.IsDefined(s.Monitoring)) s.Monitoring = AudioMonitoring.Off;
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

/// <summary>Blocs remis à l'enregistrement : 10 ms, stéréo entrelacée, 48 kHz.</summary>
public static class AudioBlock
{
    public const int Rate = 48000;
    public const int Frames = 480;
    public const int Samples = Frames * 2;
}
public static class AudioMath
{
    public static double Linear(double db) => db <= -60 ? 0 : Math.Pow(10, db / 20);
    public static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));
}
