using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Le panneau Audio de Display, sous la grille (MyChurch 3.0.1). Tout le son passe par le moteur d'OBS (libobs).
/// À gauche le mélangeur vertical, comme dans OBS : une bande par source (échelle de 0 à −60 dB, vumètres gauche et droite,
/// fader, sourdine, écoute, filtres et pastilles des destinations). À droite les enregistrements (5 au plus, qui peuvent
/// tourner en même temps) et les sorties MyVirtualSound (4 au plus), chacun avec les sources choisies par l'utilisateur.
/// Le mélangeur démarre vide : rien n'est préréglé.
/// </summary>
public sealed class AudioPanel : Border
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    const string Vide = "Le mélangeur est vide : « + Source audio » pour ajouter un micro, le son d'une sortie ou un média.";
    const string Legende = "Pastilles : 1 à 5 = enregistrements, A à D = sorties MyVirtualSound (allumée : la source y va) · clic droit sur une bande : renommer, changer, mono, ordre, supprimer";
    const double LargeurBande = 112, HauteurVu = 176;

    /// <summary>Une bande verticale du mélangeur.</summary>
    sealed class Bande
    {
        public AudioSource S = null!;
        public Border Cadre = null!;
        public TextBlock Nom = null!, Genre = null!, Db = null!;
        public Vumetre G = null!, D = null!;
        public Slider Volume = null!;
        public Button Muet = null!, Casque = null!, Filtres = null!;
        public Shapes.Path CasqueIcone = null!;
        public readonly List<(Button Bouton, IDestinationAudio Destination, string Marque)> Pastilles = new();
    }

    /// <summary>La carte d'un enregistrement ou d'une sortie MyVirtualSound.</summary>
    sealed class Carte
    {
        public string Id = "";
        public Border Cadre = null!;
        public TextBlock Nom = null!, Etat = null!;
        public Vumetre Vu = null!;
        public Button? Bouton;
        public TextBlock? Symbole;
        public CheckBox? Actif;
        public Slider? Volume;
        public TextBlock? Db;
        public Button? Muet;
    }

    readonly DispatcherTimer minuterie = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    readonly TextBlock moteur = new() { FontSize = 13 };
    readonly Border puce;
    readonly Button ajouter, peripheriques, reduire, reparer;
    readonly Shapes.Path chevron;
    readonly Border alerte;
    readonly TextBlock alerteTexte = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid corps = new() { Margin = new Thickness(0, 16, 0, 0) };
    readonly StackPanel bandes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock vide;
    readonly StackPanel droite = new();
    readonly ScrollViewer defilDroite;
    readonly StackPanel listeEnregistrements = new(), listeSorties = new();
    readonly Button creerEnregistrement, creerSortie;
    readonly TextBlock compteEnregistrements = new(), compteSorties = new(), aideEnregistrements, aideSorties;
    readonly ComboBox ecoute = new() { MinHeight = 40 };
    readonly List<Bande> lignes = new();
    readonly List<Carte> enregistrements = new(), sorties = new();
    readonly Dictionary<string, string> messages = new();
    readonly DockPanel tete = new();
    readonly WrapPanel actions = new() { VerticalAlignment = VerticalAlignment.Center };
    bool? etroit;
    bool remplissage, demarre;
    int tic;

    Window? Fenetre => Window.GetWindow(this);
    void Toast(string texte) => (Fenetre as MainWindow ?? Application.Current.MainWindow as MainWindow)?.Toast(texte);

    public AudioPanel()
    {
        Style = S("Carte");
        Padding = new Thickness(22, 18, 22, 20);

        // ---------------- en-tête : Audio · Moteur OBS · + Source audio · Périphériques
        moteur.Foreground = P("Brume");
        puce = new Border { Style = S("Puce"), Margin = new Thickness(14, 2, 0, 0), Child = moteur };
        var titre = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titre.Children.Add(new TextBlock { Text = "Audio", Style = S("TitreCarte"), VerticalAlignment = VerticalAlignment.Center });
        titre.Children.Add(puce);
        ajouter = new Button { Style = S("BtnOr"), Content = "+ Source audio", Height = 42, Padding = new Thickness(18, 0, 18, 0), ToolTip = "Ajouter un micro, le son d'une sortie, un fichier ou le son de la vidéo projetée" };
        ajouter.Click += (_, _) => MenuAjouter();
        peripheriques = new Button { Style = S("Btn"), Content = "Périphériques", Height = 42, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Paramètres : micro et sortie par défaut, écoute au casque, VB-CABLE, ffmpeg" };
        peripheriques.Click += (_, _) => { (Fenetre as MainWindow)?.OuvrirParametres(); Rafraichir(); };
        chevron = new Shapes.Path
        {
            Data = Geometry.Parse("M1,1 L6,6 L11,1"), Stroke = P("Brume"), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 12, Height = 8, Stretch = Stretch.None, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        reduire = new Button { Style = S("BtnPlat"), Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = chevron };
        reduire.Click += (_, _) => { C.PanelVisible = !C.PanelVisible; MajReduction(); A.Apply(); };
        actions.Children.Add(ajouter);
        actions.Children.Add(peripheriques);
        actions.Children.Add(reduire);
        DockPanel.SetDock(actions, Dock.Right);
        tete.Children.Add(actions);
        tete.Children.Add(titre);

        // ---------------- alerte : moteur absent (Réparer) ou plus de piste d'OBS libre
        reparer = new Button { Style = S("BtnOr"), Content = "Réparer le moteur audio", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Télécharge la dernière version de MyChurch sur GitHub et la réinstalle (chants, agendas et réglages gardés)" };
        reparer.Click += (_, _) => Reparer();
        var alerteLigne = new DockPanel();
        DockPanel.SetDock(reparer, Dock.Right);
        alerteLigne.Children.Add(reparer);
        alerteTexte.Foreground = P("RougeTexte");
        alerteLigne.Children.Add(alerteTexte);
        alerte = new Border { Background = P("RougeLeger"), BorderBrush = P("RougeBord"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0), Child = alerteLigne, Visibility = Visibility.Collapsed };

        // ---------------- à gauche : le mélangeur vertical
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        corps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        corps.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var gauche = new DockPanel();
        var titreMelangeur = Etiquette("MÉLANGEUR", 0);
        DockPanel.SetDock(titreMelangeur, Dock.Top);
        gauche.Children.Add(titreMelangeur);
        var legende = new TextBlock { Text = Legende, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(legende, Dock.Bottom);
        gauche.Children.Add(legende);
        vide = new TextBlock { Text = Vide, FontSize = 14, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 12), LineHeight = 22 };
        var zone = new Grid();
        zone.Children.Add(new ScrollViewer { Content = bandes, VerticalAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 6) });
        zone.Children.Add(vide);
        gauche.Children.Add(zone);
        corps.Children.Add(gauche);

        // ---------------- à droite : enregistrements, MyVirtualSound, écoute au casque
        droite.Children.Add(TitreSection("ENREGISTREMENTS", compteEnregistrements, 0));
        aideEnregistrements = Aide("Un enregistrement garde les sources que vous choisissez ; avec des sources Display, c'est une vidéo. Plusieurs peuvent tourner en même temps.");
        droite.Children.Add(aideEnregistrements);
        droite.Children.Add(listeEnregistrements);
        creerEnregistrement = new Button { Style = S("BtnContour"), Content = "+ Créer un enregistrement", Height = 38, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Choisir ses sources audio et, pour une vidéo, ses sources Display" };
        creerEnregistrement.Click += (_, _) => Montrer(new EnregistrementWindow(null));
        droite.Children.Add(creerEnregistrement);

        droite.Children.Add(TitreSection("MYVIRTUALSOUND", compteSorties, 20));
        aideSorties = Aide("Une sortie envoie le mélange de ses sources vers un câble audio virtuel : Zoom ou un navigateur le reçoivent comme un micro. Plusieurs peuvent être actives en même temps.");
        droite.Children.Add(aideSorties);
        droite.Children.Add(listeSorties);
        creerSortie = new Button { Style = S("BtnContour"), Content = "+ Créer une sortie", Height = 38, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Choisir ses sources et le câble virtuel (VB-CABLE) ou la sortie" };
        creerSortie.Click += (_, _) => Montrer(new SortieWindow(null));
        droite.Children.Add(creerSortie);

        droite.Children.Add(Etiquette("ÉCOUTE AU CASQUE", 20));
        ecoute.ToolTip = "Où OBS fait entendre les sources dont le casque est allumé (casque de la régie)";
        ecoute.SelectionChanged += (_, _) =>
        {
            if (remplissage || ecoute.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            C.MonitorDeviceId = id;
            C.MonitorDeviceName = id == "default" ? "" : item.Content as string ?? "";
            A.Apply();
        };
        droite.Children.Add(ecoute);
        defilDroite = new ScrollViewer { Content = droite, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(defilDroite, 2);
        corps.Children.Add(defilDroite);

        var racine = new DockPanel();
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        DockPanel.SetDock(alerte, Dock.Top);
        racine.Children.Add(alerte);
        racine.Children.Add(corps);
        Child = racine;

        // ---------------- vie du panneau
        A.Changed += () => Dispatcher.BeginInvoke(new Action(SurChangement));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(() => { MajCartes(); Annoncer(); }));
        minuterie.Tick += (_, _) => Battre();
        Loaded += (_, _) => minuterie.Start();
        SizeChanged += (_, e) => Disposer(e.NewSize.Width);
        Unloaded += (_, _) => minuterie.Stop();
        Rafraichir();
        MajReduction();
        SurChangement();
    }

    /// <summary>Montre le mélangeur (bouton ou raccourci) : déplie le panneau et le fait défiler à l'écran.</summary>
    public void Deplier()
    {
        if (!C.PanelVisible) { C.PanelVisible = true; MajReduction(); A.Apply(); }
        BringIntoView();
    }

    // ------------------------------------------------------------ construction
    static TextBlock Etiquette(string texte, double haut) => new() { Text = texte, Style = S("CarteTitre"), Margin = new Thickness(0, haut, 0, 10) };

    static DockPanel TitreSection(string texte, TextBlock compte, double haut)
    {
        var d = new DockPanel { Margin = new Thickness(0, haut, 0, 10) };
        compte.FontSize = 12.5;
        compte.Foreground = P("Brume2");
        compte.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(compte, Dock.Right);
        d.Children.Add(compte);
        d.Children.Add(new TextBlock { Text = texte, Style = S("CarteTitre"), Margin = new Thickness(0) });
        return d;
    }

    static TextBlock Aide(string texte) => new() { Text = texte, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), LineHeight = 18 };

    static Button BoutonM(double largeur, double hauteur) => new()
    {
        Style = S("Btn"), Width = largeur, Height = hauteur, Padding = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 14, Content = "M",
        ToolTip = "Sourdine (M) : couper ou rétablir le son",
    };

    static Border Badge(string marque) => new()
    {
        Background = P("Nuit2"), BorderBrush = P("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 0),
        Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = marque, FontSize = 12, FontWeight = FontWeights.SemiBold },
    };

    internal static string MarqueSortie(int i) => ((char)('A' + i)).ToString();

    /// <summary>Reconstruit tout le panneau à partir des réglages (après un ajout, un retrait, un changement d'ordre…).</summary>
    public void Rafraichir()
    {
        remplissage = true;
        try
        {
            bandes.Children.Clear();
            lignes.Clear();
            foreach (var s in C.Sources)
            {
                var b = CreerBande(s);
                lignes.Add(b);
                bandes.Children.Add(b.Cadre);
            }
            vide.Visibility = C.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            listeEnregistrements.Children.Clear();
            enregistrements.Clear();
            for (int i = 0; i < C.Profiles.Count; i++)
            {
                var c = CarteEnregistrement(C.Profiles[i], i);
                enregistrements.Add(c);
                listeEnregistrements.Children.Add(c.Cadre);
            }
            listeSorties.Children.Clear();
            sorties.Clear();
            for (int i = 0; i < C.VirtualSounds.Count; i++)
            {
                var c = CarteSortie(C.VirtualSounds[i], i);
                sorties.Add(c);
                listeSorties.Children.Add(c.Cadre);
            }
            creerEnregistrement.Visibility = C.Profiles.Count < AudioConfiguration.MaxEnregistrements ? Visibility.Visible : Visibility.Collapsed;
            creerSortie.Visibility = C.VirtualSounds.Count < AudioConfiguration.MaxSortiesVirtuelles ? Visibility.Visible : Visibility.Collapsed;
            aideEnregistrements.Visibility = C.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            aideSorties.Visibility = C.VirtualSounds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            compteEnregistrements.Text = $"{C.Profiles.Count} / {AudioConfiguration.MaxEnregistrements}";
            compteSorties.Text = $"{C.VirtualSounds.Count} / {AudioConfiguration.MaxSortiesVirtuelles}";
            RemplirEcoute();
        }
        finally { remplissage = false; }
        foreach (var b in lignes) MajBande(b);
        MajCartes();
        MajAlerte();
    }

    Bande CreerBande(AudioSource s)
    {
        var b = new Bande { S = s };
        var pile = new StackPanel();
        b.Nom = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        b.Genre = new TextBlock { FontSize = 11.5, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        b.Db = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Volume de la source" };
        pile.Children.Add(b.Nom);
        pile.Children.Add(b.Genre);
        pile.Children.Add(b.Db);

        // échelle de 0 à −60 dB, vumètres gauche et droite, puis le fader (comme dans OBS)
        var zone = new Grid { Height = HauteurVu, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double w in new[] { 26.0, 7, 3, 7, 10, 28 }) zone.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        zone.Children.Add(new EchelleDb { Margin = new Thickness(0, 0, 3, 0) });
        b.G = new Vumetre(7, true) { ToolTip = "Gauche : niveau d'OBS (après volume et filtres), de 0 à −60 dBFS" };
        b.D = new Vumetre(7, true) { ToolTip = "Droite : niveau d'OBS (après volume et filtres), de 0 à −60 dBFS" };
        Grid.SetColumn(b.G, 1);
        Grid.SetColumn(b.D, 3);
        zone.Children.Add(b.G);
        zone.Children.Add(b.D);
        b.Volume = new Slider
        {
            Style = S("CurseurVertical"), Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true,
            Value = AudioConfiguration.Bound(s.VolumeDb, -60, 6), ToolTip = "Volume (−60 à +6 dB) · double-clic : 0 dB · molette : ±1 dB",
        };
        b.Volume.ValueChanged += (_, e) =>
        {
            if (remplissage) return;
            s.VolumeDb = Math.Round(e.NewValue, 1);
            b.Db.Text = TexteDb(s.VolumeDb);
            A.Apply();
        };
        b.Volume.MouseDoubleClick += (_, _) => b.Volume.Value = 0;
        b.Volume.PreviewMouseWheel += (_, e) => { b.Volume.Value = Math.Clamp(b.Volume.Value + Math.Sign(e.Delta), -60, 6); e.Handled = true; };
        Grid.SetColumn(b.Volume, 5);
        zone.Children.Add(b.Volume);
        pile.Children.Add(zone);

        // M et casque, puis les filtres
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        b.Muet = BoutonM(40, 30);
        b.Muet.Click += (_, _) => { s.Muted = !s.Muted; A.Apply(); MajBande(b); };
        b.CasqueIcone = new Shapes.Path
        {
            Data = Geometry.Parse("M3,13 V10 A7,7 0 0 1 17,10 V13 M3,12 H6 V18 H3 Z M14,12 H17 V18 H14 Z"),
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 20, Height = 20, Stretch = Stretch.None,
        };
        b.Casque = new Button { Style = S("Btn"), Width = 40, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = b.CasqueIcone };
        b.Casque.Click += (_, _) => MenuEcoute(b);
        boutons.Children.Add(b.Muet);
        boutons.Children.Add(b.Casque);
        pile.Children.Add(boutons);
        b.Filtres = new Button { Style = S("Btn"), Height = 28, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 6, 0, 0), FontSize = 12.5 };
        b.Filtres.Click += (_, _) => OuvrirFiltres(s);
        pile.Children.Add(b.Filtres);

        // les destinations : 1 à 5 (enregistrements), A à D (sorties MyVirtualSound)
        var pastilles = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < C.Profiles.Count; i++) pastilles.Children.Add(Pastille(b, C.Profiles[i], (i + 1).ToString()));
        for (int i = 0; i < C.VirtualSounds.Count; i++) pastilles.Children.Add(Pastille(b, C.VirtualSounds[i], MarqueSortie(i)));
        if (pastilles.Children.Count > 0) pile.Children.Add(pastilles);

        b.Cadre = new Border
        {
            Child = pile, Width = LargeurBande, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 10, 8, 10), Margin = new Thickness(0, 0, 10, 0),
            BorderThickness = new Thickness(1), BorderBrush = P("Nuit3"), Background = P("Encre"),
            ToolTip = "Clic droit : renommer, changer, mono, ordre, supprimer",
        };
        b.Cadre.MouseRightButtonUp += (_, e) => { MenuBande(b); e.Handled = true; };
        return b;
    }

    Button Pastille(Bande b, IDestinationAudio d, string marque)
    {
        var p = new Button { Style = S("Btn"), Width = 26, Height = 24, Padding = new Thickness(0), FontSize = 12, FontWeight = FontWeights.SemiBold, Content = marque, Margin = new Thickness(2) };
        p.Click += (_, _) =>
        {
            if (!d.SourceIds.Remove(b.S.Id)) d.SourceIds.Add(b.S.Id);
            A.Apply();
            foreach (var l in lignes) MajBande(l);
            MajCartes();
        };
        b.Pastilles.Add((p, d, marque));
        return p;
    }

    /// <summary>Petits écrans : la colonne de droite passe sous le mélangeur, les boutons de l'en-tête sous le titre.</summary>
    void Disposer(double largeur)
    {
        bool e = largeur < 900;
        if (e == etroit) return;
        etroit = e;
        corps.ColumnDefinitions[0].MinWidth = e ? 0 : 360;
        corps.ColumnDefinitions[1].Width = new GridLength(e ? 0 : 24);
        corps.ColumnDefinitions[2].Width = new GridLength(e ? 0 : 300);
        Grid.SetColumn(defilDroite, e ? 0 : 2);
        Grid.SetRow(defilDroite, e ? 1 : 0);
        defilDroite.Margin = new Thickness(0, e ? 22 : 0, 0, 0);
        defilDroite.MaxHeight = e ? double.PositiveInfinity : 430;
        DockPanel.SetDock(actions, e ? Dock.Bottom : Dock.Right);
        actions.Margin = new Thickness(0, e ? 12 : 0, 0, 0);
        actions.HorizontalAlignment = e ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    // ------------------------------------------------------------ mise à jour des bandes
    /// <summary>Les destinations qui coupent cette source en ce moment (option « Couper les autres sources »), ou null.</summary>
    static string? CoupeePar(AudioSource s)
    {
        if (!A.SourdinesForcees.Contains(s.Id)) return null;
        var noms = C.DestinationsActives(A.Recorder.Reserves).Where(d => d.MuteOthers && !d.SourceIds.Contains(s.Id)).Select(d => "« " + d.Name + " »").ToList();
        return noms.Count == 0 ? "un enregistrement" : string.Join(", ", noms);
    }

    static bool EnDirect(IDestinationAudio d) => d is RecordingProfile ? A.Recorder.Active(d.Id) : d is VirtualSoundSettings { Enabled: true };

    void MajBande(Bande b)
    {
        var s = b.S;
        b.Nom.Text = s.Name;
        b.Nom.ToolTip = s.Name;
        b.Genre.Text = Genre(s);
        b.Genre.ToolTip = Description(s);
        b.Db.Text = TexteDb(s.VolumeDb);
        string? par = CoupeePar(s);
        if (s.Muted)
        {
            Allume(b.Muet, true, "Direct");
            b.Muet.ToolTip = "Sourdine active : la source ne va plus nulle part (M pour rétablir)";
        }
        else if (par != null)
        {
            b.Muet.Background = P("RougeLeger");
            b.Muet.BorderBrush = P("RougeBord");
            b.Muet.Foreground = P("RougeTexte");
            b.Muet.ToolTip = $"Coupée tant que {par} tourne (option « Couper les autres sources »)";
        }
        else
        {
            Allume(b.Muet, false, "Direct");
            b.Muet.ToolTip = "Sourdine (M) : couper le son de cette source";
        }
        bool ecoute = s.Monitoring != AudioMonitoring.Off;
        Allume(b.Casque, ecoute, "Or");
        b.CasqueIcone.Stroke = ecoute ? P("Encre") : P("Blanc");
        b.Casque.ToolTip = "Écoute au casque : " + NomEcoute(s.Monitoring).ToLower(Fr) + " (cliquer pour changer)";
        b.Filtres.Content = s.Filters.Count == 0 ? "Filtres" : "Filtres " + s.Filters.Count;
        b.Filtres.ToolTip = s.Filters.Count == 0 ? "Ajouter un filtre (réduction du bruit, noise gate, compresseur, limiteur)" : string.Join(" → ", s.Filters.Select(f => AudioFilter.Nom(f.Kind) + (f.Enabled ? "" : " (coupé)")));
        b.G.Gris = b.D.Gris = s.Muted || par != null;
        bool seule = s.Monitoring == AudioMonitoring.Only;
        foreach (var (bouton, d, marque) in b.Pastilles)
        {
            bool dans = d.SourceIds.Contains(s.Id);
            bool direct = EnDirect(d);
            Allume(bouton, dans, direct ? (d is RecordingProfile ? "Direct" : "Or") : "Blanc");
            bouton.Opacity = seule ? 0.4 : 1;
            string quoi = d is RecordingProfile ? $"Enregistrement {marque} « {d.Name} »" : $"Sortie MyVirtualSound {marque} « {d.Name} »";
            bouton.ToolTip = seule ? quoi + " : écoute seule, la source ne va dans aucune destination"
                : quoi + (dans ? " : la source y va" : " : la source n'y va pas") + (direct ? (d is RecordingProfile ? " (en cours)" : " (active)") : "") + " · cliquer pour changer";
        }
    }

    /// <summary>Bouton « allumé » : fond plein (rouge pour la sourdine, bleu pour l'écoute, noir pour une destination) et texte blanc.</summary>
    static void Allume(Button b, bool actif, string fond)
    {
        if (actif)
        {
            b.Background = P(fond);
            b.BorderBrush = P(fond);
            b.Foreground = P("Encre");
        }
        else
        {
            b.ClearValue(Control.BackgroundProperty);
            b.ClearValue(Control.BorderBrushProperty);
            b.ClearValue(Control.ForegroundProperty);
        }
    }

    internal static string Genre(AudioSource s)
    {
        string g = s.Kind switch
        {
            AudioInputKind.Microphone => "Micro",
            AudioInputKind.Sortie => "Sortie audio",
            _ when s.FollowProjection => "Vidéo Display",
            _ => "Média",
        };
        return s.Mono ? g + " · mono" : g;
    }

    static string Description(AudioSource s)
    {
        string peripherique = s.DeviceId == "default" || string.IsNullOrEmpty(s.DeviceName) ? "périphérique Windows par défaut" : s.DeviceName;
        string d = s.Kind switch
        {
            AudioInputKind.Microphone => "Microphone · " + peripherique,
            AudioInputKind.Sortie => "Sortie audio · " + peripherique,
            _ when s.FollowProjection => "Vidéo projetée · " + (s.MediaPath.Length > 0 ? "lecture en cours" : "en attente d'une vidéo"),
            _ => "Média · " + (s.MediaPath.Length > 0 ? Path.GetFileName(s.MediaPath) : "aucun fichier") + (s.Loop ? " · en boucle" : ""),
        };
        return s.Mono ? d + " · mono" : d;
    }

    public static string TexteDb(double db) =>
        db <= -60 ? "−∞ dB" : (db < -0.05 ? "−" : db > 0.05 ? "+" : "") + Math.Abs(db).ToString("0.0", Fr) + " dB";

    public static string NomEcoute(AudioMonitoring m) => m switch
    {
        AudioMonitoring.Off => "Écoute coupée",
        AudioMonitoring.Only => "Écoute seule (sortie coupée)",
        _ => "Écoute et sortie",
    };

    static string Taille(long octets) => octets >= 1L << 30
        ? (octets / (double)(1L << 30)).ToString("0.0", Fr) + " Go"
        : Math.Max(0, octets / (double)(1 << 20)).ToString("0", Fr) + " Mo";

    static string NomSource(string id) => C.Sources.FirstOrDefault(s => s.Id == id)?.Name ?? "?";

    /// <summary>« Pasteur · vidéo · coupe les autres » : ce que garde un enregistrement.</summary>
    internal static string Resume(RecordingProfile p)
    {
        int n = p.SourceIds.Count, d = p.DisplayIds.Count;
        string sources = n == 0 ? "aucune source audio" : n == 1 ? NomSource(p.SourceIds[0]) : n + " sources";
        string quoi = d == 0 ? p.Format.ToUpperInvariant() : d == 1 ? "vidéo" : $"vidéo ({d} Display)";
        return sources + " · " + quoi + (p.MuteOthers ? " · coupe les autres" : "");
    }

    // ------------------------------------------------------------ cartes des enregistrements et des sorties
    Carte CarteEnregistrement(RecordingProfile p, int i)
    {
        var c = new Carte { Id = p.Id };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        c.Symbole = new TextBlock { FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        c.Bouton = new Button { Style = S("Btn"), Width = 40, Height = 40, Padding = new Thickness(0), Content = c.Symbole, VerticalAlignment = VerticalAlignment.Center };
        c.Bouton.Click += (_, _) => Basculer(p);
        g.Children.Add(c.Bouton);
        var milieu = new StackPanel { Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var haut = new DockPanel();
        var badge = Badge((i + 1).ToString());
        haut.Children.Add(badge);
        c.Nom = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        haut.Children.Add(c.Nom);
        milieu.Children.Add(haut);
        c.Etat = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        milieu.Children.Add(c.Etat);
        c.Vu = new Vumetre(5) { Margin = new Thickness(0, 7, 0, 0), ToolTip = "Niveau enregistré (mélange de ses sources)" };
        milieu.Children.Add(c.Vu);
        Grid.SetColumn(milieu, 1);
        g.Children.Add(milieu);
        var plus = new Button { Style = S("BtnPlat"), Width = 32, Height = 32, Padding = new Thickness(0), Content = "⋯", FontSize = 18, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Modifier, ouvrir le dossier, supprimer" };
        plus.Click += (_, _) => MenuEnregistrement(p, plus);
        Grid.SetColumn(plus, 2);
        g.Children.Add(plus);
        c.Cadre = new Border { Child = g, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = P("Nuit3"), Background = P("Encre"), Padding = new Thickness(10, 10, 6, 10), Margin = new Thickness(0, 0, 0, 8) };
        c.Cadre.MouseRightButtonUp += (_, e) => { MenuEnregistrement(p, c.Cadre); e.Handled = true; };
        return c;
    }

    Carte CarteSortie(VirtualSoundSettings v, int i)
    {
        var c = new Carte { Id = v.Id };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        c.Actif = new CheckBox { Style = S("Interrupteur"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Activer ou couper cette sortie" };
        c.Actif.Click += (_, _) =>
        {
            v.Enabled = c.Actif.IsChecked == true;
            A.Apply();
            foreach (var l in lignes) MajBande(l);
            MajCartes();
        };
        g.Children.Add(c.Actif);
        var milieu = new StackPanel { Margin = new Thickness(12, 0, 6, 0) };
        var haut = new DockPanel();
        haut.Children.Add(Badge(MarqueSortie(i)));
        c.Nom = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        haut.Children.Add(c.Nom);
        milieu.Children.Add(haut);
        c.Etat = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        milieu.Children.Add(c.Etat);
        c.Vu = new Vumetre(5) { Margin = new Thickness(0, 7, 0, 0), ToolTip = "Niveau envoyé (après le volume de la sortie)" };
        milieu.Children.Add(c.Vu);
        Grid.SetColumn(milieu, 1);
        g.Children.Add(milieu);
        var plus = new Button { Style = S("BtnPlat"), Width = 32, Height = 32, Padding = new Thickness(0), Content = "⋯", FontSize = 18, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Modifier, supprimer" };
        plus.Click += (_, _) => MenuSortie(v, plus);
        Grid.SetColumn(plus, 2);
        g.Children.Add(plus);

        var volume = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        volume.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volume.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volume.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        c.Volume = new Slider { Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, Value = AudioConfiguration.Bound(v.VolumeDb, -60, 6), ToolTip = "Volume de cette sortie · double-clic : 0 dB" };
        c.Volume.ValueChanged += (_, e) =>
        {
            if (remplissage) return;
            v.VolumeDb = Math.Round(e.NewValue, 1);
            if (c.Db != null) c.Db.Text = TexteDb(v.VolumeDb);
            A.Apply();
        };
        c.Volume.MouseDoubleClick += (_, _) => c.Volume.Value = 0;
        volume.Children.Add(c.Volume);
        c.Db = new TextBlock { FontSize = 12.5, MinWidth = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(c.Db, 1);
        volume.Children.Add(c.Db);
        c.Muet = BoutonM(32, 28);
        c.Muet.Margin = new Thickness(8, 0, 0, 0);
        c.Muet.Click += (_, _) => { v.Muted = !v.Muted; A.Apply(); MajCartes(); };
        Grid.SetColumn(c.Muet, 2);
        volume.Children.Add(c.Muet);
        Grid.SetRow(volume, 1);
        Grid.SetColumn(volume, 1);
        Grid.SetColumnSpan(volume, 2);
        g.Children.Add(volume);
        c.Cadre = new Border { Child = g, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = P("Nuit3"), Background = P("Encre"), Padding = new Thickness(12, 10, 6, 10), Margin = new Thickness(0, 0, 0, 8) };
        c.Cadre.MouseRightButtonUp += (_, e) => { MenuSortie(v, c.Cadre); e.Handled = true; };
        return c;
    }

    void MajCartes()
    {
        bool avant = remplissage;
        remplissage = true;
        try
        {
            foreach (var c in enregistrements)
                if (C.Profiles.FirstOrDefault(p => p.Id == c.Id) is { } p) MajCarte(c, p);
            foreach (var c in sorties)
                if (C.VirtualSounds.FirstOrDefault(v => v.Id == c.Id) is { } v) MajCarte(c, v);
        }
        finally { remplissage = avant; }
    }

    void MajCarte(Carte c, RecordingProfile p)
    {
        var r = A.Recorder;
        bool actif = r.Active(p.Id), occupe = r.Busy(p.Id);
        var etat = r.State(p.Id);
        string message = r.Message(p.Id);
        c.Nom.Text = p.Name;
        c.Nom.ToolTip = p.Name;
        c.Bouton!.Style = S(actif ? "BtnRouge" : "Btn");
        c.Symbole!.Text = actif ? "■" : "●";
        c.Symbole.Foreground = actif ? P("Encre") : P("Direct");
        c.Bouton.IsEnabled = !occupe;
        c.Bouton.ToolTip = actif ? $"Arrêter « {p.Name} » (les autres enregistrements continuent)" : $"Démarrer « {p.Name} »";
        string texte;
        bool rouge = false;
        if (actif) { texte = "● " + r.Duration(p.Id).ToString(@"hh\:mm\:ss") + " · " + Taille(r.Bytes(p.Id)); rouge = true; }
        else if (occupe) texte = message;
        else if (etat == ChurchRecordingState.Error) { texte = message; rouge = true; }
        else if (message.StartsWith("Terminé", StringComparison.Ordinal) && r.LastFile(p.Id) is { } f) texte = "Terminé · " + Path.GetFileName(f);
        else texte = Resume(p);
        c.Etat.Text = texte;
        c.Etat.Foreground = P(rouge ? "Direct" : "Brume");
        c.Etat.ToolTip = (etat == ChurchRecordingState.Error ? message + "\n" : "") + Resume(p) + "\nDossier : " + p.Directory
            + (actif ? "\nEncodeur : " + r.Encoder(p.Id) : "");
        c.Vu.Visibility = actif ? Visibility.Visible : Visibility.Collapsed;
        c.Cadre.BorderBrush = P(actif ? "Direct" : "Nuit3");
    }

    static string NomPeripherique(VirtualSoundSettings v) => v.DeviceId switch
    {
        "" => "câble virtuel automatique",
        "default" => "sortie Windows par défaut",
        _ => v.DeviceName.Length > 0 ? v.DeviceName : "sortie choisie",
    };

    void MajCarte(Carte c, VirtualSoundSettings v)
    {
        c.Nom.Text = v.Name;
        c.Nom.ToolTip = v.Name;
        c.Actif!.IsChecked = v.Enabled;
        bool marche = A.Sorties.Actif(v.Id);
        string statut = v.Enabled ? A.Sorties.Status(v.Id) : "Désactivée · " + NomPeripherique(v);
        c.Etat.Text = statut;
        c.Etat.Foreground = P(v.Enabled && !marche ? "Direct" : "Brume");
        string sources = v.SourceIds.Count == 0 ? "aucune source" : string.Join(", ", v.SourceIds.Select(NomSource));
        c.Etat.ToolTip = statut + "\nSources : " + sources + (v.MuteOthers ? "\nCoupe les autres sources tant qu'elle est active" : "");
        // pas pendant que l'utilisateur tient le curseur
        if (c.Volume is { IsMouseCaptureWithin: false } curseur && Math.Abs(curseur.Value - v.VolumeDb) > 0.05) curseur.Value = AudioConfiguration.Bound(v.VolumeDb, -60, 6);
        if (c.Db != null) c.Db.Text = TexteDb(v.VolumeDb);
        if (c.Muet != null) Allume(c.Muet, v.Muted, "Direct");
        c.Vu.Gris = v.Muted || !v.Enabled;
        c.Cadre.BorderBrush = P(marche ? "Or" : "Nuit3");
    }

    void Basculer(RecordingProfile p)
    {
        if (!A.Recorder.Active(p.Id) && p.SourceIds.Count == 0 && p.DisplayIds.Count == 0)
        {
            Toast($"« {p.Name} » n'a aucune source : choisissez-les.");
            Montrer(new EnregistrementWindow(p));
            return;
        }
        _ = A.Recorder.ToggleAsync(p);
        MajCartes();
    }

    /// <summary>Un message par enregistrement (erreur, fichier terminé), une seule fois.</summary>
    void Annoncer()
    {
        foreach (var p in C.Profiles)
        {
            string m = A.Recorder.Message(p.Id);
            if (messages.TryGetValue(p.Id, out var avant) && avant == m) continue;
            bool premier = !messages.ContainsKey(p.Id);
            messages[p.Id] = m;
            if (premier) continue;
            var etat = A.Recorder.State(p.Id);
            if (etat == ChurchRecordingState.Error) Toast($"« {p.Name} » : {m}");
            else if (etat == ChurchRecordingState.Ready && m.StartsWith("Terminé", StringComparison.Ordinal) && A.Recorder.LastFile(p.Id) is { } f)
                Toast($"« {p.Name} » terminé : {Path.GetFileName(f)}");
        }
    }

    // ------------------------------------------------------------ état du moteur
    void MajReduction()
    {
        bool ouvert = C.PanelVisible;
        corps.Visibility = ouvert ? Visibility.Visible : Visibility.Collapsed;
        chevron.RenderTransform = new RotateTransform(ouvert ? 180 : 0);
        reduire.ToolTip = ouvert ? "Réduire le panneau Audio" : "Afficher le panneau Audio";
    }

    void MajAlerte()
    {
        string? texte = null;
        bool reparable = false;
        if (A.ObsAbsent) { texte = A.Diagnostic; reparable = true; }
        else if (!A.Running && A.Diagnostic.StartsWith("Moteur audio OBS indisponible", StringComparison.Ordinal)) { texte = A.Diagnostic; reparable = A.Diagnostic.Contains("Réparer"); }
        else if (!A.PistesSuffisantes)
            texte = "Plus de piste d'OBS libre : 6 mélanges différents sont déjà en cours (enregistrements et sorties MyVirtualSound). Arrêtez-en un, ou donnez-leur les mêmes sources : ils partagent alors une piste.";
        alerte.Visibility = texte == null ? Visibility.Collapsed : Visibility.Visible;
        alerteTexte.Text = texte ?? "";
        reparer.Visibility = reparable ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Le moteur a changé (démarrage, réglages appliqués, vidéo projetée, enregistrement démarré) : textes et couleurs seulement.</summary>
    void SurChangement()
    {
        moteur.Text = A.Running ? "Moteur OBS · libobs " + A.Version + (A.TracksActive ? "" : " · pistes indisponibles") : A.ObsAbsent ? "Moteur OBS absent" : A.Diagnostic;
        puce.ToolTip = A.Diagnostic;
        if (A.Running != demarre)
        {
            demarre = A.Running;
            RemplirEcoute();
        }
        MajAlerte();
        foreach (var b in lignes) MajBande(b);
        MajCartes();
    }

    async void Reparer()
    {
        if (!Dialogue.Confirmer(Fenetre, "Réparer le moteur audio : MyChurch télécharge sa dernière version sur GitHub et la réinstalle. Vos chants, agendas et réglages sont gardés. MyChurch se fermera puis se rouvrira.", "Réparer")) return;
        reparer.IsEnabled = false;
        try
        {
            var r = await MiseAJour.Verifier("0.0.0");
            if (r.Erreur != null || r.Url.Length == 0) { Toast(r.Erreur ?? "Aucun installateur de MyChurch trouvé sur GitHub."); return; }
            await MiseAJour.Installer(r.Url, new Progress<int>(n => reparer.Content = $"Téléchargement… {n} %"));
            Journal.Info("réparation du moteur audio : réinstallation de MyChurch " + r.Version);
            Toast("Installation… MyChurch va se fermer puis se rouvrir.");
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception e)
        {
            Toast("Réparation impossible : " + e.Message);
            Journal.Erreur("réparation du moteur audio", e);
        }
        finally
        {
            reparer.IsEnabled = true;
            reparer.Content = "Réparer le moteur audio";
        }
    }

    void RemplirEcoute()
    {
        bool avant = remplissage;
        remplissage = true;
        try
        {
            ecoute.Items.Clear();
            ecoute.Items.Add(new ComboBoxItem { Content = "Périphérique Windows par défaut", Tag = "default" });
            var liste = A.MonitoringDevices();
            foreach (var d in liste) ecoute.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
            // le périphérique enregistré reste affiché même s'il est débranché (ou si le moteur n'a pas démarré)
            if (C.MonitorDeviceId != "default" && liste.All(d => d.Id != C.MonitorDeviceId))
                ecoute.Items.Add(new ComboBoxItem { Content = (C.MonitorDeviceName.Length > 0 ? C.MonitorDeviceName : C.MonitorDeviceId) + " (absent)", Tag = C.MonitorDeviceId });
            ecoute.SelectedItem = ecoute.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == C.MonitorDeviceId) ?? ecoute.Items[0];
        }
        finally { remplissage = avant; }
    }

    // ------------------------------------------------------------ vumètres et durées (20 fois par seconde)
    void Battre()
    {
        if (!IsVisible) return;
        if (++tic % 5 == 0) MajCartes(); // durée et taille des fichiers en cours, 4 fois par seconde
        if (!C.PanelVisible) return;
        foreach (var b in lignes)
        {
            var (g, d) = A.Level(b.S.Id);
            b.G.Niveau(g);
            b.D.Niveau(d);
        }
        foreach (var c in enregistrements)
            if (C.Profiles.FirstOrDefault(p => p.Id == c.Id) is { Track: > 0 } p && A.Recorder.Active(p.Id)) c.Vu.Niveau(A.TrackLevel(p.Track - 1));
        foreach (var c in sorties) c.Vu.Niveau(A.Sorties.Level(c.Id));
    }

    // ------------------------------------------------------------ menus
    static MenuItem Article(string titre, Action action, bool actif = true)
    {
        var m = new MenuItem { Header = titre, IsEnabled = actif };
        m.Click += (_, _) => action();
        return m;
    }

    void Ouvrir(ContextMenu m, UIElement cible, PlacementMode ou)
    {
        m.PlacementTarget = cible;
        m.Placement = ou;
        m.FontFamily = (FontFamily)FindResource("Ui");
        m.FontSize = 14;
        m.IsOpen = true;
    }

    void Montrer(Window w)
    {
        if (Fenetre is { IsLoaded: true } f) w.Owner = f;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        Rafraichir();
    }

    static void Lancer(string fichier, string arguments = "")
    {
        try { Process.Start(new ProcessStartInfo(fichier, arguments) { UseShellExecute = true }); }
        catch (Exception e) { Journal.Erreur("ouverture de " + fichier, e); }
    }

    void MenuEnregistrement(RecordingProfile p, UIElement cible)
    {
        bool actif = A.Recorder.Active(p.Id) || A.Recorder.Busy(p.Id);
        var m = new ContextMenu();
        m.Items.Add(Article("Modifier…", () => Montrer(new EnregistrementWindow(p))));
        m.Items.Add(Article("Ouvrir le dossier", () =>
        {
            try { Directory.CreateDirectory(p.Directory); } catch (Exception) { }
            Lancer(p.Directory);
        }));
        if (A.Recorder.LastFile(p.Id) is { } f && File.Exists(f))
            m.Items.Add(Article("Montrer le dernier fichier", () => Lancer("explorer.exe", $"/select,\"{f}\"")));
        m.Items.Add(new Separator());
        var supprimer = Article("Supprimer", () =>
        {
            if (!Dialogue.Confirmer(Fenetre, $"Supprimer l'enregistrement « {p.Name} » ? Les fichiers déjà enregistrés restent dans leur dossier.", "Supprimer")) return;
            C.Profiles.Remove(p);
            A.Apply();
            Rafraichir();
        }, !actif);
        if (actif) supprimer.ToolTip = "Arrêtez d'abord cet enregistrement";
        ToolTipService.SetShowOnDisabled(supprimer, true);
        m.Items.Add(supprimer);
        Ouvrir(m, cible, cible is Button ? PlacementMode.Bottom : PlacementMode.MousePoint);
    }

    void MenuSortie(VirtualSoundSettings v, UIElement cible)
    {
        var m = new ContextMenu();
        m.Items.Add(Article("Modifier…", () => Montrer(new SortieWindow(v))));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Supprimer", () =>
        {
            if (!Dialogue.Confirmer(Fenetre, $"Supprimer la sortie MyVirtualSound « {v.Name} » ?", "Supprimer")) return;
            C.VirtualSounds.Remove(v);
            A.Apply();
            Rafraichir();
        }));
        Ouvrir(m, cible, cible is Button ? PlacementMode.Bottom : PlacementMode.MousePoint);
    }

    // ------------------------------------------------------------ ajouter, changer, retirer une source
    void MenuAjouter()
    {
        var m = new ContextMenu();
        m.Items.Add(Article("Microphone…", () => AjouterPeripherique(AudioInputKind.Microphone)));
        m.Items.Add(Article("Son d'une sortie audio…", () => AjouterPeripherique(AudioInputKind.Sortie)));
        m.Items.Add(Article("Fichier audio ou vidéo…", AjouterFichier));
        m.Items.Add(Article("Son de la vidéo projetée (Display)", AjouterProjection));
        Ouvrir(m, ajouter, PlacementMode.Bottom);
    }

    void Ajouter(AudioSource s)
    {
        C.Sources.Add(s);
        A.Apply();
        Rafraichir();
        Deplier();
        Toast($"« {s.Name} » ajoutée. Allumez ses pastilles (1, 2… ou A, B…) pour l'envoyer dans un enregistrement ou une sortie.");
    }

    static string NomLibre(string nom)
    {
        if (C.Sources.All(s => s.Name != nom)) return nom;
        for (int i = 2; ; i++)
            if (C.Sources.All(s => s.Name != $"{nom} {i}")) return $"{nom} {i}";
    }

    /// <summary>« Microphone (Focusrite USB) » → « Focusrite USB » : un nom court pour la bande (l'utilisateur peut le renommer).</summary>
    static string NomCourt(string nom)
    {
        int a = nom.IndexOf('('), b = nom.LastIndexOf(')');
        return a >= 0 && b > a + 1 ? nom[(a + 1)..b].Trim() : nom.Trim();
    }

    List<AudioDevice> Peripheriques(AudioInputKind kind)
    {
        var liste = new List<AudioDevice> { new("default", kind == AudioInputKind.Microphone ? "Microphone Windows par défaut" : "Sortie Windows par défaut") };
        liste.AddRange(kind == AudioInputKind.Microphone ? A.Microphones() : A.Outputs());
        return liste;
    }

    void AjouterPeripherique(AudioInputKind kind)
    {
        bool micro = kind == AudioInputKind.Microphone;
        string aide = micro ? "Le micro, la table de mixage ou la carte son à envoyer dans le mélangeur." : "MyChurch capte ce qui joue sur cette sortie (son de l'ordinateur, navigateur, Zoom…).";
        if (!A.Running) aide += " Le moteur OBS n'est pas démarré : seul le périphérique par défaut est proposé.";
        var d = ChoisirPeripherique(Fenetre, micro ? "Ajouter un microphone" : "Ajouter le son d'une sortie audio", aide, Peripheriques(kind), micro ? C.MicrophoneId : C.OutputId);
        if (d == null) return;
        string nom = micro ? (d.Id == "default" ? "Micro" : NomCourt(d.Name)) : "Son de l'ordinateur";
        Ajouter(new AudioSource { Name = NomLibre(nom), Kind = kind, DeviceId = d.Id, DeviceName = d.Id == "default" ? "" : d.Name });
    }

    static string? ChoisirFichier(Window? owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Fichier audio ou vidéo pour le mélangeur",
            Filter = "Audio et vidéo|*.mp3;*.wav;*.flac;*.ogg;*.opus;*.m4a;*.aac;*.wma;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|Tous les fichiers|*.*",
        };
        bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    void AjouterFichier()
    {
        if (ChoisirFichier(Fenetre) is not { } chemin) return;
        Ajouter(new AudioSource { Name = NomLibre(Path.GetFileNameWithoutExtension(chemin)), Kind = AudioInputKind.Media, MediaPath = chemin });
    }

    void AjouterProjection()
    {
        if (C.Sources.FirstOrDefault(s => s.Kind == AudioInputKind.Media && s.FollowProjection) is { } deja)
        {
            Toast($"« {deja.Name} » joue déjà le son de la vidéo projetée.");
            return;
        }
        Ajouter(new AudioSource { Name = NomLibre("Vidéo Display"), Kind = AudioInputKind.Media, FollowProjection = true });
    }

    void MenuEcoute(Bande b)
    {
        var s = b.S;
        var m = new ContextMenu();
        foreach (var mode in new[] { AudioMonitoring.Off, AudioMonitoring.Only, AudioMonitoring.AndOutput })
        {
            var item = new MenuItem { Header = NomEcoute(mode), IsCheckable = true, IsChecked = s.Monitoring == mode };
            item.Click += (_, _) =>
            {
                s.Monitoring = mode;
                A.Apply();
                MajBande(b);
            };
            m.Items.Add(item);
        }
        Ouvrir(m, b.Casque, PlacementMode.Bottom);
    }

    void MenuBande(Bande b)
    {
        var s = b.S;
        int i = C.Sources.IndexOf(s);
        var m = new ContextMenu();
        m.Items.Add(Article("Renommer…", () =>
        {
            if (Dialogue.Demander(Fenetre, "Nom de la source :", s.Name, "Renommer") is { } n && n.Trim().Length > 0) { s.Name = n.Trim(); Appliquer(); }
        }));
        if (s.Kind != AudioInputKind.Media) m.Items.Add(Article("Changer de périphérique…", () => ChangerPeripherique(s)));
        else if (!s.FollowProjection) m.Items.Add(Article("Changer de fichier…", () =>
        {
            if (ChoisirFichier(Fenetre) is { } chemin) { s.MediaPath = chemin; Appliquer(); }
        }));
        var mono = new MenuItem { Header = "Mono", IsCheckable = true, IsChecked = s.Mono };
        mono.Click += (_, _) => { s.Mono = mono.IsChecked; Appliquer(); };
        m.Items.Add(mono);
        if (s.Kind == AudioInputKind.Media)
        {
            if (!s.FollowProjection)
            {
                var boucle = new MenuItem { Header = "En boucle", IsCheckable = true, IsChecked = s.Loop };
                boucle.Click += (_, _) => { s.Loop = boucle.IsChecked; Appliquer(); };
                m.Items.Add(boucle);
            }
            m.Items.Add(Article("Relancer le média", () => A.RestartMedia(s)));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Article("Vers la gauche", () => Deplacer(s, -1), i > 0));
        m.Items.Add(Article("Vers la droite", () => Deplacer(s, 1), i >= 0 && i < C.Sources.Count - 1));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Supprimer", () =>
        {
            if (!Dialogue.Confirmer(Fenetre, $"Retirer « {s.Name} » du mélangeur ? Elle sort aussi des enregistrements et des sorties.", "Retirer")) return;
            C.Sources.Remove(s);
            Appliquer();
        }));
        Ouvrir(m, b.Cadre, PlacementMode.MousePoint);
    }

    void Appliquer()
    {
        A.Apply();
        Rafraichir();
    }

    void Deplacer(AudioSource s, int sens)
    {
        int i = C.Sources.IndexOf(s), j = i + sens;
        if (i < 0 || j < 0 || j >= C.Sources.Count) return;
        (C.Sources[i], C.Sources[j]) = (C.Sources[j], C.Sources[i]);
        Appliquer();
    }

    void ChangerPeripherique(AudioSource s)
    {
        bool micro = s.Kind == AudioInputKind.Microphone;
        var d = ChoisirPeripherique(Fenetre, micro ? "Changer de microphone" : "Changer de sortie audio", "La source garde son nom, son volume, ses filtres et ses destinations.", Peripheriques(s.Kind), s.DeviceId);
        if (d == null) return;
        s.DeviceId = d.Id;
        s.DeviceName = d.Id == "default" ? "" : d.Name;
        Appliquer();
    }

    void OuvrirFiltres(AudioSource s)
    {
        var w = new FiltresWindow(s);
        if (Fenetre is { IsLoaded: true } f) w.Owner = f;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        Rafraichir();
    }

    /// <summary>Choisir un périphérique dans une liste (ajout ou changement d'une source). Renvoie null si l'utilisateur annule.</summary>
    internal static AudioDevice? ChoisirPeripherique(Window? owner, string titre, string aide, List<AudioDevice> liste, string defaut)
    {
        var w = new FenetreDialogue { Title = titre, Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        if (owner is { IsLoaded: true }) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var pile = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        pile.Children.Add(new TextBlock { Text = titre, Style = S("Titre2") });
        pile.Children.Add(new TextBlock { Text = aide, Style = S("Muet"), FontSize = 14, Margin = new Thickness(0, 6, 0, 0) });
        var choix = new ListBox
        {
            Style = S("ListeChants"), ItemContainerStyle = S("ElementChant"), Height = 280, Margin = new Thickness(0, 14, 0, 0),
            ItemsSource = liste, DisplayMemberPath = "Name",
        };
        int index = liste.FindIndex(d => d.Id == defaut);
        choix.SelectedIndex = index >= 0 ? index : 0;
        pile.Children.Add(choix);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var non = new Button { Style = S("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 40 };
        var oui = new Button { Style = S("BtnOr"), Content = "Choisir", IsDefault = true, MinWidth = 120, Height = 40, Margin = new Thickness(10, 0, 0, 0) };
        AudioDevice? res = null;
        non.Click += (_, _) => w.Close();
        oui.Click += (_, _) => { res = choix.SelectedItem as AudioDevice; w.Close(); };
        choix.MouseDoubleClick += (_, _) => { if (choix.SelectedItem is AudioDevice d) { res = d; w.Close(); } };
        boutons.Children.Add(non);
        boutons.Children.Add(oui);
        pile.Children.Add(boutons);
        w.Content = pile;
        w.Loaded += (_, _) => { choix.Focus(); choix.ScrollIntoView(choix.SelectedItem); };
        w.ShowDialog();
        return res;
    }
}

/// <summary>
/// L'échelle d'un vumètre vertical, comme dans OBS : 0 en haut, −60 en bas, un chiffre tous les 5 dB
/// (tous les 10 dB si la place manque), un petit trait à chaque graduation.
/// </summary>
internal sealed class EchelleDb : FrameworkElement
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, w = ActualWidth;
        if (h <= 0 || w <= 0) return;
        int pas = h / 12 >= 13 ? 5 : 10;
        var encre = MainWindow.Pinceau("Brume");
        var trait = new Pen(MainWindow.Pinceau("Brume2"), 1);
        trait.Freeze();
        var police = new Typeface(TryFindResource("Ui") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int db = 0; db >= -60; db -= 5)
        {
            double y = Math.Round(h * -db / 60.0) + 0.5;
            dc.DrawLine(trait, new Point(w - (db % 10 == 0 ? 4 : 2), y), new Point(w, y));
            if (db % pas != 0) continue;
            var t = new FormattedText(db == 0 ? "0" : "−" + (-db).ToString(Fr), Fr, FlowDirection.LeftToRight, police, 9.5, encre, dpi);
            dc.DrawText(t, new Point(w - 6 - t.Width, y - t.Height / 2));
        }
    }
}

/// <summary>
/// Un vumètre de −60 à 0 dBFS, horizontal ou vertical : vert, puis jaune vers −20 dB, rouge près de 0 dB (comme OBS),
/// avec une retombée douce et un repère de crête tenu 1,5 s. Grisé quand la source est coupée (le signal reste visible, comme dans OBS).
/// </summary>
internal sealed class Vumetre : Grid
{
    static Brush? couleursH, couleursV, fondH, fondV;
    readonly bool vertical;
    readonly Shapes.Rectangle barre;
    readonly Border crete;
    readonly RectangleGeometry coupe = new(new Rect(0, 0, 0, 0));
    double affiche = double.NegativeInfinity, pic = double.NegativeInfinity;
    long picDepuis;
    bool gris;

    public Vumetre(double epaisseur, bool vertical = false)
    {
        this.vertical = vertical;
        if (vertical) Width = epaisseur; else Height = epaisseur;
        SnapsToDevicePixels = true;
        Children.Add(new Border { Background = vertical ? (fondV ??= Zones(true)) : (fondH ??= Zones(false)), CornerRadius = new CornerRadius(epaisseur / 2) });
        barre = new Shapes.Rectangle { Fill = vertical ? (couleursV ??= Degrade(true)) : (couleursH ??= Degrade(false)), RadiusX = epaisseur / 2, RadiusY = epaisseur / 2, Clip = coupe };
        Children.Add(barre);
        crete = new Border
        {
            Background = MainWindow.Pinceau("Blanc"), Visibility = Visibility.Collapsed, Opacity = 0.75,
            HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Stretch,
        };
        if (vertical) crete.Height = 2; else crete.Width = 2;
        Children.Add(crete);
    }

    public bool Gris
    {
        get => gris;
        set { gris = value; barre.Opacity = value ? 0.32 : 1; crete.Opacity = value ? 0.25 : 0.75; }
    }

    /// <summary>Nouveau niveau en dBFS (−∞ = silence).</summary>
    public void Niveau(double dbfs)
    {
        if (double.IsNaN(dbfs) || dbfs < -100) dbfs = -100;
        // retombée d'environ 24 dB par seconde, montée immédiate ; la crête reste 1,5 s puis redescend
        affiche = dbfs >= affiche ? dbfs : Math.Max(dbfs, affiche - 1.2);
        long maintenant = Environment.TickCount64;
        if (dbfs >= pic) { pic = dbfs; picDepuis = maintenant; }
        else if (maintenant - picDepuis > 1500) pic = Math.Max(affiche, pic - 1.5);
        double f = Math.Clamp((affiche + 60) / 60, 0, 1), fp = Math.Clamp((pic + 60) / 60, 0, 1);
        double w = Math.Max(0, ActualWidth), h = Math.Max(0, ActualHeight);
        if (vertical)
        {
            coupe.Rect = new Rect(0, h * (1 - f), w, h * f);
            crete.Margin = new Thickness(0, Math.Max(0, h * (1 - fp) - 1), 0, 0);
        }
        else
        {
            coupe.Rect = new Rect(0, 0, w * f, h);
            crete.Margin = new Thickness(Math.Max(0, w * fp - 1), 0, 0, 0);
        }
        crete.Visibility = fp > 0.001 ? Visibility.Visible : Visibility.Collapsed;
    }

    static Brush Degrade(bool vertical)
    {
        var b = vertical ? new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) } : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0.64)); // −21,6 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.8));  // −12 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.86));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xD9, 0x2D, 0x20), 0.96));  // −2,4 dB
        b.Freeze();
        return b;
    }

    static Brush Zones(bool vertical)
    {
        var b = vertical ? new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) } : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE9, 0xEE), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE9, 0xEE), 0.86));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xDC, 0xD9), 0.88));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xDC, 0xD9), 1));
        b.Freeze();
        return b;
    }
}
