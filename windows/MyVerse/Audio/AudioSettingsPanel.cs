using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les sections audio des Paramètres (MyChurch 3.0.1) : périphériques et moteur OBS, MyVirtualSound (installation de VB-CABLE, sur clic),
/// enregistrements (dossier, ffmpeg), code source et licences (exigé par la licence GNU GPL de libobs et de FFmpeg).
/// </summary>
public sealed class AudioSettingsPanel : StackPanel
{
    public const string DepotMyChurch = "https://github.com/capmaster2023/Verselight";
    public const string DepotObs = "https://github.com/obsproject/obs-studio/tree/33.0.1";
    public const string SiteVbCable = "https://www.vb-cable.com";
    public const string SourcesFfmpeg = "https://ffmpeg.org/releases/";
    const string LicenceGplEnLigne = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";

    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly TextBlock cable = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock progression = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button installer = new() { Content = "Installer VB-CABLE", Height = 40, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };

    Window? Fenetre => Window.GetWindow(this);

    public AudioSettingsPanel()
    {
        Children.Add(SectionPeripheriques());
        Children.Add(SectionVirtualSound());
        Children.Add(SectionEnregistrement());
        Children.Add(SectionCodeSource());
    }

    // ------------------------------------------------------------ petits outils de mise en page
    static (Border Cadre, StackPanel Corps) Section(string titre, string? sousTitre = null)
    {
        var corps = new StackPanel();
        corps.Children.Add(new TextBlock { Text = titre, Style = S("SousTitre"), Margin = new Thickness(0) });
        if (sousTitre != null)
            corps.Children.Add(new TextBlock { Text = sousTitre, FontSize = 13, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return (new Border { Style = S("Section"), Child = corps }, corps);
    }

    static FrameworkElement Champ(string libelle, UIElement controle)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = libelle, FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 0, 0, 6) });
        p.Children.Add(controle);
        return p;
    }

    /// <summary>Une grille de deux colonnes égales, comme la maquette des Paramètres.</summary>
    static Grid DeuxColonnes(params FrameworkElement[] elements)
    {
        var g = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int lignes = (elements.Length + 1) / 2;
        for (int r = 0; r < lignes; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < elements.Length; i++)
        {
            var e = elements[i];
            Grid.SetRow(e, i / 2);
            Grid.SetColumn(e, i % 2 * 2);
            if (i / 2 < lignes - 1) e.Margin = new Thickness(0, 0, 0, 14);
            g.Children.Add(e);
        }
        return g;
    }

    static TextBlock Note(string texte, double haut = 10) => new() { Text = texte, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0) };

    /// <summary>Liste de périphériques : « Périphérique Windows par défaut » puis ceux trouvés ; le choix enregistré reste visible même débranché.</summary>
    static ComboBox Liste(List<AudioDevice> appareils, string choisi, string nomChoisi, Action<string, string> changer)
    {
        var cb = new ComboBox { MinHeight = 40 };
        cb.Items.Add(new ComboBoxItem { Content = "Périphérique Windows par défaut", Tag = "default" });
        foreach (var d in appareils) cb.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        if (choisi != "default" && choisi.Length > 0 && appareils.All(d => d.Id != choisi))
            cb.Items.Add(new ComboBoxItem { Content = (nomChoisi.Length > 0 ? nomChoisi : choisi) + " (absent)", Tag = choisi });
        cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? cb.Items[0];
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not ComboBoxItem { Tag: string id } item) return;
            string nom = id == "default" ? "" : (item.Content as string ?? "").Replace(" (absent)", "");
            changer(id, nom);
        };
        return cb;
    }

    static void Ouvrir(string cible)
    {
        try { Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true }); }
        catch (Exception e) { Journal.Erreur("ouverture de " + cible, e); }
    }

    /// <summary>Un lien cliquable (ouvre le navigateur).</summary>
    static TextBlock Lien(string texte, string adresse, double taille = 13)
    {
        var h = new Hyperlink(new Run(texte)) { Foreground = P("Or"), Cursor = System.Windows.Input.Cursors.Hand };
        h.Click += (_, _) => Ouvrir(adresse);
        var t = new TextBlock { FontSize = taille, TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(h);
        return t;
    }

    // ------------------------------------------------------------ périphériques audio et moteur OBS
    Border SectionPeripheriques()
    {
        string etat = A.Running
            ? $"Moteur OBS actif · libobs {A.Version}" + (A.TracksActive ? "" : " · pistes indisponibles sur cet ordinateur (carte graphique)")
            : A.Diagnostic;
        var (cadre, corps) = Section("Périphériques audio", etat);
        var micro = Liste(A.Microphones(), C.MicrophoneId, C.MicrophoneName, (id, nom) => { C.MicrophoneId = id; C.MicrophoneName = nom; A.Apply(); });
        var sortie = Liste(A.Outputs(), C.OutputId, C.OutputName, (id, nom) => { C.OutputId = id; C.OutputName = nom; A.Apply(); });
        var casque = Liste(A.MonitoringDevices(), C.MonitorDeviceId, C.MonitorDeviceName, (id, nom) => { C.MonitorDeviceId = id; C.MonitorDeviceName = nom; A.Apply(); });
        var format = new TextBlock { Text = "48 kHz · stéréo · 6 mélanges", FontSize = 14, Height = 40, Padding = new Thickness(0, 10, 0, 0) };
        corps.Children.Add(DeuxColonnes(
            Champ("Microphone par défaut", micro),
            Champ("Sortie audio capturée", sortie),
            Champ("Écoute au casque", casque),
            Champ("Format du moteur", format)));
        corps.Children.Add(Note("Le micro et la sortie par défaut sont proposés en premier dans « + Source audio ». L'écoute au casque fait entendre les sources dont le casque est allumé dans le mélangeur."));
        return cadre;
    }

    // ------------------------------------------------------------ MyVirtualSound
    readonly StackPanel sortiesListe = new() { Margin = new Thickness(0, 12, 0, 0) };

    Border SectionVirtualSound()
    {
        var (cadre, corps) = Section("MyVirtualSound", "Des sorties qui envoient chacune le mélange de leurs sources vers un câble audio virtuel : Zoom, Teams, OBS ou un navigateur les reçoivent comme un micro. Elles se créent dans le panneau Audio de Display (« + Créer une sortie »), 4 au plus, actives en même temps si besoin.");
        corps.Children.Add(sortiesListe);

        var detection = new Border { Background = P("Nuit2"), BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 14, 0, 0) };
        var pile = new StackPanel();
        pile.Children.Add(cable);
        installer.Style = S("BtnOr");
        installer.ToolTip = "Télécharge le paquet officiel sur vb-audio.com puis lance son installateur (Windows demande l'accord de l'administrateur)";
        installer.Click += async (_, _) =>
        {
            installer.IsEnabled = false;
            progression.Visibility = Visibility.Visible;
            var etat = new Progress<string>(t => progression.Text = t);
            try { await VbCable.InstallerAsync(etat); }
            catch (Exception e) { progression.Text = "Installation impossible : " + e.Message; Journal.Erreur("installation de VB-CABLE", e); }
            finally
            {
                installer.IsEnabled = true;
                A.Sorties.Reset();
                A.Apply();
                MajCable();
                MajSorties();
            }
        };
        pile.Children.Add(installer);
        pile.Children.Add(progression);
        detection.Child = pile;
        corps.Children.Add(detection);

        corps.Children.Add(new TextBlock { Text = "Dans Zoom, choisissez « CABLE Output » comme microphone.", FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        corps.Children.Add(Note(VbCable.Mention, 8));
        var site = Lien("www.vb-cable.com", SiteVbCable, 12.5);
        site.Margin = new Thickness(0, 2, 0, 0);
        corps.Children.Add(site);
        MajCable();
        MajSorties();
        return cadre;
    }

    void MajCable()
    {
        var trouve = VbCable.Entree();
        cable.Text = trouve is { } c ? "Câble détecté : " + c.Nom : "Aucun câble audio virtuel détecté (VB-CABLE, Virtual Audio Driver ou Virtual Cables).";
        installer.Visibility = VbCable.Installe ? Visibility.Collapsed : Visibility.Visible;
    }

    void MajSorties()
    {
        sortiesListe.Children.Clear();
        if (C.VirtualSounds.Count == 0)
        {
            sortiesListe.Children.Add(Note("Aucune sortie pour l'instant : « + Créer une sortie » dans le panneau Audio.", 0));
            return;
        }
        for (int i = 0; i < C.VirtualSounds.Count; i++)
        {
            var v = C.VirtualSounds[i];
            var t = new TextBlock { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            t.Inlines.Add(new Bold(new Run(AudioPanel.MarqueSortie(i) + " · " + v.Name)));
            t.Inlines.Add(new Run(" : " + (v.Enabled ? A.Sorties.Status(v.Id) : "désactivée")) { Foreground = P("Brume") });
            sortiesListe.Children.Add(t);
        }
    }

    // ------------------------------------------------------------ enregistrements
    Border SectionEnregistrement()
    {
        var (cadre, corps) = Section("Enregistrements", "Ils se créent dans le panneau Audio de Display (« + Créer un enregistrement ») : chacun garde les sources audio choisies et, avec des sources Display, fait une vidéo MKV. 5 au plus, qui peuvent tourner en même temps.");

        var dossier = new TextBox { Style = S("Champ"), Text = AudioConfiguration.DossierEnregistrements, IsReadOnly = true, FontSize = 14 };
        var ouvrir = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        ouvrir.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(AudioConfiguration.DossierEnregistrements); } catch (Exception) { }
            Ouvrir(AudioConfiguration.DossierEnregistrements);
        };
        var ligneDossier = new DockPanel();
        DockPanel.SetDock(ouvrir, Dock.Right);
        ligneDossier.Children.Add(ouvrir);
        ligneDossier.Children.Add(dossier);
        var champDossier = Champ("Dossier par défaut (un sous-dossier par enregistrement)", ligneDossier);
        champDossier.Margin = new Thickness(0, 14, 0, 0);
        corps.Children.Add(champDossier);

        // ffmpeg : livré avec MyChurch (FLAC et vidéo) ; un autre ffmpeg.exe peut le remplacer
        var etat = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        var autre = new Button { Style = S("Btn"), Content = "Choisir un autre ffmpeg.exe…", Height = 38, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5 };
        var inclus = new Button { Style = S("BtnLien"), Content = "Revenir à celui de MyChurch", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void MajFfmpeg()
        {
            bool present = File.Exists(ChurchAudioService.FfmpegInclus);
            if (C.FfmpegPath.Length > 0) etat.Text = "ffmpeg utilisé : " + C.FfmpegPath + (File.Exists(C.FfmpegPath) ? "" : " (introuvable : celui de MyChurch le remplace)");
            else etat.Text = present ? "ffmpeg 8.0.1 est inclus avec MyChurch : FLAC et vidéo MKV marchent sans rien installer." : "ffmpeg est absent du dossier de MyChurch : réinstallez MyChurch, ou choisissez un ffmpeg.exe.";
            etat.Foreground = present || File.Exists(C.FfmpegPath) ? P("Blanc") : P("Danger");
            inclus.Visibility = C.FfmpegPath.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        autre.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "ffmpeg.exe (FLAC et vidéo)", Filter = "ffmpeg.exe|ffmpeg.exe|Programmes|*.exe" };
            bool? ok = Fenetre != null ? d.ShowDialog(Fenetre) : d.ShowDialog();
            if (ok != true) return;
            C.FfmpegPath = d.FileName;
            A.Apply();
            MajFfmpeg();
        };
        inclus.Click += (_, _) => { C.FfmpegPath = ""; A.Apply(); MajFfmpeg(); };
        var cadreFfmpeg = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        cadreFfmpeg.Children.Add(etat);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        boutons.Children.Add(autre);
        boutons.Children.Add(inclus);
        cadreFfmpeg.Children.Add(boutons);
        corps.Children.Add(cadreFfmpeg);
        corps.Children.Add(Note("La vidéo utilise l'encodeur H.264 de la carte graphique (NVIDIA, Intel ou AMD) s'il marche, sinon x264. Le son et l'image suivent la même horloge ; un décalage de caméra se corrige dans chaque enregistrement (« Synchro du son »)."));
        MajFfmpeg();
        return cadre;
    }

    // ------------------------------------------------------------ code source et licences (GPL)
    Border SectionCodeSource()
    {
        var (cadre, corps) = Section("Code source et licences");
        var texte = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) };
        texte.Inlines.Add(new Run("MyChurch est un logiciel libre. Son moteur audio utilise "));
        texte.Inlines.Add(new Bold(new Run("libobs")));
        texte.Inlines.Add(new Run(", la bibliothèque d'OBS Studio 33.0.1, distribuée sous licence GNU GPL version 2 ou ultérieure. Tout le code source de MyChurch est à votre disposition."));
        corps.Children.Add(texte);
        corps.Children.Add(LigneDepot("Code source de MyChurch", DepotMyChurch));
        corps.Children.Add(LigneDepot("Code source d'OBS Studio 33.0.1 (libobs)", DepotObs));
        corps.Children.Add(LigneDepot("Code source de FFmpeg (ffmpeg.exe 8.0.1, build gyan.dev)", SourcesFfmpeg));

        var licence = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var lire = new Button { Style = S("BtnContour"), Content = "Lire la licence GNU GPL", Height = 36, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5 };
        lire.Click += (_, _) =>
        {
            string copie = Path.Combine(AppContext.BaseDirectory, "COPYING.txt");
            Ouvrir(File.Exists(copie) ? copie : LicenceGplEnLigne);
        };
        licence.Children.Add(lire);
        licence.Children.Add(new TextBlock { Text = "Le texte complet de la licence est installé avec MyChurch (COPYING.txt).", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        corps.Children.Add(licence);
        corps.Children.Add(Note("FFmpeg (www.ffmpeg.org), livré dans le dossier « ffmpeg » de MyChurch pour le FLAC et la vidéo, est un programme libre sous licence GNU GPL version 3 : son texte est dans ffmpeg\\LICENSE.txt. Build « essentials » de gyan.dev (www.gyan.dev/ffmpeg/builds).", 14));
        corps.Children.Add(Note(VbCable.Mention + " Il n'est pas inclus dans MyChurch : il s'installe à la demande depuis le site officiel.", 8));
        corps.Children.Add(Note("Polices Inter, Space Grotesk, Literata, Atkinson Hyperlegible Next et Unbounded : licence SIL Open Font License 1.1.", 6));
        return cadre;
    }

    static Border LigneDepot(string titre, string adresse)
    {
        var g = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ouvrir = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
        ouvrir.Click += (_, _) => Ouvrir(adresse);
        var copier = new Button { Style = S("Btn"), Content = "Copier le lien", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13, Margin = new Thickness(8, 0, 0, 0) };
        copier.Click += async (_, _) =>
        {
            try { Clipboard.SetText(adresse); } catch (Exception) { return; }
            copier.Content = "Lien copié";
            await Task.Delay(1600);
            copier.Content = "Copier le lien";
        };
        boutons.Children.Add(ouvrir);
        boutons.Children.Add(copier);
        DockPanel.SetDock(boutons, Dock.Right);
        g.Children.Add(boutons);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold });
        var lien = Lien(adresse, adresse);
        lien.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(lien);
        g.Children.Add(textes);
        return new Border { Child = g, BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
    }
}
