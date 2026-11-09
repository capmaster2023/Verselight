using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Recherche par mots dans toutes les Bibles installées (Louis Segond, King James, Bibles importées : kirundi…).
/// Toutes les langues, fautes tolérées (lettres oubliées, inversées, en trop ; mots écrits « à l'oreille »),
/// versets classés par pertinence.
/// </summary>
public static class Recherche
{
    public sealed record Resultat(int B, int C, int V, string Texte, double Note, string Version, bool Principale);

    static readonly Regex MOT = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    public static string Norm(string s) => Texte.SansAccents(s.ToLowerInvariant()).Replace("œ", "oe").Replace("æ", "ae").Replace("ß", "ss");
    public static List<string> Mots(string s) => MOT.Matches(Norm(s)).Select(m => m.Value).ToList();

    // ------------------------------------------------------------ index (un par Bible)
    sealed class Index
    {
        public int[] Refs = Array.Empty<int>();
        public Dictionary<string, int[]> Termes = new();
        public List<string>?[] ParLongueur = Array.Empty<List<string>?>();
        public Dictionary<string, List<string>>? Sons;
        public int N => Refs.Length;
    }
    static readonly ConditionalWeakTable<Bible, Index> Indexes = new();

    static Index Indexer(Bible bible)
    {
        lock (Indexes)
        {
            if (Indexes.TryGetValue(bible, out var ix)) return ix;
            var refs = new List<int>(32000);
            var listes = new Dictionary<string, List<int>>();
            for (int b = 0; b < bible.Livres.Count; b++)
                for (int c = 0; c < bible.Livres[b].Chapitres.Count; c++)
                {
                    var vs = bible.Livres[b].Chapitres[c];
                    for (int v = 0; v < vs.Count; v++)
                    {
                        if (string.IsNullOrWhiteSpace(vs[v])) continue;
                        int id = refs.Count;
                        refs.Add(b * 1_000_000 + (c + 1) * 1000 + (v + 1));
                        foreach (var w in new HashSet<string>(Mots(vs[v])))
                        {
                            if (!listes.TryGetValue(w, out var l)) listes[w] = l = new List<int>();
                            l.Add(id);
                        }
                    }
                }
            ix = new Index { Refs = refs.ToArray() };
            int max = 0;
            foreach (var (w, l) in listes) { ix.Termes[w] = l.ToArray(); if (w.Length > max) max = w.Length; }
            ix.ParLongueur = new List<string>?[max + 1];
            foreach (var w in ix.Termes.Keys) (ix.ParLongueur[w.Length] ??= new List<string>()).Add(w);
            Indexes.AddOrUpdate(bible, ix);
            return ix;
        }
    }

    /// <summary>Prépare l'index à l'avance (la première recherche est alors instantanée).</summary>
    public static void Preparer(Bible bible) => Indexer(bible);

    // ------------------------------------------------------------ comparaison de mots
    /// <summary>Distance de Damerau-Levenshtein bornée (au-delà de max : max + 1).</summary>
    public static int Distance(string a, string b, int max)
    {
        int la = a.Length, lb = b.Length;
        if (Math.Abs(la - lb) > max) return max + 1;
        var p2 = new int[lb + 1];
        var p = new int[lb + 1];
        var c = new int[lb + 1];
        for (int j = 0; j <= lb; j++) p[j] = j;
        for (int i = 1; i <= la; i++)
        {
            c[0] = i;
            int min = i;
            char ai = a[i - 1];
            for (int j = 1; j <= lb; j++)
            {
                char bj = b[j - 1];
                int v = p[j - 1] + (ai == bj ? 0 : 1);
                if (p[j] + 1 < v) v = p[j] + 1;
                if (c[j - 1] + 1 < v) v = c[j - 1] + 1;
                if (i > 1 && j > 1 && ai == b[j - 2] && a[i - 2] == bj && p2[j - 2] + 1 < v) v = p2[j - 2] + 1;
                c[j] = v;
                if (v < min) min = v;
            }
            if (min > max) return max + 1;
            (p2, p, c) = (p, c, p2);
        }
        return p[lb];
    }

    static readonly (Regex R, string X)[] R_SON =
    {
        (new Regex("ph"), "f"), (new Regex("ch(?=[rl])"), "k"), (new Regex("ch"), "x"), (new Regex("qu|q"), "k"),
        (new Regex("c(?=[eiy])"), "s"), (new Regex("c"), "k"), (new Regex("[jg]e(?=[ao])"), "j"), (new Regex("g(?=[eiy])"), "j"),
        (new Regex("gu(?=[eiy])"), "g"), (new Regex("h"), ""), (new Regex("y"), "i"), (new Regex("w"), "v"), (new Regex(@"(.)\1+"), "$1"),
        (new Regex("eau|au"), "o"), (new Regex("oeu|oe|eu"), "e"), (new Regex("ou"), "u"),
        (new Regex("[ae]i[nm](?![aeiou])|[iu][nm](?![aeiou])"), "1"), (new Regex("[ae][nm](?![aeiou])"), "2"), (new Regex("o[nm](?![aeiou])"), "3"),
        (new Regex("ai|ei"), "e"), (new Regex("pt"), "t"),
    };
    static readonly Regex R_FIN_VERBE = new("(?<=...)(er|ez)$");
    static readonly Regex R_FIN1 = new("(?<=..)(s|x|t|d)$");
    static readonly Regex R_FIN2 = new("(?<=..)e$");

    /// <summary>Mot écrit comme il se prononce (keur → coeur, sein → saint, jeunesse → genèse).</summary>
    public static string Son(string w)
    {
        var s = R_FIN_VERBE.Replace(w, "e", 1);
        foreach (var (r, x) in R_SON) s = r.Replace(s, x);
        s = R_FIN1.Replace(s, "", 1);
        s = R_FIN2.Replace(s, "", 1);
        return R_FIN1.Replace(s, "", 1);
    }
    static bool SonUtile(string s) => s.Length >= 3 || (s.Length == 2 && s.Any(char.IsDigit));
    static int Tolerance(int n) => n <= 3 ? 0 : n <= 6 ? 1 : 2;

    /// <summary>Mots de la Bible qui correspondent à un mot tapé : terme → qualité (1 = exact).</summary>
    static Dictionary<string, double> Candidats(Index ix, string q)
    {
        var o = new Dictionary<string, double>();
        void Ajouter(string t, double s) { if (!o.TryGetValue(t, out var a) || a < s) o[t] = s; }
        int L = q.Length;
        if (ix.Termes.ContainsKey(q)) Ajouter(q, 1);
        int k = Tolerance(L);
        for (int n = Math.Max(1, L - k); n < ix.ParLongueur.Length; n++)
        {
            var liste = ix.ParLongueur[n];
            if (liste == null) continue;
            bool prefixe = L >= 3 && n > L, flou = k > 0 && n <= L + k;
            if (!prefixe && !flou) continue;
            foreach (var t in liste)
            {
                if (prefixe && t.StartsWith(q, StringComparison.Ordinal)) { Ajouter(t, n - L <= 3 ? 0.9 : 0.7); continue; }
                if (flou && t != q)
                {
                    int e = Distance(q, t, k);
                    // une faute sur la première lettre est plus rare : un peu moins de points
                    if (e <= k) Ajouter(t, (e == 1 ? 0.75 : 0.55) * (t[0] == q[0] ? 1.0 : 0.85));
                }
            }
        }
        if (L >= 4 && q.Any(ch => ch >= 'a' && ch <= 'z'))
        {
            if (ix.Sons == null)
            {
                var m = new Dictionary<string, List<string>>();
                foreach (var t in ix.Termes.Keys)
                    if (t.Length >= 3)
                    {
                        var s = Son(t);
                        if (!SonUtile(s)) continue;
                        if (!m.TryGetValue(s, out var l)) m[s] = l = new List<string>();
                        l.Add(t);
                    }
                ix.Sons = m;
            }
            var sq = Son(q);
            if (SonUtile(sq) && ix.Sons.TryGetValue(sq, out var ts)) foreach (var t in ts) Ajouter(t, 0.85);
        }
        return o;
    }

    sealed record Trouve(int B, int C, int V, string Texte, double Note);

    static List<Trouve> ChercherDans(Bible bible, string requete, HashSet<string> tousTermes)
    {
        var ix = Indexer(bible);
        var qs = Mots(requete).Distinct().Where(w => w.Length > 1 || w.Any(char.IsDigit)).ToList();
        if (qs.Count == 0 || ix.N == 0) return new();
        int n = ix.N;
        var score = new double[n];
        var groupes = new List<(double W, HashSet<string> Termes)>();
        double total = 0;
        foreach (var q in qs)
        {
            var cands = Candidats(ix, q);
            var meilleur = new double[n];
            int df = 0;
            foreach (var (t, s) in cands)
                foreach (var id in ix.Termes[t])
                {
                    if (meilleur[id] == 0) df++;
                    if (s > meilleur[id]) meilleur[id] = s;
                }
            // les mots rares (« patiente ») comptent plus que les mots courants (« et », « le »)
            double w = Math.Log(1.0 + (double)n / (df + 1));
            total += w;
            if (df > 0) for (int id = 0; id < n; id++) if (meilleur[id] > 0) score[id] += w * meilleur[id];
            groupes.Add((w, new HashSet<string>(cands.Keys)));
            foreach (var t in cands.Keys) tousTermes.Add(t);
        }
        double best = 0;
        var ids = new List<int>();
        for (int id = 0; id < n; id++) if (score[id] > 0) { ids.Add(id); if (score[id] > best) best = score[id]; }
        if (best == 0) return new();
        ids.Sort((a, b) => score[b] != score[a] ? score[b].CompareTo(score[a]) : a.CompareTo(b));
        var res = new List<Trouve>();
        foreach (var id in ids.Take(600))
        {
            if (score[id] < best * 0.6 || score[id] < total * 0.34) continue;
            int code = ix.Refs[id], b = code / 1_000_000, c = code / 1000 % 1000, v = code % 1000;
            var vs = bible.Versets(b, c);
            var texte = v - 1 < vs.Count ? vs[v - 1] : "";
            double bonus = 0;
            if (groupes.Count > 1)
            {
                // mots tapés dans le même ordre, l'un après l'autre (« tant aimé le monde ») : bonus
                var ws = Mots(texte);
                for (int g = 0; g + 1 < groupes.Count; g++)
                    for (int p = 0; p + 1 < ws.Count; p++)
                        if (groupes[g].Termes.Contains(ws[p]) && groupes[g + 1].Termes.Contains(ws[p + 1])) { bonus += 0.3 * (groupes[g].W + groupes[g + 1].W) / 2; break; }
            }
            res.Add(new Trouve(b, c, v, texte, (score[id] + bonus) / total));
        }
        return res;
    }

    /// <summary>
    /// Cherche des mots (même mal écrits) dans plusieurs Bibles ; la première est la version principale.
    /// termes : les mots de la Bible qui ont correspondu (pour les surligner).
    /// </summary>
    public static List<Resultat> Chercher(IReadOnlyList<Bible> bibles, string requete, int limite, out HashSet<string> termes)
    {
        termes = new HashSet<string>();
        var parRef = new Dictionary<int, (Resultat R, int Rang)>();
        for (int i = 0; i < bibles.Count; i++)
            foreach (var x in ChercherDans(bibles[i], requete, termes))
            {
                int cle = x.B * 1_000_000 + x.C * 1000 + x.V;
                if (!parRef.TryGetValue(cle, out var p) || x.Note > p.R.Note + 0.05)
                    parRef[cle] = (new Resultat(x.B, x.C, x.V, x.Texte, x.Note, bibles[i].Court, i == 0), i);
            }
        if (parRef.Count == 0) return new();
        double best = parRef.Values.Max(p => p.R.Note);
        return parRef.Where(kv => kv.Value.R.Note >= best * 0.6)
            .OrderByDescending(kv => kv.Value.R.Note).ThenBy(kv => kv.Value.Rang).ThenBy(kv => kv.Key)
            .Take(limite).Select(kv => kv.Value.R).ToList();
    }

    /// <summary>Découpe un texte en morceaux (texte, mot trouvé ?) pour surligner les mots trouvés.</summary>
    public static List<(string T, bool Mot)> Morceaux(string texte, HashSet<string> termes)
    {
        var o = new List<(string, bool)>();
        int i = 0;
        foreach (Match m in MOT.Matches(texte))
        {
            if (!termes.Contains(Norm(m.Value))) continue;
            if (m.Index > i) o.Add((texte[i..m.Index], false));
            o.Add((m.Value, true));
            i = m.Index + m.Length;
        }
        if (i < texte.Length) o.Add((texte[i..], false));
        return o;
    }
}
