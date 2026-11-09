namespace MyVerse;

/// <summary>
/// Versions de la Bible : Louis Segond 1910 et King James intégrées, Bibles importées (dossier bibles).
/// Version principale + 2e version affichée sous le verset (versets bilingues).
/// </summary>
public sealed class Bibles
{
    public sealed record Info(string Id, string Nom, string Court, int Versets = 0, bool Integree = false);

    public static readonly Info[] Integrees =
    {
        new("lsg", "Louis Segond 1910", "LSG", 31102, true),
        new("kjv", "King James (anglais)", "KJV", 31102, true),
    };

    readonly Donnees donnees;
    readonly Dictionary<string, Bible> cache = new();
    readonly object verrou = new();

    public Bible Principale { get; private set; } = null!;
    public Bible? Seconde { get; private set; }
    ReglagesBibles R => donnees.Reglages.Bibles;

    /// <summary>La version principale ou la 2e version a changé.</summary>
    public event Action? Change;

    public Bibles(Donnees d)
    {
        donnees = d;
        Appliquer(false);
    }

    public IReadOnlyList<Info> Toutes() =>
        Integrees.Concat(R.Importees.Select(b => new Info(b.Id, b.Name, b.Short, b.Versets))).ToList();
    public Info? InfoDe(string id) => Toutes().FirstOrDefault(v => v.Id == id);
    public string IdPrincipale => R.Principale;
    public string IdSeconde => R.Seconde;

    string Fichier(string id) => id switch
    {
        "lsg" or "kjv" => Path.Combine(donnees.DossierRessources, id + ".json"),
        _ => Path.Combine(donnees.DossierBibles, id + ".json"),
    };

    /// <summary>Charge une Bible (gardée en mémoire ensuite). Null si le fichier manque ou est illisible.</summary>
    public Bible? Charger(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (verrou)
        {
            if (cache.TryGetValue(id, out var b)) return b;
        }
        try
        {
            var f = Fichier(id);
            if (!File.Exists(f)) { Journal.Erreur($"Bible « {id} » introuvable ({f})"); return null; }
            var bible = Bible.DepuisJson(id, Texte.Decoder(File.ReadAllBytes(f)));
            var info = InfoDe(id);
            if (info != null)
            {
                if (string.IsNullOrEmpty(bible.Court)) bible.Court = info.Court;
                bible.Court = info.Court; // le nom court choisi à l'import
                if (string.IsNullOrEmpty(bible.Nom)) bible.Nom = info.Nom;
            }
            lock (verrou) cache[id] = bible;
            return bible;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Journal.Erreur($"Bible « {id} » illisible", e);
            return null;
        }
    }

    void Appliquer(bool prevenir = true)
    {
        var p = Charger(R.Principale);
        if (p == null && R.Principale != "lsg") { R.Principale = "lsg"; p = Charger("lsg"); }
        Principale = p ?? new Bible { Id = "lsg", Nom = "Louis Segond 1910", Court = "LSG", Livres = Enumerable.Range(0, 66).Select(i => new Bible.Livre { Nom = Livres.Nom(i) }).ToList() };
        Seconde = string.IsNullOrEmpty(R.Seconde) || R.Seconde == R.Principale ? null : Charger(R.Seconde);
        if (prevenir) Change?.Invoke();
    }

    /// <summary>Affiche cette Bible (version principale).</summary>
    public void Choisir(string id)
    {
        if (R.Principale == id) return;
        R.Principale = id;
        if (R.Seconde == id) R.Seconde = "";
        donnees.SauverReglages();
        Appliquer();
    }

    /// <summary>Ajoute ou retire cette Bible comme 2e version (sous le verset).</summary>
    public void BasculerSeconde(string id)
    {
        if (id == R.Principale) return;
        R.Seconde = R.Seconde == id ? "" : id;
        donnees.SauverReglages();
        Appliquer();
    }

    public void DefinirSeconde(string id)
    {
        if (id == R.Principale) id = "";
        if (R.Seconde == id) return;
        R.Seconde = id;
        donnees.SauverReglages();
        Appliquer();
    }

    /// <summary>« LSG » ou « LSG · KJV » (versets bilingues).</summary>
    public string Etiquette()
    {
        var p = InfoDe(R.Principale) ?? Integrees[0];
        var s = !string.IsNullOrEmpty(R.Seconde) && Seconde != null ? InfoDe(R.Seconde) : null;
        return p.Court + (s != null ? " · " + s.Court : "");
    }

    /// <summary>Texte de la 2e version pour ces versets (vide sans 2e version).</summary>
    public string Texte2(int b, int c, int v1, int v2)
    {
        var s = Seconde;
        if (s == null) return "";
        var vs = s.Versets(b, c);
        var o = new List<string>();
        for (int v = v1; v <= v2; v++) if (v - 1 < vs.Count && !string.IsNullOrWhiteSpace(vs[v - 1])) o.Add(vs[v - 1]);
        return string.Join(" ", o);
    }

    /// <summary>Bibles où chercher des mots : principale, 2e version, puis toutes les Bibles importées.</summary>
    public List<Bible> Versions()
    {
        var ids = new List<string> { R.Principale };
        if (!string.IsNullOrEmpty(R.Seconde)) ids.Add(R.Seconde);
        ids.AddRange(R.Importees.Select(b => b.Id));
        var o = new List<Bible>();
        foreach (var id in ids.Distinct())
        {
            var b = id == R.Principale ? Principale : Charger(id);
            if (b != null) o.Add(b);
        }
        return o;
    }

    /// <summary>Lit un fichier de Bible (sans l'enregistrer) : pour proposer le nom court avant l'import.</summary>
    public static Bible Lire(string chemin) => BibleImport.Analyser(Path.GetFileName(chemin), Texte.Decoder(File.ReadAllBytes(chemin)));

    /// <summary>Enregistre une Bible lue avec Lire() dans le dossier bibles et l'ajoute à la liste.</summary>
    public Info Ajouter(Bible b, string court)
    {
        court = (court ?? "").Trim();
        if (court.Length > 8) court = court[..8];
        if (court.Length == 0) court = b.Court.Length > 0 ? b.Court : "BIB";
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = "b" + t.ToString("x");
        while (R.Importees.Any(x => x.Id == id)) id = "b" + (++t).ToString("x");
        b.Id = id;
        b.Court = court;
        Directory.CreateDirectory(donnees.DossierBibles);
        Donnees.Ecrire(Fichier(id), b.VersJson());
        int n = b.NombreVersets();
        R.Importees.Add(new BibleImportee { Id = id, Name = b.Nom, Short = court, Versets = n });
        donnees.SauverReglages();
        lock (verrou) cache[id] = b;
        Journal.Info($"Bible importée : {b.Nom} ({n} versets)");
        Change?.Invoke();
        return new Info(id, b.Nom, court, n);
    }

    public void Supprimer(string id)
    {
        var x = R.Importees.FirstOrDefault(b => b.Id == id);
        if (x == null) return;
        R.Importees.Remove(x);
        if (R.Principale == id) R.Principale = "lsg";
        if (R.Seconde == id) R.Seconde = "";
        lock (verrou) cache.Remove(id);
        try { File.Delete(Fichier(id)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        donnees.SauverReglages();
        Appliquer();
    }

    /// <summary>Prépare les index de recherche en arrière-plan (la première recherche est alors instantanée).</summary>
    public void PreparerRecherche()
    {
        foreach (var b in Versions()) Recherche.Preparer(b);
    }
}
