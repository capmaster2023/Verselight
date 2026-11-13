using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MyVerse.Audio;
using NAudio.CoreAudioApi;

namespace MyVerse.Ui.Audio;

/// <summary>Les sorties Windows (identifiants NAudio, pas ceux d'OBS) : où une sortie MyVirtualSound peut jouer son mélange.</summary>
internal static class SortiesWindows
{
    public static List<AudioDevice> Lister()
    {
        var liste = new List<AudioDevice>();
        try
        {
            using var enumerateur = new MMDeviceEnumerator();
            foreach (var d in enumerateur.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (d) liste.Add(new AudioDevice(d.ID, d.FriendlyName));
        }
        catch (Exception e) { Journal.Erreur("liste des sorties Windows", e); }
        return liste;
    }
}

/// <summary>Le cadre commun des fenêtres « enregistrement » et « sortie MyVirtualSound » : titre, aide, zone qui défile, boutons en bas.</summary>
public abstract class FenetreDestination : FenetreDialogue
{
    protected static ChurchAudioService A => App.Audio;
    protected static AudioConfiguration C => App.Audio.Config;
    protected readonly StackPanel Corps = new();
    protected readonly Button Valider, Supprimer;
    readonly TextBlock erreur;

    protected FenetreDestination(string titre, string aide, bool existe, string ok)
    {
        Title = titre;
        Width = 680;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        Background = Pinceau("Page");
        var racine = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = titre, Style = Style_("Titre2"), TextTrimming = TextTrimming.CharacterEllipsis });
        tete.Children.Add(new TextBlock { Text = aide, Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6), LineHeight = 21 });
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        var pied = new StackPanel();
        erreur = new TextBlock { Foreground = Pinceau("Danger"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        pied.Children.Add(erreur);
        var bas = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        Supprimer = new Button { Style = Style_("BtnDanger"), Content = "Supprimer", Height = 40, Padding = new Thickness(16, 0, 16, 0), Visibility = existe ? Visibility.Visible : Visibility.Collapsed };
        DockPanel.SetDock(Supprimer, Dock.Left);
        bas.Children.Add(Supprimer);
        Valider = new Button { Style = Style_("BtnOr"), Content = ok, IsDefault = true, MinWidth = 130, Height = 40, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(Valider, Dock.Right);
        bas.Children.Add(Valider);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 40 };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        pied.Children.Add(bas);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = Corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
        Content = racine;
    }

    protected static TextBlock Libelle(string texte, double haut = 20) => new() { Text = texte, Style = Style_("CarteTitre"), Margin = new Thickness(0, haut, 0, 8) };

    protected static TextBlock Note(string texte, double haut = 6) => new() { Text = texte, FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0), LineHeight = 18 };

    /// <summary>Une case à cocher sur deux lignes : le nom, puis une précision en gris.</summary>
    protected static CheckBox Case(string titre, string detail, bool coche, UIElement? avant = null)
    {
        var textes = new StackPanel { MaxWidth = 250 };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = titre });
        if (detail.Length > 0) textes.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        object contenu = textes;
        if (avant != null)
        {
            var d = new DockPanel();
            d.Children.Add(avant);
            d.Children.Add(textes);
            contenu = d;
        }
        return new CheckBox { Content = contenu, IsChecked = coche, Margin = new Thickness(0, 0, 12, 10), HorizontalAlignment = HorizontalAlignment.Left };
    }

    /// <summary>« Piste audio », comme dans les paramètres de sortie d'OBS : piste 1 à 6, et les sources qui vont dans la piste choisie.</summary>
    protected ComboBox ChoixPiste(int actuelle)
    {
        var cb = new ComboBox { MinHeight = 40 };
        for (int t = 1; t <= AudioConfiguration.Pistes; t++) cb.Items.Add(new ComboBoxItem { Content = $"Piste {t}", Tag = t });
        cb.SelectedIndex = Math.Clamp(actuelle, 1, AudioConfiguration.Pistes) - 1;
        Corps.Children.Add(cb);
        var note = Note("", 8);
        void Maj() => note.Text = AudioPanel.SourcesPiste(cb.SelectedIndex + 1) + ". Les pistes de chaque source se cochent dans les propriétés audio avancées (⚙ du mélangeur), comme dans OBS.";
        cb.SelectionChanged += (_, _) => Maj();
        Maj();
        Corps.Children.Add(note);
        return cb;
    }

    protected void Erreur(string message)
    {
        erreur.Text = message;
        erreur.Visibility = Visibility.Visible;
    }

    protected void EffacerErreur() => erreur.Visibility = Visibility.Collapsed;
}

/// <summary>
/// Créer ou modifier un enregistrement : son nom, sa piste audio (comme dans OBS), ses sources Display
/// (la vidéo, dans l'ordre : la 1re en grand), la disposition, la synchro du son et le dossier.
/// </summary>
public sealed class EnregistrementWindow : FenetreDestination
{
    readonly RecordingProfile? existant;
    readonly TextBox nom;
    readonly ComboBox piste;
    readonly List<(CheckBox Case, string Id, Border Pastille, TextBlock Rang)> display = new();
    readonly List<string> ordre = new();
    readonly StackPanel optionsVideo = new(), optionsSon = new(), disposition = new();
    readonly RadioButton incrustation, cote, wav, flac;
    readonly Slider synchro;
    readonly TextBlock synchroTexte = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, MinWidth = 170, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox dossier;
    bool dossierChoisi;

    public EnregistrementWindow(RecordingProfile? p) : base(
        p == null ? "Créer un enregistrement" : $"Modifier « {p.Name} »",
        "Comme dans OBS, un enregistrement garde une piste audio (1 à 6) ; avec des sources Display, c'est une vidéo. Jusqu'à 5 enregistrements, qui peuvent tourner en même temps.",
        p != null, p == null ? "Créer" : "Enregistrer")
    {
        existant = p;
        bool tourne = p != null && (A.Recorder.Active(p.Id) || A.Recorder.Busy(p.Id));
        if (tourne)
        {
            var info = Note("Cet enregistrement tourne : la piste audio change tout de suite ; la vidéo, le format et le dossier au prochain démarrage.", 0);
            info.Foreground = Pinceau("Or");
            info.FontWeight = FontWeights.SemiBold;
            Corps.Children.Add(info);
        }

        Corps.Children.Add(Libelle("NOM", tourne ? 14 : 6));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = p?.Name ?? $"Enregistrement {C.Profiles.Count + 1}" };
        Corps.Children.Add(nom);

        Corps.Children.Add(Libelle("PISTE AUDIO"));
        piste = ChoixPiste(p?.Track ?? 1);

        Corps.Children.Add(Libelle("SOURCES DISPLAY FILMÉES (FACULTATIF)"));
        Corps.Children.Add(Note("Sans source Display, le fichier garde le son seul. Avec, c'est une vidéo MKV : cochez dans l'ordre, la 1re est en grand.", 0));
        var liste = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        AjouterDisplay(liste, RecordingProfile.Projection, "La projection", "Ce qui est à l'écran : Bible, chants, Display, Duo");
        foreach (var a in App.Donnees.Annonces)
            AjouterDisplay(liste, a.Id, a.Titre.Length > 0 ? a.Titre : "Sans titre", a.Description.Split('\n')[0].Trim());
        Corps.Children.Add(liste);
        if (p != null) ordre.AddRange(p.DisplayIds.Where(id => display.Any(d => d.Id == id)));

        // vidéo : disposition et synchro du son
        disposition.Children.Add(Libelle("DISPOSITION", 10));
        incrustation = new RadioButton { Content = "Incrustation : la 1re en grand, les autres en petit en bas à droite", GroupName = "disposition", IsChecked = p?.Layout != "cote", Margin = new Thickness(0, 0, 0, 8) };
        cote = new RadioButton { Content = "Côte à côte : toutes de la même taille", GroupName = "disposition", IsChecked = p?.Layout == "cote" };
        disposition.Children.Add(incrustation);
        disposition.Children.Add(cote);
        optionsVideo.Children.Add(disposition);
        optionsVideo.Children.Add(Libelle("SYNCHRO DU SON"));
        var ligne = new DockPanel();
        DockPanel.SetDock(synchroTexte, Dock.Right);
        ligne.Children.Add(synchroTexte);
        synchro = new Slider { Minimum = -500, Maximum = 1000, SmallChange = 10, LargeChange = 50, TickFrequency = 10, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Value = p?.AudioDelayMs ?? 0, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Double-clic : 0 ms" };
        synchro.ValueChanged += (_, _) => MajSynchro();
        synchro.MouseDoubleClick += (_, _) => synchro.Value = 0;
        ligne.Children.Add(synchro);
        optionsVideo.Children.Add(ligne);
        optionsVideo.Children.Add(Note("Le son et l'image suivent la même horloge, sans dérive. Si la caméra a un peu de retard sur le son (souvent 50 à 200 ms), retardez le son (+) jusqu'à ce que la voix et les lèvres coïncident ; si c'est l'inverse, retardez l'image (−)."));
        optionsVideo.Children.Add(Note("Vidéo 1280 × 720, 30 images par seconde, H.264 (carte graphique si possible), fichier MKV lisible même après une coupure de courant."));
        Corps.Children.Add(optionsVideo);

        // son seul : le format
        optionsSon.Children.Add(Libelle("FORMAT"));
        wav = new RadioButton { Content = "WAV : 24 bits, sans perte", GroupName = "format", IsChecked = p?.Format != "flac", Margin = new Thickness(0, 0, 0, 8) };
        flac = new RadioButton { Content = "FLAC : compressé, sans perte", GroupName = "format", IsChecked = p?.Format == "flac" };
        optionsSon.Children.Add(wav);
        optionsSon.Children.Add(flac);
        Corps.Children.Add(optionsSon);

        Corps.Children.Add(Libelle("DOSSIER"));
        dossier = new TextBox { Style = Style_("Champ"), IsReadOnly = true, FontSize = 14, Text = p?.Directory ?? "" };
        var choisir = new Button { Style = Style_("Btn"), Content = "Choisir…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        choisir.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier de cet enregistrement" };
            if (Directory.Exists(dossier.Text)) d.InitialDirectory = dossier.Text;
            if (d.ShowDialog(this) != true) return;
            dossier.Text = d.FolderName;
            dossierChoisi = true;
        };
        var ligneDossier = new DockPanel();
        DockPanel.SetDock(choisir, Dock.Right);
        ligneDossier.Children.Add(choisir);
        ligneDossier.Children.Add(dossier);
        Corps.Children.Add(ligneDossier);
        Corps.Children.Add(Note("Chaque fichier porte le nom de l'enregistrement, la date et l'heure ; rien n'est jamais écrasé."));

        dossierChoisi = p != null && !string.Equals(p.Directory, DossierParDefaut(p.Name), StringComparison.OrdinalIgnoreCase);
        if (!dossierChoisi) dossier.Text = DossierParDefaut(nom.Text);
        nom.TextChanged += (_, _) => { if (!dossierChoisi) dossier.Text = DossierParDefaut(nom.Text); };

        Valider.Click += (_, _) => Enregistrer();
        Supprimer.IsEnabled = !tourne;
        if (tourne) { Supprimer.ToolTip = "Arrêtez d'abord cet enregistrement"; ToolTipService.SetShowOnDisabled(Supprimer, true); }
        Supprimer.Click += (_, _) =>
        {
            if (existant == null || !Dialogue.Confirmer(this, $"Supprimer l'enregistrement « {existant.Name} » ? Les fichiers déjà enregistrés restent dans leur dossier.", "Supprimer")) return;
            C.Profiles.Remove(existant);
            A.Apply();
            Close();
        };
        Loaded += (_, _) => { nom.Focus(); nom.SelectAll(); };
        MajOptions();
        MajSynchro();
    }

    static string DossierParDefaut(string nom) =>
        Path.Combine(AudioConfiguration.DossierEnregistrements, RecordingFiles.SafeName(nom.Trim().Length > 0 ? nom.Trim() : "Enregistrement"));

    void AjouterDisplay(UniformGrid liste, string id, string titre, string detail)
    {
        var rang = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("SurOr"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pastille = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Pinceau("OrVif"), Margin = new Thickness(0, 0, 8, 0), Child = rang, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var cb = Case(titre, detail, existant?.DisplayIds.Contains(id) ?? false, pastille);
        cb.Click += (_, _) =>
        {
            if (cb.IsChecked == true)
            {
                if (ordre.Count >= 6) { cb.IsChecked = false; Erreur("6 sources Display au plus dans une vidéo."); return; }
                ordre.Add(id);
            }
            else ordre.Remove(id);
            MajOptions();
        };
        display.Add((cb, id, pastille, rang));
        liste.Children.Add(cb);
    }

    void MajOptions()
    {
        foreach (var d in display)
        {
            int i = ordre.IndexOf(d.Id);
            d.Pastille.Visibility = i >= 0 ? Visibility.Visible : Visibility.Collapsed;
            d.Rang.Text = (i + 1).ToString();
        }
        bool video = ordre.Count > 0;
        optionsVideo.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        disposition.Visibility = ordre.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        optionsSon.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        EffacerErreur();
    }

    void MajSynchro()
    {
        int ms = (int)Math.Round(synchro.Value);
        synchroTexte.Text = ms == 0 ? "0 ms" : ms > 0 ? $"+{ms} ms (son retardé)" : $"−{-ms} ms (image retardée)";
    }

    void Enregistrer()
    {
        string n = nom.Text.Trim();
        if (n.Length == 0) { Erreur("Donnez un nom à l'enregistrement."); nom.Focus(); return; }
        if (existant == null && C.Profiles.Count >= AudioConfiguration.MaxEnregistrements) { Erreur($"{AudioConfiguration.MaxEnregistrements} enregistrements au plus."); return; }
        if (C.Profiles.Any(x => x != existant && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase))) { Erreur($"Un autre enregistrement s'appelle déjà « {n} »."); return; }
        var p = existant ?? new RecordingProfile();
        p.Name = n;
        p.Track = piste.SelectedIndex + 1;
        p.DisplayIds = ordre.ToList();
        p.Layout = cote.IsChecked == true ? "cote" : "incrustation";
        p.AudioDelayMs = (int)Math.Round(synchro.Value);
        p.AudioOnly = ordre.Count == 0;
        p.Format = ordre.Count > 0 ? "mkv" : flac.IsChecked == true ? "flac" : "wav";
        p.Directory = dossier.Text;
        if (existant == null) C.Profiles.Add(p);
        A.Apply();
        Close();
    }
}

/// <summary>
/// Créer ou modifier une sortie MyVirtualSound : son nom, le câble virtuel (ou la sortie) où elle joue et sa piste audio, comme dans OBS.
/// Plusieurs sorties peuvent être actives en même temps.
/// </summary>
public sealed class SortieWindow : FenetreDestination
{
    readonly VirtualSoundSettings? existant;
    readonly TextBox nom;
    readonly ComboBox peripherique = new() { MinHeight = 40 };
    readonly ComboBox piste;
    readonly CheckBox activer;

    public SortieWindow(VirtualSoundSettings? v) : base(
        v == null ? "Créer une sortie MyVirtualSound" : $"Modifier « {v.Name} »",
        "Une piste audio (1 à 6), envoyée vers un câble audio virtuel : Zoom, Teams, OBS ou un navigateur la reçoivent comme un micro. Plusieurs sorties peuvent être actives en même temps.",
        v != null, v == null ? "Créer" : "Enregistrer")
    {
        existant = v;
        Corps.Children.Add(Libelle("NOM", 6));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = v?.Name ?? (C.VirtualSounds.Count == 0 ? "Zoom" : "Sortie " + AudioPanel.MarqueSortie(C.VirtualSounds.Count)) };
        Corps.Children.Add(nom);

        Corps.Children.Add(Libelle("SORTIE"));
        var cable = VbCable.Entree();
        peripherique.Items.Add(new ComboBoxItem { Content = "Automatique : " + (cable is { } c ? c.Nom : "câble virtuel installé"), Tag = "" });
        peripherique.Items.Add(new ComboBoxItem { Content = "Sortie Windows par défaut", Tag = "default" });
        var liste = SortiesWindows.Lister();
        foreach (var d in liste) peripherique.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        string choisi = v?.DeviceId ?? "";
        if (choisi.Length > 0 && choisi != "default" && liste.All(d => d.Id != choisi))
            peripherique.Items.Add(new ComboBoxItem { Content = (v!.DeviceName.Length > 0 ? v.DeviceName : "Sortie enregistrée") + " (absente)", Tag = choisi });
        peripherique.SelectedItem = peripherique.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? peripherique.Items[0];
        Corps.Children.Add(peripherique);
        Corps.Children.Add(Note(cable is { } trouve
            ? $"Câble détecté : {trouve.Nom}. Dans Zoom, choisissez « CABLE Output » comme microphone."
            : "Aucun câble audio virtuel détecté : installez VB-CABLE dans Réglages › MyVirtualSound (sur clic), ou choisissez une sortie."));

        Corps.Children.Add(Libelle("PISTE AUDIO"));
        piste = ChoixPiste(v?.Track ?? 1);

        activer = new CheckBox { Content = "Activer cette sortie", FontSize = 14.5, FontWeight = FontWeights.SemiBold, IsChecked = v?.Enabled ?? true, Margin = new Thickness(0, 18, 0, 0) };
        Corps.Children.Add(activer);
        Corps.Children.Add(Note(VbCable.Mention, 16));

        Valider.Click += (_, _) => Enregistrer();
        Supprimer.Click += (_, _) =>
        {
            if (existant == null || !Dialogue.Confirmer(this, $"Supprimer la sortie MyVirtualSound « {existant.Name} » ?", "Supprimer")) return;
            C.VirtualSounds.Remove(existant);
            A.Apply();
            Close();
        };
        Loaded += (_, _) => { nom.Focus(); nom.SelectAll(); };
    }

    void Enregistrer()
    {
        string n = nom.Text.Trim();
        if (n.Length == 0) { Erreur("Donnez un nom à la sortie (par exemple « Zoom »)."); nom.Focus(); return; }
        if (existant == null && C.VirtualSounds.Count >= AudioConfiguration.MaxSortiesVirtuelles) { Erreur($"{AudioConfiguration.MaxSortiesVirtuelles} sorties au plus."); return; }
        var v = existant ?? new VirtualSoundSettings();
        v.Name = n;
        v.Track = piste.SelectedIndex + 1;
        v.Enabled = activer.IsChecked == true;
        if (peripherique.SelectedItem is ComboBoxItem { Tag: string id } item)
        {
            v.DeviceId = id;
            v.DeviceName = id.Length == 0 || id == "default" ? "" : (item.Content as string ?? "").Replace(" (absente)", "");
        }
        if (existant == null) C.VirtualSounds.Add(v);
        A.Apply();
        Close();
    }
}
