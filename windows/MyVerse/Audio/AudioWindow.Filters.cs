using System.Windows;
using System.Windows.Controls;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public sealed partial class AudioWindow
{
    static readonly string[] FilterNames = { "Gain", "Passe-haut", "Passe-bas", "Égaliseur paramétrique", "Noise gate", "Expandeur", "Compresseur", "Compression sidechain", "Limiteur", "Réduction du bruit (spectrale)" };
    void EditFilters(AudioSource source)
    {
        var panel = new StackPanel();
        void Build()
        {
            panel.Children.Clear(); var toolbar = new WrapPanel(); panel.Children.Add(toolbar);
            var choice = new ComboBox { ItemsSource = FilterNames, SelectedIndex = 0, MinWidth = 250, Margin = new Thickness(0, 0, 8, 0) }; toolbar.Children.Add(choice);
            toolbar.Children.Add(Button("Ajouter", () =>
            {
                var kind = (AudioFilterKind)choice.SelectedIndex;
                source.Filters.Add(new() { Kind = kind, Name = FilterNames[(int)kind], ThresholdDb = kind == AudioFilterKind.Limiter ? -1 : kind == AudioFilterKind.NoiseReduction ? -55 : -30 }); Save(); Build();
            }));
            var preset = new ComboBox { ItemsSource = C.FilterPresets.Keys.ToList(), MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) }; toolbar.Children.Add(preset);
            toolbar.Children.Add(Button("Charger préréglage", () => { if (preset.SelectedItem is string name) { source.Filters = AudioConfiguration.Copy(C.FilterPresets[name]); Save(); Build(); } }));
            toolbar.Children.Add(Button("Sauver préréglage", () => { var name = Dialogue.Demander(this, "Nom du préréglage", source.Name); if (!string.IsNullOrWhiteSpace(name)) { C.FilterPresets[name] = AudioConfiguration.Copy(source.Filters); Save(); Build(); } }));
            var otherSources = C.Sources.Where(s => s.Id != source.Id).ToList();
            if (otherSources.Count > 0)
            {
                var row = Row(); panel.Children.Add(row); var others = new ComboBox { ItemsSource = otherSources.Select(s => s.Name).ToList(), SelectedIndex = 0, Width = 240 }; row.Children.Add(others);
                row.Children.Add(Button("Copier cette chaîne vers la source choisie", () => { otherSources[others.SelectedIndex].Filters = AudioConfiguration.Copy(source.Filters); Save(); }));
            }
            foreach (var f in source.Filters)
            {
                var p = new StackPanel(); Heading(p, f.Name); Field(p, "Nom du filtre", f.Name, v => f.Name = v); Check(p, "Filtre activé", f.Enabled, b => f.Enabled = b);
                if (f.Kind is AudioFilterKind.Gain or AudioFilterKind.Equalizer or AudioFilterKind.Compressor or AudioFilterKind.Sidechain) Number(p, "Gain / compensation (dB)", f.GainDb, -60, 30, v => f.GainDb = v);
                if (f.Kind is AudioFilterKind.HighPass or AudioFilterKind.LowPass or AudioFilterKind.Equalizer) { Number(p, "Fréquence (Hz)", f.Frequency, 20, 20000, v => f.Frequency = v); Number(p, "Facteur Q", f.Q, .1, 20, v => f.Q = v); }
                if (f.Kind is AudioFilterKind.Gate or AudioFilterKind.Expander or AudioFilterKind.Compressor or AudioFilterKind.Sidechain or AudioFilterKind.Limiter or AudioFilterKind.NoiseReduction) Number(p, f.Kind == AudioFilterKind.NoiseReduction ? "Plancher de bruit (dBFS)" : "Seuil (dBFS)", f.ThresholdDb, -90, 0, v => f.ThresholdDb = v);
                if (f.Kind is AudioFilterKind.Expander or AudioFilterKind.Compressor or AudioFilterKind.Sidechain) Number(p, "Ratio", f.Ratio, 1, 100, v => f.Ratio = v);
                if (f.Kind is AudioFilterKind.Gate or AudioFilterKind.Expander or AudioFilterKind.Compressor or AudioFilterKind.Sidechain or AudioFilterKind.Limiter)
                {
                    Number(p, "Attaque (ms)", f.AttackMs, .1, 2000, v => f.AttackMs = v); Number(p, "Relâchement (ms)", f.ReleaseMs, 1, 5000, v => f.ReleaseMs = v);
                    if (f.Kind is AudioFilterKind.Gate or AudioFilterKind.Expander) Number(p, "Maintien (ms)", f.HoldMs, 0, 2000, v => f.HoldMs = v);
                }
                if (f.Kind == AudioFilterKind.Sidechain)
                {
                    var options = C.Sources.Where(s => s.Id != source.Id).ToList();
                    if (options.Count > 0) Choice(p, "Source de déclenchement", options.Select(s => s.Name).ToList(), options.FindIndex(s => s.Id == f.SidechainSourceId), i => f.SidechainSourceId = options[i].Id);
                    p.Children.Add(Text("Le niveau pré-filtre de la source choisie commande la compression de cette source. Une source désactivée ou en sourdine ne déclenche pas le sidechain."));
                }
                if (f.Kind == AudioFilterKind.NoiseReduction) p.Children.Add(Text("Suppression spectrale locale, sans service externe. Réglez le plancher à partir d’un moment de silence. Elle peut modifier le timbre ; écoutez avant de valider. Latence de traitement d’environ 5 ms."));
                var buttons = Row(); p.Children.Add(buttons);
                void Move(int delta) { int index = source.Filters.IndexOf(f), next = Math.Clamp(index + delta, 0, source.Filters.Count - 1); source.Filters.RemoveAt(index); source.Filters.Insert(next, f); Save(); Build(); }
                buttons.Children.Add(Button("↑", () => Move(-1))); buttons.Children.Add(Button("↓", () => Move(1)));
                buttons.Children.Add(Button("Supprimer", () => { source.Filters.Remove(f); Save(); Build(); })); panel.Children.Add(Card(p));
            }
        }
        Build(); ShowEditor("Filtres ordonnés · " + source.Name, panel);
    }
}
