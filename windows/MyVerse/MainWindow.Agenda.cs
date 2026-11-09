using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>Une ligne du déroulé : heure, icône du type, titre ; « EN COURS » et « fait » suivent l'écran.</summary>
public sealed class LigneAgenda : INotifyPropertyChanged
{
    public required ElementAgenda Element { get; init; }
    public string Titre { get; init; } = "";
    public string Sous { get; init; } = "";
    public Geometry? IconeType { get; init; }
    string heure = "";
    bool enCours, fait;
    public string Heure { get => heure; set { if (heure == value) return; heure = value; Prevenir(nameof(Heure)); } }
    public bool EnCours { get => enCours; set { if (enCours == value) return; enCours = value; Prevenir(nameof(EnCours)); Prevenir(nameof(Icone)); } }
    public bool Fait { get => fait; set { if (fait == value) return; fait = value; Prevenir(nameof(Fait)); Prevenir(nameof(Icone)); } }
    /// <summary>Une coche quand l'élément est passé (et n'est plus à l'écran).</summary>
    public Geometry? Icone => fait && !enCours ? MainWindow.IconeFait : IconeType;
    void Prevenir(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Onglets « Culte » et « Annonces » (design « Aube ») : déroulé du culte préparé à l'avance (comme l'agenda de VideoPsalm),
/// élément en cours au centre (lecture en 2 langues), résumé du culte à partager, compte à rebours d'accueil,
/// habillage de l'écran, minuteur de prédication, écran de scène.
/// </summary>
public partial class MainWindow
{
    ElementAgenda? elementChoisi;
    Annonce? annonceChoisie;
    List<ResultatCommande> cmdRes = new();
    int cmdIndex;
    bool noir, chargementAgenda;
    EtatEcran? etatNoir;
    string cleDiapos = "";
    DateTime? chronoDepart;
    TimeSpan chronoCumul;
    SceneWindow? scene;
    readonly DispatcherTimer minuterieCmd = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer chrono = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer ticCompte = new() { Interval = TimeSpan.FromMilliseconds(500) };

    static Geometry Icone_(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
    public static readonly Geometry IconeFait = Icone_("M5,12.5 L9.5,17 L19,7.5");
    static readonly Geometry IconeChant = Icone_("M9,18 V5 L21,3 V16 M3,18 A3,3 0 1 0 9,18 A3,3 0 1 0 3,18 M15,16 A3,3 0 1 0 21,16 A3,3 0 1 0 15,16");
    static readonly Geometry IconeBible = Icone_("M4,19.5 V5 A2,2 0 0 1 6,3 H20 V17 H6 A2,2 0 0 0 4,19 A2,2 0 0 0 6,21 H20 M9,7 H16");
    static readonly Geometry IconeAnnonce = Icone_("M3,10.5 V13.5 A1,1 0 0 0 4,14.5 H6 L12,19 V5 L6,9.5 H4 A1,1 0 0 0 3,10.5 Z M16,9 A4,4 0 0 1 16,15 M19,6 A8,8 0 0 1 19,18");
    static readonly Geometry IconeCompte = Icone_("M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 M12,7 V12 L15,14");

    static readonly (string Tag, string Nom)[] FondsListe =
        { ("aube", "Aube"), ("lin", "Lin"), ("ciel", "Ciel"), ("nuit", "Nuit"), ("noir", "Noir"), ("vert", "Vert"), ("transparent", "OBS") };

    void InitAgenda()
    {
        // ---------- déroulé du culte
        AgendaMenu.Click += (_, _) => MenuAgendas();
        AgendaAjouter.Click += (_, _) => MenuAjouter();
        AgendaPartager.Click += (_, _) => PartagerWhatsApp(R.DeroulePartage());
        ResumePartager.Click += (_, _) => PartagerWhatsApp(R.ResumePartage());
        ListeAgenda.SelectionChanged += (_, _) =>
        {
            if (chargementAgenda) return;
            if (ListeAgenda.SelectedItem is LigneAgenda l && l.Element != elementChoisi) { elementChoisi = l.Element; AfficherDiapos(true); }
        };
        ListeAgenda.MouseDoubleClick += (_, _) => { if (ListeAgenda.SelectedItem is LigneAgenda l) R.AfficherElement(l.Element, 0); };
        ListeAgenda.PreviewMouseRightButtonUp += (_, _) => { if (ListeAgenda.SelectedItem is LigneAgenda l) MenuElement(l.Element); };
        ListeAgenda.PreviewKeyDown += (_, e) =>
        {
            if (elementChoisi is not { } el) return;
            if (e.Key == Key.Delete) { RetirerElement(el); e.Handled = true; }
            else if (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down) { R.DeplacerAgenda(el.Id, e.SystemKey == Key.Up ? -1 : 1); e.Handled = true; }
        };
        ElemAfficher.Click += (_, _) => { if (elementChoisi != null) R.AfficherElement(elementChoisi, 0); };
        ElemModifier.Click += (_, _) => ModifierElement();
        DefilDiapos.SizeChanged += (_, _) => LargeurDiapos();
        AgendaPiste.SizeChanged += (_, _) => MajProgres();
        R.AgendaChange += AgendaMaj;

        // ---------- recherche du culte (Ctrl K)
        Cmd.TextChanged += (_, _) =>
        {
            CmdIndice.Visibility = Cmd.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            minuterieCmd.Stop();
            minuterieCmd.Start();
        };
        minuterieCmd.Tick += (_, _) => { minuterieCmd.Stop(); CommandeMaj(); };
        Cmd.PreviewKeyDown += CmdTouche;
        Cmd.GotKeyboardFocus += (_, _) => { RechercheCulte.BorderBrush = Pinceau("OrVif"); if (Cmd.Text.Length > 0) CmdResultatsMaj(); };
        Cmd.LostKeyboardFocus += (_, _) => RechercheCulte.BorderBrush = Pinceau("Nuit3");

        // ---------- habillage, minuteur, compte à rebours, écran de scène
        ConstruireFonds();
        MinuteurBtn.Click += (_, _) =>
        {
            if (chronoDepart == null) { chronoDepart = DateTime.Now; chrono.Start(); }
            else { chronoCumul += DateTime.Now - chronoDepart.Value; chronoDepart = null; chrono.Stop(); }
            MinuteurMaj();
        };
        MinuteurZero.Click += (_, _) => { chronoCumul = TimeSpan.Zero; if (chronoDepart != null) chronoDepart = DateTime.Now; MinuteurMaj(); };
        MinuteurDuree.SelectionChanged += (_, _) => MinuteurMaj();
        chrono.Tick += (_, _) => MinuteurMaj();
        if (MinuteurBarre.Parent is FrameworkElement piste) piste.SizeChanged += (_, _) => MinuteurMaj();
        ticCompte.Tick += (_, _) => R.TicCompte();
        ticCompte.Start();
        SceneBtn.Click += (_, _) =>
        {
            if (scene != null) { scene.Activate(); return; }
            scene = new SceneWindow(MinuteurPourScene) { Owner = null };
            scene.Closed += (_, _) => scene = null;
            scene.Show();
        };

        // ---------- annonces
        R.AnnoncesChange += AnnoncesMaj;
        ListeAnnonces.SelectionChanged += (_, _) =>
        {
            if (ListeAnnonces.SelectedItem is not Annonce a || a == annonceChoisie) return;
            annonceChoisie = a;
            AnnonceTitre.Text = a.Titre;
            AnnonceTexte.Text = a.Texte;
        };
        AnnonceNouvelle.Click += (_, _) =>
        {
            annonceChoisie = R.NouvelleAnnonce("Nouvelle annonce", "");
            AnnonceTitre.Text = annonceChoisie.Titre;
            AnnonceTexte.Text = "";
            AnnoncesMaj();
            AnnonceTitre.Focus();
            AnnonceTitre.SelectAll();
        };
        AnnonceEnregistrer.Click += (_, _) => { if (EnregistrerAnnonce() != null) Toast("Annonce enregistrée."); };
        AnnonceAfficher.Click += (_, _) => { if (EnregistrerAnnonce() is { } a) R.AfficherAnnonce(a); };
        AnnonceAjouter.Click += (_, _) =>
        {
            if (EnregistrerAnnonce() is { } a && R.AjouterAnnonceAgenda(a.Id) is { } el) Toast($"« {el.Titre} » ajoutée au déroulé « {R.Agenda.Nom} ».");
        };
        AnnonceSupprimer.Click += (_, _) =>
        {
            if (annonceChoisie is not { } a || !Dialogue.Confirmer(this, $"Supprimer l'annonce « {a.Titre} » ?", "Supprimer")) return;
            R.SupprimerAnnonce(a.Id);
            annonceChoisie = null;
            AnnonceTitre.Text = AnnonceTexte.Text = "";
        };

        MinuteurMaj();
        AnnoncesMaj();
        AgendaMaj();
    }

    // ------------------------------------------------------------ déroulé du culte
    void AgendaMaj()
    {
        var ag = R.Agenda;
        AgendaTitre.Text = ag.Nom;
        AgendaTitre.ToolTip = ag.Nom;
        if (elementChoisi != null && !ag.Elements.Contains(elementChoisi)) elementChoisi = null;
        ListeAgendaMaj();
        AfficherDiapos(true);
    }

    void ListeAgendaMaj()
    {
        var lignes = R.Agenda.Elements.Select(e =>
        {
            var (_, sous) = R.Description(e);
            var l = new LigneAgenda
            {
                Element = e, Sous = TypeLib(e) + " · " + sous,
                Titre = e.Type == "chant" && R.Trouver(e.ChantId) is { } c ? c.Title : e.Titre,
                IconeType = e.Type switch { "chant" => IconeChant, "bible" => IconeBible, "compte" => IconeCompte, _ => IconeAnnonce },
            };
            MajLigne(l);
            return l;
        }).ToList();
        chargementAgenda = true;
        ListeAgenda.ItemsSource = lignes;
        ListeAgenda.SelectedItem = lignes.FirstOrDefault(l => l.Element == elementChoisi);
        chargementAgenda = false;
        AgendaVide.Visibility = lignes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MajProgres();
    }

    void MajLigne(LigneAgenda l)
    {
        var e = l.Element;
        l.EnCours = R.ElementCourant == e;
        l.Fait = R.EstFait(e);
        l.Heure = e.Heure ?? (l.Fait && R.HeureAffiche(e) is DateTime h ? h.ToString("HH:mm") : "");
    }

    static string TypeLib(ElementAgenda e) => e.Type switch { "chant" => "Chant", "bible" => "Lecture", "compte" => "Accueil", _ => "Annonce" };

    void MajProgres()
    {
        var l = R.Agenda.Elements;
        int i = R.ElementCourant != null ? l.IndexOf(R.ElementCourant) : -1;
        int faits = l.Count(R.EstFait);
        int n = Math.Max(i + 1, faits);
        AgendaProgres.Text = l.Count == 0 ? "" : $"{n} / {l.Count}";
        AgendaBarre.Width = l.Count > 0 ? Math.Max(0, AgendaPiste.ActualWidth * n / l.Count) : 0;
    }

    void ChoisirElement(ElementAgenda? e)
    {
        elementChoisi = e;
        if (ListeAgenda.ItemsSource is List<LigneAgenda> l)
        {
            chargementAgenda = true;
            ListeAgenda.SelectedItem = l.FirstOrDefault(x => x.Element == e);
            chargementAgenda = false;
            if (ListeAgenda.SelectedItem != null) ListeAgenda.ScrollIntoView(ListeAgenda.SelectedItem);
        }
        AfficherDiapos(true);
    }

    /// <summary>L'élément choisi, au centre : en-tête (étape, titre, versions) et ses diapositives en cartes.</summary>
    void AfficherDiapos(bool versHaut)
    {
        var e = elementChoisi;
        int? pos = e != null && R.ElementCourant == e ? R.PositionAgenda(e) : null;
        cleDiapos = (e?.Id ?? "") + "|" + (pos?.ToString() ?? "") + "|" + VB.Etiquette();
        Diapos.Children.Clear();
        ElemVersions.Children.Clear();
        ElemBoutons.Visibility = e != null ? Visibility.Visible : Visibility.Collapsed;
        ElemModifier.Visibility = e != null && e.Type is "texte" or "chant" or "compte" ? Visibility.Visible : Visibility.Collapsed;
        if (e == null)
        {
            ElemType.Text = R.Agenda.Elements.Count > 0 ? "DÉROULÉ DU CULTE" : "POUR COMMENCER";
            ElemTitre.Text = R.Agenda.Elements.Count > 0 ? "Choisissez un élément à gauche" : "Préparez le culte";
            if (R.Agenda.Elements.Count == 0)
                Diapos.Children.Add(new TextBlock
                {
                    Style = Style_("Muet"), FontSize = 15, LineHeight = 26, Margin = new Thickness(2, 4, 0, 0),
                    Text = "1. « + Ajouter » sous le déroulé : un chant, un passage, une annonce ou le compte à rebours d'accueil.\n" +
                           "2. Ou cherchez en haut (Ctrl K) : « chant 12 », « jean 3 16 »… puis Tab. Les boutons + de Bible, Chants et Annonces ajoutent aussi.\n" +
                           "3. Pendant le culte : Entrée affiche, → passe à la diapositive suivante, puis à l'élément suivant.",
                });
            return;
        }
        int etape = R.Agenda.Elements.IndexOf(e) + 1;
        ElemType.Text = $"ÉTAPE {etape} · {TypeLib(e).ToUpperInvariant()}";
        ElemTitre.Text = e.Type == "chant" && R.Trouver(e.ChantId) is { } c ? c.Title : e.Titre;
        if (e.Type == "bible")
        {
            var parts = VB.Etiquette().Split(" · ");
            ElemVersions.Children.Add(Puce(parts[0], true));
            if (parts.Length > 1) ElemVersions.Children.Add(Puce("+ " + parts[1] + " en dessous", false));
        }
        var d = R.Diapos(e);
        for (int i = 0; i < d.Count; i++) Diapos.Children.Add(Carte(e, d[i], i, pos == i));
        if (d.Count == 0)
            Diapos.Children.Add(new TextBlock { Text = e.Type == "chant" ? "Ce chant n'existe plus dans MyVerse." : "Rien à afficher dans cette Bible.", Style = Style_("Muet"), FontSize = 15, Margin = new Thickness(4) });
        if (versHaut) DefilDiapos.ScrollToTop();
        LargeurDiapos();
        if (pos is int p && p < Diapos.Children.Count && Diapos.Children[p] is FrameworkElement fe)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => fe.BringIntoView()));
    }

    Border Puce(string texte, bool pleine) => new()
    {
        CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        Background = pleine ? Pinceau("Blanc") : Pinceau("Encre"), BorderBrush = Pinceau("Trait2"), BorderThickness = new Thickness(pleine ? 0 : 1),
        Child = new TextBlock { Text = texte, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = pleine ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE6)) : Pinceau("Blanc") },
    };

    /// <summary>Une carte : verset (numéro, texte en Literata, 2e version dessous), strophe, annonce ou compte à rebours.</summary>
    Border Carte(ElementAgenda e, DiapoAgenda d, int i, bool direct)
    {
        var g = new Grid();
        UIElement droite = direct
            ? new TextBlock { Text = "À L'ÉCRAN", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Direct"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 0, 0) }
            : i < 9 ? new Border { Style = Style_("Touche"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0), Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume") } }
            : new Border();
        if (e.Type == "bible" && d.Cible is CibleVerset cv)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Background = Pinceau(direct ? "OrVif" : "Nuit2"),
                Child = new TextBlock { Text = cv.V1.ToString(), FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Pinceau(direct ? "SurOr" : "Encre2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = d.Texte, FontFamily = (FontFamily)FindResource("UiTitre"), FontSize = 18.5, LineHeight = 28, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc") });
            var t2 = cv.Parts <= 1 && VB.Seconde != null ? VB.Texte2(cv.B, cv.C, cv.V1, cv.V1) : "";
            if (t2.Length > 0) txt.Children.Add(new TextBlock { Text = t2, FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 8, 0, 0) });
            if (cv.Parts > 1) txt.Children.Add(new TextBlock { Text = d.Label, FontSize = 12, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 6, 0, 0) });
            Grid.SetColumn(txt, 2);
            g.Children.Add(txt);
            Grid.SetColumn(droite, 3);
            g.Children.Add(droite);
        }
        else
        {
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = e.Type == "compte" ? "Compte à rebours d'accueil" : d.Label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) });
            if (e.Type == "compte")
            {
                var l = d.Texte.Split('\n');
                txt.Children.Add(new TextBlock { Text = l[0], FontSize = 16, Foreground = Pinceau("Encre2") });
                txt.Children.Add(new TextBlock { Text = direct && R.Etat.Lignes.Count > 1 ? R.Etat.Lignes[1] : l.Length > 1 ? l[1] : "", FontFamily = (FontFamily)FindResource("UiTitre"), FontSize = 44, FontWeight = FontWeights.SemiBold });
            }
            else txt.Children.Add(new TextBlock { Text = d.Texte, FontSize = 15, LineHeight = 21, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc"), MaxHeight = 260, TextTrimming = TextTrimming.CharacterEllipsis });
            g.Children.Add(txt);
            Grid.SetColumn(droite, 1);
            g.Children.Add(droite);
        }
        var carte = new Border
        {
            Background = Pinceau("Encre"), BorderBrush = Pinceau(direct ? "OrVif" : "Nuit3"), BorderThickness = new Thickness(direct ? 2 : 1),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 14, 16, 14), Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand, Child = g, Tag = i,
            ToolTip = "Clic : afficher",
        };
        if (direct) carte.Effect = new DropShadowEffect { Color = Color.FromRgb(0x8A, 0x5A, 0x12), Opacity = 0.14, BlurRadius = 16, ShadowDepth = 4, Direction = 270 };
        carte.MouseLeftButtonDown += (_, _) => R.AfficherElement(e, i);
        return carte;
    }

    /// <summary>Versets : une carte par ligne ; strophes et annonces : autant de colonnes que la largeur le permet.</summary>
    void LargeurDiapos()
    {
        double w = DefilDiapos.ActualWidth - 8;
        if (w <= 0) return;
        bool ligne = elementChoisi?.Type is "bible" or "compte";
        int cols = ligne ? 1 : Math.Max(1, (int)((w + 12) / 270));
        double l = Math.Floor((w + 12) / cols) - 12;
        foreach (var c in Diapos.Children.OfType<Border>()) c.Width = l;
    }

    /// <summary>L'écran a changé : « EN COURS », éléments faits, la carte à l'écran et le résumé suivent.</summary>
    void MajAgendaEcran()
    {
        if (R.ElementCourant is { } el && el != elementChoisi && R.Agenda.Elements.Contains(el)) ChoisirElement(el);
        if (ListeAgenda.ItemsSource is List<LigneAgenda> l)
            foreach (var x in l) MajLigne(x);
        MajProgres();
        var e = elementChoisi;
        int? pos = e != null && R.ElementCourant == e ? R.PositionAgenda(e) : null;
        var cle = (e?.Id ?? "") + "|" + (pos?.ToString() ?? "") + "|" + VB.Etiquette();
        if (cle != cleDiapos || e?.Type == "compte") AfficherDiapos(false);
        var res = R.Resume();
        ResumeTexte.Text = res.Length > 0 ? "Déjà affiché : " + res : "Rien n'a encore été affiché.";
    }

    void AfficherAgendaCourant()
    {
        if (elementChoisi == null)
        {
            if (R.Agenda.Elements.FirstOrDefault() is { } premier) { ChoisirElement(premier); R.AfficherElement(premier, 0); }
            else Toast("Le déroulé est vide : « + Ajouter », ou cherchez un chant ou un verset en haut (Ctrl K).");
            return;
        }
        R.AfficherElement(elementChoisi, R.ElementCourant == elementChoisi && R.PositionAgenda(elementChoisi) is int p ? p : 0);
    }

    void RetirerElement(ElementAgenda el)
    {
        if (elementChoisi == el) elementChoisi = null;
        R.RetirerAgenda(el.Id);
    }

    void ModifierElement() { if (elementChoisi is { } e) ModifierElement(e); }

    void ModifierElement(ElementAgenda e)
    {
        switch (e.Type)
        {
            case "chant":
                Editer(R.Trouver(e.ChantId));
                AfficherDiapos(false);
                break;
            case "texte":
                if (Dialogue.DemanderTexte(this, "Modifier le texte", e.Titre, e.Texte ?? "") is { } t) R.ModifierTexteAgenda(e.Id, t.Titre, t.Texte);
                break;
            case "compte":
                if (Dialogue.Demander(this, "Durée du compte à rebours (minutes) :", (e.Minutes ?? 5).ToString(), "Compte à rebours") is { } m && int.TryParse(m, out var mn)
                    && Dialogue.Demander(this, "Message au-dessus du compte à rebours :", e.Texte ?? "Le culte commence dans", "Compte à rebours") is { } msg)
                    R.ModifierCompteAgenda(e.Id, mn, msg);
                break;
        }
    }

    void DemanderHeure(ElementAgenda e)
    {
        var t = Dialogue.Demander(this, "Heure prévue (par exemple 10:15). Laissez vide pour l'enlever.", e.Heure ?? "", "Heure prévue");
        if (t == null) return;
        if (t.Trim().Length > 0 && Regie.LireHeure(t) == null) { Toast("Heure non comprise : écrivez par exemple 10:15 ou 10h15."); return; }
        R.HeureAgenda(e.Id, t);
    }

    // ------------------------------------------------------------ menus
    static MenuItem Article(string titre, Action action, bool gras = false)
    {
        var m = new MenuItem { Header = titre, FontWeight = gras ? FontWeights.Bold : FontWeights.Normal };
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

    /// <summary>Les cultes préparés (un agenda par culte) : changer, nouveau, renommer, supprimer.</summary>
    void MenuAgendas()
    {
        var m = new ContextMenu();
        var actuel = R.Agenda;
        foreach (var a in R.Agendas)
        {
            var id = a.Id;
            m.Items.Add(Article($"{a.Nom}  ({a.Elements.Count})", () => { elementChoisi = null; R.ChoisirAgenda(id); }, a.Id == actuel.Id));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Article("Nouveau culte…", () =>
        {
            if (Dialogue.Demander(this, "Nom du nouveau culte :", Regie.NomAgendaParDefaut(), "Nouveau culte") is { } nom) { elementChoisi = null; R.NouvelAgenda(nom); }
        }));
        m.Items.Add(Article("Renommer…", () =>
        {
            if (Dialogue.Demander(this, "Nouveau nom :", actuel.Nom, "Renommer") is { } nom && nom.Trim().Length > 0) R.RenommerAgenda(actuel.Id, nom);
        }));
        m.Items.Add(Article("Supprimer ce culte", () =>
        {
            if (!Dialogue.Confirmer(this, $"Supprimer « {actuel.Nom} » ({actuel.Elements.Count} élément(s)) ?\n\nLes chants, les Bibles et les annonces ne sont pas touchés.", "Supprimer")) return;
            elementChoisi = null;
            R.SupprimerAgenda(actuel.Id);
        }));
        Ouvrir(m, AgendaMenu, PlacementMode.Bottom);
    }

    void MenuAjouter()
    {
        var m = new ContextMenu();
        m.Items.Add(Article("Chant ou verset…  (Ctrl K)", () =>
        {
            OuvrirCommande();
            Toast("Tapez « chant 12 » ou « jean 3 16 », puis Tab ou + pour l'ajouter au déroulé.");
        }));
        var annonces = new MenuItem { Header = "Annonce de la bibliothèque" };
        if (R.Annonces.Count == 0) annonces.Items.Add(new MenuItem { Header = "(aucune : onglet Annonces)", IsEnabled = false });
        foreach (var a in R.Annonces)
        {
            var id = a.Id;
            annonces.Items.Add(Article(a.Titre, () => { if (R.AjouterAnnonceAgenda(id) is { } el) ChoisirElement(el); }));
        }
        m.Items.Add(annonces);
        m.Items.Add(Article("Texte ou prière…", () =>
        {
            if (Dialogue.DemanderTexte(this, "Texte, annonce ou prière", "Prière", "Prions ensemble") is { } t) ChoisirElement(R.AjouterTexteAgenda(t.Titre, t.Texte));
        }));
        m.Items.Add(Article("Compte à rebours d'accueil…", () =>
        {
            if (Dialogue.Demander(this, "Durée du compte à rebours (minutes) :", "5", "Compte à rebours d'accueil") is { } t && int.TryParse(t, out var mn))
                ChoisirElement(R.AjouterCompteAgenda(mn));
        }));
        Ouvrir(m, AgendaAjouter, PlacementMode.Top);
    }

    void MenuElement(ElementAgenda e)
    {
        var m = new ContextMenu();
        m.Items.Add(Article("Afficher depuis le début", () => R.AfficherElement(e, 0), true));
        m.Items.Add(Article("Heure prévue…", () => DemanderHeure(e)));
        if (e.Type is "texte" or "chant" or "compte") m.Items.Add(Article("Modifier…", () => ModifierElement(e)));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Monter", () => R.DeplacerAgenda(e.Id, -1)));
        m.Items.Add(Article("Descendre", () => R.DeplacerAgenda(e.Id, 1)));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Retirer du déroulé", () => RetirerElement(e)));
        Ouvrir(m, ListeAgenda, PlacementMode.MousePoint);
    }

    /// <summary>WhatsApp s'ouvre avec le texte (ordinateur ou web) ; il est aussi copié pour le coller ailleurs.</summary>
    void PartagerWhatsApp(string texte)
    {
        try { Clipboard.SetText(texte); } catch (Exception) { }
        try
        {
            Process.Start(new ProcessStartInfo("https://wa.me/?text=" + Uri.EscapeDataString(texte)) { UseShellExecute = true });
            Toast("WhatsApp s'ouvre avec le texte. Il est aussi copié : Ctrl V pour le coller ailleurs.");
        }
        catch (Exception x)
        {
            Journal.Erreur("partage WhatsApp", x);
            Toast("Texte copié : collez-le dans WhatsApp (Ctrl V).");
        }
    }

    // ------------------------------------------------------------ boutons « + » des listes (Chants, Bible, Annonces)
    void AjouterChantListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Chant c && R.AjouterChantAgenda(c.Id) is { } el)
            Toast($"« {el.Titre} » ajouté au déroulé « {R.Agenda.Nom} ».");
    }

    void AjouterAnnonceListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Annonce a && R.AjouterAnnonceAgenda(a.Id) is { } el)
            Toast($"« {el.Titre} » ajoutée au déroulé « {R.Agenda.Nom} ».");
    }

    void AjouterVersetAgenda(int b, int c, int v)
    {
        var r = sel is Ref s && s.B == b && s.C == c && s.V2 > s.V1 && v >= s.V1 && v <= s.V2 ? s : new Ref(b, c, v, v);
        var el = R.AjouterBibleAgenda(r);
        Toast($"{el.Titre} ajouté au déroulé « {R.Agenda.Nom} ».");
    }

    static IEnumerable<T> Enfants<T>(DependencyObject o) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var x = VisualTreeHelper.GetChild(o, i);
            if (x is T t) yield return t;
            foreach (var y in Enfants<T>(x)) yield return y;
        }
    }

    // ------------------------------------------------------------ recherche du culte (Ctrl K)
    void OuvrirCommande()
    {
        if (onglet is not ("agenda" or "annonces")) MontrerOnglet("agenda");
        Cmd.Focus();
        Cmd.SelectAll();
    }

    void CommandeMaj()
    {
        var rec = App.Donnees.Reglages.Recueil;
        cmdRes = R.Commande(Cmd.Text, rec.Length > 0 ? rec : null);
        cmdIndex = 0;
        CmdResultatsMaj();
    }

    void CmdResultatsMaj()
    {
        CmdResultats.Children.Clear();
        bool texte = Cmd.Text.Trim().Length > 0;
        CmdPopup.IsOpen = texte && Cmd.IsKeyboardFocusWithin;
        CmdAide.Text = texte && cmdRes.Count == 0
            ? "Rien trouvé. Essayez « jean 3 16 », « chant 45 » ou des mots du chant."
            : "Entrée : afficher · Tab ou + : ajouter au déroulé · ↑ ↓ : choisir";
        for (int i = 0; i < cmdRes.Count; i++)
        {
            var r = cmdRes[i];
            bool choisi = i == cmdIndex;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = r.Type, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau(choisi ? "Or" : "Brume"), VerticalAlignment = VerticalAlignment.Center });
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = r.Titre, FontWeight = FontWeights.Bold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = r.Sous, FontSize = 13, Foreground = Pinceau(choisi ? "Encre2" : "Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);
            if (choisi)
            {
                var k = new Border { Style = Style_("Touche"), Margin = new Thickness(10, 0, 0, 0), Background = Pinceau("Encre"), Child = new TextBlock { Text = "Entrée", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or") } };
                Grid.SetColumn(k, 2);
                g.Children.Add(k);
            }
            var ligne = new Button { Style = Style_("BtnPlat"), Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 8, 10, 8), Background = choisi ? Pinceau("OrLeger") : Brushes.Transparent };
            ligne.Click += (_, _) => ExecuterCommande(r, false);
            var rang = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            rang.ColumnDefinitions.Add(new ColumnDefinition());
            rang.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rang.Children.Add(ligne);
            if (r.Action == null)
            {
                var plus = new Button { Style = Style_("BtnAjout"), Margin = new Thickness(6, 0, 4, 0), ToolTip = "Ajouter au déroulé (Tab)", VerticalAlignment = VerticalAlignment.Center };
                plus.Click += (_, _) => ExecuterCommande(r, true);
                Grid.SetColumn(plus, 1);
                rang.Children.Add(plus);
            }
            CmdResultats.Children.Add(rang);
        }
    }

    void CmdTouche(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (cmdRes.Count > 0) { cmdIndex = (cmdIndex + 1) % cmdRes.Count; CmdResultatsMaj(); }
                e.Handled = true;
                break;
            case Key.Up:
                if (cmdRes.Count > 0) { cmdIndex = (cmdIndex - 1 + cmdRes.Count) % cmdRes.Count; CmdResultatsMaj(); }
                e.Handled = true;
                break;
            case Key.Enter:
                if (minuterieCmd.IsEnabled) { minuterieCmd.Stop(); CommandeMaj(); }
                if (cmdRes.Count > 0) ExecuterCommande(cmdRes[Math.Clamp(cmdIndex, 0, cmdRes.Count - 1)], false);
                e.Handled = true;
                break;
            case Key.Tab:
                if (minuterieCmd.IsEnabled) { minuterieCmd.Stop(); CommandeMaj(); }
                if (cmdRes.Count > 0 && cmdRes[Math.Clamp(cmdIndex, 0, cmdRes.Count - 1)] is { Action: null } r) { ExecuterCommande(r, true); e.Handled = true; }
                break;
        }
    }

    void ExecuterCommande(ResultatCommande r, bool ajouter)
    {
        switch (r.Action)
        {
            case "minuteur":
                ChoisirDuree(r.Minutes);
                chronoCumul = TimeSpan.Zero;
                chronoDepart = DateTime.Now;
                chrono.Start();
                MinuteurMaj();
                Toast($"Minuteur de prédication : {r.Minutes} min.");
                FinCommande();
                return;
            case "noir":
                BasculerNoir();
                FinCommande();
                return;
            case "retirer":
                R.Effacer();
                FinCommande();
                return;
        }
        if (ajouter)
        {
            var el = r.ChantId != null ? R.AjouterChantAgenda(r.ChantId) : r.Ref is Ref ra ? R.AjouterBibleAgenda(ra) : null;
            if (el != null) { Toast($"« {el.Titre} » ajouté au déroulé."); ChoisirElement(el); }
            Cmd.SelectAll();
            return;
        }
        if (r.ChantId != null)
        {
            if (R.Trouver(r.ChantId) is { Sections.Count: > 0 } c) R.AfficherSection(c.Id, 0);
        }
        else if (r.Ref is Ref rf)
        {
            var s = rf.V1 > 0 ? rf : new Ref(rf.B, rf.C, 1, 1);
            R.Selection = s;
            R.AfficherVerset(s);
        }
        FinCommande();
    }

    void FinCommande()
    {
        Cmd.Clear();
        cmdRes = new();
        CmdPopup.IsOpen = false;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    // ------------------------------------------------------------ écran noir (touche B)
    void BasculerNoir()
    {
        noir = !noir;
        etatNoir = noir ? R.Etat : null;
        MajEcran();
        Toast(noir ? "Écran noir (touche B) : le texte revient avec B ou la diapositive suivante." : "Écran rallumé.");
    }

    // ------------------------------------------------------------ habillage de l'écran
    static Brush FondApercu(string t)
    {
        if (t == "transparent") return Pinceau("Damier");
        Brush b = t switch
        {
            "aube" => new LinearGradientBrush(Color.FromRgb(0x5A, 0x3A, 0x12), Color.FromRgb(0x0A, 0x06, 0x04), 90),
            "lin" => new SolidColorBrush(Color.FromRgb(0xF1, 0xEE, 0xE8)),
            "ciel" => new LinearGradientBrush(Color.FromRgb(0xEE, 0xF5, 0xFD), Color.FromRgb(0xDC, 0xE9, 0xF8), 90),
            "nuit" => new LinearGradientBrush(Color.FromRgb(0x22, 0x30, 0x6A), Color.FromRgb(0x05, 0x08, 0x17), 90),
            "noir" => Brushes.Black,
            "vert" => new SolidColorBrush(Color.FromRgb(0x00, 0xB1, 0x40)),
            _ => new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x6A)),
        };
        if (!b.IsFrozen && b.CanFreeze) b.Freeze();
        return b;
    }

    void ConstruireFonds()
    {
        Fonds.Children.Clear();
        foreach (var (tag, nom) in FondsListe)
        {
            var pile = new StackPanel();
            pile.Children.Add(new Border { Height = 28, CornerRadius = new CornerRadius(7), Background = FondApercu(tag), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1) });
            pile.Children.Add(new TextBlock { Text = nom, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            var b = new Button
            {
                Style = Style_("Btn"), Width = 74, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Content = pile, Tag = tag,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, FontWeight = FontWeights.SemiBold,
                ToolTip = tag switch { "transparent" => "Transparent : pour OBS (la caméra se voit derrière)", "vert" => "Vert : pour l'incrustation", _ => "Fond « " + nom + " »" },
            };
            b.Click += (_, _) => { R.Theme(tag); MajFonds(); };
            Fonds.Children.Add(b);
        }
        MajFonds();
    }

    void MajFonds()
    {
        var t = App.Donnees.Reglages.Theme;
        foreach (var b in Fonds.Children.OfType<Button>())
        {
            bool ok = (string)b.Tag == t;
            b.BorderBrush = Pinceau(ok ? "Blanc" : "Nuit3");
            b.BorderThickness = new Thickness(ok ? 2 : 1);
        }
    }

    // ------------------------------------------------------------ minuteur de prédication
    int DureeMinutes => MinuteurDuree.SelectedItem is ComboBoxItem i && int.TryParse(i.Tag as string, out var m) ? m : 45;

    void ChoisirDuree(int min)
    {
        var item = MinuteurDuree.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == min.ToString());
        if (item == null)
        {
            item = new ComboBoxItem { Tag = min.ToString(), Content = $"{min} min" };
            MinuteurDuree.Items.Add(item);
        }
        MinuteurDuree.SelectedItem = item;
    }

    TimeSpan Ecoule() => chronoCumul + (chronoDepart is DateTime d ? DateTime.Now - d : TimeSpan.Zero);

    static string Mmss(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    /// <summary>Le temps qui reste (« +02:15 » une fois dépassé).</summary>
    (string Texte, bool Depasse) MinuteurPourScene()
    {
        var reste = TimeSpan.FromMinutes(DureeMinutes) - Ecoule();
        return reste < TimeSpan.Zero ? ("+" + Mmss(-reste), true) : (Mmss(reste), false);
    }

    void MinuteurMaj()
    {
        var t = Ecoule();
        var (texte, depasse) = MinuteurPourScene();
        MinuteurTemps.Text = texte;
        MinuteurTemps.Foreground = Pinceau(depasse ? "Direct" : "Blanc");
        double f = Math.Min(1, t.TotalSeconds / Math.Max(1, DureeMinutes * 60));
        double w = (MinuteurBarre.Parent as FrameworkElement)?.ActualWidth ?? 0;
        MinuteurBarre.Width = Math.Max(0, w * f);
        MinuteurBarre.Background = Pinceau(depasse ? "Rouge" : "OrVif");
        MinuteurBtn.Content = chronoDepart != null ? "Pause" : t > TimeSpan.Zero ? "Reprendre" : "Démarrer";
    }

    // ------------------------------------------------------------ annonces
    void AnnoncesMaj()
    {
        var l = R.Annonces.ToList();
        ListeAnnonces.ItemsSource = l;
        ListeAnnonces.SelectedItem = annonceChoisie != null ? l.FirstOrDefault(a => a.Id == annonceChoisie.Id) : null;
        AnnoncesVide.Visibility = l.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AnnonceSupprimer.IsEnabled = annonceChoisie != null;
    }

    /// <summary>Enregistre ce qui est tapé (crée l'annonce au besoin). Null si tout est vide.</summary>
    Annonce? EnregistrerAnnonce()
    {
        var titre = AnnonceTitre.Text.Trim();
        var texte = AnnonceTexte.Text;
        if (texte.Trim().Length == 0) { Toast("Écrivez d'abord le texte de l'annonce."); AnnonceTexte.Focus(); return null; }
        if (annonceChoisie == null) annonceChoisie = R.NouvelleAnnonce(titre, texte);
        else R.ModifierAnnonce(annonceChoisie.Id, titre, texte);
        return annonceChoisie;
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestOngletAgenda() => MontrerOnglet("agenda");
    public void TestOngletAnnonces() => MontrerOnglet("annonces");
    public List<ResultatCommande> TestCommande(string q) { Cmd.Text = q; minuterieCmd.Stop(); CommandeMaj(); return cmdRes; }
    public void TestNoir() => BasculerNoir();
    public bool TestEstNoir => noir;
    public string TestResume => ResumeTexte.Text;
    public void TestChoisirAnnonce(Annonce a) { annonceChoisie = a; AnnonceTitre.Text = a.Titre; AnnonceTexte.Text = a.Texte; AnnoncesMaj(); }
    public Window? TestScene() { SceneBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); return scene; }

    /// <summary>Test : clic sur le « + » du premier chant de la liste. Renvoie le nombre d'éléments ajoutés.</summary>
    public int TestPlusPremierChant()
    {
        ListeChants.UpdateLayout();
        if (ListeChants.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem li) return -1;
        if (Enfants<Button>(li).FirstOrDefault() is not { } bouton) return -2;
        int avant = R.Agenda.Elements.Count;
        bouton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return R.Agenda.Elements.Count - avant;
    }

    /// <summary>Test : clic sur le « + » du premier verset affiché.</summary>
    public int TestPlusPremierVerset()
    {
        if (Versets.Children.OfType<Border>().FirstOrDefault() is not { } ligne || Enfants<Button>(ligne).FirstOrDefault() is not { } bouton) return -2;
        int avant = R.Agenda.Elements.Count;
        bouton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return R.Agenda.Elements.Count - avant;
    }
}
