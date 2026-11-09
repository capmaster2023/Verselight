using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Rangement automatique d'un chant collé brut : titre, refrain (marqué ou répété), couplets numérotés,
/// accords retirés, lignes trop longues coupées, diapositives équilibrées de 3 à 5 lignes.
/// </summary>
public static class Ranger
{
    public sealed record Resultat(string Titre, List<Section> Sections);

    static string Norm(string? s) => Regex.Replace(Texte.SansAccents((s ?? "").ToLowerInvariant()), "[^a-z0-9]+", " ").Trim();

    // ligne d'accords seule : « C  G  Am  F », « Do Ré Mim Sol », « | C / G | »
    static readonly Regex ACCORD = new(@"^(?:[A-G](?:#|b)?|(?:do|ré|re|mi|fa|sol|la|si)(?:#|b)?)(?:m|min|maj|dim|aug|sus|add|M)?\d{0,2}(?:sus\d|add\d|maj\d)?(?:/[A-G](?:#|b)?)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static bool LigneAccords(string l)
    {
        var t = Regex.Replace(l, @"[|/()\-.]", " ").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return t.Length > 0 && t.All(x => ACCORD.IsMatch(x)) && !Regex.IsMatch(l.Trim(), "^(la|si|do|mi|sol|fa|re|ré)$", RegexOptions.IgnoreCase);
    }

    // en-têtes de strophe : « Refrain », « R/ », « Chœur : », « Couplet 2 », « 2. », « Pont »…
    static readonly Regex ENTETE = new(@"^\s*(?:\[\s*)?(refrain|ref|r/|r\.|chœur|choeur|chorus|couplet|strophe|verse|verset|pont|bridge|pré-?refrain|pre-?chorus|intro|fin|final|coda)\s*(\d+)?\s*(?:\])?\s*[:.\-–)]?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NUMERO = new(@"^\s*(\d{1,2})\s*[.)\-–]\s+(.+)$", RegexOptions.Compiled);

    static string NomEntete(string mot, string num)
    {
        var m = mot.ToLowerInvariant();
        if (Regex.IsMatch(m, "^(refrain|ref|r/|r\\.|chœur|choeur|chorus)$")) return "Refrain";
        if (m is "pont" or "bridge") return "Pont";
        if (Regex.IsMatch(m, "^(pré-?refrain|pre-?chorus)$")) return "Pré-refrain";
        if (m == "intro") return "Intro";
        if (m is "fin" or "final" or "coda") return "Fin";
        return "Couplet" + (num.Length > 0 ? " " + num : "");
    }

    /// <summary>Coupe une ligne trop longue en deux, à la virgule (ou à l'espace) la plus proche du milieu.</summary>
    public static List<string> CouperLigne(string l, int max)
    {
        if (l.Length <= max) return new List<string> { l };
        double mil = l.Length / 2.0;
        int best = -1;
        for (int i = 0; i < l.Length; i++)
            if ((l[i] == ',' || l[i] == ';' || l[i] == ':') && i + 1 < l.Length && l[i + 1] == ' ' && (best < 0 || Math.Abs(i - mil) < Math.Abs(best - mil))) best = i + 1;
        if (best < 0 || Math.Abs(best - mil) > l.Length * 0.3)
        {
            best = -1;
            for (int i = 0; i < l.Length; i++) if (l[i] == ' ' && (best < 0 || Math.Abs(i - mil) < Math.Abs(best - mil))) best = i;
        }
        if (best <= 0) return new List<string> { l };
        var o = CouperLigne(l[..best].Trim(), max);
        o.AddRange(CouperLigne(l[best..].Trim(), max));
        return o;
    }

    /// <summary>Découpe équilibrée : n lignes → morceaux d'au plus max lignes, de tailles voisines.</summary>
    public static List<int> Tailles(int n, int max)
    {
        int k = Math.Max(1, (int)Math.Ceiling(n / (double)max));
        int b = n / k, reste = n % k;
        return Enumerable.Range(0, k).Select(i => b + (i < reste ? 1 : 0)).ToList();
    }

    sealed class Bloc { public string Label = ""; public List<string> Lignes = new(); }

    /// <summary>Range un chant brut. max : lignes par diapositive (0 = automatique, 5 au plus).</summary>
    public static Resultat Ranger_(string? brut, int max = 0, string? titre = null, int longueur = 64)
    {
        max = max > 0 ? Math.Clamp(max, 2, 8) : 5;
        var texte = (brut ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        texte = Regex.Replace(texte, "[​-‍﻿]", "").Replace('\t', ' ');
        texte = Regex.Replace(texte, @"\[[A-G](?:#|b)?[^\]\n]{0,6}\]", ""); // accords ChordPro dans les paroles
        texte = texte.Replace(' ', ' ');
        var lignes = texte.Split('\n').Select(l => l.Trim()).ToList();
        lignes = lignes.Where(l => !LigneAccords(l) || l.Trim().Length == 0).ToList();

        // titre : première ligne suivie d'une ligne vide (ou titre fourni)
        var title = (titre ?? "").Trim();
        while (lignes.Count > 0 && lignes[0].Trim().Length == 0) lignes.RemoveAt(0);
        if (title.Length == 0 && lignes.Count > 2 && lignes[0].Length <= 70 && lignes[1].Trim().Length == 0 && !ENTETE.IsMatch(lignes[0]) && !NUMERO.IsMatch(lignes[0]))
        {
            title = Regex.Replace(lignes[0], @"^[#*\s]+|[*\s]+$", "");
            lignes.RemoveAt(0);
        }

        // blocs : lignes vides et en-têtes
        var blocs = new List<Bloc>();
        Bloc? cur = null;
        void Nouveau(string label) { cur = new Bloc { Label = label }; blocs.Add(cur); }
        foreach (var l in lignes)
        {
            if (l.Trim().Length == 0) { cur = null; continue; }
            var m = ENTETE.Match(l);
            if (m.Success)
            {
                var g1 = m.Groups[1].Value;
                var debut = l.Length > g1.Length + 4 ? l[..(g1.Length + 4)] : l;
                if (m.Groups[3].Value.Length == 0 || l.Length < 40 || Regex.IsMatch(l, @"^\s*\[") || Regex.IsMatch(debut, "[:.)]"))
                {
                    Nouveau(NomEntete(g1, m.Groups[2].Value));
                    if (m.Groups[3].Value.Trim().Length > 0) cur!.Lignes.Add(m.Groups[3].Value.Trim());
                    continue;
                }
            }
            m = NUMERO.Match(l);
            if (m.Success && (cur == null || cur.Lignes.Count > 0))
            {
                Nouveau("Couplet " + m.Groups[1].Value);
                cur!.Lignes.Add(m.Groups[2].Value.Trim());
                continue;
            }
            if (cur == null) Nouveau("");
            cur!.Lignes.Add(Regex.Replace(l, @"\s{2,}", " "));
        }
        var pleins = blocs.Where(b => b.Lignes.Count > 0).ToList();

        // refrain répété sans nom : un bloc identique à un autre est le refrain
        var cles = pleins.Select(b => Norm(string.Join(" ", b.Lignes))).ToList();
        var refrain = new HashSet<string>();
        for (int i = 0; i < cles.Count; i++) if (cles[i].Length > 0 && cles.IndexOf(cles[i]) != i) refrain.Add(cles[i]);
        var refrainCle = pleins.FirstOrDefault(b => b.Label == "Refrain");
        if (refrainCle != null) refrain.Add(Norm(string.Join(" ", refrainCle.Lignes)));
        // « Refrain » seul sans paroles (reprise) : on recopie le premier refrain
        var premierRefrain = pleins.FirstOrDefault(b => b.Label == "Refrain" || refrain.Contains(Norm(string.Join(" ", b.Lignes))));
        foreach (var b in blocs) if (b.Lignes.Count == 0 && b.Label == "Refrain" && premierRefrain != null) b.Lignes = premierRefrain.Lignes.ToList();

        // noms des strophes
        int n = 0;
        var nommes = new List<(string Label, List<string> Lignes)>();
        foreach (var b in blocs.Where(b => b.Lignes.Count > 0))
        {
            var label = b.Label;
            if (refrain.Contains(Norm(string.Join(" ", b.Lignes)))) label = "Refrain";
            if (label.Length == 0) label = "Couplet " + ++n;
            else if (Regex.IsMatch(label, @"^Couplet \d+$")) n = Math.Max(n, int.Parse(label.Split(' ')[1]));
            else if (label == "Couplet") label = "Couplet " + ++n;
            nommes.Add((label, b.Lignes.SelectMany(l => CouperLigne(l, longueur)).ToList()));
        }

        // diapositives de 3 à 5 lignes
        var sections = new List<Section>();
        foreach (var (label, ls) in nommes)
        {
            var t = Tailles(ls.Count, max);
            int i = 0;
            for (int j = 0; j < t.Count; j++)
            {
                sections.Add(new Section { Label = t.Count > 1 ? $"{label} ({j + 1}/{t.Count})" : label, Text = string.Join("\n", ls.Skip(i).Take(t[j])) });
                i += t[j];
            }
        }
        return new Resultat(title, sections);
    }

    /// <summary>Texte de l'éditeur sans les lignes « [Nom] ».</summary>
    public static string SansEntetes(string t) => string.Join("\n", t.Replace("\r\n", "\n").Split('\n').Where(l => !Regex.IsMatch(l, @"^\s*\[[^\]]*\]\s*$")));
}
