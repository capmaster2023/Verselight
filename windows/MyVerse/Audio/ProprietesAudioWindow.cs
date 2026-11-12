using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les propriétés audio avancées d'OBS (MyChurch 4.0) : une ligne par source avec son état, son volume (dB ou %), mono,
/// balance, décalage de synchronisation, écoute au casque et ses destinations (les pistes de MyChurch : enregistrements 1 à 5,
/// sorties MyVirtualSound A à D). Chaque changement s'entend tout de suite.
/// </summary>
public sealed class ProprietesAudioWindow : FenetreDialogue
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;

    readonly Grid table = new();
    readonly CheckBox activesSeulement = new() { Content = "Sources actives seulement", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox unite = new() { MinHeight = 36, MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
    bool enPourcent;

    static readonly (string Titre, double Largeur)[] Colonnes =
    {
        ("Nom", 210), ("État", 90), ("Volume", 104), ("Mono", 64), ("Balance", 170), ("Décalage de synchro", 140), ("Écoute au casque", 210), ("Destinations", 230),
    };

    public ProprietesAudioWindow()
    {
        Title = "Propriétés audio avancées";
        Width = 1290;
        Height = 620;
        MinWidth = 900;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");

        var racine = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = "Propriétés audio avancées", Style = Style_("Titre2") });
        tete.Children.Add(new TextBlock
        {
            Text = "Comme dans OBS : chaque changement s'entend tout de suite. Entrée ou Tab pour valider un nombre ; double-clic sur la balance pour la recentrer. Les destinations remplacent les pistes d'OBS.",
            Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14),
        });
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var fermer = new Button { Style = Style_("BtnOr"), Content = "Fermer", MinWidth = 120, Height = 42 };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Right);
        bas.Children.Add(fermer);
        var options = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        activesSeulement.Click += (_, _) => Remplir();
        options.Children.Add(activesSeulement);
        options.Children.Add(new TextBlock { Text = "Volume en", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) });
        unite.Items.Add(new ComboBoxItem { Content = "dB", Tag = "db" });
        unite.Items.Add(new ComboBoxItem { Content = "%", Tag = "pc" });
        unite.SelectedIndex = 0;
        unite.SelectionChanged += (_, _) => { enPourcent = unite.SelectedIndex == 1; Remplir(); };
        options.Children.Add(unite);
        bas.Children.Add(options);
        DockPanel.SetDock(bas, Dock.Bottom);
        racine.Children.Add(bas);

        foreach (var (_, largeur) in Colonnes) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largeur) });
        racine.Children.Add(new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 6) });
        Content = racine;
        Remplir();
    }

    static void Appliquer() => A.Apply();

    void Placer(UIElement e, int ligne, int colonne)
    {
        Grid.SetRow(e, ligne);
        Grid.SetColumn(e, colonne);
        table.Children.Add(e);
    }

    void Remplir()
    {
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < Colonnes.Length; c++)
            Placer(new TextBlock { Text = Colonnes[c].Titre.ToUpper(Fr), Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Bottom }, 0, c);
        var sources = C.Sources.Where(s => !activesSeulement.IsChecked.GetValueOrDefault() || !A.Inactive(s)).ToList();
        if (sources.Count == 0)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var vide = new TextBlock { Text = C.Sources.Count == 0 ? "Aucune source dans le mélangeur." : "Aucune source active.", FontSize = 14, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetColumnSpan(vide, Colonnes.Length);
            Placer(vide, 1, 0);
            return;
        }
        int r = 1;
        foreach (var s in sources) Ligne(s, r++);
    }

    void Ligne(AudioSource s, int r)
    {
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fond = new Border { BorderBrush = Pinceau("Trait"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetColumnSpan(fond, Colonnes.Length);
        Placer(fond, r, 0);
        var marge = new Thickness(0, 10, 10, 10);

        // nom et étiquette d'OBS 32.1
        bool inactive = A.Inactive(s);
        var nom = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = marge };
        nom.Children.Add(new TextBlock { Text = s.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s.Name });
        nom.Children.Add(new TextBlock { Text = AudioPanel.Genre(s), FontSize = 12, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
        Placer(nom, r, 0);
        var etat = AudioConfiguration.Etat(s, inactive);
        var statut = new TextBlock
        {
            Text = inactive ? "Inactive" : etat == EtatMelangeur.Globale ? "Globale" : "Active", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = marge,
            Foreground = Pinceau(inactive ? "Brume2" : etat == EtatMelangeur.Globale ? "Or" : "Vert"),
            ToolTip = inactive ? (s.Kind == AudioInputKind.Media ? "Fichier introuvable" : "Périphérique débranché") : null,
        };
        Placer(statut, r, 1);

        // volume : dB (−60 à +6) ou % (0 à 200), comme OBS
        var volume = new TextBox { Style = Style_("Champ"), FontSize = 14, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Margin = marge, ToolTip = "Volume · molette : ±1 dB" };
        string Texte() => enPourcent ? Math.Round(AudioMath.Linear(s.VolumeDb) * 100).ToString(Fr) + " %" : AudioPanel.TexteDb(s.VolumeDb);
        volume.Text = Texte();
        void LireVolume()
        {
            string t = volume.Text.Replace("dB", "").Replace("%", "").Replace("−", "-").Replace(',', '.').Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                double db = enPourcent ? (v <= 0 ? -60 : AudioMath.Db(v / 100)) : v;
                s.VolumeDb = Math.Round(AudioConfiguration.Bound(db, -60, 6), 1);
                Appliquer();
            }
            volume.Text = Texte();
        }
        volume.LostFocus += (_, _) => LireVolume();
        volume.KeyDown += (_, e) => { if (e.Key == Key.Enter) LireVolume(); };
        volume.PreviewMouseWheel += (_, e) =>
        {
            s.VolumeDb = Math.Round(Math.Clamp(s.VolumeDb + Math.Sign(e.Delta), -60, 6), 1);
            volume.Text = Texte();
            Appliquer();
            e.Handled = true;
        };
        Placer(volume, r, 2);

        var mono = new CheckBox { IsChecked = s.Mono, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = marge, ToolTip = "Mono : les deux côtés reçoivent le même son" };
        mono.Click += (_, _) => { s.Mono = mono.IsChecked == true; Appliquer(); };
        Placer(mono, r, 3);

        // balance : gauche ↔ droite, double-clic pour recentrer
        var balance = new Grid { Margin = marge, VerticalAlignment = VerticalAlignment.Center };
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var g = new TextBlock { Text = "G", FontSize = 12, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var d = new TextBlock { Text = "D", FontSize = 12, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var curseur = new Slider { Minimum = 0, Maximum = 1, Value = s.Balance, SmallChange = 0.01, LargeChange = 0.1, TickFrequency = 0.5, TickPlacement = TickPlacement.BottomRight, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        string Balance() => Math.Abs(s.Balance - 0.5) < 0.005 ? "Balance : centre" : s.Balance < 0.5 ? $"Balance : {Math.Round((0.5 - s.Balance) * 200)} % à gauche" : $"Balance : {Math.Round((s.Balance - 0.5) * 200)} % à droite";
        curseur.ToolTip = Balance();
        curseur.ValueChanged += (_, e) =>
        {
            double v = Math.Abs(e.NewValue - 0.5) < 0.02 ? 0.5 : Math.Round(e.NewValue, 2);
            if (Math.Abs(v - s.Balance) < 0.001) return;
            s.Balance = v;
            curseur.ToolTip = Balance();
            Appliquer();
        };
        curseur.MouseDoubleClick += (_, _) => curseur.Value = 0.5;
        Grid.SetColumn(curseur, 1);
        Grid.SetColumn(d, 2);
        balance.Children.Add(g);
        balance.Children.Add(curseur);
        balance.Children.Add(d);
        Placer(balance, r, 4);

        // décalage de synchronisation (ms), de −950 à 20 000 comme OBS
        var synchro = new StackPanel { Orientation = Orientation.Horizontal, Margin = marge, VerticalAlignment = VerticalAlignment.Center };
        var champ = new TextBox { Style = Style_("Champ"), Width = 84, FontSize = 14, Text = s.SyncOffsetMs.ToString(Fr), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Retarde le son de cette source (−950 à 20 000 ms), par exemple pour le caler sur une caméra en retard" };
        void LireSynchro()
        {
            string t = champ.Text.Replace("−", "-").Trim();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) { s.SyncOffsetMs = Math.Clamp(v, -950, 20000); Appliquer(); }
            champ.Text = s.SyncOffsetMs.ToString(Fr);
        }
        champ.LostFocus += (_, _) => LireSynchro();
        champ.KeyDown += (_, e) => { if (e.Key == Key.Enter) LireSynchro(); };
        synchro.Children.Add(champ);
        synchro.Children.Add(new TextBlock { Text = "ms", FontSize = 13, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        Placer(synchro, r, 5);

        var ecoute = new ComboBox { MinHeight = 36, Margin = marge, VerticalAlignment = VerticalAlignment.Center };
        foreach (var mode in new[] { AudioMonitoring.Off, AudioMonitoring.Only, AudioMonitoring.AndOutput })
            ecoute.Items.Add(new ComboBoxItem { Content = AudioPanel.NomEcoute(mode), Tag = mode });
        ecoute.SelectedIndex = (int)s.Monitoring;
        ecoute.SelectionChanged += (_, _) => { if (ecoute.SelectedItem is ComboBoxItem { Tag: AudioMonitoring m }) { s.Monitoring = m; Appliquer(); } };
        Placer(ecoute, r, 6);

        // les destinations : enregistrements 1 à 5, sorties MyVirtualSound A à D
        var destinations = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
        for (int i = 0; i < C.Profiles.Count; i++) destinations.Children.Add(Destination(s, C.Profiles[i], (i + 1).ToString(), $"Enregistrement {i + 1} « {C.Profiles[i].Name} »"));
        for (int i = 0; i < C.VirtualSounds.Count; i++) destinations.Children.Add(Destination(s, C.VirtualSounds[i], AudioPanel.MarqueSortie(i), $"Sortie MyVirtualSound {AudioPanel.MarqueSortie(i)} « {C.VirtualSounds[i].Name} »"));
        if (destinations.Children.Count == 0) destinations.Children.Add(new TextBlock { Text = "aucune", FontSize = 13, Foreground = Pinceau("Brume2") });
        Placer(destinations, r, 7);
    }

    /// <summary>Une destination à allumer ou éteindre, comme les pastilles du mélangeur (allumée : la source y va).</summary>
    static Button Destination(AudioSource s, IDestinationAudio d, string marque, string quoi)
    {
        var b = new Button { Style = Style_("Btn"), Content = marque, Width = 30, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4), FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        void Maj()
        {
            bool dans = d.SourceIds.Contains(s.Id);
            if (dans) { b.Background = Pinceau("Blanc"); b.BorderBrush = Pinceau("Blanc"); b.Foreground = Pinceau("Encre"); }
            else { b.ClearValue(Control.BackgroundProperty); b.ClearValue(Control.BorderBrushProperty); b.ClearValue(Control.ForegroundProperty); }
            b.ToolTip = quoi + (dans ? " : la source y va" : " : la source n'y va pas") + " · cliquer pour changer";
        }
        b.Click += (_, _) =>
        {
            if (!d.SourceIds.Remove(s.Id)) d.SourceIds.Add(s.Id);
            Appliquer();
            Maj();
        };
        Maj();
        return b;
    }
}
