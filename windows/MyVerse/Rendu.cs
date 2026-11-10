using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MyVerse.Ui;

/// <summary>
/// Dessin de l'écran de projection (comme ecran.html de la 3.x) : fond du thème, texte à la plus grande taille
/// qui tient à l'écran (lignes équilibrées), 2e version en italique, référence en or ; plein écran ou bandeau.
/// Utilisé par la fenêtre de projection et par l'aperçu de la régie.
/// </summary>
public sealed class Rendu : Grid
{
    readonly Border fond = new();
    readonly Image media = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly Border scene = new();
    readonly StackPanel pile = new();
    readonly TextBlock texte = new(), texte2 = new(), refT = new();
    EtatEcran etat = new();
    string cle = "";
    int version;

    /// <summary>Fondu entre deux diapositives (fenêtre de projection) ; sans fondu pour l'aperçu.</summary>
    public bool Transitions { get; set; } = true;

    static readonly SolidColorBrush Blanc = Gele(new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEC)));
    static readonly SolidColorBrush Or2 = Gele(new SolidColorBrush(Color.FromRgb(0xF6, 0xD6, 0x8A)));
    static readonly SolidColorBrush FondBandeau = Gele(new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x10, 0x28)));
    // fonds clairs « Lin » et « Ciel » : texte foncé, sans ombre
    static readonly SolidColorBrush FondBandeauClair = Gele(new SolidColorBrush(Color.FromArgb(0xF0, 0xFB, 0xF8, 0xF2)));
    static readonly SolidColorBrush EncreLin = Gele(new SolidColorBrush(Color.FromRgb(0x1F, 0x1B, 0x16)));
    static readonly SolidColorBrush EncreCiel = Gele(new SolidColorBrush(Color.FromRgb(0x14, 0x21, 0x3D)));
    static readonly SolidColorBrush RefLin = Gele(new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x12)));
    static readonly SolidColorBrush RefCiel = Gele(new SolidColorBrush(Color.FromRgb(0x1E, 0x4F, 0xB0)));
    static bool Clair(string? t) => t is "lin" or "ciel";
    readonly DropShadowEffect ombre = new() { Color = Colors.Black, Opacity = 0.55, BlurRadius = 18, ShadowDepth = 3, Direction = 270, RenderingBias = RenderingBias.Performance };
    static T Gele<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static FontFamily Police(string cle, string secours)
    {
        try { if (Application.Current?.TryFindResource(cle) is FontFamily f) return f; } catch (Exception) { }
        return new FontFamily(secours);
    }
    static readonly FontFamily Serif = Police("Serif", "Georgia");
    static readonly FontFamily Sans = Police("Sans", "Segoe UI");

    public Rendu()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Children.Add(fond);
        Children.Add(scene);
        Children.Add(media);
        scene.Child = pile;
        pile.VerticalAlignment = VerticalAlignment.Center;
        pile.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var t in new[] { texte, texte2, refT })
        {
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextWrapping = TextWrapping.WrapWithOverflow;
            t.Foreground = Blanc;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            pile.Children.Add(t);
        }
        texte2.FontStyle = FontStyles.Italic;
        texte2.FontFamily = Serif;
        texte2.Opacity = 0.86;
        refT.FontFamily = Sans;
        refT.FontWeight = FontWeights.Bold;
        refT.Foreground = Or2;
        pile.Effect = ombre;
        scene.Opacity = 0;
        fond.Opacity = 0;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SizeChanged += (_, _) => { Placer(); Ajuster(); PlacerDuo(); };
    }

    public void ImageDisplay(ImageSource? image) { media.Source = image; if (gauche != null) gauche.media.Source = image; }

    // ------------------------------------------------------------ Duo : un second contenu dans un coin, ou côte à côte
    Rendu? gauche, epingle;
    DuoEcran? duo;
    ImageSource? imageDuo;

    /// <summary>Image vivante (caméra, fenêtre, image) du contenu épinglé.</summary>
    public void ImageDuo(ImageSource? image) { imageDuo = image; if (epingle != null) epingle.media.Source = image; }

    Rendu Enfant(ref Rendu? r)
    {
        if (r == null)
        {
            r = new Rendu { Transitions = Transitions, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Children.Add(r);
        }
        r.Transitions = Transitions;
        return r;
    }

    /// <summary>Taille connue : celle du dessin, sinon celle demandée (aperçu 1920 × 1080, caméra virtuelle) avant la première mise en page.</summary>
    static double Cote(double reel, double demande) => reel > 0 ? reel : double.IsNaN(demande) ? 0 : demande;

    void PlacerDuo()
    {
        if (duo == null)
        {
            if (gauche != null) gauche.Visibility = Visibility.Collapsed;
            if (epingle != null) epingle.Visibility = Visibility.Collapsed;
            return;
        }
        // les deux dessins existent toujours quand il y a un Duo, même si la taille n'est pas encore connue
        var ep = Enfant(ref epingle);
        ep.Visibility = Visibility.Visible;
        ep.media.Source = imageDuo;
        Rendu? g = null;
        if (duo.Place == "cote")
        {
            g = Enfant(ref gauche);
            g.Visibility = Visibility.Visible;
            g.media.Source = media.Source;
        }
        else if (gauche != null) gauche.Visibility = Visibility.Collapsed;
        double W = Cote(ActualWidth, Width), H = Cote(ActualHeight, Height);
        if (W <= 0 || H <= 0) return;
        if (g != null)
        {
            // côte à côte : deux moitiés de même forme que l'écran, centrées en hauteur ; le fond principal reste visible autour
            double w = W / 2, h = H / 2, y = (H - h) / 2;
            g.Width = w; g.Height = h; g.Margin = new Thickness(0, y, 0, 0);
            ep.Width = w; ep.Height = h; ep.Margin = new Thickness(w, y, 0, 0);
        }
        else
        {
            double part = Math.Clamp(duo.Part, 0.2, 0.5);
            double w = W * part, h = H * part, m = 0.03 * H;
            ep.Width = w; ep.Height = h;
            ep.Margin = new Thickness(duo.Place is "hg" or "bg" ? m : W - w - m, duo.Place is "hd" or "hg" ? m : H - h - m, 0, 0);
        }
    }

    /// <summary>Affiche un nouvel état ; avec un Duo, le contenu épinglé en plus.</summary>
    public void Afficher(EtatEcran e)
    {
        duo = e.Duo;
        var seul = e with { Duo = null };
        PlacerDuo();
        if (duo == null)
        {
            // plus de Duo : les dessins cachés ne gardent ni texte ni image
            if (epingle != null) { epingle.media.Source = null; imageDuo = null; }
            if (gauche != null) gauche.media.Source = null;
            AfficherSeul(seul);
            return;
        }
        epingle!.Afficher(duo.Etat with { Duo = null, Theme = e.Theme, Taille = e.Taille, Presentation = "plein" });
        if (duo.Place == "cote")
        {
            gauche!.Afficher(seul with { Presentation = "plein" });
            // l'écran principal ne garde que son fond, visible derrière les deux moitiés
            AfficherSeul(seul with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null });
            Animer(fond, 1, 0);
            return;
        }
        AfficherSeul(seul);
    }

    // ------------------------------------------------------------ fonds des thèmes
    static Brush FondTheme(string theme)
    {
        switch (theme)
        {
            case "noir": return Brushes.Black;
            case "vert": return Gele(new SolidColorBrush(Color.FromRgb(0x00, 0xB1, 0x40)));
            case "transparent": return Brushes.Transparent;
            case "lin": return Gele(new SolidColorBrush(Color.FromRgb(0xF1, 0xEE, 0xE8)));
            case "ciel":
            {
                var b = new LinearGradientBrush(Color.FromRgb(0xEE, 0xF5, 0xFD), Color.FromRgb(0xDC, 0xE9, 0xF8), 90);
                return Gele(b);
            }
            case "aube":
            {
                var b = new RadialGradientBrush { Center = new Point(0.5, 1.0), GradientOrigin = new Point(0.5, 1.0), RadiusX = 1.1, RadiusY = 0.9 };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x3A, 0x12), 0));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x1A, 0x0A), 0.45));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x06, 0x04), 1));
                return Gele(b);
            }
            default:
            {
                var b = new RadialGradientBrush { Center = new Point(0.5, 0.1), GradientOrigin = new Point(0.5, 0.1), RadiusX = 1.2, RadiusY = 0.9 };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0x30, 0x6A), 0));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x18, 0x3A), 0.45));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x05, 0x08, 0x17), 1));
                return Gele(b);
            }
        }
    }

    static void Animer(UIElement e, double vers, double secondes, Action? fini = null)
    {
        if (secondes <= 0)
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = vers;
            fini?.Invoke();
            return;
        }
        var a = new DoubleAnimation(vers, TimeSpan.FromSeconds(secondes)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        if (fini != null) a.Completed += (_, _) => fini();
        e.BeginAnimation(OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Affiche un nouvel état (fondu si le texte change).</summary>
    void AfficherSeul(EtatEcran e)
    {
        var avantTheme = etat.Theme;
        etat = e;
        if (avantTheme != e.Theme || fond.Background == null)
        {
            fond.Background = FondTheme(e.Theme);
            var encre = e.Theme == "ciel" ? EncreCiel : e.Theme == "lin" ? EncreLin : Blanc;
            texte.Foreground = texte2.Foreground = encre;
            refT.Foreground = e.Theme == "ciel" ? RefCiel : e.Theme == "lin" ? RefLin : Or2;
            pile.Effect = Clair(e.Theme) ? null : ombre;
            Placer();
        }
        media.Visibility = e.Mode == "media" ? Visibility.Visible : Visibility.Collapsed;
        if (e.Mode == "media")
        {
            version++; cle = "media";
            Animer(scene, 0, 0); Animer(fond, 1, 0); fond.Background = Brushes.Black;
            return;
        }
        // A live source may have temporarily replaced the theme background.
        fond.Background = FondTheme(e.Theme);
        bool vide = e.Vide;
        var deux = e.Mode == "verset" && e.Second != null ? e.Second.Texte : "";
        var nouvelle = string.Join("\u0001", new[] { e.Mode, string.Join("\n", e.Lignes), e.Ref, e.Taille.ToString(), e.Presentation, deux });
        double dureeFond = Transitions ? 0.6 : 0;
        Animer(fond, vide ? 0 : 1, dureeFond);
        if (nouvelle == cle) return;
        var avant = cle;
        cle = nouvelle;
        int v = ++version;
        if (vide)
        {
            Animer(scene, 0, Transitions ? 0.45 : 0);
            return;
        }
        if (Transitions && avant.Length > 0 && scene.Opacity > 0.05)
        {
            Animer(scene, 0, 0.38, () =>
            {
                if (v != version) return;
                Remplir(e, deux);
                Animer(scene, 1, 0.45);
            });
        }
        else
        {
            Remplir(e, deux);
            Animer(scene, 1, Transitions ? 0.45 : 0);
        }
    }

    void Remplir(EtatEcran e, string deux)
    {
        bool chant = e.Mode == "chant";
        texte.Text = string.Join("\n", e.Lignes);
        texte.FontFamily = chant ? Sans : Serif;
        texte.FontWeight = chant ? FontWeights.Bold : FontWeights.Normal;
        texte2.Text = deux;
        texte2.Visibility = deux.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        refT.Text = e.Ref ?? "";
        refT.Visibility = refT.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Placer();
        Ajuster();
    }

    double Rem => Math.Max(1, ActualHeight) / 108.0;

    /// <summary>Plein écran (marges 6 % / 7 %) ou bandeau en bas (30 % de la hauteur).</summary>
    void Placer()
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0) return;
        if (etat.Presentation == "bandeau")
        {
            scene.Margin = new Thickness(0.05 * W, 0, 0.05 * W, 0.05 * H);
            scene.VerticalAlignment = VerticalAlignment.Bottom;
            scene.Height = 0.30 * H;
            scene.Background = Clair(etat.Theme) ? FondBandeauClair : FondBandeau;
            scene.CornerRadius = new CornerRadius(2 * Rem);
            scene.Padding = new Thickness(0.04 * W, 0.03 * H, 0.04 * W, 0.03 * H);
            refT.Margin = new Thickness(0, 1.4 * Rem, 0, 0);
            refT.FontSize = 2.6 * Rem;
        }
        else
        {
            scene.Margin = new Thickness(0.07 * W, 0.06 * H, 0.07 * W, 0.06 * H);
            scene.VerticalAlignment = VerticalAlignment.Stretch;
            scene.Height = double.NaN;
            scene.Background = null;
            scene.CornerRadius = new CornerRadius(0);
            scene.Padding = new Thickness(0);
            refT.Margin = new Thickness(0, 3.2 * Rem, 0, 0);
            refT.FontSize = 3.2 * Rem;
        }
        texte2.Margin = new Thickness(0, 2.2 * Rem, 0, 0);
    }

    void Taille(double r, bool avec2)
    {
        bool chant = etat.Mode == "chant";
        texte.FontSize = r * Rem;
        texte.LineHeight = r * Rem * (chant ? 1.25 : 1.32);
        if (avec2)
        {
            texte2.FontSize = r * 0.68 * Rem;
            texte2.LineHeight = r * 0.68 * Rem * 1.3;
        }
    }

    static Size Mesure(TextBlock t, double largeur)
    {
        t.Measure(new Size(largeur, double.PositiveInfinity));
        return t.DesiredSize;
    }

    /// <summary>La plus grande taille de texte qui tient dans l'écran, puis des lignes de longueurs voisines.</summary>
    void Ajuster()
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0 || texte.Text.Length == 0) return;
        texte.MaxWidth = double.PositiveInfinity;
        texte2.MaxWidth = double.PositiveInfinity;
        double largeur = W - scene.Margin.Left - scene.Margin.Right - scene.Padding.Left - scene.Padding.Right;
        double hauteur = (double.IsNaN(scene.Height) ? H - scene.Margin.Top - scene.Margin.Bottom : scene.Height) - scene.Padding.Top - scene.Padding.Bottom;
        if (largeur <= 10 || hauteur <= 10) return;
        bool avec2 = texte2.Visibility == Visibility.Visible;
        double maxH = hauteur;
        if (refT.Visibility == Visibility.Visible) maxH -= Mesure(refT, largeur).Height + refT.Margin.Top + 40 * H / 1080;
        double lo = 2, hi = 11, best = 2;
        for (int i = 0; i < 14; i++)
        {
            double mid = (lo + hi) / 2;
            Taille(mid, avec2);
            var s1 = Mesure(texte, largeur);
            double haut = s1.Height;
            bool ok = s1.Width <= largeur + 2;
            if (avec2)
            {
                var s2 = Mesure(texte2, largeur);
                haut += texte2.Margin.Top + s2.Height;
                ok &= s2.Width <= largeur + 2;
            }
            if (haut <= maxH + 2 && ok) { best = mid; lo = mid; } else hi = mid;
        }
        double t = best * (etat.Taille > 0 ? etat.Taille : 1);
        Taille(t, avec2);
        Equilibrer(texte, largeur);
        if (avec2) Equilibrer(texte2, largeur);
    }

    /// <summary>Lignes équilibrées (comme « text-wrap: balance ») : la largeur la plus petite qui garde le même nombre de lignes.</summary>
    static void Equilibrer(TextBlock t, double largeur)
    {
        double h = Mesure(t, largeur).Height;
        if (h <= t.LineHeight * 1.5) return; // une seule ligne
        double lo = largeur * 0.45, hi = largeur;
        for (int i = 0; i < 9; i++)
        {
            double mid = (lo + hi) / 2;
            if (Mesure(t, mid).Height <= h + 0.5) hi = mid; else lo = mid;
        }
        t.MaxWidth = Math.Min(largeur, hi + 2);
    }
}
