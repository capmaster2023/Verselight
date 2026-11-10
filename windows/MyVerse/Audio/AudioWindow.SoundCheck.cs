using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public sealed partial class AudioWindow
{
    async void SoundCheckWizard()
    {
        if (!audio.Running) { Dialogue.Informer(this, "Activez le moteur audio et configurez les périphériques avant la balance."); return; }
        if (audio.Recorder.Active) { Dialogue.Informer(this, "Faites la balance avant de démarrer l’enregistrement."); return; }
        var panel = new StackPanel();
        var window = new FenetreDialogue { Owner = this, Title = "Préparer le son du culte", Width = 850, Height = 680, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) } };
        Heading(panel, "Vérifier puis tester chaque voix et instrument"); panel.Children.Add(Text(audio.Diagnostic));
        panel.Children.Add(Text("Vérifiez les branchements, les canaux et les sorties. Pour chaque source : mesurez d’abord 3 secondes de silence, puis 6 secondes de voix ou d’instrument à son niveau habituel. Les mesures utilisent l’entrée avant les filtres et le volume commun."));
        var state = Text(""); panel.Children.Add(state);
        var ids = C.Sources.Where(s => s.Kind == AudioInputKind.Capture).ToList(); bool measuring = false, closed = false;
        window.Closed += (_, _) => closed = true;
        foreach (var source in ids)
        {
            var p = new StackPanel(); Heading(p, source.Name); var resultLabel = Text("Pas encore testé"); p.Children.Add(resultLabel);
            double noiseRms = .0001; bool noiseMeasured = false; SoundCheckResult? result = null; var original = AudioConfiguration.Copy(source.Filters); bool preview = false;
            async Task Measure(bool noise)
            {
                if (measuring) return; if (!noise && !noiseMeasured) throw new InvalidOperationException("Mesurez d’abord le silence pour cette source."); if (!audio.InputReady(source.Id)) throw new IOException("Périphérique absent ou canal inaccessible.");
                measuring = true; double squares = 0, peak = 0; long frames = 0, clipped = 0;
                try
                {
                    var until = DateTime.UtcNow.AddSeconds(noise ? 3 : 6);
                    var meter = audio.Engine.RawMeter(source.Id) ?? throw new IOException("Source non initialisée.");
                    meter.Reset();
                    long beforeFrames = 0, beforeClips = 0; double beforeSum = 0;
                    while (DateTime.UtcNow < until && !closed)
                    {
                        await Task.Delay(80); peak = meter.PeakHold;
                        state.Text = noise ? source.Name + " : gardez le silence…" : source.Name + " : parlez ou jouez maintenant…";
                    }
                    if (closed) return;
                    frames = meter.Frames - beforeFrames; squares = meter.SumSquares - beforeSum; clipped = meter.ClippedFrames - beforeClips;
                    if (frames < 48000) throw new IOException("Mesure trop courte ou moteur arrêté. Recommencez.");
                    if (noise) { noiseMeasured = true; noiseRms = Math.Sqrt(squares / Math.Max(1, frames)); resultLabel.Text = $"Bruit mesuré : {AudioMath.Db(noiseRms):0.0} dBFS. Passez au test de signal."; }
                    else { result = SoundCheck.Analyze(squares, frames, peak, noiseRms, clipped); resultLabel.Text = $"Moyenne {result.RmsDb:0.0} · crête {result.PeakDb:0.0} · bruit {result.NoiseDb:0.0} dBFS\n{result.Advice}"; }
                }
                finally { measuring = false; state.Text = ""; }
            }
            var buttons = new WrapPanel(); p.Children.Add(buttons);
            var noiseButton = Button("1 · Mesurer le silence", () => { _ = SafeMeasure(true); }); var signalButton = Button("2 · Tester la voix / l’instrument", () => { _ = SafeMeasure(false); });
            async Task SafeMeasure(bool noise) { try { await Measure(noise); } catch (Exception e) { if (!closed) Dialogue.Informer(window, e.Message); } }
            buttons.Children.Add(noiseButton); buttons.Children.Add(signalButton);
            buttons.Children.Add(Button("Écouter avant / après", () =>
            {
                if (result == null || result.NoSignal || result.Clipped) throw new InvalidOperationException("Mesurez un signal non saturé avant l’écoute.");
                preview = !preview; audio.PreviewFilters(source.Id, preview ? original.Concat(result.Proposed).ToList() : null); resultLabel.Text += preview ? "\nÉcoute : réglages proposés." : "\nÉcoute : réglages précédents.";
            }));
            buttons.Children.Add(Button("Valider ces réglages", () => { if (result == null || result.NoSignal || result.Clipped) throw new InvalidOperationException("Le signal ne permet pas de valider les réglages."); source.Filters = AudioConfiguration.Copy(original.Concat(result.Proposed).ToList()); original = AudioConfiguration.Copy(source.Filters); preview = false; audio.PreviewFilters(source.Id, null); Save(); resultLabel.Text += "\nRéglages validés."; }));
            window.Closed += (_, _) => { if (preview) audio.PreviewFilters(source.Id, null); }; panel.Children.Add(Card(p, source.Color));
        }
        EndpointChoice(panel, "Casque de vérification", C.MonitorDeviceId, id => C.MonitorDeviceId = id);
        Check(panel, "Écouter le bus Zoom", C.ListenZoom, b => C.ListenZoom = b);
        panel.Children.Add(Button("Tester le trajet vers Zoom", audio.TestZoom));
        panel.Children.Add(Text("L’écoute compare le signal local avant / après traitement. Vérifiez aussi le microphone sélectionné dans Zoom et faites un essai avec une personne à distance. Les entrées directes ne mesurent pas l’acoustique de la salle."));
        panel.Children.Add(Button("Terminer la préparation", () => window.Close())); window.ShowDialog(); Refresh();
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
