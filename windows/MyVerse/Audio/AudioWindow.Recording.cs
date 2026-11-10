using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public sealed partial class AudioWindow
{
    Button RecordButton(RecordingProfile profile)
    {
        bool active = audio.Recorder.ActiveProfile?.Id == profile.Id;
        var b = Button(profile.Icon + " " + profile.Name + (active ? " · Arrêter" : " · Enregistrer"), () => { _ = audio.Recorder.ToggleAsync(profile); });
        try { b.BorderBrush = (Brush)new BrushConverter().ConvertFromString(profile.Color)!; b.BorderThickness = new Thickness(active ? 3 : 2); } catch (Exception) { }
        b.IsEnabled = !audio.Recorder.Busy; return b;
    }
    void BuildProfiles()
    {
        Heading(profiles, "Quatre boutons d’enregistrement maximum");
        var add = Button("+ Créer un bouton", () =>
        {
            if (C.Profiles.Count >= 4) throw new InvalidOperationException("Quatre profils maximum.");
            var p = new RecordingProfile { Name = new[] { "Prédication", "Louange", "Solo", "Culte complet" }[C.Profiles.Count], Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MyChurch") };
            C.Profiles.Add(p); Save(); Refresh(); EditProfile(p);
        }); add.IsEnabled = C.Profiles.Count < 4; profiles.Children.Add(add);
        foreach (var profile in C.Profiles)
        {
            var p = new StackPanel(); Heading(p, profile.Name); var buttons = Row(); p.Children.Add(buttons); buttons.Children.Add(RecordButton(profile)); buttons.Children.Add(Button("Modifier", () => { EditProfile(profile); Refresh(); }));
            buttons.Children.Add(Button("Supprimer", () => { if (audio.Recorder.ActiveProfile?.Id == profile.Id) throw new InvalidOperationException("Arrêtez ce profil avant de le supprimer."); C.Profiles.Remove(profile); Save(); Refresh(); }));
            p.Children.Add(Text($"{(profile.AudioOnly ? "Audio" : "Vidéo")} · {profile.Format.ToUpperInvariant()} · {profile.Directory}"));
            p.Children.Add(Text("Sources autorisées : " + string.Join(", ", C.Sources.Where(s => profile.AllowedSources.Contains(s.Id)).Select(s => s.Name))));
            if (audio.Recorder.ActiveProfile?.Id == profile.Id) p.Children.Add(Text("Restrictions actives sur Zoom et Enregistrement" + (profile.ApplyToRoom ? ", ainsi que Salle." : ". La salle conserve son mix indépendant.")));
            profiles.Children.Add(Card(p, profile.Color));
        }
        profiles.Children.Add(Text("Un second clic arrête et finalise le fichier. Un autre bouton vérifie son profil, finalise le précédent puis crée un nouveau fichier. Les sources exclues restent coupées dans Zoom et Enregistrement même si vous changez de mode."));
        profiles.Children.Add(Text("WAV : PCM 24 bits RF64 avec points de récupération. Vidéo : MKV, conteneur résistant aux interruptions ; le dernier fragment peut être incomplet. Aucun remuxage ne remplace l’original automatiquement."));
    }
    void EditProfile(RecordingProfile profile)
    {
        var p = new StackPanel(); Field(p, "Nom du bouton", profile.Name, s => profile.Name = s); Field(p, "Couleur", profile.Color, s => { _ = new BrushConverter().ConvertFromString(s); profile.Color = s; }); Field(p, "Icône (texte / symbole)", profile.Icon, s => profile.Icon = s);
        var dir = Field(p, "Dossier de destination", profile.Directory, s => profile.Directory = s);
        p.Children.Add(Button("Choisir le dossier", () => { var d = new OpenFolderDialog { Title = "Dossier des enregistrements" }; if (d.ShowDialog(this) == true) { profile.Directory = dir.Text = d.FolderName; Save(); } }));
        var formats = new[] { "WAV · audio uniquement", "FLAC · audio uniquement", "MKV · vidéo et audio" };
        Choice(p, "Format", formats, profile.AudioOnly ? profile.Format == "flac" ? 1 : 0 : 2, i => { profile.AudioOnly = i != 2; profile.Format = i == 0 ? "wav" : i == 1 ? "flac" : "mkv"; });
        Number(p, "Largeur vidéo (pixels pairs)", profile.Width, 320, 1920, v => profile.Width = (int)v); Number(p, "Hauteur vidéo (pixels pairs)", profile.Height, 180, 1080, v => profile.Height = (int)v);
        var rates = new[] { 24, 25, 30, 50, 60 }; Choice(p, "Images par seconde", rates.Select(n => n.ToString()).ToList(), Array.IndexOf(rates, profile.Fps), i => profile.Fps = rates[i]);
        Number(p, "Débit vidéo (kbit/s)", profile.VideoKbps, 500, 50000, v => profile.VideoKbps = (int)v); Number(p, "Débit audio AAC (kbit/s)", profile.AudioKbps, 64, 512, v => profile.AudioKbps = (int)v);
        var encoders = new[] { "mjpeg", "libx264", "h264_nvenc", "h264_qsv", "h264_amf" }; Choice(p, "Encodeur demandé", encoders, Math.Max(0, Array.IndexOf(encoders, profile.Encoder)), i => profile.Encoder = encoders[i]);
        p.Children.Add(Text("L’encodeur est testé sur la machine avant le démarrage. Si l’encodeur demandé est indisponible, un test MJPEG est effectué. L’enregistrement affiche l’encodeur réellement utilisé. WAV et FLAC conservent le son à 48 kHz ; WAV est en PCM 24 bits."));
        Heading(p, "Sources audio autorisées");
        foreach (var source in C.Sources)
        {
            Check(p, source.Name, profile.AllowedSources.Contains(source.Id), b => { if (b) profile.AllowedSources.Add(source.Id); else profile.AllowedSources.Remove(source.Id); audio.Recorder.UpdateSelection(profile); });
            Fader(p, "Envoi fichier · " + source.Name, profile.SendDb.GetValueOrDefault(source.Id), v => { profile.SendDb[source.Id] = v; audio.Recorder.UpdateSelection(profile); }, 12);
        }
        Check(p, "Appliquer la sélection audio aussi à la salle", profile.ApplyToRoom, b => { profile.ApplyToRoom = b; audio.Recorder.UpdateSelection(profile); });
        p.Children.Add(Text("La sélection autorise les sources ; elle ne retire pas leurs sourdines explicites. Pendant l’enregistrement, ces autorisations peuvent changer ici. Les autres changements de format ou de composition s’appliquent au prochain fichier."));
        p.Children.Add(Button("Configurer la composition visuelle", () => EditVisuals(profile)));
        ShowEditor("Profil d’enregistrement · " + profile.Name, p);
    }
    void EditVisuals(RecordingProfile profile)
    {
        var panel = new StackPanel();
        void Build()
        {
            panel.Children.Clear(); var add = Row(); panel.Children.Add(add);
            foreach (var kind in new[] { "projection", "camera", "image", "video" })
                add.Children.Add(Button("+ " + kind, () => { profile.Visuals.Add(new() { Kind = kind }); Save(); Build(); }));
            panel.Children.Add(Text("Les éléments sont superposés dans l’ordre de la liste : le dernier est devant. Le cadrage est propre à l’enregistrement et ne modifie pas le projecteur. Les vidéos ajoutées ici fournissent l’image ; ajoutez aussi leur audio comme source du mixeur si nécessaire."));
            foreach (var visual in profile.Visuals)
            {
                var p = new StackPanel(); Heading(p, visual.Kind); Check(p, "Visible", visual.Visible, b => visual.Visible = b);
                if (visual.Kind == "camera")
                {
                    var devices = SourcesDisplay.Peripheriques(); if (devices.Count == 0) p.Children.Add(Text("Aucune caméra détectée."));
                    else Choice(p, "Caméra / carte de capture", devices.Select(d => d.Nom).ToList(), devices.FindIndex(d => d.Id == visual.DeviceId), i => visual.DeviceId = devices[i].Id);
                }
                if (visual.Kind is "image" or "video")
                {
                    var path = Field(p, "Fichier", visual.Path, s => visual.Path = s); p.Children.Add(Button("Choisir le fichier", () => { var d = new OpenFileDialog(); if (d.ShowDialog(this) == true) { path.Text = visual.Path = d.FileName; Save(); } }));
                }
                Number(p, "Position X (0 à 1)", visual.X, 0, 1, v => visual.X = v); Number(p, "Position Y (0 à 1)", visual.Y, 0, 1, v => visual.Y = v);
                Number(p, "Largeur (part de l’image)", visual.Width, .01, 1, v => visual.Width = v); Number(p, "Hauteur (part de l’image)", visual.Height, .01, 1, v => visual.Height = v);
                Check(p, "Remplir le cadre en rognant", visual.Crop, b => visual.Crop = b);
                var row = Row(); p.Children.Add(row);
                void Move(int delta) { int at = profile.Visuals.IndexOf(visual), next = Math.Clamp(at + delta, 0, profile.Visuals.Count - 1); profile.Visuals.RemoveAt(at); profile.Visuals.Insert(next, visual); Save(); Build(); }
                row.Children.Add(Button("↑", () => Move(-1))); row.Children.Add(Button("↓", () => Move(1))); row.Children.Add(Button("Supprimer", () => { profile.Visuals.Remove(visual); Save(); Build(); })); panel.Children.Add(Card(p));
            }
        }
        Build(); ShowEditor("Composition de l’enregistrement", panel);
    }
}
