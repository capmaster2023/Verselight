using System.Diagnostics;
using System.Runtime.InteropServices;
using MyVerse.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

public sealed record AudioEndpoint(string Id, string Name, int Channels, int Rate)
{
    public override string ToString() => $"{Name} — {Channels} canal(aux), {Rate} Hz";
}

sealed class WindowsInput : IAudioInput, IDisposable
{
    readonly MMDevice? device;
    readonly WasapiCapture? capture;
    readonly AudioRing ring;
    readonly float[] converted;
    readonly int bits, bytes, rate;
    readonly bool floating;
    volatile string status = "Connexion…";
    public int Channels { get; }
    public string Status => status;
    public string EndpointId { get; }
    public bool Failed { get; private set; }
    public WindowsInput(MMDevice device, bool loopback)
    {
        this.device = device; EndpointId = device.ID;
        try
        {
        capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
        var format = capture.WaveFormat;
        Channels = format.Channels; rate = format.SampleRate; bits = format.BitsPerSample; bytes = bits / 8;
        floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible e && e.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        if (Channels < 1 || Channels > 64 || bits is not (16 or 24 or 32) || floating && bits != 32) throw new NotSupportedException("Format du pilote non pris en charge : " + format);
        ring = new(Channels, rate, (int)(rate * .06));
        converted = new float[Channels * rate]; // bounded one second, allocated before callback
        capture.DataAvailable += (_, e) =>
        {
            int total = e.BytesRecorded / bytes;
            for (int start = 0; start < total;)
            {
                int length = Math.Min(converted.Length, total - start); length -= length % Channels;
                if (length == 0) break;
                for (int i = 0; i < length; i++)
                {
                    int p = (start + i) * bytes;
                    converted[i] = floating ? BitConverter.ToSingle(e.Buffer, p) : bits switch
                    {
                        16 => BitConverter.ToInt16(e.Buffer, p) / 32768f,
                        24 => ((e.Buffer[p] | e.Buffer[p + 1] << 8 | e.Buffer[p + 2] << 16) << 8 >> 8) / 8388608f,
                        _ => BitConverter.ToInt32(e.Buffer, p) / 2147483648f
                    };
                }
                for (int i = 0; i < length; i++) if (!float.IsFinite(converted[i])) converted[i] = 0;
                ring.Write(converted.AsSpan(0, length)); start += length;
            }
            status = "Connecté";
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Failed = true; status = "Déconnecté : " + e.Exception.Message; } };
        capture.StartRecording();
        }
        catch { capture?.Dispose(); device.Dispose(); throw; }
    }
    public void Read(Span<float> destination) => ring.Read(destination, rate / 48000d);
    public string Diagnostics => $"{Channels} canaux partagés WASAPI ; sous-alimentations {ring.Underruns}, débordements {ring.Overruns}";
    public void Dispose() { try { capture?.StopRecording(); } catch (Exception) { } capture?.Dispose(); device?.Dispose(); status = "Déconnecté"; }
}

/// <summary>One shared decoder per path, stereo float 48 kHz. The worker owns all disk/COM work.
/// Playback pauses when not referenced by the graph; never uses system loopback.</summary>
sealed class MediaAudioInput : IAudioInput, IDisposable
{
    readonly CancellationTokenSource cancel = new();
    readonly AudioRing ring = new(2, 48000, 480);
    readonly Task worker;
    volatile string status = "Chargement du média…";
    public int Channels => 2;
    public string Status => status;
    public MediaAudioInput(string path)
    {
        worker = Task.Run(async () =>
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                using var resampler = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { ResamplerQuality = 60 };
                var bytes = new byte[AudioEngine.Samples * 4]; var floats = new float[AudioEngine.Samples];
                while (!cancel.IsCancellationRequested)
                {
                    if (ring.BufferedFrames > 4800) { await Task.Delay(5, cancel.Token); continue; }
                    int got = resampler.Read(bytes, 0, bytes.Length);
                    if (got == 0) { status = "Fin du média"; break; }
                    Buffer.BlockCopy(bytes, 0, floats, 0, got); ring.Write(floats.AsSpan(0, got / 4)); status = "Lecture";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { status = "Média : " + e.Message; Journal.Erreur("décodage audio", e); }
        });
    }
    public void Read(Span<float> destination) => ring.Read(destination, 1, false);
    public void Dispose() { cancel.Cancel(); _ = worker.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default); }
}

sealed class WindowsOutput : IDisposable
{
    readonly MMDevice device;
    readonly WasapiOut output;
    readonly BufferedWaveProvider buffer = new(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { BufferDuration = TimeSpan.FromMilliseconds(200), DiscardOnBufferOverflow = true };
    readonly byte[] bytes = new byte[AudioEngine.Samples * 4];
    public string Id => device.ID;
    public string[] Buses { get; }
    public bool Monitor { get; }
    public bool Failed { get; private set; }
    public readonly float[] Mix = new float[AudioEngine.Samples];
    public WindowsOutput(MMDevice device, string[] buses, bool monitor)
    {
        this.device = device; Buses = buses; Monitor = monitor;
        output = new(device, AudioClientShareMode.Shared, true, 60);
        output.PlaybackStopped += (_, e) => { if (e.Exception != null) Failed = true; };
        output.Init(buffer); output.Play();
    }
    public void Write()
    {
        // Safety ceiling on physical outputs; meters still show pre-ceiling overloads.
        for (int i = 0; i < Mix.Length; i++) Mix[i] = Math.Clamp(Mix[i], -1, 1);
        Buffer.BlockCopy(Mix, 0, bytes, 0, bytes.Length); buffer.AddSamples(bytes, 0, bytes.Length);
    }
    public void Dispose() { try { output.Stop(); } catch (Exception) { } output.Dispose(); device.Dispose(); }
}

public sealed class ChurchAudioService : IDisposable
{
    readonly MMDeviceEnumerator enumerator = new();
    readonly Dictionary<string, WindowsInput> captures = new();
    readonly Dictionary<string, MediaAudioInput> media = new();
    readonly Dictionary<string, IAudioInput> sourceInputs = new();
    WindowsOutput[] outputs = Array.Empty<WindowsOutput>();
    readonly System.Windows.Threading.DispatcherTimer health = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    long lastRefresh;
    bool disposed;
    readonly Thread render;
    readonly ManualResetEventSlim stop = new();
    readonly object renderGate = new();
    string outputSignature = "";
    long testUntil;
    double testPhase;
    bool running;
    public AudioConfiguration Config { get; }
    public AudioEngine Engine { get; } = new();
    public ChurchRecordingService Recorder { get; }
    public string Diagnostic { get; private set; } = "Arrêté — activez le moteur audio";
    public bool Running => running;
    public event Action? Changed;
    public ChurchAudioService(AudioConfiguration config)
    {
        Config = config;
        Recorder = new(this);
        render = new Thread(Render) { IsBackground = true, Name = "MyChurch audio 48 kHz", Priority = ThreadPriority.AboveNormal };
        render.Start();
        health.Tick += async (_, _) =>
        {
            if (disposed) return;
            try
            {
                await Recorder.PollAsync();
                if (running && Environment.TickCount64 - lastRefresh > 3000)
                {
                    lastRefresh = Environment.TickCount64;
                    Refresh();
                }
            }
            catch (Exception e) { Diagnostic = "Vérification audio : " + e.Message; Journal.Erreur("vérification audio", e); }
        };
        health.Start();
        App.Regie.Change += ProjectionChanged;
    }
    SourceDisplay? projectionMedia;
    void ProjectionChanged()
    {
        var next = App.Regie.Etat.Mode == "media" ? App.Regie.Etat.Media : null;
        if (Equals(next, projectionMedia)) return;
        projectionMedia = next;
        if (running && Config.Sources.Any(s => s.Kind == AudioInputKind.Media && s.FollowProjection)) Refresh();
    }
    public List<AudioEndpoint> Endpoints(bool input)
    {
        var results = new List<AudioEndpoint>();
        var list = enumerator.EnumerateAudioEndPoints(input ? DataFlow.Capture : DataFlow.Render, DeviceState.Active);
        foreach (var d in list) using (d)
        {
            try { var f = d.AudioClient.MixFormat; results.Add(new(d.ID, d.FriendlyName, f.Channels, f.SampleRate)); }
            catch (Exception e) { Journal.Erreur("format du périphérique audio", e); }
        }
        return results;
    }
    MMDevice Resolve(string id, bool input) => id.Length > 0 ? enumerator.GetDevice(id) : enumerator.GetDefaultAudioEndpoint(input ? DataFlow.Capture : DataFlow.Render, Role.Multimedia);
    public void Start() { running = true; Refresh(true); }
    public void Stop()
    {
        if (Recorder.Active) throw new InvalidOperationException("Arrêtez d’abord l’enregistrement.");
        running = false;
        lock (renderGate)
        {
            foreach (var o in outputs) o.Dispose(); outputs = Array.Empty<WindowsOutput>(); outputSignature = "";
            foreach (var c in captures.Values) c.Dispose(); captures.Clear();
            foreach (var m in media.Values) m.Dispose(); media.Clear(); sourceInputs.Clear();
            Engine.Configure(Config, _ => null);
        }
        Diagnostic = "Moteur arrêté"; Changed?.Invoke();
    }
    public void Apply(int fadeMs = 20)
    {
        Config.Normalize();
        // Persistence is on UI/control thread, never audio callback.
        App.Donnees.SauverReglages();
        if (running) Refresh(false, fadeMs);
        Changed?.Invoke();
    }
    public void RestartMedia(AudioSource source)
    {
        if (media.Remove(source.MediaPath, out var old)) old.Dispose();
        Refresh();
    }
    public void SynchronizeProfileMedia(RecordingProfile profile)
    {
        var paths = profile.Visuals.Where(v => v.Visible && v.Kind == "video").Select(v => v.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Config.Sources.Where(s => s.Kind == AudioInputKind.Media && !s.FollowProjection && profile.AllowedSources.Contains(s.Id) && paths.Contains(s.MediaPath)))
            if (media.Remove(source.MediaPath, out var old)) old.Dispose();
        if (paths.Count > 0) Refresh();
    }
    public void TestZoom() => Interlocked.Exchange(ref testUntil, Environment.TickCount64 + 2000);
    public void Refresh(bool force = false, int fadeMs = 20)
    {
        if (!running) return;
        var warnings = new List<string>();
        Config.Normalize();
        {
            var usedCaptures = new HashSet<string>(); var usedMedia = new HashSet<string>(); sourceInputs.Clear();
            foreach (var s in Config.Sources)
            {
                try
                {
                    if (s.Kind == AudioInputKind.Media)
                    {
                        var path = s.MediaPath;
                        if (s.FollowProjection) path = App.Regie.Etat.Mode == "media" && App.Regie.Etat.Media is { Type: "video" } v ? v.Chemin : "";
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        if (!File.Exists(path)) throw new IOException("Fichier média absent");
                        usedMedia.Add(path);
                        if (!media.TryGetValue(path, out var m)) media[path] = m = new(path);
                        sourceInputs[s.Id] = m; continue;
                    }
                    var d = Resolve(s.DeviceId, s.Kind == AudioInputKind.Capture); string key = (s.Kind == AudioInputKind.Loopback ? "loop:" : "in:") + d.ID;
                    usedCaptures.Add(key);
                    if (captures.TryGetValue(key, out var capture) && capture.Failed) { capture.Dispose(); captures.Remove(key); capture = null; }
                    if (capture == null) captures[key] = capture = new(d, s.Kind == AudioInputKind.Loopback); else d.Dispose();
                    if (s.Channel >= capture.Channels || s.Stereo && s.Channel + 1 >= capture.Channels)
                        throw new IOException($"Le mode partagé WASAPI expose {capture.Channels} canal(aux). Choisissez un canal exposé ou changez le mode dans le pilote Windows. ASIO n’est pas fourni.");
                    sourceInputs[s.Id] = capture;
                }
                catch (Exception e) { warnings.Add(s.Name + " : " + e.Message); }
            }
            foreach (var key in captures.Keys.Except(usedCaptures).ToArray()) { captures[key].Dispose(); captures.Remove(key); }
            foreach (var key in media.Keys.Except(usedMedia).ToArray()) { media[key].Dispose(); media.Remove(key); }
            // Resolve actual endpoint IDs before detecting capture/monitor feedback, including defaults.
            var routes = new Dictionary<string, List<string>>(); string? monitorId = null;
            foreach (var bus in Config.Buses.Where(b => b.OutputDeviceId != null))
            {
                try
                {
                    using var d = Resolve(bus.OutputDeviceId!, false);
                    if (captures.ContainsKey("loop:" + d.ID)) throw new IOException("Boucle évitée : cette sortie est aussi capturée par loopback. Choisissez une sortie distincte.");
                    if (!routes.TryGetValue(d.ID, out var buses)) routes[d.ID] = buses = new(); buses.Add(bus.Id);
                }
                catch (Exception e) { warnings.Add(bus.Name + " : " + e.Message); }
            }
            if (Config.MonitorDeviceId != null)
            {
                try
                {
                    using var d = Resolve(Config.MonitorDeviceId, false);
                    if (captures.ContainsKey("loop:" + d.ID) || routes.TryGetValue(d.ID, out var buses) && buses.Contains("zoom")) throw new IOException("Le casque doit être distinct du loopback et de la sortie Zoom.");
                    monitorId = d.ID; if (!routes.ContainsKey(d.ID)) routes[d.ID] = new();
                }
                catch (Exception e) { warnings.Add("Écoute : " + e.Message); }
            }
            string signature = string.Join("|", routes.Select(r => r.Key + ":" + string.Join(",", r.Value))) + ":" + monitorId;
            if (signature != outputSignature || force || outputs.Any(o => o.Failed))
            {
                var oldOutputs = Interlocked.Exchange(ref outputs, Array.Empty<WindowsOutput>());
                foreach (var output in oldOutputs) output.Dispose();
                var next = new List<WindowsOutput>();
                foreach (var route in routes)
                    try { next.Add(new(Resolve(route.Key, false), route.Value.ToArray(), route.Key == monitorId)); }
                    catch (Exception e) { warnings.Add("Sortie : " + e.Message); }
                Volatile.Write(ref outputs, next.ToArray()); outputSignature = signature;
            }
            lock (renderGate) Engine.Configure(ProcessingConfiguration(), s => sourceInputs.GetValueOrDefault(s.Id), Recorder.ActiveProfile, fadeMs);
        }
        Diagnostic = warnings.Count == 0 ? $"48 kHz · {captures.Count} capture(s) partagée(s) · {outputs.Length} sortie(s)" : string.Join("\n", warnings);
    }
    readonly Dictionary<string, List<AudioFilter>> filterPreviews = new();
    public void PreviewFilters(string sourceId, List<AudioFilter>? filters)
    {
        if (filters == null) filterPreviews.Remove(sourceId); else filterPreviews[sourceId] = AudioConfiguration.Copy(filters);
        Refresh();
    }
    AudioConfiguration ProcessingConfiguration()
    {
        if (filterPreviews.Count == 0) return Config;
        var copy = AudioConfiguration.Copy(Config);
        foreach (var source in copy.Sources) if (filterPreviews.TryGetValue(source.Id, out var filters)) source.Filters = AudioConfiguration.Copy(filters);
        return copy;
    }
    public void SetRecordingRestriction(RecordingProfile? profile)
    {
        lock (renderGate) Engine.Configure(ProcessingConfiguration(), s => sourceInputs.GetValueOrDefault(s.Id), profile, 20);
    }
    public bool InputReady(string id) => sourceInputs.ContainsKey(id);
    void Render()
    {
        var clock = Stopwatch.StartNew(); long next = 0;
        while (!stop.IsSet)
        {
            long now = clock.ElapsedMilliseconds;
            if (now < next) { stop.Wait((int)Math.Min(next - now, 10)); continue; }
            next += 10;
            if (now - next > 100) { next = now + 10; Recorder.ReportRenderGap(); }
            if (!running) continue;
            try
            {
                lock (renderGate)
                {
                    Engine.Process();
                    Recorder.AcceptAudio(Engine.BusData("record"));
                    foreach (var output in outputs)
                    {
                        Array.Clear(output.Mix);
                        foreach (var bus in output.Buses)
                        {
                            var data = Engine.BusData(bus);
                            for (int i = 0; i < data.Length; i++) output.Mix[i] += data[i];
                            if (bus == "zoom" && Environment.TickCount64 < Volatile.Read(ref testUntil))
                                for (int i = 0; i < AudioEngine.Samples; i += 2) { float sample = (float)(Math.Sin(testPhase) * .05); testPhase += 2 * Math.PI * 440 / 48000; output.Mix[i] += sample; output.Mix[i + 1] += sample; }
                        }
                        if (output.Monitor) for (int i = 0; i < AudioEngine.Samples; i++) output.Mix[i] += Engine.Monitor[i];
                        output.Write();
                    }
                }
            }
            catch (Exception e) { Diagnostic = "Moteur : " + e.Message; Recorder.Fail(Diagnostic); running = false; Journal.Erreur("moteur audio", e); }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; health.Stop(); App.Regie.Change -= ProjectionChanged;
        Recorder.Dispose(); running = false; stop.Set(); render.Join(3000);
        lock (renderGate) { foreach (var o in outputs) o.Dispose(); foreach (var c in captures.Values) c.Dispose(); foreach (var m in media.Values) m.Dispose(); }
        enumerator.Dispose(); stop.Dispose();
    }
}
