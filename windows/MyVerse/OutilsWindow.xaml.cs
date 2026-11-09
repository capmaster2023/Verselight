using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MyVerse.Ui;

/// <summary>Outils : mise à jour (GitHub Releases) et journal d'erreurs (copier, vider).</summary>
public partial class OutilsWindow : FenetreDialogue
{
    MiseAJour.Resultat? maj;

    public OutilsWindow(bool chercher)
    {
        InitializeComponent();
        maj = (Application.Current.MainWindow as MainWindow)?.MiseAJourDisponible;
        MajEtat.Text = "Version installée : " + App.Version + (maj != null ? " · nouvelle version " + maj.Version + " disponible" : "");
        Installer.Visibility = maj != null ? Visibility.Visible : Visibility.Collapsed;
        Aide.Text = "Pour signaler un problème : Copier le journal, puis collez-le dans votre message. Copie aussi dans " + Path.Combine(App.Donnees.Dossier, "journal-myverse.txt");
        RemplirJournal();
        Verifier.Click += async (_, _) => await Chercher();
        Installer.Click += async (_, _) => await Installer_();
        Actualiser.Click += (_, _) => RemplirJournal();
        Copier.Click += (_, _) =>
        {
            RemplirJournal();
            try { Clipboard.SetText(JournalTexte.Text); } catch (Exception) { }
            (Owner as MainWindow)?.Toast("Journal copié : collez-le dans votre message.");
        };
        Vider.Click += (_, _) =>
        {
            Journal.Vider();
            RemplirJournal();
            (Owner as MainWindow)?.Toast("Journal vidé.");
        };
        Dossier.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + App.Donnees.Dossier + "\"") { UseShellExecute = true }); }
            catch (Exception x) { Journal.Erreur("ouverture du dossier", x); }
        };
        ExporterBibles.Click += (_, _) => Exporter(false, true);
        ExporterChants.Click += (_, _) => Exporter(true, false);
        ExporterTout.Click += (_, _) => Exporter(true, true);
        ImporterFichier.Click += (_, _) => ImporterTransfert();
        Fermer.Click += (_, _) => Close();
        if (chercher) Loaded += async (_, _) => await Chercher();
    }

    void RemplirJournal()
    {
        var t = App.Telephone;
        var o = App.Obs;
        var m = Application.Current.MainWindow as MainWindow;
        var lignes = new List<string>
        {
            $"MyVerse {App.Version} (Windows) · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · programme : {AppContext.BaseDirectory}",
            $"Données : {App.Donnees.Dossier} · {App.Donnees.Chants.Count} chants · Bible : {App.Bibles.Etiquette()}",
            $"Téléphones : port {(t.Port > 0 ? t.Port.ToString() : "AUCUN")} · pare-feu : {(App.Parefeu_.Length > 0 ? App.Parefeu_ : "inconnu")} · connectés : {t.Connectes().Count} / jumelés : {t.Store.Liste().Count}",
            $"OBS : {(o.Actif ? (o.Connecte ? "connecté" : "NON CONNECTÉ") : "désactivé")} · écran de projection : {(m?.Ecran != null ? "ouvert" : App.Donnees.Reglages.Ecran?.Mode == "obs" ? "OBS (fichier local)" : "fermé")}",
            $"Windows : {Environment.OSVersion.VersionString} · .NET {Environment.Version}",
            "",
            "===== MyVerse =====",
        };
        var j = Journal.Lignes();
        lignes.AddRange(j.Count > 0 ? j : new List<string> { "(vide)" });
        JournalTexte.Text = string.Join(Environment.NewLine, lignes);
        JournalTexte.ScrollToEnd();
    }

    async Task Chercher()
    {
        MajEtat.Text = "Recherche d'une mise à jour…";
        Verifier.IsEnabled = false;
        try
        {
            var m = Application.Current.MainWindow as MainWindow;
            var r = m != null ? await m.VerifierMaj(true) : await MiseAJour.Verifier(App.Version);
            if (r.Disponible)
            {
                maj = r;
                Installer.Visibility = Visibility.Visible;
                MajEtat.Text = $"Nouvelle version disponible : {r.Version} (installée : {App.Version}).";
            }
            else
            {
                Installer.Visibility = Visibility.Collapsed;
                MajEtat.Text = r.Erreur ?? $"MyVerse est à jour (version {App.Version}).";
            }
        }
        finally { Verifier.IsEnabled = true; }
    }

    async Task Installer_()
    {
        if (maj == null) return;
        Installer.IsEnabled = false;
        Verifier.IsEnabled = false;
        MajEtat.Text = $"Téléchargement de MyVerse {maj.Version}…";
        try
        {
            var p = new Progress<int>(pct => MajEtat.Text = $"Téléchargement… {pct} %");
            await MiseAJour.Installer(maj.Url, p);
            MajEtat.Text = "Installation… MyVerse va se fermer puis se rouvrir.";
            Journal.Info($"mise à jour vers {maj.Version} : installation");
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception x)
        {
            Journal.Erreur("mise à jour", x);
            MajEtat.Text = "Téléchargement de la mise à jour impossible. Réessayez plus tard. (" + x.Message + ")";
            Installer.IsEnabled = true;
            Verifier.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ transfert vers un autre ordinateur
    static string Contenu(Transfert.Manifeste m) => string.Join(" et ", new[]
    {
        m.Chants > 0 ? $"{m.Chants} chants ({m.Recueils.Count} recueil{(m.Recueils.Count > 1 ? "s" : "")})" : null,
        m.Bibles.Count > 0 ? $"{m.Bibles.Count} Bible{(m.Bibles.Count > 1 ? "s" : "")} ({string.Join(", ", m.Bibles.Select(b => b.Short))})" : null,
    }.Where(x => x != null));

    static T Patienter<T>(Func<T> f)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try { return f(); }
        finally { Mouse.OverrideCursor = null; }
    }

    void Exporter(bool chants, bool bibles)
    {
        int nbBibles = App.Donnees.Reglages.Bibles.Importees.Count, nbChants = App.Donnees.Chants.Count;
        if ((!chants || nbChants == 0) && (!bibles || nbBibles == 0))
        {
            Dialogue.Informer(this, bibles && !chants
                ? "Aucune Bible importée à exporter : LSG et KJV sont déjà dans chaque MyVerse."
                : "Aucun chant à exporter.");
            return;
        }
        var quoi = chants && bibles ? "tout" : chants ? "chants" : "bibles";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter vers un autre ordinateur",
            FileName = $"MyVerse-{quoi}-{DateTime.Now:yyyy-MM-dd}{Transfert.Extension}",
            DefaultExt = Transfert.Extension,
            Filter = $"Fichier MyVerse (*{Transfert.Extension})|*{Transfert.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var m = Patienter(() => Transfert.Exporter(dlg.FileName, App.Donnees, chants, bibles, App.Version));
            Dialogue.Informer(this, $"Exporté : {Contenu(m)}.\n\nFichier : {dlg.FileName}\n\nSur l'autre ordinateur : Outils → Importer un fichier MyVerse.");
        }
        catch (Exception x)
        {
            Journal.Erreur("export MyVerse", x);
            Dialogue.Informer(this, "L'export n'a pas pu se faire : " + x.Message);
        }
    }

    void ImporterTransfert()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer un fichier MyVerse",
            Filter = $"Fichier MyVerse (*{Transfert.Extension};*.zip)|*{Transfert.Extension};*.zip",
        };
        if (dlg.ShowDialog(this) != true) return;
        Transfert.Manifeste m;
        try { m = Transfert.Lire(dlg.FileName); }
        catch (Exception x)
        {
            Journal.Erreur("import MyVerse", x);
            Dialogue.Informer(this, "Ce fichier n'est pas un export MyVerse. Sur l'autre ordinateur : Outils → Exporter.");
            return;
        }
        var contenu = Contenu(m);
        if (contenu.Length == 0) { Dialogue.Informer(this, "Ce fichier MyVerse est vide."); return; }
        if (!Dialogue.Confirmer(this, $"Importer {contenu} ?" +
                (m.Chants > 0 ? "\n\nUn chant qui existe déjà (même recueil, même titre) est remplacé par celui du fichier ; rien n'est mis en double." : ""),
                "Importer")) return;
        try
        {
            var b = Patienter(() => Transfert.Importer(dlg.FileName, App.Regie, App.Bibles));
            var lignes = new List<string>();
            if (m.Chants > 0)
                lignes.Add($"{b.ChantsAjoutes} chant{(b.ChantsAjoutes > 1 ? "s" : "")} ajouté{(b.ChantsAjoutes > 1 ? "s" : "")}" +
                           (b.ChantsMisAJour > 0 ? $", {b.ChantsMisAJour} mis à jour" : "") + (b.ChantsPareils > 0 ? $", {b.ChantsPareils} déjà là" : ""));
            if (m.Bibles.Count > 0)
                lignes.Add($"{b.BiblesAjoutees} Bible{(b.BiblesAjoutees > 1 ? "s" : "")} ajoutée{(b.BiblesAjoutees > 1 ? "s" : "")}" +
                           (b.BiblesPresentes > 0 ? $", {b.BiblesPresentes} déjà là" : ""));
            Dialogue.Informer(this, "Import terminé :\n" + string.Join("\n", lignes));
        }
        catch (Exception x)
        {
            Journal.Erreur("import MyVerse", x);
            Dialogue.Informer(this, "L'import n'a pas pu se faire : " + x.Message);
        }
    }
}
