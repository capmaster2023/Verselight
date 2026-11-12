using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Duo : deux contenus en même temps à l'écran (comme deux sources superposées dans OBS).
/// On épingle ce qui est à l'écran : il part dans un coin (ou côte à côte) et l'écran principal se libère pour autre chose.
/// </summary>
public partial class MainWindow
{
    /// <summary>Le contenu épinglé et l'écran principal montrent la même source vivante : une seule capture sert aux deux.</summary>
    bool duoMemeSource;
    bool majDuo;

    void InitDuo()
    {
        var reg = App.Donnees.Reglages;
        majDuo = true;
        DuoPlace.SelectedItem = DuoPlace.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == reg.DuoPlace) ?? DuoPlace.Items[0];
        DuoTaille.Value = Math.Clamp(Math.Round(reg.DuoPart * 100), DuoTaille.Minimum, DuoTaille.Maximum);
        majDuo = false;
        DuoEpingler.Click += (_, _) => Epingler();
        DuoRetirer.Click += (_, _) => Desepingler();
        DuoPlace.SelectionChanged += (_, _) => PlacerDuo();
        DuoTaille.ValueChanged += (_, _) => PlacerDuo();
        App.SourcesDuo.Change += () => { if (!duoMemeSource) ImagesDuo(); };
        MajDuo();
    }

    /// <summary>Ce qui part à l'écran : l'état de la régie, ou rien du tout (ni texte ni Duo) pendant l'écran noir.</summary>
    EtatEcran EtatProjete() =>
        noir ? R.Etat with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null, Duo = null } : R.Etat;

    void Epingler(string? libelle = null)
    {
        if (R.Epingler(libelle) && R.Etat.Duo is { } d) Toast($"Épinglé : « {d.Libelle} ». Affichez maintenant autre chose : les deux seront à l'écran.");
    }

    void Desepingler()
    {
        if (R.Etat.Duo == null) return;
        R.Desepingler();
        Toast("Duo retiré.");
    }

    /// <summary>Touche P : épingle ce qui est à l'écran, ou retire le Duo s'il y en a un.</summary>
    void BasculerDuo()
    {
        if (R.Etat.Duo != null) Desepingler(); else Epingler();
    }

    /// <summary>Menu des sources Display : la source part à l'écran, puis dans le Duo.</summary>
    void EpinglerDisplay(Annonce a)
    {
        R.AfficherAnnonce(a);
        if (!R.Etat.Vide) Epingler(a.Titre);
    }

    void PlacerDuo()
    {
        if (majDuo || chargement) return;
        if (DuoPlace.SelectedItem is ComboBoxItem i) R.PlaceDuo((string)i.Tag, DuoTaille.Value / 100);
        MajDuo();
    }

    /// <summary>La carte « DUO » suit la régie (bouton, téléphone, raccourci).</summary>
    void MajDuo()
    {
        if (DuoLibelle == null) return;
        var d = R.Etat.Duo;
        var reg = App.Donnees.Reglages;
        majDuo = true;
        try
        {
            string place = d?.Place ?? reg.DuoPlace;
            double part = d?.Part ?? reg.DuoPart;
            if (DuoPlace.SelectedItem is not ComboBoxItem c || (string)c.Tag != place)
                DuoPlace.SelectedItem = DuoPlace.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == place) ?? DuoPlace.Items[0];
            double v = Math.Clamp(Math.Round(part * 100), DuoTaille.Minimum, DuoTaille.Maximum);
            if (Math.Abs(DuoTaille.Value - v) > 0.5) DuoTaille.Value = v;
            bool cote = place == "cote";
            DuoTaille.IsEnabled = !cote;
            DuoTailleLab.Text = cote ? "Taille : deux moitiés égales" : $"Taille du contenu épinglé : {DuoTaille.Value:0} %";
        }
        finally { majDuo = false; }
        DuoBoite.Visibility = d != null ? Visibility.Visible : Visibility.Collapsed;
        DuoAide.Visibility = d != null ? Visibility.Collapsed : Visibility.Visible;
        DuoLibelle.Text = d?.Libelle ?? "";
        DuoEtat.Text = d == null ? "rien d'épinglé" : noir ? "caché (écran noir)" : "à l'écran";
        DuoEtat.Foreground = Pinceau(d != null && !noir ? "Direct" : "Brume");
        DuoEpingler.Content = d == null ? "Épingler le contenu en direct" : "Remplacer le contenu épinglé";
        DuoEpingler.IsEnabled = !R.Etat.Vide;
        DuoRetirer.IsEnabled = d != null;
        DuoRetirer.Visibility = d != null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ image vivante du contenu épinglé
    /// <summary>Lance (ou arrête) la 2e capture : celle du contenu épinglé, si c'est une image, une caméra ou une fenêtre.</summary>
    void ChoisirSourceDuo(EtatEcran e)
    {
        var s = e.Duo?.Etat is { Mode: "media", Media: { } m } ? m : null;
        // la même caméra ne peut pas être ouverte deux fois : la capture de l'écran principal sert aussi au Duo
        duoMemeSource = s != null && e.Mode == "media" && e.Media == s;
        App.SourcesDuo.ChoisirSource(duoMemeSource ? null : s);
    }

    /// <summary>Envoie l'image vivante du contenu épinglé à l'aperçu, à la projection et à MyVirtualCam.</summary>
    void ImagesDuo()
    {
        var b = duoMemeSource ? App.Sources.Image : App.SourcesDuo.Image;
        ApercuRendu.ImageDuo(b);
        CulteRendu.ImageDuo(b);
        ecran?.Rendu.ImageDuo(b);
        App.CameraVirtuelle?.MediaDuo(b);
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestBasculerDuo() => BasculerDuo();
    public void TestEpingler() => Epingler();
    public string TestDuoLibelle => DuoBoite.Visibility == Visibility.Visible ? DuoLibelle.Text : "";
    /// <summary>Choisit la place et la taille dans la carte DUO, comme à la souris.</summary>
    public bool TestDuoPlace(string place, double pourcent)
    {
        if (DuoPlace.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == place) is not { } item) return false;
        DuoPlace.SelectedItem = item;
        DuoTaille.Value = pourcent;
        return true;
    }

    /// <summary>« Reconnecter » : relance les captures en cours (écran principal et Duo).</summary>
    void ReconnecterSources()
    {
        var e = EtatProjete();
        App.Sources.Reconnecter(e);
        if (!duoMemeSource && e.Duo?.Etat is { Mode: "media", Media: { } m }) App.SourcesDuo.ReconnecterSource(m);
    }
}
