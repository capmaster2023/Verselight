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
/// Le panneau Audio de Display, sous la grille. Tout le son passe par le moteur d'OBS (libobs).
/// À gauche le mélangeur d'OBS 32.1 : vertical par défaut (ou horizontal), une bande par source (échelle de 0 à −60 dB,
/// vumètres gauche et droite, fader, sourdine, écoute, filtres et pastilles des destinations), les périphériques globaux et les
/// sources épinglées d'abord, les masquées et les inactives selon les options. À droite les enregistrements (5 au plus, qui peuvent
/// tourner en même temps) et les sorties MyVirtualSound (4 au plus), chacun avec les sources choisies par l'utilisateur.
/// </summary>
public sealed class AudioPanel : Border
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    const string Vide = "Le mélangeur est vide : « + Source audio » pour ajouter un micro, le son d'une sortie ou un média.";
    const string Legende = "Pastilles : 1 à 5 = enregistrements, A à D = sorties MyVirtualSound (allumée : la source y va) · clic sur le nom ou clic droit : épingler, masquer, renommer, écoute, ordre";
    const double LargeurBande = 112, HauteurVu = 176;

    /// <summary>Une bande verticale du mélangeur.</summary>
    sealed class Bande
    {
        public AudioSource S = null!;
        public Border Cadre = null!;
        public TextBlock Nom = null!, Genre = null!, Perif = null!, Db = null!;
        public Vumetre G = null!, D = null!;
        public Slider Volume = null!;
        public Button Muet = null!, Casque = null!, Filtres = null!;
        public Shapes.Path CasqueIcone = null!;
        public readonly List<(Button Bouton, IDestinationAudio Destination, string Marque)> Pastilles = new();
        /// <summary>L'étiquette d'OBS 32.1 : GLOBALE, ÉPINGLÉE, ACTIVE, INACTIVE ou MASQUÉE.</summary>
        public TextBlock Statut = null!;
        /// <summary>Ligne de la disposition horizontale (sinon bande verticale).</summary>
        public bool Horizontale;
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
    readonly ScrollViewer defilBandes;
    readonly Button masquees, inactives, options, disposition, avancees;
    readonly Shapes.Path iconeDisposition;
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
        peripheriques = new Button { Style = S("Btn"), Content = "Réglages audio", Height = 42, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Réglages › Audio, comme dans OBS : fréquence, canaux, périphériques globaux, vumètres, écoute, raccourcis" };
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
        // la barre du mélangeur d'OBS 32.1 : « 2 masquées », Options, disposition, propriétés audio avancées
        masquees = new Button { Style = S("BtnLien"), FontSize = 12.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Afficher les sources masquées" };
        masquees.Click += (_, _) => { C.ShowHidden = true; A.Apply(); Rafraichir(); };
        inactives = new Button { Style = S("BtnLien"), FontSize = 12.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Afficher les sources inactives (périphérique débranché, fichier introuvable)" };
        inactives.Click += (_, _) => { C.ShowInactive = true; A.Apply(); Rafraichir(); };
        options = new Button { Style = S("BtnPlat"), Content = "Options ▾", Height = 32, Padding = new Thickness(10, 0, 10, 0), FontSize = 13, ToolTip = "Sources masquées et inactives, tout réafficher, disposition" };
        options.Click += (_, _) => MenuOptions();
        iconeDisposition = new Shapes.Path { Stroke = P("Blanc"), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 20, Height = 20, Stretch = Stretch.None };
        disposition = new Button { Style = S("BtnPlat"), Width = 34, Height = 32, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), Content = iconeDisposition };
        disposition.Click += (_, _) => { C.VerticalLayout = !C.VerticalLayout; A.Apply(); Rafraichir(); };
        avancees = new Button
        {
            Style = S("BtnPlat"), Width = 34, Height = 32, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Propriétés audio avancées (volume, mono, balance, synchro, écoute, destinations)",
            Content = new Shapes.Path
            {
                Data = Geometry.Parse("M3,6 H17 M3,14 H17 M7,3 V9 M13,11 V17"), Stroke = P("Blanc"), StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 20, Height = 20, Stretch = Stretch.None,
            },
        };
        avancees.Click += (_, _) => OuvrirProprietes();
        var outils = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        outils.Children.Add(masquees);
        outils.Children.Add(inactives);
        outils.Children.Add(options);
        outils.Children.Add(disposition);
        outils.Children.Add(avancees);
        var titreMelangeur = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(outils, Dock.Right);
        titreMelangeur.Children.Add(outils);
        titreMelangeur.Children.Add(new TextBlock { Text = "MÉLANGEUR", Style = S("CarteTitre"), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(titreMelangeur, Dock.Top);
        gauche.Children.Add(titreMelangeur);
        var legende = new TextBlock { Text = Legende, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(legende, Dock.Bottom);
        gauche.Children.Add(legende);
        vide = new TextBlock { Text = Vide, FontSize = 14, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 12), LineHeight = 22 };
        var zone = new Grid();
        defilBandes = new ScrollViewer { Content = bandes, VerticalAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 6) };
        zone.Children.Add(defilBandes);
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
        // un micro branché ou débranché : bandes ajoutées, actives ou inactives (OBS 32.1)
        A.PeripheriquesChange += () => Dispatcher.BeginInvoke(new Action(Rafraichir));
        A.RaccourcisChange += () => Dispatcher.BeginInvoke(new Action(() => { foreach (var b in lignes) MajBande(b); }));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(() => { MajCartes(); Annoncer(); }));
        minuterie.Tick += (_, _) => Battre();
        Loaded += (_, _) => minuterie.Start();
        SizeChanged += (_, e) => Disposer(e.NewSize.Width);
        Unloaded += (_, _) => minuterie.Stop();
        Rafraichir();
        MajReduction();
        SurChangement();
    }

    /// <summary>Les bandes affichées, dans l'ordre (test automatique).</summary>
    public List<string> TestBandes => lignes.Select(l => l.S.Name).ToList();
    /// <summary>L'étiquette d'OBS 32.1 d'une bande affichée (test automatique).</summary>
    public string TestStatut(string nom) => lignes.FirstOrDefault(l => l.S.Name == nom)?.Statut.Text ?? "";
    /// <summary>Le mélangeur est en disposition horizontale (test automatique).</summary>
    public bool TestHorizontal => lignes.Count > 0 && lignes.All(l => l.Horizontale);

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
            // disposition verticale (par défaut depuis OBS 32.1) ou horizontale
            bool horizontal = !C.VerticalLayout;
            bandes.Orientation = horizontal ? Orientation.Vertical : Orientation.Horizontal;
            bandes.HorizontalAlignment = horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            defilBandes.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            defilBandes.VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            defilBandes.MaxHeight = horizontal ? 470 : double.PositiveInfinity;
            iconeDisposition.Data = Geometry.Parse(horizontal ? "M5,3 V17 M10,3 V17 M15,3 V17" : "M3,5 H17 M3,10 H17 M3,15 H17");
            disposition.ToolTip = horizontal ? "Disposition verticale (par défaut, comme OBS 32.1)" : "Disposition horizontale (comme l'ancien mélangeur d'OBS)";
            var visibles = C.Affichage(A.Inactive);
            foreach (var s in visibles)
            {
                var b = horizontal ? CreerLigne(s) : CreerBande(s);
                lignes.Add(b);
                bandes.Children.Add(b.Cadre);
            }
            vide.Text = C.Sources.Count == 0 ? Vide : "Toutes les sources sont masquées ou inactives : « Options » pour les afficher.";
            vide.Visibility = visibles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
        MajCompteurs();
        MajCartes();
        MajAlerte();
    }

    /// <summary>« 2 masquées » et « 1 inactive » dans la barre du mélangeur (un clic les affiche), comme OBS 32.1.</summary>
    void MajCompteurs()
    {
        int nm = C.Sources.Count(s => s.Hidden && !s.Pinned && s.Global.Length == 0);
        int ni = C.Sources.Count(s => !s.Hidden && !s.Pinned && s.Global.Length == 0 && A.Inactive(s));
        masquees.Content = nm == 1 ? "1 masquée" : $"{nm} masquées";
        masquees.Visibility = nm > 0 && !C.ShowHidden ? Visibility.Visible : Visibility.Collapsed;
        inactives.Content = ni == 1 ? "1 inactive" : $"{ni} inactives";
        inactives.Visibility = ni > 0 && !C.ShowInactive ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Une bande verticale (disposition par défaut d'OBS 32.1) : nom, étiquette, échelle et vumètres, fader, M, casque, filtres, destinations.</summary>
    Bande CreerBande(AudioSource s)
    {
        var b = new Bande { S = s };
        var pile = new StackPanel();
        b.Nom = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        b.Statut = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
        b.Genre = new TextBlock { FontSize = 11.5, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        b.Perif = new TextBlock { FontSize = 10.5, Foreground = P("Brume2"), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        b.Db = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Volume de la source" };
        pile.Children.Add(b.Nom);
        pile.Children.Add(b.Statut);
        pile.Children.Add(b.Genre);
        pile.Children.Add(b.Perif);
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
        b.Volume = new Slider { Style = S("CurseurVertical"), Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true, Value = AudioConfiguration.Bound(s.VolumeDb, -60, 6) };
        Grid.SetColumn(b.Volume, 5);
        zone.Children.Add(b.Volume);
        pile.Children.Add(zone);

        // M et casque, puis les filtres
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        b.Muet = BoutonM(40, 30);
        b.CasqueIcone = IconeCasque();
        b.Casque = new Button { Style = S("Btn"), Width = 40, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = b.CasqueIcone };
        boutons.Children.Add(b.Muet);
        boutons.Children.Add(b.Casque);
        pile.Children.Add(boutons);
        b.Filtres = new Button { Style = S("Btn"), Height = 28, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 6, 0, 0), FontSize = 12.5 };
        pile.Children.Add(b.Filtres);

        // les destinations : 1 à 5 (enregistrements), A à D (sorties MyVirtualSound)
        var pastilles = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        pile.Children.Add(pastilles);

        b.Cadre = new Border
        {
            Child = pile, Width = LargeurBande, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 10, 8, 10), Margin = new Thickness(0, 0, 10, 0),
            BorderThickness = new Thickness(1), BorderBrush = P("Nuit3"), Background = P("Encre"),
        };
        Brancher(b, pastilles);
        return b;
    }

    /// <summary>Une ligne de la disposition horizontale d'OBS : nom et étiquette, vumètres gauche et droite sur l'échelle, fader, volume, boutons et destinations.</summary>
    Bande CreerLigne(AudioSource s)
    {
        var b = new Bande { S = s, Horizontale = true };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tete = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        b.Nom = new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        b.Statut = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 1, 0, 0) };
        b.Genre = new TextBlock { FontSize = 11.5, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        b.Perif = new TextBlock { FontSize = 10.5, Foreground = P("Brume2"), TextTrimming = TextTrimming.CharacterEllipsis };
        tete.Children.Add(b.Nom);
        tete.Children.Add(b.Statut);
        tete.Children.Add(b.Genre);
        tete.Children.Add(b.Perif);
        g.Children.Add(tete);

        var milieu = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        b.G = new Vumetre(7) { ToolTip = "Gauche : niveau d'OBS (après volume et filtres), de −60 à 0 dBFS" };
        b.D = new Vumetre(7) { Margin = new Thickness(0, 3, 0, 0), ToolTip = "Droite : niveau d'OBS (après volume et filtres), de −60 à 0 dBFS" };
        milieu.Children.Add(b.G);
        milieu.Children.Add(b.D);
        milieu.Children.Add(new EchelleDb { Horizontale = true, Height = 15, Margin = new Thickness(0, 2, 0, 0) });
        b.Volume = new Slider { Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true, Value = AudioConfiguration.Bound(s.VolumeDb, -60, 6), Margin = new Thickness(0, 2, 0, 0) };
        milieu.Children.Add(b.Volume);
        Grid.SetColumn(milieu, 1);
        g.Children.Add(milieu);

        b.Db = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), ToolTip = "Volume de la source" };
        Grid.SetColumn(b.Db, 2);
        g.Children.Add(b.Db);

        var droite = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        b.Muet = BoutonM(36, 30);
        b.CasqueIcone = IconeCasque();
        b.Casque = new Button { Style = S("Btn"), Width = 36, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = b.CasqueIcone };
        b.Filtres = new Button { Style = S("Btn"), Height = 30, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 12.5 };
        var pastilles = new WrapPanel { MaxWidth = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        droite.Children.Add(b.Muet);
        droite.Children.Add(b.Casque);
        droite.Children.Add(b.Filtres);
        droite.Children.Add(pastilles);
        Grid.SetColumn(droite, 3);
        g.Children.Add(droite);

        b.Cadre = new Border
        {
            Child = g, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 10, 10), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1), BorderBrush = P("Nuit3"), Background = P("Encre"),
        };
        Brancher(b, pastilles);
        return b;
    }

    static Shapes.Path IconeCasque() => new()
    {
        Data = Geometry.Parse("M3,13 V10 A7,7 0 0 1 17,10 V13 M3,12 H6 V18 H3 Z M14,12 H17 V18 H14 Z"),
        StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Width = 20, Height = 20, Stretch = Stretch.None,
    };

    /// <summary>Ce que les deux dispositions partagent : fader, M, casque (un clic : écouter ou non, comme OBS 32.1), filtres, destinations et menus.</summary>
    void Brancher(Bande b, WrapPanel pastilles)
    {
        var s = b.S;
        b.Volume.ToolTip = "Volume (−60 à +6 dB) · double-clic : 0 dB · molette : ±1 dB";
        b.Volume.ValueChanged += (_, e) =>
        {
            if (remplissage) return;
            s.VolumeDb = Math.Round(e.NewValue, 1);
            b.Db.Text = TexteDb(s.VolumeDb);
            A.Apply();
        };
        b.Volume.MouseDoubleClick += (_, _) => b.Volume.Value = 0;
        b.Volume.PreviewMouseWheel += (_, e) => { b.Volume.Value = Math.Clamp(b.Volume.Value + Math.Sign(e.Delta), -60, 6); e.Handled = true; };
        b.Muet.Click += (_, _) => { s.Muted = !s.Muted; A.Apply(); MajBande(b); };
        b.Casque.Click += (_, _) =>
        {
            s.Monitoring = s.Monitoring == AudioMonitoring.Off ? AudioMonitoring.AndOutput : AudioMonitoring.Off;
            A.Apply();
            MajBande(b);
        };
        b.Casque.MouseRightButtonUp += (_, e) => { MenuEcoute(b); e.Handled = true; };
        b.Filtres.Click += (_, _) => OuvrirFiltres(s);
        for (int i = 0; i < C.Profiles.Count; i++) pastilles.Children.Add(Pastille(b, C.Profiles[i], (i + 1).ToString()));
        for (int i = 0; i < C.VirtualSounds.Count; i++) pastilles.Children.Add(Pastille(b, C.VirtualSounds[i], MarqueSortie(i)));
        if (pastilles.Children.Count == 0) pastilles.Visibility = Visibility.Collapsed;
        // comme OBS 32.1 : un clic sur le nom ouvre le menu de la source (épingler, masquer, renommer…)
        b.Nom.Cursor = Cursors.Hand;
        b.Nom.MouseLeftButtonUp += (_, e) => { MenuBande(b); e.Handled = true; };
        b.Cadre.ToolTip = "Clic sur le nom ou clic droit : épingler, masquer, renommer, écoute, filtres, propriétés avancées";
        b.Cadre.MouseRightButtonUp += (_, e) => { MenuBande(b); e.Handled = true; };
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
        b.Nom.ToolTip = s.Name + " · cliquer : épingler, masquer, renommer…";
        // l'étiquette d'OBS 32.1
        bool inactive = A.Inactive(s);
        var etat = AudioConfiguration.Etat(s, inactive);
        (string texte, string couleur, string aide) = etat switch
        {
            EtatMelangeur.Globale => ("GLOBALE", "Or", "Périphérique audio global (Réglages › Audio) : toujours dans le mélangeur, comme dans OBS"),
            EtatMelangeur.Epinglee => ("ÉPINGLÉE", "Or", "Épinglée : toujours affichée, à gauche"),
            EtatMelangeur.Masquee => ("MASQUÉE", "Brume2", "Masquée : affichée parce que « Afficher les sources masquées » est coché"),
            EtatMelangeur.Inactive => ("INACTIVE", "Brume2", s.Kind == AudioInputKind.Media ? "Inactive : fichier introuvable" : "Inactive : périphérique débranché (elle revient quand il est rebranché)"),
            _ => ("ACTIVE", "Vert", "Active"),
        };
        if (inactive && etat != EtatMelangeur.Inactive) { texte += " · INACTIVE"; aide += "\nInactive : " + (s.Kind == AudioInputKind.Media ? "fichier introuvable" : "périphérique débranché"); }
        b.Statut.Text = texte;
        b.Statut.Foreground = P(couleur);
        b.Statut.ToolTip = aide;
        b.Cadre.Opacity = inactive ? 0.62 : 1;
        b.Genre.Inlines.Clear();
        if (s.Kind == AudioInputKind.Microphone && s.Channel > 0)
        {
            // « Micro · entrée 2 » : le numéro en ambre, comme la maquette
            b.Genre.Inlines.Add(new System.Windows.Documents.Run("Micro · entrée "));
            b.Genre.Inlines.Add(new System.Windows.Documents.Run(s.Channel.ToString()) { Foreground = P("Or"), FontWeight = FontWeights.Bold });
        }
        else b.Genre.Text = Genre(s);
        b.Perif.Text = s.Kind == AudioInputKind.Media ? (inactive ? "fichier introuvable" : " ")
            : (s.DeviceId == "default" || s.DeviceName.Length == 0 ? "Windows par défaut" : NomCourt(s.DeviceName)) + (inactive ? " · débranché" : "");
        string probleme = s.Channel > 0 && !inactive ? A.StatutEntrees(s.DeviceId) : "";
        b.Genre.ToolTip = b.Perif.ToolTip = Description(s) + (probleme.Length > 0 ? "\n" + probleme : "");
        b.Perif.Foreground = P(probleme.Length > 0 || inactive ? "RougeTexte" : "Brume2");
        b.Db.Text = TexteDb(s.VolumeDb);
        string? par = CoupeePar(s);
        bool raccourci = A.CoupeeParRaccourci(s.Id);
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
        else if (raccourci)
        {
            Allume(b.Muet, false, "Direct");
            b.Muet.BorderBrush = P("OrVif");
            b.Muet.Foreground = P("Or");
            b.Muet.ToolTip = s.PushToTalk && s.PushToTalkKey.Length > 0
                ? $"Coupée : appuyer pour parler ({Touches.Afficher(s.PushToTalkKey)} relâchée)"
                : $"Coupée : appuyer pour couper le son ({Touches.Afficher(s.PushToMuteKey)} enfoncée)";
        }
        else
        {
            Allume(b.Muet, false, "Direct");
            b.Muet.ToolTip = "Sourdine (M) : couper le son de cette source";
        }
        bool ecoute = s.Monitoring != AudioMonitoring.Off;
        Allume(b.Casque, ecoute, "Or");
        b.CasqueIcone.Stroke = ecoute ? P("Encre") : P("Blanc");
        b.Casque.ToolTip = "Écoute au casque : " + NomEcoute(s.Monitoring).ToLower(Fr) + " · clic : écouter ou non · clic droit : écoute seule";
        b.Filtres.Content = s.Filters.Count == 0 ? "Filtres" : "Filtres " + s.Filters.Count;
        b.Filtres.ToolTip = s.Filters.Count == 0 ? "Ajouter un filtre (réduction du bruit, noise gate, compresseur, limiteur)" : string.Join(" → ", s.Filters.Select(f => AudioFilter.Nom(f.Kind) + (f.Enabled ? "" : " (coupé)")));
        b.G.Gris = b.D.Gris = s.Muted || par != null || raccourci;
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
            AudioInputKind.Microphone when s.Channel > 0 => $"Micro · entrée {s.Channel} seule de « {peripherique} »",
            AudioInputKind.Microphone => "Microphone · " + peripherique + " (toutes les entrées ensemble)",
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
        c.Symbole = new TextBlock { FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
        // en cours : bouton rouge plein avec ■ blanc ; arrêté : ● rouge sur fond blanc
        Allume(c.Bouton!, actif, "Direct");
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
        // une source ajoutée, retirée, épinglée ou masquée ailleurs (réglages, entrée branchée) : le mélangeur se refait
        var voulues = C.Affichage(A.Inactive);
        if (!voulues.SequenceEqual(lignes.Select(l => l.S)) || lignes.Any(l => l.Horizontale == C.VerticalLayout))
        {
            Rafraichir();
            return;
        }
        foreach (var b in lignes) MajBande(b);
        MajCompteurs();
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
        m.Items.Add(Article("Micros (une bande par entrée)…", AjouterMicros));
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
    internal static string NomCourt(string nom)
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

    /// <summary>
    /// « Micros (une bande par entrée) » : chaque micro de Windows avec ses entrées à cocher. Une carte son à 4 entrées donne 4 cases :
    /// chaque entrée cochée devient sa propre bande, comme une source dans OBS. « Toutes ensemble » garde l'ancienne bande stéréo.
    /// </summary>
    /// <summary>« Micros (une bande par entrée) » : aussi appelé par « + Micros » de la colonne de contrôle.</summary>
    public void AjouterMicros()
    {
        var liste = Peripheriques(AudioInputKind.Microphone);
        var w = new FenetreDialogue { Title = "Ajouter des micros", Width = 640, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        if (Fenetre is { IsLoaded: true } f) w.Owner = f;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var pile = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        pile.Children.Add(new TextBlock { Text = "Ajouter des micros", Style = S("Titre2") });
        pile.Children.Add(new TextBlock
        {
            Text = "Cochez chaque entrée où un micro est branché. Chaque entrée devient sa propre bande dans le mélangeur, avec son volume, ses filtres et son écoute, comme une source dans OBS.",
            Style = S("Muet"), FontSize = 14, LineHeight = 21, Margin = new Thickness(0, 6, 0, 4),
        });
        if (!A.EntreesSeparees)
            pile.Children.Add(new TextBlock { Text = "Le moteur OBS n'est pas démarré : seules « toutes les entrées ensemble » sont possibles pour l'instant.", Foreground = P("RougeTexte"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var cases = new List<(CheckBox Case, AudioDevice Peripherique, int Entree)>();
        var compte = new TextBlock { FontSize = 13.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        void Compter()
        {
            int n = cases.Count(c => c.Case.IsChecked == true && c.Case.IsEnabled);
            compte.Text = n == 0 ? "Les entrées grisées sont déjà dans le mélangeur." : $"{n} bande{(n > 1 ? "s" : "")} ser{(n > 1 ? "ont" : "a")} ajoutée{(n > 1 ? "s" : "")} au mélangeur.";
        }
        var defil = new StackPanel();
        foreach (var d in liste)
        {
            int n = ChurchAudioService.Entrees(d.Id);
            var carte = new StackPanel();
            var tete = new DockPanel();
            var nb = new TextBlock { Text = n == 0 ? "absent" : $"{n} entrée{(n > 1 ? "s" : "")}", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(nb, Dock.Right);
            tete.Children.Add(nb);
            tete.Children.Add(new TextBlock { Text = d.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
            carte.Children.Add(tete);
            var entrees = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            for (int k = 1; k <= n && k <= AudioConfiguration.MaxEntrees; k++)
            {
                var deja = C.Sources.FirstOrDefault(x => x.Kind == AudioInputKind.Microphone && x.DeviceId == d.Id && x.Channel == k);
                var c = new CheckBox
                {
                    Content = deja != null ? $"Entrée {k} · {deja.Name}" : $"Entrée {k}", IsChecked = deja != null, IsEnabled = deja == null && A.EntreesSeparees,
                    Margin = new Thickness(0, 0, 18, 8), FontSize = 14,
                };
                c.Click += (_, _) => Compter();
                cases.Add((c, d, k));
                entrees.Children.Add(c);
            }
            if (entrees.Children.Count > 0) carte.Children.Add(entrees);
            var dejaTout = C.Sources.FirstOrDefault(x => x.Kind == AudioInputKind.Microphone && x.DeviceId == d.Id && x.Channel == 0);
            var tout = new CheckBox
            {
                Content = dejaTout != null ? $"Toutes ensemble · {dejaTout.Name}" : "Toutes ensemble (une seule bande stéréo)", IsChecked = dejaTout != null, IsEnabled = dejaTout == null,
                FontSize = 13.5, Margin = new Thickness(0, entrees.Children.Count > 0 ? 2 : 10, 0, 0), Foreground = P("Encre2"),
            };
            tout.Click += (_, _) => Compter();
            cases.Add((tout, d, 0));
            carte.Children.Add(tout);
            defil.Children.Add(new Border { Style = S("Carte"), Padding = new Thickness(16, 14, 16, 12), Margin = new Thickness(0, 12, 0, 0), Child = carte });
        }
        pile.Children.Add(new ScrollViewer { Content = defil, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) });
        var bas = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var boutons = new StackPanel { Orientation = Orientation.Horizontal };
        var non = new Button { Style = S("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        var oui = new Button { Style = S("BtnOr"), Content = "Ajouter", IsDefault = true, MinWidth = 120, Height = 42, Margin = new Thickness(10, 0, 0, 0) };
        bool ok = false;
        non.Click += (_, _) => w.Close();
        oui.Click += (_, _) => { ok = true; w.Close(); };
        boutons.Children.Add(non);
        boutons.Children.Add(oui);
        DockPanel.SetDock(boutons, Dock.Right);
        bas.Children.Add(boutons);
        bas.Children.Add(compte);
        pile.Children.Add(bas);
        w.Content = pile;
        Compter();
        w.ShowDialog();
        if (!ok) return;
        var choix = cases.Where(c => c.Case.IsChecked == true && c.Case.IsEnabled).ToList();
        if (choix.Count == 0) return;
        foreach (var (_, d, k) in choix)
        {
            string nom = k > 0 ? NomLibre("Micro " + (C.Sources.Count(x => x.Kind == AudioInputKind.Microphone) + 1)) : NomLibre(d.Id == "default" ? "Micro" : NomCourt(d.Name));
            C.Sources.Add(new AudioSource { Name = nom, Kind = AudioInputKind.Microphone, DeviceId = d.Id, DeviceName = d.Id == "default" ? "" : d.Name, Channel = k });
        }
        A.Apply();
        Rafraichir();
        Deplier();
        Toast(choix.Count == 1
            ? "Micro ajouté. Clic droit sur sa bande pour le renommer (« Pasteur », « Chorale »…)."
            : $"{choix.Count} micros ajoutés, chacun sur sa bande. Clic droit pour les renommer (« Pasteur », « Chorale »…).");
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
        bool globale = s.Global.Length > 0;
        var m = new ContextMenu();
        // OBS 32.1 : épingler (toujours affichée, à gauche) et masquer
        if (!globale)
        {
            m.Items.Add(Article(s.Pinned ? "Désépingler" : "Épingler", () => { s.Pinned = !s.Pinned; if (s.Pinned) s.Hidden = false; Appliquer(); }));
            var masquer = Article(s.Hidden ? "Ne plus masquer" : "Masquer", () =>
            {
                s.Hidden = !s.Hidden;
                Appliquer();
                if (s.Hidden && !C.ShowHidden) Toast($"« {s.Name} » est masquée. « {C.Sources.Count(x => x.Hidden)} masquée(s) » ou Options › Tout réafficher pour la revoir.");
            }, !s.Pinned);
            if (s.Pinned) { masquer.ToolTip = "Une source épinglée est toujours affichée : désépinglez-la d'abord"; ToolTipService.SetShowOnDisabled(masquer, true); }
            m.Items.Add(masquer);
            m.Items.Add(new Separator());
        }
        m.Items.Add(Article("Renommer…", () =>
        {
            if (Dialogue.Demander(Fenetre, "Nom de la source :", s.Name, "Renommer") is { } n && n.Trim().Length > 0) { s.Name = n.Trim(); Appliquer(); }
        }));
        if (s.Kind == AudioInputKind.Microphone && !globale)
        {
            // une carte son à plusieurs entrées : cette bande écoute une entrée seule, ou toutes ensemble
            int n = ChurchAudioService.Entrees(s.DeviceId);
            var entree = new MenuItem { Header = s.Channel > 0 ? $"Entrée {s.Channel}" : "Entrée : toutes ensemble" };
            for (int k = 1; k <= Math.Max(n, s.Channel); k++)
            {
                int kk = k;
                var it = new MenuItem { Header = $"Entrée {k}" + (k > n ? " (absente)" : ""), IsCheckable = true, IsChecked = s.Channel == k, IsEnabled = A.EntreesSeparees };
                it.Click += (_, _) => { s.Channel = kk; Appliquer(); Toast($"« {s.Name} » écoute maintenant l'entrée {kk}."); };
                entree.Items.Add(it);
            }
            if (entree.Items.Count > 0) entree.Items.Add(new Separator());
            var toutes = new MenuItem { Header = "Toutes les entrées ensemble", IsCheckable = true, IsChecked = s.Channel == 0 };
            toutes.Click += (_, _) => { s.Channel = 0; Appliquer(); };
            entree.Items.Add(toutes);
            if (!A.EntreesSeparees) entree.ToolTip = "Le moteur OBS doit être démarré pour écouter une entrée seule.";
            m.Items.Add(entree);
        }
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
        var ecoute = new MenuItem { Header = "Écoute au casque" };
        foreach (var mode in new[] { AudioMonitoring.Off, AudioMonitoring.Only, AudioMonitoring.AndOutput })
        {
            var item = new MenuItem { Header = NomEcoute(mode), IsCheckable = true, IsChecked = s.Monitoring == mode };
            item.Click += (_, _) => { s.Monitoring = mode; A.Apply(); MajBande(b); };
            ecoute.Items.Add(item);
        }
        m.Items.Add(ecoute);
        m.Items.Add(Article(s.Filters.Count == 0 ? "Filtres…" : $"Filtres ({s.Filters.Count})…", () => OuvrirFiltres(s)));
        m.Items.Add(Article("Propriétés audio avancées…", OuvrirProprietes));
        m.Items.Add(new Separator());
        bool h = !C.VerticalLayout;
        m.Items.Add(Article(h ? "Vers le haut" : "Vers la gauche", () => Deplacer(s, -1), PeutDeplacer(s, -1)));
        m.Items.Add(Article(h ? "Vers le bas" : "Vers la droite", () => Deplacer(s, 1), PeutDeplacer(s, 1)));
        m.Items.Add(new Separator());
        if (globale)
            m.Items.Add(Article("Désactiver ce périphérique global", () =>
            {
                if (!Dialogue.Confirmer(Fenetre, $"Désactiver « {s.Name} » ? Comme dans OBS, il quitte le mélangeur ; Réglages › Audio › Périphériques audio globaux pour le remettre.", "Désactiver")) return;
                C.RegleGlobal(s.Global, null);
                Appliquer();
            }));
        else
            m.Items.Add(Article("Supprimer", () =>
            {
                if (!Dialogue.Confirmer(Fenetre, $"Retirer « {s.Name} » du mélangeur ? Elle sort aussi des enregistrements et des sorties.", "Retirer")) return;
                C.Retirer(s);
                Appliquer();
            }));
        Ouvrir(m, b.Nom.IsMouseOver ? b.Nom : b.Cadre, b.Nom.IsMouseOver ? PlacementMode.Bottom : PlacementMode.MousePoint);
    }

    void Appliquer()
    {
        A.Apply();
        Rafraichir();
    }

    /// <summary>Changer de place avec la voisine affichée du même groupe (les globales restent dans l'ordre des réglages, comme OBS).</summary>
    bool PeutDeplacer(AudioSource s, int sens)
    {
        if (s.Global.Length > 0) return false;
        var vus = C.Affichage(A.Inactive);
        int i = vus.IndexOf(s), j = i + sens;
        return i >= 0 && j >= 0 && j < vus.Count && C.Groupe(vus[j], A.Inactive(vus[j])) == C.Groupe(s, A.Inactive(s));
    }

    void Deplacer(AudioSource s, int sens)
    {
        if (!PeutDeplacer(s, sens)) return;
        var vus = C.Affichage(A.Inactive);
        var autre = vus[vus.IndexOf(s) + sens];
        int a = C.Sources.IndexOf(s), z = C.Sources.IndexOf(autre);
        (C.Sources[a], C.Sources[z]) = (C.Sources[z], C.Sources[a]);
        Appliquer();
    }

    /// <summary>Les options du mélangeur d'OBS 32.1.</summary>
    void MenuOptions()
    {
        var m = new ContextMenu();
        MenuItem Case(string titre, bool valeur, Action<bool> changer)
        {
            var it = new MenuItem { Header = titre, IsCheckable = true, IsChecked = valeur };
            it.Click += (_, _) => { changer(it.IsChecked); A.Apply(); Rafraichir(); };
            return it;
        }
        m.Items.Add(Case("Afficher les sources masquées", C.ShowHidden, v => C.ShowHidden = v));
        m.Items.Add(Case("Afficher les sources inactives", C.ShowInactive, v => C.ShowInactive = v));
        m.Items.Add(Case("Garder les sources masquées à droite", C.HiddenRight, v => C.HiddenRight = v));
        m.Items.Add(Case("Garder les sources inactives à droite", C.InactiveRight, v => C.InactiveRight = v));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Tout réafficher", () => { foreach (var x in C.Sources) x.Hidden = false; Appliquer(); }, C.Sources.Any(x => x.Hidden)));
        m.Items.Add(new Separator());
        m.Items.Add(Case("Disposition verticale", C.VerticalLayout, v => C.VerticalLayout = v));
        m.Items.Add(Article("Propriétés audio avancées…", OuvrirProprietes));
        m.Items.Add(Article("Réglages audio…", () => (Fenetre as MainWindow)?.OuvrirParametres()));
        Ouvrir(m, options, PlacementMode.Bottom);
    }

    /// <summary>Les propriétés audio avancées d'OBS : toutes les sources dans un tableau.</summary>
    public void OuvrirProprietes()
    {
        var w = new ProprietesAudioWindow();
        if (Fenetre is { IsLoaded: true } f) w.Owner = f;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        Rafraichir();
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

    /// <summary>Échelle couchée sous les vumètres de la disposition horizontale : −60 à gauche, 0 à droite.</summary>
    public bool Horizontale { get; init; }

    public EchelleDb()
    {
        // redessinée quand le thème change (les couleurs sont figées dans le dessin)
        Action redessiner = InvalidateVisual;
        Loaded += (_, _) => { Palette.Change -= redessiner; Palette.Change += redessiner; };
        Unloaded += (_, _) => Palette.Change -= redessiner;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, w = ActualWidth;
        if (h <= 0 || w <= 0) return;
        int pas = h / 12 >= 13 ? 5 : 10;
        // couleurs du thème du moment : un pinceau lié à la palette ne peut pas être figé
        var encre = new SolidColorBrush(Palette.Actuelle.Brume);
        encre.Freeze();
        var trait = new Pen(new SolidColorBrush(Palette.Actuelle.Brume2), 1);
        trait.Freeze();
        var police = new Typeface(TryFindResource("Ui") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Horizontale)
        {
            int pasH = w / 12 >= 26 ? 5 : 10;
            for (int db = 0; db >= -60; db -= 5)
            {
                double x = Math.Round(w * (db + 60) / 60.0) + 0.5;
                dc.DrawLine(trait, new Point(x, 0), new Point(x, db % 10 == 0 ? 4 : 2));
                if (db % pasH != 0) continue;
                var t = new FormattedText(db == 0 ? "0" : "−" + (-db).ToString(Fr), Fr, FlowDirection.LeftToRight, police, 9.5, encre, dpi);
                dc.DrawText(t, new Point(Math.Clamp(x - t.Width / 2, 0, Math.Max(0, w - t.Width)), 4));
            }
            return;
        }
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
    static Brush? couleursH, couleursV;
    readonly bool vertical;
    readonly Shapes.Rectangle barre;
    readonly Border crete;
    readonly RectangleGeometry coupe = new(new Rect(0, 0, 0, 0));
    double affiche = double.NegativeInfinity, pic = double.NegativeInfinity;
    long picDepuis, dernier;
    bool gris;

    /// <summary>Vitesse de retombée (dB par seconde) de Réglages › Audio › Vumètres, comme OBS : rapide 23,53, moyenne 11,76, lente 8,57.</summary>
    internal static double Retombee = 23.53;

    public Vumetre(double epaisseur, bool vertical = false)
    {
        this.vertical = vertical;
        if (vertical) Width = epaisseur; else Height = epaisseur;
        SnapsToDevicePixels = true;
        Children.Add(Zones(vertical, epaisseur));
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
        // montée immédiate, retombée à la vitesse choisie (comme OBS) ; la crête reste 1,5 s puis redescend
        long maintenant = Environment.TickCount64;
        double dt = dernier == 0 ? 0.05 : Math.Clamp((maintenant - dernier) / 1000.0, 0, 0.5);
        dernier = maintenant;
        affiche = dbfs >= affiche ? dbfs : Math.Max(dbfs, affiche - Retombee * dt);
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

    /// <summary>Le fond du vumètre : gris, et rosé dans les derniers dB (près de 0), aux couleurs du thème.</summary>
    static UIElement Zones(bool vertical, double epaisseur)
    {
        var g = new Grid { ClipToBounds = true };
        var fond = new Border { CornerRadius = new CornerRadius(epaisseur / 2) };
        fond.SetResourceReference(Border.BackgroundProperty, "VuFond");
        var crete = new Border();
        crete.SetResourceReference(Border.BackgroundProperty, "VuCrete");
        if (vertical)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86, GridUnitType.Star) });
            Grid.SetRowSpan(fond, 2);
            crete.CornerRadius = new CornerRadius(epaisseur / 2, epaisseur / 2, 0, 0);
        }
        else
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Star) });
            Grid.SetColumnSpan(fond, 2);
            Grid.SetColumn(crete, 1);
            crete.CornerRadius = new CornerRadius(0, epaisseur / 2, epaisseur / 2, 0);
        }
        g.Children.Add(fond);
        g.Children.Add(crete);
        return g;
    }
}
