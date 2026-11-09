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
    readonly Moniteur moniteur;
    bool plein;
    readonly DispatcherTimer curseur = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>Commande clavier dans la fenêtre de projection : « suiv », « prec » ou « retirer ».</summary>
    public event Action<string>? Commande;

    public EcranWindow(Moniteur m, bool pleinEcran)
    {
        moniteur = m;
        plein = pleinEcran;
        Title = "MyVerse — écran";
        Background = Brushes.Black;
        Content = Rendu;
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
