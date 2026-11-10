using MyVerse.Audio;

namespace MyVerse.Tests;

/// <summary>Le mélangeur sur la vraie libobs : un fichier son joué par OBS, mesuré par ses vumètres et ses pistes.
/// Dossier d'OBS : variable MYCHURCH_OBS (Windows) ; sous Linux, la libobs du système si elle est installée.</summary>
public static class ObsTests
{
    static string Sinus(string dossier)
    {
        string chemin = Path.Combine(dossier, "sinus.wav");
        const int rate = 48000, secondes = 4;
        using var w = new BinaryWriter(File.Create(chemin));
        int octets = rate * secondes * 4;
        w.Write("RIFF"u8); w.Write(36 + octets); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(octets);
        for (int i = 0; i < rate * secondes; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5 * 32767); // −6 dBFS
            w.Write(v); w.Write(v);
        }
        return chemin;
    }
    static float Attendre(ObsMixer m, Func<float> lire, Func<float, bool> bon, int ms = 4000)
    {
        float v = float.NegativeInfinity;
        for (int t = 0; t < ms; t += 50) { Thread.Sleep(50); v = lire(); if (bon(v)) return v; }
        return v;
    }
    public static void Run(Action<bool, string, string?> ok)
    {
        string? racine = Environment.GetEnvironmentVariable("MYCHURCH_OBS");
        var chemins = racine != null ? ObsPaths.Windows(racine) : OperatingSystem.IsLinux() ? ObsPaths.Linux() : null;
        if (chemins == null || !chemins.Present) { Console.WriteLine("  (libobs absente : essais du mélangeur OBS non faits)"); return; }
        var journal = new List<string>();
        ObsMixer.Log += (niveau, texte) => { lock (journal) journal.Add(niveau + " " + texte); };
        string dossier = Path.Combine(Path.GetTempPath(), "mychurch-obs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dossier);
        var m = new ObsMixer();
        try
        {
            m.Start(chemins);
            Console.WriteLine($"  libobs {m.Version} · modules : {string.Join(", ", m.Modules)} · pistes : {(m.TracksActive ? "actives" : "indisponibles ici")}");
            ok(m.Running && m.Version.Length > 0, "libobs démarre", null);
            ok(m.Modules.Contains("obs-filters") && m.Modules.Contains("obs-ffmpeg"), "modules filtres et média chargés", string.Join(", ", m.Modules));
            if (OperatingSystem.IsWindows()) ok(m.Modules.Contains("win-wasapi"), "module des périphériques Windows (WASAPI) chargé", string.Join(", ", m.Modules));
            _ = m.Microphones(); _ = m.Outputs(); _ = m.MonitoringDevices();
            ok(true, "listes de périphériques lues sans erreur", null);

            var c = new AudioConfiguration();
            var s = new AudioSource { Name = "Sinus", Kind = AudioInputKind.Media, MediaPath = Sinus(dossier), Loop = true, Tracks = 1 };
            c.Sources.Add(s); c.Normalize(); m.Apply(c);
            ok(m.Has(s.Id), "source média créée dans OBS", null);
            float v = Attendre(m, () => m.Level(s.Id).L, x => x > -8 && x < -4);
            ok(v > -8 && v < -4, "vumètre OBS : sinus à −6 dBFS", v.ToString("0.0"));

            s.VolumeDb = -12; m.Apply(c);
            v = Attendre(m, () => m.Level(s.Id).L, x => x > -20 && x < -16);
            ok(v > -20 && v < -16, "volume −12 dB : vumètre à −18 dBFS", v.ToString("0.0"));

            s.VolumeDb = 0;
            var limiteur = new AudioFilter { Kind = AudioFilterKind.Limiter, ThresholdDb = -20, ReleaseMs = 60 };
            s.Filters.Add(limiteur); m.Apply(c);
            v = Attendre(m, () => m.Level(s.Id).L, x => x < -18 && x > -30);
            ok(v < -18 && v > -30, "limiteur OBS à −20 dB : crête ramenée sous le seuil", v.ToString("0.0"));
            limiteur.Enabled = false; m.Apply(c);
            v = Attendre(m, () => m.Level(s.Id).L, x => x > -8);
            ok(v > -8, "limiteur désactivé : niveau d'origine", v.ToString("0.0"));
            s.Filters.Add(new AudioFilter { Kind = AudioFilterKind.Compressor });
            s.Filters.Add(new AudioFilter { Kind = AudioFilterKind.NoiseGate });
            s.Filters.Add(new AudioFilter { Kind = AudioFilterKind.NoiseSuppression });
            m.Apply(c);
            v = Attendre(m, () => m.Level(s.Id).L, x => x > -40, 3000);
            ok(v > -40 && !journal.Any(l => l.Contains("filtre OBS refusé")), "compresseur, noise gate et réduction du bruit acceptés par OBS", v.ToString("0.0"));
            var ordre = m.FilterOrder(s.Id);
            ok(ordre.SequenceEqual(s.Filters.Select(f => f.Id)), "OBS applique les filtres dans l'ordre du mélangeur", string.Join(", ", ordre));
            (s.Filters[0], s.Filters[3]) = (s.Filters[3], s.Filters[0]);
            (s.Filters[1], s.Filters[2]) = (s.Filters[2], s.Filters[1]);
            m.Apply(c);
            ordre = m.FilterOrder(s.Id);
            ok(ordre.SequenceEqual(s.Filters.Select(f => f.Id)), "Monter / Descendre : OBS suit le nouvel ordre des filtres", string.Join(", ", ordre));
            v = Attendre(m, () => m.Level(s.Id).L, x => x > -40, 3000);
            ok(v > -40, "le son passe toujours après le changement d'ordre", v.ToString("0.0"));
            s.Filters.RemoveAt(1); m.Apply(c);
            ordre = m.FilterOrder(s.Id);
            ok(ordre.SequenceEqual(s.Filters.Select(f => f.Id)), "filtre supprimé : l'ordre des autres est gardé", string.Join(", ", ordre));
            s.Filters.Clear(); m.Apply(c);
            ok(m.FilterOrder(s.Id).Count == 0, "tous les filtres retirés d'OBS", null);

            if (m.TracksActive)
            {
                v = Attendre(m, () => m.TrackLevel(0), x => x > -8 && x < -4);
                ok(v > -8 && v < -4, "piste 1 : reçoit la source", v.ToString("0.0"));
                ok(float.IsNegativeInfinity(m.TrackLevel(1)) || m.TrackLevel(1) < -80, "piste 2 : silencieuse (source non envoyée)", m.TrackLevel(1).ToString("0.0"));
                int blocs = 0; double crete = 0;
                int blocs2 = 0;
                Action<float[]> abonne = b => { if (b.Length == AudioBlock.Samples) Interlocked.Increment(ref blocs); crete = Math.Max(crete, b.Max(Math.Abs)); };
                Action<float[]> abonne2 = b => Interlocked.Increment(ref blocs2);
                m.AddTrackConsumer(0, abonne); m.AddTrackConsumer(0, abonne2);
                Thread.Sleep(700);
                m.RemoveTrackConsumer(0, abonne); m.RemoveTrackConsumer(0, abonne2);
                ok(blocs2 > 40, "deux abonnés sur la même piste (enregistrement + MyVirtualSound)", blocs2.ToString());
                ok(blocs > 40 && crete > .4 && crete < .6, "piste 1 livrée par blocs de 10 ms pour l'enregistrement", $"{blocs} blocs, crête {crete:0.00}");
                s.Tracks = 2; m.Apply(c);
                v = Attendre(m, () => m.TrackLevel(1), x => x > -8);
                float p1 = Attendre(m, () => m.TrackLevel(0), x => x < -80 || float.IsNegativeInfinity(x));
                ok(v > -8 && (p1 < -80 || float.IsNegativeInfinity(p1)), "routage : la source passe de la piste 1 à la piste 2", $"p1 {p1:0.0}, p2 {v:0.0}");
                s.Monitoring = AudioMonitoring.Only; m.Apply(c);
                v = Attendre(m, () => m.TrackLevel(1), x => x < -80 || float.IsNegativeInfinity(x));
                ok(v < -80 || float.IsNegativeInfinity(v), "écoute seule : la source ne va plus dans les pistes", v.ToString("0.0"));
                s.Monitoring = AudioMonitoring.AndOutput; m.Apply(c);
                v = Attendre(m, () => m.TrackLevel(1), x => x > -8);
                ok(v > -8, "écoute et sortie : la source revient dans sa piste", v.ToString("0.0"));
                s.Monitoring = AudioMonitoring.Off; s.Tracks = 1; m.Apply(c);
            }

            s.Muted = true; m.Apply(c);
            if (m.TracksActive)
            {
                // Comme dans OBS, le vumètre d'une source en sourdine continue de montrer son signal (affiché grisé) : c'est la piste qui se tait.
                v = Attendre(m, () => m.TrackLevel(0), x => x < -80 || float.IsNegativeInfinity(x));
                ok(v < -80 || float.IsNegativeInfinity(v), "sourdine : la piste devient silencieuse", v.ToString("0.0"));
            }
            s.Muted = false; m.Apply(c);
            if (m.TracksActive)
            {
                v = Attendre(m, () => m.TrackLevel(0), x => x > -8);
                ok(v > -8, "sourdine retirée : le son revient dans la piste", v.ToString("0.0"));
            }

            c.Sources.Clear(); m.Apply(c);
            ok(!m.Has(s.Id), "source retirée d'OBS", null);
        }
        catch (Exception e) { ok(false, "mélangeur OBS", e.ToString()); }
        finally
        {
            try { m.Dispose(); ok(!m.Running, "libobs s'arrête proprement", null); } catch (Exception e) { ok(false, "arrêt de libobs", e.Message); }
            try { Directory.Delete(dossier, true); } catch (Exception) { }
            if (Environment.GetEnvironmentVariable("TOUT") == "1" || journal.Any(l => l.StartsWith("100")))
                lock (journal) foreach (var l in journal.TakeLast(60)) Console.WriteLine("    obs│ " + l.TrimEnd());
        }
    }
}
