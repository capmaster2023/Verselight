using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Éditeur de chant : un chant collé (même en désordre) est rangé tout seul en diapositives de 3 à 5 lignes ;
/// Entrée devant une ligne = nouvelle diapositive ; aperçu des diapositives à droite.
/// </summary>
public partial class EditeurWindow : FenetreDialogue
{
    readonly string? id;
    readonly string recueil;
    string brut = "";
    readonly DispatcherTimer minuterie = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>Identifiant du chant enregistré (null si annulé).</summary>
    public string? IdEnregistre { get; private set; }

    public EditeurWindow(Chant? c, int? focus = null, string? recueilNouveau = null)
    {
        InitializeComponent();
        id = c?.Id;
        recueil = recueilNouveau ?? "";
        Titre.Text = c?.Title ?? "";
        Texte.Text = c != null ? ChantsFormats.VersTexte(c.Sections) : "";
        int l = App.Donnees.Reglages.LignesDiapo;
        Lignes.SelectedItem = Lignes.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == l.ToString()) ?? Lignes.Items[0];
        Apercu_();

        DataObject.AddPastingHandler(Texte, Coller);
        Texte.TextChanged += (_, _) => { minuterie.Stop(); minuterie.Start(); };
        minuterie.Tick += (_, _) => { minuterie.Stop(); Apercu_(); };
        Ranger.Click += (_, _) => { if (brut.Length == 0) brut = MyVerse.Ranger.SansEntetes(Texte.Text); RangerTexte(brut); };
        Lignes.SelectionChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            App.Donnees.Reglages.LignesDiapo = NbLignes;
            App.Donnees.SauverReglages();
            if (brut.Length == 0) brut = MyVerse.Ranger.SansEntetes(Texte.Text);
            if (brut.Trim().Length > 0) RangerTexte(brut);
        };
        Brut.Click += (_, _) => { Texte.Text = brut; Apercu_(); };
        Annuler.Click += (_, _) => Close();
        Enregistrer.Click += (_, _) => Sauver();
        Loaded += (_, _) =>
        {
            if (c != null && focus is int f)
            {
                // curseur au début de la strophe double-cliquée
                int pos = c.Sections.Take(f).Sum(s => $"[{s.Label}]\n{s.Text}".Length + 2);
                Texte.Focus();
                Texte.Select(Math.Min(pos, Texte.Text.Length), 0);
                int ligne = Texte.GetLineIndexFromCharacterIndex(Math.Min(pos, Math.Max(0, Texte.Text.Length - 1)));
                if (ligne >= 0) Texte.ScrollToLine(ligne);
            }
            else if (c == null) Texte.Focus();
            else Titre.Focus();
        };
    }

    int NbLignes => int.TryParse((Lignes.SelectedItem as ComboBoxItem)?.Tag as string, out var n) ? n : 0;

    /// <summary>Un chant complet collé dans l'éditeur vide (ou tout sélectionné) : rangé directement.</summary>
    void Coller(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        var t = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        bool tout = Texte.SelectionLength == Texte.Text.Length;
        if (t.Replace("\r\n", "\n").Split('\n').Count(x => x.Trim().Length > 0) >= 3 && (Texte.Text.Trim().Length == 0 || tout))
        {
            e.CancelCommand();
            brut = t.Replace("\r\n", "\n");
            Dispatcher.BeginInvoke(new Action(() => RangerTexte(brut)));
        }
    }

    void RangerTexte(string texte, bool silence = false)
    {
        var r = MyVerse.Ranger.Ranger_(texte, NbLignes, Titre.Text);
        if (r.Sections.Count == 0) return;
        if (Titre.Text.Trim().Length == 0 && r.Titre.Length > 0) Titre.Text = r.Titre;
        Texte.Text = ChantsFormats.VersTexte(r.Sections);
        Brut.Visibility = brut.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Apercu_();
        if (!silence && Owner is MainWindow m) m.Toast($"Chant rangé : {r.Sections.Count} diapositive{(r.Sections.Count > 1 ? "s" : "")}.");
    }

    /// <summary>Pour le test automatique : comme un chant collé.</summary>
    public void TestColler(string t)
    {
        brut = t.Replace("\r\n", "\n");
        RangerTexte(brut, true);
    }

    void Apercu_()
    {
        var secs = ChantsFormats.SplitSections(Texte.Text);
        Compte.Text = secs.Count > 0 ? $"({secs.Count})" : "";
        Dialogue.Miniatures(Apercu, secs.Select((s, i) => ($"{i + 1}. {(s.Label.Length > 0 ? s.Label : "Strophe")}", s.Text)));
    }

    void Sauver()
    {
        var sections = ChantsFormats.SplitSections(Texte.Text);
        if (sections.Count == 0)
        {
            Dialogue.Informer(this, "Le chant est vide.");
            return;
        }
        IdEnregistre = App.Regie.EnregistrerChant(id, Titre.Text, sections, recueil);
        DialogResult = true;
    }
}
