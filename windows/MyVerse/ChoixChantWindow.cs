using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace MyVerse.Ui;

/// <summary>
/// Choisir un chant pour un élément d'un dossier Duo : d'abord le recueil (une église en a souvent plusieurs),
/// puis le numéro ou des mots du titre. Double-clic ou Entrée pour choisir.
/// </summary>
public sealed class ChoixChantWindow : FenetreDialogue
{
    const int Max = 300;
    static Regie R => App.Regie;
    readonly ComboBox recueil = new() { MinHeight = 40 };
    readonly TextBox recherche;
    readonly ListBox liste;
    readonly TextBlock compte = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Threading.DispatcherTimer attente = new() { Interval = TimeSpan.FromMilliseconds(160) };

    /// <summary>Le chant choisi (null si l'utilisateur annule).</summary>
    public Chant? Choisi { get; private set; }

    public ChoixChantWindow(string? recueilParDefaut)
    {
        Title = "Choisir un chant";
        Width = 600;
        Height = 680;
        MinWidth = 460;
        MinHeight = 440;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");

        var racine = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = "Choisir un chant", Style = Style_("Titre2") });
        tete.Children.Add(new TextBlock { Text = "Choisissez le recueil, puis tapez le numéro ou des mots du titre.", Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
        tete.Children.Add(new TextBlock { Text = "RECUEIL", Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 8) });
        recueil.Items.Add(new ComboBoxItem { Content = $"Tous les recueils · {R.Chants.Count} chants", Tag = null });
        foreach (var (nom, n) in R.Recueils())
            recueil.Items.Add(new ComboBoxItem { Content = (nom.Length > 0 ? nom : "Sans recueil") + $" · {n} chant" + (n > 1 ? "s" : ""), Tag = nom });
        recueil.SelectedItem = recueil.Items.OfType<ComboBoxItem>().FirstOrDefault(i => recueilParDefaut != null && i.Tag is string t && t == recueilParDefaut) ?? recueil.Items[0];
        recueil.SelectionChanged += (_, _) => Remplir();
        tete.Children.Add(recueil);
        tete.Children.Add(new TextBlock { Text = "NUMÉRO OU TITRE", Style = Style_("CarteTitre"), Margin = new Thickness(0, 16, 0, 8) });
        recherche = new TextBox { Style = Style_("Champ"), FontSize = 15, ToolTip = "Exemples : 45, grand dieu, à toi la gloire" };
        recherche.TextChanged += (_, _) => { attente.Stop(); attente.Start(); };
        recherche.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && liste!.Items.Count > 0)
            {
                liste.SelectedIndex = Math.Min(liste.Items.Count - 1, liste.SelectedIndex + 1);
                liste.ScrollIntoView(liste.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && liste!.Items.Count > 0)
            {
                liste.SelectedIndex = Math.Max(0, liste.SelectedIndex - 1);
                liste.ScrollIntoView(liste.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { Valider(); e.Handled = true; }
        };
        tete.Children.Add(recherche);
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var oui = new Button { Style = Style_("BtnOr"), Content = "Choisir", MinWidth = 120, Height = 40, Margin = new Thickness(10, 0, 0, 0) };
        oui.Click += (_, _) => Valider();
        DockPanel.SetDock(oui, Dock.Right);
        bas.Children.Add(oui);
        var non = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 40 };
        non.Click += (_, _) => Close();
        DockPanel.SetDock(non, Dock.Right);
        bas.Children.Add(non);
        compte.Foreground = Pinceau("Brume");
        DockPanel.SetDock(compte, Dock.Left);
        bas.Children.Add(compte);
        DockPanel.SetDock(bas, Dock.Bottom);
        racine.Children.Add(bas);

        liste = new ListBox
        {
            Style = Style_("ListeChants"), ItemContainerStyle = Style_("ElementChant"), Margin = new Thickness(0, 14, 0, 0),
            // le titre, puis le recueil en plus petit
            ItemTemplate = (DataTemplate)XamlReader.Parse(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<StackPanel Margin='2,3,2,3'><TextBlock Text='{Binding Title}' FontSize='15' TextTrimming='CharacterEllipsis' />" +
                "<TextBlock Text='{Binding Author}' FontSize='12' Opacity='0.6' TextTrimming='CharacterEllipsis' /></StackPanel></DataTemplate>"),
        };
        liste.MouseDoubleClick += (_, _) => Valider();
        liste.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Valider(); e.Handled = true; } };
        racine.Children.Add(liste);
        Content = racine;

        attente.Tick += (_, _) => { attente.Stop(); Remplir(); };
        Loaded += (_, _) => recherche.Focus();
        Remplir();
    }

    void Remplir()
    {
        string? rec = (recueil.SelectedItem as ComboBoxItem)?.Tag as string;
        var res = R.Filtrer(recherche.Text, rec);
        liste.ItemsSource = res.Take(Max).ToList();
        compte.Text = res.Count == 0 ? "Aucun chant" : res.Count > Max ? $"{Max} premiers sur {res.Count}" : res.Count == 1 ? "1 chant" : $"{res.Count} chants";
        if (res.Count > 0) liste.SelectedIndex = 0;
    }

    void Valider()
    {
        if (liste.SelectedItem is not Chant c) return;
        Choisi = c;
        DialogResult = true;
    }
}
