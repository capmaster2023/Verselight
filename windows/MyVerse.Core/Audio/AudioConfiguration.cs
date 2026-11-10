using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse.Audio;

public enum AudioInputKind { Capture, Loopback, Media }
public enum AudioFilterKind { Gain, HighPass, LowPass, Equalizer, Gate, Expander, Compressor, Sidechain, Limiter, NoiseReduction }
public enum AudioMonitoring { Off, Only, AndOutput }
public sealed class AudioFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Filtre";
    public AudioFilterKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public double GainDb { get; set; }
    public double Frequency { get; set; } = 100;
    public double Q { get; set; } = .707;
    public double ThresholdDb { get; set; } = -30;
    public double Ratio { get; set; } = 4;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 150;
    public double HoldMs { get; set; } = 50;
    public string SidechainSourceId { get; set; } = "";
}
public sealed class AudioSend
{
    public bool Enabled { get; set; } = true;
    public bool Muted { get; set; }
    public double VolumeDb { get; set; }
}
public sealed class AudioSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Source";
    public string Color { get; set; } = "#8A5A12";
    public AudioInputKind Kind { get; set; }
    // Empty ID means Windows' current default endpoint. Concrete IDs are retained across reconnects.
    public string DeviceId { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public bool FollowProjection { get; set; }
    public int Channel { get; set; }
    public bool Stereo { get; set; }
    public bool Muted { get; set; }
    public bool Enabled { get; set; } = true;
    public double VolumeDb { get; set; }
    public double Balance { get; set; }
    public bool Mono { get; set; }
    public bool InvertPolarity { get; set; }
    public int DelayMs { get; set; }
    public string ExclusionGroup { get; set; } = "";
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();
    public Dictionary<string, AudioSend> Sends { get; set; } = new()
    {
        ["salle"] = new(), ["zoom"] = new(), ["record"] = new()
    };
}
public sealed class AudioBus
{
    public string Id { get; set; } = "salle";
    public string Name { get; set; } = "Salle";
    // null disables physical output; empty string explicitly selects Windows default.
    public string? OutputDeviceId { get; set; }
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public bool Mono { get; set; }
}
public sealed class AudioScene
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Mode";
    public int FadeMs { get; set; } = 400;
    public List<AudioSource> Sources { get; set; } = new();
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
    public string Color { get; set; } = "#B42318";
    public string Icon { get; set; } = "●";
    public string Directory { get; set; } = "";
    public bool AudioOnly { get; set; } = true;
    public string Format { get; set; } = "wav"; // wav, flac or mkv (video)
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 30;
    public int VideoKbps { get; set; } = 5000;
    public int AudioKbps { get; set; } = 192;
    public string Encoder { get; set; } = "mjpeg";
    public HashSet<string> AllowedSources { get; set; } = new();
    public Dictionary<string, double> SendDb { get; set; } = new();
    public bool ApplyToRoom { get; set; }
    public List<RecordingVisual> Visuals { get; set; } = new() { new() };
}
public sealed class AudioConfiguration
{
    public int Version { get; set; } = 1;
    public int SampleRate { get; set; } = 48000;
    public string FfmpegPath { get; set; } = "";
    public string? MonitorDeviceId { get; set; }
    public double MonitorDb { get; set; } = -12;
    public bool ListenZoom { get; set; }
    public List<AudioSource> Sources { get; set; } = new();
    public List<AudioBus> Buses { get; set; } = new()
    {
        new() { Id = "salle", Name = "Salle" }, new() { Id = "zoom", Name = "Zoom" }, new() { Id = "record", Name = "Enregistrement" }
    };
    public List<AudioScene> Scenes { get; set; } = new[] { "Prédication", "Louange", "Prière", "Solo" }.Select(n => new AudioScene { Name = n }).ToList();
    public List<RecordingProfile> Profiles { get; set; } = new();
    public Dictionary<string, List<AudioFilter>> FilterPresets { get; set; } = new();
    public static T Copy<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Donnees.Json), Donnees.Json)!;
    public static double Bound(double x, double min, double max, double fallback = 0) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;
    public void Normalize()
    {
        SampleRate = 48000;
        Sources ??= new(); Buses ??= new(); Profiles ??= new(); Scenes ??= new(); FilterPresets ??= new();
        if (Profiles.Count > 4) throw new InvalidDataException("Quatre profils d’enregistrement maximum.");
        if (Buses.Count > 6) throw new InvalidDataException("Six bus maximum.");
        foreach (var id in new[] { "salle", "zoom", "record" })
            if (!Buses.Any(b => b.Id == id)) Buses.Add(new() { Id = id, Name = id == "record" ? "Enregistrement" : id == "zoom" ? "Zoom" : "Salle" });
        if (Buses.Count > 6 || Buses.Select(b => b.Id).Distinct().Count() != Buses.Count) throw new InvalidDataException("Identifiants de bus invalides.");
        var ids = new HashSet<string>();
        foreach (var s in Sources)
        {
            if (!ids.Add(s.Id)) throw new InvalidDataException("Identifiants de sources en double.");
            s.Filters ??= new(); s.Sends ??= new(); s.ExclusionGroup ??= ""; s.DeviceId ??= ""; s.MediaPath ??= ""; s.Name ??= "Source"; s.Color ??= "#8A5A12";
            s.Channel = Math.Clamp(s.Channel, 0, 63); s.DelayMs = Math.Clamp(s.DelayMs, 0, 2000);
            s.VolumeDb = Bound(s.VolumeDb, -90, 24); s.Balance = Bound(s.Balance, -1, 1);
            foreach (var b in Buses) if (!s.Sends.ContainsKey(b.Id)) s.Sends[b.Id] = new() { Enabled = false };
            foreach (var send in s.Sends.Values) send.VolumeDb = Bound(send.VolumeDb, -90, 12);
            foreach (var f in s.Filters)
            {
                f.SidechainSourceId ??= ""; f.Name ??= "Filtre";
                f.GainDb = Bound(f.GainDb, -60, 30); f.Frequency = Bound(f.Frequency, 20, 20000, 100);
                f.Q = Bound(f.Q, .1, 20, .707); f.Ratio = Bound(f.Ratio, 1, 100, 4);
                f.ThresholdDb = Bound(f.ThresholdDb, -90, 0, -30);
                f.AttackMs = Bound(f.AttackMs, .1, 2000, 10); f.ReleaseMs = Bound(f.ReleaseMs, 1, 5000, 150); f.HoldMs = Bound(f.HoldMs, 0, 2000, 50);
            }
        }
        // A corrupt persisted group must not unexpectedly sum two profiles of one microphone.
        foreach (var g in Sources.Where(s => s.Enabled && s.ExclusionGroup.Length > 0).GroupBy(s => s.ExclusionGroup))
            foreach (var s in g.Skip(1)) s.Enabled = false;
        foreach (var b in Buses) b.VolumeDb = Bound(b.VolumeDb, -90, 12);
        MonitorDb = Bound(MonitorDb, -90, 0, -12);
    }
    public void Activate(string id, bool enabled)
    {
        var source = Sources.Single(s => s.Id == id);
        source.Enabled = enabled;
        if (enabled && source.ExclusionGroup.Length > 0)
            foreach (var other in Sources.Where(s => s.Id != id && s.ExclusionGroup == source.ExclusionGroup)) other.Enabled = false;
    }
    public AudioScene Snapshot(string name, int fadeMs = 400) => new() { Name = name, FadeMs = fadeMs, Sources = Copy(Sources) };
    public void ApplyScene(AudioScene scene)
    {
        foreach (var stored in scene.Sources)
        {
            var source = Sources.FirstOrDefault(s => s.Id == stored.Id);
            if (source == null) continue;
            source.Muted = stored.Muted; source.Enabled = stored.Enabled; source.VolumeDb = stored.VolumeDb;
            source.Sends = Copy(stored.Sends); source.Filters = Copy(stored.Filters);
        }
        Normalize();
    }
}

/// <summary>Temporary allow-list and gains, never serialized into permanent faders/mutes.</summary>
public sealed class RecordingRestriction
{
    public HashSet<string> Allowed { get; }
    public IReadOnlyDictionary<string, double> SendDb { get; }
    public bool Room { get; }
    public RecordingRestriction(RecordingProfile profile)
    {
        Allowed = new(profile.AllowedSources); SendDb = new Dictionary<string, double>(profile.SendDb); Room = profile.ApplyToRoom;
    }
    public bool Allows(string sourceId, string bus) => bus != "zoom" && bus != "record" && !(Room && bus == "salle") || Allowed.Contains(sourceId);
    public double Gain(string sourceId, string bus) => bus == "record" && SendDb.TryGetValue(sourceId, out var db) ? AudioMath.Linear(db) : 1;
}
public static class AudioMath
{
    public static double Linear(double db) => db <= -90 ? 0 : Math.Pow(10, db / 20);
    public static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));
}
