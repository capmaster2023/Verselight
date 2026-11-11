using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// La régie, design « Pupitre » (MyChurch 3.2) : barre des pages, colonne de la page (déroulé, Bibles, recueil, bibliothèque, dossiers),
/// centre (vignettes du culte, versets, strophes, grille, dossier), colonne de contrôle à droite (petit écran en direct, suivant,
/// boutons, audio) et barre du bas (enregistrements, sorties, écran, téléphones, heure).
/// </summary>
public partial class MainWindow : Window
{
    Regie R => App.Regie;
    Bibles VB => App.Bibles;
    Bible B => App.Bibles.Principale;

    EcranWindow? ecran;
    string onglet = "bible";
    bool ongletBible => onglet == "bible";
    (int B, int C)? vue;             // chapitre affiché
    Ref? sel;                        // versets choisis
    string? chantOuvert;
    int recherches;
    readonly DispatcherTimer minuterieQ = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer minuterieQc = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer minuterieNum = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly DispatcherTimer minuterieToast = new() { Interval = TimeSpan.FromMilliseconds(3200) };
    readonly DispatcherTimer minuterieHorloge = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<Button> boutonsLivres = new();
    bool chargement = true;
    MiseAJour.Resultat? maj;

    public static Brush Pinceau(string cle) => (Brush)Application.Current.FindResource(cle);
    public static Style Style_(string cle) => (Style)Application.Current.FindResource(cle);

    public MainWindow()
    {
        InitializeComponent();
        PositionDepart();
        VersionBtn.Content = "v" + App.Version;
        Placement();
        ConstruireLivres();
        ConstruireAide();
        RemplirBibles();

        // ---------- barre du haut
        OngletAgenda.Checked += (_, _) => MontrerOnglet("agenda");
        OngletBible.Checked += (_, _) => MontrerOnglet("bible");
        OngletChants.Checked += (_, _) => MontrerOnglet("chants");
        OngletAnnonces.Checked += (_, _) => MontrerOnglet("annonces");
        OngletDuo.Checked += (_, _) => MontrerOnglet("duo");
        OngletReglages.Checked += (_, _) => MontrerOnglet("reglages");
        Q.TextChanged += (_, _) =>
        {
            QIndice.Visibility = Q.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            QEffacer.Visibility = Q.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            minuterieQ.Stop(); minuterieQ.Start();
        };
        Q.GotKeyboardFocus += (_, _) => RechercheBible.BorderBrush = Pinceau("OrVif");
        Q.LostKeyboardFocus += (_, _) => RechercheBible.BorderBrush = Pinceau("Nuit3");
        Q.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            minuterieQ.Stop();
            Chercher();
            if (sel is Ref s && s.V1 > 0) AfficherVerset(s);
        };
        QEffacer.Click += (_, _) => { Q.Clear(); Q.Focus(); };
        minuterieQ.Tick += (_, _) => { minuterieQ.Stop(); Chercher(); };
        Qc.TextChanged += (_, _) =>
        {
            QcIndice.Visibility = Qc.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            QcEffacer.Visibility = Qc.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (Qc.Text.Length > 0 && NumChant.Text.Length > 0) NumChant.Clear();
            minuterieQc.Stop(); minuterieQc.Start();
        };
        Qc.GotKeyboardFocus += (_, _) => RechercheChants.BorderBrush = Pinceau("OrVif");
        Qc.LostKeyboardFocus += (_, _) => RechercheChants.BorderBrush = Pinceau("Nuit3");
        Qc.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (ListeChants.Items.Count > 0 && ListeChants.Items[0] is Chant c) OuvrirChant(c.Id);
        };
        QcEffacer.Click += (_, _) => { Qc.Clear(); Qc.Focus(); };
        minuterieQc.Tick += (_, _) => { minuterieQc.Stop(); ListeChantsMaj(); };

        // recherche par numéro, dans le recueil choisi
        NumChant.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        NumChant.TextChanged += (_, _) =>
        {
            var d = new string(NumChant.Text.Where(char.IsDigit).ToArray());
            if (d != NumChant.Text) { NumChant.Text = d; NumChant.CaretIndex = d.Length; return; }
            NumIndice.Visibility = d.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            NumEffacer.Visibility = d.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (d.Length > 0 && Qc.Text.Length > 0) Qc.Clear();
            minuterieNum.Stop(); minuterieNum.Start();
        };
        NumChant.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            AllerAuNumero(true);
        };
        NumChant.PreviewMouseLeftButtonDown += (_, e) => { if (!NumChant.IsKeyboardFocusWithin) { NumChant.Focus(); e.Handled = true; } };
        NumChant.GotKeyboardFocus += (_, _) => { NumBoite.BorderBrush = Pinceau("OrVif"); NumChant.SelectAll(); };
        NumChant.LostKeyboardFocus += (_, _) => NumBoite.BorderBrush = Pinceau("Nuit3");
        NumEffacer.Click += (_, _) => { NumChant.Clear(); NumChant.Focus(); };
        minuterieNum.Tick += (_, _) => AllerAuNumero(false);
        TelBtn.Click += (_, _) => new TelephoneWindow { Owner = this }.ShowDialog();
        ProjectionBtn.Click += (_, _) => MenuProjection();
        // le mélangeur audio (moteur OBS) : dans Display, sous la grille
        PanneauAudio = new Audio.AudioPanel();
        AudioHote.Child = PanneauAudio;
        AudioBref.Content = new Audio.AudioBref();
        AudioListeHote.Content = new Audio.AudioListe();
        Reglages = new ReglagesPage();
        PanneauReglages.Child = Reglages;
        VersionBtn.Click += (_, _) => { MontrerOnglet("reglages"); Reglages.Aller("maj", true); };
        MajBtn.Click += (_, _) => { MontrerOnglet("reglages"); Reglages.Aller("maj", false); };
        // clair ou sombre d'un clic (aussi dans Réglages › Apparence)
        ThemeBtn.Click += (_, _) => App.AppliquerApparence(Palette.Actuelle.Sombre ? "clair" : "sombre");
        Palette.Change += MajThemeBtn;
        MajThemeBtn();
        // l'heure de l'ordinateur, en bas à droite
        minuterieHorloge.Tick += (_, _) => Horloge.Text = DateTime.Now.ToString("HH:mm:ss");
        Horloge.Text = DateTime.Now.ToString("HH:mm:ss");
        minuterieHorloge.Start();

        // ---------- Bible
        BiblesImporter.Click += (_, _) => ImporterBible();
        BiblesGerer.Click += (_, _) => { new BiblesWindow { Owner = this }.ShowDialog(); RemplirBibles(); };
        ChapPrec.Click += (_, _) => { if (vue is { } v) OuvrirChapitre(v.B, v.C - 1); };
        ChapSuiv.Click += (_, _) => { if (vue is { } v) OuvrirChapitre(v.B, v.C + 1); };

        // ---------- chants
        Recueil.SelectionChanged += (_, _) =>
        {
            if (onglet == "chants") MajEntete();
            if (chargement) return;
            App.Donnees.Reglages.Recueil = (Recueil.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            App.Donnees.SauverReglages();
            ListeChantsMaj();
        };
        ListeChants.SelectionChanged += (_, _) => { if (ListeChants.SelectedItem is Chant c && c.Id != chantOuvert) OuvrirChant(c.Id); };
        Importer.Click += (_, _) => new ImportWindow { Owner = this }.ShowDialog();
        Nouveau.Click += (_, _) =>
        {
            var e = new EditeurWindow(null, null, (Recueil.SelectedItem as ComboBoxItem)?.Tag as string) { Owner = this };
            if (e.ShowDialog() == true && e.IdEnregistre != null) { ListeChantsMaj(); OuvrirChant(e.IdEnregistre); }
        };
        SupprimerRecueil.Click += (_, _) =>
        {
            if (Recueil.SelectedItem is not ComboBoxItem i) return;
            var nom = (string)i.Tag;
            int n = R.Chants.Count(c => (c.Author ?? "") == nom);
            if (!Dialogue.Confirmer(this, $"Supprimer le recueil « {(nom.Length > 0 ? nom : "Sans recueil")} » et ses {n} chants ?", "Supprimer")) return;
            R.SupprimerRecueil(nom);
            App.Donnees.Reglages.Recueil = "";
            ListeChantsMaj();
            Toast($"Recueil supprimé ({n} chants).");
        };
        Modifier.Click += (_, _) => Editer(R.Trouver(chantOuvert));
        Supprimer.Click += (_, _) =>
        {
            var c = R.Trouver(chantOuvert);
            if (c == null || !Dialogue.Confirmer(this, $"Supprimer « {c.Title} » ?", "Supprimer")) return;
            R.SupprimerChant(c.Id);
            OuvrirChant(null);
        };
        DefilSections.SizeChanged += (_, _) => LargeurSections();

        // ---------- à l'écran
        Afficher.Click += (_, _) => AfficherCourant();
        Prec.Click += (_, _) => R.Bouger(-1);
        Suiv.Click += (_, _) => R.Bouger(1);
        Effacer.Click += (_, _) => R.Effacer();
        NoirBtn.Click += (_, _) => BasculerNoir();
        Decouper.Click += (_, _) => { if (R.Prochaine() is { } s) OuvrirDecoupe(s.Cible); };
        Apercu.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (R.Cible is CibleChant cc) OuvrirDecoupe(cc);
            else if (R.Cible is CibleVerset cv)
            {
                if (cv.V1 == cv.V2) OuvrirDecoupe(new CibleVerset(cv.B, cv.C, cv.V1, cv.V1));
                else Toast("Découper : affichez un seul verset à la fois.");
            }
        };
        Apercu.SizeChanged += (_, _) => Apercu.Clip = new RectangleGeometry(new Rect(0, 0, Apercu.ActualWidth, Apercu.ActualHeight), 10, 10);
        SuivantApercu.SizeChanged += (_, _) => SuivantApercu.Clip = new RectangleGeometry(new Rect(0, 0, SuivantApercu.ActualWidth, SuivantApercu.ActualHeight), 8, 8);
        var reg = App.Donnees.Reglages;
        Presentation.SelectedItem = Presentation.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == reg.Presentation) ?? Presentation.Items[0];
        Taille.Value = Math.Clamp(reg.Taille, Taille.Minimum, Taille.Maximum);
        Presentation.SelectionChanged += (_, _) => { if (!chargement && Presentation.SelectedItem is ComboBoxItem i) R.Presentation((string)i.Tag); };
        Taille.ValueChanged += (_, _) => { if (!chargement) R.Taille(Taille.Value); };

        // ---------- régie et téléphones
        R.EcranOuvert = () => ecran != null;
        R.Change += MajEcran;
        R.ChantsChange += () => { ListeChantsMaj(); if (chantOuvert != null) OuvrirChant(chantOuvert); };
        R.Message += Toast;
        VB.Change += () => { RemplirBibles(); if (vue is { } v) OuvrirChapitre(v.B, v.C, sel?.V1 ?? 0, sel?.V2 ?? 0, false); };
        App.Telephone.Change += () => Dispatcher.BeginInvoke(new Action(MajTelephone));
        minuterieToast.Tick += (_, _) => { minuterieToast.Stop(); ToastBoite.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))); };

        PreviewKeyDown += Clavier;
        PreviewTextInput += (_, e) =>
        {
            if (e.Text != "/" || Keyboard.FocusedElement is TextBox or PasswordBox) return;
            (onglet is "agenda" or "annonces" or "duo" ? Cmd : ongletBible ? Q : Qc).Focus();
            e.Handled = true;
        };
        SizeChanged += (_, _) => Placement();
        bool closingAfterRecording = false;
        Closing += async (_, e) =>
        {
            if (!closingAfterRecording && App.Audio.Recorder.AnyBusy)
            {
                e.Cancel = true; IsEnabled = false;
                Toast("Finalisation des enregistrements en cours…");
                await App.Audio.Recorder.StopAllAsync();
                closingAfterRecording = true; Close(); return;
            }
            Fermeture();
        };
        SourceInitialized += (_, _) => Natif.TitreSombre(this, Palette.Actuelle.Sombre);
        Loaded += (_, _) => Demarre();

        RemplirRecueils();
        ListeChantsMaj();
        OuvrirChapitre(42, 3, 16, 16);
        InitAgenda();
        InitDisplay();
        InitGrille();
        InitDossiersDuo();
        InitDuo();
        AgendaAjoutBible.Click += (_, _) =>
        {
            if (sel is Ref s && s.V1 > 0) Toast($"{R.AjouterBibleAgenda(s).Titre} ajouté au déroulé « {R.Agenda.Nom} ».");
            else if (vue is { } v) Toast($"{R.AjouterBibleAgenda(new Ref(v.B, v.C, 0, 0)).Titre} (chapitre entier) ajouté au déroulé.");
            else Toast("Choisissez d'abord un verset ou un chapitre.");
        };
        AgendaAjoutChant.Click += (_, _) =>
        {
            if (chantOuvert != null && R.AjouterChantAgenda(chantOuvert) is { } el) Toast($"« {el.Titre} » ajouté au déroulé « {R.Agenda.Nom} ».");
        };
        MontrerOnglet("agenda");
        MajEcran();
        MajProjection();
        MajTelephone();
        chargement = false;
    }

    /// <summary>Taille et position de la dernière fois (agrandie par défaut).</summary>
    void PositionDepart()
    {
        if (App.AutoTest != null) { Width = 1600; Height = 900; return; }
        var f = App.Donnees.Reglages.Fenetre;
        if (f != null && !f.Max && f.W > 400 && f.H > 300 && f.X > SystemParameters.VirtualScreenLeft - 50 && f.Y > SystemParameters.VirtualScreenTop - 50
            && f.X < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 && f.Y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = f.X; Top = f.Y; Width = f.W; Height = f.H;
        }
        else WindowState = WindowState.Maximized;
    }

    void Demarre()
    {
        (onglet is "agenda" or "annonces" or "duo" ? Cmd : Q).Focus();
        // l'index de recherche se prépare en arrière-plan : la première recherche est instantanée
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            try { VB.PreparerRecherche(); } catch (Exception e) { Journal.Erreur("index de recherche", e); }
        });
        if (App.AutoTest == null)
        {
            _ = Task.Run(() =>
            {
                bool ok;
                try { ok = Parefeu.Present(); }
                catch (Exception e) { Journal.Erreur("pare-feu", e); ok = false; }
                Dispatcher.BeginInvoke(new Action(() => { App.Parefeu_ = ok ? "ok" : "missing"; MajTelephone(); }));
            });
            _ = VerifierMaj(false, 8000);
        }
    }

    void Fermeture()
    {
        try
        {
            var r = App.Donnees.Reglages;
            r.Fenetre = WindowState == WindowState.Maximized
                ? new PositionFenetre { Max = true, X = RestoreBounds.X, Y = RestoreBounds.Y, W = RestoreBounds.Width, H = RestoreBounds.Height }
                : new PositionFenetre { Max = false, X = Left, Y = Top, W = Width, H = Height };
            App.Donnees.SauverReglages();
        }
        catch (Exception e) { Journal.Erreur("fermeture", e); }
        ecran?.Close();
    }

    /// <summary>Un 2e lancement de MyChurch ramène cette fenêtre.</summary>
    public void Ramener()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Natif.Devant(this);
    }

    /// <summary>Largeurs des colonnes selon la fenêtre (maquette « Pupitre » en 1440 px : 280 à gauche, 330 pour la colonne de contrôle).</summary>
    void Placement()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        ColonneGauche.Width = new GridLength(Math.Clamp(w * 0.195, 250, 330));
        ColonneDroite.Width = new GridLength(Math.Clamp(w * 0.23, 300, 380));
        Recherches.Width = Math.Clamp(w * 0.22, 230, 400);
        // petit écran : en-tête et barre du bas plus courts
        bool compact = w < 1360;
        VersionsLab.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SceneBtn.Visibility = SceneSep.Visibility = w < 1200 ? Visibility.Collapsed : Visibility.Visible;
        ProjectionCompact = compact;
        MajProjection();
        LargeurVignettes();
    }
    bool ProjectionCompact;

    // ------------------------------------------------------------ message court
    public void Toast(string t)
    {
        ToastTexte.Text = t;
        ToastBoite.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        minuterieToast.Stop();
        minuterieToast.Interval = TimeSpan.FromMilliseconds(Math.Max(3200, t.Length * 55));
        minuterieToast.Start();
    }

    // ------------------------------------------------------------ onglets
    void MontrerOnglet(bool bible) => MontrerOnglet(bible ? "bible" : "chants");

    void MontrerOnglet(string o)
    {
        onglet = o;
        R.OngletBible = o == "bible";
        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        // chaque page : sa colonne de gauche, son centre et le titre de l'en-tête
        PanneauAgenda.Visibility = CentreAgenda.Visibility = ElemTitre.Visibility = V(o == "agenda");
        PanneauBible.Visibility = CentreBible.Visibility = TeteBible.Visibility = V(o == "bible");
        PanneauChants.Visibility = DefilSections.Visibility = TeteChants.Visibility = V(o == "chants");
        PanneauAnnonces.Visibility = DefilGrille.Visibility = TeteDisplay.Visibility = V(o == "annonces");
        PanneauDuo.Visibility = CentreDuo.Visibility = TeteDuo.Visibility = V(o == "duo");
        // Réglages : toute la largeur, sans la colonne de contrôle
        PanneauReglages.Visibility = V(o == "reglages");
        EnTete.Visibility = ColonneControle.Visibility = V(o != "reglages");
        RechercheCulte.Visibility = V(o is "agenda" or "annonces" or "duo");
        RechercheBible.Visibility = V(o == "bible");
        RechercheChants.Visibility = V(o == "chants");
        if (o is not ("agenda" or "annonces" or "duo")) CmdPopup.IsOpen = false;
        (o switch { "agenda" => OngletAgenda, "bible" => OngletBible, "annonces" => OngletAnnonces, "duo" => OngletDuo, "reglages" => OngletReglages, _ => OngletChants }).IsChecked = true;
        // sur Culte, un clic sur une vignette affiche : pas de bouton « Afficher la sélection » ; sur Display, le mélangeur est complet
        Afficher.Visibility = V(o is not ("agenda" or "reglages"));
        AudioListeHote.Visibility = V(o != "annonces");
        MajEntete();
        if (o == "agenda") Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { LargeurVignettes(); DefilerVers(elementChoisi ?? R.ElementCourant); }));
    }

    /// <summary>Le surtitre du centre (« TOUTES LES DIAPOSITIVES DU CULTE », « BIBLE · LSG »…), le titre du dossier Duo et l'en-tête du déroulé.</summary>
    void MajEntete()
    {
        Surtitre.Libelle = onglet switch
        {
            "agenda" => "Toutes les diapositives du culte",
            "bible" => "Bible · " + VB.Etiquette(),
            "chants" => "Chants · " + ((Recueil.SelectedItem as ComboBoxItem)?.Content as string ?? "aucun recueil"),
            "annonces" => $"Display · {R.Annonces.Count} source{(R.Annonces.Count > 1 ? "s" : "")} · {R.Annonces.Count(a => a.Grille)} dans la grille",
            "duo" => $"Duo · {R.Duos.Count()} dossier{(R.Duos.Count() > 1 ? "s" : "")}",
            _ => "",
        };
        TeteDuo.Text = duoChoisi is { } d ? (d.Nom.Trim().Length > 0 ? d.Nom : "Dossier sans nom") : "Dossiers Duo";
        var (sur, titre) = DecouperNomCulte(R.Agenda.Nom);
        AgendaSurtitre.Libelle = sur;
        AgendaTitre.Text = titre;
        AgendaTitre.ToolTip = R.Agenda.Nom;
        AgendaInfo.Text = InfoDeroule();
    }

    /// <summary>Le bouton clair / sombre de la barre de gauche montre le thème où il mène.</summary>
    void MajThemeBtn()
    {
        bool s = Palette.Actuelle.Sombre;
        ThemeBtn.Content = s ? "Clair" : "Sombre";
        ThemeBtn.Tag = FindResource(s ? "IconeSoleil" : "IconeLune");
        ThemeBtn.ToolTip = s ? "Passer en mode clair (aussi dans Réglages › Apparence)" : "Passer en mode sombre (aussi dans Réglages › Apparence)";
    }

    // ------------------------------------------------------------ raccourcis clavier
    void Clavier(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OuvrirCommande();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && (Keyboard.FocusedElement == Cmd || CmdPopup.IsOpen) && Cmd.Text.Length > 0)
        {
            Cmd.Clear();
            CmdPopup.IsOpen = false;
            Cmd.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            R.Effacer();
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
            e.Handled = true;
            return;
        }
        var f = Keyboard.FocusedElement;
        bool champ = f is TextBox or PasswordBox or ComboBox or ComboBoxItem;
        if (champ) return;
        switch (e.Key)
        {
            case Key.Divide:
                (onglet is "agenda" or "annonces" or "duo" ? Cmd : ongletBible ? Q : Qc).Focus();
                e.Handled = true;
                break;
            case Key.B:
                BasculerNoir();
                e.Handled = true;
                break;
            case Key.P:
                BasculerDuo();
                e.Handled = true;
                break;
            case Key.Right or Key.Space or Key.PageDown:
                R.Bouger(1);
                e.Handled = true;
                break;
            case Key.Left or Key.PageUp:
                R.Bouger(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                AfficherCourant();
                e.Handled = true;
                break;
            default:
                int chiffre = e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
                if (chiffre < 0) break;
                if (onglet == "agenda" && elementChoisi != null) { R.AfficherElement(elementChoisi, chiffre); e.Handled = true; }
                else if (onglet == "annonces" && annonceChoisie != null) { R.AfficherAnnonce(annonceChoisie, chiffre); e.Handled = true; }
                else if (onglet == "chants" && chantOuvert != null) { R.AfficherSection(chantOuvert, chiffre); e.Handled = true; }
                break;
        }
    }

    void ConstruireAide()
    {
        void Ajout(string texte, params string[] touches)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
            foreach (var t in touches)
                p.Children.Add(new Border { Style = Style_("Touche"), Child = new TextBlock { Text = t, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume") } });
            p.Children.Add(new TextBlock { Text = texte, FontSize = 12, Foreground = Pinceau("Brume2"), VerticalAlignment = VerticalAlignment.Center });
            Aide.Children.Add(p);
        }
        Ajout("chercher", "Ctrl K");
        Ajout("afficher", "Entrée");
        Ajout("suivant", "→", "Espace");
        Ajout("précédent", "←");
        Ajout("retirer", "Échap");
        Ajout("écran noir", "B");
        Ajout("épingler (duo)", "P");
        Ajout("diapositive", "1–9");
    }

    // ------------------------------------------------------------ Bibles (colonne de gauche)
    void RemplirBibles()
    {
        BiblesChoix.Children.Clear();
        foreach (var v in VB.Toutes())
        {
            bool principale = v.Id == VB.IdPrincipale, deuxieme = v.Id == VB.IdSeconde;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var rond = new System.Windows.Shapes.Ellipse
            {
                Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center,
                Stroke = Pinceau(principale ? "Or" : "Brume2"), StrokeThickness = principale ? 5 : 2,
            };
            var nom = new TextBlock { Text = v.Nom, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            var court = new TextBlock { Text = v.Court, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau(principale ? "Or2" : "Brume2"), VerticalAlignment = VerticalAlignment.Center };
            var contenu = new DockPanel();
            DockPanel.SetDock(rond, Dock.Left);
            DockPanel.SetDock(court, Dock.Right);
            contenu.Children.Add(rond);
            contenu.Children.Add(court);
            contenu.Children.Add(nom);
            var b = new Button
            {
                Style = Style_("Btn"), Content = contenu, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(11, 9, 11, 9),
                Background = Pinceau(principale ? "OrLeger" : "Encre"), BorderBrush = Pinceau(principale ? "Or" : "Nuit3"),
                ToolTip = principale ? "Bible affichée à l'écran" : "Afficher cette Bible",
            };
            var id = v.Id;
            b.Click += (_, _) => ChoisirBible(id);
            var deux = new Button
            {
                Style = Style_("Btn"), Content = "2e", Padding = new Thickness(0), IsEnabled = !principale,
                Background = Pinceau(deuxieme ? "OrLeger" : "Encre"),
                BorderBrush = Pinceau(deuxieme ? "Or2" : "Nuit3"), Foreground = Pinceau(deuxieme ? "Or2" : "Brume"),
                ToolTip = deuxieme ? "Ne plus l'afficher sous le verset" : "L'afficher aussi sous le verset (versets bilingues)",
            };
            deux.Click += (_, _) => BasculerSeconde(id);
            Grid.SetColumn(deux, 2);
            g.Children.Add(b);
            g.Children.Add(deux);
            BiblesChoix.Children.Add(g);
        }
        VersionsLab.Text = "Version : " + VB.Etiquette() + (VB.IdSeconde.Length > 0 ? " (bilingue)" : "");
    }

    void EtatBibles(string t) => BiblesEtat.Text = t;

    void ChoisirBible(string id)
    {
        if (id == VB.IdPrincipale) return;
        EtatBibles("Chargement de la Bible…");
        Cursor = Cursors.Wait;
        try { VB.Choisir(id); }
        finally { Cursor = null; }
        EtatBibles($"« {VB.InfoDe(VB.IdPrincipale)?.Nom ?? VB.IdPrincipale} » est affichée.");
    }

    void BasculerSeconde(string id)
    {
        Cursor = Cursors.Wait;
        try { VB.BasculerSeconde(id); }
        finally { Cursor = null; }
        EtatBibles(VB.IdSeconde.Length > 0 ? (VB.Seconde != null ? "Versets bilingues : " + VB.Etiquette() : "Cette version n'a pas pu être chargée.") : "Une seule version à l'écran.");
    }

    /// <summary>Importer une Bible : fichier, nom court, puis l'afficher tout de suite si on le souhaite.</summary>
    public async void ImporterBible(Action<string>? etat = null)
    {
        if (etat == null) etat = EtatBibles;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer une Bible",
            Filter = "Bibles (XML, JSON, texte)|*.xml;*.json;*.txt;*.usfx;*.osis;*.zef|Tous les fichiers|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        var nomF = Path.GetFileName(dlg.FileName);
        etat($"Lecture de « {nomF} »…");
        Bible b;
        try { b = await Task.Run(() => Bibles.Lire(dlg.FileName)); }
        catch (Exception x)
        {
            etat("Import impossible : " + x.Message);
            Journal.Erreur($"import de la Bible « {nomF} »", x);
            return;
        }
        var court = Dialogue.Demander(this, "Nom court affiché après la référence (ex. KIR, SW, DARBY) :", b.Court, "Importer une Bible");
        if (court == null) { etat("Import annulé."); return; }
        var info = VB.Ajouter(b, court);
        etat($"« {info.Nom} » importée ({info.Versets} versets).");
        if (Dialogue.Confirmer(this, $"« {info.Nom} » est importée.\n\nL'afficher maintenant à l'écran ?", "L'afficher", "La garder dans la liste"))
            ChoisirBible(info.Id);
        else etat($"« {info.Nom} » importée : cliquez-la dans la liste pour l'afficher, ou « 2e » pour les versets bilingues.");
    }

    // ------------------------------------------------------------ livres et chapitres
    void ConstruireLivres()
    {
        for (int i = 0; i < Livres.Nombre; i++)
        {
            int b = i;
            var x = new Button { Style = Style_("BtnLivre"), Content = Livres.Courts[i], ToolTip = Livres.Nom(i) };
            x.Click += (_, _) => OuvrirChapitre(b, 1);
            (i < 39 ? LivresAT : LivresNT).Children.Add(x);
            boutonsLivres.Add(x);
        }
    }

    void OuvrirChapitre(int b, int c, int v1 = 0, int v2 = 0, bool defiler = true)
    {
        int n = B.NombreChapitres(b);
        if (n == 0) { TitreChap.Text = Livres.Nom(b) + " : absent de cette Bible"; Versets.Children.Clear(); return; }
        c = Math.Clamp(c, 1, n);
        vue = (b, c);
        sel = v1 > 0 ? new Ref(b, c, v1, v2 > 0 ? v2 : v1) : null;
        R.Selection = sel;
        for (int i = 0; i < boutonsLivres.Count; i++) boutonsLivres[i].Style = Style_(i == b ? "BtnLivreOn" : "BtnLivre");
        TitreChap.Text = $"{Livres.Nom(b)} {c}";
        TitreChap.ToolTip = null;
        ChapPrec.Visibility = ChapSuiv.Visibility = Visibility.Visible;
        ChapPrec.IsEnabled = c > 1;
        ChapSuiv.IsEnabled = c < n;
        Versets.Children.Clear();
        var vs = B.Versets(b, c);
        for (int i = 0; i < vs.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(vs[i])) continue;
            Versets.Children.Add(LigneVerset(b, c, i + 1, null, vs[i], null, null));
        }
        Marquer();
        if (defiler)
        {
            var premier = sel is Ref s ? Versets.Children.OfType<Border>().FirstOrDefault(r => r.Tag is ValueTuple<int, int, int> t && t.Item3 == s.V1) : null;
            if (premier != null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Centrer(premier)));
            else DefilVersets.ScrollToTop();
        }
    }

    void Centrer(FrameworkElement e)
    {
        try
        {
            var p = e.TransformToAncestor(Versets).Transform(new Point(0, 0));
            DefilVersets.ScrollToVerticalOffset(Math.Max(0, p.Y - DefilVersets.ViewportHeight / 2 + e.ActualHeight / 2));
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Une ligne de verset : numéro, texte (mots trouvés surlignés), référence pour les résultats de recherche.</summary>
    Border LigneVerset(int b, int c, int v, string? refTexte, string texte, HashSet<string>? termes, string? version)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        // « + » devant le verset : l'ajouter à l'agenda (tout le passage s'il fait partie des versets choisis)
        var plus = new Button { Style = Style_("BtnAjout"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        plus.Click += (_, ev) => { ev.Handled = true; AjouterVersetAgenda(b, c, v); };
        g.Children.Add(plus);
        var num = new TextBlock { Text = v.ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume2"), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(num, 1);
        g.Children.Add(num);
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)FindResource("Ui"), FontSize = 16.5, LineHeight = 25, Foreground = Pinceau("Blanc") };
        if (refTexte != null)
        {
            var r = new Span(new Run(refTexte)) { FontFamily = (FontFamily)FindResource("Mono"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or") };
            t.Inlines.Add(r);
            if (!string.IsNullOrEmpty(version))
                t.Inlines.Add(new InlineUIContainer(new Border
                {
                    Background = Pinceau("OrVif"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock { Text = version, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Pinceau("SurOr") },
                }) { BaselineAlignment = BaselineAlignment.Center });
            t.Inlines.Add(new LineBreak());
        }
        if (termes != null)
            foreach (var (m, mot) in Recherche.Morceaux(texte, termes))
                t.Inlines.Add(mot ? new Run(m) { Background = Surligne } : new Run(m));
        else if (refTexte != null) t.Inlines.Add(new Run(texte));
        else t.Text = texte;
        Grid.SetColumn(t, 3);
        g.Children.Add(t);
        var row = new Border { Style = (Style)FindResource("LigneVerset"), Child = g, Tag = (b, c, v) };
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                sel = new Ref(b, c, v, v);
                R.Selection = sel;
                AfficherVerset(sel.Value);
                return;
            }
            if (refTexte != null) { OuvrirChapitre(b, c, v, v); return; } // résultat de recherche : ouvre le chapitre
            // Maj + clic : plusieurs versets (du premier choisi jusqu'à celui-ci)
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && sel is Ref s && s.B == b && s.C == c)
                sel = new Ref(b, c, Math.Min(s.V1, v), Math.Max(s.V1, v));
            else sel = new Ref(b, c, v, v);
            R.Selection = sel;
            Marquer();
        };
        return row;
    }

    static Brush Surligne => Pinceau("Surligne");

    /// <summary>Versets choisis (gris) et à l'écran (or).</summary>
    void Marquer()
    {
        var cible = R.Cible as CibleVerset;
        foreach (var r in Versets.Children.OfType<Border>())
        {
            if (r.Tag is not ValueTuple<int, int, int> tag) continue;
            var (b, c, v) = tag;
            bool direct = cible != null && cible.B == b && cible.C == c && v >= cible.V1 && v <= cible.V2;
            bool choisi = sel is Ref s && s.B == b && s.C == c && v >= s.V1 && v <= Math.Max(s.V1, s.V2) && vue is { } vv && vv.B == b && vv.C == c;
            if (direct) { r.Background = Pinceau("OrLeger"); r.BorderBrush = Pinceau("OrVif"); }
            else if (choisi) { r.Background = Pinceau("Nuit2"); r.BorderBrush = Pinceau("Trait2"); }
            else { r.ClearValue(Border.BackgroundProperty); r.ClearValue(Border.BorderBrushProperty); }
        }
        foreach (var s in Sections.Children.OfType<Border>())
        {
            bool direct = R.Cible is CibleChant cc && cc.Id == chantOuvert && s.Tag is int i && cc.I == i;
            if (direct) { s.Background = Pinceau("OrLeger"); s.BorderBrush = Pinceau("OrVif"); }
            else { s.ClearValue(Border.BackgroundProperty); s.ClearValue(Border.BorderBrushProperty); }
        }
    }

    void AfficherVerset(Ref r)
    {
        R.AfficherVerset(r);
    }

    // ------------------------------------------------------------ recherche : référence (« jn 3 16 ») ou mots
    async void Chercher()
    {
        var q = Q.Text.Trim();
        if (q.Length == 0) return;
        var lu = Livres.Lire(q);
        if (lu is Ref r0)
        {
            var r = B.Ajuster(r0);
            if (q.Any(char.IsDigit)) { OuvrirChapitre(r.B, r.C, r.V1, r.V2); return; }
            if (q.Length <= 4) { OuvrirChapitre(r.B, r.C); return; }
        }
        if (q.Length < 3) return;
        // mots (même mal écrits) cherchés dans toutes les Bibles installées
        int n = ++recherches;
        var versions = VB.Versions();
        List<Recherche.Resultat> res;
        HashSet<string> termes = new();
        try
        {
            res = await Task.Run(() =>
            {
                var x = Recherche.Chercher(versions, q, 120, out var t);
                termes = t;
                return x;
            });
        }
        catch (Exception e)
        {
            Journal.Erreur($"recherche « {q} »", e);
            res = new();
        }
        if (n != recherches || Q.Text.Trim() != q) return; // une recherche plus récente est en cours
        vue = null;
        sel = null;
        R.Selection = null;
        ChapPrec.Visibility = ChapSuiv.Visibility = Visibility.Collapsed;
        TitreChap.Text = res.Count > 0 ? $"{res.Count}{(res.Count >= 120 ? "+" : "")} résultat{(res.Count > 1 ? "s" : "")} pour « {q} »" : "";
        TitreChap.ToolTip = TitreChap.Text;
        Versets.Children.Clear();
        DefilVersets.ScrollToTop();
        if (res.Count == 0)
        {
            if (lu is Ref rr) { var a = B.Ajuster(rr); OuvrirChapitre(a.B, a.C); return; }
            Versets.Children.Add(new TextBlock { Text = "Aucun verset trouvé. Essayez une référence comme « jn 3 16 » ou d'autres mots.", Style = Style_("Muet"), FontSize = 15, TextAlignment = TextAlignment.Center, Margin = new Thickness(30) });
            return;
        }
        foreach (var x in res)
            Versets.Children.Add(LigneVerset(x.B, x.C, x.V, Livres.Libelle(x.B, x.C, x.V), x.Texte, termes, x.Principale ? null : x.Version));
        Marquer();
    }

    // ------------------------------------------------------------ chants
    void RemplirRecueils()
    {
        var actuel = App.Donnees.Reglages.Recueil;
        bool avant = chargement;
        chargement = true;
        Recueil.Items.Clear();
        foreach (var (nom, n) in R.Recueils()) Recueil.Items.Add(new ComboBoxItem { Content = $"{(nom.Length > 0 ? nom : "Sans recueil")} ({n})", Tag = nom });
        Recueil.SelectedItem = Recueil.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == actuel) ?? (Recueil.Items.Count > 0 ? Recueil.Items[0] : null);
        SupprimerRecueil.IsEnabled = Recueil.Items.Count > 0;
        chargement = avant;
    }

    void ListeChantsMaj()
    {
        RemplirRecueils();
        var rec = (Recueil.SelectedItem as ComboBoxItem)?.Tag as string;
        var q = Qc.Text.Trim();
        var num = NumChant.Text.Trim();
        // une recherche cherche dans tous les recueils ; un numéro, dans le recueil choisi
        var liste = q.Length > 0 ? R.Filtrer(q, null)
                  : num.Length > 0 ? R.ParNumero(num, rec ?? "\u0001")
                  : R.Filtrer("", rec ?? "\u0001");
        ListeChants.ItemsSource = liste;
        if (chantOuvert != null) ListeChants.SelectedItem = liste.FirstOrDefault(c => c.Id == chantOuvert);
        ListeVide.Text = q.Length > 0 ? "Aucun chant trouvé." : num.Length > 0 ? $"Aucun chant n° {num} dans ce recueil." : "";
        ListeVide.Visibility = liste.Count == 0 && ListeVide.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Montre le chant du numéro tapé et l'ouvre ; avec Entrée, rend la main aux raccourcis 1-9 des strophes.</summary>
    void AllerAuNumero(bool entree)
    {
        minuterieNum.Stop();
        ListeChantsMaj();
        if (NumChant.Text.Length == 0 || ListeChants.Items.Count == 0 || ListeChants.Items[0] is not Chant c) return;
        OuvrirChant(c.Id);
        if (entree) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); }
    }

    public void OuvrirChant(string? id)
    {
        chantOuvert = id;
        var c = R.Trouver(id);
        ChantTitre.Text = c?.Title ?? "Aucun chant choisi";
        Modifier.IsEnabled = Supprimer.IsEnabled = AgendaAjoutChant.IsEnabled = c != null;
        Sections.Children.Clear();
        if (c != null)
            for (int i = 0; i < c.Sections.Count; i++)
            {
                var s = c.Sections[i];
                int k = i;
                var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (i < 9)
                {
                    var kbd = new Border { Style = Style_("Touche"), Margin = new Thickness(6, 0, 0, 0), Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume") } };
                    DockPanel.SetDock(kbd, Dock.Right);
                    tete.Children.Add(kbd);
                }
                // petit bouton « Modifier » : ouvre l'éditeur directement sur cette strophe
                var modif = new Button { Style = Style_("BtnPetit"), Content = "Modifier", FontSize = 11, Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(6, -2, 0, -2), ToolTip = "Modifier ce chant (curseur sur cette strophe)" };
                modif.Click += (_, _) => Editer(R.Trouver(chantOuvert), k);
                DockPanel.SetDock(modif, Dock.Right);
                tete.Children.Add(modif);
                tete.Children.Add(new TextBlock { Text = s.Label.Length > 0 ? s.Label : "Strophe", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or"), TextTrimming = TextTrimming.CharacterEllipsis });
                var corps = new StackPanel();
                corps.Children.Add(tete);
                corps.Children.Add(new TextBlock { Text = s.Text, FontSize = 14, LineHeight = 19.6, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc") });
                var carte = new Border { Style = (Style)FindResource("CarteSection"), Child = corps, Tag = i, ToolTip = "Clic : afficher · double-clic : modifier" };
                carte.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2) Editer(R.Trouver(chantOuvert), k);
                    else if (chantOuvert != null) R.AfficherSection(chantOuvert, k);
                };
                Sections.Children.Add(carte);
            }
        LargeurSections();
        if (ListeChants.SelectedItem is not Chant sc || sc.Id != id)
            ListeChants.SelectedItem = (ListeChants.ItemsSource as List<Chant>)?.FirstOrDefault(x => x.Id == id);
        DefilSections.ScrollToTop();
        Marquer();
    }

    /// <summary>Cartes des strophes : autant de colonnes de 240 px au moins que la largeur le permet.</summary>
    void LargeurSections()
    {
        // la zone moins ses marges (24 + 12) et la place de la barre de défilement
        double w = DefilSections.ActualWidth - 36 - 12;
        if (w <= 0) return;
        int cols = Math.Max(1, (int)((w + 10) / 250));
        double l = Math.Floor((w + 10) / cols) - 10;
        foreach (var c in Sections.Children.OfType<FrameworkElement>()) c.Width = l;
    }

    public void Editer(Chant? c, int? focus = null)
    {
        var e = new EditeurWindow(c, focus) { Owner = this };
        if (e.ShowDialog() == true && e.IdEnregistre != null)
        {
            ListeChantsMaj();
            OuvrirChant(e.IdEnregistre);
        }
    }

    // ------------------------------------------------------------ afficher, découper
    void AfficherCourant()
    {
        if (onglet == "agenda") { AfficherAgendaCourant(); return; }
        if (onglet == "duo") { AfficherDuoChoisi(); return; }
        if (onglet == "annonces")
        {
            if (annonceChoisie == null && AnnonceTexte.Text.Trim().Length == 0) Toast("Choisissez d'abord un display.");
            else if (EnregistrerAnnonce() is { } a) R.AfficherAnnonce(a);
            return;
        }
        if (ongletBible)
        {
            if (sel is Ref s && s.V1 > 0) AfficherVerset(s);
            else Toast("Choisissez d'abord un verset.");
            return;
        }
        if (chantOuvert == null) { Toast("Choisissez d'abord un chant."); return; }
        int i = R.Cible is CibleChant cc && cc.Id == chantOuvert ? cc.I : 0;
        R.AfficherSection(chantOuvert, i);
    }

    void OuvrirDecoupe(Cible c)
    {
        var d = new DecoupeWindow(c) { Owner = this };
        if (d.ShowDialog() == true && d.Message.Length > 0) Toast(d.Message);
        // un verset ou une strophe découpé : ses vignettes suivent
        VignettesMaj();
    }

    // ------------------------------------------------------------ ce qui est à l'écran
    void MajEcran()
    {
        // écran noir : le texte est caché sans être perdu ; une nouvelle diapositive le rallume
        if (noir && !ReferenceEquals(R.Etat, etatNoir)) noir = false;
        var e = EtatProjete();
        App.Sources.Choisir(e);
        ChoisirSourceDuo(e);
        ApercuRendu.Afficher(e);
        App.CameraVirtuelle?.Afficher(e);
        ecran?.Rendu.Afficher(e);
        ApercuRendu.ImageDisplay(App.Sources.Image);
        ecran?.Rendu.ImageDisplay(App.Sources.Image);
        ImagesDuo();
        App.SortieObs.Ecrire(e);
        bool rien = e.Vide && e.Duo == null;
        Apercu.BorderBrush = Pinceau(rien ? "Nuit3" : "Direct");
        Apercu.Background = rien || e.Theme == "transparent" ? Pinceau("Damier") : Brushes.Black;
        // écran noir : le bouton « Noir » s'inverse
        if (noir) { NoirBtn.Background = Pinceau("Blanc"); NoirBtn.Foreground = Pinceau("Page"); NoirBtn.BorderBrush = Pinceau("Blanc"); }
        else { NoirBtn.ClearValue(BackgroundProperty); NoirBtn.ClearValue(ForegroundProperty); NoirBtn.ClearValue(BorderBrushProperty); }
        MajDuo();
        MajPastille();
        MajSuivant();
        var s = R.Prochaine();
        Decouper.IsEnabled = s != null && s.Cible is CibleChant or CibleVerset;
        // le verset à l'écran est dans un autre chapitre (Suivant, téléphone) : la Bible le suit
        // (pas pendant qu'une liste de résultats de recherche est affichée)
        if (R.Cible is CibleVerset cv && ongletBible && vue is { } v && (v.B != cv.B || v.C != cv.C))
            OuvrirChapitre(cv.B, cv.C, cv.V1, cv.V2);
        else
        {
            if (vue != null && R.Selection != sel) sel = R.Selection;
            Marquer();
        }
        MajAgendaEcran();
        MajFonds();
    }

    /// <summary>Pastille de l'aperçu : la diapositive part-elle vers la fenêtre de projection ?</summary>
    void MajPastille()
    {
        var e = R.Etat;
        bool ouvert = R.EcranOuvert();
        PastilleTexte.Text = noir ? "Écran noir" : ouvert ? (e.Vide && e.Duo == null ? "Écran ouvert · vide" : "À l'écran") : "Aperçu seulement";
        // l'état s'affiche à droite d'« EN DIRECT » (« Aperçu seulement » tant qu'aucun écran n'est ouvert)
        var m = App.Donnees.Reglages.Ecran;
        EcranInfo.Text = PastilleTexte.Text;
        EcranInfo.Foreground = Pinceau(ouvert && !noir ? "Vert" : "Brume");
        EcranInfo.ToolTip = ecran != null && m != null ? $"{m.Nom} · {m.W} × {m.H}" : "Aucun écran de projection : Projection, en bas à droite";
        MajProjection();
    }

    // ------------------------------------------------------------ écran de projection
    /// <summary>Ouvre la projection sur un écran (plein écran) ou en fenêtre.</summary>
    public void OuvrirSur(Moniteur m, bool plein)
    {
        ecran?.Close();
        var w = new EcranWindow(m, plein);
        w.Commande += k =>
        {
            if (k == "suiv") R.Bouger(1);
            else if (k == "prec") R.Bouger(-1);
            else if (k == "retirer") R.Effacer();
        };
        w.Closed += (_, _) =>
        {
            if (ecran == w) ecran = null;
            MajEcran();
            App.Telephone.Actualiser();
        };
        ecran = w;
        App.Donnees.Reglages.Ecran = new EcranChoisi { Mode = plein ? "plein" : "fenetre", X = m.X, Y = m.Y, W = m.W, H = m.H, Nom = m.Nom, Principal = m.Principal };
        App.Donnees.SauverReglages();
        w.Show();
        w.Rendu.Afficher(EtatProjete());
        Toast(plein ? $"Projection plein écran sur {m.Nom}." : "Projection en fenêtre.");
        MajEcran();
        App.Telephone.Actualiser();
        Activate();
    }

    /// <summary>Fenêtre de projection ouverte (pour le test automatique).</summary>
    public EcranWindow? Ecran => ecran;

    // ------------------------------------------------------------ téléphone et mises à jour
    public void MajTelephone()
    {
        int n = App.Telephone.Connectes().Count;
        bool parefeu = App.Parefeu_ is "missing" or "refused" or "error";
        TelPoint.Fill = Pinceau(n > 0 ? "Vert" : parefeu ? "Rouge" : "Brume2");
        TelPoint.Visibility = n > 0 || parefeu ? Visibility.Visible : Visibility.Collapsed;
        TelTexte.Text = n > 0 ? $"{n} téléphone{(n > 1 ? "s" : "")}" : "Téléphone";
        TelBtn.ToolTip = n > 0 ? $"{n} téléphone(s) connecté(s)" : App.Parefeu_ is "missing" or "refused" or "error" ? "Pare-feu Windows : autorisation nécessaire pour le téléphone" : "Aucun téléphone connecté";
    }

    public MiseAJour.Resultat? MiseAJourDisponible => maj;

    public async Task<MiseAJour.Resultat> VerifierMaj(bool manuel, int delai = 0)
    {
        if (delai > 0) await Task.Delay(delai);
        var r = await MiseAJour.Verifier(App.Version);
        if (r.Disponible)
        {
            maj = r;
            MajBtn.Visibility = Visibility.Visible;
            MajBtn.Content = $"Mettre à jour ({r.Version})";
        }
        else if (manuel && r.Erreur != null) Journal.Info("mise à jour : " + r.Erreur);
        return r;
    }

    /// <summary>La page Réglages (apparence, audio, MyVirtualSound, enregistrements, mise à jour, transfert, journal, licences).</summary>
    public ReglagesPage Reglages { get; }
    /// <summary>Réglages › Audio (périphériques).</summary>
    public void OuvrirParametres() { MontrerOnglet("reglages"); Reglages.Aller("audio", false); }
    /// <summary>Le mélangeur audio de Display.</summary>
    public Audio.AudioPanel? PanneauAudio { get; private set; }
    /// <summary>« + Micros » de la colonne de contrôle : cocher les entrées de la carte son (une bande par entrée).</summary>
    public void AjouterMicros() => PanneauAudio?.AjouterMicros();
    /// <summary>Montre le mélangeur : onglet Display, panneau Audio déplié.</summary>
    public void MontrerAudio()
    {
        MontrerOnglet("annonces");
        PanneauAudio?.Deplier();
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestRecherche(string q) { MontrerOnglet(true); Q.Text = q; minuterieQ.Stop(); Chercher(); }
    public void TestAfficherSelection() => AfficherCourant();
    public void TestOnglet(bool bible) => MontrerOnglet(bible);
    /// <summary>Test : affiche une page (« agenda », « bible », « chants », « annonces », « duo », « reglages »).</summary>
    public void TestPage(string o) => MontrerOnglet(o);
    public string TestTheme => ThemeBtn.Content as string ?? "";
    public void TestBasculerTheme() => ThemeBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public string TestDeroule => AgendaSurtitre.Libelle + " / " + AgendaTitre.Text + " / " + AgendaInfo.Text;
    /// <summary>Test : le « + » (ou ✓) d'une source de la bibliothèque Display.</summary>
    public void TestPlusGrille(Annonce a) => GrilleAnnonceListe_Click(new Button { DataContext = a }, new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public Chant? TestPremierChant() => ListeChants.Items.Count > 0 ? ListeChants.Items[0] as Chant : null;
    public List<Chant> TestNumero(string n) { NumChant.Text = n; AllerAuNumero(false); return ListeChants.Items.Cast<Chant>().ToList(); }
}
