using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Page Culte, design « Pupitre » (MyChurch 3.2) : toutes les diapositives du culte en vignettes, groupées par élément du déroulé.
/// Chaque vignette est dessinée comme l'écran (le même rendu que la projection, avec l'habillage choisi) ;
/// « À L'ÉCRAN » en rouge, « SUIVANT » en ambre ; un clic l'affiche, un clic droit ouvre le menu de l'élément.
/// La colonne de droite montre aussi la diapositive suivante en petit.
/// </summary>
public partial class MainWindow
{
    sealed class Vignette
    {
        public required ElementAgenda Element { get; init; }
        public required int Index { get; init; }
        /// <summary>Cadre extérieur de 3 px : rouge (à l'écran), ambre (suivant) ou transparent.</summary>
        public required Border Anneau { get; init; }
        /// <summary>La zone 16:9 (sa hauteur suit la largeur de la vignette).</summary>
        public required Grid Ecran { get; init; }
        public required Border Badge { get; init; }
        public required TextBlock BadgeTexte { get; init; }
    }

    readonly List<Vignette> vignettes = new();
    readonly Dictionary<string, FrameworkElement> sectionsVignettes = new();
    readonly DispatcherTimer minuterieVignettes = new() { Interval = TimeSpan.FromMilliseconds(250) };
    string habillageVignettes = "";
    int nbDiapos;
    Vignette? vignetteDirecte;
    string? imageSuivantChemin;
    BitmapSource? imageSuivant;

    const double LargeurMinVignette = 210, EcartVignettes = 10;

    void InitVignettes()
    {
        minuterieVignettes.Tick += (_, _) => { minuterieVignettes.Stop(); VignettesMaj(); };
        DefilVignettes.SizeChanged += (_, _) => LargeurVignettes();
        R.ChantsChange += () => { ListeAgendaMaj(); VignettesPlusTard(); };
        R.DuosChange += VignettesPlusTard;
        VB.Change += VignettesPlusTard;
    }

    /// <summary>Un chant, une Bible ou un dossier Duo a changé : les vignettes suivent, un instant plus tard (une seule fois).</summary>
    void VignettesPlusTard()
    {
        minuterieVignettes.Stop();
        minuterieVignettes.Start();
    }

    /// <summary>La couleur d'un type d'élément (trait des cartes du déroulé, groupes de vignettes).</summary>
    static string CleType(ElementAgenda e) => e.Type switch
    {
        "chant" => "TypeChant",
        "bible" => "TypeLecture",
        "compte" => "TypeAccueil",
        "principal" => "TypePrincipal",
        _ => e.Source?.Type == "duo" ? "TypeDuo" : "TypeDisplay",
    };

    string TitreElement(ElementAgenda e) => e.Type == "chant" && R.Trouver(e.ChantId) is { } c ? c.Title : e.Titre;

    /// <summary>Reconstruit toutes les vignettes (déroulé, chants, Bible, découpage ou habillage changés).</summary>
    void VignettesMaj()
    {
        minuterieVignettes.Stop();
        Vignettes.Children.Clear();
        vignettes.Clear();
        sectionsVignettes.Clear();
        vignetteDirecte = null;
        var et = R.Etat;
        habillageVignettes = Habillage(et);
        nbDiapos = 0;
        var l = R.Agenda.Elements;
        for (int k = 0; k < l.Count; k++)
        {
            var section = Section(l[k], k);
            sectionsVignettes[l[k].Id] = section;
            Vignettes.Children.Add(section);
        }
        VignettesVide.Visibility = l.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LargeurVignettes();
        VignettesEtat(false);
        MajTitreElement();
        MajEntete();
    }

    static string Habillage(EtatEcran e) => $"{e.Theme}|{e.Taille}|{e.Presentation}";

    /// <summary>Un élément du déroulé : « ■ 10:30 · Jean 3:16-17   2 diapositives », puis ses vignettes.</summary>
    FrameworkElement Section(ElementAgenda e, int k)
    {
        var d = R.Diapos(e);
        nbDiapos += d.Count;
        var pile = new StackPanel { Margin = new Thickness(0, k == 0 ? 2 : 18, 0, 0) };
        var tete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Clic : choisir · double-clic : afficher depuis le début · clic droit : menu" };
        tete.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = Pinceau(CleType(e)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        tete.Children.Add(new TextBlock { Text = (e.Heure is { Length: > 0 } h ? h + " · " : "") + TitreElement(e), FontSize = 13.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        tete.Children.Add(new TextBlock
        {
            Text = d.Count == 1 ? "1 diapositive" : $"{d.Count} diapositives", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Pinceau("Brume2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        });
        tete.MouseLeftButtonDown += (_, ev) =>
        {
            ChoisirElement(e, false);
            if (ev.ClickCount == 2) R.AfficherElement(e, 0);
        };
        tete.MouseRightButtonUp += (_, ev) => { ChoisirElement(e, false); MenuElement(e); ev.Handled = true; };
        pile.Children.Add(tete);
        var grille = new WrapPanel();
        for (int i = 0; i < d.Count; i++) grille.Children.Add(CreerVignette(e, d[i], i));
        if (d.Count == 0)
            grille.Children.Add(new TextBlock { Text = e.Type == "chant" ? "Ce chant n'existe plus dans MyChurch." : "Rien à afficher dans cette Bible.", Style = Style_("Muet"), FontSize = 14, Margin = new Thickness(2, 0, 0, 4) });
        pile.Children.Add(grille);
        return pile;
    }

    /// <summary>Une vignette : la diapositive dessinée comme à l'écran, son numéro et son nom dessous (trait de la couleur du type).</summary>
    Border CreerVignette(ElementAgenda e, DiapoAgenda d, int i)
    {
        var ecran = new Grid { Background = Brushes.Black, ClipToBounds = true };
        EtatEcran? etat = null;
        try { etat = R.EtatCible(d.Cible, e); }
        catch (Exception x) { Journal.Erreur("vignette du déroulé", x); }
        if (etat != null)
        {
            var rendu = new Rendu { Transitions = false, Width = 1920, Height = 1080, IsHitTestVisible = false };
            rendu.Afficher(etat);
            if (etat is { Mode: "media", Media: { Type: "image" } m1 }) rendu.ImageDisplay(ImageFichier(m1.Chemin, 480));
            if (etat.Duo?.Etat is { Mode: "media", Media: { Type: "image" } m2 }) rendu.ImageDuo(ImageFichier(m2.Chemin, 480));
            ecran.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = rendu });
            if (etat.Theme == "transparent") ecran.Background = Pinceau("Damier");
            if (etat is { Mode: "media", Media.Type: "camera" or "fenetre" or "video" } && etat.Media is { } mv)
                ecran.Children.Add(new TextBlock
                {
                    Text = mv.Type switch { "camera" => "Caméra", "fenetre" => "Fenêtre", _ => "Vidéo" }, FontSize = 14, FontWeight = FontWeights.SemiBold,
                    Foreground = Pinceau("TuileTexte"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
        }
        var badgeTexte = new TextBlock { FontSize = 10, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.White };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(7), Child = badgeTexte,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
        };
        ecran.Children.Add(badge);

        var num = new TextBlock
        {
            Text = e.Type == "bible" && d.Cible is CibleVerset cv ? cv.V1.ToString() : (i + 1).ToString(), FontFamily = (FontFamily)FindResource("Mono"),
            FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Pinceau("Brume2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        if (i < 9) num.ToolTip = $"Touche {i + 1} quand l'élément est choisi";
        var nom = new TextBlock { Text = NomDiapo(e, d), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var pied = new DockPanel();
        DockPanel.SetDock(num, Dock.Left);
        pied.Children.Add(num);
        pied.Children.Add(nom);
        var bas = new Border { BorderBrush = Pinceau(CleType(e)), BorderThickness = new Thickness(0, 3, 0, 0), Padding = new Thickness(10, 6, 10, 8), Child = pied };
        var pile = new DockPanel();
        DockPanel.SetDock(bas, Dock.Bottom);
        pile.Children.Add(bas);
        pile.Children.Add(ecran);
        var carte = new Border { Background = Pinceau("Encre"), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = pile };
        // coins arrondis : la diapositive ne déborde pas de la carte
        carte.SizeChanged += (_, _) => pile.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, carte.ActualWidth - 2), Math.Max(0, carte.ActualHeight - 2)), 9, 9);
        var anneau = new Border
        {
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, EcartVignettes, EcartVignettes),
            Child = carte, Cursor = Cursors.Hand, ToolTip = "Clic : afficher · clic droit : menu de l'élément",
        };
        int k = i;
        anneau.MouseLeftButtonUp += (_, _) => { ChoisirElement(e, false); R.AfficherElement(e, k); };
        anneau.MouseRightButtonUp += (_, ev) => { ChoisirElement(e, false); MenuElement(e); ev.Handled = true; };
        vignettes.Add(new Vignette { Element = e, Index = i, Anneau = anneau, Ecran = ecran, Badge = badge, BadgeTexte = badgeTexte });
        return anneau;
    }

    /// <summary>Le nom sous la vignette : « Jean 3:16 », « Refrain », la première ligne d'un texte, le titre d'une source.</summary>
    static string NomDiapo(ElementAgenda e, DiapoAgenda d) => e.Type switch
    {
        "texte" => d.Texte.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? d.Label,
        "display" => e.Titre,
        _ => d.Label,
    };

    /// <summary>Autant de colonnes de 210 px au moins que la largeur le permet ; la diapositive garde le 16:9.</summary>
    void LargeurVignettes()
    {
        // largeur utile : la zone moins ses marges (24 + 10) et la place de la barre de défilement
        double w = DefilVignettes.ActualWidth - 24 - 10 - 12;
        if (w <= 0) return;
        int cols = Math.Max(1, (int)((w + EcartVignettes) / (LargeurMinVignette + EcartVignettes)));
        double l = Math.Floor((w + EcartVignettes) / cols) - EcartVignettes;
        foreach (var v in vignettes)
        {
            v.Anneau.Width = l;
            v.Ecran.Height = Math.Max(1, (l - 8) * 9 / 16);
        }
    }

    /// <summary>« À L'ÉCRAN » (rouge) et « SUIVANT » (ambre) ; la vignette à l'écran reste visible quand elle change.</summary>
    void VignettesEtat(bool defiler)
    {
        var cur = R.ElementCourant;
        int? pos = cur != null && R.Agenda.Elements.Contains(cur) ? R.PositionAgenda(cur) : null;
        // 3.3 : l'écran par défaut revenu par « Retirer » est à l'écran (vignette de son écran principal) ;
        // la suivante est alors celle d'après ce qui a été retiré
        if (cur == null && R.Cible is CibleDefaut cd && R.Agenda.Elements.FirstOrDefault(x => x.Id == cd.Id) is { } principal) { cur = principal; pos = 0; }
        var suivante = R.SuivanteDeroule();
        Vignette? directe = null;
        foreach (var v in vignettes)
        {
            bool live = pos is int q && v.Element == cur && v.Index == q;
            bool next = !live && suivante is { } s && v.Element == s.E && v.Index == s.I;
            v.Anneau.BorderBrush = live ? Pinceau("Direct") : next ? Pinceau("OrVif") : Brushes.Transparent;
            v.Badge.Visibility = live || next ? Visibility.Visible : Visibility.Collapsed;
            if (live || next)
            {
                v.Badge.Background = Pinceau(live ? "Direct" : "OrVif");
                v.BadgeTexte.Text = live ? (noir ? "ÉCRAN NOIR" : "À L'ÉCRAN") : "SUIVANT";
                v.BadgeTexte.Foreground = live ? Brushes.White : Pinceau("SurOr");
            }
            if (live) directe = v;
        }
        if (defiler && directe != null && directe != vignetteDirecte && onglet == "agenda") directe.Anneau.BringIntoView();
        vignetteDirecte = directe;
    }

    /// <summary>Fait défiler les vignettes jusqu'au groupe d'un élément.</summary>
    void DefilerVers(ElementAgenda? e)
    {
        if (e == null || !sectionsVignettes.TryGetValue(e.Id, out var s) || !s.IsVisible) return;
        try
        {
            var p = s.TransformToAncestor(Vignettes).Transform(new Point(0, 0));
            DefilVignettes.ScrollToVerticalOffset(Math.Max(0, p.Y - 2));
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Titre du centre de la page Culte : l'élément choisi (sinon le culte).</summary>
    void MajTitreElement()
    {
        var e = elementChoisi;
        ElemTitre.Text = e != null ? TitreElement(e) : R.Agenda.Elements.Count > 0 ? R.Agenda.Nom : "Préparez le culte";
        ElemTitre.ToolTip = ElemTitre.Text;
    }

    /// <summary>« Culte du dimanche » au-dessus de « 15 novembre » ; un autre nom reste entier sous « Déroulé ».</summary>
    static (string Surtitre, string Titre) DecouperNomCulte(string nom)
    {
        var m = Regex.Match(nom ?? "", @"^(.*\b(?:dimanche|lundi|mardi|mercredi|jeudi|vendredi|samedi))\s+(\S.*)$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()) : ("Déroulé", nom ?? "");
    }

    /// <summary>« 10:00 → 11:30 · 6 éléments · 18 diapositives ».</summary>
    string InfoDeroule()
    {
        var l = R.Agenda.Elements;
        if (l.Count == 0) return "Déroulé vide";
        var heures = l.Select(e => e.Heure).Where(h => !string.IsNullOrEmpty(h)).ToList();
        string plage = heures.Count >= 2 ? $"{heures[0]} → {heures[^1]} · " : heures.Count == 1 ? heures[0] + " · " : "";
        return $"{plage}{l.Count} élément{(l.Count > 1 ? "s" : "")} · {nbDiapos} diapositive{(nbDiapos > 1 ? "s" : "")}";
    }

    /// <summary>La diapositive qui vient avec « Suivant », en petit dans la colonne de droite.</summary>
    void MajSuivant()
    {
        var s = R.Prochaine();
        SuivantLab.Text = s?.Label ?? "—";
        SuivantLab.ToolTip = s?.Label;
        SuivantTexte.Text = s == null ? "Rien après." : "";
        SuivantApercu.ToolTip = s != null && s.Texte.Length > 0 ? s.Label + " : " + s.Texte.Replace("\r", "").Replace("\n", " ") : "La diapositive qui vient avec Suivant";
        EtatEcran? e = null;
        try { e = s != null ? R.EtatCible(s.Cible, R.ElementCourant) : null; }
        catch (Exception x) { Journal.Erreur("aperçu suivant", x); }
        SuivantRendu.Afficher(e ?? new EtatEcran());
        string? chemin = e is { Mode: "media", Media: { Type: "image" } m } ? m.Chemin : null;
        if (chemin != imageSuivantChemin) { imageSuivantChemin = chemin; imageSuivant = chemin != null ? ImageFichier(chemin, 480) : null; }
        SuivantRendu.ImageDisplay(imageSuivant);
        SuivantApercu.Background = e == null || e.Theme == "transparent" ? Pinceau("Damier") : Brushes.Black;
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestVignettes => vignettes.Count;
    /// <summary>« Jean 3:16 · À L'ÉCRAN » : la vignette en direct (vide s'il n'y en a pas).</summary>
    public string TestVignetteDirecte => vignettes.FirstOrDefault(v => v.Badge.Visibility == Visibility.Visible && v.Badge.Background == Pinceau("Direct")) is { } v
        ? NomDiapo(v.Element, R.Diapos(v.Element)[v.Index]) + " · " + v.BadgeTexte.Text : "";
    public string TestVignetteSuivante => vignettes.FirstOrDefault(v => v.Badge.Visibility == Visibility.Visible && v.BadgeTexte.Text == "SUIVANT") is { } v
        ? NomDiapo(v.Element, R.Diapos(v.Element)[v.Index]) : "";
    /// <summary>Clic sur la vignette n (dans l'ordre du culte).</summary>
    public bool TestCliquerVignette(int n)
    {
        if (n < 0 || n >= vignettes.Count) return false;
        vignettes[n].Anneau.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        return true;
    }
    public string TestSuivantLabel => SuivantLab.Text;
}
