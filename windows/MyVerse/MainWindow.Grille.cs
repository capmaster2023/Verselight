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

    static readonly Brush FondTuile = new SolidColorBrush(Color.FromRgb(0x1C, 0x17, 0x12));

    void InitGrille()
    {
        App.Sources.Change += GrilleDirect;
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
        var vue = new Grid { Background = FondTuile };
        Image? image = null;
        string type = a.Source?.Type ?? "texte";
        if (type == "texte")
            vue.Children.Add(new Viewbox
            {
                Margin = new Thickness(10), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock { Text = texte, Width = 300, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
            });
        else
        {
            vue.Children.Add(new TextBlock
            {
                Text = type == "camera" ? "Caméra" : type == "fenetre" ? "Fenêtre" : "Image", FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xBE, 0xAA)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            image = new Image { Stretch = Stretch.Uniform };
            if (type == "image" && a.Source?.Chemin is { Length: > 0 } chemin)
                try
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.DecodePixelWidth = 360;
                    b.UriSource = new Uri(chemin);
                    b.EndInit();
                    b.Freeze();
                    image.Source = b;
                }
                catch (Exception) { }
            vue.Children.Add(image);
        }
        var direct = new TextBlock { Text = "EN DIRECT", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Direct"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var bas = new DockPanel { Margin = new Thickness(8, 6, 8, 7) };
        DockPanel.SetDock(direct, Dock.Right);
        bas.Children.Add(direct);
        bas.Children.Add(new TextBlock
        {
            Text = numero + " · " + a.Titre + (total > 1 ? $" ({i + 1}/{total})" : ""), FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = Pinceau("Blanc"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var pile = new DockPanel();
        DockPanel.SetDock(bas, Dock.Bottom);
        pile.Children.Add(bas);
        pile.Children.Add(new Border { Height = 118, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = vue });
        var tuile = new Border
        {
            Width = 216, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(4), CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand,
            Background = Pinceau("Encre"), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(2), Child = pile,
            ToolTip = "Clic : en direct · clic droit : modifier, retirer",
        };
        tuile.MouseLeftButtonUp += (_, _) => R.AfficherAnnonce(a, i);
        tuile.MouseRightButtonUp += (_, e) => { MenuDisplay(a, tuile, true); e.Handled = true; };
        tuiles.Add((tuile, a.Id, i, type is "camera" or "fenetre" ? image : null, direct));
        return tuile;
    }

    /// <summary>La tuile à l'écran : cadre rouge, « EN DIRECT », et l'image vivante d'une caméra ou d'une fenêtre.</summary>
    void GrilleDirect()
    {
        var c = R.ElementCourant == null && !R.Etat.Vide ? R.Cible as CibleTexte : null;
        foreach (var t in tuiles)
        {
            bool ok = c != null && c.Element == t.Id && c.I == t.I;
            t.Tuile.BorderBrush = Pinceau(ok ? "Direct" : "Nuit3");
            t.Direct.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (t.Vue != null) t.Vue.Source = ok ? App.Sources.Image : null;
        }
        GrilleInfo.Text = ecran != null ? "Écran : " + NomEcran() : "Aucun écran choisi (bouton Projection en haut)";
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestTuiles => tuiles.Count;
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
        if (ObsTexte == null) return;
        ObsTexte.Text = ObsCompact ? "Projection" : "Projection · " + NomEcran();
        ObsBtn.ToolTip = "Choisir l'écran de projection · " + NomEcran();
        ObsPoint.Fill = Pinceau(ecran != null ? "Vert" : "Brume2");
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
        Ouvrir(m, ObsBtn, PlacementMode.Bottom);
    }
}
