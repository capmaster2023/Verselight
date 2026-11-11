using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVerse.Ui;

/// <summary>
/// Display : la grille de sources (un clic = en direct sur l'écran choisi), les boutons « + » et « Modifier »
/// de la bibliothèque, et le choix de l'écran de projection (à la place d'OBS).
/// </summary>
public partial class MainWindow
{
    readonly List<(Border Tuile, string Id, int I, Image? Vue, TextBlock Direct)> tuiles = new();
    readonly System.Windows.Threading.DispatcherTimer minuterieGrille = new() { Interval = TimeSpan.FromMilliseconds(350) };

    static readonly Brush FondTuile = Fige(new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x1F)));
    static readonly Brush FondTuileSource = Fige(new SolidColorBrush(Color.FromRgb(0x27, 0x2D, 0x36)));
    static readonly Brush TexteTuile = Fige(new SolidColorBrush(Color.FromRgb(0xC9, 0xCE, 0xD6)));
    static Brush Fige(Brush b) { b.Freeze(); return b; }

    void InitGrille()
    {
        App.Sources.Change += GrilleDirect;
        minuterieGrille.Tick += (_, _) => { minuterieGrille.Stop(); GrilleMaj(); };
        R.DuosChange += GrillePlusTard;
        ListeAnnonces.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(ListeAnnonces, o) is ListBoxItem { DataContext: Annonce a })
            { MenuDisplay(a, ListeAnnonces, false); e.Handled = true; }
        };
        GrilleMaj();
        MajProjection();
    }

    // ------------------------------------------------------------ bibliothèque : « + » et « Modifier »
    void GrilleAnnonceListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not Annonce a) return;
        if (a.Grille) { Toast($"« {a.Titre} » est déjà dans la grille."); return; }
        R.GrilleAnnonce(a.Id, true);
        Toast($"« {a.Titre} » ajouté à la grille : un clic l'envoie en direct.");
    }

    void ModifierAnnonceListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Annonce a) ModifierDisplay(a);
    }

    void ModifierDisplay(Annonce a)
    {
        if (a.Source == null)
        {
            if (Dialogue.DemanderTexte(this, "Modifier", a.Titre, a.Texte) is { } t) R.ModifierAnnonce(a.Id, t.Titre, t.Texte);
        }
        else if (Dialogue.Demander(this, "Titre de la source :", a.Titre, "Modifier") is { } titre && titre.Trim().Length > 0)
            R.ModifierAnnonce(a.Id, titre, "");
    }

    void SupprimerDisplay(Annonce a)
    {
        if (!Dialogue.Confirmer(this, $"Supprimer « {a.Titre} » ?", "Supprimer")) return;
        if (annonceChoisie?.Id == a.Id) annonceChoisie = null;
        R.SupprimerAnnonce(a.Id);
    }

    void MenuDisplay(Annonce a, UIElement cible, bool tuile)
    {
        var m = new ContextMenu();
        m.Items.Add(Article("Afficher en direct", () => R.AfficherAnnonce(a), true));
        m.Items.Add(Article("Afficher puis épingler", () => EpinglerDisplay(a)));
        m.Items.Add(Article("Modifier…", () => ModifierDisplay(a)));
        m.Items.Add(a.Grille ? Article("Retirer de la grille", () => R.GrilleAnnonce(a.Id, false)) : Article("Ajouter à la grille", () => R.GrilleAnnonce(a.Id, true)));
        m.Items.Add(Article("Ajouter au déroulé du culte", () =>
        {
            if (R.AjouterAnnonceAgenda(a.Id) is { } el) Toast($"« {el.Titre} » ajouté au déroulé « {R.Agenda.Nom} ».");
        }));
        if (a.Source?.Type is "camera" or "fenetre") m.Items.Add(Article("Reconnecter la source", ReconnecterSources));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Supprimer", () => SupprimerDisplay(a)));
        Ouvrir(m, cible, PlacementMode.MousePoint);
    }

    // ------------------------------------------------------------ la grille
    void GrilleMaj()
    {
        GrilleSources.Children.Clear();
        tuiles.Clear();
        int n = 0;
        foreach (var a in R.Annonces.Where(x => x.Grille))
        {
            var textes = R.TextesAnnonce(a);
            for (int i = 0; i < textes.Count; i++) GrilleSources.Children.Add(Tuile(a, i, textes[i], textes.Count, ++n));
        }
        GrilleVide.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        GrilleDirect();
    }

    Border Tuile(Annonce a, int i, string texte, int total, int numero)
    {
        string type = a.Source?.Type ?? "texte";
        var vue = new Grid { Background = type == "texte" ? FondTuile : FondTuileSource };
        Image? image = null;
        if (type == "texte")
            vue.Children.Add(new Viewbox
            {
                Margin = new Thickness(10), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock { Text = texte, Width = 300, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
            });
        else if (type == "duo")
        {
            // le dossier Duo tel qu'il sort sur l'écran principal : ses deux éléments, chacun à sa place et à sa taille
            if (R.EtatAnnonce(a) is { } etat)
            {
                var rendu = new Rendu { Transitions = false, Width = 1920, Height = 1080, IsHitTestVisible = false };
                rendu.Afficher(etat);
                if (etat is { Mode: "media", Media: { Type: "image" } m1 }) rendu.ImageDisplay(ImageFichier(m1.Chemin, 640));
                if (etat.Duo?.Etat is { Mode: "media", Media: { Type: "image" } m2 }) rendu.ImageDuo(ImageFichier(m2.Chemin, 640));
                vue.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = rendu });
                vue.Tag = "duo-rendu";
            }
            else vue.Children.Add(new TextBlock
            {
                Text = "Duo incomplet : choisissez ses 2 éléments (onglet Duo)", FontSize = 13.5, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                Foreground = TexteTuile, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            vue.Children.Add(new TextBlock
            {
                Text = type == "camera" ? "Caméra" : type == "fenetre" ? "Fenêtre" : type == "video" ? "Vidéo" : "Image", FontSize = 15,
                Foreground = TexteTuile, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            image = new Image { Stretch = Stretch.Uniform };
            if (type == "image" && a.Source?.Chemin is { Length: > 0 } chemin) image.Source = ImageFichier(chemin, 360);
            vue.Children.Add(image);
        }
        var direct = new TextBlock { Text = "EN DIRECT", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Direct"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(6, 0, 0, 0) };
        var bas = new DockPanel { Margin = new Thickness(14, 10, 12, 11) };
        DockPanel.SetDock(direct, Dock.Right);
        bas.Children.Add(direct);
        bas.Children.Add(new TextBlock
        {
            Text = numero + " · " + a.Titre + (total > 1 ? $" ({i + 1}/{total})" : ""), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = Pinceau("Blanc"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var pile = new DockPanel();
        DockPanel.SetDock(bas, Dock.Bottom);
        pile.Children.Add(bas);
        // la vignette sombre remplit le haut de la tuile (coins arrondis en haut seulement), le nom est dessous sur fond blanc
        pile.Children.Add(new Border { Height = 124, CornerRadius = new CornerRadius(10, 10, 0, 0), ClipToBounds = true, Child = vue });
        var tuile = new Border
        {
            Width = 236, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(0), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            Background = Pinceau("Encre"), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1.5), Child = pile,
            ToolTip = "Clic : en direct · clic droit : modifier, retirer",
        };
        tuile.MouseLeftButtonUp += (_, _) => R.AfficherAnnonce(a, i);
        tuile.MouseRightButtonUp += (_, e) => { MenuDisplay(a, tuile, true); e.Handled = true; };
        tuiles.Add((tuile, a.Id, i, type is "camera" or "fenetre" ? image : null, direct));
        return tuile;
    }

    /// <summary>Une image de la bibliothèque, réduite pour une vignette (null si elle ne se lit pas).</summary>
    static BitmapSource? ImageFichier(string chemin, int largeur)
    {
        if (chemin.Length == 0) return null;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.DecodePixelWidth = largeur;
            b.UriSource = new Uri(chemin);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Un dossier Duo a changé (éléments, mesures) : ses vignettes de la grille suivent, un instant plus tard.</summary>
    void GrillePlusTard()
    {
        if (!R.Annonces.Any(a => a.Grille && a.Source?.Type == "duo")) return;
        minuterieGrille.Stop();
        minuterieGrille.Start();
    }

    /// <summary>La tuile à l'écran : cadre rouge, « EN DIRECT », et l'image vivante d'une caméra ou d'une fenêtre.</summary>
    void GrilleDirect()
    {
        var cible = R.ElementCourant == null && !R.Etat.Vide ? R.Cible : null;
        var dossier = R.Etat.Duo is { Place: "libre" } dl ? dl.Dossier : null;
        var c = dossier == null ? cible as CibleTexte : null;
        var tuileDuo = dossier == null ? null : R.Annonces.FirstOrDefault(a => a.Source is { Type: "duo" } s && s.Chemin == dossier)?.Id;
        foreach (var t in tuiles)
        {
            bool ok = c != null && c.Element == t.Id && c.I == t.I || dossier == null && cible is CibleDisplay cd && cd.Id == t.Id || tuileDuo == t.Id;
            t.Tuile.BorderBrush = Pinceau(ok ? "Direct" : "Nuit3");
            t.Tuile.BorderThickness = new Thickness(ok ? 2 : 1.5);
            t.Direct.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (t.Vue != null) t.Vue.Source = ok ? App.Sources.Image : null;
        }
        GrilleInfo.Text = ecran != null ? "Écran : " + NomEcran() : "Aucun écran choisi (bouton Projection en haut)";
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestTuiles => tuiles.Count;
    /// <summary>Une tuile de dossier Duo dessinée comme l'écran principal (et non un simple texte).</summary>
    public bool TestTuileDuoDessinee => tuiles.Any(t => t.Tuile.Child is DockPanel d && d.Children.OfType<Border>().Any(b => b.Child is Grid { Tag: "duo-rendu" }));
    public bool TestCliquerTuile(int k)
    {
        if (k < 0 || k >= tuiles.Count) return false;
        tuiles[k].Tuile.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        return true;
    }

    // ------------------------------------------------------------ écran de projection (à la place d'OBS)
    string NomEcran()
    {
        var m = App.Donnees.Reglages.Ecran;
        if (ecran == null || m == null) return "arrêtée";
        return m.Mode == "fenetre" ? "fenêtre" : $"{(m.Nom is { Length: > 0 } n ? n : "Écran")} {m.W} × {m.H}";
    }

    void MajProjection()
    {
        if (ProjectionTexte == null) return;
        ProjectionTexte.Text = ProjectionCompact ? "Projection" : ecran == null ? "Projection arrêtée" : "Projection · " + NomEcran();
        ProjectionBtn.ToolTip = "Choisir l'écran de projection · " + NomEcran();
        ProjectionPoint.Fill = Pinceau(ecran != null ? "Vert" : "Brume2");
        if (GrilleInfo != null) GrilleInfo.Text = ecran != null ? "Écran : " + NomEcran() : "Aucun écran choisi (bouton Projection en haut)";
    }

    /// <summary>Comme « Projecteur plein écran » d'OBS : la liste des écrans, une fenêtre, ou arrêter.</summary>
    void MenuProjection()
    {
        var m = new ContextMenu();
        var actuel = App.Donnees.Reglages.Ecran;
        int k = 0;
        foreach (var e in Natif.Ecrans())
        {
            var ec = e;
            bool ici = ecran != null && actuel is { Mode: "plein" } && actuel.X == e.X && actuel.Y == e.Y;
            m.Items.Add(Article($"Écran {++k} · {(e.Nom.Length > 0 ? e.Nom + " · " : "")}{e.W} × {e.H} @ {e.X},{e.Y}{(e.Principal ? " (principal)" : "")}", () => OuvrirSur(ec, true), ici));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Article("Nouvelle fenêtre", () =>
        {
            var l = Natif.Ecrans();
            var p = l.FirstOrDefault(x => x.Principal) ?? l.FirstOrDefault();
            if (p != null) OuvrirSur(p, false);
        }, ecran != null && actuel is { Mode: "fenetre" }));
        var arret = Article("Arrêter la projection", () => { ecran?.Close(); MajProjection(); });
        arret.IsEnabled = ecran != null;
        m.Items.Add(arret);
        Ouvrir(m, ProjectionBtn, PlacementMode.Bottom);
    }
}
