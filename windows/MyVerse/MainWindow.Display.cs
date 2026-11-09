using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MyVerse.Ui;

public partial class MainWindow
{
    void InitDisplay()
    {
        DisplayImage.Click += (_, _) => AjouterImage();
        DisplayCamera.Click += (_, _) => AjouterCamera();
        DisplayFenetre.Click += (_, _) => AjouterFenetre();
        DisplayReconnecter.Click += (_, _) => App.Sources.Reconnecter(R.Etat);
        VirtualCamBtn.Click += (_, _) =>
        {
            if (MyVirtualCam.Installee) Toast("MyVirtualCam est disponible. Choisissez-la comme caméra dans votre logiciel vidéo.");
            else MyVirtualCam.Installer(this);
        };
        VirtualCamBtn.Content = "MyVirtualCam · automatique";
        VirtualCamBtn.ToolTip = "Sortie vidéo permanente 1280 × 720 · cliquez pour installer le périphérique si nécessaire";
        ListeAnnonces.MouseDoubleClick += Display_DoubleClick;
        ListeChants.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(ListeChants, o) is ListBoxItem { DataContext: Chant c })
            { R.AfficherSection(c.Id, 0); e.Handled = true; }
        };
        ChantTitre.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && chantOuvert != null) { R.AfficherSection(chantOuvert, 0); e.Handled = true; } };
        AnnonceTitre.MouseDoubleClick += (_, e) => { if (EnregistrerAnnonce() is { } a) { R.AfficherAnnonce(a); e.Handled = true; } };
        App.Sources.Change += () =>
        {
            var b = App.Sources.Image;
            ApercuRendu.ImageDisplay(b); ecran?.Rendu.ImageDisplay(b); App.CameraVirtuelle?.Media(b);
            DisplaySourceInfo.Text = App.Sources.Statut;
        };
    }

    void Display_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject o) return;
        // A double click on the add button or on empty space must not send an unrelated item live.
        for (var p = o; p != null && p is not ListBoxItem; p = p is FrameworkContentElement f ? f.Parent : VisualTreeHelper.GetParent(p))
            if (p is ButtonBase) return;
        if (ItemsControl.ContainerFromElement(ListeAnnonces, o) is not ListBoxItem { DataContext: Annonce a }) return;
        if (a == annonceChoisie && EnregistrerAnnonce() is { } sauvegardee) a = sauvegardee;
        R.AfficherAnnonce(a); e.Handled = true;
    }

    void ChoisirDisplay(Annonce a)
    {
        annonceChoisie = a;
        AnnonceTitre.Text = a.Titre;
        AnnonceTexte.Text = a.Source != null ? a.Description : a.Texte;
        AnnonceTexte.IsReadOnly = a.Source != null;
        DisplayReconnecter.Visibility = a.Source?.Type is "camera" or "fenetre" ? Visibility.Visible : Visibility.Collapsed;
        AnnoncesMaj();
    }

    void AjouterImage()
    {
        var dialog = new OpenFileDialog { Title = "Ajouter des images à Display", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var dir = Path.Combine(App.Donnees.Dossier, "display-images"); Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(file));
                File.Copy(file, dest);
                ChoisirDisplay(R.NouveauDisplay(Path.GetFileNameWithoutExtension(file), new SourceDisplay { Type = "image", Chemin = dest }));
            }
            catch (Exception ex) { Toast("Image inaccessible : " + ex.Message); }
        }
    }
    void AjouterCamera()
    {
        try
        {
            var devices = SourcesDisplay.Peripheriques();
            if (devices.Count == 0) { Toast("Aucun périphérique vidéo. Branchez une webcam ou une carte de capture USB/HDMI, puis réessayez."); return; }
            if (ChoisirSource("Périphérique de capture vidéo", "Webcam ou carte de capture USB/HDMI", devices, d => d.Nom) is { } device)
                ChoisirDisplay(R.NouveauDisplay(device.Nom, new SourceDisplay { Type = "camera", Peripherique = device.Id }));
        }
        catch (Exception ex) { Toast("Liste des périphériques inaccessible : " + ex.Message); }
    }
    void AjouterFenetre()
    {
        var windows = SourcesDisplay.Fenetres();
        if (windows.Count == 0) { Toast("Ouvrez la fenêtre à capturer, puis réessayez."); return; }
        if (ChoisirSource("Capture de fenêtre", "Gardez la fenêtre ouverte et non réduite. Certaines fenêtres protégées ou accélérées par le GPU ne permettent pas la capture.", windows, w => w.Titre) is { } w)
            ChoisirDisplay(R.NouveauDisplay(w.Titre, new SourceDisplay { Type = "fenetre", Fenetre = w.Handle, Processus = w.Processus }));
    }
    T? ChoisirSource<T>(string titre, string aide, List<T> items, Func<T, string> nom) where T : class
    {
        var list = new ListBox { ItemsSource = items.Select(nom).ToList(), SelectedIndex = 0, MinHeight = 160, Margin = new Thickness(0, 12, 0, 12) };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var w = new Window { Title = titre, Owner = this, Width = 550, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Pinceau("Nuit") };
        panel.Children.Add(new TextBlock { Text = aide, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc"), FontSize = 15 });
        panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Style = Style_("Btn"), Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Ajouter la source", IsDefault = true, Style = Style_("BtnOr") };
        add.Click += (_, _) => w.DialogResult = true;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) w.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(add); panel.Children.Add(buttons); w.Content = panel;
        return w.ShowDialog() == true && list.SelectedIndex >= 0 ? items[list.SelectedIndex] : null;
    }

    public void TestDisplayDoubleClick(Annonce a)
    {
        ChoisirDisplay(a); ListeAnnonces.UpdateLayout();
        if (ListeAnnonces.ItemContainerGenerator.ContainerFromItem(a) is ListBoxItem item)
        {
            var ev = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = item };
            Display_DoubleClick(ListeAnnonces, ev);
        }
    }
}
