using MyVerse.Audio;

namespace MyVerse.Tests;
public static class AudioTests
{
    sealed class Signal : IAudioInput
    {
        public int Channels { get; set; } = 2;
        public string Status => "Synthétique";
        public int Reads;
        public float Left = .1f, Right = .5f;
        public void Read(Span<float> data) { Reads++; for (int i = 0; i < data.Length; i += Channels) { data[i] = Left; if (Channels > 1) data[i + 1] = Right; } }
    }
    static double Average(ReadOnlySpan<float> x) { double sum = 0; foreach (float s in x) sum += Math.Abs(s); return sum / Math.Max(1, x.Length); }
    static void Settle(AudioEngine engine, int blocks = 100) { for (int i = 0; i < blocks; i++) engine.Process(); }
    public static void Run(Action<bool, string, string?> ok)
    {
        void Check(bool value, string name) => ok(value, name, null);
        var config = new AudioConfiguration();
        AudioSource singer = new AudioSource { Name = "Chanteur", Channel = 0 }, pastor = new AudioSource { Name = "Pasteur", Channel = 0 }, guitar = new AudioSource { Name = "Guitare", Channel = 1 };
        config.Sources.AddRange(new[] { singer, pastor, guitar });
        var input = new Signal(); var engine = new AudioEngine(); engine.Configure(config, _ => input); Settle(engine);
        Check(input.Reads == 100, "Une entrée physique lue une fois pour trois sources logiques");
        Check(Math.Abs(engine.Meter(singer.Id)!.Rms - .1) < .001 && Math.Abs(engine.Meter(guitar.Id)!.Rms - .5) < .001, "Séparation entrée 1 / entrée 2");
        Check(Math.Abs(Average(engine.BusData("salle")) - .7) < .001, "Distribution de l’entrée 1 aux profils Chanteur et Pasteur");
        singer.Sends["zoom"].Muted = true; engine.Configure(config, _ => input); Settle(engine);
        Check(Math.Abs(Average(engine.BusData("zoom")) - .6) < .001 && Math.Abs(Average(engine.BusData("salle")) - .7) < .001, "Indépendance Zoom / salle");
        pastor.VolumeDb = -6.020599913; engine.Configure(config, _ => input); Settle(engine);
        Check(Math.Abs(engine.Meter(pastor.Id)!.Rms - .05) < .001, "Fader en dB réellement appliqué");
        singer.ExclusionGroup = pastor.ExclusionGroup = "Focusrite-1"; config.Activate(pastor.Id, true); engine.Configure(config, _ => input); Settle(engine);
        Check(!singer.Enabled && pastor.Enabled, "Exclusion Chanteur / Pasteur");
        var profile = new RecordingProfile { Name = "Prédication", AllowedSources = new() { pastor.Id } };
        engine.Configure(config, _ => input, profile); Settle(engine);
        Check(Math.Abs(Average(engine.BusData("record")) - .05) < .001 && Math.Abs(Average(engine.BusData("zoom")) - .05) < .001, "Restrictions d’enregistrement sur Zoom et fichier");
        Check(Math.Abs(Average(engine.BusData("salle")) - .55) < .001, "Salle indépendante du profil");
        var scene = config.Snapshot("Louange"); scene.Sources.ForEach(s => { s.Enabled = true; s.Muted = false; s.VolumeDb = 0; }); config.ApplyScene(scene);
        engine.Configure(config, _ => input, profile); Settle(engine);
        Check(Average(engine.BusData("record")) <= .101 && Average(engine.BusData("zoom")) <= .101, "Un changement de mode ne réactive pas les sources exclues");
        guitar.VolumeDb = -12; guitar.Sends["zoom"].Muted = true; engine.Configure(config, _ => input, profile); Settle(engine);
        engine.Configure(config, _ => input, null); Settle(engine);
        Check(guitar.VolumeDb == -12 && guitar.Sends["zoom"].Muted, "Retrait de l’overlay conserve les modifications manuelles");
        Check(Average(engine.BusData("salle")) > .12, "Retrait des restrictions restaure les envois normaux");
        profile.ApplyToRoom = true; engine.Configure(config, _ => input, profile); Settle(engine);
        Check(Average(engine.BusData("salle")) <= .101, "Option explicite : appliquer aussi à la salle");
        pastor.Muted = true; config.Activate(pastor.Id, true); engine.Configure(config, _ => input, profile); Settle(engine);
        Check(Average(engine.BusData("record")) < .00001, "L’allow-list ne réactive pas une sourdine explicite");
        // Preparing a candidate must not mutate an existing live configuration on failure.
        var saved = AudioConfiguration.Copy(config);
        try { var bad = new RecordingProfile(); RecordingFiles.Validate(bad); } catch (InvalidDataException) { }
        Check(System.Text.Json.JsonSerializer.Serialize(config, Donnees.Json) == System.Text.Json.JsonSerializer.Serialize(saved, Donnees.Json), "Prévalidation échouée sans écrasement de l’état audio");
        var gain = new AudioFilter { Kind = AudioFilterKind.Gain, GainDb = 20 };
        var limiter = new AudioFilter { Kind = AudioFilterKind.Limiter, ThresholdDb = -6, AttackMs = .1, Ratio = 100 };
        var data = Enumerable.Repeat(.8f, AudioEngine.Samples).ToArray(); new AudioFilterChain(new[] { gain, limiter }).Process(data);
        Check(data.Max(v => Math.Abs(v)) <= AudioMath.Linear(-6) + .0001, "Limiteur stéréo après gain : plafond respecté dès le premier échantillon");
        var reversed = Enumerable.Repeat(.8f, AudioEngine.Samples).ToArray(); new AudioFilterChain(new[] { limiter, gain }).Process(reversed);
        Check(reversed.Max() > data.Max() * 5, "L’ordre des filtres change réellement le signal");
        var hp = new AudioFilterChain(new[] { new AudioFilter { Kind = AudioFilterKind.HighPass, Frequency = 200, Q = .707 } });
        for (int i = 0; i < 20; i++) { Array.Fill(data, 1f); hp.Process(data); }
        Check(Average(data) < .00001, "Passe-haut supprime la composante continue");
        var eq = new AudioFilterChain(new[] { new AudioFilter { Kind = AudioFilterKind.Equalizer, Frequency = 1000, GainDb = -12 } });
        for (int n = 0; n < 20; n++) { for (int i = 0; i < data.Length; i += 2) data[i] = data[i + 1] = (float)Math.Sin(2 * Math.PI * 1000 * (i / 2 + n * 480) / 48000); eq.Process(data); }
        Check(Average(data) < .2, "Égaliseur paramétrique atténue la fréquence choisie");
        var sidechain = new AudioFilterChain(new[] { new AudioFilter { Kind = AudioFilterKind.Sidechain, SidechainSourceId = "pastor", ThresholdDb = -30, Ratio = 10, AttackMs = .1 } });
        for (int n = 0; n < 10; n++) { Array.Fill(data, .5f); sidechain.Process(data, new Dictionary<string, double> { ["pastor"] = .8 }); }
        Check(Average(data) < .05, "Sidechain piloté par une autre source");
        var gate = new AudioFilterChain(new[] { new AudioFilter { Kind = AudioFilterKind.Gate, ThresholdDb = -30, HoldMs = 0, AttackMs = .1, ReleaseMs = 1 } }); Array.Fill(data, .001f); gate.Process(data);
        Check(Average(data) < .00001, "Gate ferme sous le seuil");
        var noise = new AudioFilterChain(new[] { new AudioFilter { Kind = AudioFilterKind.NoiseReduction, ThresholdDb = -45 } });
        var random = new Random(42); double beforeNoise = 0;
        for (int n = 0; n < 20; n++) { for (int i = 0; i < data.Length; i++) data[i] = (float)(random.NextDouble() - .5) * .002f; beforeNoise = Average(data); noise.Process(data); }
        Check(Average(data) < beforeNoise * .5 && data.All(float.IsFinite), "Suppression spectrale atténue un faible bruit sans NaN");
        var ring = new AudioRing(2, 2000, 100); var raw = new float[1000]; for (int i = 0; i < raw.Length; i += 2) { raw[i] = .2f; raw[i + 1] = .6f; } ring.Write(raw); var got = new float[400]; ring.Read(got, 1, false);
        Check(Math.Abs(got[0] - .2) < .001 && Math.Abs(got[1] - .6) < .001, "Tampon conserve les canaux physiques");
        for (int i = 0; i < 10; i++) ring.Write(raw); Check(ring.Overruns > 0 && ring.BufferedFrames <= 2000, "Tampons bornés et débordements comptés");
        for (int i = 0; i < 20; i++) ring.Read(got); Check(ring.Underruns > 0, "Sous-alimentations détectées");
        var check = SoundCheck.Analyze(.01 * 48000, 48000, .9, .001, 0); Check(!check.Clipped && !check.NoSignal && check.Proposed.Count == 4, "Balance : propositions issues des mesures");
        Check(SoundCheck.Analyze(1, 48000, 1, .001, 1).Clipped, "Balance : saturation physique signalée");
        Check(SoundCheck.Analyze(0, 48000, 0, 0, 0).NoSignal, "Balance : absence de signal détectée");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Reglages>("{\"version\":4,\"theme\":\"lin\"}", Donnees.Json)!;
        Check(legacy.Audio.Sources.Count == 0 && legacy.Audio.Buses.Count == 3 && legacy.Theme == "lin", "Migration 1.1.2 : configuration audio vide sans perte des réglages");
        var roundTrip = AudioConfiguration.Copy(config); roundTrip.Normalize(); Check(roundTrip.Sources.Count == 3 && roundTrip.Sources[1].ExclusionGroup == "Focusrite-1", "Persistance des groupes, sources et canaux");
        var tooMany = new AudioConfiguration { Profiles = Enumerable.Range(0, 5).Select(_ => new RecordingProfile()).ToList() }; bool rejected = false; try { tooMany.Normalize(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Configuration : cinquième profil refusé");
        var fixtureDir = Environment.GetEnvironmentVariable("MYCHURCH_AUDIO_FIXTURES") ?? Path.Combine(Path.GetTempPath(), "mychurch-audio-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtureDir);
        string file = RecordingFiles.ReservePath(fixtureDir, "Test:<>/\\", "wav");
        using (var writer = new PcmRecordingWriter(file))
        {
            for (int n = 0; n < 100; n++)
            {
                for (int i = 0; i < data.Length; i += 2) { data[i] = (float)(.25 * Math.Sin(2 * Math.PI * 440 * (n * 480 + i / 2) / 48000)); data[i + 1] = (float)(.25 * Math.Sin(2 * Math.PI * 880 * (n * 480 + i / 2) / 48000)); }
                writer.Write(data);
            }
        }
        byte[] header = File.ReadAllBytes(file); Check(header.Length == 288080 && BitConverter.ToInt64(header, 36) == 48000, "Fichier RF64 réel : 48 000 trames stéréo 24 bits");
        string recovery = Path.Combine(fixtureDir, "recovered.wav"); PcmRecordingWriter.RecoverCopy(file, recovery); Check(File.ReadAllBytes(recovery).SequenceEqual(header), "Récupération en copie sans changer l’original");
        string second = RecordingFiles.ReservePath(fixtureDir, "Test:<>/\\", "wav"); Check(second != file, "Nommage sûr sans écrasement");
        if (Environment.GetEnvironmentVariable("MYCHURCH_AUDIO_FIXTURES") == null) Directory.Delete(fixtureDir, true);
    }
}
