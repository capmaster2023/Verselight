using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyVerse.Ui;

/// <summary>
/// La page Réglages (MyChurch 3.1) : un sommaire à gauche (Apparence, Audio, MyVirtualSound, Enregistrements, Projection,
/// Téléphone, Mise à jour, Transfert, Journal, Licences) et les sections en cartes à droite. Le sommaire suit le défilement.
/// </summary>
public sealed class ReglagesPage : Grid
{
    readonly ScrollViewer defil;
    readonly StackPanel pile = new() { MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
    readonly List<(string Id, RadioButton Bouton, FrameworkElement Section)> sections = new();
    readonly Audio.AudioSettingsPanel audio = new();
    readonly StackPanel ecrans = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly Button clair, sombre;
    bool defilement;

    public OutilsPanel Outils { get; } = new();

    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    MainWindow? Principale => Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;

    public ReglagesPage()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        ColumnDefinitions.Add(new ColumnDefinition());

        // ---------------- sections
        (clair, sombre) = (Apercu(false), Apercu(true));
        var themes = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        themes.Children.Add(clair);
        themes.Children.Add(sombre);
        var apparence = Section("Apparence", "Le thème clair est celui par défaut. Le thème sombre repose les yeux dans une salle obscure ; il s'applique tout de suite, à toutes les fenêtres. La projection garde son propre habillage.", themes);

        var projection = Section("Projection", "L'écran choisi reçoit la Bible, les chants et Display, comme le projecteur plein écran d'OBS. MyVirtualCam reçoit toujours la même image en 720p.", ecrans);

        var jumeler = new Button { Style = S("Btn"), Content = "Jumeler un téléphone (code QR)…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        jumeler.Click += (_, _) => { if (Principale is { } m) new TelephoneWindow { Owner = m }.ShowDialog(); };
        var telephone = Section("Téléphone", "La télécommande Android passe les diapositives et choisit les chants. Le PC et le téléphone doivent être sur le même réseau.", jumeler);

        // l'ordre de la page : Licences (dernière section de l'audio) passe tout en bas
        audio.Children.Remove(audio.Licences);
        Outils.Children_Retirer(out var maj, out var transfert, out var journal);
        Ajouter("apparence", "Apparence", apparence);
        Ajouter("audio", "Audio", audio.Peripheriques, false);
        Ajouter("myvirtualsound", "MyVirtualSound", audio.VirtualSound, false);
        Ajouter("enregistrements", "Enregistrements", audio.Enregistrements, false);
        pile.Children.Add(audio);
        Ajouter("projection", "Projection", projection);
        Ajouter("telephone", "Téléphone", telephone);
        Ajouter("maj", "Mise à jour", maj);
        Ajouter("transfert", "Transfert", transfert);
        Ajouter("journal", "Journal", journal);
        Ajouter("licences", "Licences", audio.Licences);

        // ---------------- sommaire et défilement
        var sommaire = new StackPanel { Margin = new Thickness(20, 22, 14, 18) };
        foreach (var (_, b, _) in sections) sommaire.Children.Add(b);
        Children.Add(new Border { BorderBrush = P("Nuit3"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sommaire });
        defil = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(28, 22, 28, 40) };
        SetColumn(defil, 1);
        Children.Add(defil);
        defil.ScrollChanged += (_, _) => Suivre();
        sections[0].Bouton.IsChecked = true;

        Palette.Change += MajApparence;
        IsVisibleChanged += (_, _) => { if (IsVisible) { MajEcrans(); MajApparence(); Outils.MajJournal(); } };
        MajApparence();
    }

    void Ajouter(string id, string titre, FrameworkElement section, bool dansLaPile = true)
    {
        var b = new RadioButton { Style = S("Sommaire"), Content = titre, GroupName = "Reglages" + GetHashCode() };
        b.Click += (_, _) => Aller(id, false);
        sections.Add((id, b, section));
        if (dansLaPile) pile.Children.Add(section);
    }

    /// <summary>Fait défiler jusqu'à une section (« apparence », « audio », « maj »…) ; « maj » avec chercher : recherche une mise à jour.</summary>
    public async void Aller(string id, bool chercher)
    {
        var s = sections.FirstOrDefault(x => x.Id == id);
        if (s.Section == null) return;
        s.Bouton.IsChecked = true;
        UpdateLayout();
        try
        {
            var haut = s.Section.TransformToAncestor(pile).Transform(new Point(0, 0)).Y;
            defilement = true;
            defil.ScrollToVerticalOffset(Math.Max(0, haut - 4));
        }
        catch (InvalidOperationException) { }
        await Task.Delay(120);
        defilement = false;
        if (id == "maj" && chercher) await Outils.ChercherMaintenant();
    }

    /// <summary>Le sommaire montre la section en haut de l'écran.</summary>
    void Suivre()
    {
        if (defilement || !IsVisible) return;
        var actuelle = sections[0];
        foreach (var s in sections)
        {
            try
            {
                if (!s.Section.IsVisible) continue;
                if (s.Section.TransformToAncestor(defil).Transform(new Point(0, 0)).Y <= 80) actuelle = s;
            }
            catch (InvalidOperationException) { }
        }
        // tout en bas : la dernière section
        if (defil.VerticalOffset >= defil.ScrollableHeight - 2 && defil.ScrollableHeight > 0) actuelle = sections[^1];
        actuelle.Bouton.IsChecked = true;
    }

    // ------------------------------------------------------------ sections
    static Border Section(string titre, string texte, UIElement contenu)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = titre, Style = S("SousTitre"), Margin = new Thickness(0) });
        p.Children.Add(new TextBlock { Text = texte, Style = S("Muet"), FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 6, 0, 0) });
        p.Children.Add(contenu);
        return new Border { Style = S("Section"), Child = p };
    }

    /// <summary>Un aperçu de thème : une mini-fenêtre dessinée avec les couleurs de ce thème, son nom dessous.</summary>
    Button Apercu(bool fonce)
    {
        Brush C(string cle) => new SolidColorBrush(Palette.De(cle, fonce));
        var mini = new Grid { Height = 128, ClipToBounds = true };
        mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        mini.ColumnDefinitions.Add(new ColumnDefinition());
        var rail = new StackPanel { Background = C("Rail") };
        rail.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(4), Background = C("OrVif"), Margin = new Thickness(0, 8, 0, 6) });
        for (int i = 0; i < 3; i++) rail.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(4), Background = C("Nuit2"), Margin = new Thickness(0, 0, 0, 6) });
        mini.Children.Add(rail);
        var corps = new Grid { Background = C("Page"), Margin = new Thickness(0) };
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        corps.RowDefinitions.Add(new RowDefinition());
        corps.RowDefinitions.Add(new RowDefinition());
        void Bloc(int r, int c, Brush fond, bool bord)
        {
            var b = new Border { Background = fond, CornerRadius = new CornerRadius(5), Margin = new Thickness(4), BorderBrush = bord ? C("Nuit3") : null, BorderThickness = new Thickness(bord ? 1 : 0) };
            SetRow(b, r); SetColumn(b, c);
            corps.Children.Add(b);
        }
        Bloc(0, 0, C("Encre"), true);
        Bloc(0, 1, new RadialGradientBrush(Color.FromRgb(0x26, 0x35, 0x7A), Color.FromRgb(0x0C, 0x12, 0x30)), false);
        Bloc(1, 0, C("Encre"), true);
        Bloc(1, 1, C("OrVif"), false);
        SetColumn(corps, 1);
        mini.Children.Add(corps);
        var nom = new DockPanel { Margin = new Thickness(4, 10, 4, 0) };
        var note = new TextBlock { Text = fonce ? "salle obscure" : "par défaut", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(note, Dock.Right);
        nom.Children.Add(note);
        nom.Children.Add(new TextBlock { Text = fonce ? "Sombre" : "Clair", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var pileB = new StackPanel { Width = 230 };
        pileB.Children.Add(new Border { CornerRadius = new CornerRadius(9), Child = mini, ClipToBounds = true, Background = C("Page") });
        pileB.Children.Add(nom);
        var b = new Button
        {
            Style = S("Btn"), Content = pileB, Padding = new Thickness(10, 10, 10, 12), Margin = new Thickness(0, 0, 16, 0), BorderThickness = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = fonce ? "Thème sombre" : "Thème clair (par défaut)",
        };
        b.Click += (_, _) =>
        {
            App.AppliquerApparence(fonce ? "sombre" : "clair");
            Principale?.Toast(fonce ? "Thème sombre : appliqué tout de suite, à toutes les fenêtres." : "Thème clair (par défaut).");
        };
        return b;
    }

    void MajApparence()
    {
        bool fonce = Palette.Actuelle.Sombre;
        clair.BorderBrush = P(fonce ? "Nuit3" : "OrVif");
        sombre.BorderBrush = P(fonce ? "OrVif" : "Nuit3");
    }

    /// <summary>Les écrans détectés, avec « Projeter ici » ou « Arrêter », puis la fenêtre et MyVirtualCam.</summary>
    void MajEcrans()
    {
        ecrans.Children.Clear();
        var m = Principale;
        var actuel = App.Donnees.Reglages.Ecran;
        bool ouvert = m?.Ecran != null;
        int k = 0;
        try
        {
            foreach (var e in Natif.Ecrans())
            {
                var ec = e;
                bool ici = ouvert && actuel is { Mode: "plein" } && actuel.X == e.X && actuel.Y == e.Y;
                var bouton = new Button { Style = S(ici ? "BtnRouge" : "BtnPetit"), Content = ici ? "Arrêter" : "Projeter ici", Height = 34 };
                bouton.Click += (_, _) => { if (ici) m?.Ecran?.Close(); else m?.OuvrirSur(ec, true); MajEcrans(); };
                ecrans.Children.Add(Ligne(ici, $"Écran {++k}{(e.Nom.Length > 0 ? " · " + e.Nom : "")} · {e.W} × {e.H}" + (e.Principal ? " (principal)" : ""),
                    ici ? "en cours de projection" : e.Principal ? "l'écran de la régie" : "disponible", bouton));
            }
        }
        catch (Exception x) { Journal.Erreur("liste des écrans", x); }
        bool fenetre = ouvert && actuel is { Mode: "fenetre" };
        var f = new Button { Style = S("BtnPetit"), Content = fenetre ? "Fermer" : "Ouvrir", Height = 34 };
        f.Click += (_, _) =>
        {
            if (fenetre) m?.Ecran?.Close();
            else if (Natif.Ecrans() is { Count: > 0 } l) m?.OuvrirSur(l.FirstOrDefault(x => x.Principal) ?? l[0], false);
            MajEcrans();
        };
        ecrans.Children.Add(Ligne(fenetre, "Fenêtre", "une fenêtre redimensionnable", f));
        ecrans.Children.Add(Ligne(App.CameraVirtuelle != null, "MyVirtualCam", App.CameraVirtuelle != null ? "caméra virtuelle 720p30, toujours disponible" : "indisponible sur cet ordinateur", null));
    }

    static Border Ligne(bool actif, string titre, string sous, Button? bouton)
    {
        var d = new DockPanel();
        d.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = P(actif ? "Vert" : "Brume2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        if (bouton != null) { DockPanel.SetDock(bouton, Dock.Right); d.Children.Add(bouton); }
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = sous, FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 2, 0, 0) });
        d.Children.Add(t);
        return new Border { Child = d, BorderBrush = P("Trait"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 11, 0, 11) };
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestSection(string id) => Aller(id, false);
}
