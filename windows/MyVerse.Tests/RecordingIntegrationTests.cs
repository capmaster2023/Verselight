using System.Diagnostics;
using System.Text.Json;
using MyVerse.Audio;

namespace MyVerse.Tests;

/// <summary>Real native WAV and FFmpeg queue/transport tests; no simulated encoder.</summary>
public static class RecordingIntegrationTests
{
    sealed class Frames : IRecordingFrameProvider
    {
        readonly object gate = new();
        byte color = 25;
        public void Set(byte value) { lock (gate) color = value; }
        public void CopyFrame(byte[] destination)
        {
            lock (gate)
                for (int i = 0; i < destination.Length; i += 4) { destination[i] = color; destination[i + 1] = color; destination[i + 2] = color; destination[i + 3] = 255; }
        }
    }
    static async Task<string> ProcessAsync(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); } catch { p.Kill(true); throw; }
        if (p.ExitCode != 0) throw new IOException(await error); return await output;
    }
    static void FillAudio(float[] block, int n)
    {
        for (int i = 0; i < block.Length; i += 2)
        {
            double amplitude = n >= 50 && n < 60 ? .8 : .02;
            block[i] = block[i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * (n * 480 + i / 2) / 48000));
        }
    }
    public static async Task RunAsync(Action<bool, string, string?> ok)
    {
        string? executable = Environment.GetEnvironmentVariable("MYCHURCH_FFMPEG");
        string directory = Environment.GetEnvironmentVariable("MYCHURCH_AUDIO_FIXTURES") ?? Path.Combine(Path.GetTempPath(), "mychurch-recording-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var frame = new Frames(); var block = new float[AudioBlock.Samples];
        var formats = executable == null ? new[] { "wav" } : new[] { "wav", "flac", "mkv" };
        foreach (string format in formats)
        {
            var profile = new RecordingProfile { Name = "integration-" + format, Format = format, AudioOnly = format != "mkv", Directory = directory, Width = 320, Height = 180, Fps = 30, Encoder = "mjpeg" };
            string path = RecordingFiles.ReservePath(directory, profile.Name, format);
            if (format == "wav" || OperatingSystem.IsWindows())
            {
                using var session = new RecordingSession(profile, path, executable ?? "", "mjpeg", format == "mkv" ? frame : null);
                await session.PrepareAsync();
                for (int n = 0; n < 150; n++)
                {
                    frame.Set(n >= 50 && n < 60 ? (byte)240 : (byte)25);
                    FillAudio(block, n);
                    if (!session.Accept(block)) throw new IOException(session.Error);
                    if (format != "wav") await Task.Delay(10);
                }
                await session.StopAsync();
                ok(session.Error == null && new FileInfo(path).Length > 1000, "Session réelle : " + format.ToUpperInvariant() + " finalisé après 150 blocs", null);
                ok(session.AcceptedFrames == 72000, "Session : 72 000 trames acceptées (" + format + ")", null);
                ok(!session.Accept(block), "Session arrêtée refuse les nouveaux blocs (" + format + ")", null);
            }
            else
            {
                // This Linux environment forbids Unix sockets. Validate the exact encoder arguments
                // using real input files; Windows named-pipe transport is explicitly not claimed tested.
                string rawAudio = path + ".f32", rawVideo = path + ".bgra";
                using (var audioFile = File.Create(rawAudio))
                using (var videoFile = format == "mkv" ? File.Create(rawVideo) : null)
                {
                    var bytes = new byte[block.Length * 4]; var pixels = new byte[profile.Width * profile.Height * 4];
                    for (int n = 0; n < 150; n++)
                    {
                        FillAudio(block, n); Buffer.BlockCopy(block, 0, bytes, 0, bytes.Length); audioFile.Write(bytes);
                        if (videoFile != null && n % 10 == 0)
                        {
                            for (int k = 0; k < 3; k++) { int tick = n + k * 10 / 3; frame.Set(tick >= 50 && tick < 60 ? (byte)240 : (byte)25); frame.CopyFrame(pixels); videoFile.Write(pixels); }
                        }
                    }
                }
                await ProcessAsync(executable!, RecordingSession.FfmpegArguments(profile, rawAudio, format == "mkv" ? rawVideo : null, "mjpeg", path));
                ok(new FileInfo(path).Length > 1000, "FFmpeg réel sur fichiers : " + format + " (transport Windows non exécuté)", null);
                File.Delete(rawAudio); if (File.Exists(rawVideo)) File.Delete(rawVideo);
            }
            if (executable != null)
            {
                string ffprobe = Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                string json = await ProcessAsync(ffprobe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path });
                File.WriteAllText(path + ".ffprobe.json", json);
                using var doc = JsonDocument.Parse(json); var streams = doc.RootElement.GetProperty("streams");
                var audio = streams.EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "audio");
                ok(audio.GetProperty("sample_rate").GetString() == "48000" && audio.GetProperty("channels").GetInt32() == 2, "FFprobe : stéréo 48 kHz (" + format + ")", null);
                double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                ok(Math.Abs(duration - 1.5) < .12, "FFprobe : durée du fichier (" + format + ")", duration.ToString());
                if (format == "mkv") ok(streams.EnumerateArray().Any(s => s.TryGetProperty("width", out var width) && width.GetInt32() == 320 && s.GetProperty("height").GetInt32() == 180), "FFprobe : vidéo 320 × 180 réelle", null);
                await ProcessAsync(executable, new[] { "-v", "error", "-i", path, "-f", "null", "-" });
                ok(true, "Décodage complet FFmpeg sans erreur (" + format + ")", null);
            }
        }
        if (executable == null) Console.WriteLine("  FFmpeg non configuré : tests FLAC/MKV facultatifs ignorés (MYCHURCH_FFMPEG).");
        if (Environment.GetEnvironmentVariable("MYCHURCH_AUDIO_FIXTURES") == null) Directory.Delete(directory, true);
    }
}
