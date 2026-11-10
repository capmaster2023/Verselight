using System.Windows;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>MyVerse 4.0 : démarrage, une seule copie à la fois, services (Bibles, régie, téléphone, OBS).</summary>
public partial class App : Application
{
    /// <summary>La version compilée (fichier VERSION du dépôt, « 3.0.0 »), sans le numéro de commit ajouté par la compilation.</summary>
    public static string Version { get; } = LireVersion();
    static string LireVersion()
    {
        var v = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "";
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        return v.Trim().Length > 0 ? v.Trim() : "3.0.0";
    }
    public static string Ressources => Path.Combine(AppContext.BaseDirectory, "Ressources");
    public static Donnees Donnees { get; private set; } = null!;
    public static Bibles Bibles { get; private set; } = null!;
    public static Regie Regie { get; private set; } = null!;
    public static SortieObs SortieObs { get; private set; } = null!;
    public static SourcesDisplay Sources { get; private set; } = null!;
    /// <summary>Capture du contenu épinglé (Duo), en même temps que celle de l'écran principal.</summary>
    public static SourcesDisplay SourcesDuo { get; private set; } = null!;
    public static MyVirtualCam? CameraVirtuelle { get; private set; }
    public static Audio.ChurchAudioService Audio { get; private set; } = null!;
    public static Telephone Telephone { get; private set; } = null!;
    /// <summary>Dossier du test automatique (« MyChurch.exe --autotest dossier »), sinon null.</summary>
    public static string? AutoTest { get; private set; }
    /// <summary>Résultat du contrôle du pare-feu : « ok », « missing », « refused », « error », « demande » ou vide (en cours).</summary>
    public static string Parefeu_
    {
        get => parefeu;
        set { parefeu = value; ParefeuChange?.Invoke(); }
    }
    static string parefeu = "";
    /// <summary>L'état du pare-feu a changé (fil de l'interface).</summary>
    public static event Action? ParefeuChange;

    static Mutex? instance;
    static EventWaitHandle? signal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Contains("--virtualcam-install") || args.Contains("--virtualcam-uninstall"))
        {
            Shutdown(MyVirtualCam.Enregistrer(args.Contains("--virtualcam-install")));
            return;
        }
        // lancé en administrateur par l'installateur ou par « Autoriser dans le pare-feu »
        if (args.Contains("--parefeu"))
        {
            int code = 1;
            try { code = MyVerse.Parefeu.Appliquer(); } catch (Exception) { }
            Shutdown(code);
            return;
        }
        // vérification (dépannage, compilation sur GitHub) : code 0 si les règles du pare-feu sont en place
        if (args.Contains("--parefeu-verifier"))
        {
            bool ok = false;
            try { ok = MyVerse.Parefeu.Present(); } catch (Exception) { }
            Shutdown(ok ? 0 : 2);
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
        Journal.Info($"— démarrage de MyChurch {Version} (Windows) —");
        DispatcherUnhandledException += (_, ev) =>
        {
            Journal.Erreur("erreur", ev.Exception);
            ev.Handled = true;
            if (AutoTest != null) Ui.AutoTest.Echec("exception : " + ev.Exception);
            else if (MainWindow is MainWindow mw) mw.Toast("Erreur : " + ev.Exception.Message + " (détails dans Paramètres › Journal d'erreurs)");
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
            MessageBox.Show("MyChurch n'a pas pu démarrer : " + x.Message + "\n\nJournal : " + Path.Combine(dossier, "journal-myverse.txt"), "MyVerse", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        SortieObs = new SortieObs(Path.Combine(Ressources, "obs"), AutoTest != null ? Path.Combine(dossier, "VerseLight-OBS") : null);
        Telephone = new Telephone(dossier, Regie, f => Dispatcher.InvokeAsync(f).Task.Unwrap());
        try { Telephone.Demarrer(); }
        catch (Exception x) { Journal.Erreur("téléphone : démarrage", x); }
        _ = Task.Run(() =>
        {
            if (SortieObs.Preparer()) SortieObs.Ecrire(Regie.Etat);
        });

        Sources = new SourcesDisplay();
        SourcesDuo = new SourcesDisplay("display-duo.jpg");
        if (AutoTest == null || Environment.GetEnvironmentVariable("MYCHURCH_TEST_CAM") == "1")
            try { CameraVirtuelle = new MyVirtualCam(); } catch (Exception ex) { Journal.Erreur("MyVirtualCam démarrage", ex); }
        Audio = new Audio.ChurchAudioService(Donnees.Reglages.Audio);
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
        // le moteur audio d'OBS démarre une fois la fenêtre affichée (quelques dixièmes de seconde) ;
        // sans le dossier « obs », MyChurch fonctionne normalement et le panneau Audio l'indique
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Audio.Start()));
        if (Donnees.Migration.Length > 0) w.Toast("Données de MyChurch 3 reprises : " + Donnees.Migration + ".");
        if (AutoTest != null) Ui.AutoTest.Lancer(w, AutoTest);
    }

    /// <summary>Une seule copie de MyChurch : un 2e lancement ramène la fenêtre déjà ouverte.</summary>
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
        Audio?.Dispose();
        Sources?.Dispose();
        SourcesDuo?.Dispose();
        CameraVirtuelle?.Dispose();
        try { Telephone?.Dispose(); } catch (Exception) { }
        Journal.Sauver();
        base.OnExit(e);
    }
}
