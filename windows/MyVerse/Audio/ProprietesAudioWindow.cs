using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyVerse.Audio;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les propriétés audio avancées, comme dans OBS : une ligne par source avec son nom, son état (actif ou inactif), son volume (dB ou %),
/// mono, la balance, le décalage de synchronisation, le monitoring audio et ses pistes 1 à 6. Chaque changement s'entend tout de suite.
/// Les enregistrements et les sorties MyVirtualSound prennent chacun une piste.
/// </summary>
public sealed class ProprietesAudioWindow : FenetreDialogue
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    /// <summary>« % » coché dans l'en-tête du volume : gardé pendant que MyChurch est ouvert, comme dans OBS.</summary>
    static bool enPourcent;

    readonly Grid table = new();
    readonly CheckBox activesSeulement = new() { Content = "Sources actives uniquement", FontSize = 14, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

    static readonly (string Titre, double Largeur)[] Colonnes =
    {
        ("Nom", 220), ("État", 76), ("Volume", 132), ("Mono", 56), ("Balance", 180), ("Décalage de synchronisation", 176), ("Monitoring audio", 262), ("Pistes", 250),
    };

    public ProprietesAudioWindow()
    {
        Title = "Propriétés audio avancées";
        Width = 1420;
        Height = 520;
        MinWidth = 820;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");

        var racine = new DockPanel { Margin = new Thickness(18, 16, 18, 16) };
        var bas = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var fermer = new Button { Style = Style_("Btn"), Content = "Fermer", MinWidth = 110, Height = 40, IsCancel = true };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Right);
        bas.Children.Add(fermer);
        activesSeulement.Click += (_, _) => Remplir();
        bas.Children.Add(activesSeulement);
        DockPanel.SetDock(bas, Dock.Bottom);
        racine.Children.Add(bas);

        foreach (var (_, largeur) in Colonnes) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largeur) });
        var cadre = new Border
        {
            Background = Pinceau("Encre"), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 6),
            Child = new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 4) },
        };
        racine.Children.Add(cadre);
        Content = racine;
        A.PeripheriquesChange += Rafraichir;
        Closed += (_, _) => A.PeripheriquesChange -= Rafraichir;
        Remplir();
    }

    void Rafraichir() => Dispatcher.BeginInvoke(new Action(Remplir));

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
        {
            var titre = new TextBlock { Text = Colonnes[c].Titre, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (Colonnes[c].Titre == "Volume")
            {
                // « Volume ☐ % » : comme OBS, la case affiche les volumes en pourcentage
                var entete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 8) };
                entete.Children.Add(titre);
                var pc = new CheckBox { Content = "%", IsChecked = enPourcent, FontSize = 13.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume en pourcentage" };
                pc.Click += (_, _) => { enPourcent = pc.IsChecked == true; Remplir(); };
                entete.Children.Add(pc);
                Placer(entete, 0, c);
            }
            else
            {
                titre.Margin = new Thickness(0, 0, 10, 8);
                Placer(titre, 0, c);
            }
        }
        var sources = C.Sources.Where(s => activesSeulement.IsChecked != true || !A.Inactive(s)).ToList();
        if (sources.Count == 0)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var vide = new TextBlock { Text = C.Sources.Count == 0 ? "Aucune source dans le mélangeur." : "Aucune source active.", FontSize = 14, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 10, 0, 10) };
            Grid.SetColumnSpan(vide, Colonnes.Length);
            Placer(vide, 1, 0);
            return;
        }
        int r = 1;
        foreach (var s in sources) Ligne(s, r++);
    }

    static Shapes.Path IconeHautParleur() => new()
    {
        Data = Geometry.Parse("M2,7 H5 L9,3 V15 L5,11 H2 Z M12,6 Q14,9 12,12"),
        Stroke = Pinceau("Brume"), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Width = 18, Height = 18, Stretch = Stretch.None, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
    };

    void Ligne(AudioSource s, int r)
    {
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fond = new Border { BorderBrush = Pinceau("Trait"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetColumnSpan(fond, Colonnes.Length);
        Placer(fond, r, 0);
        var marge = new Thickness(0, 6, 10, 6);

        // nom
        var nom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = marge, ToolTip = AudioPanel.Description(s) };
        nom.Children.Add(IconeHautParleur());
        nom.Children.Add(new TextBlock { Text = s.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 186, VerticalAlignment = VerticalAlignment.Center });
        Placer(nom, r, 0);

        // état, comme OBS : « Actif » ou « Inactif »
        bool inactive = A.Inactive(s);
        Placer(new TextBlock
        {
            Text = inactive ? "Inactif" : "Actif", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = marge,
            Foreground = Pinceau(inactive ? "Brume2" : "Direct"),
            ToolTip = inactive ? (s.Kind == AudioInputKind.Media ? "Fichier introuvable" : "Périphérique débranché") : null,
        }, r, 1);

        // volume : dB (−60 à +6) ou % (0 à 200)
        var volume = new ChampNombre(
            () => enPourcent ? Math.Round(AudioMath.Linear(s.VolumeDb) * 100).ToString(Fr) + " %" : AudioPanel.TexteDb(s.VolumeDb),
            t =>
            {
                if (!ChampNombre.Lire(t, out double v)) return;
                double db = enPourcent ? (v <= 0 ? -60 : AudioMath.Db(v / 100)) : v;
                s.VolumeDb = Math.Round(AudioConfiguration.Bound(db, -60, 6), 1);
                Appliquer();
            },
            pas =>
            {
                if (enPourcent)
                {
                    double pc = Math.Clamp(Math.Round(AudioMath.Linear(s.VolumeDb) * 100) + pas, 0, 200);
                    s.VolumeDb = Math.Round(pc <= 0 ? -60 : AudioConfiguration.Bound(AudioMath.Db(pc / 100), -60, 6), 1);
                }
                else s.VolumeDb = Math.Round(Math.Clamp(s.VolumeDb + pas * 0.1, -60, 6), 1);
                Appliquer();
            }, 10, "Volume pour « " + s.Name + " »") { Margin = marge };
        Placer(volume, r, 2);

        var mono = new CheckBox { IsChecked = s.Mono, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = marge, ToolTip = "Conversion en mono pour « " + s.Name + " »" };
        mono.Click += (_, _) => { s.Mono = mono.IsChecked == true; Appliquer(); };
        Placer(mono, r, 3);

        // balance : G ── D, double-clic pour recentrer
        var balance = new Grid { Margin = marge, VerticalAlignment = VerticalAlignment.Center };
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        balance.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var g = new TextBlock { Text = "G", FontSize = 12.5, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var d = new TextBlock { Text = "D", FontSize = 12.5, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var curseur = new Slider { Minimum = 0, Maximum = 1, Value = s.Balance, SmallChange = 0.01, LargeChange = 0.1, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        string Balance() => Math.Abs(s.Balance - 0.5) < 0.005 ? "Balance audio : centre" : s.Balance < 0.5 ? $"Balance audio : {Math.Round((0.5 - s.Balance) * 200)} % à gauche" : $"Balance audio : {Math.Round((s.Balance - 0.5) * 200)} % à droite";
        curseur.ToolTip = Balance() + " · double-clic : centre";
        curseur.ValueChanged += (_, e) =>
        {
            double v = Math.Abs(e.NewValue - 0.5) < 0.02 ? 0.5 : Math.Round(e.NewValue, 2);
            if (Math.Abs(v - s.Balance) < 0.001) return;
            s.Balance = v;
            curseur.ToolTip = Balance() + " · double-clic : centre";
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
        var synchro = new ChampNombre(
            () => s.SyncOffsetMs.ToString(Fr) + " ms",
            t =>
            {
                if (!ChampNombre.Lire(t, out double v)) return;
                s.SyncOffsetMs = (int)Math.Clamp(Math.Round(v), -950, 20000);
                Appliquer();
            },
            pas => { s.SyncOffsetMs = Math.Clamp(s.SyncOffsetMs + pas, -950, 20000); Appliquer(); },
            10, "Décalage de synchronisation pour « " + s.Name + " » : retarde son son, par exemple pour le caler sur une caméra en retard") { Margin = marge };
        Placer(synchro, r, 5);

        // monitoring audio : les trois choix d'OBS
        var ecoute = new ComboBox { MinHeight = 34, Margin = marge, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Monitoring audio pour « " + s.Name + " » (casque de la régie)" };
        foreach (var mode in new[] { AudioMonitoring.Off, AudioMonitoring.Only, AudioMonitoring.AndOutput })
            ecoute.Items.Add(new ComboBoxItem { Content = AudioPanel.NomEcoute(mode), Tag = mode });
        ecoute.SelectedIndex = (int)s.Monitoring;
        ecoute.SelectionChanged += (_, _) => { if (ecoute.SelectedItem is ComboBoxItem { Tag: AudioMonitoring m }) { s.Monitoring = m; Appliquer(); } };
        Placer(ecoute, r, 6);

        // pistes 1 à 6
        var pistes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
        for (int t = 1; t <= AudioConfiguration.Pistes; t++)
        {
            int piste = t;
            var cb = new CheckBox { Content = t.ToString(), IsChecked = s.DansPiste(t), FontSize = 13.5, Margin = new Thickness(0, 0, 12, 0), ToolTip = Destinations(piste) };
            cb.Click += (_, _) =>
            {
                if (cb.IsChecked == true) s.Tracks |= 1 << (piste - 1);
                else s.Tracks &= ~(1 << (piste - 1));
                Appliquer();
            };
            pistes.Children.Add(cb);
        }
        Placer(pistes, r, 7);
    }

    /// <summary>« Piste 2 : enregistrement « Culte », sortie « Zoom » » : ce qui reçoit une piste.</summary>
    static string Destinations(int piste)
    {
        var noms = C.Profiles.Where(p => p.Track == piste).Select(p => $"enregistrement « {p.Name} »")
            .Concat(C.VirtualSounds.Where(v => v.Track == piste).Select(v => $"sortie MyVirtualSound « {v.Name} »")).ToList();
        return $"Piste {piste}" + (noms.Count == 0 ? " : aucun enregistrement ni sortie pour l'instant" : " : " + string.Join(", ", noms));
    }
}

/// <summary>
/// Un champ numérique avec ses deux flèches, comme ceux d'OBS : Entrée ou Tab pour valider un nombre tapé,
/// flèches pour l'augmenter ou le diminuer d'un pas, molette de <c>pasMolette</c> pas à la fois.
/// <c>pas</c> reçoit le nombre de pas, positif ou négatif.
/// </summary>
internal sealed class ChampNombre : Grid
{
    readonly TextBox champ;
    readonly Func<string> afficher;

    public ChampNombre(Func<string> afficher, Action<string> lire, Action<int> pas, int pasMolette, string infobulle)
    {
        this.afficher = afficher;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        VerticalAlignment = VerticalAlignment.Center;
        champ = new TextBox
        {
            Style = MainWindow.Style_("Champ"), FontSize = 13.5, Padding = new Thickness(8, 6, 6, 6), TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center, ToolTip = infobulle,
        };
        Children.Add(champ);
        var fleches = new StackPanel { Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (signe, haut) in new[] { (1, true), (-1, false) })
        {
            var b = new Button
            {
                Style = MainWindow.Style_("BtnPlat"), Width = 20, Height = 15, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Focusable = false,
                Content = new Shapes.Path
                {
                    Data = Geometry.Parse(haut ? "M1,5 L5,1 L9,5" : "M1,1 L5,5 L9,1"), Stroke = MainWindow.Pinceau("Brume"), StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 10, Height = 6, Stretch = Stretch.None,
                },
            };
            b.Click += (_, _) => { pas(signe); Maj(); };
            fleches.Children.Add(b);
        }
        Grid.SetColumn(fleches, 1);
        Children.Add(fleches);
        champ.LostFocus += (_, _) => { lire(champ.Text); Maj(); };
        champ.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { lire(champ.Text); Maj(); e.Handled = true; }
            else if (e.Key == Key.Up) { pas(1); Maj(); e.Handled = true; }
            else if (e.Key == Key.Down) { pas(-1); Maj(); e.Handled = true; }
        };
        PreviewMouseWheel += (_, e) =>
        {
            pas(Math.Sign(e.Delta) * pasMolette);
            Maj();
            e.Handled = true;
        };
        Maj();
    }

    public void Maj() => champ.Text = afficher();

    /// <summary>Lit un nombre tapé (« −6,5 dB », « 80 % », « 120 ms ») : virgule ou point, signe moins typographique accepté.</summary>
    public static bool Lire(string texte, out double v)
    {
        string t = texte.Replace("dB", "").Replace("%", "").Replace("ms", "").Replace("−", "-").Replace(' ', ' ').Replace(" ", "").Replace(',', '.').Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
    }
}
