using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Onglet « Duo » : des dossiers de 2 éléments (source Display, chant ou verset) affichés ensemble,
/// chacun avec sa place et sa taille. Un dossier peut être mis dans Display (« + Display »).
/// </summary>
public partial class MainWindow
{
    DossierDuo? duoChoisi;
    Canvas? duoPlan;
    bool majDossier;
    readonly List<Action> duoSynchro = new();
    const double PlanL = 520, PlanH = 292.5;

    void InitDossiersDuo()
    {
        DuoNouveau.Click += (_, _) => { duoChoisi = R.NouveauDuo(""); DossiersMaj(); EditeurDuo(); };
        ListeDuos.SelectionChanged += (_, _) =>
        {
            if (majDossier || ListeDuos.SelectedItem is not DossierDuo d || d == duoChoisi) return;
            duoChoisi = d;
            EditeurDuo();
        };
        ListeDuos.MouseDoubleClick += (_, _) => AfficherDuoChoisi();
        R.DuosChange += DossiersMaj;
        DossiersMaj();
        EditeurDuo();
    }

    void DossiersMaj()
    {
        var l = R.Duos.ToList();
        bool parti = duoChoisi != null && !l.Contains(duoChoisi);
        if (parti) duoChoisi = null;
        majDossier = true;
        ListeDuos.ItemsSource = l;
        ListeDuos.SelectedItem = duoChoisi;
        majDossier = false;
        DuosVide.Visibility = l.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (parti) EditeurDuo();
    }

    void AfficherDuoChoisi()
    {
        if (duoChoisi == null) Toast("Choisissez d'abord un dossier Duo.");
        else R.AfficherDuo(duoChoisi);
    }

    static TextBlock Etiquette(string t) => new() { Text = t, Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 8) };

    Button Bouton(string texte, string style, Action action, string? aide = null)
    {
        var b = new Button { Style = Style_(style), Content = texte, Height = 44, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 8), ToolTip = aide };
        b.Click += (_, _) => action();
        return b;
    }

    /// <summary>Le dossier choisi : nom, actions, plan de l'écran et les réglages des 2 éléments.</summary>
    void EditeurDuo()
    {
        var d = duoChoisi;
        DuoPile.Children.Clear();
        duoSynchro.Clear();
        duoPlan = null;
        DuoEditeur.Visibility = d != null ? Visibility.Visible : Visibility.Collapsed;
        DuoChoisirAide.Visibility = d != null ? Visibility.Collapsed : Visibility.Visible;
        if (d == null) return;

        DuoPile.Children.Add(Etiquette("NOM DU DOSSIER"));
        var nom = new TextBox { Style = Style_("Champ"), Text = d.Nom, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        nom.TextChanged += (_, _) =>
        {
            if (nom.Text.Trim().Length == 0 || nom.Text.Trim() == d.Nom) return;
            d.Nom = nom.Text.Trim();
            R.EnregistrerDuo(d);
        };
        DuoPile.Children.Add(nom);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        actions.Children.Add(Bouton("Afficher", "BtnOr", AfficherDuoChoisi, "Envoie les 2 éléments en direct"));
        actions.Children.Add(Bouton("+ Display", "Btn", () =>
        {
            if (!d.A.Choisi || !d.B.Choisi) { Toast("Choisissez d'abord les 2 éléments du dossier."); return; }
            R.AjouterDuoDisplay(d);
            Toast($"« {d.Nom} » est dans Display : un clic sur sa tuile l'envoie en direct.");
        }, "Ajoute ce dossier dans la grille de Display"));
        actions.Children.Add(Bouton("Côte à côte", "Btn", () => Mesures(d, 0, 0.25, 0.5, 0.5, 0.25, 0.5)));
        actions.Children.Add(Bouton("Grand + coin", "Btn", () => Mesures(d, 0, 0, 1, 0.62, 0.03, 0.35)));
        actions.Children.Add(Bouton("Échanger 1 et 2", "Btn", () =>
        {
            (d.A.Type, d.B.Type) = (d.B.Type, d.A.Type);
            (d.A.Id, d.B.Id) = (d.B.Id, d.A.Id);
            (d.A.Ref, d.B.Ref) = (d.B.Ref, d.A.Ref);
            (d.A.Titre, d.B.Titre) = (d.B.Titre, d.A.Titre);
            R.EnregistrerDuo(d);
            EditeurDuo();
        }));
        actions.Children.Add(Bouton("Supprimer", "BtnRouge", () =>
        {
            if (!Dialogue.Confirmer(this, $"Supprimer le dossier « {d.Nom} » ?\n\nLes sources, les chants et les versets ne sont pas touchés.", "Supprimer")) return;
            R.SupprimerDuo(d.Id);
        }));
        DuoPile.Children.Add(actions);

        DuoPile.Children.Add(Etiquette("PLAN DE L'ÉCRAN"));
        duoPlan = new Canvas { Width = PlanL, Height = PlanH, ClipToBounds = true };
        // le plan rétrécit avec la fenêtre (jamais coupé)
        DuoPile.Children.Add(new Viewbox
        {
            MaxWidth = PlanL, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14),
            Child = new Border { Width = PlanL, Height = PlanH, CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1A, 0x1F)), Child = duoPlan },
        });

        var parts = new WrapPanel();
        parts.Children.Add(PartEditeur(d, d.A, 1));
        parts.Children.Add(PartEditeur(d, d.B, 2));
        DuoPile.Children.Add(parts);
        DessinerPlan();
    }

    void Mesures(DossierDuo d, double ax, double ay, double al, double bx, double by, double bl)
    {
        (d.A.X, d.A.Y, d.A.L, d.B.X, d.B.Y, d.B.L) = (ax, ay, al, bx, by, bl);
        MesuresChangees(d);
    }

    void MesuresChangees(DossierDuo d)
    {
        R.EnregistrerDuo(d, false);
        GrillePlusTard();
        majDossier = true;
        foreach (var a in duoSynchro) a();
        majDossier = false;
        DessinerPlan();
    }

    void DessinerPlan()
    {
        if (duoPlan == null || duoChoisi is not { } d) return;
        duoPlan.Children.Clear();
        foreach (var (p, n, cle) in new[] { (d.A, 1, "OrVif"), (d.B, 2, "Direct") })
        {
            var cadre = new Border
            {
                Width = PlanL * p.L, Height = PlanH * p.L, BorderBrush = Pinceau(cle), BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = n + " · " + (p.Choisi ? p.Titre : "rien"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4),
                },
            };
            Canvas.SetLeft(cadre, PlanL * p.X);
            Canvas.SetTop(cadre, PlanH * p.Y);
            duoPlan.Children.Add(cadre);
        }
    }

    /// <summary>Un élément du dossier : ce qu'il montre, et ses 3 mesures (gauche, haut, taille).</summary>
    Border PartEditeur(DossierDuo d, PartDuo p, int numero)
    {
        var pile = new StackPanel();
        pile.Children.Add(Etiquette(numero == 1 ? "ÉLÉMENT 1 · DESSOUS" : "ÉLÉMENT 2 · DESSUS"));
        var ligne = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var choisir = new Button { Style = Style_("Btn"), Content = p.Choisi ? "Changer…" : "Choisir…", Height = 40, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(10, 0, 0, 0) };
        choisir.Click += (_, _) => MenuPart(d, p, choisir);
        DockPanel.SetDock(choisir, Dock.Right);
        ligne.Children.Add(choisir);
        ligne.Children.Add(new TextBlock
        {
            Text = p.Choisi ? p.Titre : "Rien de choisi", FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Pinceau(p.Choisi ? "Blanc" : "Brume"),
        });
        pile.Children.Add(ligne);
        pile.Children.Add(Mesure("Gauche", () => p.X, v => p.X = v, 0, d));
        pile.Children.Add(Mesure("Haut", () => p.Y, v => p.Y = v, 0, d));
        pile.Children.Add(Mesure("Taille", () => p.L, v => p.L = v, 10, d));
        return new Border { Width = 330, Background = Pinceau("Nuit2"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 12, 12), Child = pile };
    }

    StackPanel Mesure(string nom, Func<double> lire, Action<double> ecrire, double min, DossierDuo d)
    {
        var lab = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Pinceau("Encre2"), Margin = new Thickness(0, 6, 0, 2) };
        var s = new Slider { Minimum = min, Maximum = 100, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 1 };
        void Synchro()
        {
            s.Value = Math.Round(lire() * 100);
            lab.Text = $"{nom} : {s.Value:0} %";
        }
        Synchro();
        duoSynchro.Add(Synchro);
        s.ValueChanged += (_, _) =>
        {
            if (majDossier) return;
            ecrire(s.Value / 100);
            MesuresChangees(d);
        };
        var p = new StackPanel();
        p.Children.Add(lab);
        p.Children.Add(s);
        return p;
    }

    /// <summary>Choisir ce que montre un élément : un chant (dans le recueil choisi), un verset ou une source Display.</summary>
    void MenuPart(DossierDuo d, PartDuo p, UIElement cible)
    {
        void Mettre(string type, string id, int[]? rf, string titre)
        {
            (p.Type, p.Id, p.Ref, p.Titre) = (type, id, rf, titre);
            R.EnregistrerDuo(d);
            EditeurDuo();
        }
        var m = new ContextMenu();
        m.Items.Add(Article("Chant…", () =>
        {
            // le recueil d'abord (il y en a souvent plusieurs), puis le numéro ou des mots du titre
            string? actuel = p.Type == "chant" ? R.Trouver(p.Id)?.Author : App.Donnees.Reglages.Recueil is { Length: > 0 } rc ? rc : null;
            var w = new ChoixChantWindow(actuel) { Owner = this };
            if (w.ShowDialog() == true && w.Choisi is { } c)
                Mettre("chant", c.Id, null, c.Title + (c.Author.Length > 0 ? " (" + c.Author + ")" : ""));
        }, true));
        m.Items.Add(Article("Verset…", () =>
        {
            if (Dialogue.Demander(this, "Passage de la Bible (exemples : jean 3 16, ps 23, rom 8 28-30) :", "", "Choisir") is not { } t || t.Trim().Length == 0) return;
            var r = R.Commande(t, null).FirstOrDefault(x => x.Action == null && x.Ref != null);
            if (r?.Ref is not Ref rf) { Toast("Passage introuvable. Essayez « jean 3 16 »."); return; }
            Mettre("bible", "", new[] { rf.B, rf.C, Math.Max(1, rf.V1), Math.Max(1, rf.V2 > 0 ? rf.V2 : rf.V1) }, r.Titre);
        }, true));
        m.Items.Add(new Separator());
        var sources = R.Annonces.Where(a => a.Source?.Type != "duo").ToList();
        if (sources.Count == 0) m.Items.Add(new MenuItem { Header = "(aucune source : onglet Display)", IsEnabled = false });
        foreach (var a in sources)
        {
            var src = a;
            m.Items.Add(Article("Display · " + a.Titre, () => Mettre("display", src.Id, null, src.Titre)));
        }
        Ouvrir(m, cible, PlacementMode.Bottom);
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestOngletDuo() => MontrerOnglet("duo");
    public void TestChoisirDossier(DossierDuo d) { duoChoisi = d; DossiersMaj(); EditeurDuo(); }
    public void TestAfficherDossier() => AfficherDuoChoisi();
}
