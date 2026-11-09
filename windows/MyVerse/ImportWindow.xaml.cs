using System.Windows;
using UglyToad.PdfPig;

namespace MyVerse.Ui;

/// <summary>
/// Importer des chants : coller un chant, recueil en texte, fichiers (VideoPsalm, OpenLyrics…), recueil en PDF, site web.
/// </summary>
public partial class ImportWindow : FenetreDialogue
{
    CancellationTokenSource? web;

    public ImportWindow()
    {
        InitializeComponent();
        NomRecueil.TextChanged += (_, _) => RecueilIndice.Visibility = NomRecueil.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Coller.Click += (_, _) =>
        {
            var rec = NomRecueil.Text.Trim();
            Close();
            if (Owner is MainWindow m)
            {
                var e = new EditeurWindow(null, null, rec) { Owner = m };
                if (e.ShowDialog() == true && e.IdEnregistre != null) m.OuvrirChant(e.IdEnregistre);
            }
        };
        TexteRecueil.Click += (_, _) => RecueilTexte();
        Fichiers.Click += (_, _) => FichiersChants();
        Dossier.Click += (_, _) => Dossiers();
        Pdf.Click += async (_, _) => await RecueilPdf();
        Web.Click += async (_, _) => await DepuisWeb();
        Url.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await DepuisWeb(); } };
        Arreter.Click += (_, _) => web?.Cancel();
        Fermer.Click += (_, _) => Close();
        Closed += (_, _) => web?.Cancel();
    }

    void Dire(string t) => Etat.Text = t;
    string Recueil => NomRecueil.Text.Trim();
    static string Pluriel(int n, string mot) => $"{n} {mot}{(n > 1 ? "s" : "")}";

    void RecueilParDefaut(string fichier)
    {
        if (Recueil.Length == 0) NomRecueil.Text = Path.GetFileNameWithoutExtension(fichier);
    }

    void RecueilTexte()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Recueil en texte", Filter = "Texte|*.txt|Tous les fichiers|*.*" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            var chants = Recueils.ChantsDepuisTexte(Texte.Decoder(File.ReadAllBytes(d.FileName)));
            if (chants.Count == 0) { Dire("Aucun chant trouvé dans ce fichier."); return; }
            RecueilParDefaut(d.FileName);
            int n = App.Regie.AjouterChants(chants, Recueil);
            Dire($"{Pluriel(n, "chant")} importé{(n > 1 ? "s" : "")} dans le recueil « {Recueil} ».");
            Journal.Info($"recueil texte importé : {d.FileName} ({n} chants)");
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Dire("Erreur de lecture : " + x.Message);
        }
    }

    void Dossiers()
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Dossiers de chants", Multiselect = true };
        if (d.ShowDialog(this) != true) return;
        Importer(d.FolderNames, f => ChantsFormats.LireDossier(f, erreurs));
    }

    readonly List<string> erreurs = new();

    void Importer(IEnumerable<string> sources, Func<string, List<ChantsFormats.ChantRecueil>> lire)
    {
        erreurs.Clear();
        var tous = new List<ChantsFormats.ChantRecueil>();
        foreach (var s in sources)
        {
            try { tous.AddRange(lire(s)); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidDataException) { erreurs.Add(Path.GetFileName(s) + " : " + x.Message); }
        }
        int n = App.Regie.AjouterChantsRecueils(tous, Recueil);
        var recs = tous.Select(x => Recueil.Length > 0 ? Recueil : x.Chant.Author).Distinct().ToList();
        if (recs.Count > 0) { App.Donnees.Reglages.Recueil = recs[0]; App.Donnees.SauverReglages(); (Owner as MainWindow)?.OuvrirChant(null); }
        Dire($"{Pluriel(n, "chant")} importé{(n > 1 ? "s" : "")} dans {Pluriel(recs.Count, "recueil")} : {string.Join(", ", recs)}." + (erreurs.Count > 0 ? " Erreurs : " + string.Join(" · ", erreurs) : ""));
    }

    void FichiersChants()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Fichiers de chants", Filter = ChantsFormats.Filtre, Multiselect = true };
        if (d.ShowDialog(this) != true) return;
        var zips = d.FileNames.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (zips.Count > 0) Importer(zips, f => ChantsFormats.LireZip(f, erreurs));
        if (zips.Count == d.FileNames.Length) return;
        int n = 0;
        var err = new List<string>();
        var tous = new List<Chant>();
        foreach (var f in d.FileNames.Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            try { tous.AddRange(ChantsFormats.LireFichier(f)); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
            {
                err.Add(Path.GetFileName(f) + " : " + x.Message);
            }
        }
        n = App.Regie.AjouterChants(tous, Recueil);
        var msg = (n > 0 ? $"{Pluriel(n, "chant")} importé{(n > 1 ? "s" : "")}." : "") + (err.Count > 0 ? " Erreurs : " + string.Join(" · ", err) : "");
        Dire(msg.Trim());
        if (err.Count > 0) Journal.Erreur("import de chants : " + string.Join(" · ", err));
    }

    /// <summary>Lignes de texte d'un PDF (mots et leur position) pour y trouver les chants numérotés.</summary>
    internal static List<Recueils.LignePdf> LignesPdf(string chemin, IProgress<string> progres)
    {
        using var doc = PdfDocument.Open(chemin);
        var pages = new List<IReadOnlyList<Recueils.MorceauPdf>>();
        int n = doc.NumberOfPages;
        for (int p = 1; p <= n; p++)
        {
            if (p % 10 == 0) progres.Report($"Lecture du PDF : page {p} sur {n}…");
            var page = doc.GetPage(p);
            var morceaux = new List<Recueils.MorceauPdf>();
            foreach (var w in page.GetWords())
            {
                if (string.IsNullOrWhiteSpace(w.Text)) continue;
                var l0 = w.Letters.Count > 0 ? w.Letters[0] : null;
                double h = w.Letters.Count > 0 ? w.Letters.Max(l => l.PointSize) : w.BoundingBox.Height;
                morceaux.Add(new Recueils.MorceauPdf(w.Text, w.BoundingBox.Left, l0 != null ? l0.StartBaseLine.Y : w.BoundingBox.Bottom, h));
            }
            pages.Add(morceaux);
        }
        return Recueils.LignesDepuisPages(pages);
    }

    async Task RecueilPdf()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Recueil en PDF", Filter = "PDF|*.pdf" };
        if (d.ShowDialog(this) != true) return;
        Dire("Lecture du PDF…");
        IsEnabled = false;
        try
        {
            var progres = new Progress<string>(Dire);
            var chants = await Task.Run(() => Recueils.ChantsDepuisLignes(LignesPdf(d.FileName, progres)));
            if (chants.Count == 0) { Dire("Aucun chant numéroté trouvé dans ce PDF (le texte est peut-être une image scannée)."); return; }
            RecueilParDefaut(d.FileName);
            int n = App.Regie.AjouterChants(chants, Recueil);
            Dire($"{n} chants importés dans le recueil « {Recueil} ». Vérifiez quelques chants : un PDF mal formé peut couper des strophes.");
            Journal.Info($"recueil PDF importé : {d.FileName} ({n} chants)");
        }
        catch (Exception x)
        {
            Dire("Erreur de lecture du PDF : " + x.Message);
            Journal.Erreur("lecture du PDF " + Path.GetFileName(d.FileName), x);
        }
        finally { IsEnabled = true; }
    }

    async Task DepuisWeb()
    {
        var url = Url.Text.Trim();
        if (url.Length == 0 || web != null) return;
        web = new CancellationTokenSource();
        Web.IsEnabled = false;
        Arreter.Visibility = Visibility.Visible;
        try
        {
            var progres = new Progress<string>(Dire);
            var jeton = web.Token;
            var res = await Task.Run(() => Recueils.DepuisWeb(url, ((IProgress<string>)progres).Report, jeton));
            if (res.Count == 0) { Dire("Aucune parole trouvée sur cette page."); return; }
            int n = App.Regie.AjouterChants(res, Recueil);
            Dire(n == 1 ? $"« {res[0].Title} » importé." : $"{n} chants importés depuis {new Uri(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url).Host}.");
        }
        catch (OperationCanceledException) { Dire("Import arrêté."); }
        catch (Exception x)
        {
            Dire("Impossible de lire cette page : " + x.Message);
            Journal.Erreur("import web " + url, x);
        }
        finally
        {
            web?.Dispose();
            web = null;
            Web.IsEnabled = true;
            Arreter.Visibility = Visibility.Collapsed;
        }
    }
}
