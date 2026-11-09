using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Écran de scène (pour le prédicateur et la chorale) : l'heure, le minuteur de prédication,
/// ce qui est à l'écran et ce qui vient ensuite. Double-clic ou F11 : plein écran.
/// </summary>
public sealed class SceneWindow : Window
{
    readonly TextBlock heure = new() { FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
    readonly TextBlock minuteur = new() { FontSize = 44, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock minuteurLib = new() { Text = "PRÉDICATION", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB0, 0x4B)), HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock texte = new() { FontSize = 48, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock reference = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xD6, 0x8A)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
    readonly TextBlock suivant = new() { FontSize = 26, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xBE, 0xAA)), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly DispatcherTimer tic = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Func<(string Texte, bool Depasse)> lireMinuteur;
    bool plein;

    public SceneWindow(Func<(string Texte, bool Depasse)> minuteurTexte)
    {
        lireMinuteur = minuteurTexte;
        Title = "MyVerse — écran de scène";
        Width = 1100; Height = 640;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x0D));
        FontFamily = (FontFamily)Application.Current.FindResource("Ui");
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/myverse.ico")); } catch (Exception) { }

        var haut = new Grid();
        haut.ColumnDefinitions.Add(new ColumnDefinition());
        haut.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        haut.Children.Add(heure);
        var droite = new StackPanel();
        droite.Children.Add(minuteurLib);
        droite.Children.Add(minuteur);
        Grid.SetColumn(droite, 1);
        haut.Children.Add(droite);

        var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        centre.Children.Add(texte);
        centre.Children.Add(reference);

        var bas = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x32, 0x26)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 16, 0, 0),
            Child = suivant,
        };

        var page = new DockPanel { Margin = new Thickness(40, 28, 40, 28) };
        DockPanel.SetDock(haut, Dock.Top);
        DockPanel.SetDock(bas, Dock.Bottom);
        page.Children.Add(haut);
        page.Children.Add(bas);
        page.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = new Border { Width = 1000, Padding = new Thickness(0, 20, 0, 20), Child = centre } });
        Content = page;

        tic.Tick += (_, _) => Maj();
        App.Regie.Change += Maj;
        Closed += (_, _) => { tic.Stop(); App.Regie.Change -= Maj; };
        MouseDoubleClick += (_, _) => Basculer();
        KeyDown += (_, e) => { if (e.Key == Key.F11) Basculer(); else if (e.Key == Key.Escape && plein) Basculer(); };
        SourceInitialized += (_, _) => Natif.TitreSombre(this);
        tic.Start();
        Maj();
    }

    void Basculer()
    {
        plein = !plein;
        WindowStyle = plein ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowState = plein ? WindowState.Maximized : WindowState.Normal;
    }

    void Maj()
    {
        var r = App.Regie;
        heure.Text = DateTime.Now.ToString("HH:mm");
        var (m, depasse) = lireMinuteur();
        minuteur.Text = m;
        minuteur.Foreground = depasse ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)) : Brushes.White;
        var e = r.Etat;
        texte.Text = e.Vide ? "—" : string.Join("\n", e.Lignes);
        texte.FontSize = e.Mode == "chant" ? 52 : 44;
        reference.Text = e.Ref;
        reference.Visibility = e.Ref.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var s = r.Prochaine();
        suivant.Text = s != null ? "Ensuite › " + s.Label + " — " + s.Texte.Replace("\n", " ") : "Ensuite › —";
    }
}
