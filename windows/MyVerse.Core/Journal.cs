namespace MyVerse;

/// <summary>
/// Journal d'erreurs : erreurs et événements importants. Visible dans Paramètres › Journal d'erreurs ;
/// copie dans %APPDATA%\VerseLight\journal-myverse.txt (lisible même si MyVerse ne s'ouvre pas).
/// </summary>
public static class Journal
{
    const int MAX = 400;
    static readonly object Verrou = new();
    static readonly List<string> lignes = new();
    static string? fichier;
    static Timer? minuterie;

    /// <summary>Événement : une ligne a été ajoutée (thread quelconque).</summary>
    public static event Action<string>? Ajoute;

    public static void Ouvrir(string dossier)
    {
        lock (Verrou)
        {
            fichier = Path.Combine(dossier, "journal-myverse.txt");
            try
            {
                if (File.Exists(fichier))
                {
                    var anciennes = File.ReadAllLines(fichier).Where(l => l.Length > 0).ToList();
                    lignes.InsertRange(0, anciennes.Skip(Math.Max(0, anciennes.Count - MAX)));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static void Ajouter(string type, string msg)
    {
        var l = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {type} {Texte.Espaces.Replace(msg ?? "", " ")}";
        if (l.Length > 900) l = l[..900] + "…";
        lock (Verrou)
        {
            lignes.Add(l);
            if (lignes.Count > MAX) lignes.RemoveRange(0, lignes.Count - MAX);
            minuterie ??= new Timer(_ => Sauver(), null, Timeout.Infinite, Timeout.Infinite);
            minuterie.Change(800, Timeout.Infinite);
        }
        System.Diagnostics.Debug.WriteLine(l);
        try { Ajoute?.Invoke(l); } catch { }
    }

    public static void Info(string msg) => Ajouter("INFO", msg);
    public static void Erreur(string msg) => Ajouter("ERREUR", msg);
    public static void Erreur(string quoi, Exception e) => Ajouter("ERREUR", $"{quoi} : {e.GetType().Name} : {e.Message}" + (e.InnerException != null ? $" ({e.InnerException.Message})" : "") + " @ " + PremiereLigne(e.StackTrace));

    static string PremiereLigne(string? pile)
    {
        var l = (pile ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
        return l.Length > 200 ? l[..200] : l;
    }

    public static List<string> Lignes() { lock (Verrou) return lignes.ToList(); }

    public static void Vider()
    {
        lock (Verrou) lignes.Clear();
        Sauver();
    }

    /// <summary>Écrit le journal tout de suite (à la fermeture de MyVerse).</summary>
    public static void Sauver()
    {
        string? f;
        string contenu;
        lock (Verrou)
        {
            f = fichier;
            contenu = lignes.Count > 0 ? string.Join("\r\n", lignes) + "\r\n" : "";
        }
        if (f == null) return;
        try { File.WriteAllText(f, contenu); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
