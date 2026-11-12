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
    /// <summary>Pousse en temps réel, par blocs de 10 ms, un sinus à 440 Hz dans chaque entrée (amplitude donnée), pendant la durée voulue.</summary>
    static void PousserSinus(ObsMixer m, (string Id, float Amplitude, int Rate)[] entrees, int ms)
    {
        var horloge = System.Diagnostics.Stopwatch.StartNew();
        long blocs = 0;
        var phase = new double[entrees.Length];
        while (horloge.ElapsedMilliseconds < ms)
        {
            while (blocs * 10 <= horloge.ElapsedMilliseconds + 20)
            {
                for (int k = 0; k < entrees.Length; k++)
                {
                    var (id, amplitude, rate) = entrees[k];
                    int frames = rate / 100;
                    var bloc = new float[frames];
                    for (int f = 0; f < frames; f++) { bloc[f] = (float)(amplitude * Math.Sin(phase[k])); phase[k] += 2 * Math.PI * 440 / rate; }
                    m.Pousser(id, bloc, frames, rate);
                }
                blocs++;
            }
            Thread.Sleep(2);
        }
    }

    /// <summary>Crête d'une piste par côté, mesurée sur les blocs de 10 ms livrés à l'enregistrement pendant la durée donnée.</summary>
    static (int Blocs, float G, float D) Mesurer(ObsMixer m, int piste, int ms)
    {
        int blocs = 0; float g = 0, d = 0;
        Action<float[]> abonne = b =>
        {
            if (b.Length != AudioBlock.Samples) return;
            Interlocked.Increment(ref blocs);
            for (int i = 0; i + 1 < b.Length; i += 2) { g = Math.Max(g, Math.Abs(b[i])); d = Math.Max(d, Math.Abs(b[i + 1])); }
        };
        m.AddTrackConsumer(piste, abonne);
        Thread.Sleep(ms);
        m.RemoveTrackConsumer(piste, abonne);
        return (blocs, g, d);
    }

    /// <summary>MyChurch 4.0 : ce que règlent les propriétés audio avancées et les raccourcis d'OBS (balance, synchro, appuyer pour parler, type de crête).</summary>
    static void ReglagesObs32(ObsMixer m, AudioConfiguration c, AudioSource s, Action<bool, string, string?> ok)
    {
        s.Muted = false; s.VolumeDb = 0; s.Filters.Clear(); s.Monitoring = AudioMonitoring.Off; s.Tracks = 1;
        c.PeakMeter = "reelle"; m.Apply(c);
        float v = Attendre(m, () => m.Level(s.Id).L, x => x > -8 && x < -3);
        ok(v > -8 && v < -3, "crête réelle (type de crête d'OBS) : le vumètre lit toujours le sinus à −6 dBFS", v.ToString("0.0"));
        c.PeakMeter = "echantillon"; m.Apply(c);
        if (!m.TracksActive) return;
        Attendre(m, () => m.TrackLevel(0), x => x > -8);
        s.Balance = 0; m.Apply(c);
        Thread.Sleep(400);
        var (n, g, d) = Mesurer(m, 0, 600);
        ok(n > 20 && g > 0.3 && d < 0.02, "balance tout à gauche : le côté droit se tait dans la piste", $"{n} blocs, gauche {g:0.00}, droite {d:0.000}");
        s.Balance = 1; m.Apply(c);
        Thread.Sleep(400);
        (n, g, d) = Mesurer(m, 0, 600);
        ok(n > 20 && d > 0.3 && g < 0.02, "balance tout à droite : le côté gauche se tait", $"gauche {g:0.000}, droite {d:0.00}");
        s.Balance = 0.5; m.Apply(c);
        m.Raccourci(s.Id, true);
        v = Attendre(m, () => m.TrackLevel(0), x => x < -80 || float.IsNegativeInfinity(x));
        ok((v < -80 || float.IsNegativeInfinity(v)) && m.CoupeeParRaccourci(s.Id) && !s.Muted, "appuyer pour parler (touche relâchée) : la piste se tait sans toucher à la sourdine", v.ToString("0.0"));
        m.Apply(c);
        v = Attendre(m, () => m.TrackLevel(0), x => x < -80 || float.IsNegativeInfinity(x), 600);
        ok(v < -80 || float.IsNegativeInfinity(v), "un changement de réglage garde la coupure du raccourci", v.ToString("0.0"));
        m.Raccourci(s.Id, false);
        v = Attendre(m, () => m.TrackLevel(0), x => x > -8);
        ok(v > -8 && !m.CoupeeParRaccourci(s.Id), "touche enfoncée : le son revient", v.ToString("0.0"));
        s.SyncOffsetMs = 300; m.Apply(c);
        Thread.Sleep(500);
        v = Attendre(m, () => m.TrackLevel(0), x => x > -8);
        ok(v > -8, "décalage de synchronisation de 300 ms accepté par OBS (le son passe toujours)", v.ToString("0.0"));
        s.SyncOffsetMs = 0; m.Apply(c);
    }

    /// <summary>Un moteur redémarré à une autre fréquence ou d'autres canaux : les pistes arrivent toujours en stéréo, par blocs de 10 ms.</summary>
    static void AutreFormat(ObsPaths chemins, string dossier, int rate, string canaux, bool faibleLatence, Action<bool, string, string?> ok)
    {
        var c = new AudioConfiguration { SampleRate = rate, Channels = canaux, LowLatency = faibleLatence };
        c.Normalize();
        var m = new ObsMixer();
        string quoi = $"{rate / 1000.0:0.#} kHz {canaux}" + (faibleLatence ? " faible latence" : "");
        try
        {
            m.Start(chemins, c.SampleRate, c.HautParleurs, c.LowLatency);
            ok(m.Running && m.SampleRate == rate && m.Speakers == c.HautParleurs && AudioBlock.Rate == rate && AudioBlock.Samples == rate / 50,
                $"moteur redémarré en {quoi}", $"{m.SampleRate} Hz, speakers {m.Speakers}, bloc {AudioBlock.Samples}");
            if (faibleLatence) ok(m.LowLatency, "mise en mémoire tampon à faible latence acceptée (obs_reset_audio2)", null);
            var s = new AudioSource { Name = "Sinus", Kind = AudioInputKind.Media, MediaPath = Sinus(dossier), Loop = true, Tracks = 1 };
            c.Sources.Add(s); c.Normalize(); m.Apply(c);
            float v = Attendre(m, () => m.Level(s.Id).L, x => x > -12 && x < -3);
            ok(v > -12 && v < -3, $"{quoi} : vumètre OBS sur le sinus", v.ToString("0.0"));
            if (m.TracksActive)
            {
                Attendre(m, () => m.TrackLevel(0), x => x > -20);
                var (n, g, d) = Mesurer(m, 0, 700);
                ok(n > 30 && g > 0.1 && d > 0.1, $"{quoi} : la piste arrive en stéréo à l'enregistrement, par blocs de 10 ms", $"{n} blocs de {AudioBlock.Samples}, crêtes {g:0.00} / {d:0.00}");
            }
        }
        catch (Exception e) { ok(false, "moteur en " + quoi, e.Message); }
        finally
        {
            try { m.Dispose(); } catch (Exception e) { ok(false, "arrêt du moteur en " + quoi, e.Message); }
        }
    }

    /// <summary>MyChurch 3.1 : chaque entrée d'une carte son est sa propre bande (type de source « entrée » de MyChurch, son poussé).</summary>
    static void EntreesSeparees(ObsMixer m, AudioConfiguration c, Action<bool, string, string?> ok)
    {
        ok(m.EntreesSeparees, "OBS accepte le type de source « entrée séparée » de MyChurch", null);
        if (!m.EntreesSeparees) return;
        var e1 = new AudioSource { Name = "Pasteur", Kind = AudioInputKind.Microphone, DeviceId = "default", Channel = 1, Tracks = 1 };
        var e2 = new AudioSource { Name = "Chorale", Kind = AudioInputKind.Microphone, DeviceId = "default", Channel = 2, Tracks = 1 };
        c.Sources.Clear(); c.Sources.Add(e1); c.Sources.Add(e2); c.Normalize(); m.Apply(c);
        ok(m.Has(e1.Id) && m.Has(e2.Id) && m.EstEntree(e1.Id) && m.EstEntree(e2.Id), "deux entrées du même périphérique : deux sources dans OBS", null);
        // l'entrée 1 à −6 dBFS (48 kHz), l'entrée 2 à −20 dBFS (44,1 kHz : OBS rééchantillonne) ; chacune sur sa bande, des deux côtés
        var pousse = Task.Run(() => PousserSinus(m, new[] { (e1.Id, 0.5f, 48000), (e2.Id, 0.1f, 44100) }, 4500));
        float v1 = Attendre(m, () => m.Level(e1.Id).L, x => x > -8 && x < -4);
        float v1d = m.Level(e1.Id).R;
        float v2 = Attendre(m, () => m.Level(e2.Id).R, x => x > -22 && x < -18);
        ok(v1 > -8 && v1 < -4 && v1d > -8 && v1d < -4, "entrée 1 : son poussé reçu sur sa bande, à gauche et à droite (−6 dBFS)", $"{v1:0.0} / {v1d:0.0}");
        ok(v2 > -22 && v2 < -18, "entrée 2 : son poussé à 44,1 kHz reçu sur sa propre bande (−20 dBFS)", v2.ToString("0.0"));
        e1.VolumeDb = -12; m.Apply(c);
        float v = Attendre(m, () => m.Level(e1.Id).L, x => x > -20 && x < -16);
        ok(v > -20 && v < -16, "volume d'OBS sur une entrée séparée : −12 dB → −18 dBFS", v.ToString("0.0"));
        e1.Channel = 0; m.Apply(c);
        ok(m.Has(e1.Id) && !m.EstEntree(e1.Id) && !m.Pousser(e1.Id, new float[480], 480, 48000), "toutes les entrées ensemble : la bande redevient une capture du périphérique", null);
        e1.Channel = 3; m.Apply(c);
        ok(m.EstEntree(e1.Id), "puis entrée 3 : de nouveau une entrée séparée", null);
        pousse.Wait();
        c.Sources.Clear(); m.Apply(c);
        ok(!m.Has(e1.Id) && !m.Has(e2.Id) && !m.Pousser(e2.Id, new float[480], 480, 48000), "entrées retirées : plus rien n'est poussé vers OBS", null);
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

            ReglagesObs32(m, c, s, ok);

            c.Sources.Clear(); m.Apply(c);
            ok(!m.Has(s.Id), "source retirée d'OBS", null);

            EntreesSeparees(m, c, ok);
        }
        catch (Exception e) { ok(false, "mélangeur OBS", e.ToString()); }
        finally
        {
            try { m.Dispose(); ok(!m.Running, "libobs s'arrête proprement", null); } catch (Exception e) { ok(false, "arrêt de libobs", e.Message); }
            // 4.0 : les autres formats du moteur (Réglages › Audio › Général d'OBS), chacun sur un moteur redémarré
            if (!m.Running)
            {
                AutreFormat(chemins, dossier, 44100, "5.1", true, ok);
                AutreFormat(chemins, dossier, 48000, "mono", false, ok);
                AudioBlock.Configurer(48000);
            }
            try { Directory.Delete(dossier, true); } catch (Exception) { }
            if (Environment.GetEnvironmentVariable("TOUT") == "1" || journal.Any(l => l.StartsWith("100")))
                lock (journal) foreach (var l in journal.TakeLast(60)) Console.WriteLine("    obs│ " + l.TrimEnd());
        }
    }
}
