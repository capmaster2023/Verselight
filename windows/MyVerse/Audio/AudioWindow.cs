using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public sealed partial class AudioWindow : FenetreDialogue
{
    readonly ChurchAudioService audio = App.Audio;
    AudioConfiguration C => audio.Config;
    readonly TabControl tabs = new();
    readonly StackPanel simple = new(), sources = new(), buses = new(), modes = new(), profiles = new(), advanced = new();
    readonly TextBlock diagnostic = Text(""), recordStatus = Text("");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly List<Action> meters = new();
    AudioScene? previousScene;
    bool refreshing;
    public AudioWindow()
    {
        Title = "MyChurch — Mixeur audio"; Width = 1120; Height = 800; MinWidth = 760; MinHeight = 520;
        var root = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var commands = Row(); top.Children.Add(commands);
        commands.Children.Add(Button("Activer le moteur", () => { audio.Start(); Refresh(); }));
        commands.Children.Add(Button("Arrêter le moteur", () => { audio.Stop(); Refresh(); }));
        commands.Children.Add(Button("Préparer le son du culte", SoundCheckWizard));
        commands.Children.Add(Button("Actualiser les périphériques", () => { audio.Refresh(true); Refresh(); }));
        top.Children.Add(diagnostic); top.Children.Add(recordStatus);
        root.Children.Add(tabs); Content = root;
        AddTab("Culte · vue simple", simple); AddTab("Sources et filtres", sources); AddTab("Bus et sorties", buses); AddTab("Modes de culte", modes); AddTab("Enregistrement", profiles); AddTab("Réglages avancés", advanced);
        timer.Tick += (_, _) => { diagnostic.Text = audio.Diagnostic; recordStatus.Text = audio.Recorder.Message + (audio.Recorder.Active ? " · " + audio.Recorder.Duration.ToString(@"hh\:mm\:ss") : ""); foreach (var update in meters) update(); };
        Closed += (_, _) => { timer.Stop(); audio.Recorder.Changed -= RecorderChanged; };
        audio.Recorder.Changed += RecorderChanged;
        Refresh(); timer.Start();
    }
    void RecorderChanged() => Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) Refresh(); }));
    void AddTab(string name, Panel panel) => tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) } });
    static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 5) };
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Pinceau("Blanc"), Margin = new Thickness(0, 4, 8, 6), FontSize = 14 };
    Button Button(string name, Action action)
    {
        var b = new Button { Content = name, Style = MainWindow.Style_("Btn"), Margin = new Thickness(0, 2, 7, 4), Padding = new Thickness(10, 7, 10, 7) };
        b.Click += (_, _) => Run(action); return b;
    }
    void Run(Action action) { try { action(); } catch (Exception e) { Dialogue.Informer(this, e.Message); } }
    void Save(int fade = 20) { if (!refreshing) audio.Apply(fade); }
    static Border Card(UIElement content, string? color = null)
    {
        Brush border = MainWindow.Pinceau("Trait2");
        if (color != null) try { border = (Brush)new BrushConverter().ConvertFromString(color)!; } catch (Exception) { }
        return new() { Child = content, Background = MainWindow.Pinceau("Nuit2"), BorderBrush = border, BorderThickness = new Thickness(2, 1, 1, 1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
    }
    static void Heading(Panel panel, string name) { var t = Text(name); t.FontSize = 20; t.FontWeight = FontWeights.Bold; panel.Children.Add(t); }
    TextBox Field(Panel panel, string label, string value, Action<string> set)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 5) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(240) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(Text(label)); var box = new TextBox { Text = value, Style = MainWindow.Style_("Champ"), MinWidth = 100 };
        Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
        box.LostKeyboardFocus += (_, _) => Run(() => { set(box.Text); Save(); }); return box;
    }
    void Number(Panel panel, string label, double value, double min, double max, Action<double> set)
    {
        Field(panel, label, value.ToString("0.###", CultureInfo.CurrentCulture), text =>
        {
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < min || n > max) throw new InvalidDataException($"{label} : entre {min} et {max}."); set(n);
        });
    }
    CheckBox Check(Panel panel, string label, bool value, Action<bool> set)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 10, 6), Foreground = MainWindow.Pinceau("Blanc") }; panel.Children.Add(check);
        check.Click += (_, _) => Run(() => { set(check.IsChecked == true); Save(); }); return check;
    }
    ComboBox Choice(Panel panel, string label, IReadOnlyList<string> labels, int index, Action<int> set)
    {
        var row = Row(); row.Children.Add(new TextBlock { Text = label, Width = 240, VerticalAlignment = VerticalAlignment.Center, Foreground = MainWindow.Pinceau("Blanc") });
        var combo = new ComboBox { ItemsSource = labels, SelectedIndex = index, MinWidth = 250, MaxWidth = 650 }; row.Children.Add(combo); panel.Children.Add(row);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) Run(() => { set(combo.SelectedIndex); Save(); }); }; return combo;
    }
    void EndpointChoice(Panel panel, string label, string? current, Action<string?> set, bool input = false, bool disabled = true)
    {
        List<AudioEndpoint> endpoints;
        try { endpoints = audio.Endpoints(input); } catch (Exception) { endpoints = new(); }
        var ids = new List<string?>(); var labels = new List<string>();
        if (disabled) { ids.Add(null); labels.Add("Désactivé — aucune sortie"); }
        ids.Add(""); labels.Add("Périphérique Windows par défaut");
        foreach (var e in endpoints) { ids.Add(e.Id); labels.Add(e.ToString()); }
        if (current != null && !ids.Contains(current)) { ids.Add(current); labels.Add("Périphérique absent · " + current); }
        Choice(panel, label, labels, Math.Max(0, ids.IndexOf(current)), i => set(ids[i]));
    }
    void Fader(Panel panel, string label, double db, Action<double> set, double maximum = 24)
    {
        var row = Row(); row.Children.Add(new TextBlock { Text = label, Width = 170, Foreground = MainWindow.Pinceau("Blanc"), VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = -90, Maximum = maximum, Value = db, Width = 270, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        var precise = new TextBox { Text = db.ToString("0.0"), Width = 72, Margin = new Thickness(10, 0, 4, 0), Style = MainWindow.Style_("Champ") }; bool updating = false;
        slider.ValueChanged += (_, _) => { if (updating) return; precise.Text = slider.Value.ToString("0.0"); set(slider.Value); Save(); };
        precise.LostKeyboardFocus += (_, _) => Run(() => { if (!double.TryParse(precise.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < -90 || n > maximum) throw new InvalidDataException($"Volume : −90 à +{maximum} dB."); updating = true; slider.Value = n; updating = false; set(n); Save(); });
        row.Children.Add(slider); row.Children.Add(precise); row.Children.Add(Text("dB")); panel.Children.Add(row);
    }
    void Meter(Panel panel, string id)
    {
        var row = Row(); var bars = new StackPanel { Width = 300, VerticalAlignment = VerticalAlignment.Center };
        var l = new ProgressBar { Minimum = -60, Maximum = 0, Height = 7, Margin = new Thickness(0, 1, 0, 2), Foreground = Brushes.ForestGreen };
        var r = new ProgressBar { Minimum = -60, Maximum = 0, Height = 7, Foreground = Brushes.ForestGreen }; bars.Children.Add(l); bars.Children.Add(r);
        var label = Text("−∞ dBFS"); row.Children.Add(bars); row.Children.Add(label); panel.Children.Add(row);
        var status = Text(""); panel.Children.Add(status);
        meters.Add(() =>
        {
            var m = audio.Engine.Meter(id); if (m == null) return;
            l.Value = AudioMath.Db(m.Left); r.Value = AudioMath.Db(m.Right);
            label.Text = $"  L {AudioMath.Db(m.Left):0.0} · R {AudioMath.Db(m.Right):0.0} · crête {AudioMath.Db(m.PeakHold):0.0} dBFS" + (audio.Engine.RawMeter(id)?.Clipped == true ? "  SATURATION ENTRÉE" : m.Clipped ? "  SATURATION" : ""); label.Foreground = MainWindow.Pinceau(m.Clipped ? "Rouge" : "Blanc");
            status.Text = C.Sources.Any(s => s.Id == id) ? audio.Engine.Status(id) + (m.Rms < .0001 ? " · sans signal" : "") : "";
        });
    }
    void Refresh()
    {
        refreshing = true;
        try
        {
            meters.Clear(); foreach (var p in new[] { simple, sources, buses, modes, profiles, advanced }) p.Children.Clear();
            BuildSimple(); BuildSources(); BuildBuses(); BuildModes(); BuildProfiles(); BuildAdvanced();
        }
        finally { refreshing = false; }
    }
    void BuildSimple()
    {
        Heading(simple, "Le son du culte"); var sceneButtons = new WrapPanel(); simple.Children.Add(sceneButtons);
        foreach (var scene in C.Scenes) sceneButtons.Children.Add(Button(scene.Name, () => ApplyScene(scene)));
        simple.Children.Add(Text("Un mode doit être mémorisé après la balance. Les modes sans réglages sont signalés à l’application."));
        var recordingButtons = new WrapPanel(); simple.Children.Add(recordingButtons);
        foreach (var profile in C.Profiles) recordingButtons.Children.Add(RecordButton(profile));
        if (C.Profiles.Count == 0) simple.Children.Add(Text("Créez jusqu’à quatre boutons dans l’onglet Enregistrement."));
        Check(simple, "Écouter le mix Zoom au casque", C.ListenZoom, b => C.ListenZoom = b);
        simple.Children.Add(Text("Écoute du signal fourni à Zoom, avant les traitements Zoom et la transmission. Ce n’est pas une mesure du son reçu à distance."));
        foreach (var s in C.Sources)
        {
            var p = new StackPanel(); Heading(p, s.Name); Meter(p, s.Id);
            Fader(p, "Volume commun", s.VolumeDb, v => s.VolumeDb = v);
            var row = Row(); p.Children.Add(row); Check(row, "Active", s.Enabled, b => { C.Activate(s.Id, b); Dispatcher.BeginInvoke(new Action(Refresh)); }); Check(row, "Sourdine", s.Muted, b => s.Muted = b);
            simple.Children.Add(Card(p, s.Color));
        }
    }
    void BuildSources()
    {
        var add = new WrapPanel(); sources.Children.Add(add);
        add.Children.Add(Button("+ Entrée micro / interface", () => AddSource(AudioInputKind.Capture)));
        add.Children.Add(Button("+ Son Windows (loopback)", () => AddSource(AudioInputKind.Loopback)));
        add.Children.Add(Button("+ Média audio / vidéo", () => AddSource(AudioInputKind.Media)));
        foreach (var s in C.Sources)
        {
            var p = new StackPanel(); Heading(p, s.Name); Meter(p, s.Id);
            var buttons = new WrapPanel(); p.Children.Add(buttons);
            buttons.Children.Add(Button("Propriétés", () => SourceProperties(s))); buttons.Children.Add(Button("Filtres", () => EditFilters(s)));
            buttons.Children.Add(Button("Dupliquer", () => { var copy = AudioConfiguration.Copy(s); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " (copie)"; copy.Enabled = false; C.Sources.Add(copy); Save(); Refresh(); }));
            buttons.Children.Add(Button("↑", () => MoveSource(s, -1))); buttons.Children.Add(Button("↓", () => MoveSource(s, 1)));
            buttons.Children.Add(Button("Supprimer", () => { if (audio.Recorder.Active) throw new InvalidOperationException("Arrêtez l’enregistrement avant de supprimer une source."); C.Sources.Remove(s); Save(); Refresh(); }));
            Fader(p, "Volume commun", s.VolumeDb, v => s.VolumeDb = v);
            Check(p, "Sourdine commune", s.Muted, b => s.Muted = b);
            foreach (var bus in C.Buses)
            {
                var send = s.Sends.GetValueOrDefault(bus.Id); if (send == null) continue;
                var row = Row(); p.Children.Add(row); Check(row, "Vers " + bus.Name, send.Enabled, b => send.Enabled = b); Check(row, "Sourdine de cet envoi", send.Muted, b => send.Muted = b);
                Fader(p, "Envoi " + bus.Name, send.VolumeDb, v => send.VolumeDb = v, 12);
            }
            sources.Children.Add(Card(p, s.Color));
        }
    }
    void AddSource(AudioInputKind kind)
    {
        var s = new AudioSource { Kind = kind, Name = kind == AudioInputKind.Capture ? "Nouvelle entrée" : kind == AudioInputKind.Loopback ? "Son Windows" : "Média MyChurch", Stereo = kind != AudioInputKind.Capture };
        if (kind == AudioInputKind.Media) s.FollowProjection = true;
        C.Sources.Add(s); Save(); Refresh(); SourceProperties(s);
    }
    void MoveSource(AudioSource source, int delta) { int index = C.Sources.IndexOf(source), to = Math.Clamp(index + delta, 0, C.Sources.Count - 1); C.Sources.RemoveAt(index); C.Sources.Insert(to, source); Save(); Refresh(); }
    void SourceProperties(AudioSource s)
    {
        var p = new StackPanel(); Field(p, "Nom", s.Name, v => s.Name = v); Field(p, "Couleur (#RRGGBB)", s.Color, v => { _ = new BrushConverter().ConvertFromString(v); s.Color = v; });
        if (s.Kind != AudioInputKind.Media) EndpointChoice(p, "Périphérique", s.DeviceId, id => s.DeviceId = id ?? "", s.Kind == AudioInputKind.Capture, false);
        else
        {
            Check(p, "Suivre la vidéo projetée dans Display", s.FollowProjection, b => s.FollowProjection = b);
            var file = Field(p, "Fichier média indépendant", s.MediaPath, v => s.MediaPath = v);
            p.Children.Add(Button("Choisir un média", () => { var d = new OpenFileDialog { Filter = "Audio / vidéo|*.wav;*.mp3;*.flac;*.m4a;*.mp4;*.mkv;*.wmv;*.avi|Tous|*.*" }; if (d.ShowDialog(this) == true) { s.MediaPath = file.Text = d.FileName; s.FollowProjection = false; Save(); } }));
            p.Children.Add(Text("La lecture indépendante commence à l’activation du moteur ou à l’ajout du fichier. Recréez la lecture avec le bouton ci-dessous."));
            p.Children.Add(Button("Relancer ce média", () => audio.RestartMedia(s)));
        }
        Number(p, "Canal physique (1 à 64)", s.Channel + 1, 1, 64, v => s.Channel = (int)v - 1);
        Check(p, "Stéréo : ce canal et le suivant", s.Stereo, b => s.Stereo = b);
        p.Children.Add(Text("Seuls les canaux réellement exposés par WASAPI partagé sont accessibles. Si l’interface expose un seul canal, les autres entrées ne peuvent pas être séparées dans ce mode. Vérifiez les modes du pilote dans Windows. Aucune prise en charge ASIO n’est annoncée."));
        Field(p, "Groupe d’exclusion (facultatif)", s.ExclusionGroup, v => s.ExclusionGroup = v);
        Check(p, "Source active", s.Enabled, b => C.Activate(s.Id, b));
        Number(p, "Balance (−1 gauche, +1 droite)", s.Balance, -1, 1, v => s.Balance = v);
        Check(p, "Conversion mono", s.Mono, b => s.Mono = b); Check(p, "Inverser la polarité", s.InvertPolarity, b => s.InvertPolarity = b);
        Number(p, "Retard (0 à 2 000 ms)", s.DelayMs, 0, 2000, v => s.DelayMs = (int)v);
        Choice(p, "Monitoring", new[] { "Désactivé", "Écoute uniquement", "Écoute et sortie" }, (int)s.Monitoring, i => s.Monitoring = (AudioMonitoring)i);
        ShowEditor("Propriétés · " + s.Name, p); Refresh();
    }
    void BuildBuses()
    {
        buses.Children.Add(Text("Les volumes et sourdines ci-dessous agissent sur leur bus uniquement. Un bus sans sortie et sans consommateur ne produit pas de sortie physique."));
        foreach (var bus in C.Buses)
        {
            var p = new StackPanel(); Heading(p, bus.Name); Meter(p, bus.Id); Fader(p, "Master " + bus.Name, bus.VolumeDb, v => bus.VolumeDb = v, 12);
            Check(p, "Sourdine master", bus.Muted, b => bus.Muted = b); Check(p, "Sortie mono", bus.Mono, b => bus.Mono = b);
            EndpointChoice(p, "Sortie physique", bus.OutputDeviceId, id => bus.OutputDeviceId = id);
            if (bus.Id == "record") p.Children.Add(Text("Le bus Enregistrement alimente le fichier seulement lorsqu’un profil est actif. Une sortie physique est facultative."));
            buses.Children.Add(Card(p));
        }
        Heading(buses, "Zoom et casque");
        buses.Children.Add(Text("Avec un câble audio virtuel déjà installé : choisissez CABLE Input pour le bus Zoom, puis CABLE Output comme microphone dans Zoom. MyChurch ne crée pas de microphone virtuel et n’installe aucun pilote."));
        buses.Children.Add(Button("Tester la sortie Zoom (440 Hz, 2 s)", () => { if (!audio.Running || C.Buses.First(b => b.Id == "zoom").OutputDeviceId == null) throw new InvalidOperationException("Activez le moteur et une sortie Zoom."); audio.TestZoom(); }));
        EndpointChoice(buses, "Casque d’écoute indépendant", C.MonitorDeviceId, id => C.MonitorDeviceId = id);
        Fader(buses, "Volume casque", C.MonitorDb, v => C.MonitorDb = v, 0);
        Check(buses, "Écouter le mix Zoom", C.ListenZoom, b => C.ListenZoom = b);
        buses.Children.Add(Text("Pour éviter une réinjection : le retour des interlocuteurs Zoom doit sortir sur un périphérique distinct de toute sortie capturée par loopback. MyChurch bloque ses boucles locales détectables ; il ne peut pas inspecter le routage interne de Zoom."));
    }
    void ApplyScene(AudioScene scene)
    {
        if (scene.Sources.Count == 0) throw new InvalidOperationException("Ce mode n’a pas encore été mémorisé. Faites la balance puis cliquez sur « Mémoriser le mix actuel ».");
        previousScene = C.Snapshot("Mode précédent"); C.ApplyScene(scene); Save(scene.FadeMs); Refresh();
    }
    void BuildModes()
    {
        var top = Row(); modes.Children.Add(top);
        top.Children.Add(Button("+ Nouveau mode", () => { var name = Dialogue.Demander(this, "Nom du mode", "Nouveau mode"); if (name != null) { C.Scenes.Add(C.Snapshot(name)); Save(); Refresh(); } }));
        top.Children.Add(Button("Revenir au mode précédent", () => { if (previousScene != null) ApplyScene(previousScene); }));
        foreach (var scene in C.Scenes)
        {
            var p = new StackPanel(); Heading(p, scene.Name); Field(p, "Nom", scene.Name, n => scene.Name = n); Number(p, "Transition (ms)", scene.FadeMs, 20, 5000, v => scene.FadeMs = (int)v);
            var row = Row(); p.Children.Add(row); row.Children.Add(Button("Appliquer", () => ApplyScene(scene)));
            row.Children.Add(Button("Mémoriser le mix actuel", () => { scene.Sources = AudioConfiguration.Copy(C.Sources); Save(); Refresh(); }));
            row.Children.Add(Button("Supprimer", () => { C.Scenes.Remove(scene); Save(); Refresh(); })); modes.Children.Add(Card(p));
        }
        modes.Children.Add(Text("Les modes mémorisent l’activité, les sourdines, les volumes d’envoi et les chaînes de filtres, y compris le sidechain. Les restrictions du profil d’enregistrement restent prioritaires."));
    }
    void ShowEditor(string title, Panel panel)
    {
        var w = new FenetreDialogue { Title = title, Owner = this, Width = 880, Height = 700, MinWidth = 650, MinHeight = 400 };
        var root = new DockPanel { Margin = new Thickness(18) }; var close = Button("Fermer", () => { w.Close(); Save(); }); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); w.Content = root; w.ShowDialog();
    }
    void BuildAdvanced()
    {
        Heading(advanced, "Encodeurs et diagnostic"); var ffmpeg = Field(advanced, "Exécutable FFmpeg", C.FfmpegPath, v => C.FfmpegPath = v);
        advanced.Children.Add(Button("Choisir ffmpeg.exe", () => { var d = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe|Exécutables|*.exe" }; if (d.ShowDialog(this) == true) { C.FfmpegPath = ffmpeg.Text = d.FileName; Save(); } }));
        advanced.Children.Add(Text("WAV audio : encodeur PCM 24 bits intégré, sans FFmpeg. FLAC et vidéo : FFmpeg externe fourni par l’utilisateur. Repli vidéo réel vers MJPEG si l’encodeur demandé échoue. Le choix utilisé est affiché pendant l’enregistrement."));
        advanced.Children.Add(Button("Réinitialiser les crêtes", audio.Engine.ResetMeters));
        advanced.Children.Add(Button("Ajouter un bus logique (6 maximum)", () => { if (C.Buses.Count >= 6) throw new InvalidOperationException("Six bus maximum."); C.Buses.Add(new() { Id = Guid.NewGuid().ToString("N"), Name = "Piste logique " + (C.Buses.Count + 1) }); C.Normalize(); Save(); Refresh(); }));
        advanced.Children.Add(Text("Les pistes logiques supplémentaires sont des bus de routage ; elles ne sont pas automatiquement enregistrées en pistes séparées. Le fichier de cette version reçoit le bus Enregistrement stéréo."));
        advanced.Children.Add(Button("Récupérer une copie d’un WAV interrompu", () =>
        {
            var open = new OpenFileDialog { Filter = "WAV MyChurch|*.wav" }; if (open.ShowDialog(this) != true) return;
            var save = new SaveFileDialog { Filter = "WAV récupéré|*.wav", FileName = Path.GetFileNameWithoutExtension(open.FileName) + "-recupere.wav" }; if (save.ShowDialog(this) == true) { PcmRecordingWriter.RecoverCopy(open.FileName, save.FileName); Dialogue.Informer(this, "Copie récupérée. L’original est conservé."); }
        }));
        advanced.Children.Add(Text("Synchronisation : moteur flottant 48 kHz, blocs de 10 ms, mémoire bornée, correction adaptative de la dérive de chaque interface (jusqu’à ±0,5 %). Le taux de sous-alimentation et les déconnexions doivent être contrôlés lors d’un essai matériel prolongé."));
        advanced.Children.Add(Text("Configuration conservée dans les réglages existants. La migration depuis 1.1.2 ajoute une configuration audio vide, sans changer Bible, chants, Duo ou OBS."));
    }
}
