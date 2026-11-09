using System.Windows;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>MyVerse 4.0 : démarrage, une seule copie à la fois, services (Bibles, régie, téléphone, OBS).</summary>
public partial class App : Application
{
    public static string Version { get; } = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "4.0.0";
    public static string Ressources => Path.Combine(AppContext.BaseDirectory, "Ressources");
    public static Donnees Donnees { get; private set; } = null!;
    public static Bibles Bibles { get; private set; } = null!;
    public static Regie Regie { get; private set; } = null!;
    public static SortieObs SortieObs { get; private set; } = null!;
    public static Obs Obs { get; private set; } = null!;
    public static Telephone Telephone { get; private set; } = null!;
    /// <summary>Dossier du test automatique (« MyVerse.exe --autotest dossier »), sinon null.</summary>
    public static string? AutoTest { get; private set; }
    /// <summary>Résultat du contrôle du pare-feu : « ok », « missing », « refused », « error » ou vide (en cours).</summary>
    public static string Parefeu_ { get; set; } = "";

    static Mutex? instance;
    static EventWaitHandle? signal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        // lancé en administrateur par l'installateur ou par « Autoriser dans le pare-feu »
        if (args.Contains("--parefeu"))
        {
            int code = 1;
            try { code = MyVerse.Parefeu.Appliquer(); } catch (Exception) { }
            Shutdown(code);
            return;
        }
        int i = Array.IndexOf(args, "--autotest");
        if (i >= 0) AutoTest = i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "myverse-autotest");
        if (AutoTest == null && !UneSeuleCopie())
        {
            Shutdown(0);
            return;
        }

        var dossier = Donnees.DossierParDefaut();
        try { Directory.CreateDirectory(dossier); } catch (Exception) { }
        Journal.Ouvrir(dossier);
        Journal.Info($"— démarrage de MyVerse {Version} (Windows) —");
        DispatcherUnhandledException += (_, ev) =>
        {
            Journal.Erreur("erreur", ev.Exception);
            ev.Handled = true;
            if (AutoTest != null) Ui.AutoTest.Echec("exception : " + ev.Exception);
            else if (MainWindow is MainWindow mw) mw.Toast("Erreur : " + ev.Exception.Message + " (détails dans Outils › Journal)");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception x) Journal.Erreur("erreur fatale", x);
            Journal.Sauver();
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Journal.Erreur("tâche", ev.Exception);
            ev.SetObserved();
        };

        try
        {
            Donnees = Donnees.Charger(dossier, Ressources);
            Bibles = new Bibles(Donnees);
            Regie = new Regie(Donnees, Bibles);
        }
        catch (Exception x)
        {
            Journal.Erreur("démarrage", x);
            Journal.Sauver();
            MessageBox.Show("MyVerse n'a pas pu démarrer : " + x.Message + "\n\nJournal : " + Path.Combine(dossier, "journal-myverse.txt"), "MyVerse", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        SortieObs = new SortieObs(Path.Combine(Ressources, "obs"), AutoTest != null ? Path.Combine(dossier, "VerseLight-OBS") : null);
        Obs = new Obs(() => Donnees.Reglages.Obs, SortieObs);
        Telephone = new Telephone(dossier, Regie, f => Dispatcher.InvokeAsync(f).Task.Unwrap());
        Telephone.EtatObs = () => (Obs.Actif, Obs.Connecte);
        Regie.Oeil += v => _ = Obs.Oeil(v);
        try { Telephone.Demarrer(); }
        catch (Exception x) { Journal.Erreur("téléphone : démarrage", x); }
        _ = Task.Run(() =>
        {
            if (SortieObs.Preparer()) SortieObs.Ecrire(Regie.Etat);
        });

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
        if (Donnees.Migration.Length > 0) w.Toast("Données de MyVerse 3 reprises : " + Donnees.Migration + ".");
        if (AutoTest != null) Ui.AutoTest.Lancer(w, AutoTest);
    }

    /// <summary>Une seule copie de MyVerse : un 2e lancement ramène la fenêtre déjà ouverte.</summary>
    static bool UneSeuleCopie()
    {
        instance = new Mutex(true, @"Local\MyVerse4", out bool premier);
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MyVerse4-montrer");
        if (!premier)
        {
            signal.Set();
            return false;
        }
        var t = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
                Current?.Dispatcher.BeginInvoke(new Action(() => (Current.MainWindow as MainWindow)?.Ramener()));
            }
        }) { IsBackground = true, Name = "une seule copie" };
        t.Start();
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Journal.Info("fermeture de MyVerse");
        try { Telephone?.Dispose(); } catch (Exception) { }
        try { Obs?.Dispose(); } catch (Exception) { }
        Journal.Sauver();
        base.OnExit(e);
    }
}
