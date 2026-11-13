using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Barre du bas (design « Pupitre », MyChurch 3.2) : les enregistrements (● / ■, nom, chrono) et les sorties MyVirtualSound
/// (« A · Zoom actif ») en pastilles. Un clic démarre ou arrête l'enregistrement, active ou coupe la sortie.
/// Ils se créent dans Display › Audio, avec le mélangeur.
/// </summary>
public sealed class AudioBref : StackPanel
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly DispatcherTimer minuterie = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly List<Action> chronos = new();
    string signature = "";

    public AudioBref()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        A.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        minuterie.Tick += (_, _) => { if (IsVisible) Maj(); };
        Loaded += (_, _) => minuterie.Start();
        Unloaded += (_, _) => minuterie.Stop();
        Maj();
    }

    /// <summary>Reconstruit les pastilles si la liste ou leur état a changé, sinon avance seulement les chronos.</summary>
    void Maj()
    {
        var r = A.Recorder;
        string sig = string.Join("|", C.Profiles.Select(p => p.Id + p.Name + r.Active(p.Id) + r.Busy(p.Id)))
                   + "#" + string.Join("|", C.VirtualSounds.Select(v => v.Id + v.Name + v.Enabled + A.Sorties.Actif(v.Id)));
        if (sig != signature)
        {
            signature = sig;
            Children.Clear();
            chronos.Clear();
            for (int i = 0; i < C.Profiles.Count; i++) Children.Add(PuceEnregistrement(C.Profiles[i]));
            for (int i = 0; i < C.VirtualSounds.Count; i++) Children.Add(PuceSortie(C.VirtualSounds[i], i));
            if (Children.Count == 0)
            {
                var lien = new Button
                {
                    Style = S("BtnPlat"), Content = "Enregistrements et sortie Zoom : Display › Audio", FontSize = 13, Foreground = P("Brume"), Padding = new Thickness(8, 6, 8, 6),
                    ToolTip = "Créer un enregistrement du culte ou une sortie MyVirtualSound",
                };
                lien.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.MontrerAudio();
                Children.Add(lien);
            }
        }
        foreach (var maj in chronos) maj();
    }

    static StackPanel Contenu(UIElement icone, string nom, TextBlock detail)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(icone);
        p.Children.Add(new TextBlock { Text = nom, FontSize = 13, FontWeight = FontWeights.Bold, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        detail.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
        detail.FontSize = 12;
        detail.Margin = new Thickness(8, 1, 0, 0);
        detail.VerticalAlignment = VerticalAlignment.Center;
        p.Children.Add(detail);
        return p;
    }

    static Button Puce(object contenu) => new()
    {
        Style = S("Btn"), Height = 34, Padding = new Thickness(8, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), Content = contenu, Background = P("Page"),
    };

    /// <summary>« ● Culte complet 00:31:11 » : un clic démarre ou arrête l'enregistrement.</summary>
    Button PuceEnregistrement(RecordingProfile p)
    {
        var r = A.Recorder;
        bool actif = r.Active(p.Id);
        var rond = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        rond.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = P("Direct"), StrokeThickness = 2 });
        rond.Children.Add(actif
            ? new System.Windows.Shapes.Rectangle { Width = 7, Height = 7, RadiusX = 1, RadiusY = 1, Fill = P("Direct") }
            : new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = P("Direct") });
        var chrono = new TextBlock();
        var b = Puce(Contenu(rond, p.Name, chrono));
        b.IsEnabled = !r.Busy(p.Id);
        b.ToolTip = (actif ? $"Arrêter « {p.Name} »" : $"Démarrer « {p.Name} »") + " · " + AudioPanel.Resume(p);
        if (actif) { b.BorderBrush = P("Direct"); b.Foreground = P("Direct"); }
        b.Click += (_, _) => _ = A.Recorder.ToggleAsync(p);
        chronos.Add(() =>
        {
            bool a = r.Active(p.Id);
            chrono.Text = a ? r.Duration(p.Id).ToString(@"hh\:mm\:ss") : r.Busy(p.Id) ? "…" : "prêt";
            chrono.Foreground = P(a ? "Direct" : "Brume2");
        });
        return b;
    }

    /// <summary>« A · Zoom actif » : un clic active ou coupe la sortie MyVirtualSound.</summary>
    Button PuceSortie(VirtualSoundSettings v, int i)
    {
        bool actif = v.Enabled && A.Sorties.Actif(v.Id);
        var etat = new TextBlock { Text = !v.Enabled ? "coupée" : actif ? "active" : "erreur", Foreground = P(!v.Enabled ? "Brume2" : actif ? "Or" : "RougeTexte") };
        var marque = new Border
        {
            Background = P(actif ? "OrVif" : "Nuit2"), CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = AudioPanel.MarqueSortie(i), FontSize = 11, FontWeight = FontWeights.ExtraBold, Foreground = P(actif ? "SurOr" : "Brume") },
        };
        var b = Puce(Contenu(marque, v.Name, etat));
        b.ToolTip = (v.Enabled ? "Couper" : "Activer") + $" la sortie « {v.Name} » (MyVirtualSound)" + (v.Enabled ? " · " + A.Sorties.Status(v.Id) : "");
        if (actif) b.BorderBrush = P("OrVif");
        b.Click += (_, _) => { v.Enabled = !v.Enabled; A.Apply(); };
        return b;
    }
}
