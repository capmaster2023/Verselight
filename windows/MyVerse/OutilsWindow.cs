using System.Windows;
using System.Windows.Controls;

namespace MyVerse.Ui;

/// <summary>
/// La fenêtre Paramètres d'avant la 3.1 : les mêmes réglages que la page Réglages (audio, MyVirtualSound, enregistrements,
/// code source, mise à jour, transfert, journal), dans une fenêtre. Gardée pour le test automatique et les anciens raccourcis.
/// </summary>
public sealed class OutilsWindow : FenetreDialogue
{
    public OutilsPanel Outils { get; } = new();

    public OutilsWindow(bool chercher)
    {
        Title = "Réglages";
        Width = 900; Height = 780; MinWidth = 720; MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(BackgroundProperty, "Page");
        var fermer = new Button { Style = MainWindow.Style_("Btn"), Content = "Fermer", Height = 40, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, IsCancel = true };
        fermer.Click += (_, _) => Close();
        var tete = new DockPanel { Margin = new Thickness(28, 22, 28, 14) };
        DockPanel.SetDock(fermer, Dock.Right);
        tete.Children.Add(fermer);
        tete.Children.Add(new TextBlock { Text = "Réglages", Style = MainWindow.Style_("TitrePage"), FontSize = 24, VerticalAlignment = VerticalAlignment.Center });
        var pile = new StackPanel();
        pile.Children.Add(new Audio.AudioSettingsPanel());
        pile.Children.Add(Outils);
        var defil = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(28, 0, 22, 12) };
        var racine = new DockPanel();
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        racine.Children.Add(defil);
        Content = racine;
        if (chercher) Loaded += async (_, _) => await Outils.ChercherMaintenant();
    }
}
