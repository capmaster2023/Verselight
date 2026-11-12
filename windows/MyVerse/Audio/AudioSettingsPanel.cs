using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les sections audio des Paramètres (MyChurch 3.0.1) : périphériques et moteur OBS, MyVirtualSound (installation de VB-CABLE, sur clic),
/// enregistrements (dossier, ffmpeg), code source et licences (exigé par la licence GNU GPL de libobs et de FFmpeg).
/// </summary>
public sealed class AudioSettingsPanel : StackPanel
{
    public const string DepotMyChurch = "https://github.com/capmaster2023/Verselight";
    public const string DepotObs = "https://github.com/obsproject/obs-studio/tree/33.0.1";
    public const string SiteVbCable = "https://www.vb-cable.com";
    public const string SourcesFfmpeg = "https://ffmpeg.org/releases/";
    const string LicenceGplEnLigne = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";

    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly TextBlock cable = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock progression = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button installer = new() { Content = "Installer VB-CABLE", Height = 40, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };

    Window? Fenetre => Window.GetWindow(this);

    public AudioSettingsPanel()
    {
        Children.Add(Peripheriques = SectionPeripheriques());
        Children.Add(VirtualSound = SectionVirtualSound());
        Children.Add(Enregistrements = SectionEnregistrement());
        Children.Add(Licences = SectionCodeSource());
    }

    /// <summary>Les sections, pour le sommaire de la page Réglages.</summary>
    public Border Peripheriques { get; }
    public Border VirtualSound { get; }
    public Border Enregistrements { get; }
    public Border Licences { get; }

    // ------------------------------------------------------------ petits outils de mise en page
    static (Border Cadre, StackPanel Corps) Section(string titre, string? sousTitre = null)
    {
        var corps = new StackPanel();
        corps.Children.Add(new TextBlock { Text = titre, Style = S("SousTitre"), Margin = new Thickness(0) });
        if (sousTitre != null)
            corps.Children.Add(new TextBlock { Text = sousTitre, FontSize = 13, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return (new Border { Style = S("Section"), Child = corps }, corps);
    }

    static FrameworkElement Champ(string libelle, UIElement controle)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = libelle, FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 0, 0, 6) });
        p.Children.Add(controle);
        return p;
    }

    /// <summary>Une grille de deux colonnes égales, comme la maquette des Paramètres.</summary>
    static Grid DeuxColonnes(params FrameworkElement[] elements)
    {
        var g = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int lignes = (elements.Length + 1) / 2;
        for (int r = 0; r < lignes; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < elements.Length; i++)
        {
            var e = elements[i];
            Grid.SetRow(e, i / 2);
            Grid.SetColumn(e, i % 2 * 2);
            if (i / 2 < lignes - 1) e.Margin = new Thickness(0, 0, 0, 14);
            g.Children.Add(e);
        }
        return g;
    }

    static TextBlock Note(string texte, double haut = 10) => new() { Text = texte, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0) };

    /// <summary>Liste de périphériques : « Périphérique Windows par défaut » puis ceux trouvés ; le choix enregistré reste visible même débranché.</summary>
    static ComboBox Liste(List<AudioDevice> appareils, string choisi, string nomChoisi, Action<string, string> changer)
    {
        var cb = new ComboBox { MinHeight = 40 };
        cb.Items.Add(new ComboBoxItem { Content = "Périphérique Windows par défaut", Tag = "default" });
        foreach (var d in appareils) cb.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        if (choisi != "default" && choisi.Length > 0 && appareils.All(d => d.Id != choisi))
            cb.Items.Add(new ComboBoxItem { Content = (nomChoisi.Length > 0 ? nomChoisi : choisi) + " (absent)", Tag = choisi });
        cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? cb.Items[0];
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            string nom = id == "default" ? "" : (item.Content as string ?? "").Replace(" (absent)", "");
            changer(id, nom);
        };
        return cb;
    }

    static void Ouvrir(string cible)
    {
        try { Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true }); }
        catch (Exception e) { Journal.Erreur("ouverture de " + cible, e); }
    }

    /// <summary>Un lien cliquable (ouvre le navigateur).</summary>
    static TextBlock Lien(string texte, string adresse, double taille = 13)
    {
        var h = new Hyperlink(new Run(texte)) { Foreground = P("Or"), Cursor = System.Windows.Input.Cursors.Hand };
        h.Click += (_, _) => Ouvrir(adresse);
        var t = new TextBlock { FontSize = taille, TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(h);
        return t;
    }

    // ------------------------------------------------------------ Réglages › Audio, comme OBS 32 (MyChurch 4.0)
    readonly TextBlock audioEtat = new() { FontSize = 13, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly StackPanel audioCorps = new();
    readonly Border redemarrage = new()
    {
        Background = P("OrLeger"), BorderBrush = P("OrBord"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed,
    };
    bool remplissage, aRemplir = true;

    /// <summary>
    /// La page Audio des réglages d'OBS 32 : Général (fréquence, canaux), Périphériques audio globaux (Audio du bureau 1 et 2, Micro/Aux 1 à 4),
    /// Vumètres (retombée, type de crête), Avancé (écoute, atténuation de Windows, faible latence) et Raccourcis (appuyer pour couper / pour parler).
    /// En plus : les entrées branchées s'ajoutent toutes seules au mélangeur.
    /// </summary>
    Border SectionPeripheriques()
    {
        var (cadre, corps) = Section("Audio");
        corps.Children.Add(audioEtat);
        corps.Children.Add(audioCorps);
        var redemarrer = new Button { Style = S("BtnOr"), Content = "Redémarrer MyChurch", Height = 36, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        redemarrer.Click += (_, _) =>
        {
            if (A.Recorder.Reserves.Any() && !Dialogue.Confirmer(Fenetre, "Un enregistrement est en cours : il sera arrêté et son fichier finalisé avant le redémarrage.", "Redémarrer")) return;
            App.Redemarrer();
        };
        var ligne = new DockPanel();
        DockPanel.SetDock(redemarrer, Dock.Right);
        ligne.Children.Add(redemarrer);
        ligne.Children.Add(new TextBlock { Text = "Comme dans OBS, la fréquence, les canaux et la faible latence s'appliquent au prochain démarrage de MyChurch.", FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = P("Or2") });
        redemarrage.Child = ligne;
        A.Changed += () => Dispatcher.BeginInvoke(new Action(MajEtat));
        A.PeripheriquesChange += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) RemplirAudio(); else aRemplir = true; }));
        IsVisibleChanged += (_, _) => { if (IsVisible && aRemplir) RemplirAudio(); };
        RemplirAudio();
        return cadre;
    }

    void MajEtat()
    {
        audioEtat.Text = A.Running
            ? $"Moteur OBS actif · libobs {A.Version} · {A.Format}" + (A.TracksActive ? "" : " · pistes indisponibles sur cet ordinateur (carte graphique)")
            : A.Diagnostic;
        redemarrage.Visibility = A.RedemarrageRequis ? Visibility.Visible : Visibility.Collapsed;
    }

    static TextBlock Partie(string titre, double haut = 22) => new() { Text = titre, Style = S("CarteTitre"), Margin = new Thickness(0, haut, 0, 0) };

    /// <summary>Une liste de choix (identifiant, nom), comme les listes des réglages d'OBS.</summary>
    ComboBox Choix(IEnumerable<(string Id, string Nom)> choix, string choisi, Action<string> changer)
    {
        var cb = new ComboBox { MinHeight = 40 };
        foreach (var (id, nom) in choix) cb.Items.Add(new ComboBoxItem { Content = nom, Tag = id });
        cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? cb.Items[0];
        cb.SelectionChanged += (_, _) =>
        {
            if (remplissage || cb.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            changer(id);
        };
        return cb;
    }

    /// <summary>Un interrupteur suivi de son texte (cliquer sur le texte bascule aussi).</summary>
    FrameworkElement Interrupteur(string texte, string? aide, bool valeur, Action<bool> changer)
    {
        var cb = new CheckBox { Style = S("Interrupteur"), IsChecked = valeur, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        var textes = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        textes.Children.Add(new TextBlock { Text = texte, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (aide != null) textes.Children.Add(new TextBlock { Text = aide, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        cb.Click += (_, _) => { if (!remplissage) changer(cb.IsChecked == true); };
        textes.MouseLeftButtonUp += (_, _) => { cb.IsChecked = cb.IsChecked != true; changer(cb.IsChecked == true); };
        var d = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(cb, Dock.Left);
        d.Children.Add(cb);
        d.Children.Add(textes);
        return d;
    }

    /// <summary>Un périphérique global : « Désactivé », « Par défaut » ou un périphérique (comme OBS). Un périphérique débranché reste affiché.</summary>
    ComboBox ListeGlobal(AudioGlobal g)
    {
        var appareils = g.Kind == AudioInputKind.Sortie ? A.Outputs() : A.Microphones();
        var s = C.Globale(g.Slot);
        string choisi = s?.DeviceId ?? "";
        var cb = new ComboBox { MinHeight = 40 };
        cb.Items.Add(new ComboBoxItem { Content = "Désactivé", Tag = "" });
        cb.Items.Add(new ComboBoxItem { Content = "Par défaut", Tag = "default" });
        foreach (var d in appareils) cb.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        if (s != null && choisi != "default" && appareils.All(d => d.Id != choisi))
            cb.Items.Add(new ComboBoxItem { Content = (s.DeviceName.Length > 0 ? s.DeviceName : choisi) + " (débranché)", Tag = choisi });
        cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? cb.Items[0];
        cb.SelectionChanged += (_, _) =>
        {
            if (remplissage || cb.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            string nom = id is "" or "default" ? "" : (item.Content as string ?? "").Replace(" (débranché)", "");
            C.RegleGlobal(g.Slot, id.Length == 0 ? null : id, nom);
            A.Apply();
            RemplirRaccourcis();
        };
        return cb;
    }

    readonly StackPanel raccourcisListe = new();

    /// <summary>Remplit toute la page Audio (à l'ouverture des réglages et quand un périphérique est branché ou débranché).</summary>
    void RemplirAudio()
    {
        aRemplir = false;
        remplissage = true;
        try
        {
            audioCorps.Children.Clear();

            // ---- Général
            audioCorps.Children.Add(Partie("GÉNÉRAL", 16));
            var frequence = Choix(new[] { ("44100", "44,1 kHz"), ("48000", "48 kHz") }, C.SampleRate.ToString(), v => { C.SampleRate = int.Parse(v); A.Apply(); MajEtat(); });
            var canaux = Choix(AudioConfiguration.Dispositions.Select(d => (d.Id, d.Nom)), C.Channels, v => { C.Channels = v; A.Apply(); MajEtat(); });
            audioCorps.Children.Add(DeuxColonnes(Champ("Fréquence d'échantillonnage", frequence), Champ("Canaux", canaux)));
            audioCorps.Children.Add(redemarrage);
            audioCorps.Children.Add(Note("Avec plus de deux canaux, les enregistrements et les sorties MyVirtualSound reçoivent un mélange stéréo."));

            // ---- Périphériques audio globaux
            audioCorps.Children.Add(Partie("PÉRIPHÉRIQUES AUDIO GLOBAUX"));
            audioCorps.Children.Add(DeuxColonnes(AudioConfiguration.Globaux.Select(g => Champ(g.Nom, ListeGlobal(g))).ToArray()));
            audioCorps.Children.Add(Note("Comme dans OBS, un périphérique global est toujours dans le mélangeur, épinglé à gauche. « Par défaut » suit le périphérique choisi dans Windows."));
            audioCorps.Children.Add(Interrupteur("Ajouter au mélangeur les entrées audio branchées",
                "Un micro USB, une carte son ou une table de mixage branché pour la première fois apparaît tout seul dans le mélangeur. Débranché, il devient inactif ; rebranché, il revient.",
                C.AutoAddInputs, v => { C.AutoAddInputs = v; A.Apply(); }));

            // ---- Vumètres
            audioCorps.Children.Add(Partie("VUMÈTRES"));
            var retombee = Choix(AudioConfiguration.Retombees.Select(r => (r.Id, r.Nom)), C.MeterDecay, v => { C.MeterDecay = v; A.Apply(); });
            var crete = Choix(AudioConfiguration.Cretes.Select(c => (c.Id, c.Nom)), C.PeakMeter, v => { C.PeakMeter = v; A.Apply(); });
            audioCorps.Children.Add(DeuxColonnes(Champ("Vitesse de retombée", retombee), Champ("Type de crêtemètre", crete)));

            // ---- Avancé
            audioCorps.Children.Add(Partie("AVANCÉ"));
            var casque = Liste(A.MonitoringDevices(), C.MonitorDeviceId, C.MonitorDeviceName, (id, nom) =>
            {
                if (remplissage) return;
                C.MonitorDeviceId = id; C.MonitorDeviceName = nom; A.Apply();
            });
            var choixCasque = Champ("Périphérique d'écoute (casque de la régie)", casque);
            choixCasque.Margin = new Thickness(0, 14, 0, 0);
            audioCorps.Children.Add(choixCasque);
            audioCorps.Children.Add(Interrupteur("Désactiver l'atténuation audio de Windows",
                "Pendant un appel (Teams, Zoom…), Windows baisse le son des autres programmes : MyChurch n'est plus baissé.",
                C.DisableDucking, v => A.Attenuation(v)));
            audioCorps.Children.Add(Interrupteur("Mise en mémoire tampon audio à faible latence (pour les sorties Decklink/NDI)",
                "Peut faire grésiller ou couper certaines sources. S'applique au prochain démarrage.",
                C.LowLatency, v => { C.LowLatency = v; A.Apply(); MajEtat(); }));

            // ---- Raccourcis
            audioCorps.Children.Add(Partie("RACCOURCIS"));
            audioCorps.Children.Add(Note("Pour chaque source : « appuyer pour couper le son » la coupe tant que la touche est enfoncée, « appuyer pour parler » ne la laisse passer que touche enfoncée. Le délai prolonge l'effet après avoir relâché la touche. Les touches marchent même quand MyChurch est en arrière-plan.", 6));
            audioCorps.Children.Add(raccourcisListe);
            RemplirRaccourcis();
        }
        finally { remplissage = false; }
        MajEtat();
    }

    void RemplirRaccourcis()
    {
        raccourcisListe.Children.Clear();
        if (C.Sources.Count == 0) { raccourcisListe.Children.Add(Note("Aucune source dans le mélangeur pour l'instant.")); return; }
        foreach (var s in C.Sources) raccourcisListe.Children.Add(CarteRaccourcis(s));
    }

    Border CarteRaccourcis(AudioSource s)
    {
        var pile = new StackPanel();
        var titre = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        titre.Inlines.Add(new Run(s.Name));
        if (s.Global.Length > 0) titre.Inlines.Add(new Run("  ·  global") { Foreground = P("Or"), FontWeight = FontWeights.Normal, FontSize = 12.5 });
        pile.Children.Add(titre);
        pile.Children.Add(LigneRaccourci("Appuyer pour couper le son", s.PushToMute, s.PushToMuteDelayMs, s.PushToMuteKey,
            (actif, delai, touche) => { s.PushToMute = actif; s.PushToMuteDelayMs = delai; s.PushToMuteKey = touche; A.Apply(); }));
        pile.Children.Add(LigneRaccourci("Appuyer pour parler", s.PushToTalk, s.PushToTalkDelayMs, s.PushToTalkKey,
            (actif, delai, touche) => { s.PushToTalk = actif; s.PushToTalkDelayMs = delai; s.PushToTalkKey = touche; A.Apply(); }));
        return new Border { Child = pile, BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 10, 0, 0) };
    }

    /// <summary>Une ligne de raccourci : l'interrupteur, le délai (ms) et la touche.</summary>
    static FrameworkElement LigneRaccourci(string titre, bool actif, int delai, string touche, Action<bool, int, string> changer)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var cb = new CheckBox { Style = S("Interrupteur"), IsChecked = actif, VerticalAlignment = VerticalAlignment.Center };
        var nom = new TextBlock { Text = titre, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), TextWrapping = TextWrapping.Wrap };
        var etiquette = new TextBlock { Text = "Délai", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var champ = new TextBox { Style = S("Champ"), Width = 76, Text = delai.ToString(), FontSize = 14, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Après avoir relâché la touche, l'effet dure encore ce temps (millisecondes, 0 à 10 000)" };
        var ms = new TextBlock { Text = "ms", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 14, 0) };
        string t = touche;
        bool a = actif;
        int d = delai;
        BoutonTouche? bouton = null;
        void Avertir()
        {
            if (bouton == null) return;
            if (a && t.Length == 0) { bouton.BorderBrush = P("Danger"); bouton.ToolTip = "Choisissez une touche : sans touche, ce raccourci ne fait rien."; }
            else bouton.ClearValue(Control.BorderBrushProperty);
        }
        bouton = new BoutonTouche(touche, nouvelle => { t = nouvelle; changer(a, d, t); Avertir(); });
        void LireDelai()
        {
            d = int.TryParse(champ.Text.Trim(), out int v) ? Math.Clamp(v, 0, 10000) : d;
            champ.Text = d.ToString();
            changer(a, d, t);
        }
        cb.Click += (_, _) => { a = cb.IsChecked == true; changer(a, d, t); Avertir(); };
        champ.LostFocus += (_, _) => LireDelai();
        champ.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) LireDelai(); };
        Avertir();
        var elements = new FrameworkElement[] { cb, nom, etiquette, champ, ms, bouton };
        for (int i = 0; i < elements.Length; i++) { Grid.SetColumn(elements[i], i); g.Children.Add(elements[i]); }
        return g;
    }

    // ------------------------------------------------------------ MyVirtualSound
    readonly StackPanel sortiesListe = new() { Margin = new Thickness(0, 12, 0, 0) };

    Border SectionVirtualSound()
    {
        var (cadre, corps) = Section("MyVirtualSound", "Des sorties qui envoient chacune le mélange de leurs sources vers un câble audio virtuel : Zoom, Teams, OBS ou un navigateur les reçoivent comme un micro. Elles se créent dans le panneau Audio de Display (« + Créer une sortie »), 4 au plus, actives en même temps si besoin.");
        corps.Children.Add(sortiesListe);

        var detection = new Border { Background = P("Nuit2"), BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 14, 0, 0) };
        var pile = new StackPanel();
        pile.Children.Add(cable);
        installer.Style = S("BtnOr");
        installer.ToolTip = "Télécharge le paquet officiel sur vb-audio.com puis lance son installateur (Windows demande l'accord de l'administrateur)";
        installer.Click += async (_, _) =>
        {
            installer.IsEnabled = false;
            progression.Visibility = Visibility.Visible;
            var etat = new Progress<string>(t => progression.Text = t);
            try { await VbCable.InstallerAsync(etat); }
            catch (Exception e) { progression.Text = "Installation impossible : " + e.Message; Journal.Erreur("installation de VB-CABLE", e); }
            finally
            {
                installer.IsEnabled = true;
                A.Sorties.Reset();
                A.Apply();
                MajCable();
                MajSorties();
            }
        };
        pile.Children.Add(installer);
        pile.Children.Add(progression);
        detection.Child = pile;
        corps.Children.Add(detection);

        corps.Children.Add(new TextBlock { Text = "Dans Zoom, choisissez « CABLE Output » comme microphone.", FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        corps.Children.Add(Note(VbCable.Mention, 8));
        var site = Lien("www.vb-cable.com", SiteVbCable, 12.5);
        site.Margin = new Thickness(0, 2, 0, 0);
        corps.Children.Add(site);
        MajCable();
        MajSorties();
        return cadre;
    }

    void MajCable()
    {
        var trouve = VbCable.Entree();
        cable.Text = trouve is { } c ? "Câble détecté : " + c.Nom : "Aucun câble audio virtuel détecté (VB-CABLE, Virtual Audio Driver ou Virtual Cables).";
        installer.Visibility = VbCable.Installe ? Visibility.Collapsed : Visibility.Visible;
    }

    void MajSorties()
    {
        sortiesListe.Children.Clear();
        if (C.VirtualSounds.Count == 0)
        {
            sortiesListe.Children.Add(Note("Aucune sortie pour l'instant : « + Créer une sortie » dans le panneau Audio.", 0));
            return;
        }
        for (int i = 0; i < C.VirtualSounds.Count; i++)
        {
            var v = C.VirtualSounds[i];
            var t = new TextBlock { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            t.Inlines.Add(new Bold(new Run(AudioPanel.MarqueSortie(i) + " · " + v.Name)));
            t.Inlines.Add(new Run(" : " + (v.Enabled ? A.Sorties.Status(v.Id) : "désactivée")) { Foreground = P("Brume") });
            sortiesListe.Children.Add(t);
        }
    }

    // ------------------------------------------------------------ enregistrements
    Border SectionEnregistrement()
    {
        var (cadre, corps) = Section("Enregistrements", "Ils se créent dans le panneau Audio de Display (« + Créer un enregistrement ») : chacun garde les sources audio choisies et, avec des sources Display, fait une vidéo MKV. 5 au plus, qui peuvent tourner en même temps.");

        var dossier = new TextBox { Style = S("Champ"), Text = AudioConfiguration.DossierEnregistrements, IsReadOnly = true, FontSize = 14 };
        var ouvrir = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        ouvrir.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(AudioConfiguration.DossierEnregistrements); } catch (Exception) { }
            Ouvrir(AudioConfiguration.DossierEnregistrements);
        };
        var ligneDossier = new DockPanel();
        DockPanel.SetDock(ouvrir, Dock.Right);
        ligneDossier.Children.Add(ouvrir);
        ligneDossier.Children.Add(dossier);
        var champDossier = Champ("Dossier par défaut (un sous-dossier par enregistrement)", ligneDossier);
        champDossier.Margin = new Thickness(0, 14, 0, 0);
        corps.Children.Add(champDossier);

        // ffmpeg : livré avec MyChurch (FLAC et vidéo) ; un autre ffmpeg.exe peut le remplacer
        var etat = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        var autre = new Button { Style = S("Btn"), Content = "Choisir un autre ffmpeg.exe…", Height = 38, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5 };
        var inclus = new Button { Style = S("BtnLien"), Content = "Revenir à celui de MyChurch", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void MajFfmpeg()
        {
            bool present = File.Exists(ChurchAudioService.FfmpegInclus);
            if (C.FfmpegPath.Length > 0) etat.Text = "ffmpeg utilisé : " + C.FfmpegPath + (File.Exists(C.FfmpegPath) ? "" : " (introuvable : celui de MyChurch le remplace)");
            else etat.Text = present ? "ffmpeg 8.0.1 est inclus avec MyChurch : FLAC et vidéo MKV marchent sans rien installer." : "ffmpeg est absent du dossier de MyChurch : réinstallez MyChurch, ou choisissez un ffmpeg.exe.";
            etat.Foreground = present || File.Exists(C.FfmpegPath) ? P("Blanc") : P("Danger");
            inclus.Visibility = C.FfmpegPath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        autre.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "ffmpeg.exe (FLAC et vidéo)", Filter = "ffmpeg.exe|ffmpeg.exe|Programmes|*.exe" };
            bool? ok = Fenetre != null ? d.ShowDialog(Fenetre) : d.ShowDialog();
            if (ok != true) return;
            C.FfmpegPath = d.FileName;
            A.Apply();
            MajFfmpeg();
        };
        inclus.Click += (_, _) => { C.FfmpegPath = ""; A.Apply(); MajFfmpeg(); };
        var cadreFfmpeg = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        cadreFfmpeg.Children.Add(etat);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        boutons.Children.Add(autre);
        boutons.Children.Add(inclus);
        cadreFfmpeg.Children.Add(boutons);
        corps.Children.Add(cadreFfmpeg);
        corps.Children.Add(Note("La vidéo utilise l'encodeur H.264 de la carte graphique (NVIDIA, Intel ou AMD) s'il marche, sinon x264. Le son et l'image suivent la même horloge ; un décalage de caméra se corrige dans chaque enregistrement (« Synchro du son »)."));
        MajFfmpeg();
        return cadre;
    }

    // ------------------------------------------------------------ code source et licences (GPL)
    Border SectionCodeSource()
    {
        var (cadre, corps) = Section("Code source et licences");
        var texte = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) };
        texte.Inlines.Add(new Run("MyChurch est un logiciel libre. Son moteur audio utilise "));
        texte.Inlines.Add(new Bold(new Run("libobs")));
        texte.Inlines.Add(new Run(", la bibliothèque d'OBS Studio 33.0.1, distribuée sous licence GNU GPL version 2 ou ultérieure. Tout le code source de MyChurch est à votre disposition."));
        corps.Children.Add(texte);
        corps.Children.Add(LigneDepot("Code source de MyChurch", DepotMyChurch));
        corps.Children.Add(LigneDepot("Code source d'OBS Studio 33.0.1 (libobs)", DepotObs));
        corps.Children.Add(LigneDepot("Code source de FFmpeg (ffmpeg.exe 8.0.1, build gyan.dev)", SourcesFfmpeg));

        var licence = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var lire = new Button { Style = S("BtnContour"), Content = "Lire la licence GNU GPL", Height = 36, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5 };
        lire.Click += (_, _) =>
        {
            string copie = Path.Combine(AppContext.BaseDirectory, "COPYING.txt");
            Ouvrir(File.Exists(copie) ? copie : LicenceGplEnLigne);
        };
        licence.Children.Add(lire);
        licence.Children.Add(new TextBlock { Text = "Le texte complet de la licence est installé avec MyChurch (COPYING.txt).", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        corps.Children.Add(licence);
        corps.Children.Add(Note("FFmpeg (www.ffmpeg.org), livré dans le dossier « ffmpeg » de MyChurch pour le FLAC et la vidéo, est un programme libre sous licence GNU GPL version 3 : son texte est dans ffmpeg\\LICENSE.txt. Build « essentials » de gyan.dev (www.gyan.dev/ffmpeg/builds).", 14));
        corps.Children.Add(Note(VbCable.Mention + " Il n'est pas inclus dans MyChurch : il s'installe à la demande depuis le site officiel.", 8));
        corps.Children.Add(Note("Polices Manrope, IBM Plex Mono, Literata, Atkinson Hyperlegible Next et Unbounded : licence SIL Open Font License 1.1.", 6));
        return cadre;
    }

    static Border LigneDepot(string titre, string adresse)
    {
        var g = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ouvrir = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
        ouvrir.Click += (_, _) => Ouvrir(adresse);
        var copier = new Button { Style = S("Btn"), Content = "Copier le lien", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13, Margin = new Thickness(8, 0, 0, 0) };
        copier.Click += async (_, _) =>
        {
            try { Clipboard.SetText(adresse); } catch (Exception) { return; }
            copier.Content = "Lien copié";
            await Task.Delay(1600);
            copier.Content = "Copier le lien";
        };
        boutons.Children.Add(ouvrir);
        boutons.Children.Add(copier);
        DockPanel.SetDock(boutons, Dock.Right);
        g.Children.Add(boutons);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold });
        var lien = Lien(adresse, adresse);
        lien.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(lien);
        g.Children.Add(textes);
        return new Border { Child = g, BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
    }
}
