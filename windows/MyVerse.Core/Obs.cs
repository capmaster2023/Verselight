using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyVerse;

/// <summary>
/// Sortie OBS sans fenêtre : dossier %APPDATA%\VerseLight-OBS avec ecran.html (source navigateur « Fichier local »),
/// qui relit etat.json (écrit ici à chaque changement).
/// </summary>
public sealed class SortieObs
{
    readonly string modele;
    Timer? minuterie;
    EtatEcran? enAttente;
    readonly object verrou = new();

    public string Dossier { get; }
    public string Page => Path.Combine(Dossier, "ecran.html");
    public bool Prete { get; private set; }

    /// <param name="modele">Dossier des fichiers de la page (Ressources\obs : ecran.html, css, fonts).</param>
    public SortieObs(string modele, string? dossier = null)
    {
        this.modele = modele;
        Dossier = dossier ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerseLight-OBS");
    }

    /// <summary>Copie la page et ses polices dans le dossier OBS (si elles ont changé).</summary>
    public bool Preparer()
    {
        try
        {
            if (!Directory.Exists(modele)) { Journal.Erreur("page OBS introuvable : " + modele); return false; }
            foreach (var f in Directory.GetFiles(modele, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(modele, f);
                var dest = Path.Combine(Dossier, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var fi = new FileInfo(dest);
                if (!fi.Exists || fi.Length != new FileInfo(f).Length || fi.LastWriteTimeUtc < File.GetLastWriteTimeUtc(f))
                    File.Copy(f, dest, true);
            }
            Prete = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Journal.Erreur("préparation de la sortie OBS", e);
        }
        return Prete;
    }

    /// <summary>Écrit l'état pour la page OBS (regroupé : au plus une écriture toutes les 30 ms).</summary>
    public void Ecrire(EtatEcran e)
    {
        if (!Prete) return;
        lock (verrou)
        {
            enAttente = e;
            minuterie ??= new Timer(_ => Vider(), null, Timeout.Infinite, Timeout.Infinite);
            minuterie.Change(30, Timeout.Infinite);
        }
    }

    void Vider()
    {
        EtatEcran? e;
        lock (verrou) { e = enAttente; enAttente = null; }
        if (e == null) return;
        var n = JsonSerializer.SerializeToNode(e, Donnees.Json)!.AsObject();
        n["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int essai = 0; essai < 3; essai++)
        {
            try
            {
                var tmp = Path.Combine(Dossier, "etat.json.tmp");
                File.WriteAllText(tmp, n.ToJsonString(Donnees.Json), new UTF8Encoding(false));
                File.Move(tmp, Path.Combine(Dossier, "etat.json"), true);
                return;
            }
            catch (IOException) { Thread.Sleep(15); } // OBS est peut-être en train de le lire
            catch (UnauthorizedAccessException) { Thread.Sleep(15); }
        }
    }
}
