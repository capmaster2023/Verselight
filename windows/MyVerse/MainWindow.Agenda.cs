using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>Une ligne de l'agenda (liste de gauche) : numéro, titre, type ; « EN COURS » suit l'écran.</summary>
public sealed class LigneAgenda : INotifyPropertyChanged
{
    public required ElementAgenda Element { get; init; }
    public string Num { get; init; } = "";
    public string Titre { get; init; } = "";
    public string Type { get; init; } = "";
    public string Sous { get; init; } = "";
    bool enCours;
    public bool EnCours
    {
        get => enCours;
        set { if (enCours == value) return; enCours = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnCours))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Onglet « Agenda » (5.0, design « Pupitre ») : agendas préparés comme dans VideoPsalm, barre de commande (Ctrl K),
/// notes de prédication → versets (F1 à F9), écran noir, minuteur, et l'assemblée qui suit sur son téléphone.
/// </summary>
public partial class MainWindow
{
    ElementAgenda? elementChoisi;
    List<RefNotes> notesRefs = new();
    List<ResultatCommande> cmdRes = new();
    int cmdIndex;
    bool noir, chargementAgenda;
    EtatEcran? etatNoir;
    string? urlSuivre;
    DateTime? chronoDepart;
    TimeSpan chronoCumul;
    readonly DispatcherTimer minuterieCmd = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer minuterieNotes = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer minuterieSuivre = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer chrono = new() { Interval = TimeSpan.FromMilliseconds(500) };

    void InitAgenda()
    {
        // ---------- agendas
        AgendaChoix.SelectionChanged += (_, _) =>
        {
            if (chargementAgenda || AgendaChoix.SelectedItem is not ComboBoxItem i || i.Tag is not string id || id == R.Agenda.Id) return;
            elementChoisi = null;
            R.ChoisirAgenda(id);
        };
        AgendaNouveau.Click += (_, _) =>
        {
            var nom = Dialogue.Demander(this, "Nom du nouvel agenda :", Regie.NomAgendaParDefaut(), "Nouvel agenda");
            if (nom == null) return;
            elementChoisi = null;
            R.NouvelAgenda(nom);
        };
        AgendaRenommer.Click += (_, _) =>
        {
            var a = R.Agenda;
            var nom = Dialogue.Demander(this, "Nouveau nom de l'agenda :", a.Nom, "Renommer l'agenda");
            if (!string.IsNullOrWhiteSpace(nom)) R.RenommerAgenda(a.Id, nom);
        };
        AgendaSupprimer.Click += (_, _) =>
        {
            var a = R.Agenda;
            if (!Dialogue.Confirmer(this, $"Supprimer l'agenda « {a.Nom} » ({a.Elements.Count} élément{(a.Elements.Count > 1 ? "s" : "")}) ?\n\nLes chants et les Bibles ne sont pas touchés.", "Supprimer")) return;
            elementChoisi = null;
            R.SupprimerAgenda(a.Id);
        };
        ListeAgenda.SelectionChanged += (_, _) =>
        {
            if (chargementAgenda) return;
            if (ListeAgenda.SelectedItem is LigneAgenda l && l.Element != elementChoisi) { elementChoisi = l.Element; AfficherDiapos(); }
            MajBoutonsAgenda();
        };
        ListeAgenda.MouseDoubleClick += (_, _) => { if (ListeAgenda.SelectedItem is LigneAgenda l) R.AfficherElement(l.Element, 0); };
        AgendaHaut.Click += (_, _) => { if (elementChoisi is { } el) R.DeplacerAgenda(el.Id, -1); };
        AgendaBas.Click += (_, _) => { if (elementChoisi is { } el) R.DeplacerAgenda(el.Id, 1); };
        AgendaRetirer.Click += (_, _) =>
        {
            if (elementChoisi is not { } el) return;
            elementChoisi = null;
            R.RetirerAgenda(el.Id);
        };
        AgendaTexte.Click += (_, _) =>
        {
            if (Dialogue.DemanderTexte(this, "Texte, annonce ou prière", "Annonces", "") is not { } t) return;
            ChoisirElement(R.AjouterTexteAgenda(t.Titre, t.Texte));
        };
        ElemAfficher.Click += (_, _) => { if (elementChoisi != null) R.AfficherElement(elementChoisi, 0); };
        ElemModifier.Click += (_, _) => ModifierElement();
        VueAgenda.Checked += (_, _) => { BlocAgenda.Visibility = Visibility.Visible; BlocNotes.Visibility = Visibility.Collapsed; };
        VueNotes.Checked += (_, _) => { BlocAgenda.Visibility = Visibility.Collapsed; BlocNotes.Visibility = Visibility.Visible; };
        DefilDiapos.SizeChanged += (_, _) => LargeurDiapos();
        R.AgendaChange += AgendaMaj;

        // ---------- notes de prédication
        Notes.TextChanged += (_, _) => { minuterieNotes.Stop(); minuterieNotes.Start(); };
        minuterieNotes.Tick += (_, _) => { minuterieNotes.Stop(); NotesMaj(); };
        NotesTout.Click += (_, _) =>
        {
            foreach (var r in notesRefs) R.AjouterBibleAgenda(r.Ref);
            Toast($"{notesRefs.Count} passage{(notesRefs.Count > 1 ? "s ajoutés" : " ajouté")} à l'agenda « {R.Agenda.Nom} ».");
        };

        // ---------- barre de commande
        Cmd.TextChanged += (_, _) =>
        {
            CmdIndice.Visibility = Cmd.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            minuterieCmd.Stop();
            minuterieCmd.Start();
        };
        minuterieCmd.Tick += (_, _) => { minuterieCmd.Stop(); CommandeMaj(); };
        Cmd.PreviewKeyDown += CmdTouche;

        // ---------- écran noir, minuteur, l'assemblée suit
        Noir.Click += (_, _) => BasculerNoir();
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
        minuterieSuivre.Tick += (_, _) => SuivreMaj();
        QrEcran.Checked += (_, _) => ecran?.MontrerQr(SuivreQr.Source, urlSuivre);
        QrEcran.Unchecked += (_, _) => ecran?.MontrerQr(null, null);
        minuterieSuivre.Start();
        SuivreMaj();
        MinuteurMaj();
        AgendaMaj();
    }

    // ------------------------------------------------------------ agenda
    void AgendaMaj()
    {
        var ag = R.Agenda;
        chargementAgenda = true;
        AgendaChoix.Items.Clear();
        foreach (var a in R.Agendas) AgendaChoix.Items.Add(new ComboBoxItem { Content = $"{a.Nom}  ·  {a.Elements.Count}", Tag = a.Id });
        AgendaChoix.SelectedItem = AgendaChoix.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ag.Id);
        chargementAgenda = false;
        if (elementChoisi != null && !ag.Elements.Contains(elementChoisi)) elementChoisi = null;
        ListeAgendaMaj();
        AfficherDiapos();
    }

    void ListeAgendaMaj()
    {
        var ag = R.Agenda;
        var lignes = ag.Elements.Select((e, i) =>
        {
            var (type, sous) = R.Description(e);
            return new LigneAgenda
            {
                Element = e, Num = (i + 1).ToString("00"), Type = type, Sous = sous, EnCours = R.ElementCourant == e,
                Titre = e.Type == "chant" && R.Trouver(e.ChantId) is { } c ? c.Title : e.Titre,
            };
        }).ToList();
        chargementAgenda = true;
        ListeAgenda.ItemsSource = lignes;
        ListeAgenda.SelectedItem = lignes.FirstOrDefault(l => l.Element == elementChoisi);
        chargementAgenda = false;
        AgendaVide.Visibility = lignes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MajBoutonsAgenda();
    }

    void MajBoutonsAgenda()
    {
        int i = elementChoisi != null ? R.Agenda.Elements.IndexOf(elementChoisi) : -1;
        AgendaHaut.IsEnabled = i > 0;
        AgendaBas.IsEnabled = i >= 0 && i < R.Agenda.Elements.Count - 1;
        AgendaRetirer.IsEnabled = i >= 0;
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
        MajBoutonsAgenda();
        AfficherDiapos();
    }

    /// <summary>Les diapositives de l'élément choisi, au centre : clic = afficher, 1 à 9 au clavier.</summary>
    void AfficherDiapos()
    {
        Diapos.Children.Clear();
        var e = elementChoisi;
        ElemBoutons.Visibility = e != null ? Visibility.Visible : Visibility.Collapsed;
        ElemModifier.Visibility = e != null && e.Type is "texte" or "chant" ? Visibility.Visible : Visibility.Collapsed;
        if (e == null)
        {
            ElemType.Text = "";
            ElemTitre.Text = R.Agenda.Elements.Count > 0 ? "Choisissez un élément de l'agenda" : "Préparez le culte";
            if (R.Agenda.Elements.Count == 0)
                Diapos.Children.Add(new TextBlock
                {
                    Style = Style_("Muet"), FontSize = 15, LineHeight = 26, Margin = new Thickness(2, 4, 0, 0),
                    Text = "1. Tapez « chant 12 » ou « jean 3 16 » dans la barre ci-dessus, puis Tab pour l'ajouter à l'agenda.\n" +
                           "2. Ou « + Agenda » dans Bible et Chants, « + Texte » pour une annonce.\n" +
                           "3. Pendant le culte : Entrée affiche, → passe à la diapositive suivante, puis à l'élément suivant.",
                });
            return;
        }
        ElemType.Text = R.Description(e).Type;
        ElemTitre.Text = e.Type == "chant" && R.Trouver(e.ChantId) is { } c ? c.Title : e.Titre;
        var d = R.Diapos(e);
        for (int i = 0; i < d.Count; i++)
        {
            int k = i;
            var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (i < 9)
            {
                var kbd = new Border { Style = Style_("Touche"), Margin = new Thickness(6, 0, 0, 0), Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume") } };
                DockPanel.SetDock(kbd, Dock.Right);
                tete.Children.Add(kbd);
            }
            tete.Children.Add(new TextBlock { Text = d[i].Label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or"), TextTrimming = TextTrimming.CharacterEllipsis });
            var corps = new StackPanel();
            corps.Children.Add(tete);
            corps.Children.Add(new TextBlock { Text = d[i].Texte, FontSize = 14, LineHeight = 19.6, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc"), MaxHeight = 240, TextTrimming = TextTrimming.CharacterEllipsis });
            var carte = new Border { Style = (Style)FindResource("CarteSection"), Child = corps, Tag = k, ToolTip = "Clic : afficher" };
            carte.MouseLeftButtonDown += (_, _) => R.AfficherElement(e, k);
            Diapos.Children.Add(carte);
        }
        if (d.Count == 0)
            Diapos.Children.Add(new TextBlock { Text = e.Type == "chant" ? "Ce chant n'existe plus dans MyVerse." : "Rien à afficher dans cette Bible.", Style = Style_("Muet"), FontSize = 15, Margin = new Thickness(4) });
        DefilDiapos.ScrollToTop();
        LargeurDiapos();
        MarquerDiapos();
    }

    void LargeurDiapos()
    {
        double w = DefilDiapos.ActualWidth - 4;
        if (w <= 0) return;
        int cols = Math.Max(1, (int)((w + 10) / 250));
        double l = Math.Floor((w + 10) / cols) - 10;
        foreach (var c in Diapos.Children.OfType<Border>()) c.Width = l;
    }

    void MarquerDiapos()
    {
        int? pos = elementChoisi != null && R.ElementCourant == elementChoisi ? R.PositionAgenda(elementChoisi) : null;
        foreach (var b in Diapos.Children.OfType<Border>())
        {
            if (pos is int p && b.Tag is int i && i == p) { b.Background = Pinceau("OrLeger"); b.BorderBrush = Pinceau("Or"); }
            else { b.ClearValue(Border.BackgroundProperty); b.ClearValue(Border.BorderBrushProperty); }
        }
    }

    /// <summary>L'écran a changé : « EN COURS » suit, et la liste suit l'élément projeté (Suivant, téléphone).</summary>
    void MajAgendaEcran()
    {
        if (R.ElementCourant is { } el && el != elementChoisi && R.Agenda.Elements.Contains(el)) ChoisirElement(el);
        if (ListeAgenda.ItemsSource is List<LigneAgenda> l)
            foreach (var x in l) x.EnCours = x.Element == R.ElementCourant;
        MarquerDiapos();
    }

    void AfficherAgendaCourant()
    {
        if (elementChoisi == null)
        {
            if (R.Agenda.Elements.FirstOrDefault() is { } premier) { ChoisirElement(premier); R.AfficherElement(premier, 0); }
            else Toast("L'agenda est vide : ajoutez des chants ou des versets (Ctrl K, puis Tab).");
            return;
        }
        R.AfficherElement(elementChoisi, R.ElementCourant == elementChoisi && R.PositionAgenda(elementChoisi) is int p ? p : 0);
    }

    void ModifierElement()
    {
        if (elementChoisi is not { } e) return;
        if (e.Type == "chant") { Editer(R.Trouver(e.ChantId)); AfficherDiapos(); return; }
        if (e.Type == "texte" && Dialogue.DemanderTexte(this, "Modifier le texte", e.Titre, e.Texte ?? "") is { } t)
            R.ModifierTexteAgenda(e.Id, t.Titre, t.Texte);
    }

    // ------------------------------------------------------------ boutons « + » des listes (Chants, Bible)
    void AjouterChantListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Chant c && R.AjouterChantAgenda(c.Id) is { } el)
            Toast($"« {el.Titre} » ajouté à l'agenda « {R.Agenda.Nom} ».");
    }

    void AjouterVersetAgenda(int b, int c, int v)
    {
        var r = sel is Ref s && s.B == b && s.C == c && s.V2 > s.V1 && v >= s.V1 && v <= s.V2 ? s : new Ref(b, c, v, v);
        var el = R.AjouterBibleAgenda(r);
        Toast($"{el.Titre} ajouté à l'agenda « {R.Agenda.Nom} ».");
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

    /// <summary>Test : clic sur le « + » du premier chant de la liste. Renvoie le nombre d'éléments ajoutés.</summary>
    public int TestPlusPremierChant()
    {
        ListeChants.UpdateLayout();
        if (ListeChants.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem li) return -1;
        if (Enfants<Button>(li).FirstOrDefault() is not { } bouton) return -2;
        int avant = R.Agenda.Elements.Count;
        bouton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return R.Agenda.Elements.Count - avant;
    }

    /// <summary>Test : clic sur le « + » du premier verset affiché.</summary>
    public int TestPlusPremierVerset()
    {
        if (Versets.Children.OfType<Border>().FirstOrDefault() is not { } ligne || Enfants<Button>(ligne).FirstOrDefault() is not { } bouton) return -2;
        int avant = R.Agenda.Elements.Count;
        bouton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return R.Agenda.Elements.Count - avant;
    }

    // ------------------------------------------------------------ notes de prédication
    void NotesMaj()
    {
        notesRefs = R.RefsDansNotes(Notes.Text);
        NotesRefs.Children.Clear();
        int n = notesRefs.Count;
        NotesNb.Text = n == 0 ? "Aucun verset trouvé" : $"{n} verset{(n > 1 ? "s trouvés" : " trouvé")}";
        NotesTout.IsEnabled = n > 0;
        for (int i = 0; i < n; i++)
        {
            var r = notesRefs[i];
            int k = i;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = i < 9 ? $"F{i + 1}" : "", FontFamily = (FontFamily)FindResource("Mono"), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center });
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = r.Libelle, FontWeight = FontWeights.Bold, FontSize = 14 });
            txt.Children.Add(new TextBlock { Text = r.Apercu, FontSize = 12, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);
            var ligne = new Button { Style = Style_("BtnPlat"), Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 7, 4, 7), ToolTip = "Afficher" + (i < 9 ? $" (F{i + 1})" : "") };
            ligne.Click += (_, _) => AfficherRefNotes(k);
            var plus = new Button { Style = Style_("BtnPlat"), Content = "＋", FontSize = 16, Padding = new Thickness(12, 6, 12, 6), ToolTip = "Ajouter à l'agenda", VerticalAlignment = VerticalAlignment.Center };
            plus.Click += (_, _) => { R.AjouterBibleAgenda(r.Ref); Toast($"{r.Libelle} ajouté à l'agenda."); };
            var rang = new Grid();
            rang.ColumnDefinitions.Add(new ColumnDefinition());
            rang.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(plus, 1);
            rang.Children.Add(ligne);
            rang.Children.Add(plus);
            NotesRefs.Children.Add(new Border { BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(0, 0, 0, 1), Child = rang });
        }
    }

    bool AfficherRefNotes(int i)
    {
        if (i < 0 || i >= notesRefs.Count) return false;
        var r = notesRefs[i].Ref;
        var s = r.V1 > 0 ? r : new Ref(r.B, r.C, 1, 1);
        R.Selection = s;
        R.AfficherVerset(s);
        return true;
    }

    // ------------------------------------------------------------ barre de commande (Ctrl K)
    void OuvrirCommande()
    {
        MontrerOnglet("agenda");
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
        CmdResultatsBoite.Visibility = cmdRes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CmdAide.Text = Cmd.Text.Trim().Length > 0 && cmdRes.Count == 0
            ? "Rien trouvé. Essayez « jean 3 16 », « chant 45 », « minuteur 35 » ou des mots du chant."
            : "Entrée : afficher · Tab : ajouter à l'agenda · ↑ ↓ : choisir";
        for (int i = 0; i < cmdRes.Count; i++)
        {
            var r = cmdRes[i];
            bool choisi = i == cmdIndex;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = r.Type, FontFamily = (FontFamily)FindResource("Mono"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau(choisi ? "Or" : "Brume"), VerticalAlignment = VerticalAlignment.Center });
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = r.Titre, FontWeight = FontWeights.Bold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = r.Sous, FontSize = 13, Foreground = Pinceau(choisi ? "Encre2" : "Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);
            if (choisi)
            {
                var k = new Border { Style = Style_("Touche"), Margin = new Thickness(10, 0, 0, 0), Background = Pinceau("Encre"), Child = new TextBlock { Text = "Entrée", FontFamily = (FontFamily)FindResource("Mono"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pinceau("Or") } };
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
                var plus = new Button { Style = Style_("BtnPlat"), Content = "＋", FontSize = 17, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(2, 0, 0, 0), ToolTip = "Ajouter à l'agenda (Tab)", VerticalAlignment = VerticalAlignment.Center };
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
            if (el != null) { Toast($"« {el.Titre} » ajouté à l'agenda."); ChoisirElement(el); }
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
        CmdResultatsMaj();
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    // ------------------------------------------------------------ écran noir
    void BasculerNoir()
    {
        noir = !noir;
        etatNoir = noir ? R.Etat : null;
        MajEcran();
        Toast(noir ? "Écran noir : le texte revient avec « Rallumer » ou la diapositive suivante." : "Écran rallumé.");
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

    void MinuteurMaj()
    {
        var t = chronoCumul + (chronoDepart is DateTime d ? DateTime.Now - d : TimeSpan.Zero);
        int total = DureeMinutes * 60;
        MinuteurTemps.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00} / {DureeMinutes:00}:00";
        double f = total > 0 ? Math.Min(1, t.TotalSeconds / total) : 0;
        double w = (MinuteurBarre.Parent as FrameworkElement)?.ActualWidth ?? 0;
        MinuteurBarre.Width = Math.Max(0, w * f);
        MinuteurBarre.Background = Pinceau(t.TotalSeconds > total ? "Rouge" : "Or");
        MinuteurBtn.Content = chronoDepart != null ? "Pause" : t > TimeSpan.Zero ? "Reprendre" : "Démarrer";
    }

    // ------------------------------------------------------------ l'assemblée suit sur son téléphone
    void SuivreMaj()
    {
        var url = App.Telephone.AdresseSuivre();
        if (url != urlSuivre)
        {
            urlSuivre = url;
            SuivreUrl.Text = url ?? "Pas de réseau : branchez le Wi-Fi.";
            try { SuivreQr.Source = url != null ? TelephoneWindow.CodeQr(url) : null; }
            catch (Exception e) { Journal.Erreur("code QR de la page suivre", e); }
            if (QrEcran.IsChecked == true) ecran?.MontrerQr(SuivreQr.Source, url);
        }
        int n = App.Telephone.Suiveurs();
        SuivreNb.Text = n == 0 ? "Personne ne suit pour l'instant" : $"{n} téléphone{(n > 1 ? "s suivent" : " suit")} le culte";
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestOngletAgenda() => MontrerOnglet("agenda");
    public List<ResultatCommande> TestCommande(string q) { Cmd.Text = q; minuterieCmd.Stop(); CommandeMaj(); return cmdRes; }
    public int TestNotes(string notes) { VueNotes.IsChecked = true; Notes.Text = notes; minuterieNotes.Stop(); NotesMaj(); return notesRefs.Count; }
    public void TestVueAgenda() => VueAgenda.IsChecked = true;
    public void TestNoir() => BasculerNoir();
    public bool TestEstNoir => noir;
}
