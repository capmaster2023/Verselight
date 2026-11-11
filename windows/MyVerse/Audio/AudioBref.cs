using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Sous l'écran en direct (page Culte et colonne de droite) : les enregistrements avec leur bouton ● / ■, leur durée et leur taille,
/// puis les sorties MyVirtualSound avec leur interrupteur, comme sur la maquette 3.1. Le mélangeur complet reste dans Display.
/// </summary>
public sealed class AudioBref : StackPanel
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    readonly StackPanel enregistrements = new(), sorties = new();
    readonly DockPanel teteEnr, teteSorties;
    readonly TextBlock compteEnr = new(), compteSorties = new();
    readonly DispatcherTimer minuterie = new() { Interval = TimeSpan.FromMilliseconds(500) };
    string signature = "";

    public AudioBref()
    {
        teteEnr = Tete("Enregistrements", compteEnr);
        teteSorties = Tete("MyVirtualSound", compteSorties);
        Children.Add(teteEnr);
        Children.Add(enregistrements);
        Children.Add(teteSorties);
        Children.Add(sorties);
        A.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        minuterie.Tick += (_, _) => { if (IsVisible) Maj(); };
        Loaded += (_, _) => minuterie.Start();
        Unloaded += (_, _) => minuterie.Stop();
        Maj();
    }

    static DockPanel Tete(string titre, TextBlock compte)
    {
        var d = new DockPanel { Margin = new Thickness(0, 16, 0, 10) };
        compte.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
        compte.FontSize = 12;
        compte.Foreground = P("Brume2");
        DockPanel.SetDock(compte, Dock.Right);
        d.Children.Add(compte);
        d.Children.Add(new Capitales { Libelle = titre, Margin = new Thickness(0) });
        return d;
    }

    static string Taille(long octets) => octets >= 1L << 30
        ? (octets / (double)(1L << 30)).ToString("0.0", Fr) + " Go"
        : Math.Max(0, octets / (double)(1 << 20)).ToString("0", Fr) + " Mo";

    /// <summary>Reconstruit les cartes si la liste a changé, sinon met seulement à jour les textes.</summary>
    void Maj()
    {
        var r = A.Recorder;
        string sig = string.Join("|", C.Profiles.Select(p => p.Id + p.Name + r.Active(p.Id) + r.Busy(p.Id))) + "#" + string.Join("|", C.VirtualSounds.Select(v => v.Id + v.Name + v.Enabled + A.Sorties.Actif(v.Id)));
        compteEnr.Text = $"{C.Profiles.Count} / {AudioConfiguration.MaxEnregistrements}";
        compteSorties.Text = $"{C.VirtualSounds.Count} / {AudioConfiguration.MaxSortiesVirtuelles}";
        teteEnr.Visibility = C.Profiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        teteSorties.Visibility = C.VirtualSounds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sig != signature)
        {
            signature = sig;
            enregistrements.Children.Clear();
            for (int i = 0; i < C.Profiles.Count; i++) enregistrements.Children.Add(CarteEnregistrement(C.Profiles[i], i));
            sorties.Children.Clear();
            for (int i = 0; i < C.VirtualSounds.Count; i++) sorties.Children.Add(CarteSortie(C.VirtualSounds[i], i));
        }
        foreach (var t in enregistrements.Children.OfType<Border>()) if (t.Tag is Action maj) maj();
    }

    static Border Marque(string m) => new()
    {
        Background = P("Nuit2"), BorderBrush = P("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MinWidth = 20, Padding = new Thickness(5, 0, 5, 0),
        Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = m, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
    };

    Border CarteEnregistrement(RecordingProfile p, int i)
    {
        var r = A.Recorder;
        bool actif = r.Active(p.Id);
        var symbole = new Border { Width = actif ? 12 : 13, Height = actif ? 12 : 13, CornerRadius = new CornerRadius(actif ? 2 : 7), Background = actif ? Brushes.White : P("Direct") };
        var bouton = new Button
        {
            Style = S("Btn"), Width = 42, Height = 42, Padding = new Thickness(0), Content = symbole, IsEnabled = !r.Busy(p.Id),
            Background = actif ? P("Direct") : P("Nuit2"), BorderBrush = actif ? P("Direct") : P("Nuit3"),
            ToolTip = actif ? $"Arrêter « {p.Name} »" : $"Démarrer « {p.Name} »",
        };
        bouton.Click += (_, _) =>
        {
            if (!A.Recorder.Active(p.Id) && p.SourceIds.Count == 0 && p.DisplayIds.Count == 0)
            {
                (Application.Current.MainWindow as MainWindow)?.Toast($"« {p.Name} » n'a aucune source : choisissez-les dans Display › Audio.");
                return;
            }
            _ = A.Recorder.ToggleAsync(p);
        };
        var nom = new DockPanel();
        nom.Children.Add(Marque((i + 1).ToString()));
        nom.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var etat = new TextBlock { FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        var milieu = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        milieu.Children.Add(nom);
        milieu.Children.Add(etat);
        var g = new DockPanel();
        DockPanel.SetDock(bouton, Dock.Left);
        g.Children.Add(bouton);
        g.Children.Add(milieu);
        var carte = new Border
        {
            Child = g, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(11, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8),
            Background = P("Encre"), BorderBrush = P(actif ? "RougeBord" : "Nuit3"),
        };
        carte.Tag = new Action(() =>
        {
            bool a = r.Active(p.Id);
            etat.Text = a ? r.Duration(p.Id).ToString(@"hh\:mm\:ss") + " · " + Taille(r.Bytes(p.Id)) + (p.DisplayIds.Count > 0 ? " · vidéo" : " · " + p.Format.ToUpperInvariant())
                : r.Busy(p.Id) ? r.Message(p.Id) : AudioPanel.Resume(p);
            etat.Foreground = P(a ? "RougeTexte" : "Brume");
        });
        return carte;
    }

    Border CarteSortie(VirtualSoundSettings v, int i)
    {
        var inter = new CheckBox { Style = S("Interrupteur"), IsChecked = v.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Activer ou couper cette sortie" };
        inter.Click += (_, _) => { v.Enabled = inter.IsChecked == true; A.Apply(); };
        var nom = new DockPanel();
        nom.Children.Add(Marque(AudioPanel.MarqueSortie(i)));
        nom.Children.Add(new TextBlock { Text = v.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var etat = new TextBlock
        {
            Text = v.Enabled ? A.Sorties.Status(v.Id) : "Désactivée", FontFamily = (FontFamily)Application.Current.FindResource("Mono"), FontSize = 12.5,
            Foreground = P(v.Enabled && !A.Sorties.Actif(v.Id) ? "RougeTexte" : "Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0),
        };
        var milieu = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        milieu.Children.Add(nom);
        milieu.Children.Add(etat);
        var g = new DockPanel();
        DockPanel.SetDock(inter, Dock.Left);
        g.Children.Add(inter);
        g.Children.Add(milieu);
        return new Border
        {
            Child = g, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8),
            Background = P("Encre"), BorderBrush = P(A.Sorties.Actif(v.Id) ? "OrVif" : "Nuit3"),
        };
    }
}
