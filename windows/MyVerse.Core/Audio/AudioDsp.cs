namespace MyVerse.Audio;

/// <summary>Single-producer/single-consumer bounded interleaved ring, short lock around memory only.
/// Fractional playback ratio tracks endpoint drift; no per-packet allocations, no disk or waits.</summary>
public sealed class AudioRing
{
    readonly object gate = new();
    readonly float[] samples;
    readonly int channels, target;
    int read, count;
    double phase, correction = 1;
    bool primed;
    public long Underruns { get; private set; }
    public long Overruns { get; private set; }
    public int Channels => channels;
    public int BufferedFrames { get { lock (gate) return count; } }
    public AudioRing(int channels, int capacityFrames = 48000, int targetFrames = 2880)
    {
        this.channels = channels; samples = new float[capacityFrames * channels]; target = targetFrames;
    }
    public void Write(ReadOnlySpan<float> source)
    {
        lock (gate)
        {
            int frames = source.Length / channels, capacity = samples.Length / channels;
            if (frames > capacity) { source = source[^samples.Length..]; frames = capacity; }
            if (count + frames > capacity) { int lost = count + frames - capacity; read = (read + lost) % capacity; count -= lost; Overruns++; }
            for (int i = 0; i < frames; i++) source.Slice(i * channels, channels).CopyTo(samples.AsSpan(((read + count + i) % capacity) * channels, channels));
            count += frames;
        }
    }
    public void Read(Span<float> destination, double nominalRatio = 1, bool drift = true)
    {
        lock (gate)
        {
            destination.Clear();
            if (!primed) { if (count < target) return; primed = true; }
            double desired = drift ? 1 + Math.Clamp((count - target) * .000002, -.005, .005) : 1;
            correction += (desired - correction) * .02;
            double ratio = nominalRatio * correction;
            int capacity = samples.Length / channels;
            for (int frame = 0; frame < destination.Length / channels; frame++)
            {
                int offset = (int)phase;
                if (offset + 1 >= count) { Underruns++; primed = false; int consumed = Math.Min(offset, count); read = (read + consumed) % capacity; count -= consumed; phase = 0; return; }
                float frac = (float)(phase - offset);
                int a = ((read + offset) % capacity) * channels, b = ((read + offset + 1) % capacity) * channels;
                for (int c = 0; c < channels; c++) destination[frame * channels + c] = samples[a + c] + (samples[b + c] - samples[a + c]) * frac;
                phase += ratio;
            }
            int used = Math.Min((int)phase, count); read = (read + used) % capacity; count -= used; phase -= used;
        }
    }
}

public sealed class AudioMeter
{
    public double Left { get; private set; }
    public double Right { get; private set; }
    public double Rms { get; private set; }
    public double PeakHold { get; private set; }
    public bool Clipped { get; private set; }
    public long Frames { get; private set; }
    public double SumSquares { get; private set; }
    public long ClippedFrames { get; private set; }
    public void Reset() { PeakHold = 0; Clipped = false; Frames = 0; SumSquares = 0; ClippedFrames = 0; }
    public void Measure(ReadOnlySpan<float> stereo)
    {
        double l = 0, r = 0, sum = 0;
        for (int i = 0; i + 1 < stereo.Length; i += 2)
        {
            double a = Math.Abs(stereo[i]), b = Math.Abs(stereo[i + 1]); l = Math.Max(l, a); r = Math.Max(r, b);
            sum += (a * a + b * b) * .5; if (Math.Max(a, b) >= .9999) { Clipped = true; ClippedFrames++; }
        }
        Left = Math.Max(l, Left * .88); Right = Math.Max(r, Right * .88); Rms = Math.Sqrt(sum / Math.Max(1, stereo.Length / 2));
        PeakHold = Math.Max(PeakHold, Math.Max(l, r)); Frames += stereo.Length / 2; SumSquares += sum;
    }
}

sealed class Biquad
{
    double b0, b1, b2, a1, a2;
    readonly double[] z1 = new double[2], z2 = new double[2];
    public Biquad(AudioFilter f)
    {
        double w = 2 * Math.PI * f.Frequency / 48000, c = Math.Cos(w), sn = Math.Sin(w), alpha = sn / (2 * f.Q), a0;
        if (f.Kind == AudioFilterKind.Equalizer)
        {
            double a = Math.Pow(10, f.GainDb / 40); a0 = 1 + alpha / a;
            b0 = (1 + alpha * a) / a0; b1 = -2 * c / a0; b2 = (1 - alpha * a) / a0; a1 = b1; a2 = (1 - alpha / a) / a0;
        }
        else
        {
            a0 = 1 + alpha; double sign = f.Kind == AudioFilterKind.HighPass ? 1 : -1;
            b0 = (1 + sign * c) / 2 / a0; b1 = -sign * (1 + sign * c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - alpha) / a0;
        }
    }
    public void CopyState(Biquad o) { z1[0] = o.z1[0]; z1[1] = o.z1[1]; z2[0] = o.z2[0]; z2[1] = o.z2[1]; }
    public float Run(float x, int channel)
    {
        double y = b0 * x + z1[channel]; z1[channel] = b1 * x - a1 * y + z2[channel]; z2[channel] = b2 * x - a2 * y;
        return (float)y;
    }
}

/// <summary>Original spectral noise suppressor. Windowed FFT bins estimate input power and
/// attenuate bins below a calibrated floor. Not an AI voice-isolation component.</summary>
sealed class NoiseSuppressor
{
    const int N = 256, Hop = 128;
    readonly double[][] real = { new double[N], new double[N] }, imag = { new double[N], new double[N] };
    readonly double[,] input = new double[2, N], output = new double[2, N * 2];
    readonly double[] window = new double[N];
    readonly double floor;
    int pos;
    public NoiseSuppressor(double floorDb) { floor = Math.Pow(10, floorDb / 10) * N / 2; for (int i = 0; i < N; i++) window[i] = Math.Sqrt(.5 - .5 * Math.Cos(2 * Math.PI * i / N)); }
    public void Run(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i += 2)
        {
            for (int c = 0; c < 2; c++) { input[c, pos % N] = buffer[i + c]; buffer[i + c] = (float)output[c, pos % (N * 2)]; output[c, pos % (N * 2)] = 0; }
            pos++;
            if (pos % Hop != 0) continue;
            for (int c = 0; c < 2; c++)
            {
                var re = real[c]; var im = imag[c];
                for (int k = 0; k < N; k++) { re[k] = input[c, (pos + k) % N] * window[k]; im[k] = 0; }
                Fft(re, im, false);
                for (int k = 0; k < N; k++) { double power = re[k] * re[k] + im[k] * im[k]; double gain = Math.Sqrt(Math.Max(.01, 1 - floor / Math.Max(1e-20, power))); re[k] *= gain; im[k] *= gain; }
                Fft(re, im, true);
                for (int k = 0; k < N; k++) output[c, (pos + k) % (N * 2)] += re[k] * window[k];
            }
        }
        // Keep counters bounded without changing circular buffer alignment.
        if (pos > 1_000_000) pos %= N * 2;
    }
    static void Fft(double[] re, double[] im, bool inverse)
    {
        for (int i = 1, j = 0; i < N; i++)
        {
            int bit = N >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int length = 2; length <= N; length <<= 1)
        {
            double angle = (inverse ? 2 : -2) * Math.PI / length, wr0 = Math.Cos(angle), wi0 = Math.Sin(angle);
            for (int i = 0; i < N; i += length)
            {
                double wr = 1, wi = 0;
                for (int j = 0; j < length / 2; j++)
                {
                    int a = i + j, b = a + length / 2; double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                    (wr, wi) = (wr * wr0 - wi * wi0, wr * wi0 + wi * wr0);
                }
            }
        }
        if (inverse) for (int i = 0; i < N; i++) { re[i] /= N; im[i] /= N; }
    }
}

public sealed class AudioFilterChain
{
    readonly Stage[] stages;
    sealed class Stage
    {
        public readonly AudioFilter F;
        readonly Biquad? eq;
        NoiseSuppressor? noise;
        double envelope, gain = 1;
        int hold, lookPos;
        // limiteur à anticipation : 2 ms de retard, le gain descend avant que la crête ne sorte
        readonly float[]? look;
        /// <summary>Même structure : la nouvelle chaîne reprend l'état (pas de remise à zéro en bougeant un curseur).</summary>
        public void CopyState(Stage o)
        {
            envelope = o.envelope; gain = o.gain; hold = o.hold;
            if (eq != null && o.eq != null) eq.CopyState(o.eq);
            if (noise != null && o.noise != null && F.ThresholdDb == o.F.ThresholdDb) noise = o.noise;
            if (look != null && o.look != null) { Array.Copy(o.look, look, look.Length); lookPos = o.lookPos; }
        }
        readonly double attack, release;
        public Stage(AudioFilter f)
        {
            F = f; attack = Math.Exp(-1 / (48 * f.AttackMs)); release = Math.Exp(-1 / (48 * f.ReleaseMs));
            if (f.Kind is AudioFilterKind.HighPass or AudioFilterKind.LowPass or AudioFilterKind.Equalizer) eq = new(f);
            if (f.Kind == AudioFilterKind.NoiseReduction) noise = new(f.ThresholdDb);
            if (f.Kind == AudioFilterKind.Limiter) look = new float[96 * 2];
        }
        public void Run(Span<float> data, double sidechain)
        {
            if (noise != null) { noise.Run(data); return; }
            double threshold = AudioMath.Linear(F.ThresholdDb), makeup = AudioMath.Linear(F.GainDb);
            for (int i = 0; i < data.Length; i += 2)
            {
                if (eq != null) { data[i] = eq.Run(data[i], 0); data[i + 1] = eq.Run(data[i + 1], 1); continue; }
                if (F.Kind == AudioFilterKind.Gain) { data[i] *= (float)makeup; data[i + 1] *= (float)makeup; continue; }
                double peak = Math.Max(Math.Abs(data[i]), Math.Abs(data[i + 1]));
                if (look != null)
                {
                    double cible = peak > threshold ? threshold / peak : 1;
                    gain = cible < gain ? cible + (gain - cible) * .92 : cible + (gain - cible) * release;
                    float dl = look[lookPos], dr = look[lookPos + 1];
                    look[lookPos] = data[i]; look[lookPos + 1] = data[i + 1];
                    lookPos = (lookPos + 2) % look.Length;
                    double plafond = threshold * makeup;
                    data[i] = (float)Math.Clamp(dl * gain * makeup, -plafond, plafond);
                    data[i + 1] = (float)Math.Clamp(dr * gain * makeup, -plafond, plafond);
                    continue;
                }
                double level = F.Kind == AudioFilterKind.Sidechain ? sidechain : peak;
                envelope = level + (envelope - level) * (level > envelope ? attack : release);
                double wanted = 1;
                if (F.Kind is AudioFilterKind.Gate or AudioFilterKind.Expander)
                {
                    if (envelope >= threshold) hold = (int)(F.HoldMs * 48);
                    if (hold > 0) hold--;
                    else if (envelope < threshold) wanted = F.Kind == AudioFilterKind.Gate ? 0 : Math.Pow(Math.Max(1e-9, envelope / threshold), F.Ratio - 1);
                }
                else if (envelope > threshold) wanted = Math.Pow(envelope / threshold, 1 / F.Ratio - 1);
                gain = wanted + (gain - wanted) * (wanted < gain ? attack : release);
                if (F.Kind == AudioFilterKind.Limiter) gain = Math.Min(gain, threshold / Math.Max(1e-9, peak));
                data[i] *= (float)(gain * makeup); data[i + 1] *= (float)(gain * makeup);
            }
        }
    }
    /// <summary>Vrai si l'état de la chaîne précédente a été repris (mêmes filtres, même ordre).</summary>
    public bool Inherited { get; }
    /// <summary>Nombre de fois où un filtre a produit une valeur invalide (NaN, infini) et a été remis à zéro.</summary>
    public long Faults { get; private set; }
    public AudioFilterChain(IEnumerable<AudioFilter> filters, AudioFilterChain? previous = null)
    {
        stages = filters.Where(f => f.Enabled).Select(f => new Stage(f)).ToArray();
        if (previous == null || previous.stages.Length != stages.Length) return;
        for (int i = 0; i < stages.Length; i++) if (stages[i].F.Kind != previous.stages[i].F.Kind) return;
        for (int i = 0; i < stages.Length; i++) stages[i].CopyState(previous.stages[i]);
        Inherited = true;
    }
    public void Process(Span<float> data, IReadOnlyDictionary<string, double>? levels = null)
    {
        foreach (var stage in stages) { double side = 0; levels?.TryGetValue(stage.F.SidechainSourceId, out side); stage.Run(data, side); }
        // garde-fou : une valeur invalide ne doit jamais atteindre les bus
        for (int i = 0; i < data.Length; i++)
        {
            if (float.IsFinite(data[i])) continue;
            data.Clear();
            for (int k = 0; k < stages.Length; k++) stages[k] = new Stage(stages[k].F);
            Faults++;
            break;
        }
    }
}
