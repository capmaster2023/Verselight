using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Le panneau Audio de Display, sous la grille (MyChurch 3.0). Tout le son passe par le moteur d'OBS (libobs) :
/// à gauche le mélangeur (une bande par source : vumètres, volume, sourdine, écoute, filtres, pistes 1 à 6),
/// à droite les filtres de la source choisie, l'écoute au casque, les 6 pistes et MyVirtualSound.
/// Le mélangeur démarre vide : l'utilisateur ajoute lui-même chaque source.
/// </summary>
public sealed class AudioPanel : Border
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    const string Vide = "Le mélangeur est vide : + Source audio pour ajouter un micro, une sortie ou un média.";
    const string Legende = "M = sourdine · casque = écoute · chiffres 1 à 6 = pistes où la source est envoyée · clic droit sur une bande : renommer, changer, mono, ordre, supprimer";

    /// <summary>Une bande du mélangeur.</summary>
    sealed class Ligne
    {
        public AudioSource S = null!;
        public Border Cadre = null!;
        public TextBlock Nom = null!, Appareil = null!, Db = null!;
        public Vumetre G = null!, D = null!;
        public Slider Volume = null!;
        public Button Muet = null!, Casque = null!, Filtres = null!;
        public Shapes.Path CasqueIcone = null!;
        public readonly Button[] Pistes = new Button[AudioConfiguration.Pistes];
    }

    readonly DispatcherTimer minuterie = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    readonly TextBlock moteur = new() { FontSize = 13 };
    readonly Border puce;
    readonly Button enregistrer, ajouter, peripheriques, reduire;
    readonly Run enregistrerPoint = new("● "), enregistrerTexte = new("Enregistrer");
    readonly Shapes.Path chevron;
    readonly Grid corps = new() { Margin = new Thickness(0, 16, 0, 0) };
    readonly StackPanel bandes = new();
    readonly TextBlock vide;
    readonly TextBlock filtresTitre = new() { Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel filtres = new();
    readonly ComboBox ecoute = new() { MinHeight = 40 };
    readonly TextBlock ecouteEtat = new() { FontSize = 13.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly List<(Vumetre Vu, TextBlock Nombre)> pistes = new();
    readonly CheckBox vsActif = new() { Content = "Activer", FontSize = 14 };
    readonly Vumetre vsVu = new(6) { Margin = new Thickness(0, 10, 0, 0) };
    readonly Slider vsVolume = new() { Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume de MyVirtualSound · double-clic : 0 dB" };
    readonly TextBlock vsDb = new() { FontSize = 13, MinWidth = 58, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly Button vsMuet;
    readonly TextBlock vsEtat = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly List<Ligne> lignes = new();
    string? choisie;
    bool remplissage, demarre, enregistrementActif;
    string dernierMessage;

    Window? Fenetre => Window.GetWindow(this);
    void Toast(string texte) => (Fenetre as MainWindow ?? Application.Current.MainWindow as MainWindow)?.Toast(texte);

    public AudioPanel()
    {
        Style = S("Carte");
        Padding = new Thickness(22, 18, 22, 20);
        dernierMessage = A.Recorder.Message;

        // ---------------- en-tête : Audio · Moteur OBS · Enregistrer · + Source audio · Périphériques
        moteur.Foreground = P("Brume");
        puce = new Border { Style = S("Puce"), Margin = new Thickness(14, 2, 0, 0), Child = moteur };
        var titre = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titre.Children.Add(new TextBlock { Text = "Audio", Style = S("TitreCarte"), VerticalAlignment = VerticalAlignment.Center });
        titre.Children.Add(puce);

        enregistrerPoint.Foreground = P("Direct");
        var texteEnregistrer = new TextBlock();
        texteEnregistrer.Inlines.Add(enregistrerPoint);
        texteEnregistrer.Inlines.Add(enregistrerTexte);
        enregistrer = new Button { Style = S("Btn"), Height = 42, Padding = new Thickness(14, 0, 14, 0), Content = texteEnregistrer };
        enregistrer.Click += (_, _) => Enregistrer();
        ajouter = new Button { Style = S("BtnOr"), Content = "+ Source audio", Height = 42, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ajouter un micro, le son d'une sortie, un fichier ou le son de la vidéo projetée" };
        ajouter.Click += (_, _) => MenuAjouter();
        peripheriques = new Button { Style = S("Btn"), Content = "Périphériques", Height = 42, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Paramètres : micro et sortie par défaut, écoute au casque, MyVirtualSound, enregistrement" };
        peripheriques.Click += (_, _) => { (Fenetre as MainWindow)?.OuvrirParametres(); Rafraichir(); };
        chevron = new Shapes.Path
        {
            Data = Geometry.Parse("M1,1 L6,6 L11,1"), Stroke = P("Brume"), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 12, Height = 8, Stretch = Stretch.None, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        reduire = new Button { Style = S("BtnPlat"), Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Content = chevron };
        reduire.Click += (_, _) => { C.PanelVisible = !C.PanelVisible; MajReduction(); A.Apply(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(enregistrer);
        actions.Children.Add(ajouter);
        actions.Children.Add(peripheriques);
        actions.Children.Add(reduire);
        var tete = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        tete.Children.Add(actions);
        tete.Children.Add(titre);

        // ---------------- à gauche : le mélangeur
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        var gauche = new DockPanel();
        var titreMelangeur = Etiquette("MÉLANGEUR", 0);
        DockPanel.SetDock(titreMelangeur, Dock.Top);
        gauche.Children.Add(titreMelangeur);
        var legende = new TextBlock { Text = Legende, FontSize = 13, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(legende, Dock.Bottom);
        gauche.Children.Add(legende);
        vide = new TextBlock { Text = Vide, FontSize = 14, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 12), LineHeight = 22 };
        var zone = new Grid();
        zone.Children.Add(new ScrollViewer { Content = bandes, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) });
        zone.Children.Add(vide);
        gauche.Children.Add(zone);
        corps.Children.Add(gauche);

        // ---------------- à droite : filtres de la source choisie, écoute, pistes, MyVirtualSound
        var droite = new StackPanel();
        Grid.SetColumn(droite, 2);
        filtresTitre.Style = S("CarteTitre");
        droite.Children.Add(filtresTitre);
        droite.Children.Add(filtres);

        droite.Children.Add(Etiquette("ÉCOUTE AU CASQUE", 18));
        droite.Children.Add(new TextBlock { Text = "Périphérique d'écoute", FontSize = 13, Foreground = P("Brume"), Margin = new Thickness(0, 0, 0, 6) });
        ecoute.ToolTip = "Où OBS fait entendre les sources en écoute (casque de la régie)";
        ecoute.SelectionChanged += (_, _) =>
        {
            if (remplissage || ecoute.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            C.MonitorDeviceId = id;
            C.MonitorDeviceName = id == "default" ? "" : item.Content as string ?? "";
            A.Apply();
        };
        droite.Children.Add(ecoute);
        droite.Children.Add(ecouteEtat);

        droite.Children.Add(Etiquette("PISTES INDÉPENDANTES", 18));
        for (int i = 0; i < AudioConfiguration.Pistes; i++)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            g.Children.Add(new TextBlock { Text = "Piste " + (i + 1), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var vu = new Vumetre(6) { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(vu, 1);
            g.Children.Add(vu);
            var nombre = new TextBlock { FontSize = 13, Foreground = P("Brume"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nombre, 2);
            g.Children.Add(nombre);
            pistes.Add((vu, nombre));
            droite.Children.Add(g);
        }

        droite.Children.Add(Etiquette("MYVIRTUALSOUND", 14));
        var vsTete = new DockPanel();
        var vsReglages = new Button { Style = S("BtnLien"), Content = "Réglages…", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Sortie, piste envoyée, installation de VB-CABLE" };
        vsReglages.Click += (_, _) => { (Fenetre as MainWindow)?.OuvrirParametres(); Rafraichir(); };
        DockPanel.SetDock(vsReglages, Dock.Right);
        vsTete.Children.Add(vsReglages);
        vsActif.ToolTip = "Envoie le mix d'une piste vers un câble audio virtuel : Zoom ou un navigateur le reçoivent comme un micro";
        vsActif.Click += (_, _) => { C.VirtualSound.Enabled = vsActif.IsChecked == true; A.Apply(); MajVs(); };
        vsTete.Children.Add(vsActif);
        droite.Children.Add(vsTete);
        droite.Children.Add(vsVu);
        var vsLigne = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        vsLigne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vsLigne.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        vsLigne.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        vsVolume.Value = AudioConfiguration.Bound(C.VirtualSound.VolumeDb, -60, 6);
        vsVolume.ValueChanged += (_, e) =>
        {
            if (remplissage) return;
            C.VirtualSound.VolumeDb = Math.Round(e.NewValue, 1);
            vsDb.Text = TexteDb(C.VirtualSound.VolumeDb);
            A.Apply();
        };
        vsVolume.MouseDoubleClick += (_, _) => vsVolume.Value = 0;
        vsLigne.Children.Add(vsVolume);
        Grid.SetColumn(vsDb, 1);
        vsLigne.Children.Add(vsDb);
        vsMuet = BoutonM(34, 30);
        vsMuet.Margin = new Thickness(10, 0, 0, 0);
        vsMuet.Click += (_, _) => { C.VirtualSound.Muted = !C.VirtualSound.Muted; A.Apply(); MajVs(); };
        Grid.SetColumn(vsMuet, 2);
        vsLigne.Children.Add(vsMuet);
        droite.Children.Add(vsLigne);
        vsEtat.Foreground = P("Brume");
        droite.Children.Add(vsEtat);
        corps.Children.Add(droite);

        var racine = new DockPanel();
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        racine.Children.Add(corps);
        Child = racine;

        // ---------------- vie du panneau
        A.Changed += () => Dispatcher.BeginInvoke(new Action(SurChangement));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(MajEnregistrement));
        minuterie.Tick += (_, _) => Battre();
        Loaded += (_, _) => minuterie.Start();
        Unloaded += (_, _) => minuterie.Stop();
        Rafraichir();
        MajReduction();
        MajEnregistrement();
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

    static Button BoutonM(double largeur, double hauteur) => new()
    {
        Style = S("Btn"), Width = largeur, Height = hauteur, Padding = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 14, Content = "M",
        ToolTip = "Sourdine (M) : couper ou rétablir le son",
    };

    /// <summary>Reconstruit tout le panneau à partir des réglages (après un ajout, un retrait, un changement d'ordre…).</summary>
    public void Rafraichir()
    {
        remplissage = true;
        try
        {
            if (choisie == null || C.Sources.All(s => s.Id != choisie)) choisie = C.Sources.FirstOrDefault()?.Id;
            bandes.Children.Clear();
            lignes.Clear();
            foreach (var s in C.Sources)
            {
                var l = Creer(s);
                lignes.Add(l);
                bandes.Children.Add(l.Cadre);
                MajLigne(l);
            }
            vide.Visibility = C.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            vsActif.IsChecked = C.VirtualSound.Enabled;
            vsVolume.Value = AudioConfiguration.Bound(C.VirtualSound.VolumeDb, -60, 6);
            RemplirEcoute();
        }
        finally { remplissage = false; }
        RemplirFiltres();
        MajPistes();
        MajVs();
    }

    Ligne Creer(AudioSource s)
    {
        var l = new Ligne { S = s };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // nom et périphérique
        var noms = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        l.Nom = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        l.Appareil = new TextBlock { FontSize = 12.5, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        noms.Children.Add(l.Nom);
        noms.Children.Add(l.Appareil);
        g.Children.Add(noms);

        // vumètres gauche / droite, puis le volume
        var milieu = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
        l.G = new Vumetre(6) { ToolTip = "Gauche : niveau d'OBS (après volume et filtres), de −60 à 0 dBFS" };
        l.D = new Vumetre(6) { Margin = new Thickness(0, 4, 0, 0), ToolTip = "Droite" };
        milieu.Children.Add(l.G);
        milieu.Children.Add(l.D);
        var volume = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        volume.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volume.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        l.Volume = new Slider
        {
            Minimum = -60, Maximum = 6, SmallChange = 0.5, LargeChange = 3, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center,
            Value = AudioConfiguration.Bound(s.VolumeDb, -60, 6), ToolTip = "Volume (−60 à +6 dB) · double-clic : 0 dB",
        };
        l.Volume.ValueChanged += (_, e) =>
        {
            if (remplissage) return;
            s.VolumeDb = Math.Round(e.NewValue, 1);
            l.Db.Text = TexteDb(s.VolumeDb);
            A.Apply();
        };
        l.Volume.MouseDoubleClick += (_, _) => l.Volume.Value = 0;
        volume.Children.Add(l.Volume);
        l.Db = new TextBlock { FontSize = 13.5, MinWidth = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(l.Db, 1);
        volume.Children.Add(l.Db);
        milieu.Children.Add(volume);
        Grid.SetColumn(milieu, 1);
        g.Children.Add(milieu);

        // M, casque, filtres ; puis les pistes 1 à 6
        var boutons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var haut = new StackPanel { Orientation = Orientation.Horizontal };
        l.Muet = BoutonM(40, 32);
        l.Muet.Click += (_, _) => { s.Muted = !s.Muted; A.Apply(); MajLigne(l); };
        l.CasqueIcone = new Shapes.Path
        {
            Data = Geometry.Parse("M3,13 V10 A7,7 0 0 1 17,10 V13 M3,12 H6 V18 H3 Z M14,12 H17 V18 H14 Z"),
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 20, Height = 20, Stretch = Stretch.None,
        };
        l.Casque = new Button { Style = S("Btn"), Width = 40, Height = 32, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = l.CasqueIcone };
        l.Casque.Click += (_, _) => MenuEcoute(l);
        l.Filtres = new Button { Style = S("Btn"), Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 13.5 };
        l.Filtres.Click += (_, _) => OuvrirFiltres(s);
        haut.Children.Add(l.Muet);
        haut.Children.Add(l.Casque);
        haut.Children.Add(l.Filtres);
        boutons.Children.Add(haut);
        var bas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < AudioConfiguration.Pistes; i++)
        {
            int bit = 1 << i;
            var b = new Button
            {
                Style = S("Btn"), Width = 28, Height = 28, Padding = new Thickness(0), FontSize = 12.5, Content = (i + 1).ToString(),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
            };
            b.Click += (_, _) => { s.Tracks ^= bit; A.Apply(); MajLigne(l); MajPistes(); };
            l.Pistes[i] = b;
            bas.Children.Add(b);
        }
        boutons.Children.Add(bas);
        Grid.SetColumn(boutons, 2);
        g.Children.Add(boutons);

        l.Cadre = new Border
        {
            Child = g, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Clic : choisir (ses filtres sont à droite) · clic droit : renommer, changer, mono, ordre, supprimer",
        };
        // choisir la bande sans gêner les boutons ni le curseur
        l.Cadre.PreviewMouseLeftButtonDown += (_, _) => { if (choisie != s.Id) Choisir(s.Id); };
        l.Cadre.MouseRightButtonUp += (_, e) => { Choisir(s.Id); MenuLigne(l); e.Handled = true; };
        return l;
    }

    void Choisir(string id)
    {
        choisie = id;
        foreach (var l in lignes) MajCadre(l);
        RemplirFiltres();
        MajEcouteEtat();
    }

    // ------------------------------------------------------------ mise à jour des bandes
    void MajCadre(Ligne l)
    {
        bool sel = l.S.Id == choisie;
        l.Cadre.BorderBrush = P(sel ? "Or" : "Nuit3");
        l.Cadre.BorderThickness = new Thickness(sel ? 1.5 : 1);
        l.Cadre.Background = P(sel ? "Nuit2" : "Encre");
    }

    void MajLigne(Ligne l)
    {
        var s = l.S;
        MajCadre(l);
        l.Nom.Text = s.Name;
        l.Appareil.Text = Description(s);
        l.Appareil.ToolTip = l.Appareil.Text;
        l.Db.Text = TexteDb(s.VolumeDb);
        Allume(l.Muet, s.Muted, "Direct");
        l.Muet.ToolTip = s.Muted ? "Sourdine active : la source ne va plus dans les pistes (M pour rétablir)" : "Sourdine (M) : couper le son de cette source";
        bool ecoute = s.Monitoring != AudioMonitoring.Off;
        Allume(l.Casque, ecoute, "Or");
        l.CasqueIcone.Stroke = ecoute ? P("Encre") : P("Blanc");
        l.Casque.ToolTip = "Écoute au casque : " + NomEcoute(s.Monitoring).ToLower(Fr) + " (cliquer pour changer)";
        l.Filtres.Content = "Filtres " + s.Filters.Count;
        l.Filtres.ToolTip = s.Filters.Count == 0 ? "Ajouter un filtre (réduction du bruit, noise gate, compresseur, limiteur)" : string.Join(" → ", s.Filters.Select(f => AudioFilter.Nom(f.Kind) + (f.Enabled ? "" : " (coupé)")));
        bool seule = s.Monitoring == AudioMonitoring.Only;
        for (int i = 0; i < l.Pistes.Length; i++)
        {
            bool dans = (s.Tracks & (1 << i)) != 0;
            Allume(l.Pistes[i], dans, "Blanc");
            l.Pistes[i].Opacity = seule ? 0.4 : 1;
            l.Pistes[i].ToolTip = seule ? "Écoute seule : la source ne va dans aucune piste"
                : $"Piste {i + 1} : la source {(dans ? "y est envoyée" : "n'y est pas envoyée")} (cliquer pour changer)";
        }
        bool gris = s.Muted;
        l.G.Gris = l.D.Gris = gris;
    }

    /// <summary>Bouton « allumé » : fond plein (rouge pour la sourdine, bleu pour l'écoute, noir pour une piste) et texte blanc.</summary>
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

    // ------------------------------------------------------------ colonne de droite
    AudioSource? Choisie() => C.Sources.FirstOrDefault(s => s.Id == choisie);

    void RemplirFiltres()
    {
        filtres.Children.Clear();
        var s = Choisie();
        filtresTitre.Text = s == null ? "FILTRES" : "FILTRES · " + s.Name.ToUpper(Fr);
        if (s == null)
        {
            filtres.Children.Add(new TextBlock { Text = "Choisissez une source du mélangeur pour voir ses filtres.", FontSize = 13, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap });
            MajEcouteEtat();
            return;
        }
        foreach (var f in s.Filters)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textes.Children.Add(new TextBlock { Text = AudioFilter.Nom(f.Kind), FontSize = 14.5, FontWeight = FontWeights.SemiBold });
            textes.Children.Add(new TextBlock { Text = f.Resume(), FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(textes);
            var bascule = new CheckBox { Style = S("Interrupteur"), IsChecked = f.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Activer ou couper ce filtre (en direct)" };
            bascule.Click += (_, _) =>
            {
                f.Enabled = bascule.IsChecked == true;
                A.Apply();
                if (lignes.FirstOrDefault(x => x.S == s) is { } l) MajLigne(l);
            };
            Grid.SetColumn(bascule, 1);
            g.Children.Add(bascule);
            filtres.Children.Add(new Border { Child = g, BorderBrush = P("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Background = P("Encre") });
        }
        if (s.Filters.Count == 0)
            filtres.Children.Add(new TextBlock { Text = "Aucun filtre sur cette source.", FontSize = 13, Foreground = P("Brume"), Margin = new Thickness(0, 0, 0, 8) });
        var gerer = new Button { Style = S("BtnPetit"), Content = s.Filters.Count == 0 ? "+ Ajouter un filtre…" : "Modifier les filtres…", HorizontalAlignment = HorizontalAlignment.Left };
        gerer.Click += (_, _) => OuvrirFiltres(s);
        filtres.Children.Add(gerer);
        MajEcouteEtat();
    }

    void MajEcouteEtat()
    {
        var s = Choisie();
        if (s == null) { ecouteEtat.Text = ""; ecouteEtat.Visibility = Visibility.Collapsed; return; }
        ecouteEtat.Visibility = Visibility.Visible;
        string mode = NomEcoute(s.Monitoring);
        ecouteEtat.Text = s.Name + " : " + char.ToLower(mode[0], Fr) + mode[1..];
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

    void MajPistes()
    {
        for (int i = 0; i < pistes.Count; i++)
        {
            int bit = 1 << i;
            int n = C.Sources.Count(s => (s.Tracks & bit) != 0 && s.Monitoring != AudioMonitoring.Only);
            pistes[i].Nombre.Text = n + (n > 1 ? " sources" : " source");
        }
    }

    void MajVs()
    {
        var v = C.VirtualSound;
        bool avant = remplissage;
        remplissage = true;
        try { vsActif.IsChecked = v.Enabled; }
        finally { remplissage = avant; }
        vsDb.Text = TexteDb(v.VolumeDb);
        Allume(vsMuet, v.Muted, "Direct");
        vsVu.Gris = v.Muted || !v.Enabled;
        vsEtat.Text = A.VirtualSound.Status;
    }

    void MajReduction()
    {
        bool ouvert = C.PanelVisible;
        corps.Visibility = ouvert ? Visibility.Visible : Visibility.Collapsed;
        chevron.RenderTransform = new RotateTransform(ouvert ? 180 : 0);
        reduire.ToolTip = ouvert ? "Réduire le panneau Audio" : "Afficher le panneau Audio";
    }

    /// <summary>Le moteur a changé (démarrage, réglages appliqués, vidéo projetée) : textes seulement, sans reconstruire les bandes.</summary>
    void SurChangement()
    {
        moteur.Text = A.Running ? "Moteur OBS · libobs " + A.Version + (A.TracksActive ? "" : " · pistes indisponibles") : A.Diagnostic;
        puce.ToolTip = A.Diagnostic;
        if (A.Running != demarre)
        {
            demarre = A.Running;
            RemplirEcoute();
        }
        foreach (var l in lignes)
        {
            l.Appareil.Text = Description(l.S);
            l.Appareil.ToolTip = l.Appareil.Text;
        }
        MajVs();
    }

    // ------------------------------------------------------------ vumètres et enregistrement (20 fois par seconde)
    void Battre()
    {
        if (!IsVisible) return;
        MajEnregistrement();
        if (!C.PanelVisible) return;
        foreach (var l in lignes)
        {
            var (g, d) = A.Level(l.S.Id);
            l.G.Niveau(g);
            l.D.Niveau(d);
        }
        for (int i = 0; i < pistes.Count; i++) pistes[i].Vu.Niveau(A.TrackLevel(i));
        vsVu.Niveau(A.VirtualSound.Level);
    }

    void MajEnregistrement()
    {
        var r = A.Recorder;
        bool actif = r.Active;
        if (actif != enregistrementActif)
        {
            enregistrementActif = actif;
            enregistrer.Style = S(actif ? "BtnRouge" : "Btn");
            enregistrerPoint.Text = actif ? "■ " : "● ";
        }
        enregistrerTexte.Text = actif ? "Arrêter · " + r.Duration.ToString(@"hh\:mm\:ss") : r.Busy ? "Patientez…" : "Enregistrer";
        enregistrer.IsEnabled = !r.Busy;
        var p = C.Profiles.FirstOrDefault();
        string quoi = p == null ? "" : $"Piste {p.Track} en {p.Format.ToUpperInvariant()} dans {p.Directory}";
        enregistrer.ToolTip = (actif ? "Arrêter l'enregistrement" : "Enregistrer · " + quoi) + (r.Message.Length > 0 ? "\n" + r.Message : "");
        if (r.Message != dernierMessage)
        {
            dernierMessage = r.Message;
            if (r.State == ChurchRecordingState.Error) Toast(r.Message);
            else if (r.State == ChurchRecordingState.Ready && r.LastFile != null && r.Message.StartsWith("Terminé", StringComparison.Ordinal))
                Toast("Enregistrement terminé : " + Path.GetFileName(r.LastFile));
        }
    }

    void Enregistrer()
    {
        if (C.Profiles.Count == 0) C.Normalize();
        var p = C.Profiles[0];
        if (!A.Recorder.Active && C.Sources.All(s => (s.Tracks & (1 << (p.Track - 1))) == 0 || s.Monitoring == AudioMonitoring.Only))
        {
            Toast($"Rien à enregistrer : aucune source n'est envoyée dans la piste {p.Track}. Allumez le chiffre {p.Track} sur une bande.");
            return;
        }
        _ = A.Recorder.ToggleAsync(p);
        MajEnregistrement();
    }

    // ------------------------------------------------------------ ajouter, changer, retirer une source
    static MenuItem Article(string titre, Action action)
    {
        var m = new MenuItem { Header = titre };
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
        choisie = s.Id;
        A.Apply();
        Rafraichir();
        Deplier();
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
        Ajouter(new AudioSource { Name = NomLibre(nom), Kind = kind, DeviceId = d.Id, DeviceName = d.Id == "default" ? "" : d.Name, Tracks = 1 });
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
        Ajouter(new AudioSource { Name = NomLibre(Path.GetFileNameWithoutExtension(chemin)), Kind = AudioInputKind.Media, MediaPath = chemin, Tracks = 1 });
    }

    void AjouterProjection()
    {
        if (C.Sources.FirstOrDefault(s => s.Kind == AudioInputKind.Media && s.FollowProjection) is { } deja)
        {
            Choisir(deja.Id);
            Toast($"« {deja.Name} » joue déjà le son de la vidéo projetée.");
            return;
        }
        Ajouter(new AudioSource { Name = NomLibre("Vidéo Display"), Kind = AudioInputKind.Media, FollowProjection = true, Tracks = 1 });
        Toast("Le son des vidéos projetées depuis Display passe maintenant par le mélangeur.");
    }

    void MenuEcoute(Ligne l)
    {
        var s = l.S;
        Choisir(s.Id);
        var m = new ContextMenu();
        foreach (var mode in new[] { AudioMonitoring.Off, AudioMonitoring.Only, AudioMonitoring.AndOutput })
        {
            var item = new MenuItem { Header = NomEcoute(mode), IsCheckable = true, IsChecked = s.Monitoring == mode };
            item.Click += (_, _) =>
            {
                s.Monitoring = mode;
                A.Apply();
                MajLigne(l);
                MajPistes();
                MajEcouteEtat();
            };
            m.Items.Add(item);
        }
        Ouvrir(m, l.Casque, PlacementMode.Bottom);
    }

    void MenuLigne(Ligne l)
    {
        var s = l.S;
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
        var monter = Article("Monter", () => Deplacer(s, -1));
        monter.IsEnabled = i > 0;
        m.Items.Add(monter);
        var descendre = Article("Descendre", () => Deplacer(s, 1));
        descendre.IsEnabled = i >= 0 && i < C.Sources.Count - 1;
        m.Items.Add(descendre);
        m.Items.Add(new Separator());
        m.Items.Add(Article("Supprimer", () =>
        {
            if (!Dialogue.Confirmer(Fenetre, $"Retirer « {s.Name} » du mélangeur ?", "Retirer")) return;
            C.Sources.Remove(s);
            if (choisie == s.Id) choisie = null;
            Appliquer();
        }));
        Ouvrir(m, l.Cadre, PlacementMode.MousePoint);
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
        var d = ChoisirPeripherique(Fenetre, micro ? "Changer de microphone" : "Changer de sortie audio", "La source garde son nom, son volume, ses filtres et ses pistes.", Peripheriques(s.Kind), s.DeviceId);
        if (d == null) return;
        s.DeviceId = d.Id;
        s.DeviceName = d.Id == "default" ? "" : d.Name;
        Appliquer();
    }

    void OuvrirFiltres(AudioSource s)
    {
        Choisir(s.Id);
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
/// Un vumètre horizontal de −60 à 0 dBFS : vert, puis jaune vers −20 dB, rouge près de 0 dB (comme OBS), avec une retombée douce.
/// Grisé quand la source est en sourdine (le signal reste visible, comme dans OBS).
/// </summary>
internal sealed class Vumetre : Grid
{
    static readonly Brush Couleurs = Degrade();
    static readonly Brush Fond = FondZones();
    readonly Shapes.Rectangle barre;
    readonly RectangleGeometry coupe = new(new Rect(0, 0, 0, 0));
    double affiche = double.NegativeInfinity;
    bool gris;

    public Vumetre(double hauteur)
    {
        Height = hauteur;
        SnapsToDevicePixels = true;
        Children.Add(new Border { Background = Fond, CornerRadius = new CornerRadius(hauteur / 2) });
        barre = new Shapes.Rectangle { Fill = Couleurs, RadiusX = hauteur / 2, RadiusY = hauteur / 2, Clip = coupe };
        Children.Add(barre);
    }

    public bool Gris
    {
        get => gris;
        set { gris = value; barre.Opacity = value ? 0.32 : 1; }
    }

    /// <summary>Nouveau niveau en dBFS (−∞ = silence).</summary>
    public void Niveau(double dbfs)
    {
        if (double.IsNaN(dbfs) || dbfs < -100) dbfs = -100;
        // retombée d'environ 24 dB par seconde, montée immédiate
        affiche = dbfs >= affiche ? dbfs : Math.Max(dbfs, affiche - 1.2);
        double f = Math.Clamp((affiche + 60) / 60, 0, 1);
        coupe.Rect = new Rect(0, 0, ActualWidth * f, Math.Max(0, ActualHeight));
    }

    static Brush Degrade()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x9E, 0x5B), 0.64)); // −21,6 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.8));  // −12 dB
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xB0, 0x2E), 0.86));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xD9, 0x2D, 0x20), 0.96));  // −2,4 dB
        b.Freeze();
        return b;
    }

    static Brush FondZones()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE9, 0xEE), 0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE9, 0xEE), 0.86));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xDC, 0xD9), 0.88));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xDC, 0xD9), 1));
        b.Freeze();
        return b;
    }
}
