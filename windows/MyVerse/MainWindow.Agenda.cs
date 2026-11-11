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

/// <summary>Une carte du déroulé (design « Pupitre ») : titre, « heure · type · détail », trait de la couleur du type ; « ● DIRECT » et « passé » suivent l'écran.</summary>
public sealed class LigneAgenda : INotifyPropertyChanged
{
    public required ElementAgenda Element { get; init; }
    public string Titre { get; init; } = "";
    /// <summary>« Lecture · Jean 3:16-17 » : le type et le détail (l'heure s'ajoute devant dans <see cref="Ligne2"/>).</summary>
    public string Sous { get; init; } = "";
    public Brush? Couleur { get; init; }
    string heure = "";
    bool enCours, fait;
    public string Heure { get => heure; set { if (heure == value) return; heure = value; Prevenir(nameof(Heure)); Prevenir(nameof(Ligne2)); } }
    public string Ligne2 => heure.Length > 0 ? heure + " · " + Sous : Sous;
    public bool EnCours { get => enCours; set { if (enCours == value) return; enCours = value; Prevenir(nameof(EnCours)); } }
    public bool Fait { get => fait; set { if (fait == value) return; fait = value; Prevenir(nameof(Fait)); } }
    void Prevenir(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Page Culte (design « Pupitre », MyChurch 3.2) : le déroulé du culte en cartes à gauche (comme l'agenda de VideoPsalm),
/// toutes ses diapositives en vignettes au centre (MainWindow.Vignettes.cs), résumé à partager, compte à rebours d'accueil,
/// habillage de l'écran, écran de scène ; et la bibliothèque Display.
/// </summary>
public partial class MainWindow
{
    ElementAgenda? elementChoisi;
    Annonce? annonceChoisie;
    List<ResultatCommande> cmdRes = new();
    int cmdIndex;
    bool noir, chargementAgenda;
    EtatEcran? etatNoir;
    SceneWindow? scene;
    readonly DispatcherTimer minuterieCmd = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer ticCompte = new() { Interval = TimeSpan.FromMilliseconds(500) };

    static readonly (string Tag, string Nom)[] FondsListe =
        { ("aube", "Aube"), ("lin", "Lin"), ("ciel", "Ciel"), ("nuit", "Nuit"), ("noir", "Noir"), ("vert", "Vert"), ("transparent", "Transparent") };

    void InitAgenda()
    {
        // ---------- déroulé du culte
        AgendaMenu.Click += (_, _) => MenuAgendas();
        AgendaAjouter.Click += (_, _) => MenuAjouter();
        ListeAgenda.SelectionChanged += (_, _) =>
        {
            if (chargementAgenda) return;
            if (ListeAgenda.SelectedItem is LigneAgenda l && l.Element != elementChoisi) ChoisirElement(l.Element);
        };
        ListeAgenda.MouseDoubleClick += (_, _) => { if (ListeAgenda.SelectedItem is LigneAgenda l) R.AfficherElement(l.Element, 0); };
        ListeAgenda.PreviewMouseRightButtonUp += (_, _) => { if (ListeAgenda.SelectedItem is LigneAgenda l) MenuElement(l.Element); };
        ListeAgenda.PreviewKeyDown += (_, e) =>
        {
            if (elementChoisi is not { } el) return;
            if (e.Key == Key.Delete) { RetirerElement(el); e.Handled = true; }
            else if (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down) { R.DeplacerAgenda(el.Id, e.SystemKey == Key.Up ? -1 : 1); e.Handled = true; }
        };
        R.AgendaChange += AgendaMaj;
        InitVignettes();

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

        // ---------- habillage, compte à rebours, écran de scène
        ConstruireFonds();
        ticCompte.Tick += (_, _) => R.TicCompte();
        ticCompte.Start();
        SceneBtn.Click += (_, _) =>
        {
            if (scene != null) { scene.Activate(); return; }
            scene = new SceneWindow { Owner = null };
            scene.Closed += (_, _) => scene = null;
            scene.Show();
        };

        // ---------- displays
        R.AnnoncesChange += AnnoncesMaj;
        ListeAnnonces.SelectionChanged += (_, _) =>
        {
            if (ListeAnnonces.SelectedItem is not Annonce a || a == annonceChoisie) return;
            annonceChoisie = a;
            AnnonceTitre.Text = a.Titre;
            AnnonceTexte.Text = a.Source != null ? a.Description : a.Texte;
            AnnonceTexte.IsReadOnly = a.Source != null;
            DisplayReconnecter.Visibility = a.Source?.Type is "camera" or "fenetre" ? Visibility.Visible : Visibility.Collapsed;
        };
        AnnonceEnregistrer.Click += (_, _) => { if (EnregistrerAnnonce() != null) Toast("Display enregistré."); };
        AnnonceAfficher.Click += (_, _) => { if (EnregistrerAnnonce() is { } a) R.AfficherAnnonce(a); };
        AnnonceAjouter.Click += (_, _) =>
        {
            if (EnregistrerAnnonce() is { } a && R.AjouterAnnonceAgenda(a.Id) is { } el) Toast($"« {el.Titre} » ajoutée au déroulé « {R.Agenda.Nom} ».");
        };
        AnnonceSupprimer.Click += (_, _) =>
        {
            if (annonceChoisie is not { } a || !Dialogue.Confirmer(this, $"Supprimer le display « {a.Titre} » ?", "Supprimer")) return;
            R.SupprimerAnnonce(a.Id);
            annonceChoisie = null;
            AnnonceTitre.Text = AnnonceTexte.Text = "";
        };

        AnnoncesMaj();
        AgendaMaj();
    }

    // ------------------------------------------------------------ déroulé du culte
    void AgendaMaj()
    {
        if (elementChoisi != null && !R.Agenda.Elements.Contains(elementChoisi)) elementChoisi = null;
        ListeAgendaMaj();
        VignettesMaj();
    }

    void ListeAgendaMaj()
    {
        var lignes = R.Agenda.Elements.Select(e =>
        {
            var (_, sous) = R.Description(e);
            var l = new LigneAgenda
            {
                Element = e, Sous = TypeLib(e) + " · " + sous, Titre = TitreElement(e), Couleur = Pinceau(CleType(e)),
            };
            MajLigne(l);
            return l;
        }).ToList();
        chargementAgenda = true;
        ListeAgenda.ItemsSource = lignes;
        ListeAgenda.SelectedItem = lignes.FirstOrDefault(l => l.Element == elementChoisi);
        chargementAgenda = false;
        AgendaVide.Visibility = lignes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MajEntete();
    }

    void MajLigne(LigneAgenda l)
    {
        var e = l.Element;
        l.EnCours = R.ElementCourant == e;
        l.Fait = R.EstFait(e);
        l.Heure = e.Heure ?? (l.Fait && R.HeureAffiche(e) is DateTime h ? h.ToString("HH:mm") : "");
    }

    static string TypeLib(ElementAgenda e) => e.Type switch { "chant" => "Chant", "bible" => "Lecture", "compte" => "Accueil", _ => "Display" };

    /// <summary>Choisit un élément du déroulé (carte ambre) ; avec <paramref name="defiler"/>, les vignettes défilent jusqu'à lui.</summary>
    void ChoisirElement(ElementAgenda? e, bool defiler = true)
    {
        elementChoisi = e;
        if (ListeAgenda.ItemsSource is List<LigneAgenda> l)
        {
            chargementAgenda = true;
            ListeAgenda.SelectedItem = l.FirstOrDefault(x => x.Element == e);
            chargementAgenda = false;
            if (ListeAgenda.SelectedItem != null) ListeAgenda.ScrollIntoView(ListeAgenda.SelectedItem);
        }
        MajTitreElement();
        if (defiler) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => DefilerVers(e)));
    }

    /// <summary>L'écran a changé : « ● DIRECT », éléments passés, vignettes « À L'ÉCRAN » et « SUIVANT ».</summary>
    void MajAgendaEcran()
    {
        GrilleDirect();
        if (R.ElementCourant is { } el && el != elementChoisi && R.Agenda.Elements.Contains(el)) ChoisirElement(el, false);
        if (ListeAgenda.ItemsSource is List<LigneAgenda> l)
            foreach (var x in l) MajLigne(x);
        // l'habillage (fond, taille, présentation) a changé : les vignettes sont redessinées
        if (Habillage(R.Etat) != habillageVignettes) VignettesMaj();
        else VignettesEtat(true);
        MajEntete();
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
        m.Items.Add(new Separator());
        m.Items.Add(Article("Envoyer le déroulé sur WhatsApp", () => PartagerWhatsApp(R.DeroulePartage())));
        m.Items.Add(Article("Envoyer le résumé du culte sur WhatsApp", () => PartagerWhatsApp(R.ResumePartage())));
        m.Items.Add(new Separator());
        m.Items.Add(Article("Supprimer ce culte", () =>
        {
            if (!Dialogue.Confirmer(this, $"Supprimer « {actuel.Nom} » ({actuel.Elements.Count} élément(s)) ?\n\nLes chants, les Bibles et les displays ne sont pas touchés.", "Supprimer")) return;
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
        var displays = new MenuItem { Header = "Display de la bibliothèque" };
        if (R.Annonces.Count == 0) displays.Items.Add(new MenuItem { Header = "(aucune : onglet Display)", IsEnabled = false });
        foreach (var a in R.Annonces)
        {
            var id = a.Id;
            displays.Items.Add(Article(a.Titre, () => { if (R.AjouterAnnonceAgenda(id) is { } el) ChoisirElement(el); }));
        }
        m.Items.Add(displays);
        m.Items.Add(Article("Texte ou prière…", () =>
        {
            if (Dialogue.DemanderTexte(this, "Texte, display ou prière", "Prière", "Prions ensemble") is { } t) ChoisirElement(R.AjouterTexteAgenda(t.Titre, t.Texte));
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
        if (onglet is not ("agenda" or "annonces" or "duo")) MontrerOnglet("agenda");
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
        // une pastille par fond : un carré de la couleur et son nom ; le fond choisi est cerclé de noir
        foreach (var (tag, nom) in FondsListe)
        {
            var pile = new StackPanel { Orientation = Orientation.Horizontal };
            pile.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = FondApercu(tag), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center });
            pile.Children.Add(new TextBlock { Text = nom, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) });
            var b = new Button
            {
                Style = Style_("Btn"), Height = 42, Padding = new Thickness(12, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8), Content = pile, Tag = tag,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = tag switch { "transparent" => "Fond transparent", "vert" => "Vert : pour l'incrustation", _ => "Fond « " + nom + " »" },
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

    // ------------------------------------------------------------ displays
    void AnnoncesMaj()
    {
        var l = R.Annonces.ToList();
        ListeAnnonces.ItemsSource = l;
        ListeAnnonces.SelectedItem = annonceChoisie != null ? l.FirstOrDefault(a => a.Id == annonceChoisie.Id) : null;
        AnnoncesVide.Visibility = l.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AnnonceSupprimer.IsEnabled = annonceChoisie != null;
        GrilleMaj();
        MajEntete();
    }

    /// <summary>Enregistre ce qui est tapé (crée le display au besoin). Null si tout est vide.</summary>
    Annonce? EnregistrerAnnonce()
    {
        var titre = AnnonceTitre.Text.Trim();
        var texte = annonceChoisie?.Source != null ? "" : AnnonceTexte.Text;
        if (annonceChoisie?.Source == null && texte.Trim().Length == 0) { Toast("Écrivez d'abord le texte de le display."); AnnonceTexte.Focus(); return null; }
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
    public string TestResume => R.Resume();
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
