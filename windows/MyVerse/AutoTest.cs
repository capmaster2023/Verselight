using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVerse.Ui;

/// <summary>
/// Test automatique (« MyChurch.exe --autotest dossier », lancé par la compilation sur GitHub) :
/// ouvre la régie, affiche des versets et des chants, ouvre la projection et chaque fenêtre,
/// enregistre des captures d'écran et un compte rendu (autotest.txt), puis ferme MyChurch (code 0 = tout va bien).
/// </summary>
public static class AutoTest
{
    static readonly List<string> echecs = new();
    static readonly List<string> log = new();
    static string dossier = "";

    public static void Echec(string m)
    {
        lock (echecs) echecs.Add(m);
        lock (log) log.Add("✗ " + m);
    }

    static void Ok(bool c, string quoi)
    {
        if (c) lock (log) log.Add("✓ " + quoi);
        else Echec(quoi);
    }

    static void Note(string m) { lock (log) log.Add("  " + m); }

    static Task Attendre(int ms) => Task.Delay(ms);

    /// <summary>Image de la fenêtre (fond compris), sans dépendre de l'écran du PC.</summary>
    static void Capture(Window? w, string nom)
    {
        if (w == null) { Echec("capture impossible (fenêtre absente) : " + nom); return; }
        try
        {
            w.UpdateLayout();
            // la racine visuelle de la fenêtre : tout l'intérieur, fond et marges compris
            if ((VisualTreeHelper.GetChildrenCount(w) > 0 ? VisualTreeHelper.GetChild(w, 0) : w.Content) is not FrameworkElement el) { Echec("capture sans contenu : " + nom); return; }
            int wd = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
            if (wd <= 0 || h <= 0) { Echec("capture vide : " + nom); return; }
            var rtb = new RenderTargetBitmap(wd, h, 96, 96, PixelFormats.Pbgra32);
            var fond = new DrawingVisual();
            using (var dc = fond.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(0, 0, wd, h));
            rtb.Render(fond);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var f = File.Create(Path.Combine(dossier, nom));
            enc.Save(f);
            Note($"capture {nom} ({wd}×{h})");
        }
        catch (Exception x) { Echec($"capture {nom} : {x.Message}"); }
    }

    static async Task Fenetre(Window owner, Func<Window> creer, string nom, Action<Window>? avant = null)
    {
        Window? d = null;
        try
        {
            d = creer();
            d.Owner = owner;
            d.ShowActivated = false;
            d.Show();
            avant?.Invoke(d);
            await Attendre(700);
            Capture(d, nom);
            Ok(true, "fenêtre " + d.Title);
        }
        catch (Exception x) { Echec($"fenêtre {nom} : {x}"); }
        finally { try { d?.Close(); } catch (Exception) { } }
    }

    /// <summary>
    /// Le mélangeur dans MyChurch (3.0.1) : trois sources (dont un sinus à −6 dBFS joué par OBS), une sortie MyVirtualSound,
    /// deux enregistrements qui tournent en même temps (une vidéo MKV avec la projection et deux sources Display, un WAV qui
    /// coupe les autres sources), puis les fichiers relus par le ffmpeg livré avec MyChurch. Captures du panneau et des fenêtres.
    /// </summary>
    /// <summary>
    /// MyChurch 4.0 : l'audio comme OBS 32. Périphériques globaux créés au premier démarrage, carte son branchée ajoutée toute seule
    /// (relevé simulé : la machine du CI n'a pas de micro), inactive quand elle est débranchée, épingler et masquer, disposition horizontale,
    /// appuyer pour parler, balance et synchro, fréquence au prochain démarrage, propriétés audio avancées et page Réglages › Audio.
    /// </summary>
    static async Task TestAudioObs32(MainWindow w)
    {
        var cfg = App.Audio.Config;
        var avant = MyVerse.Audio.AudioConfiguration.Copy(cfg);
        try
        {
            cfg.Sources.Clear(); cfg.Profiles.Clear(); cfg.VirtualSounds.Clear();
            cfg.GlobalsInitialized = false;
            cfg.ShowHidden = false; cfg.ShowInactive = false; cfg.HiddenRight = false; cfg.InactiveRight = false; cfg.VerticalLayout = true;
            Ok(cfg.InitialiserGlobaux() && cfg.Globale("bureau1") != null && cfg.Globale("micro1") != null, "4.0 : premier démarrage, « Audio du bureau » et « Micro/Aux » créés comme dans OBS");
            App.Audio.Apply();
            w.MontrerAudio();
            var pa = w.PanneauAudio!;
            pa.Rafraichir();
            await Attendre(500);
            Ok(pa.TestBandes.SequenceEqual(new[] { "Audio du bureau", "Micro/Aux" }) && pa.TestStatut("Micro/Aux").StartsWith("GLOBALE"), "mélangeur : les deux globales, étiquetées GLOBALE (" + string.Join(", ", pa.TestBandes) + ")");
            if (App.Audio.Running) Ok(App.Audio.InputReady(cfg.Globale("micro1")!.Id) && App.Audio.InputReady(cfg.Globale("bureau1")!.Id), "les globales sont des sources d'OBS");

            // une carte son branchée (relevé simulé)
            cfg.AutoAddInputs = true; cfg.KnownInputsReady = false; cfg.KnownInputs.Clear();
            var webcam = new MyVerse.Audio.AudioDevice("{autotest-webcam}", "Microphone (Webcam C920)");
            var carte = new MyVerse.Audio.AudioDevice("{autotest-umc}", "Line In (Behringer UMC404HD)");
            var hp = new MyVerse.Audio.AudioDevice("{autotest-hp}", "Haut-parleurs (Realtek)");
            App.Audio.Traiter(new() { webcam }, new() { hp }, true);
            Ok(cfg.KnownInputsReady && cfg.Sources.Count == 2, "premier relevé : ce qui est déjà branché est noté, rien n'est ajouté");
            App.Audio.Traiter(new() { webcam, carte }, new() { hp }, true);
            await Attendre(400);
            var umc = cfg.Sources.FirstOrDefault(x => x.DeviceId == "{autotest-umc}");
            Ok(umc is { Name: "Behringer UMC404HD" } && pa.TestBandes.Contains("Behringer UMC404HD"), "carte son branchée : ajoutée toute seule au mélangeur (" + string.Join(", ", pa.TestBandes) + ")");
            Ok(pa.TestStatut("Behringer UMC404HD") == "ACTIVE", "étiquette ACTIVE (" + pa.TestStatut("Behringer UMC404HD") + ")");
            if (App.Audio.Running && umc != null) Ok(App.Audio.InputReady(umc.Id), "la carte son branchée est une source d'OBS");
            Capture(w, "37-obs32-entree-branchee.png");
            App.Audio.Traiter(new() { webcam }, new() { hp }, true);
            await Attendre(400);
            Ok(umc != null && App.Audio.Inactive(umc) && !pa.TestBandes.Contains("Behringer UMC404HD"), "débranchée : inactive, elle quitte le mélangeur (option d'OBS 32.1)");
            cfg.ShowInactive = true; App.Audio.Apply(); pa.Rafraichir();
            Ok(pa.TestStatut("Behringer UMC404HD") == "INACTIVE", "« Afficher les sources inactives » : elle revient, étiquetée INACTIVE");
            App.Audio.Traiter(new() { webcam, carte }, new() { hp }, true);
            await Attendre(400);
            Ok(umc != null && !App.Audio.Inactive(umc) && cfg.Sources.Count(x => x.DeviceId == "{autotest-umc}") == 1, "rebranchée : de nouveau active, sans deuxième bande");

            // épingler et masquer
            var pasteur = new MyVerse.Audio.AudioSource { Name = "Pasteur", Kind = MyVerse.Audio.AudioInputKind.Microphone, DeviceId = "default", Hidden = true };
            var chorale = new MyVerse.Audio.AudioSource { Name = "Chorale", Kind = MyVerse.Audio.AudioInputKind.Microphone, DeviceId = "default", Pinned = true };
            cfg.Sources.Add(pasteur); cfg.Sources.Add(chorale);
            App.Audio.Apply(); pa.Rafraichir();
            Ok(pa.TestBandes.SequenceEqual(new[] { "Audio du bureau", "Micro/Aux", "Chorale", "Behringer UMC404HD" }), "épinglée juste après les globales, masquée cachée : " + string.Join(", ", pa.TestBandes));
            Ok(pa.TestStatut("Chorale") == "ÉPINGLÉE", "étiquette ÉPINGLÉE");
            cfg.ShowHidden = true; App.Audio.Apply(); pa.Rafraichir();
            Ok(pa.TestStatut("Pasteur") == "MASQUÉE" && pa.TestBandes.Count == 5, "« Afficher les sources masquées » : étiquette MASQUÉE");
            await Attendre(400);
            Capture(w, "38-obs32-melangeur.png");
            cfg.VerticalLayout = false; App.Audio.Apply(); pa.Rafraichir();
            await Attendre(500);
            Ok(pa.TestHorizontal, "disposition horizontale (bouton de la barre du mélangeur, comme OBS)");
            Capture(w, "38b-obs32-horizontal.png");
            cfg.VerticalLayout = true; App.Audio.Apply(); pa.Rafraichir();

            if (App.Audio.Running)
            {
                // appuyer pour parler : touche relâchée, la source est coupée sans toucher à sa sourdine
                pasteur.PushToTalk = true; pasteur.PushToTalkKey = "F24"; pasteur.PushToTalkDelayMs = 0;
                App.Audio.Apply();
                for (int i = 0; i < 30 && !App.Audio.CoupeeParRaccourci(pasteur.Id); i++) await Attendre(50);
                Ok(App.Audio.CoupeeParRaccourci(pasteur.Id) && !pasteur.Muted, "appuyer pour parler (F24 relâchée) : « Pasteur » coupée, sourdine intacte");
                pasteur.PushToTalk = false; App.Audio.Apply();
                for (int i = 0; i < 30 && App.Audio.CoupeeParRaccourci(pasteur.Id); i++) await Attendre(50);
                Ok(!App.Audio.CoupeeParRaccourci(pasteur.Id), "raccourci désactivé : le son revient");
                pasteur.Balance = 0.2; pasteur.SyncOffsetMs = 150; cfg.PeakMeter = "reelle"; cfg.MeterDecay = "lente";
                App.Audio.Apply();
                Ok(App.Audio.InputReady(pasteur.Id) && Math.Abs(Audio.Vumetre.Retombee - 8.57) < 0.01, "balance, décalage de synchro, crête réelle et retombée lente appliqués");
            }
            Ok(App.Audio.Format.StartsWith("48 kHz") && !App.Audio.RedemarrageRequis, "moteur : " + App.Audio.Format);
            cfg.SampleRate = 44100;
            Ok(App.Audio.RedemarrageRequis, "44,1 kHz choisi : redémarrage demandé, comme dans OBS");
            cfg.SampleRate = 48000;
            Note("atténuation de Windows désactivée : " + (Audio.AttenuationWindows.Desactiver(true) ? "oui" : "non (pas de sortie audio sur cette machine)"));
            Audio.AttenuationWindows.Desactiver(false);
            Ok(Audio.Touches.Afficher("Ctrl+Shift+D1") == "Ctrl + Maj + 1" && Audio.Touches.Afficher("Souris4") == "Souris 4" && !Audio.Touches.Enfoncee("F24"), "touches des raccourcis lues et affichées en français");

            await Fenetre(w, () => new Audio.ProprietesAudioWindow(), "39-proprietes-audio-avancees.png");
            w.TestPage("reglages");
            w.Reglages.Aller("audio", false);
            await Attendre(1200);
            Capture(w, "39b-reglages-audio-obs32.png");
        }
        catch (Exception x) { Echec("audio comme OBS 32 : " + x); }
        finally
        {
            cfg.Sources.Clear(); cfg.Sources.AddRange(avant.Sources);
            cfg.Profiles.Clear(); cfg.Profiles.AddRange(avant.Profiles);
            cfg.VirtualSounds.Clear(); cfg.VirtualSounds.AddRange(avant.VirtualSounds);
            cfg.GlobalsInitialized = avant.GlobalsInitialized; cfg.KnownInputs = avant.KnownInputs; cfg.KnownInputsReady = avant.KnownInputsReady;
            cfg.ShowHidden = avant.ShowHidden; cfg.ShowInactive = avant.ShowInactive; cfg.HiddenRight = avant.HiddenRight; cfg.InactiveRight = avant.InactiveRight;
            cfg.VerticalLayout = avant.VerticalLayout; cfg.AutoAddInputs = avant.AutoAddInputs;
            cfg.SampleRate = avant.SampleRate; cfg.Channels = avant.Channels; cfg.MeterDecay = avant.MeterDecay; cfg.PeakMeter = avant.PeakMeter;
            cfg.DisableDucking = avant.DisableDucking; cfg.LowLatency = avant.LowLatency;
            App.Audio.OublierReleve();
            App.Audio.Apply();
            w.MontrerAudio();
            w.PanneauAudio?.Rafraichir();
        }
    }

    static async Task TestAudio(MainWindow w, Annonce texte, Annonce image)
    {
        var cfg = App.Audio.Config;
        var avantSources = cfg.Sources.ToList();
        var avantProfils = cfg.Profiles.ToList();
        var avantSorties = cfg.VirtualSounds.ToList();
        string dossierTest = Path.Combine(Path.GetTempPath(), "mychurch-autotest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            for (int i = 0; i < 40 && !App.Audio.Running && !App.Audio.ObsAbsent && !App.Audio.Diagnostic.Contains("indisponible"); i++) await Attendre(250);
            Note("audio : " + App.Audio.Diagnostic);
            Ok(App.Audio.Running, "moteur audio OBS démarré dans MyChurch (" + App.Audio.Diagnostic + ")");
            Ok(App.Audio.EntreesSeparees, "3.1 : OBS accepte les entrées séparées d'une carte son");
            Ok(File.Exists(Audio.ChurchAudioService.FfmpegInclus), "ffmpeg livré avec MyChurch (" + Audio.ChurchAudioService.FfmpegInclus + ")");
            string sinus = Path.Combine(App.Donnees.Dossier, "test-sinus.wav");
            EcrireSinus(sinus);
            var micro = new MyVerse.Audio.AudioSource { Name = "Micro pasteur", Kind = MyVerse.Audio.AudioInputKind.Microphone, DeviceId = "default", VolumeDb = -4, Monitoring = MyVerse.Audio.AudioMonitoring.AndOutput };
            micro.Filters.Add(new MyVerse.Audio.AudioFilter { Kind = MyVerse.Audio.AudioFilterKind.NoiseSuppression });
            micro.Filters.Add(new MyVerse.Audio.AudioFilter { Kind = MyVerse.Audio.AudioFilterKind.NoiseGate, AttackMs = 25, ReleaseMs = 150 });
            micro.Filters.Add(new MyVerse.Audio.AudioFilter { Kind = MyVerse.Audio.AudioFilterKind.Compressor });
            micro.Filters.Add(new MyVerse.Audio.AudioFilter { Kind = MyVerse.Audio.AudioFilterKind.Limiter, ThresholdDb = -6 });
            var ordi = new MyVerse.Audio.AudioSource { Name = "Son de l'ordinateur", Kind = MyVerse.Audio.AudioInputKind.Sortie, DeviceId = "default", VolumeDb = -12, Muted = true };
            var media = new MyVerse.Audio.AudioSource { Name = "Sinus de test", Kind = MyVerse.Audio.AudioInputKind.Media, MediaPath = sinus, Loop = true };
            // 3.1 : deux entrées séparées de la même carte son, chacune sur sa bande
            var chorale = new MyVerse.Audio.AudioSource { Name = "Chorale", Kind = MyVerse.Audio.AudioInputKind.Microphone, DeviceId = "default", Channel = 2 };
            var piano = new MyVerse.Audio.AudioSource { Name = "Piano", Kind = MyVerse.Audio.AudioInputKind.Microphone, DeviceId = "default", Channel = 3, VolumeDb = -6 };
            cfg.Sources.Clear();
            cfg.Sources.Add(micro); cfg.Sources.Add(chorale); cfg.Sources.Add(piano); cfg.Sources.Add(ordi); cfg.Sources.Add(media);
            // créés comme par l'utilisateur : une sortie « Zoom » (le sinus), un enregistrement vidéo du culte, un WAV de la prédication
            var zoom = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", Enabled = true, DeviceId = "default", SourceIds = { media.Id } };
            var culte = new MyVerse.Audio.RecordingProfile { Name = "Culte (test)", SourceIds = { micro.Id, media.Id }, DisplayIds = { MyVerse.Audio.RecordingProfile.Projection, texte.Id, image.Id }, Directory = Path.Combine(dossierTest, "culte") };
            var predication = new MyVerse.Audio.RecordingProfile { Name = "Prédication (test)", SourceIds = { micro.Id }, MuteOthers = true, Directory = Path.Combine(dossierTest, "predication") };
            cfg.VirtualSounds.Clear(); cfg.VirtualSounds.Add(zoom);
            cfg.Profiles.Clear(); cfg.Profiles.Add(culte); cfg.Profiles.Add(predication);
            App.Audio.Apply();
            w.MontrerAudio();
            w.PanneauAudio?.Rafraichir();
            await Attendre(1800);
            if (App.Audio.Running)
            {
                var (g, d) = App.Audio.Level(media.Id);
                Ok(g > -12 && g < -2, $"vumètre OBS dans MyChurch : sinus à −6 dBFS ({g:0.0} / {d:0.0} dBFS)");
                Ok(App.Audio.InputReady(micro.Id) && App.Audio.InputReady(ordi.Id), "micro et son de l'ordinateur créés dans OBS");
                Ok(App.Audio.EstEntree(chorale.Id) && App.Audio.EstEntree(piano.Id) && !App.Audio.EstEntree(micro.Id), "entrées 2 et 3 : chacune sa bande (entrée séparée) ; le micro « toutes ensemble » reste une capture");
                Note("MyVirtualSound « Zoom » : " + App.Audio.Sorties.Status(zoom.Id));
                Capture(w, "32-audio.png");
                if (App.Audio.TracksActive)
                {
                    Ok(zoom.Track > 0 && culte.Track == 0 && predication.Track == 0, $"pistes automatiques : Zoom a la piste {zoom.Track}, les enregistrements arrêtés n'en ont pas");
                    float p = float.NegativeInfinity;
                    for (int i = 0; i < 25 && !(p > -12); i++) { await Attendre(200); p = App.Audio.TrackLevel(zoom.Track - 1); }
                    Ok(p > -12, $"piste de Zoom : reçoit le sinus ({p:0.0} dBFS)");

                    // deux enregistrements en même temps
                    await App.Audio.Recorder.ToggleAsync(culte);
                    Ok(App.Audio.Recorder.Active(culte.Id), "enregistrement vidéo « Culte » démarré : " + App.Audio.Recorder.Message(culte.Id));
                    await App.Audio.Recorder.ToggleAsync(predication);
                    Ok(App.Audio.Recorder.Active(predication.Id), "2e enregistrement en même temps (« Prédication ») : " + App.Audio.Recorder.Message(predication.Id));
                    Ok(App.Audio.SourdinesForcees.Contains(media.Id) && App.Audio.SourdinesForcees.Contains(ordi.Id) && !App.Audio.SourdinesForcees.Contains(micro.Id),
                        "« Prédication » coupe les autres sources partout (le sinus et l'ordinateur), pas le micro");
                    Ok(culte.Track > 0 && predication.Track > 0 && culte.Track != predication.Track && culte.Track != zoom.Track && predication.Track != zoom.Track,
                        $"une piste d'OBS par mélange : Culte {culte.Track}, Prédication {predication.Track}, Zoom {zoom.Track}");
                    await Attendre(1500);
                    Capture(w, "36-enregistrements-en-cours.png");
                    await App.Audio.Recorder.ToggleAsync(predication);
                    Ok(!App.Audio.SourdinesForcees.Contains(media.Id) && predication.Track == 0, "« Prédication » arrêtée : le sinus revient, sa piste est libérée");
                    await Attendre(2500);
                    await App.Audio.Recorder.ToggleAsync(culte);
                    string? video = App.Audio.Recorder.LastFile(culte.Id), wav = App.Audio.Recorder.LastFile(predication.Id);
                    long tv = video != null && File.Exists(video) ? new FileInfo(video).Length : 0, ta = wav != null && File.Exists(wav) ? new FileInfo(wav).Length : 0;
                    Ok(App.Audio.Recorder.State(culte.Id) == Audio.ChurchRecordingState.Ready && tv > 20000, $"vidéo MKV finalisée : {Path.GetFileName(video)} ({tv} octets, {App.Audio.Recorder.Message(culte.Id)})");
                    Ok(wav != null && wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && ta > 48000 * 3, $"WAV de la prédication : {Path.GetFileName(wav)} ({ta} octets)");
                    if (tv > 0)
                    {
                        var (code, journal) = await Decrire(App.Audio.Ffmpeg, video!);
                        var duree = System.Text.RegularExpressions.Regex.Match(journal, @"Duration: (\d+):(\d+):([0-9.]+)");
                        double s = duree.Success ? int.Parse(duree.Groups[1].Value) * 3600 + int.Parse(duree.Groups[2].Value) * 60 + double.Parse(duree.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                        Ok(code == 0 && journal.Contains("Video: ") && journal.Contains("Audio: aac"), "MKV relu en entier par ffmpeg : une piste vidéo et une piste audio");
                        Ok(s > 3, $"MKV : durée {s:0.0} s");
                        foreach (var ligne in journal.Split('\n').Where(l => l.Contains("Stream #"))) Note("  " + ligne.Trim());
                    }
                }
                else Note("pistes d'OBS inactives sur cette machine (pas de carte graphique) : essais des pistes et des enregistrements non faits");
            }
            await Fenetre(w, () => new Audio.FiltresWindow(micro), "33-filtres.png");
            await Fenetre(w, () => new Audio.EnregistrementWindow(culte), "34-enregistrement.png");
            await Fenetre(w, () => new Audio.SortieWindow(zoom), "35-sortie.png");
        }
        catch (Exception x) { Echec("panneau Audio : " + x); }
        finally
        {
            try { await App.Audio.Recorder.StopAllAsync(); } catch (Exception x) { Echec("arrêt des enregistrements : " + x.Message); }
            cfg.Sources.Clear(); cfg.Sources.AddRange(avantSources);
            cfg.Profiles.Clear(); cfg.Profiles.AddRange(avantProfils);
            cfg.VirtualSounds.Clear(); cfg.VirtualSounds.AddRange(avantSorties);
            App.Audio.Apply();
            w.PanneauAudio?.Rafraichir();
            try { Directory.Delete(dossierTest, true); } catch (Exception) { }
        }
    }

    /// <summary>ffmpeg relit tout le fichier (sans rien écrire) : code de sortie et journal (flux, durée).</summary>
    static async Task<(int Code, string Journal)> Decrire(string ffmpeg, string fichier)
    {
        var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-i", fichier, "-map", "0", "-f", "null", "-" }) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        var erreurs = p.StandardError.ReadToEndAsync();
        var sortie = p.StandardOutput.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { try { p.Kill(true); } catch (Exception) { } throw; }
        await sortie;
        return (p.ExitCode, await erreurs);
    }

    static void EcrireSinus(string chemin)
    {
        const int rate = 48000, secondes = 4;
        using var f = new BinaryWriter(File.Create(chemin));
        int octets = rate * secondes * 4;
        f.Write("RIFF"u8); f.Write(36 + octets); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(rate); f.Write(rate * 4); f.Write((short)4); f.Write((short)16);
        f.Write("data"u8); f.Write(octets);
        for (int i = 0; i < rate * secondes; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5 * 32767);
            f.Write(v); f.Write(v);
        }
    }

    static string Police(string cle)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            var t = new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return t.TryGetGlyphTypeface(out var g) ? string.Join("/", g.FamilyNames.Values) : "aucune police";
        }
        catch (Exception x) { return "erreur " + x.Message; }
    }

    /// <summary>Le poids réel (100 à 900) de la police trouvée pour un poids demandé : vérifie les fichiers Manrope inclus.</summary>
    static int Poids(string cle, FontWeight poids)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            return new Typeface(f, FontStyles.Normal, poids, FontStretches.Normal).TryGetGlyphTypeface(out var g) ? g.Weight.ToOpenTypeWeight() : 0;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>La police a-t-elle l'espace très fine (U+200A) que Capitales glisse entre les lettres ?</summary>
    static bool EspaceFine(string cle)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            return new Typeface(f, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal).TryGetGlyphTypeface(out var g) && g.CharacterToGlyphMap.ContainsKey(0x200A);
        }
        catch (Exception) { return false; }
    }

    public static async void Lancer(MainWindow w, string sortie)
    {
        dossier = sortie;
        Directory.CreateDirectory(sortie);
        var R = App.Regie;
        try
        {
            Note($"MyChurch {App.Version} · données : {App.Donnees.Dossier} · {App.Donnees.Chants.Count} chants");
            Note("police Serif : " + Police("Serif") + " · Sans : " + Police("Sans") + " · Titre : " + Police("Titre"));
            Note("police de l'interface : " + Police("Ui") + " · titres : " + Police("Grotesk") + " · chiffres : " + Police("Mono"));
            Ok(Police("Ui").Contains("Manrope") && Police("Grotesk").Contains("Manrope"), "police Manrope incluse (interface 3.2, design « Pupitre »)");
            int p5 = Poids("Ui", FontWeights.Medium), p6 = Poids("Ui", FontWeights.SemiBold), p7 = Poids("Ui", FontWeights.Bold), p8 = Poids("Ui", FontWeights.ExtraBold);
            Ok(p5 == 500 && p6 == 600 && p7 == 700 && p8 == 800, $"Manrope : les vrais poids 500, 600, 700, 800 ({p5}, {p6}, {p7}, {p8})");
            Ok(Police("Mono").Contains("IBM Plex Mono"), "police IBM Plex Mono incluse (heures et chronos)");
            Ok(EspaceFine("Ui"), "Manrope : espace très fine U+200A des étiquettes en capitales espacées");
            Ok(Police("Serif").Contains("Literata"), "police Literata incluse");
            Ok(Police("Sans").Contains("Atkinson"), "police Atkinson incluse");
            await Attendre(1500);
            Capture(w, "00-accueil-culte.png");
            // Les anciens réglages OBS ne doivent ni simuler un écran ouvert, ni polluer l'aperçu.
            App.Donnees.Reglages.Obs.Actif = true;
            App.Donnees.Reglages.Ecran = new EcranChoisi { Mode = "obs" };
            Ok(!R.EcranOuvert() && !w.TestStatutProjection.Contains("OBS"), "projection indépendante : anciens réglages OBS ignorés");
            App.Donnees.Reglages.Obs.Actif = false;
            w.TestOngletAnnonces();
            Ok(w.TestMenuSources(), "Display : menu déroulant d'ajout et commandes accessibles");
            Capture(w, "00b-display-menu.png");
            w.TestOnglet(true);
            await Attendre(400);
            Capture(w, "01-bible.png");

            w.TestRecherche("jn 3 16");
            await Attendre(300);
            w.TestAfficherSelection();
            await Attendre(500);
            Ok(R.Etat.Ref == "Jean 3:16 (LSG)", "Jean 3:16 à l'écran : " + R.Etat.Ref);
            Capture(w, "02-verset.png");

            var ecrans = Natif.Ecrans();
            Note("écrans : " + string.Join(" | ", ecrans.Select(e => $"{e.Nom} {e.W}×{e.H} @ {e.X},{e.Y}{(e.Principal ? " principal" : "")}")));
            w.OuvrirSur(ecrans[0], true);
            await Attendre(1500);
            Ok(w.Ecran != null, "fenêtre de projection ouverte");
            Capture(w.Ecran, "03-ecran.png");
            R.Bouger(1);
            await Attendre(1300);
            Ok(R.Etat.Ref == "Jean 3:17 (LSG)", "Suivant → " + R.Etat.Ref);
            Capture(w.Ecran, "04-ecran-suivant.png");
            R.Presentation("bandeau");
            R.Theme("vert");
            await Attendre(1300);
            Capture(w.Ecran, "05-ecran-bandeau-vert.png");
            R.Presentation("plein");
            R.Theme("aube");
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(1300);
            Ok(R.Etat.Second != null && R.Etat.Ref.Contains("LSG · KJV"), "versets bilingues : " + R.Etat.Ref);
            Capture(w, "06-bilingue.png");
            Capture(w.Ecran, "07-ecran-bilingue.png");
            App.Bibles.BasculerSeconde("kjv");
            R.Theme("nuit");

            w.TestRecherche("charite pasiente");
            await Attendre(3000);
            Capture(w, "08-recherche.png");

            w.TestOnglet(false);
            await Attendre(400);
            var c = w.TestPremierChant();
            Ok(c != null, "liste des chants : " + (c?.Title ?? "vide"));
            if (c != null)
            {
                w.OuvrirChant(c.Id);
                R.AfficherSection(c.Id, 0);
            }
            await Attendre(1300);
            Ok(R.Etat.Mode == "chant", "chant à l'écran");
            Capture(w, "09-chants.png");
            Capture(w.Ecran, "10-ecran-chant.png");

            // recherche par numéro dans le recueil choisi (un numéro à 2 chiffres : pas de 116 ni de 160)
            var nums = w.TestNumero("").Select(x => Regie.Numero(x.Title)).ToList();
            var cible = nums.FirstOrDefault(x => x >= 10 && x <= 99) ?? nums.FirstOrDefault(x => x != null);
            if (cible is int nn)
            {
                var l = w.TestNumero(nn.ToString());
                Ok(l.Count >= 1 && l.All(x => Regie.Numero(x.Title) == nn), $"recherche par numéro {nn} : " + string.Join(" | ", l.Select(x => x.Title)));
                await Attendre(500);
                Capture(w, "09b-numero.png");
                var vide = w.TestNumero("99999");
                Ok(vide.Count == 0, "numéro absent : liste vide");
                w.TestNumero("");
            }

            // 5.0.1 : boutons « + » devant les chants (liste) et les versets (Bible)
            var agPlus = R.NouvelAgenda("Test des boutons +");
            int ajC = w.TestPlusPremierChant(), ajV = w.TestPlusPremierVerset();
            Ok(ajC == 1 && ajV == 1 && R.Agenda.Elements.Count == 2, $"boutons + devant les chants et les versets : {ajC} / {ajV}");
            R.SupprimerAgenda(agPlus.Id);

            // 5.2 (design « Aube ») : déroulé du culte, recherche Ctrl K, compte à rebours d'accueil, résumé, displays, écran de scène
            w.TestOngletAgenda();
            var agTest = R.NouvelAgenda("Culte de test");
            var compte = R.AjouterCompteAgenda(5);
            if (c != null) R.AjouterChantAgenda(c.Id);
            var lecture = R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
            R.HeureAgenda(lecture.Id, "10h30");
            R.AjouterTexteAgenda("Annonces", "Bienvenue à tous !\n\nRepas fraternel après le culte");
            Ok(R.Agenda.Elements.Count == (c != null ? 4 : 3) && R.Agenda.Elements[0].Type == "compte", $"déroulé : {R.Agenda.Elements.Count} éléments, le compte à rebours en premier");
            Ok(lecture.Heure == "10:30", "heure prévue : " + lecture.Heure);
            R.OublierResume();
            R.AfficherElement(compte, 0);
            await Attendre(1300);
            var lc = R.Etat.Lignes;
            Ok(lc.Count == 2 && lc[0].StartsWith("Le culte commence") && (lc[1] == "05:00" || lc[1].StartsWith("04:5")), "compte à rebours : " + string.Join(" / ", lc));
            await Attendre(1200);
            Ok(R.Etat.Lignes.Count == 2 && R.Etat.Lignes[1].StartsWith("04:5"), "le compte à rebours descend : " + string.Join(" / ", R.Etat.Lignes));
            if (w.Ecran != null) Capture(w.Ecran, "19b-ecran-compte.png");
            var cm = w.TestCommande("jean 3 16");
            Ok(cm.Count > 0 && cm[0].Titre == "Jean 3:16", "recherche du culte (Ctrl K) : " + string.Join(" | ", cm.Select(x => x.Titre)));
            w.TestCommande("");
            for (int i = 0; i < 15 && R.ElementCourant?.Type != "bible"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "bible" && R.Etat.Ref.StartsWith("Jean 3:16"), "déroulé : Suivant passe à la lecture · " + R.Etat.Ref);
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(900);
            int nbVignettes = R.Agenda.Elements.Sum(e => R.Diapos(e).Count);
            Ok(w.TestVignettes == nbVignettes && nbVignettes >= 5, $"page Culte : toutes les diapositives du culte en vignettes ({w.TestVignettes} / {nbVignettes})");
            Ok(w.TestVignetteDirecte.StartsWith("Jean 3:16") && w.TestVignetteDirecte.EndsWith("À L'ÉCRAN"), "vignette à l'écran : " + w.TestVignetteDirecte);
            Ok(w.TestVignetteSuivante == "Jean 3:17" && w.TestSuivantLabel == "Jean 3:17", $"vignette suivante et aperçu « Suivant » : {w.TestVignetteSuivante} / {w.TestSuivantLabel}");
            Capture(w, "19-culte.png");
            App.Bibles.BasculerSeconde("kjv");
            Ok(R.EstFait(compte) && w.TestResume.Contains("Accueil") && w.TestResume.Contains("Jean 3"), "résumé du culte : " + w.TestResume);
            Ok(R.DeroulePartage().Contains("10:30") && R.ResumePartage().Length > 0, "textes à partager sur WhatsApp");
            for (int i = 0; i < 5 && R.ElementCourant?.Type != "texte"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "texte" && R.Etat.Lignes.FirstOrDefault() == "Bienvenue à tous !", "déroulé : puis les displays");
            await Attendre(500);
            Capture(w, "20-culte-displays.png");
            if (w.Ecran != null) Capture(w.Ecran, "21-ecran-displays.png");
            w.TestNoir();
            await Attendre(600);
            Ok(w.TestEstNoir, "écran noir (touche B)");
            R.Bouger(1);
            await Attendre(300);
            Ok(!w.TestEstNoir, "la diapositive suivante rallume l'écran");

            // 3.3 : écran par défaut — un écran principal dans le déroulé ; Retirer de l'écran revient direct dessus
            Ok(w.TestBarreLarge == (w.ActualWidth >= 1360), "barre de gauche large ou compacte selon la fenêtre (" + (int)w.ActualWidth + " px)");
            var principal = R.AjouterPrincipalAgenda(null);
            await Attendre(400);
            Ok(principal != null && w.TestEcranDefautChoix.Split(" | ").Count(x => x.StartsWith("Fond seul")) == 2,
                "écran principal ajouté au déroulé, proposé dans « Écran par défaut » : " + w.TestEcranDefautChoix);
            if (principal != null)
            {
                var avantRetrait = R.Etat.Lignes.FirstOrDefault();
                Ok(w.TestChoisirEcranDefaut(principal.Id) && R.EcranDefaut == principal.Id, "clic sur l'écran principal dans « Écran par défaut »");
                Ok(R.Etat.Lignes.FirstOrDefault() == avantRetrait && w.TestCulteRetour.EndsWith(principal.Titre),
                    "le choix ne touche pas aux paroles à l'écran · " + w.TestCulteRetour);
                R.Effacer();
                await Attendre(900);
                Ok(R.Cible is CibleDefaut && R.Etat.Mode == "fond" && R.EtatDiffusion(R.Etat).Vide, "Retirer de l'écran : retour direct sur l'écran par défaut (fond seul), OBS vide");
                Ok(w.TestVignetteDirecte.Contains("À L'ÉCRAN") && w.TestCulteAEcran.Contains("défaut"), "vignette et écran live : " + w.TestVignetteDirecte + " · " + w.TestCulteAEcran);
                Capture(w, "21b-culte-ecran-par-defaut.png");
                if (w.Ecran != null) Capture(w.Ecran, "21c-ecran-par-defaut.png");
                R.Bouger(-1);
                await Attendre(400);
                Ok(R.Etat.Lignes.FirstOrDefault() == avantRetrait, "Précédent remet ce qui a été retiré : " + string.Join(" / ", R.Etat.Lignes));
                w.TestChoisirEcranDefaut("noir");
                R.RetirerAgenda(principal.Id);
                await Attendre(300);
                Ok(R.EcranDefaut == "noir" && R.Etat.Lignes.FirstOrDefault() == avantRetrait, "écran par défaut remis à noir, écran principal retiré du déroulé");
            }

            // 4.0 : MyCam — l'image JPEG d'un téléphone (comme reçue par sa connexion) s'affiche dans Display, témoin rouge
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2A, 0x33, 0x46), Color.FromRgb(0xC8, 0x96, 0x2E), 30), null, new Rect(0, 0, 640, 360));
                    dc.DrawText(new FormattedText("MyCam · image du téléphone", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 34, Brushes.White, 1.0), new Point(40, 160));
                }
                var rtb = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new JpegBitmapEncoder { QualityLevel = 82 };
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                enc.Save(ms);
                var jpeg = ms.ToArray();
                var msg = new byte[jpeg.Length + 4];
                msg[0] = (byte)'M'; msg[1] = (byte)'C'; msg[2] = 1; msg[3] = 0;
                jpeg.CopyTo(msg, 4);
                var cams = App.Telephone.Cameras;
                cams.Infos("autotest-cam", "Téléphone test", System.Text.Json.Nodes.JsonNode.Parse("""{"nom":"Caméra · Chaire","taille":"640 × 360","diffuse":true,"batterie":88,"lien":"wifi"}""")!.AsObject());
                var cam = R.NouveauDisplay("Caméra · Chaire", new SourceDisplay { Type = "mycam", Peripherique = "autotest-cam" });
                R.GrilleAnnonce(cam.Id, true);
                R.AfficherAnnonce(cam);
                for (int k = 0; k < 12; k++)
                {
                    cams.Recevoir("autotest-cam", "Téléphone test", msg, msg.Length);
                    await Attendre(80);
                }
                await Attendre(600);
                Ok(App.Sources.Image is { PixelWidth: 640, PixelHeight: 360 }, "MyCam : l'image du téléphone est à l'écran (" + App.Sources.Statut + ")");
                Ok(R.TemoinMyCam("autotest-cam") == "live", "MyCam : témoin rouge pour le téléphone");
                Capture(w, "21d-mycam-direct.png");
                if (w.Ecran != null) Capture(w.Ecran, "21e-mycam-ecran.png");
                // 4.0.1 : OBS lit display.jpg pendant qu'on le remplace (Windows : « accès refusé ») ; la caméra ne doit ni s'arrêter ni passer au noir
                string jpgObs = Path.Combine(App.SortieObs.Dossier, "display.jpg");
                if (File.Exists(jpgObs))
                {
                    var avantVerrou = App.Sources.Image;
                    using (var verrou = new FileStream(jpgObs, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        for (int k = 0; k < 12; k++)
                        {
                            cams.Recevoir("autotest-cam", "Téléphone test", msg, msg.Length);
                            await Attendre(120);
                        }
                    }
                    await Attendre(300);
                    Ok(App.Sources.Image != null && !ReferenceEquals(App.Sources.Image, avantVerrou) && App.Sources.Statut == "En direct",
                        "4.0.1 : display.jpg tenu ouvert par OBS (accès refusé) : MyCam continue (" + App.Sources.Statut + ")");
                }
                else Note("display.jpg absent : essai du fichier tenu par OBS non fait");
                // téléphone tenu à la verticale, image entière : en hauteur, avec un fond flou sur l'écran
                var debout = (byte[])msg.Clone();
                debout[3] = 1;
                for (int k = 0; k < 6; k++)
                {
                    cams.Recevoir("autotest-cam", "Téléphone test", debout, debout.Length);
                    await Attendre(80);
                }
                await Attendre(500);
                Ok(App.Sources.Image is { PixelWidth: 360, PixelHeight: 640 }, "MyCam en hauteur : image tournée d'un quart de tour (" + App.Sources.Image?.PixelWidth + " × " + App.Sources.Image?.PixelHeight + ")");
                if (w.Ecran != null) Capture(w.Ecran, "21f-mycam-en-hauteur.png");
                R.Effacer();
                await Attendre(500);
                Ok(R.TemoinMyCam("autotest-cam") == "off", "MyCam retirée : témoin éteint");
                // la suite de l'autotest compte les tuiles de la grille : la source de test repart
                R.SupprimerAnnonce(cam.Id);
                cams.Deconnecte("autotest-cam");
                await Attendre(300);
            }

            // fonds clairs Lin et Ciel
            R.AfficherElement(lecture, 0);
            R.Theme("lin");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22-ecran-lin.png");
            R.Theme("ciel");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22b-ecran-ciel.png");
            R.Theme("aube");

            // écran de scène
            var scene = w.TestScene();
            await Attendre(1200);
            Ok(scene != null, "écran de scène ouvert");
            Capture(scene, "23-scene.png");
            try { scene?.Close(); } catch (Exception) { }

            // bibliothèque d'displays
            var an = R.NouvelleAnnonce("Repas fraternel", "Repas fraternel après le culte\nTout le monde est invité");
            w.TestOngletAnnonces();
            w.TestChoisirAnnonce(an);
            await Attendre(600);
            Capture(w, "24-displays.png");
            int avantAn = R.Agenda.Elements.Count;
            var elAn = R.AjouterAnnonceAgenda(an.Id);
            Ok(elAn != null && R.Agenda.Elements.Count == avantAn + 1 && elAn.Type == "texte", "display ajoutée au déroulé");
            R.AfficherAnnonce(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "display à l'écran : " + string.Join(" / ", R.Etat.Lignes));
            // MyChurch 6: the same double-click handler sends the selected title live.
            R.Effacer();
            w.TestDisplayDoubleClick(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "double-clic titre Display : en direct");
            var imagePath = Path.Combine(App.Donnees.Dossier, "test-display.png");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(200, 30, 40)), null, new Rect(0, 0, 1280, 720));
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var f = File.Create(imagePath)) png.Save(f);
            var img = R.NouveauDisplay("Image de test", new SourceDisplay { Type = "image", Chemin = imagePath });
            w.TestChoisirAnnonce(img); w.TestDisplayDoubleClick(img);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "image Display chargée et projetée");
            Capture(w, "25-display-image.png");
            // 1.0.1 : grille de sources (un clic sur une tuile = en direct)
            R.GrilleAnnonce(an.Id, true);
            R.GrilleAnnonce(img.Id, true);
            await Attendre(400);
            Ok(w.TestTuiles == 2, "grille Display : " + w.TestTuiles + " tuiles (un texte, une image)");
            Ok(w.TestCliquerTuile(0) && R.Cible is CibleTexte { I: 0 } ct && ct.Element == an.Id && R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "clic sur une tuile de la grille : en direct");
            await Attendre(500);
            Capture(w, "27-display-grille.png");

            // 3.0 : panneau Audio sous la grille, sur le vrai moteur d'OBS (dossier « obs » à côté de MyChurch.exe)
            await TestAudio(w, an, img);
            // 4.0 : l'audio comme OBS 32 (périphériques globaux, entrées branchées, mélangeur 32.1, propriétés avancées, raccourcis, réglages)
            await TestAudioObs32(w);
            w.TestCliquerTuile(1);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "clic sur la tuile image : en direct");
            if (w.Ecran != null) Capture(w.Ecran, "26-ecran-image.png");
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "display.jpg")), "image Display publiée pour la source navigateur OBS");
            if (App.CameraVirtuelle != null)
            {
                using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var view = mm.CreateViewAccessor();
                Ok(view.ReadInt32(0) == 0x4D43414D && view.ReadInt32(16) == 1280 && view.ReadInt64(8) > 0, "MyVirtualCam publie automatiquement en 720p");
                var pixel = 64 + (360 * 1280 + 640) * 3;
                Ok(view.ReadByte(pixel) == 40 && view.ReadByte(pixel + 1) == 30 && view.ReadByte(pixel + 2) == 200, "MyVirtualCam reçoit les pixels réels de la projection");
            }
            // Closed/unavailable windows report a failure and clear the picture rather than retaining a stale image.
            var absent = R.NouveauDisplay("Fenêtre fermée", new SourceDisplay { Type = "fenetre", Fenetre = 1, Processus = -1 });
            R.AfficherAnnonce(absent); await Attendre(500);
            Ok(App.Sources.Image == null && App.Sources.Statut.Contains("fermée"), "fenêtre fermée : image effacée et message utile");
            // 1.0.2 : Duo (deux contenus en même temps à l'écran)
            // 1.1 : onglet Duo — un dossier de 2 éléments, chacun à sa place, ajouté dans Display
            var dossier = R.NouveauDuo("Verset et annonce");
            dossier.A = new PartDuo { Type = "bible", Ref = new[] { 42, 3, 16, 16 }, Titre = "Jean 3:16", X = 0, Y = 0, L = 0.7 };
            dossier.B = new PartDuo { Type = "display", Id = an.Id, Titre = an.Titre, X = 0.55, Y = 0.55, L = 0.45 };
            R.EnregistrerDuo(dossier);
            w.TestOngletDuo();
            w.TestChoisirDossier(dossier);
            w.TestAfficherDossier();
            await Attendre(1600);
            Ok(R.Etat.Mode == "verset" && R.Etat.Duo is { Place: "libre" } dl0 && dl0.Dossier == dossier.Id, "onglet Duo : le dossier part en direct (verset + annonce)");
            Capture(w, "31-onglet-duo.png");
            if (w.Ecran != null) Capture(w.Ecran, "31-onglet-duo-ecran.png");
            var tuileDuo = R.AjouterDuoDisplay(dossier);
            R.Effacer();
            Ok(R.Etat.Duo == null, "Retirer de l'écran : le dossier part en entier");
            R.AfficherAnnonce(tuileDuo);
            await Attendre(400);
            Ok(R.Etat.Duo is { Place: "libre" } && R.Etat.Mode == "verset", "Display : la tuile du dossier Duo l'envoie en direct");
            // 3.0.1 : la vignette du dossier montre ses deux éléments comme sur l'écran principal
            w.TestOngletAnnonces();
            await Attendre(700);
            Ok(w.TestTuileDuoDessinee, "grille Display : la vignette du dossier Duo dessine ses deux éléments (comme à l'écran)");
            Capture(w, "31b-grille-duo.png");
            await Fenetre(w, () => new ChoixChantWindow(null), "31c-choix-chant.png");
            R.SupprimerDuo(dossier.Id);
            Ok(R.Etat.Vide && R.Annonces.All(x => x.Source?.Type != "duo"), "dossier supprimé : sa tuile Display aussi");
            w.TestOngletAnnonces();

            R.Effacer();
            R.AfficherAnnonce(an);
            await Attendre(400);
            w.TestBasculerDuo();
            await Attendre(300);
            Ok(R.Etat.Duo != null && R.Etat.Vide && R.Etat.Duo.Libelle == "Repas fraternel après le culte", "Duo : texte Display épinglé (touche P), écran principal libéré");
            R.AfficherVerset(42, 3, 16, 16);
            await Attendre(1600);
            Ok(R.Etat.Duo != null && R.Etat.Mode == "verset", "Duo : un verset + un texte épinglé");
            Ok(w.TestDuoLibelle == "Repas fraternel après le culte", "carte DUO : contenu épinglé affiché (" + w.TestDuoLibelle + ")");
            Capture(w, "28-duo.png");
            if (w.Ecran != null) Capture(w.Ecran, "28-duo-ecran.png");
            Ok(w.TestDuoPlace("cote", 35) && R.Etat.Duo?.Place == "cote", "carte DUO : côte à côte");
            await Attendre(1300);
            Capture(w, "29-duo-cote.png");
            if (w.Ecran != null) Capture(w.Ecran, "29-duo-cote-ecran.png");
            Ok(w.TestDuoPlace("bg", 50) && R.Etat.Duo is { Place: "bg", Part: 0.5 }, "carte DUO : bas gauche, 50 %");
            Ok(w.TestDuoPlace("hd", 35) && R.Etat.Duo is { Place: "hd", Part: 0.35 }, "carte DUO : haut droite, 35 %");
            // une image épinglée : la 2e capture fournit son image vivante pendant qu'un verset est à l'écran
            R.AfficherAnnonce(img);
            await Attendre(1300);
            w.TestEpingler();
            R.AfficherVerset(42, 3, 16, 16);
            await Attendre(1600);
            Ok(R.Etat.Duo?.Etat.Media == img.Source && R.Etat.Mode == "verset", "Duo : un verset + une image épinglée");
            Ok(App.SourcesDuo.Image != null && App.Sources.Image == null, "Duo : image vivante du contenu épinglé (2e capture)");
            var duoJpg = new FileInfo(Path.Combine(App.SortieObs.Dossier, "display-duo.jpg"));
            Ok(duoJpg.Exists && duoJpg.Length > 3000, "Duo : image épinglée publiée pour la page OBS (" + (duoJpg.Exists ? duoJpg.Length : 0) + " octets)");
            if (App.CameraVirtuelle != null)
            {
                await Attendre(300);
                using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var view = mm.CreateViewAccessor();
                // milieu du coin haut droite (35 % de 1280 × 720, marge 3 %) ; l'image de la caméra virtuelle est rangée de bas en haut
                var pixel = 64 + ((719 - 147) * 1280 + 1034) * 3;
                Ok(view.ReadByte(pixel) == 40 && view.ReadByte(pixel + 1) == 30 && view.ReadByte(pixel + 2) == 200,
                    $"MyVirtualCam reçoit l'image du contenu épinglé ({view.ReadByte(pixel + 2)},{view.ReadByte(pixel + 1)},{view.ReadByte(pixel)})");
            }
            Capture(w, "30-duo-image.png");
            if (w.Ecran != null) Capture(w.Ecran, "30-duo-image-ecran.png");
            w.TestBasculerDuo();
            await Attendre(400);
            Ok(R.Etat.Duo == null && R.Etat.Mode == "verset" && App.SourcesDuo.Image == null, "Duo retiré (touche P) : le verset reste, la 2e capture s'arrête");
            R.Effacer();
            R.SupprimerAnnonce(img.Id); R.SupprimerAnnonce(absent.Id);
            R.SupprimerAnnonce(an.Id);
            R.SupprimerAgenda(agTest.Id);
            w.TestOnglet(false);
            R.Effacer();
            await Attendre(900);
            Capture(w, "11-vide.png");

            // 3.1 : les pages en clair puis en sombre, la page Réglages, le + de la bibliothèque Display
            var agC = R.NouvelAgenda("Culte du dimanche 15 novembre");
            R.AjouterCompteAgenda(5);
            if (c != null) R.AjouterChantAgenda(c.Id);
            var lect = R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
            R.HeureAgenda(lect.Id, "10:30");
            R.AjouterTexteAgenda("Annonces", "Repas fraternel après le culte\n\nRéunion de prière mercredi à 19 h");
            R.AfficherElement(lect, 0);
            w.TestPage("agenda");
            await Attendre(600);
            int k17 = R.Agenda.Elements.TakeWhile(e => e != lect).Sum(e => R.Diapos(e).Count) + 1;
            Ok(w.TestCliquerVignette(k17) && R.Etat.Ref.StartsWith("Jean 3:17"), "clic sur une vignette : en direct (" + R.Etat.Ref + ")");
            await Attendre(300);
            Ok(w.TestVignetteDirecte.StartsWith("Jean 3:17"), "la vignette cliquée passe « À L'ÉCRAN »");
            Ok(w.TestDeroule.StartsWith("Culte du dimanche / 15 novembre / 10:30") && w.TestDeroule.Contains("4 éléments") || c == null, "en-tête du déroulé : " + w.TestDeroule);
            R.AfficherElement(lect, 0);
            await Attendre(400);
            Capture(w, "39-pupitre-culte.png");
            w.TestBasculerTheme();
            Ok(Palette.Actuelle.Sombre && w.TestTheme == "Clair", "bouton de la barre de gauche : thème sombre");
            w.TestBasculerTheme();
            Ok(!Palette.Actuelle.Sombre && w.TestTheme == "Sombre", "bouton de la barre de gauche : retour au clair");
            var anG = R.NouvelleAnnonce("Annonce de test", "Réunion de prière\nmercredi à 19 h");
            w.TestPage("annonces");
            w.TestPlusGrille(anG);
            Ok(anG.Grille, "Display : le + ajoute la source à la grille");
            w.TestPlusGrille(anG);
            Ok(!anG.Grille, "Display : le ✓ la retire de la grille");
            w.TestPlusGrille(anG);
            w.TestPage("reglages");
            await Attendre(600);
            Capture(w, "40-reglages.png");
            App.AppliquerApparence("sombre");
            Ok(Palette.Actuelle.Sombre && App.Donnees.Reglages.Apparence == "sombre", "thème sombre appliqué et enregistré");
            Ok(((SolidColorBrush)MainWindow.Pinceau("Page")).Color == Palette.De("Page", true), "les pinceaux suivent le thème sombre");
            await Attendre(500);
            Capture(w, "41-sombre-reglages.png");
            foreach (var (page, nom) in new[] { ("agenda", "42-sombre-culte.png"), ("bible", "43-sombre-bible.png"), ("chants", "44-sombre-chants.png"), ("annonces", "45-sombre-display.png"), ("duo", "46-sombre-duo.png") })
            {
                w.TestPage(page);
                await Attendre(500);
                Capture(w, nom);
            }
            await Fenetre(w, () => new OutilsWindow(false), "47-sombre-fenetre.png");
            App.AppliquerApparence("clair");
            Ok(!Palette.Actuelle.Sombre && ((SolidColorBrush)MainWindow.Pinceau("Page")).Color == Palette.De("Page", false), "retour au thème clair");
            foreach (var (page, nom) in new[] { ("agenda", "48-clair-culte.png"), ("annonces", "49-clair-display.png"), ("bible", "49b-clair-bible.png"), ("chants", "49c-clair-chants.png"), ("duo", "49d-clair-duo.png") })
            {
                w.TestPage(page);
                await Attendre(500);
                Capture(w, nom);
            }
            R.SupprimerAnnonce(anG.Id);
            R.Effacer();
            R.SupprimerAgenda(agC.Id);

            await Fenetre(w, () => new EditeurWindow(null), "12-editeur.png", d =>
                ((EditeurWindow)d).TestColler("Mon chant collé\n\nC G Am F\nLigne une du chant\nLigne deux du chant\nLigne trois du chant\nLigne quatre du chant\nLigne cinq du chant\nLigne six du chant\nLigne sept\n\nRefrain :\nGloire à toi\nGloire à toi"));
            await Fenetre(w, () => new DecoupeWindow(new CibleVerset(42, 3, 16, 16)), "13-decoupe.png");
            await Fenetre(w, () => new ImportWindow(), "14-import.png");
            await Fenetre(w, () => new BiblesWindow(), "15-bibles.png");
            await Fenetre(w, () => new TelephoneWindow(), "17-telephone.png");
            await Fenetre(w, () => new OutilsWindow(false), "18-outils.png");

            // recueil PDF (lecture du PDF par PdfPig)
            var pdf = Path.Combine(Environment.CurrentDirectory, "windows", "MyVerse.Tests", "donnees", "recueil-test.pdf");
            if (File.Exists(pdf))
            {
                try
                {
                    var lignes = await Task.Run(() => ImportWindow.LignesPdf(pdf, new Progress<string>(_ => { })));
                    var chants = Recueils.ChantsDepuisLignes(lignes);
                    Note("PDF : " + string.Join(" | ", chants.Select(x => x.Title + " [" + string.Join(", ", x.Sections.Select(s => s.Label)) + "]")));
                    Ok(chants.Count == 3 && chants[0].Title == "1. GRACE INFINIE" && chants[0].Sections.Any(s => s.Label == "Refrain") && chants[2].Title == "3. YERA YERA",
                        "recueil PDF : 3 chants numérotés avec refrain");
                }
                catch (Exception x) { Echec("recueil PDF : " + x); }
            }
            else Note("PDF de test absent : " + pdf);

            // le serveur des téléphones répond
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var page = await http.GetStringAsync($"http://127.0.0.1:{App.Telephone.Port}/");
                Ok(page.Contains("MyChurch ✓"), $"serveur des téléphones (port {App.Telephone.Port})");
            }
            catch (Exception x) { Echec("serveur des téléphones : " + x.Message); }
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "etat.json")), "sortie OBS (etat.json)");
            Ok(File.Exists(App.SortieObs.Page), "page OBS (ecran.html)");
        }
        catch (Exception x) { Echec("exception : " + x); }
        lock (log)
        {
            log.Add("");
            log.Add("");
            log.Add("===== journal =====");
            log.AddRange(Journal.Lignes());
            log.Add("");
            log.Add(echecs.Count == 0 ? "TOUT EST BON" : echecs.Count + " ÉCHEC(S)");
            try { File.WriteAllLines(Path.Combine(dossier, "autotest.txt"), log); } catch (Exception) { }
        }
        try { w.Ecran?.Close(); } catch (Exception) { }
        Application.Current.Shutdown(echecs.Count == 0 ? 0 : 1);
    }
}
