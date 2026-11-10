using System.Text;

namespace MyVerse.Audio;

public static class RecordingFiles
{
    public const long ReserveBytes = 512L * 1024 * 1024;
    public static string SafeName(string name)
    {
        var invalid = "<>:\"/\\|?*";
        var text = new string(name.Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return text.Length == 0 ? "Enregistrement" : text[..Math.Min(80, text.Length)];
    }
    public static string ReservePath(string directory, string profile, string extension)
    {
        System.IO.Directory.CreateDirectory(directory);
        string stem = SafeName(profile) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        for (int i = 0; i < 1000; i++)
        {
            var path = Path.Combine(directory, stem + (i == 0 ? "" : "_" + i) + "." + extension);
            try { using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException("Impossible de réserver un nom de fichier unique.");
    }
    public static long FreeSpace(string directory) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
    public static void Validate(RecordingProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Directory)) throw new InvalidDataException("Choisissez un dossier de destination.");
        if (p.Track < 1 || p.Track > AudioConfiguration.Pistes) throw new InvalidDataException("Choisissez une piste de 1 à 6.");
        if (p.AudioOnly && p.Format is not ("wav" or "flac") || !p.AudioOnly && p.Format != "mkv") throw new InvalidDataException("Audio : WAV ou FLAC. Vidéo : MKV.");
        if (!p.AudioOnly && (p.Width < 320 || p.Width > 1920 || p.Height < 180 || p.Height > 1080 || p.Width % 2 != 0 || p.Height % 2 != 0 || p.Fps is not (24 or 25 or 30 or 50 or 60))) throw new InvalidDataException("Vidéo : dimensions paires, de 320 × 180 à 1920 × 1080 ; 24, 25, 30, 50 ou 60 images/s.");
        if (p.VideoKbps < 500 || p.VideoKbps > 50000 || p.AudioKbps < 64 || p.AudioKbps > 512) throw new InvalidDataException("Débit audio ou vidéo hors limites.");
        if (!p.AudioOnly && (p.Visuals.Count == 0 || !p.Visuals.Any(v => v.Visible))) throw new InvalidDataException("Ajoutez un contenu visuel visible.");
        foreach (var v in p.Visuals)
            if (v.X < 0 || v.Y < 0 || v.Width <= 0 || v.Height <= 0 || v.X + v.Width > 1.001 || v.Y + v.Height > 1.001 || !double.IsFinite(v.X + v.Y + v.Width + v.Height)) throw new InvalidDataException("Cadres visuels : utilisez des proportions dans l’image, entre 0 et 1.");
    }
}

/// <summary>PCM 24-bit RF64, ds64 checkpoint every second, recoverable after interruption.
/// Writer runs exclusively on the recording worker. Data chunks always start at byte 80.</summary>
public sealed class PcmRecordingWriter : IDisposable
{
    readonly FileStream file;
    readonly byte[] bytes = new byte[AudioBlock.Samples * 3];
    long frames, checkpoint;
    public PcmRecordingWriter(string path)
    {
        file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var w = new BinaryWriter(file, Encoding.ASCII, true);
        w.Write(Encoding.ASCII.GetBytes("RF64")); w.Write(uint.MaxValue); w.Write(Encoding.ASCII.GetBytes("WAVEds64")); w.Write(28u);
        w.Write(72L); w.Write(0L); w.Write(0L); w.Write(0u);
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16u); w.Write((ushort)1); w.Write((ushort)2); w.Write(48000u); w.Write(288000u); w.Write((ushort)6); w.Write((ushort)24);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(uint.MaxValue);
    }
    public void Write(ReadOnlySpan<float> audio)
    {
        if (audio.Length > AudioBlock.Samples || audio.Length % 2 != 0) throw new ArgumentException("Bloc audio invalide.");
        for (int i = 0; i < audio.Length; i++)
        {
            int sample = (int)Math.Round(Math.Clamp(audio[i], -1, 1) * 8388607);
            bytes[i * 3] = (byte)sample; bytes[i * 3 + 1] = (byte)(sample >> 8); bytes[i * 3 + 2] = (byte)(sample >> 16);
        }
        file.Write(bytes, 0, audio.Length * 3); frames += audio.Length / 2;
        if (frames - checkpoint >= 48000) { Checkpoint(); checkpoint = frames; }
    }
    void Checkpoint()
    {
        long end = file.Position; file.Position = 20;
        using (var w = new BinaryWriter(file, Encoding.ASCII, true)) { w.Write(end - 8); w.Write(end - 80); w.Write(frames); }
        file.Position = end; file.Flush(true);
    }
    public void Dispose() { try { Checkpoint(); } finally { file.Dispose(); } }
    public static void RecoverCopy(string original, string destination)
    {
        using (var f = File.OpenRead(original))
        {
            var header = new byte[80]; if (f.Read(header) != 80 || Encoding.ASCII.GetString(header, 0, 4) != "RF64" || Encoding.ASCII.GetString(header, 72, 4) != "data") throw new InvalidDataException("Ce fichier n’est pas un WAV RF64 MyChurch.");
        }
        using var source = File.OpenRead(original);
        using var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
        source.CopyTo(copy); long length = 80 + ((copy.Length - 80) / 6) * 6; copy.SetLength(length); copy.Position = 20;
        using var writer = new BinaryWriter(copy, Encoding.ASCII, true); writer.Write(length - 8); writer.Write(length - 80); writer.Write((length - 80) / 6);
    }
}
