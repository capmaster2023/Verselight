using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// MyChurch 3.3 :
/// - la carte « Écran par défaut » de la colonne de contrôle : noir (l'écran se vide, comme avant), fond seul, ou un écran principal
///   du déroulé ; Retirer de l'écran (Échap) revient direct sur celui qui est choisi ;
/// - l'écran live en grand sur la page Culte (ce qui est à l'écran, ce qui vient, Précédent, Suivant, Noir, Retirer) ;
/// - la barre de gauche large (logo, nom des pages) quand la fenêtre est assez grande, compacte sinon.
/// </summary>
public partial class MainWindow
{
    bool barreLarge;
    string signatureDefaut = "";
    readonly List<(Button Bouton, string Id, Border Coche)> boutonsDefaut = new();

    void InitEcranDefaut()
    {
        CultePrec.Click += (_, _) => R.Bouger(-1);
        CulteSuiv.Click += (_, _) => R.Bouger(1);
        CulteNoir.Click += (_, _) => BasculerNoir();
        CulteRetirer.Click += (_, _) => R.Effacer();
        CentreAgenda.SizeChanged += (_, _) => TailleCulteLive();
        SizeChanged += (_, _) => AdapterBarre();
        // nouveau thème : les miniatures sont redessinées
        Palette.Change += () => { signatureDefaut = ""; MajEcranDefaut(); };
        AdapterBarre();
        MajEcranDefaut();
    }

    // ------------------------------------------------------------ barre de gauche
    /// <summary>Fenêtre de 1360 px et plus : barre large (logo, « MyChurch », icône et nom de chaque page) ; sinon la barre compacte.</summary>
    void AdapterBarre()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        bool large = double.IsNaN(w) || w >= 1360;
        if (large == barreLarge && ColonneRail.Width.Value == (large ? 208 : 78)) return;
        barreLarge = large;
        ColonneRail.Width = new GridLength(large ? 208 : 78);
        LogoLarge.Visibility = large ? Visibility.Visible : Visibility.Collapsed;
        LogoCompact.Visibility = large ? Visibility.Collapsed : Visibility.Visible;
        var style = Style_(large ? "OngletBarre" : "OngletRail");
        foreach (var o in new[] { OngletAgenda, OngletBible, OngletChants, OngletAnnonces, OngletDuo, OngletReglages }) o.Style = style;
        OngletReglages.Margin = large ? new Thickness(12, 0, 12, 0) : new Thickness(0);
        ThemeBtn.Style = Style_(large ? "BtnBarre" : "BtnRail");
        VersionBtn.HorizontalAlignment = large ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        VersionBtn.Margin = large ? new Thickness(20, 8, 0, 14) : new Thickness(0, 4, 0, 12);
    }

    // ------------------------------------------------------------ écran live de la page Culte
    /// <summary>L'écran live prend un peu moins de la moitié de la hauteur : les vignettes du culte gardent de la place dessous.</summary>
    void TailleCulteLive()
    {
        double h = CentreAgenda.ActualHeight;
        if (h <= 0) return;
        CulteApercu.MaxWidth = Math.Round(Math.Clamp(h * 0.46, 150, 440) * 16 / 9);
    }

    /// <summary>Le grand écran live : ce qui est à l'écran, ce qui vient avec Suivant, le bouton Noir.</summary>
    void MajCulteLive(EtatEcran e, Suivante? s)
    {
        CulteRendu.Afficher(e);
        CulteApercu.BorderBrush = Pinceau(e.Vide && e.Duo == null ? "Nuit3" : "Direct");
        CulteAEcran.Text = "À l'écran : " + AEcran(e);
        CulteEnsuite.Text = "Ensuite : " + (s?.Label ?? "rien");
        CulteEnsuite.ToolTip = s != null && s.Texte.Length > 0 ? s.Texte.Replace("\r", "").Replace("\n", " ") : null;
        if (noir) { CulteNoir.Background = Pinceau("Blanc"); CulteNoir.Foreground = Pinceau("Page"); CulteNoir.BorderBrush = Pinceau("Blanc"); }
        else { CulteNoir.ClearValue(BackgroundProperty); CulteNoir.ClearValue(ForegroundProperty); CulteNoir.ClearValue(BorderBrushProperty); }
    }

    /// <summary>« Jean 3:16 (LSG) », « Yera, Yera · 2/4 », « écran par défaut · Logo »…</summary>
    string AEcran(EtatEcran e)
    {
        if (noir) return "écran noir";
        if (R.Cible is CibleDefaut)
            return R.ElementCourant is { } p ? "écran principal · " + p.Titre : "écran par défaut · " + R.LibelleEcranDefaut;
        if (e.Vide) return e.Duo != null ? "contenu épinglé · " + e.Duo.Libelle : "rien";
        if (R.Cible is CibleChant cc && R.Trouver(cc.Id) is { } c) return c.Title + (c.Sections.Count > 1 ? $" · {cc.I + 1}/{c.Sections.Count}" : "");
        if (e.Mode is "verset" or "media" && e.Ref.Length > 0) return e.Ref;
        if (R.ElementCourant is { } el) return TitreElement(el);
        return e.Lignes.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
    }

    // ------------------------------------------------------------ carte « Écran par défaut »
    /// <summary>
    /// Un bouton par choix (noir, fond seul, puis les écrans principaux du déroulé), avec la miniature de ce qui reviendrait à l'écran ;
    /// le choix en cours est en or. Redessinée seulement quand la liste, l'habillage ou le thème change.
    /// </summary>
    void MajEcranDefaut()
    {
        var choix = new List<(string Id, string Nom, string Sous)>
        {
            ("noir", "Noir", "L'écran se vide (comme avant)"),
            ("fond", "Fond seul", "L'habillage, sans texte"),
        };
        foreach (var p in R.EcransPrincipaux)
            choix.Add((p.Id, p.Titre, (p.Heure is { Length: > 0 } h ? h + " · " : "") + "Déroulé · " + R.Description(p).Sous.Split(" · ")[0]));
        var signature = string.Join("|", choix.Select(c => c.Id + "=" + c.Nom + "=" + c.Sous)) + "|" + Habillage(R.Etat);
        if (signature != signatureDefaut)
        {
            signatureDefaut = signature;
            DefautChoix.Children.Clear();
            boutonsDefaut.Clear();
            foreach (var (id, nom, sous) in choix) DefautChoix.Children.Add(BoutonDefaut(id, nom, sous));
        }
        var actuel = R.EcranDefaut;
        foreach (var (b, id, coche) in boutonsDefaut)
        {
            bool ok = id == actuel;
            b.BorderBrush = Pinceau(ok ? "OrVif" : "Nuit3");
            b.BorderThickness = new Thickness(ok ? 2 : 1);
            b.Background = Pinceau(ok ? "OrLeger" : "Encre");
            coche.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        }
        var lib = R.LibelleEcranDefaut;
        CulteRetourTexte.Text = "Retour direct : " + (actuel == "noir" ? "noir" : lib);
        Effacer.ToolTip = CulteRetirer.ToolTip = actuel == "noir"
            ? "Échap · l'écran se vide (écran par défaut : noir)"
            : $"Échap · retour direct sur l'écran par défaut : {lib}";
    }

    Button BoutonDefaut(string id, string nom, string sous)
    {
        // miniature : l'écran tel qu'il reviendrait (noir : rien)
        var mini = new Grid { Background = Brushes.Black, ClipToBounds = true };
        EtatEcran? etat = null;
        if (id != "noir")
        {
            try { etat = R.EtatCible(new CibleDefaut(id)); }
            catch (Exception x) { Journal.Erreur("miniature de l'écran par défaut", x); }
        }
        if (etat != null)
        {
            var rendu = new Rendu { Transitions = false, Width = 1920, Height = 1080, IsHitTestVisible = false };
            rendu.Afficher(etat);
            if (etat is { Mode: "media", Media: { Type: "image" } m }) rendu.ImageDisplay(ImageFichier(m.Chemin, 160));
            mini.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = rendu });
            if (etat is { Mode: "media", Media.Type: "camera" or "mycam" or "fenetre" or "video" } && etat.Media is { } mv)
                mini.Children.Add(new TextBlock
                {
                    Text = mv.Type switch { "camera" => "Caméra", "mycam" => "MyCam", "fenetre" => "Fenêtre", _ => "Vidéo" }, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = Pinceau("TuileTexte"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
        }
        var cadre = new Border
        {
            Width = 64, Height = 36, CornerRadius = new CornerRadius(6), Child = mini, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), ClipToBounds = true,
        };
        var texte = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        texte.Children.Add(new TextBlock { Text = nom, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Blanc"), TextTrimming = TextTrimming.CharacterEllipsis });
        texte.Children.Add(new TextBlock { Text = sous, FontSize = 11.5, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        var coche = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Pinceau("OrVif"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Child = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconeCoche"), Stroke = Pinceau("SurOr"), StrokeThickness = 2.4, Width = 12, Height = 12, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.Children.Add(cadre);
        Grid.SetColumn(texte, 1);
        grille.Children.Add(texte);
        Grid.SetColumn(coche, 2);
        grille.Children.Add(coche);
        var b = new Button
        {
            Style = Style_("Btn"), Content = grille, Padding = new Thickness(6, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8), MinHeight = 50,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = id,
            ToolTip = id == "noir" ? "Retirer de l'écran vide l'écran, comme avant" : $"Retirer de l'écran revient direct sur « {nom} »",
        };
        b.Click += (_, _) =>
        {
            R.ChoisirEcranDefaut(id);
            MajEcranDefaut();
            Toast(id == "noir"
                ? "Écran par défaut : noir. Retirer de l'écran vide l'écran, comme avant."
                : $"Écran par défaut : {nom}. Retirer de l'écran (Échap) revient direct dessus.");
        };
        boutonsDefaut.Add((b, id, coche));
        return b;
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>« Noir | Fond seul | Logo* » : les choix de la carte « Écran par défaut », * sur celui qui est choisi.</summary>
    public string TestEcranDefautChoix => string.Join(" | ", boutonsDefaut.Select(x =>
        (((x.Bouton.Content as Grid)?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text) ?? x.Id)
        + (x.Coche.Visibility == Visibility.Visible ? "*" : "")));
    /// <summary>Clic sur le choix <paramref name="id"/> de la carte « Écran par défaut ».</summary>
    public bool TestChoisirEcranDefaut(string id)
    {
        if (boutonsDefaut.FirstOrDefault(x => x.Id == id).Bouton is not { } b) return false;
        b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return true;
    }
    public string TestCulteAEcran => CulteAEcran.Text;
    public string TestCulteRetour => CulteRetourTexte.Text;
    public bool TestBarreLarge => barreLarge;
}
