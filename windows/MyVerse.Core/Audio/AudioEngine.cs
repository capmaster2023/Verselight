namespace MyVerse.Audio;

public interface IAudioInput
{
    int Channels { get; }
    string Status { get; }
    void Read(Span<float> interleaved);
}
public sealed class AudioEngine
{
    public const int Frames = 480, Samples = Frames * 2;
    sealed class InputBlock
    {
        public readonly IAudioInput Input;
        public readonly float[] Data;
        public InputBlock(IAudioInput input) { Input = input; Data = new float[Frames * input.Channels]; }
    }
    sealed class State
    {
        public readonly AudioMeter Meter = new(), Raw = new();
        public readonly float[] Delay = new float[48000 * 2 * 2 + Samples];
        public int DelayPosition;
        public double Volume, Left = 1, Right = 1;
        public readonly Dictionary<string, double> Sends = new();
    }
    sealed class Node
    {
        public readonly AudioSource Source;
        public readonly State State;
        public readonly InputBlock? Input;
        public readonly float[] Data = new float[Samples], OldData = new float[Samples];
        public readonly AudioFilterChain Chain;
        public AudioFilterChain? PreviousChain;
        public int FadeFilter;
        public Node(AudioSource source, InputBlock? input, Node? old)
        {
            Source = source; Input = input; State = old?.State ?? new();
            foreach (var id in source.Sends.Keys) if (!State.Sends.ContainsKey(id)) State.Sends[id] = 0;
            bool same = old != null && System.Text.Json.JsonSerializer.Serialize(source.Filters, Donnees.Json) == System.Text.Json.JsonSerializer.Serialize(old.Source.Filters, Donnees.Json);
            Chain = same ? old!.Chain : new(source.Filters, old?.Chain);
            // mêmes filtres, paramètres changés : l'état est repris, pas de fondu ; structure changée : fondu de 20 ms
            if (!same && old != null && !Chain.Inherited) { PreviousChain = old.Chain; FadeFilter = 960; }
        }
    }
    sealed class Bus
    {
        public readonly AudioBus Config;
        public readonly float[] Data = new float[Samples];
        public readonly AudioMeter Meter;
        public double Gain;
        public Bus(AudioBus config, Bus? old) { Config = config; Meter = old?.Meter ?? new(); Gain = old?.Gain ?? 0; }
    }
    sealed class Graph
    {
        public Node[] Nodes = Array.Empty<Node>();
        public InputBlock[] Inputs = Array.Empty<InputBlock>();
        public Bus[] Buses = Array.Empty<Bus>();
        public readonly Dictionary<string, double> Levels = new();
        public AudioConfiguration Config = new();
        public RecordingRestriction? Restriction;
        public double Smooth = 1d / 960;
    }
    Graph graph = new();
    public long ProcessedFrames { get; private set; }
    public string? ActiveProfileId { get; private set; }
    // Must be called on control thread. Snapshot/allocations are outside the audio render thread.
    public void Configure(AudioConfiguration config, Func<AudioSource, IAudioInput?> input, RecordingProfile? restriction = null, int fadeMs = 20)
    {
        var copy = AudioConfiguration.Copy(config); copy.Normalize(); var old = Volatile.Read(ref graph);
        var next = new Graph { Config = copy, Restriction = restriction == null ? null : new(restriction), Smooth = 1 - Math.Exp(-6.907755278982137 / Math.Max(1, fadeMs * 48)) };
        var inputs = new Dictionary<IAudioInput, InputBlock>();
        next.Nodes = copy.Sources.Select(source =>
        {
            var capture = input(source); InputBlock? block = null;
            if (capture != null && !inputs.TryGetValue(capture, out block)) { block = new(capture); inputs[capture] = block; }
            next.Levels[source.Id] = 0;
            return new Node(source, block, old.Nodes.FirstOrDefault(n => n.Source.Id == source.Id));
        }).ToArray();
        next.Inputs = inputs.Values.ToArray();
        next.Buses = copy.Buses.Select(b => new Bus(b, old.Buses.FirstOrDefault(o => o.Config.Id == b.Id))).ToArray();
        ActiveProfileId = restriction?.Id;
        Volatile.Write(ref graph, next);
    }
    public AudioMeter? Meter(string id) { var g = Volatile.Read(ref graph); return g.Nodes.FirstOrDefault(n => n.Source.Id == id)?.State.Meter ?? g.Buses.FirstOrDefault(b => b.Config.Id == id)?.Meter; }
    public AudioMeter? RawMeter(string id) => Volatile.Read(ref graph).Nodes.FirstOrDefault(n => n.Source.Id == id)?.State.Raw;
    public string Status(string id) { var n = Volatile.Read(ref graph).Nodes.FirstOrDefault(n => n.Source.Id == id); return n?.Input?.Input.Status ?? "Périphérique absent"; }
    public void ResetMeters() { foreach (var n in Volatile.Read(ref graph).Nodes) { n.State.Meter.Reset(); n.State.Raw.Reset(); } }
    public ReadOnlySpan<float> BusData(string id) { foreach (var b in Volatile.Read(ref graph).Buses) if (b.Config.Id == id) return b.Data; return ReadOnlySpan<float>.Empty; }
    public readonly float[] Monitor = new float[Samples];
    public void Process()
    {
        var g = Volatile.Read(ref graph);
        foreach (var input in g.Inputs) input.Input.Read(input.Data);
        foreach (var bus in g.Buses) Array.Clear(bus.Data);
        Array.Clear(Monitor);
        // Pre-filter/raw sidechain values, same block for every source (no order-dependent graph).
        foreach (var node in g.Nodes)
        {
            var source = node.Source; var data = node.Data; Array.Clear(data);
            if (node.Input is { } input)
            {
                int ch = input.Input.Channels;
                if (source.Channel < ch && (!source.Stereo || source.Channel + 1 < ch))
                    for (int i = 0; i < Frames; i++) { float left = input.Data[i * ch + source.Channel], right = input.Data[i * ch + source.Channel + (source.Stereo ? 1 : 0)]; data[i * 2] = float.IsFinite(left) ? left : 0; data[i * 2 + 1] = float.IsFinite(right) ? right : 0; }
            }
            node.State.Raw.Measure(data);
            g.Levels[source.Id] = source.Enabled && !source.Muted ? node.State.Raw.Rms : 0;
        }
        foreach (var node in g.Nodes)
        {
            var s = node.Source; var state = node.State; var data = node.Data;
            if (node.PreviousChain != null) data.CopyTo(node.OldData, 0);
            node.Chain.Process(data, g.Levels);
            if (node.PreviousChain is { } previous)
            {
                previous.Process(node.OldData, g.Levels);
                for (int i = 0; i < Samples; i += 2)
                {
                    double wet = 1 - Math.Max(0, node.FadeFilter--) / 960d;
                    data[i] = (float)(data[i] * wet + node.OldData[i] * (1 - wet)); data[i + 1] = (float)(data[i + 1] * wet + node.OldData[i + 1] * (1 - wet));
                }
                if (node.FadeFilter <= 0) node.PreviousChain = null;
            }
            double wanted = s.Enabled && !s.Muted ? AudioMath.Linear(s.VolumeDb) : 0;
            double left = Math.Min(1, 1 - s.Balance), right = Math.Min(1, 1 + s.Balance);
            for (int i = 0; i < Samples; i += 2)
            {
                state.Volume += (wanted - state.Volume) * g.Smooth;
                state.Left += (left - state.Left) * g.Smooth; state.Right += (right - state.Right) * g.Smooth;
                double l = data[i], r = data[i + 1]; if (s.Mono) l = r = (l + r) * .5;
                if (s.InvertPolarity) { l = -l; r = -r; }
                int delay = s.DelayMs * 48 * 2, pos = state.DelayPosition;
                state.Delay[pos] = (float)l; state.Delay[pos + 1] = (float)r;
                int back = (pos - delay + state.Delay.Length) % state.Delay.Length;
                data[i] = (float)(state.Delay[back] * state.Volume * state.Left); data[i + 1] = (float)(state.Delay[back + 1] * state.Volume * state.Right);
                state.DelayPosition = (pos + 2) % state.Delay.Length;
            }
            state.Meter.Measure(data);
            if (!g.Config.ListenZoom && s.Monitoring != AudioMonitoring.Off && g.Config.MonitorDeviceId != null)
                for (int i = 0; i < Samples; i++) Monitor[i] += data[i];
            foreach (var bus in g.Buses)
            {
                if (!s.Sends.TryGetValue(bus.Config.Id, out var send)) continue;
                bool allowed = g.Restriction?.Allows(s.Id, bus.Config.Id) ?? true;
                double sendTarget = allowed && send.Enabled && !send.Muted && s.Monitoring != AudioMonitoring.Only ? AudioMath.Linear(send.VolumeDb) * (g.Restriction?.Gain(s.Id, bus.Config.Id) ?? 1) : 0;
                // Restrictions close immediately to guarantee no excluded source leaks into a new recording.
                // fermeture très rapide (environ 3 ms) mais sans clic sur le bus Zoom en direct
                double pas = allowed ? g.Smooth : Math.Max(g.Smooth, 1d / 24);
                double gain = state.Sends[bus.Config.Id];
                for (int i = 0; i < Samples; i += 2)
                {
                    gain += (sendTarget - gain) * pas;
                    bus.Data[i] += (float)(data[i] * gain); bus.Data[i + 1] += (float)(data[i + 1] * gain);
                }
                state.Sends[bus.Config.Id] = gain;
            }
        }
        foreach (var bus in g.Buses)
        {
            double target = bus.Config.Muted ? 0 : AudioMath.Linear(bus.Config.VolumeDb);
            for (int i = 0; i < Samples; i += 2)
            {
                bus.Gain += (target - bus.Gain) * g.Smooth;
                if (bus.Config.Mono) bus.Data[i] = bus.Data[i + 1] = (bus.Data[i] + bus.Data[i + 1]) * .5f;
                bus.Data[i] *= (float)bus.Gain; bus.Data[i + 1] *= (float)bus.Gain;
                if (g.Config.ListenZoom && bus.Config.Id == "zoom") { Monitor[i] += bus.Data[i]; Monitor[i + 1] += bus.Data[i + 1]; }
            }
            bus.Meter.Measure(bus.Data);
        }
        float monitorGain = (float)AudioMath.Linear(g.Config.MonitorDb);
        for (int i = 0; i < Samples; i++) Monitor[i] *= monitorGain;
        ProcessedFrames += Frames;
    }
}

public sealed record SoundCheckResult(double RmsDb, double PeakDb, double NoiseDb, bool Clipped, bool NoSignal, List<AudioFilter> Proposed, string Advice);
public static class SoundCheck
{
    public static SoundCheckResult Analyze(double sumSquares, long frames, double peak, double noiseRms, long clippedFrames)
    {
        double rms = Math.Sqrt(sumSquares / Math.Max(1, frames));
        double gain = Math.Clamp(-18 - AudioMath.Db(rms), -18, 18);
        bool clipped = clippedFrames > 0, silent = peak < .0001;
        var filters = new List<AudioFilter>
        {
            new() { Kind = AudioFilterKind.Gain, Name = "Gain proposé", GainDb = clipped || silent ? 0 : gain },
            new() { Kind = AudioFilterKind.Gate, Name = "Gate proposé", ThresholdDb = Math.Clamp(AudioMath.Db(noiseRms) + 6, -80, -30), AttackMs = 5, HoldMs = 100, ReleaseMs = 180 },
            new() { Kind = AudioFilterKind.Compressor, Name = "Compression proposée", ThresholdDb = -15, Ratio = 3, AttackMs = 15, ReleaseMs = 180 },
            new() { Kind = AudioFilterKind.Limiter, Name = "Limiteur proposé", ThresholdDb = -1, Ratio = 100, AttackMs = .1, ReleaseMs = 80 }
        };
        return new(AudioMath.Db(rms), AudioMath.Db(peak), AudioMath.Db(noiseRms), clipped, silent, filters,
            clipped ? "Signal écrêté : baissez le gain physique sur l’interface, puis recommencez. Le gain logiciel ne répare pas l’écrêtage." : silent ? "Aucun signal : vérifiez le branchement et le canal physique." : "Réglages logiciels proposés pour cette entrée directe. L’acoustique de la salle n’est pas mesurée.");
    }
}
