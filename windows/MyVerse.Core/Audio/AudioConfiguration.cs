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
    /// <summary>Pistes 1 à 6 où la source est envoyée (bit 0 = piste 1).</summary>
    public int Tracks { get; set; } = 1;
    /// <summary>Écoute au casque (monitoring OBS) : coupée, écoute seule (la source ne va plus dans les pistes) ou écoute et sortie.</summary>
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();
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
}
public sealed class RecordingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Enregistrement";
    public string Color { get; set; } = "#B3261E";
    public string Directory { get; set; } = "";
    public bool AudioOnly { get; set; } = true;
    public string Format { get; set; } = "wav"; // wav, flac or mkv (video)
    /// <summary>Piste OBS enregistrée (1 à 6).</summary>
    public int Track { get; set; } = 1;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 30;
    public int VideoKbps { get; set; } = 5000;
    public int AudioKbps { get; set; } = 192;
    public string Encoder { get; set; } = "mjpeg";
    public List<RecordingVisual> Visuals { get; set; } = new() { new() };
}

/// <summary>MyVirtualSound : le mix d'une piste d'OBS envoyé vers un périphérique de sortie (par exemple « CABLE Input » de VB-CABLE, que Zoom reçoit comme micro).</summary>
public sealed class VirtualSoundSettings
{
    public bool Enabled { get; set; }
    /// <summary>« default » = sortie Windows par défaut.</summary>
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int Track { get; set; } = 1;
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
}

public sealed class AudioConfiguration
{
    public const int Actuelle = 3;
    public const int Pistes = 6;
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
    public VirtualSoundSettings VirtualSound { get; set; } = new();
    /// <summary>
    /// Sources du mélangeur OBS. Le nom JSON « obsSources » est voulu : la liste « sources » de la 1.2 bêta
    /// (types « Capture »/« Loopback », filtres « Gain », « Equalizer »…) est ainsi ignorée au lieu de faire échouer la lecture des réglages.
    /// </summary>
    [JsonPropertyName("obsSources")]
    public List<AudioSource> Sources { get; set; } = new();
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
        Sources ??= new(); Profiles ??= new(); FfmpegPath ??= ""; VirtualSound ??= new();
        VirtualSound.DeviceId ??= ""; VirtualSound.DeviceName ??= "";
        VirtualSound.Track = Math.Clamp(VirtualSound.Track, 1, Pistes); VirtualSound.VolumeDb = Bound(VirtualSound.VolumeDb, -60, 6);
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
        if (Profiles.Count == 0) Profiles.Add(new RecordingProfile { Name = "Enregistrement", Format = "wav", AudioOnly = true, Track = 1 });
        if (Profiles.Count > 4) Profiles.RemoveRange(4, Profiles.Count - 4);
        foreach (var p in Profiles) { p.Track = Math.Clamp(p.Track, 1, Pistes); p.Visuals ??= new(); if (string.IsNullOrWhiteSpace(p.Directory)) p.Directory = DossierEnregistrements; p.Name ??= "Enregistrement"; p.Color ??= "#B3261E"; }
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
