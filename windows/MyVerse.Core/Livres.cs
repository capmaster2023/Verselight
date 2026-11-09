using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Une référence : livre (0 = Genèse), chapitre, versets v1 à v2 (v1 = 0 : chapitre entier).</summary>
public readonly record struct Ref(int B, int C, int V1, int V2);

/// <summary>Les 66 livres (noms français de la Louis Segond), abréviations et lecture des références tapées.</summary>
public static class Livres
{
    static readonly (string Nom, string Abrev)[] DEF =
    {
        ("Genèse", "gn gen ge genese"), ("Exode", "ex exo exod exode"), ("Lévitique", "lv lev levit levitique"),
        ("Nombres", "nb nom nomb nombres"), ("Deutéronome", "dt deut deuteronome"), ("Josué", "jos josue"),
        ("Juges", "jg jug juges"), ("Ruth", "rt ru ruth"), ("1 Samuel", "1s 1sa 1sam 1samuel"), ("2 Samuel", "2s 2sa 2sam 2samuel"),
        ("1 Rois", "1r 1ro 1rois"), ("2 Rois", "2r 2ro 2rois"), ("1 Chroniques", "1ch 1chr 1chron 1chroniques"),
        ("2 Chroniques", "2ch 2chr 2chron 2chroniques"), ("Esdras", "esd esdr esdras"), ("Néhémie", "ne neh nehemie"),
        ("Esther", "est esth esther"), ("Job", "jb job"), ("Psaumes", "ps psa psau psaume psaumes"),
        ("Proverbes", "pr pro prov proverbe proverbes"), ("Ecclésiaste", "ec ecc eccl ecclesiaste qo"),
        ("Cantique des cantiques", "ct ca cant cantique cantiques cantiquedescantiques"), ("Ésaïe", "es esa esaie is isaie"),
        ("Jérémie", "jr jer jerem jeremie"), ("Lamentations", "lm lam lament lamentations"), ("Ézéchiel", "ez eze ezech ezechiel"),
        ("Daniel", "dn da dan daniel"), ("Osée", "os osee"), ("Joël", "jl joe joel"), ("Amos", "am amo amos"),
        ("Abdias", "ab abd abdias"), ("Jonas", "jon jonas"), ("Michée", "mi mic michee"), ("Nahum", "na nah nahum"),
        ("Habacuc", "ha hab habacuc"), ("Sophonie", "so sop soph sophonie"), ("Aggée", "ag agg aggee"),
        ("Zacharie", "za zac zach zacharie"), ("Malachie", "ml mal malachie"), ("Matthieu", "mt mat matt matthieu"),
        ("Marc", "mc mr marc"), ("Luc", "lc lu luc"), ("Jean", "jn jea jean"), ("Actes", "ac act actes"),
        ("Romains", "rm ro rom romains"), ("1 Corinthiens", "1co 1cor 1corinthiens"), ("2 Corinthiens", "2co 2cor 2corinthiens"),
        ("Galates", "ga gal galates"), ("Éphésiens", "ep eph ephes ephesiens"), ("Philippiens", "ph phi phil philip philippiens"),
        ("Colossiens", "col colos colossiens"), ("1 Thessaloniciens", "1th 1thes 1thess 1thessaloniciens"),
        ("2 Thessaloniciens", "2th 2thes 2thess 2thessaloniciens"), ("1 Timothée", "1tm 1ti 1tim 1timothee"),
        ("2 Timothée", "2tm 2ti 2tim 2timothee"), ("Tite", "tt tit tite"), ("Philémon", "phm phl philem philemon"),
        ("Hébreux", "he heb hebr hebreux"), ("Jacques", "jc jq jac jacq jacques"), ("1 Pierre", "1p 1pi 1pie 1pierre"),
        ("2 Pierre", "2p 2pi 2pie 2pierre"), ("1 Jean", "1jn 1j 1jean"), ("2 Jean", "2jn 2j 2jean"), ("3 Jean", "3jn 3j 3jean"),
        ("Jude", "jud jude"), ("Apocalypse", "ap apo apoc apocalypse"),
    };

    /// <summary>Noms courts pour la grille des livres (nom complet au survol).</summary>
    public static readonly string[] Courts =
    {
        "Genèse", "Exode", "Lévitique", "Nombres", "Deutér.", "Josué", "Juges", "Ruth", "1 Samuel", "2 Samuel", "1 Rois", "2 Rois",
        "1 Chron.", "2 Chron.", "Esdras", "Néhémie", "Esther", "Job", "Psaumes", "Proverbes", "Ecclés.", "Cantique", "Ésaïe", "Jérémie",
        "Lament.", "Ézéchiel", "Daniel", "Osée", "Joël", "Amos", "Abdias", "Jonas", "Michée", "Nahum", "Habacuc", "Sophonie", "Aggée",
        "Zacharie", "Malachie", "Matthieu", "Marc", "Luc", "Jean", "Actes", "Romains", "1 Cor.", "2 Cor.", "Galates", "Éphés.", "Philipp.",
        "Coloss.", "1 Thess.", "2 Thess.", "1 Tim.", "2 Tim.", "Tite", "Philémon", "Hébreux", "Jacques", "1 Pierre", "2 Pierre", "1 Jean",
        "2 Jean", "3 Jean", "Jude", "Apoc.",
    };

    public const int Nombre = 66;
    public static string Nom(int b) => b >= 0 && b < DEF.Length ? DEF[b].Nom : "";

    static readonly Dictionary<string, int> Alias = new();
    static readonly List<(string Compact, int B)> Complets = new();

    static Livres()
    {
        for (int i = 0; i < DEF.Length; i++)
        {
            foreach (var a in DEF[i].Abrev.Split(' ')) Alias[a] = i;
            var compact = Regex.Replace(Texte.Norm(DEF[i].Nom), "[^a-z0-9]", "");
            Alias[compact] = i;
            Complets.Add((compact, i));
        }
    }

    /// <summary>« Jean 3:16 », « Jean 3:16-18 », « Jean 3 ».</summary>
    public static string Libelle(int b, int c, int v1 = 0, int v2 = 0)
    {
        var s = $"{Nom(b)} {c}";
        if (v1 > 0) s += $":{v1}" + (v2 > 0 && v2 != v1 ? $"-{v2}" : "");
        return s;
    }

    static readonly Regex R_REF = new(@"^([123])?\s*([a-z][a-z ]*?)\s*(?:(\d{1,3})(?:\s*[:.,]\s*|\s+)?(\d{1,3})?(?:\s*[-–à]\s*(\d{1,3}))?)?$", RegexOptions.Compiled);

    /// <summary>
    /// Référence tapée : « jn 3 16 », « Jean 3:16-18 », « 1 co 13 », « ps 23 », « premier jean 1,9 », « Rm 8.28 ».
    /// v1 = 0 : chapitre entier. Les limites sont vérifiées par la Bible (voir Bible.Borner).
    /// </summary>
    public static Ref? Lire(string? saisie)
    {
        var s = Texte.Norm(saisie).Trim();
        if (s.Length == 0) return null;
        s = Regex.Replace(s, @"\bchap(itre)?s?\b", " ");
        s = Regex.Replace(s, @"\bversets?\b", ":");
        s = Regex.Replace(s, @"\bv(?=\s*\d)", ":");
        s = Regex.Replace(s, @"\b(premiere|premier|1ere|1re|1er)\b", "1");
        s = Regex.Replace(s, @"\b(deuxieme|seconde|second|2e|2eme)\b", "2");
        s = Regex.Replace(s, @"\b(troisieme|3e|3eme)\b", "3");
        s = Regex.Replace(s, @"^(iii)\s+(?=[a-z])", "3 ");
        s = Regex.Replace(s, @"^(ii)\s+(?=[a-z])", "2 ");
        s = Regex.Replace(s, @"^(i)\s+(?=[a-z])", "1 ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        var m = R_REF.Match(s);
        if (!m.Success) return null;
        var num = m.Groups[1].Value;
        var mot = m.Groups[2].Value.Replace(" ", "");
        var cle = num + mot;
        int b = Alias.TryGetValue(cle, out var x) ? x : -1;
        if (b < 0 && mot.Length >= 3)
        {
            var cands = Complets.Where(k => k.Compact.StartsWith(cle, StringComparison.Ordinal)).ToList();
            if (cands.Count == 1) b = cands[0].B;
            else if (cands.Count > 1 && num.Length == 0) b = cands[0].B;
            if (b < 0 && num.Length == 0)
            {
                var c2 = Complets.FirstOrDefault(k => k.Compact.StartsWith("1" + mot, StringComparison.Ordinal));
                if (c2.Compact != null) b = c2.B;
            }
        }
        if (b < 0) return null;
        int ch = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? int.Parse(m.Groups[3].Value) : 1;
        int v1 = m.Groups[4].Success && m.Groups[4].Value.Length > 0 ? int.Parse(m.Groups[4].Value) : 0;
        int v2 = m.Groups[5].Success && m.Groups[5].Value.Length > 0 ? int.Parse(m.Groups[5].Value) : v1;
        return new Ref(b, ch, v1, Math.Max(v1, v2));
    }

    /// <summary>Une référence trouvée dans un texte (notes de prédication).</summary>
    public sealed record Trouvee(Ref Ref, int Index, int Longueur, string Texte);

    static readonly Regex R_DANS = new(@"(?<![\p{L}\p{N}])(?:([123])\s*)?(\p{L}{2,20})\.?\s*(\d{1,3})(?:\s*[:.,]\s*(\d{1,3})(?:\s*[-–]\s*(\d{1,3}))?)?(?![\d:])", RegexOptions.Compiled);

    /// <summary>
    /// Références dans un texte libre : « Éphésiens 2:8-9 », « Rm 5.8 », « 2 Co 5:17 », « Psaume 23 ».
    /// Sans verset, seul le nom entier d'un livre compte (« Actes 2 », pas « Rom 8 »).
    /// </summary>
    public static List<Trouvee> Trouver(string? texte)
    {
        var res = new List<Trouvee>();
        if (string.IsNullOrWhiteSpace(texte)) return res;
        int pos = 0;
        while (pos < texte.Length)
        {
            var m = R_DANS.Match(texte, pos);
            if (!m.Success) break;
            bool ok = true;
            if (!(m.Groups[4].Success && m.Groups[4].Value.Length > 0))
            {
                var cle = m.Groups[1].Value + Regex.Replace(Texte.Norm(m.Groups[2].Value), "[^a-z0-9]", "");
                ok = Complets.Any(k => k.Compact == cle) || (Alias.ContainsKey(cle) && cle.Length - m.Groups[1].Value.Length >= 4);
            }
            if (ok && Lire(m.Value) is { } r)
            {
                res.Add(new Trouvee(r, m.Index, m.Length, m.Value.Trim()));
                pos = m.Index + m.Length;
            }
            else pos = m.Index + 1; // « et 2 Co 5:17 » : on recommence juste après, sans manger le « 2 »
        }
        return res;
    }

    /// <summary>Livres proposés pendant qu'on tape le nom.</summary>
    public static List<string> Suggestions(string saisie)
    {
        var s = Texte.Norm(saisie).Trim();
        if (!Regex.IsMatch(s, "^[123]?\\s*[a-z][a-z ]*$")) return new();
        var cle = s.Replace(" ", "");
        return Enumerable.Range(0, DEF.Length)
            .Where(i => Complets[i].Compact.StartsWith(cle, StringComparison.Ordinal) || DEF[i].Abrev.Split(' ').Any(a => a.StartsWith(cle, StringComparison.Ordinal)))
            .Select(i => DEF[i].Nom).Take(8).ToList();
    }
}
