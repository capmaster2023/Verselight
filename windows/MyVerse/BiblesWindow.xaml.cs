using System.Windows;
using System.Windows.Controls;

namespace MyVerse.Ui;

/// <summary>Fenêtre « Bibles » : version principale, 2e version (bilingue), Bibles importées (supprimer, importer).</summary>
public partial class BiblesWindow : FenetreDialogue
{
    bool remplissage;

    public BiblesWindow()
    {
        InitializeComponent();
        Remplir();
        Principale.SelectionChanged += (_, _) =>
        {
            if (remplissage || Principale.SelectedItem is not ComboBoxItem i) return;
            App.Bibles.Choisir((string)i.Tag);
            Remplir();
        };
        Seconde.SelectionChanged += (_, _) =>
        {
            if (remplissage || Seconde.SelectedItem is not ComboBoxItem i) return;
            var id = (string)i.Tag;
            Etat.Text = id.Length > 0 ? "Chargement de la 2e version…" : "";
            App.Bibles.DefinirSeconde(id);
            Etat.Text = App.Bibles.IdSeconde.Length > 0 ? (App.Bibles.Seconde != null ? "Versets bilingues activés." : "Cette version n'a pas pu être chargée.") : "";
            Remplir();
        };
        Importer.Click += (_, _) =>
        {
            if (Owner is MainWindow m) m.ImporterBible(t => { Etat.Text = t; Remplir(); });
        };
        Fermer.Click += (_, _) => Close();
        App.Bibles.Change += Remplir;
        Closed += (_, _) => App.Bibles.Change -= Remplir;
    }

    void Remplir()
    {
        remplissage = true;
        var vb = App.Bibles;
        Principale.Items.Clear();
        Seconde.Items.Clear();
        Seconde.Items.Add(new ComboBoxItem { Content = "Aucune", Tag = "" });
        foreach (var v in vb.Toutes())
        {
            Principale.Items.Add(new ComboBoxItem { Content = $"{v.Nom} ({v.Court})", Tag = v.Id });
            Seconde.Items.Add(new ComboBoxItem { Content = $"{v.Nom} ({v.Court})", Tag = v.Id });
        }
        Principale.SelectedItem = Principale.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == vb.IdPrincipale);
        Seconde.SelectedItem = Seconde.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == vb.IdSeconde) ?? Seconde.Items[0];
        Liste.Children.Clear();
        var importees = vb.Toutes().Where(v => !v.Integree).ToList();
        if (importees.Count == 0) Liste.Children.Add(new TextBlock { Text = "Aucune Bible importée.", Style = Style_("Muet") });
        foreach (var v in importees)
        {
            var ligne = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var cadre = new Border { Background = Pinceau("Nuit2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Child = ligne };
            var x = new Button { Style = Style_("BtnDanger"), Content = "Supprimer" };
            var id = v.Id;
            var nom = v.Nom;
            x.Click += (_, _) =>
            {
                if (!Dialogue.Confirmer(this, $"Supprimer « {nom} » ?", "Supprimer")) return;
                App.Bibles.Supprimer(id);
                Etat.Text = $"« {nom} » supprimée.";
                Remplir();
            };
            DockPanel.SetDock(x, Dock.Right);
            ligne.Children.Add(x);
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run($"{v.Nom} ({v.Court})") { FontWeight = FontWeights.Bold });
            t.Inlines.Add(new System.Windows.Documents.Run($"  {v.Versets} versets") { Foreground = Pinceau("Brume2"), FontSize = 13 });
            ligne.Children.Add(t);
            cadre.Margin = new Thickness(0, 0, 0, 6);
            ligne.Margin = new Thickness(0);
            Liste.Children.Add(cadre);
        }
        remplissage = false;
    }
}
