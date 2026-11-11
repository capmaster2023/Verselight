using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Colonne de contrôle (design « Pupitre », MyChurch 3.2) : l'audio en bref. Une ligne par source du mélangeur
/// (nom, entrée de la carte son, vumètre, sourdine M) et « + Micros » pour ajouter une bande par entrée.
/// Le mélangeur complet (volumes, filtres, écoute) reste dans Display.
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
        var micros = new Button
        {
            Style = S("BtnPlat"), Content = "+ Micros", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = P("Or"), Padding = new Thickness(6, 2, 6, 2),
            ToolTip = "Ajouter les micros de la carte son : une bande par entrée, comme une source dans OBS",
        };
        micros.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.AjouterMicros();
        var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(micros, Dock.Right);
        tete.Children.Add(micros);
        tete.Children.Add(new TextBlock { Text = "Audio", FontSize = 15, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center });
        vide = new TextBlock
        {
            Text = "Aucune source audio. « + Micros » ajoute les micros de la carte son, une bande par entrée.", Style = S("Muet"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 8),
        };
        var pile = new StackPanel();
        pile.Children.Add(tete);
        pile.Children.Add(lignes);
        pile.Children.Add(vide);
        Child = pile;
        A.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        minuterie.Tick += (_, _) => Niveaux();
        IsVisibleChanged += (_, _) => { if (IsVisible) minuterie.Start(); else minuterie.Stop(); };
        Maj();
    }

    /// <summary>Reconstruit les lignes si les sources ont changé (ajout, nom, entrée, sourdine).</summary>
    void Maj()
    {
        string sig = string.Join("|", C.Sources.Select(s => $"{s.Id}/{s.Name}/{s.Kind}/{s.Channel}/{s.DeviceId}/{s.DeviceName}/{s.Muted}"));
        if (sig == signature) return;
        signature = sig;
        lignes.Children.Clear();
        sources.Clear();
        foreach (var s in C.Sources) lignes.Children.Add(Ligne(s));
        vide.Visibility = C.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement Ligne(AudioSource s)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nom = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        nom.Children.Add(new TextBlock { Text = s.Name, FontSize = 13, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        var sous = new TextBlock { FontSize = 11, Foreground = P("Brume2"), TextTrimming = TextTrimming.CharacterEllipsis };
        string peripherique = s.DeviceId == "default" || s.DeviceName.Length == 0 ? "Windows" : AudioPanel.NomCourt(s.DeviceName);
        if (s.Kind == AudioInputKind.Microphone && s.Channel > 0)
        {
            // « Entrée 1 · UMC404HD » : le numéro de l'entrée en ambre, comme dans le mélangeur
            sous.Inlines.Add(new Run("Entrée "));
            sous.Inlines.Add(new Run(s.Channel.ToString()) { Foreground = P("Or"), FontWeight = FontWeights.ExtraBold });
            sous.Inlines.Add(new Run(" · " + peripherique));
        }
        else sous.Text = s.Kind switch { AudioInputKind.Microphone => "Micro · " + peripherique, AudioInputKind.Sortie => "Sortie audio · " + peripherique, _ => "Média" };
        nom.Children.Add(sous);
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
