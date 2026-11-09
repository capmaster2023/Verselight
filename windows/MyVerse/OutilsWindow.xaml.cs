using System.Diagnostics;
using System.Windows;

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
}
