using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Colonne de contrôle (design « Pupitre », MyChurch 3.2) : l'audio en bref. Une ligne par source du mélangeur
/// (nom, vumètre, sourdine M) et « + Source » pour en ajouter une, comme dans OBS.
/// Le mélangeur complet (volumes, filtres, monitoring, pistes) reste dans Display.
/// </summary>
public sealed class AudioListe : Border
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly StackPanel lignes = new();
    readonly TextBlock vide;
    readonly List<(AudioSource Source, Vumetre Vu, Button Muet)> sources = new();
    readonly DispatcherTimer minuterie = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
    string signature = "";

    public AudioListe()
    {
        Style = S("Carte");
        Padding = new Thickness(14, 12, 12, 6);
        var plus = new Button
        {
            Style = S("BtnPlat"), Content = "+ Source", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = P("Or"), Padding = new Thickness(6, 2, 6, 2),
            ToolTip = "Ajouter une capture audio (entrée ou sortie) ou un média, comme dans OBS",
        };
        plus.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.AjouterSource(plus);
        var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(plus, Dock.Right);
        tete.Children.Add(plus);
        tete.Children.Add(new TextBlock { Text = "Audio", FontSize = 15, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center });
        vide = new TextBlock
        {
            Text = "Aucune source audio. « + Source » ajoute une capture audio (entrée ou sortie) ou un média.", Style = S("Muet"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 8),
        };
        var pile = new StackPanel();
        pile.Children.Add(tete);
        pile.Children.Add(lignes);
        pile.Children.Add(vide);
        Child = pile;
        A.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        A.PeripheriquesChange += () => Dispatcher.BeginInvoke(new Action(Maj));
        minuterie.Tick += (_, _) => Niveaux();
        IsVisibleChanged += (_, _) => { if (IsVisible) minuterie.Start(); else minuterie.Stop(); };
        Maj();
    }

    /// <summary>Reconstruit les lignes si les sources ont changé (ajout, nom, sourdine) : les mêmes que le mélangeur, dans le même ordre.</summary>
    void Maj()
    {
        var visibles = C.Sources.ToList();
        string sig = string.Join("|", visibles.Select(s => $"{s.Id}/{s.Name}/{s.Muted}/{A.Inactive(s)}"));
        if (sig == signature) return;
        signature = sig;
        lignes.Children.Clear();
        sources.Clear();
        foreach (var s in visibles) lignes.Children.Add(Ligne(s));
        vide.Visibility = visibles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement Ligne(AudioSource s)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nom = new TextBlock
        {
            Text = s.Name, FontSize = 13, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0), ToolTip = AudioPanel.Description(s), Opacity = A.Inactive(s) ? 0.55 : 1,
        };
        g.Children.Add(nom);
        var vu = new Vumetre(6) { VerticalAlignment = VerticalAlignment.Center, Gris = s.Muted, ToolTip = "Niveau de la source (après volume et filtres)" };
        Grid.SetColumn(vu, 1);
        g.Children.Add(vu);
        var m = new Button
        {
            Style = S("Btn"), Width = 30, Height = 26, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Content = "M", FontSize = 12, FontWeight = FontWeights.ExtraBold,
            ToolTip = "Sourdine : couper ou rétablir le son de cette source",
        };
        Allumer(m, s.Muted);
        m.Click += (_, _) =>
        {
            s.Muted = !s.Muted;
            Allumer(m, s.Muted);
            vu.Gris = s.Muted;
            A.Apply();
        };
        Grid.SetColumn(m, 2);
        g.Children.Add(m);
        sources.Add((s, vu, m));
        return new Border { BorderBrush = P("Nuit3"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 7, 0, 7), Child = g };
    }

    /// <summary>M en rouge plein quand la source est coupée.</summary>
    static void Allumer(Button b, bool muet)
    {
        if (muet)
        {
            b.Background = P("Direct");
            b.BorderBrush = P("Direct");
            b.Foreground = Brushes.White;
        }
        else
        {
            b.ClearValue(Control.BackgroundProperty);
            b.ClearValue(Control.BorderBrushProperty);
            b.ClearValue(Control.ForegroundProperty);
        }
    }

    void Niveaux()
    {
        foreach (var (s, vu, _) in sources)
        {
            var (g, d) = A.Level(s.Id);
            vu.Niveau(Math.Max(g, d));
        }
    }

    /// <summary>Nombre de lignes (test automatique).</summary>
    public int Lignes => sources.Count;
}
