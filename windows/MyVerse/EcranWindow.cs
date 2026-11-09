using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Fenêtre de projection : plein écran sur l'écran choisi (comme « Projecteur plein écran » d'OBS) ou en fenêtre.
/// Clavier et télécommande de présentation : → Espace Page↓ suivant, ← Page↑ précédent, Échap retirer.
/// </summary>
public sealed class EcranWindow : Window
{
    public Rendu Rendu { get; } = new();
    readonly System.Windows.Controls.Border qrBoite;
    readonly System.Windows.Controls.Image qrImage = new() { Width = 260, Height = 260 };
    readonly System.Windows.Controls.TextBlock qrTexte = new() { FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x5F, 0x5F)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
    readonly Moniteur moniteur;
    bool plein;
    readonly DispatcherTimer curseur = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>Commande clavier dans la fenêtre de projection : « suiv », « prec » ou « retirer ».</summary>
    public event Action<string>? Commande;

    public EcranWindow(Moniteur m, bool pleinEcran)
    {
        moniteur = m;
        plein = pleinEcran;
        Title = "MyChurch — écran";
        Background = Brushes.Black;
        // code QR « suivre sur son téléphone », dans un coin (case « QR à l'écran »)
        RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
        var pile = new System.Windows.Controls.StackPanel();
        pile.Children.Add(qrImage);
        pile.Children.Add(new System.Windows.Controls.TextBlock { Text = "Suivez sur votre téléphone", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        pile.Children.Add(qrTexte);
        qrBoite = new System.Windows.Controls.Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(20), Padding = new Thickness(22), Margin = new Thickness(56),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, Child = pile,
        };
        var grille = new System.Windows.Controls.Grid();
        grille.Children.Add(Rendu);
        grille.Children.Add(qrBoite);
        Content = grille;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/myverse.ico")); } catch (Exception) { }
        Appliquer();
        curseur.Tick += (_, _) => { curseur.Stop(); if (plein) Cursor = Cursors.None; };
        MouseMove += (_, _) => { Cursor = Cursors.Arrow; curseur.Stop(); curseur.Start(); };
        MouseDoubleClick += (_, _) => Basculer();
        KeyDown += Touche;
        Cursor = plein ? Cursors.None : Cursors.Arrow;
    }

    /// <summary>Montre (ou cache, image null) le code QR de la page « suivre ».</summary>
    public void MontrerQr(ImageSource? image, string? url)
    {
        qrImage.Source = image;
        qrTexte.Text = url ?? "";
        qrBoite.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
    }

    void Appliquer()
    {
        if (plein)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Natif.TitreSombre(this);
        Placer();
    }

    /// <summary>Place la fenêtre sur l'écran choisi (pixels réels), puis plein écran.</summary>
    void Placer()
    {
        var h = new WindowInteropHelper(this).Handle;
        var m = moniteur;
        if (plein)
        {
            Natif.SetWindowPos(h, IntPtr.Zero, m.X + 40, m.Y + 40, Math.Max(320, Math.Min(1280, m.W - 80)), Math.Max(200, Math.Min(720, m.H - 80)), Natif.SWP_NOZORDER | Natif.SWP_NOACTIVATE);
            WindowState = WindowState.Maximized;
        }
        else
        {
            int w = (int)(m.W * 0.6), hh = (int)(w * 9.0 / 16);
            Natif.SetWindowPos(h, IntPtr.Zero, m.X + 80, m.Y + 80, w, hh, Natif.SWP_NOZORDER | Natif.SWP_NOACTIVATE);
            WindowState = WindowState.Normal;
        }
    }

    /// <summary>Double-clic ou F11 : plein écran ↔ fenêtre.</summary>
    void Basculer()
    {
        plein = !plein;
        WindowState = WindowState.Normal;
        Appliquer();
        Placer();
        Cursor = plein ? Cursors.None : Cursors.Arrow;
    }

    void Touche(object sender, KeyEventArgs e)
    {
        string? k = e.Key switch
        {
            Key.Right or Key.Down or Key.PageDown or Key.Space or Key.Enter => "suiv",
            Key.Left or Key.Up or Key.PageUp or Key.Back => "prec",
            Key.Escape or Key.Delete or Key.B => "retirer",
            _ => null,
        };
        if (e.Key == Key.F11) { Basculer(); e.Handled = true; return; }
        if (k == null) return;
        e.Handled = true;
        Commande?.Invoke(k);
    }
}
