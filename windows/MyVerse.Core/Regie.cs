using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Ce qui est projeté (même forme que etat.json, lu par la page OBS).</summary>
public sealed record EtatEcran
{
    [JsonPropertyName("mode")] public string Mode { get; init; } = "vide"; // vide, verset, chant
    [JsonPropertyName("lignes")] public IReadOnlyList<string> Lignes { get; init; } = Array.Empty<string>();
    [JsonPropertyName("ref")] public string Ref { get; init; } = "";
    [JsonPropertyName("theme")] public string Theme { get; init; } = "nuit";
    [JsonPropertyName("taille")] public double Taille { get; init; } = 1;
    [JsonPropertyName("presentation")] public string Presentation { get; init; } = "plein";
    [JsonPropertyName("media"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SourceDisplay? Media { get; init; }
    [JsonPropertyName("second")] public TexteSecond? Second { get; init; }

    public sealed record TexteSecond([property: JsonPropertyName("texte")] string Texte);
    /// <summary>Duo : ce qui est épinglé en même temps (coin ou côte à côte).</summary>
    [JsonPropertyName("duo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuoEcran? Duo { get; init; }
    [JsonIgnore] public bool Vide => Mode == "vide";
}

/// <summary>Le second contenu du Duo. Mode : hd, hg, bd, bg (un coin) ou cote (côte à côte). Taille : part de l'écran pour un coin.</summary>
public sealed record DuoEcran(
    [property: JsonPropertyName("etat")] EtatEcran Etat,
    [property: JsonPropertyName("libelle")] string Libelle,
    [property: JsonPropertyName("place")] string Place,
    [property: JsonPropertyName("part")] double Part)
{
    /// <summary>Place « libre » (dossier de l'onglet Duo) : le dossier, et les cadres [x1, y1, largeur1, x2, y2, largeur2] en parts de l'écran.</summary>
    [JsonPropertyName("dossier"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Dossier { get; init; }
    [JsonPropertyName("cadres"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Cadres { get; init; }
}

/// <summary>Ce qui est à l'écran : des versets (éventuellement un morceau d'un verset découpé) ou une strophe de chant.</summary>
public abstract record Cible;
public sealed record CibleVerset(int B, int C, int V1, int V2, int Part = 0, int Parts = 0) : Cible;
public sealed record CibleChant(string Id, int I) : Cible;

/// <summary>La diapositive qui viendra avec « Suivant ».</summary>
public sealed record Suivante(Cible Cible, string Label, string Texte);

/// <summary>Aperçu d'une référence (télécommande).</summary>
public sealed record Apercu(string Ref, List<string> Lignes, List<(int V, string T)> Versets, string Livre, int C);

/// <summary>
/// La régie : ce qui est à l'écran, versets et chants, suivant / précédent, versets découpés, chants enregistrés.
/// Sans interface : utilisée par la fenêtre principale, la fenêtre de projection, OBS et les téléphones.
/// </summary>
public sealed partial class Regie
{
    readonly Donnees donnees;
    public Bibles Bibles { get; }
    public EtatEcran Etat { get; private set; }
    public Cible? Cible { get; private set; }

    /// <summary>Versets choisis dans la fenêtre principale (affichés par « Afficher » ou « Suivant » quand rien n'est à l'écran).</summary>
    public Ref? Selection { get; set; }
    /// <summary>L'onglet Bible est ouvert (sinon : Chants).</summary>
    public bool OngletBible { get; set; } = true;
    /// <summary>La fenêtre de projection est ouverte (fourni par l'interface).</summary>
    public Func<bool> EcranOuvert { get; set; } = () => false;

    /// <summary>L'état a changé (projection, aperçu, téléphones, OBS).</summary>
    public event Action? Change;
    /// <summary>Quelque chose s'affiche (vrai) ou l'écran est vidé (faux) : pour la source OBS.</summary>
    public event Action<bool>? Oeil;
    /// <summary>La liste des chants a changé (import, modification, suppression).</summary>
    public event Action? ChantsChange;
    /// <summary>Message court pour l'utilisateur.</summary>
    public event Action<string>? Message;

    public Regie(Donnees d, Bibles b)
    {
        donnees = d;
        Bibles = b;
        var r = d.Reglages;
        Etat = new EtatEcran { Theme = r.Theme, Taille = r.Taille, Presentation = r.Presentation };
        Bibles.Change += Rafraichir;
    }

    public Donnees Donnees => donnees;
    Bible B => Bibles.Principale;
    bool muet;
    void Signaler()
    {
        if (muet) return;
        LacherDuoLibre();
        Change?.Invoke();
    }
    public void Dire(string m) { Journal.Info("message : " + m); Message?.Invoke(m); }

    // ------------------------------------------------------------ réglages de l'écran
    public void Theme(string t)
    {
        donnees.Reglages.Theme = t;
        donnees.SauverReglages();
        Etat = Etat with { Theme = t };
        Signaler();
    }
    public void Taille(double t)
    {
        t = Math.Clamp(Math.Round(t, 2), 0.5, 1.6);
        donnees.Reglages.Taille = t;
        donnees.SauverReglages();
        Etat = Etat with { Taille = t };
        Signaler();
    }
    public void Presentation(string p)
    {
        donnees.Reglages.Presentation = p;
        donnees.SauverReglages();
        Etat = Etat with { Presentation = p };
        Signaler();
    }

    // ------------------------------------------------------------ versets
    static string Cle(int b, int c, int v) => $"{b}.{c}.{v}";

    /// <summary>Morceaux d'un verset découpé (✂), ou null s'il est en une seule diapositive.</summary>
    public IReadOnlyList<string>? Morceaux(int b, int c, int v) =>
        donnees.Decoupes.TryGetValue(Cle(b, c, v), out var p) && p.Count > 1 ? p : null;

    public void AfficherVerset(int b, int c, int v1, int v2, int part = 0)
    {
        if (!depuisAgenda) ElementCourant = null;
        var vs = B.Versets(b, c);
        int n = vs.Count;
        if (n == 0) return;
        v1 = Math.Clamp(v1, 1, n);
        v2 = Math.Max(v1, Math.Min(n, v2));
        var parts = v1 == v2 ? Morceaux(b, c, v1) : null;
        int p = parts != null ? (part == -1 ? parts.Count - 1 : Math.Clamp(part, 0, parts.Count - 1)) : 0;
        Cible = new CibleVerset(b, c, v1, v2, p, parts?.Count ?? 0);
        Oeil?.Invoke(true);
        var t2 = parts == null ? Bibles.Texte2(b, c, v1, v2) : "";
        Etat = Etat with
        {
            Mode = "verset", Media = null,
            Lignes = new[] { parts != null ? parts[p] : B.Texte(b, c, v1, v2) },
            Ref = Livres.Libelle(b, c, v1, v2) + $" ({Bibles.Etiquette()})",
            Second = t2.Length > 0 ? new EtatEcran.TexteSecond(t2) : null,
        };
        NoterAffichage();
        Signaler();
    }

    public void AfficherVerset(Ref r, int part = 0) => AfficherVerset(r.B, r.C, r.V1, r.V2 > 0 ? r.V2 : r.V1, part);

    // ------------------------------------------------------------ chants
    public List<Chant> Chants => donnees.Chants;
    public Chant? Trouver(string? id) => id == null ? null : donnees.Chants.FirstOrDefault(c => c.Id == id);

    public void AfficherSection(string id, int i)
    {
        var c = Trouver(id);
        if (c == null || i < 0 || i >= c.Sections.Count) return;
        if (!depuisAgenda) ElementCourant = null;
        Cible = new CibleChant(id, i);
        Oeil?.Invoke(true);
        Etat = Etat with
        {
            Mode = "chant", Media = null,
            Lignes = c.Sections[i].Text.Split('\n').Where(l => l.Trim().Length > 0).ToArray(),
            Ref = "",
            Second = null,
        };
        NoterAffichage();
        Signaler();
    }

    // ------------------------------------------------------------ Duo
    /// <summary>Épingle ce qui est à l'écran (il part dans le coin) ; l'écran principal se libère pour autre chose.</summary>
    public bool Epingler(string? libelle = null)
    {
        if (Etat.Vide) { Dire("Affichez d'abord ce que vous voulez épingler."); return false; }
        var e = Etat with { Duo = null, Presentation = "plein" };
        if (string.IsNullOrWhiteSpace(libelle))
            libelle = e.Ref.Length > 0 ? e.Ref : e.Lignes.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "Source";
        var r = donnees.Reglages;
        Etat = Etat with { Duo = new DuoEcran(e, libelle.Trim(), PlaceValide(r.DuoPlace), PartValide(r.DuoPart)) };
        Effacer();
        return true;
    }

    /// <summary>Retire le contenu épinglé ; l'écran principal ne change pas.</summary>
    public void Desepingler()
    {
        if (Etat.Duo == null) return;
        Etat = Etat with { Duo = null };
        if (Etat.Vide) Oeil?.Invoke(false);
        Signaler();
    }

    static string PlaceValide(string? place) => place is "hd" or "hg" or "bd" or "bg" or "cote" ? place : "hd";
    static double PartValide(double part) => double.IsNaN(part) ? 0.35 : Math.Clamp(Math.Round(part, 2), 0.2, 0.5);

    /// <summary>Place du contenu épinglé (hd, hg, bd, bg ou cote) et sa part de l'écran (0,2 à 0,5). Gardé pour la prochaine fois.</summary>
    public void PlaceDuo(string place, double part)
    {
        var r = donnees.Reglages;
        r.DuoPlace = PlaceValide(place);
        r.DuoPart = PartValide(part);
        donnees.SauverReglages();
        if (Etat.Duo is { } d && d.Place != "libre") { Etat = Etat with { Duo = d with { Place = r.DuoPlace, Part = r.DuoPart } }; Signaler(); }
    }

    // ------------------------------------------------------------ retirer, suivant, précédent
    /// <summary>Vide l'écran principal. Le contenu épinglé (Duo) reste : « Retirer le duo » l'enlève.</summary>
    public void Effacer()
    {
        // avec un Duo, il reste quelque chose à montrer : la source OBS reste visible
        // un dossier Duo part en entier ; un contenu épinglé reste
        var garde = Etat.Duo is { Place: "libre" } ? null : Etat.Duo;
        Oeil?.Invoke(garde != null);
        Etat = Etat with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null, Duo = garde };
        Cible = null;
        ElementCourant = null;
        Signaler();
    }

    public void Bouger(int d)
    {
        if (BougerAgenda(d) || BougerAnnonce(d)) return;
        if (Cible is CibleChant cc)
        {
            var c = Trouver(cc.Id);
            if (c != null && cc.I + d >= 0 && cc.I + d < c.Sections.Count) AfficherSection(cc.Id, cc.I + d);
            return;
        }
        if (Cible is CibleVerset cv)
        {
            // d'abord les morceaux d'un verset découpé
            if (cv.Parts > 1 && cv.Part + d >= 0 && cv.Part + d < cv.Parts)
            {
                AfficherVerset(cv.B, cv.C, cv.V1, cv.V2, cv.Part + d);
                return;
            }
            int n = B.Versets(cv.B, cv.C).Count;
            int v = d > 0 ? cv.V2 + 1 : cv.V1 - 1, ch = cv.C;
            if (v > n && ch < B.NombreChapitres(cv.B)) { ch++; v = 1; }
            if (v < 1 && ch > 1) { ch--; v = B.Versets(cv.B, ch).Count; }
            if (v >= 1 && v <= B.Versets(cv.B, ch).Count)
            {
                Selection = new Ref(cv.B, ch, v, v);
                AfficherVerset(cv.B, ch, v, v, d < 0 ? -1 : 0);
            }
            return;
        }
        if (OngletBible && Selection is Ref s && s.V1 > 0) AfficherVerset(s);
    }

    public Suivante? Prochaine()
    {
        var (agenda, sa) = ProchaineAgenda();
        if (agenda) return sa;
        if (Cible is CibleTexte) return ProchaineAnnonce();
        if (Cible is CibleChant cc)
        {
            var c = Trouver(cc.Id);
            if (c == null || cc.I + 1 >= c.Sections.Count) return null;
            var s = c.Sections[cc.I + 1];
            return new Suivante(new CibleChant(cc.Id, cc.I + 1), s.Label.Length > 0 ? s.Label : "Strophe", s.Text);
        }
        if (Cible is CibleVerset cv)
        {
            if (cv.Parts > 1 && cv.Part + 1 < cv.Parts)
            {
                var m = Morceaux(cv.B, cv.C, cv.V1)!;
                return new Suivante(new CibleVerset(cv.B, cv.C, cv.V1, cv.V1, cv.Part + 1, cv.Parts), $"{Livres.Libelle(cv.B, cv.C, cv.V1)} · morceau {cv.Part + 2}/{cv.Parts}", m[cv.Part + 1]);
            }
            int ch = cv.C, v = cv.V2 + 1;
            if (v > B.Versets(cv.B, ch).Count)
            {
                if (ch < B.NombreChapitres(cv.B)) { ch++; v = 1; }
                else return null;
            }
            var vs = B.Versets(cv.B, ch);
            if (v > vs.Count) return null;
            var p = Morceaux(cv.B, ch, v);
            return new Suivante(new CibleVerset(cv.B, ch, v, v, 0, p?.Count ?? 0), Livres.Libelle(cv.B, ch, v) + (p != null ? $" · morceau 1/{p.Count}" : ""), p != null ? p[0] : vs[v - 1]);
        }
        return null;
    }

    /// <summary>Nouvelle version de la Bible : le verset à l'écran est réaffiché.</summary>
    public void Rafraichir()
    {
        if (Cible is CibleVerset cv)
        {
            bool avant = depuisAgenda;
            depuisAgenda = ElementCourant != null;
            try { AfficherVerset(cv.B, cv.C, cv.V1, cv.V2, cv.Part); }
            finally { depuisAgenda = avant; }
        }
        else Signaler();
    }

    // ------------------------------------------------------------ ✂ découper
    /// <summary>Texte à découper : une ligne par diapositive pour un verset, une ligne vide entre diapositives pour une strophe.</summary>
    public static List<string> MorceauxTexte(string t, bool verset)
    {
        t = t.Replace("\r\n", "\n");
        if (verset) return t.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return Regex.Split(t, @"\n\s*\n").Select(b => string.Join("\n", b.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))).Where(b => b.Length > 0).ToList();
    }

    public string TexteADecouper(Cible cible)
    {
        if (cible is CibleVerset v)
        {
            var p = Morceaux(v.B, v.C, v.V1);
            if (p != null) return string.Join("\n", p);
            var vs = B.Versets(v.B, v.C);
            return v.V1 - 1 < vs.Count ? vs[v.V1 - 1] : "";
        }
        if (cible is CibleChant c && Trouver(c.Id) is { } ch && c.I < ch.Sections.Count) return ch.Sections[c.I].Text;
        return "";
    }

    /// <summary>Enregistre le découpage d'un verset (lignes) ou d'une strophe (blocs). Renvoie le message pour l'utilisateur.</summary>
    public string Decouper(Cible cible, string texte)
    {
        if (cible is CibleVerset v)
        {
            var parts = MorceauxTexte(texte, true);
            if (parts.Count == 0) return "";
            var k = Cle(v.B, v.C, v.V1);
            if (parts.Count > 1) donnees.Decoupes[k] = parts; else donnees.Decoupes.Remove(k);
            donnees.SauverDecoupes();
            // le verset à l'écran est mis à jour tout de suite
            if (Cible is CibleVerset a && a.B == v.B && a.C == v.C && a.V1 == v.V1 && a.V2 == v.V1) AfficherVerset(v.B, v.C, v.V1, v.V1, 0);
            else Signaler();
            return parts.Count > 1 ? $"{Livres.Libelle(v.B, v.C, v.V1)} : {parts.Count} diapositives." : "Verset remis en une seule diapositive.";
        }
        if (cible is CibleChant cc && Trouver(cc.Id) is { } ch && cc.I < ch.Sections.Count)
        {
            var parts = MorceauxTexte(texte, false);
            if (parts.Count == 0) return "";
            var label = ch.Sections[cc.I].Label.Length > 0 ? ch.Sections[cc.I].Label : "Strophe";
            var b = Regex.Replace(label, @"\s*\(\d+/\d+\)$", "");
            var nouvelles = parts.Select((p, j) => new Section { Label = parts.Count > 1 ? $"{b} ({j + 1}/{parts.Count})" : b, Text = p }).ToList();
            ch.Sections.RemoveAt(cc.I);
            ch.Sections.InsertRange(cc.I, nouvelles);
            donnees.SauverChants();
            ChantsChange?.Invoke();
            if (Cible is CibleChant a && a.Id == cc.Id && a.I == cc.I) AfficherSection(cc.Id, cc.I); else Signaler();
            return $"{label} : {parts.Count} diapositive{(parts.Count > 1 ? "s" : "")}.";
        }
        return "";
    }

    // ------------------------------------------------------------ chants enregistrés
    static readonly CompareInfo Fr = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    /// <summary>Comparaison « naturelle » : « 2. Titre » avant « 10. Titre », sans tenir compte des accents ni des majuscules.</summary>
    public static int Comparer(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int fi = i, fj = j;
                while (fi < a.Length && char.IsDigit(a[fi])) fi++;
                while (fj < b.Length && char.IsDigit(b[fj])) fj++;
                var na = a[i..fi].TrimStart('0'); var nb = b[j..fj].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
                i = fi; j = fj;
                continue;
            }
            int fa = i, fb = j;
            while (fa < a.Length && !char.IsDigit(a[fa])) fa++;
            while (fb < b.Length && !char.IsDigit(b[fb])) fb++;
            int r = Fr.Compare(a[i..fa], b[j..fb], CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (r != 0) return r;
            i = fa; j = fb;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Recueils (« author ») et nombre de chants, triés.</summary>
    public List<(string Nom, int N)> Recueils() =>
        Chants.GroupBy(c => c.Author ?? "")
            .Select(g => (g.Key, g.Count())).OrderBy(x => x.Key.Length == 0 ? 1 : 0).ThenBy(x => x.Key, Comparer<string>.Create(Comparer)).ToList();

    /// <summary>Supprime un recueil et tous ses chants. Renvoie le nombre de chants supprimés.</summary>
    public int SupprimerRecueil(string nom)
    {
        int n = donnees.Chants.RemoveAll(c => (c.Author ?? "") == nom);
        if (n == 0) return 0;
        donnees.SauverChants();
        ChantsChange?.Invoke();
        if (Cible is CibleChant cc && Trouver(cc.Id) == null) Signaler();
        Journal.Info($"recueil supprimé : {nom} ({n} chants)");
        return n;
    }

    /// <summary>Ajoute des chants lus d'un dossier ou d'une archive, chacun dans son recueil.</summary>
    public int AjouterChantsRecueils(IEnumerable<ChantsFormats.ChantRecueil> liste, string? forcer = null)
    {
        int n = 0;
        foreach (var x in liste)
        {
            var c = x.Chant;
            c.Id = Chant.NouvelId();
            c.Author = !string.IsNullOrWhiteSpace(forcer) ? forcer.Trim() : x.Recueil;
            if (string.IsNullOrWhiteSpace(c.Title)) c.Title = "Sans titre";
            donnees.Chants.Add(c);
            n++;
        }
        if (n > 0) { donnees.SauverChants(); ChantsChange?.Invoke(); }
        return n;
    }

    /// <summary>Chants du recueil choisi (null = tous ; "" = sans recueil) contenant la recherche, triés.
    /// Une recherche « 12 » met d'abord les chants n° 12.</summary>
    public List<Chant> Filtrer(string? q, string? recueil)
    {
        var nq = Texte.Norm((q ?? "").Trim());
        var res = Chants.Where(c => (recueil == null || (c.Author ?? "") == recueil) &&
                                    (nq.Length == 0 || Texte.Norm(c.Title + " " + c.Author + " " + string.Join(" ", c.Sections.Select(s => s.Text))).Contains(nq)))
            .OrderBy(c => c.Author ?? "", Comparer<string>.Create(Comparer)).ThenBy(c => c.Title, Comparer<string>.Create(Comparer)).ToList();
        if (Regex.IsMatch((q ?? "").Trim(), @"^\d+$") && int.TryParse(q!.Trim(), out var n))
            res = res.OrderBy(c => Numero(c.Title) == n ? 0 : 1).ToList();
        return res;
    }

    /// <summary>Numéro d'un chant d'après le début de son titre (« 12 - … », « 12. … », « N° 12 », « #12 »).</summary>
    public static int? Numero(string? titre)
    {
        var m = Regex.Match(titre ?? "", @"^\s*(?:#|n[°º]|no\.?)?\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    /// <summary>Chants portant exactement ce numéro, dans le recueil choisi (null = tous).</summary>
    public List<Chant> ParNumero(string? numero, string? recueil)
    {
        if (!int.TryParse((numero ?? "").Trim(), out var n)) return new();
        return Chants.Where(c => (recueil == null || (c.Author ?? "") == recueil) && Numero(c.Title) == n)
            .OrderBy(c => c.Author ?? "", Comparer<string>.Create(Comparer)).ThenBy(c => c.Title, Comparer<string>.Create(Comparer)).ToList();
    }

    /// <summary>Recherche pour le téléphone : numéro (« 12 ») ou mots du titre, puis des paroles.</summary>
    public List<Chant> ChercherChants(string? q)
    {
        q = (q ?? "").Trim();
        List<Chant> res;
        if (Regex.IsMatch(q, @"^\d+$"))
        {
            res = int.TryParse(q, out var n) ? Chants.Where(c => Numero(c.Title) == n).ToList() : new();
        }
        else
        {
            var nq = Texte.Norm(q);
            res = nq.Length == 0 ? Chants.ToList() : Chants.Where(c => Texte.Norm(c.Title).Contains(nq)).ToList();
            if (nq.Length > 0 && res.Count < 50)
            {
                var ids = res.Select(c => c.Id).ToHashSet();
                res.AddRange(Chants.Where(c => !ids.Contains(c.Id) && Texte.Norm(string.Join(" ", c.Sections.Select(s => s.Text))).Contains(nq)));
            }
        }
        return res.Take(60).ToList();
    }

    /// <summary>Ajoute des chants venant d'un autre MyVerse. Un chant déjà là (même identifiant, ou même recueil
    /// et même titre) est remplacé par celui du fichier ; s'il est identique, rien ne change.</summary>
    public (int ajoutes, int misAJour, int pareils) FusionnerChants(IEnumerable<Chant> liste)
    {
        int aj = 0, maj = 0, pareils = 0;
        var parId = new Dictionary<string, Chant>();
        var parCle = new Dictionary<string, List<Chant>>();
        foreach (var c in donnees.Chants)
        {
            parId.TryAdd(c.Id ?? "", c);
            var k = Cle(c);
            if (!parCle.TryGetValue(k, out var l)) parCle[k] = l = new();
            l.Add(c);
        }
        var pris = new HashSet<Chant>(); // chaque chant existant ne sert qu'une fois (titres en double dans un recueil)
        foreach (var n in liste)
        {
            if (n == null) continue;
            n.Author = (n.Author ?? "").Trim();
            n.Title = Texte.Espaces.Replace(n.Title ?? "", " ").Trim();
            if (n.Title.Length == 0) n.Title = "Sans titre";
            n.Sections ??= new();
            Chant? ex = null;
            if (!string.IsNullOrEmpty(n.Id) && parId.TryGetValue(n.Id, out var a) && !pris.Contains(a) && Cle(a) == Cle(n)) ex = a;
            else if (parCle.TryGetValue(Cle(n), out var l))
            {
                var libres = l.Where(x => !pris.Contains(x)).ToList();
                ex = libres.FirstOrDefault(x => Pareil(x, n)) ?? (libres.Count == 1 ? libres[0] : null);
            }
            if (ex == null)
            {
                if (string.IsNullOrEmpty(n.Id) || parId.ContainsKey(n.Id)) n.Id = Chant.NouvelId();
                donnees.Chants.Add(n);
                parId[n.Id] = n;
                pris.Add(n);
                aj++;
            }
            else
            {
                pris.Add(ex);
                if (Pareil(ex, n)) pareils++;
                else { ex.Title = n.Title; ex.Author = n.Author; ex.Sections = n.Sections; ex.Copyright = n.Copyright; ex.Ccli = n.Ccli; maj++; }
            }
        }
        if (aj + maj > 0) { donnees.SauverChants(); ChantsChange?.Invoke(); }
        return (aj, maj, pareils);

        static string Cle(Chant c) => Texte.Norm(Texte.Espaces.Replace(c.Author ?? "", " ").Trim()) + "\n" + Texte.Norm(Texte.Espaces.Replace(c.Title ?? "", " ").Trim());
        static bool Pareil(Chant x, Chant y) => x.Title == y.Title && (x.Author ?? "") == (y.Author ?? "") && x.Sections.Count == y.Sections.Count &&
            x.Sections.Zip(y.Sections).All(p => p.First.Label == p.Second.Label && p.First.Text == p.Second.Text);
    }

    public int AjouterChants(IEnumerable<Chant> liste, string? recueil)
    {
        int n = 0;
        recueil = (recueil ?? "").Trim();
        foreach (var s in liste)
        {
            s.Id = Chant.NouvelId();
            if (recueil.Length > 0) s.Author = recueil;
            s.Author ??= "";
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "Sans titre";
            donnees.Chants.Add(s);
            n++;
        }
        if (n > 0) { donnees.SauverChants(); ChantsChange?.Invoke(); }
        return n;
    }

    /// <summary>Enregistre un chant de l'éditeur (nouveau si id est null). Renvoie son identifiant.</summary>
    public string EnregistrerChant(string? id, string titre, List<Section> sections, string? recueil = null)
    {
        var c = Trouver(id);
        if (c == null)
        {
            c = new Chant { Author = (recueil ?? "").Trim() };
            donnees.Chants.Add(c);
        }
        c.Title = titre.Trim().Length > 0 ? titre.Trim() : "Sans titre";
        c.Sections = sections;
        donnees.SauverChants();
        ChantsChange?.Invoke();
        if (Cible is CibleChant cc && cc.Id == c.Id)
        {
            if (cc.I < c.Sections.Count) AfficherSection(c.Id, cc.I); else Signaler();
        }
        return c.Id;
    }

    public void SupprimerChant(string id)
    {
        var c = Trouver(id);
        if (c == null) return;
        donnees.Chants.Remove(c);
        donnees.SauverChants();
        ChantsChange?.Invoke();
        if (Cible is CibleChant cc && cc.Id == id) Signaler();
    }

    // ------------------------------------------------------------ télécommande
    /// <summary>Référence tapée sur le téléphone → versets (chapitre entier si aucun verset).</summary>
    public (Ref R, bool Entier)? LireRef(string? txt)
    {
        var r = Livres.Lire(txt ?? "");
        if (r == null) return null;
        var a = B.Ajuster(r.Value);
        var vs = B.Versets(a.B, a.C);
        int n = vs.Count;
        if (n == 0) return null;
        int v1 = a.V1 > 0 ? Math.Min(a.V1, n) : 1;
        int v2 = a.V1 > 0 ? Math.Min(Math.Max(a.V2, v1), n) : n;
        return (new Ref(a.B, a.C, v1, v2), a.V1 == 0);
    }

    public Apercu? ApercuRef(string? txt)
    {
        if (LireRef(txt) is not { } x) return null;
        var (r, entier) = x;
        var vs = B.Versets(r.B, r.C);
        var versets = new List<(int, string)>();
        for (int v = r.V1; v <= r.V2; v++) versets.Add((v, vs[v - 1]));
        return new Apercu(Livres.Libelle(r.B, r.C, entier ? 0 : r.V1, r.V2) + $" ({Bibles.Etiquette()})",
            new List<string> { B.Texte(r.B, r.C, r.V1, r.V2) }, versets, Livres.Nom(r.B), r.C);
    }

    /// <summary>Affiche une référence (chapitre entier → son premier verset). Faux si elle n'existe pas.</summary>
    public bool AfficherRef(string? txt)
    {
        if (LireRef(txt) is not { } x) return false;
        var (r, entier) = x;
        var sel = new Ref(r.B, r.C, r.V1, entier ? r.V1 : r.V2);
        Selection = sel;
        AfficherVerset(sel);
        return true;
    }
}
