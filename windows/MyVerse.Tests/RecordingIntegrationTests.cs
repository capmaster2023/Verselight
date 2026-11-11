using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// <summary>Journal (stderr) de FFmpeg, où silencedetect et metadata écrivent leurs mesures.</summary>
    static async Task<string> JournalAsync(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); } catch { p.Kill(true); throw; }
        await output; return await error;
    }
    /// <summary>Le moment (secondes) où le son devient fort et celui où l'image devient claire : le « clap » des blocs 50 à 59.</summary>
    static async Task<(double? Son, double? Image)> ClapAsync(string ffmpeg, string path)
    {
        string a = await JournalAsync(ffmpeg, new[] { "-hide_banner", "-nostats", "-i", path, "-map", "0:a:0", "-af", "silencedetect=n=-20dB:d=0.04", "-f", "null", "-" });
        var fin = Regex.Match(a, @"silence_end:\s*([0-9.]+)");
        string v = await JournalAsync(ffmpeg, new[] { "-hide_banner", "-nostats", "-i", path, "-map", "0:v:0", "-vf", "signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-" });
        double? image = null; double t = 0;
        foreach (var ligne in v.Split('\n'))
        {
            var mt = Regex.Match(ligne, @"pts_time:([0-9.]+)");
            if (mt.Success) t = double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
            var my = Regex.Match(ligne, @"YAVG=([0-9.]+)");
            if (my.Success && double.Parse(my.Groups[1].Value, CultureInfo.InvariantCulture) > 128) { image = t; break; }
        }
        return (fin.Success ? double.Parse(fin.Groups[1].Value, CultureInfo.InvariantCulture) : null, image);
    }
    /// <summary>Une vraie session vidéo (tubes nommés Windows) avec un décalage de synchro, puis la mesure du clap.</summary>
    static async Task<(double? Son, double? Image)> SessionClapAsync(string ffmpeg, string directory, Frames frame, int delayMs)
    {
        var profile = new RecordingProfile { Name = "synchro" + delayMs, Format = "mkv", AudioOnly = false, Directory = directory, Width = 320, Height = 180, Fps = 30, Encoder = "mjpeg", AudioDelayMs = delayMs };
        string path = RecordingFiles.ReservePath(directory, profile.Name, "mkv");
        var block = new float[AudioBlock.Samples];
        using (var session = new RecordingSession(profile, path, ffmpeg, "mjpeg", frame))
        {
            await session.PrepareAsync();
            for (int n = 0; n < 150; n++)
            {
                frame.Set(n >= 50 && n < 60 ? (byte)240 : (byte)25);
                FillAudio(block, n);
                if (!session.Accept(block)) throw new IOException(session.Error);
                await Task.Delay(10);
            }
            await session.StopAsync();
        }
        return await ClapAsync(ffmpeg, path);
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
                if (format == "mkv")
                {
                    // synchro : le son fort et l'image claire arrivent ensemble (blocs 50 à 59)
                    var (son, image) = await ClapAsync(executable, path);
                    ok(son != null && image != null && Math.Abs(son.Value - image.Value) < 0.05, "Synchro image/son mesurée dans le MKV", $"son {son:0.000} s · image {image:0.000} s");
                    if (OperatingSystem.IsWindows())
                    {
                        foreach (int delai in new[] { 200, -200 })
                        {
                            var (s2, i2) = await SessionClapAsync(executable, directory, frame, delai);
                            ok(s2 != null && i2 != null && Math.Abs(s2.Value - i2.Value - delai / 1000.0) < 0.05,
                                delai > 0 ? "Décalage de synchro : son retardé de 200 ms" : "Décalage de synchro : image retardée de 200 ms", $"son {s2:0.000} s · image {i2:0.000} s");
                        }
                    }
                }
                await ProcessAsync(executable, new[] { "-v", "error", "-i", path, "-f", "null", "-" });
                ok(true, "Décodage complet FFmpeg sans erreur (" + format + ")", null);
            }
        }
        if (executable == null) Console.WriteLine("  FFmpeg non configuré : tests FLAC/MKV facultatifs ignorés (MYCHURCH_FFMPEG).");
        if (Environment.GetEnvironmentVariable("MYCHURCH_AUDIO_FIXTURES") == null) Directory.Delete(directory, true);
    }
}
