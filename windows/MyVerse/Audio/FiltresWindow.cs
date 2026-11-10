using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les filtres d'OBS sur une source : réduction du bruit, noise gate, compresseur, limiteur.
/// Ils s'appliquent dans l'ordre, de haut en bas, et chaque changement s'entend tout de suite.
/// </summary>
public sealed class FiltresWindow : FenetreDialogue
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    readonly AudioSource source;
    readonly StackPanel liste = new();

    public FiltresWindow(AudioSource s)
    {
        source = s;
        Title = "Filtres · " + s.Name;
        Width = 640;
        Height = 700;
        MinWidth = 520;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");

        var racine = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = "Filtres · " + s.Name, Style = Style_("Titre2"), TextTrimming = TextTrimming.CharacterEllipsis });
        tete.Children.Add(new TextBlock
        {
            Text = "Les filtres d'OBS s'appliquent de haut en bas, en direct : chaque changement s'entend tout de suite. Entrée ou Tab pour valider un nombre.",
            Style = Style_("Muet"), FontSize = 14, Margin = new Thickness(0, 6, 0, 12),
        });
        var ajout = new WrapPanel();
        foreach (var k in new[] { AudioFilterKind.NoiseSuppression, AudioFilterKind.NoiseGate, AudioFilterKind.Compressor, AudioFilterKind.Limiter })
        {
            var b = new Button { Style = Style_("BtnPetit"), Content = "+ " + AudioFilter.Nom(k), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => Ajouter(k);
            ajout.Children.Add(b);
        }
        tete.Children.Add(ajout);
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        var fermer = new Button { Style = Style_("BtnOr"), Content = "Fermer", MinWidth = 120, Height = 42, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Bottom);
        racine.Children.Add(fermer);

        racine.Children.Add(new ScrollViewer { Content = liste, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(0, 0, 6, 0) });
        Content = racine;
        Remplir();
    }

    static void Appliquer() => App.Audio.Apply();

    void Ajouter(AudioFilterKind k)
    {
        var f = new AudioFilter { Kind = k };
        // valeurs de départ d'OBS pour chaque filtre
        switch (k)
        {
            case AudioFilterKind.NoiseGate: f.AttackMs = 25; f.HoldMs = 200; f.ReleaseMs = 150; break;
            case AudioFilterKind.Compressor: f.Ratio = 4; f.ThresholdDb = -18; f.AttackMs = 6; f.ReleaseMs = 60; break;
            case AudioFilterKind.Limiter: f.ThresholdDb = -6; f.ReleaseMs = 60; break;
        }
        source.Filters.Add(f);
        Appliquer();
        Remplir();
    }

    void Remplir()
    {
        liste.Children.Clear();
        if (source.Filters.Count == 0)
            liste.Children.Add(new TextBlock { Text = "Aucun filtre sur cette source. Ajoutez-en un avec les boutons ci-dessus.", FontSize = 14, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 0) });
        for (int i = 0; i < source.Filters.Count; i++) liste.Children.Add(Carte(source.Filters[i], i));
    }

    UIElement Carte(AudioFilter f, int i)
    {
        var pile = new StackPanel();
        var tete = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var monter = new Button { Style = Style_("BtnPetit"), Content = "Monter", IsEnabled = i > 0, ToolTip = "Appliquer ce filtre plus tôt" };
        monter.Click += (_, _) => Deplacer(i, -1);
        var descendre = new Button { Style = Style_("BtnPetit"), Content = "Descendre", IsEnabled = i < source.Filters.Count - 1, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Appliquer ce filtre plus tard" };
        descendre.Click += (_, _) => Deplacer(i, 1);
        var supprimer = new Button { Style = Style_("BtnDanger"), Content = "Supprimer", Margin = new Thickness(6, 0, 0, 0) };
        supprimer.Click += (_, _) => { source.Filters.Remove(f); Appliquer(); Remplir(); };
        boutons.Children.Add(monter);
        boutons.Children.Add(descendre);
        boutons.Children.Add(supprimer);
        DockPanel.SetDock(boutons, Dock.Right);
        tete.Children.Add(boutons);
        var nom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var actif = new CheckBox { Style = Style_("Interrupteur"), IsChecked = f.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Activé" };
        actif.Click += (_, _) => { f.Enabled = actif.IsChecked == true; Appliquer(); };
        nom.Children.Add(actif);
        nom.Children.Add(new TextBlock { Text = (i + 1) + ". " + AudioFilter.Nom(f.Kind), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        tete.Children.Add(nom);
        pile.Children.Add(tete);

        var champs = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        switch (f.Kind)
        {
            case AudioFilterKind.NoiseSuppression:
                var methode = new ComboBox { Width = 250, MinHeight = 38 };
                methode.Items.Add(new ComboBoxItem { Content = "RNNoise (meilleure qualité)", Tag = "rnnoise" });
                methode.Items.Add(new ComboBoxItem { Content = "Speex (moins de calcul)", Tag = "speex" });
                methode.SelectedIndex = f.Method == "speex" ? 1 : 0;
                var niveau = Champ("Niveau de suppression", "dB", () => f.SuppressDb, v => f.SuppressDb = v);
                niveau.Visibility = f.Method == "speex" ? Visibility.Visible : Visibility.Collapsed;
                methode.SelectionChanged += (_, _) =>
                {
                    f.Method = (methode.SelectedItem as ComboBoxItem)?.Tag as string ?? "rnnoise";
                    niveau.Visibility = f.Method == "speex" ? Visibility.Visible : Visibility.Collapsed;
                    Appliquer();
                };
                var bloc = new StackPanel { Margin = new Thickness(0, 0, 18, 10) };
                bloc.Children.Add(new TextBlock { Text = "Méthode", FontSize = 12.5, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 0, 0, 4) });
                bloc.Children.Add(methode);
                champs.Children.Add(bloc);
                champs.Children.Add(niveau);
                break;
            case AudioFilterKind.NoiseGate:
                champs.Children.Add(Champ("Seuil d'ouverture", "dB", () => f.OpenDb, v => f.OpenDb = v));
                champs.Children.Add(Champ("Seuil de fermeture", "dB", () => f.CloseDb, v => f.CloseDb = v));
                champs.Children.Add(Champ("Attaque", "ms", () => f.AttackMs, v => f.AttackMs = v));
                champs.Children.Add(Champ("Maintien", "ms", () => f.HoldMs, v => f.HoldMs = v));
                champs.Children.Add(Champ("Relâchement", "ms", () => f.ReleaseMs, v => f.ReleaseMs = v));
                break;
            case AudioFilterKind.Compressor:
                champs.Children.Add(Champ("Ratio", ": 1", () => f.Ratio, v => f.Ratio = v));
                champs.Children.Add(Champ("Seuil", "dB", () => f.ThresholdDb, v => f.ThresholdDb = v));
                champs.Children.Add(Champ("Attaque", "ms", () => f.AttackMs, v => f.AttackMs = v));
                champs.Children.Add(Champ("Relâchement", "ms", () => f.ReleaseMs, v => f.ReleaseMs = v));
                champs.Children.Add(Champ("Gain de sortie", "dB", () => f.OutputGainDb, v => f.OutputGainDb = v));
                break;
            default:
                champs.Children.Add(Champ("Seuil", "dB", () => f.ThresholdDb, v => f.ThresholdDb = v));
                champs.Children.Add(Champ("Relâchement", "ms", () => f.ReleaseMs, v => f.ReleaseMs = v));
                break;
        }
        pile.Children.Add(champs);
        return new Border
        {
            Child = pile, Background = Pinceau("Encre"), BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 6), Margin = new Thickness(0, 0, 0, 10),
        };
    }

    void Deplacer(int i, int sens)
    {
        int j = i + sens;
        if (j < 0 || j >= source.Filters.Count) return;
        (source.Filters[i], source.Filters[j]) = (source.Filters[j], source.Filters[i]);
        Appliquer();
        Remplir();
    }

    /// <summary>Un réglage numérique : validé par Entrée ou en quittant le champ, puis remis dans ses bornes par MyChurch.</summary>
    FrameworkElement Champ(string libelle, string unite, Func<double> lire, Action<double> ecrire)
    {
        var bloc = new StackPanel { Margin = new Thickness(0, 0, 18, 10), Width = 150 };
        bloc.Children.Add(new TextBlock { Text = libelle, FontSize = 12.5, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        var ligne = new DockPanel();
        var u = new TextBlock { Text = unite, FontSize = 13.5, Foreground = Pinceau("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(u, Dock.Right);
        ligne.Children.Add(u);
        var t = new TextBox { Style = Style_("Champ"), Text = Nombre(lire()), FontSize = 14.5, Padding = new Thickness(10, 6, 10, 6) };
        void Valider()
        {
            if (Lire(t.Text) is double v && Math.Abs(v - lire()) > 1e-9)
            {
                ecrire(v);
                Appliquer();
            }
            t.Text = Nombre(lire());
        }
        t.LostKeyboardFocus += (_, _) => Valider();
        t.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Valider();
            t.SelectAll();
            e.Handled = true;
        };
        ligne.Children.Add(t);
        bloc.Children.Add(ligne);
        return bloc;
    }

    static string Nombre(double v) => v.ToString("0.##", Fr).Replace('-', '−');

    static double? Lire(string texte)
    {
        string t = texte.Trim().Replace('−', '-').Replace(',', '.').Replace(" ", "");
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;
    }
}
