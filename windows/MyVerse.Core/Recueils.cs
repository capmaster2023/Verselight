using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Import de recueils entiers : PDF de cantiques numérotés, texte avec plusieurs chants, pages web de chants.
/// Même logique que import-recueils.js de la 3.x.
/// </summary>
public static class Recueils
{
    /// <summary>Une ligne de texte d'un PDF : texte, hauteur des lettres, page.</summary>
    public sealed record LignePdf(string T, double H, int Page);

    /// <summary>Un morceau de texte d'une page de PDF : texte, position (x, y depuis le bas), hauteur des lettres.</summary>
    public sealed record MorceauPdf(string S, double X, double Y, double H);

    /// <summary>
    /// Regroupe les morceaux de texte de chaque page en lignes (même hauteur), de haut en bas ;
    /// un grand écart vertical donne une ligne vide (séparation de strophes).
    /// </summary>
    public static List<LignePdf> LignesDepuisPages(IEnumerable<IReadOnlyList<MorceauPdf>> pages)
    {
        var toutes = new List<LignePdf>();
        int p = 0;
        foreach (var page in pages)
        {
            p++;
            var rangs = new List<(double Y, List<(double X, string S)> M, double H)>();
            foreach (var it in page)
            {
                if (string.IsNullOrWhiteSpace(it.S)) continue;
                double y = Math.Round(it.Y), hau = it.H;
                int k = rangs.FindIndex(q => Math.Abs(q.Y - y) <= Math.Max(2, hau * 0.3));
                if (k < 0) { rangs.Add((y, new List<(double, string)>(), hau)); k = rangs.Count - 1; }
                rangs[k].M.Add((it.X, it.S));
                if (hau > rangs[k].H) rangs[k] = (rangs[k].Y, rangs[k].M, hau);
            }
            rangs.Sort((a, b) => b.Y.CompareTo(a.Y));
            double? prevY = null;
            double prevH = 0;
            foreach (var r in rangs)
            {
                var txt = Texte.Espaces.Replace(string.Join(" ", r.M.OrderBy(m => m.X).Select(m => m.S)), " ").Trim();
                if (prevY is double py && py - r.Y > Math.Max(prevH, r.H) * 1.9) toutes.Add(new LignePdf("", 0, p));
                toutes.Add(new LignePdf(txt, r.H, p));
                prevY = r.Y;
                prevH = r.H;
            }
            toutes.Add(new LignePdf("", 0, p));
        }
        return toutes;
    }

    // mots qui annoncent un refrain (français, anglais, kirundi, kinyarwanda, swahili)
    static readonly Regex REFRAIN = new(@"^(refrain|chœur|choeur|chorus|inyishu|inyikirizo|ikigarukirwamwo|gusubiramwo|kiitikio|pambio|korasi)\b[\s.:–-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Découpe le texte d'un chant en strophes : « 1. … », « 2) … », refrain, lignes vides.</summary>
    public static List<Section> Strophes(IEnumerable<string> lignes)
    {
        var o = new List<Section>();
        (string Label, List<string> L)? cur = null;
        void Pousser()
        {
            if (cur is { } c && c.L.Any(x => x.Trim().Length > 0))
                o.Add(new Section { Label = c.Label, Text = string.Join("\n", c.L.Select(x => x.Trim()).Where(x => x.Length > 0)) });
            cur = null;
        }
        foreach (var brut in lignes)
        {
            var l = Texte.Espaces.Replace(brut, " ").Trim();
            if (l.Length == 0) { if (cur is { } c0 && c0.L.Count > 0) Pousser(); continue; }
            var m = Regex.Match(l, @"^(\d{1,2})[.)]\s*(.*)$");
            if (m.Success)
            {
                Pousser();
                cur = ("Couplet " + int.Parse(m.Groups[1].Value), m.Groups[2].Value.Length > 0 ? new List<string> { m.Groups[2].Value } : new List<string>());
                continue;
            }
            m = REFRAIN.Match(l);
            if (m.Success)
            {
                Pousser();
                var reste = l[m.Length..];
                cur = ("Refrain", reste.Length > 0 ? new List<string> { reste } : new List<string>());
                continue;
            }
            cur ??= ("Couplet", new List<string>());
            cur.Value.L.Add(l);
        }
        Pousser();
        // strophes trop longues pour l'écran : coupées en morceaux de 6 lignes
        var fin = new List<Section>();
        foreach (var s in o)
        {
            var ls = s.Text.Split('\n');
            if (ls.Length <= 8) { fin.Add(s); continue; }
            for (int i = 0; i < ls.Length; i += 6)
                fin.Add(new Section { Label = s.Label + (i > 0 ? " (suite)" : ""), Text = string.Join("\n", ls.Skip(i).Take(6)) });
        }
        return fin;
    }

    static bool Majuscules(string s, int min)
    {
        var lettres = Regex.Replace(s, "[^A-Za-zÀ-ÿ]", "");
        return lettres.Length > min && lettres == lettres.ToUpperInvariant();
    }

    static readonly Regex R_NUM_TITRE = new(@"^(\d{1,4})\s*[.)\-–:]?\s+(.{2,90})$", RegexOptions.Compiled);
    static readonly Regex R_NUM_SEUL = new(@"^(\d{1,4})[.)]?$", RegexOptions.Compiled);

    /// <summary>Chants numérotés d'un PDF (lignes déjà extraites) : « 12. TITRE » en en-tête plus grand ou en majuscules.</summary>
    public static List<Chant> ChantsDepuisLignes(IReadOnlyList<LignePdf> lignes)
    {
        var hs = lignes.Where(l => l.T.Length > 0).Select(l => l.H).OrderBy(h => h).ToList();
        double mediane = hs.Count > 0 ? hs[hs.Count / 2] : 10;
        if (mediane <= 0) mediane = 10;
        var chants = new List<(int Num, string Titre, List<string> Lignes)>();
        (int Num, string Titre, List<string> Lignes)? cur = null;
        int attendu = 1;
        foreach (var l in lignes)
        {
            var m = R_NUM_TITRE.Match(l.T);
            if (!m.Success) m = R_NUM_SEUL.Match(l.T);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value);
                var titre = m.Groups.Count > 2 ? m.Groups[2].Value.Trim() : "";
                // nouveau chant : numéro qui suit le précédent et en-tête visible (plus grand, en majuscules ou seul)
                bool entete = l.H > mediane * 1.12 || Majuscules(titre, 3) || titre.Length == 0;
                if ((n == attendu || (n > attendu && n <= attendu + 3)) && entete)
                {
                    if (cur != null) chants.Add(cur.Value);
                    cur = (n, Regex.Replace($"{n}. {titre}", @"\.\s*$", ""), new List<string>());
                    attendu = n + 1;
                    continue;
                }
            }
            if (cur is { } c)
            {
                if (Regex.IsMatch(c.Titre, @"^\d+\.?$") && l.T.Length > 0 && c.Lignes.Count == 0) { cur = (c.Num, $"{c.Num}. {l.T}", c.Lignes); continue; }
                c.Lignes.Add(l.T);
            }
        }
        if (cur != null) chants.Add(cur.Value);
        return chants.Select(c => new Chant { Title = c.Titre, Sections = Strophes(c.Lignes) }).Where(c => c.Sections.Count > 0).ToList();
    }

    /// <summary>Plusieurs chants dans un seul texte : « 12. TITRE », « 12 » seul, séparateurs (---, ***, ===) ou 2 lignes vides.</summary>
    public static List<Chant> ChantsDepuisTexte(string texte)
    {
        var lignes = texte.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var chants = new List<(string Titre, List<string> Lignes)>();
        string? titre = null;
        int num = 0;
        List<string>? cur = null;
        int vides = 0;
        int? attendu = null;
        void Fermer()
        {
            if (cur != null && cur.Any(l => l.Trim().Length > 0)) chants.Add((titre ?? "", cur));
            cur = null; titre = null; num = 0;
        }
        foreach (var brut in lignes)
        {
            var l = brut.Trim();
            if (l.Length == 0) { vides++; cur?.Add(""); continue; }
            bool sep = Regex.IsMatch(l, @"^([-*=#_~])\1{2,}$");
            var m = R_NUM_TITRE.Match(l);
            if (!m.Success) m = R_NUM_SEUL.Match(l);
            bool suite = m.Success && (attendu == null || (int.Parse(m.Groups[1].Value) >= attendu && int.Parse(m.Groups[1].Value) <= attendu + 3));
            var m2 = m.Success && m.Groups.Count > 2 ? m.Groups[2].Value : "";
            bool entete = m.Success && suite && (m2.Length == 0 || Majuscules(m2, 2) || vides >= 2 || cur == null);
            if (sep) { Fermer(); vides = 0; continue; }
            if (entete)
            {
                Fermer();
                num = int.Parse(m.Groups[1].Value);
                titre = m2.Length > 0 ? $"{m.Groups[1].Value}. {m2.Trim()}" : "";
                cur = new List<string>();
                attendu = num + 1;
            }
            else if (cur == null || vides >= 2)
            {
                Fermer();
                titre = "";
                cur = new List<string> { l };
            }
            else cur.Add(l);
            if (cur != null && string.IsNullOrEmpty(titre) && num > 0 && cur.Count == 1) { titre = $"{num}. {cur[0]}"; cur.Clear(); }
            vides = 0;
        }
        Fermer();
        return chants.Select(c =>
        {
            var r = Ranger.Ranger_(string.Join("\n", c.Lignes), titre: c.Titre);
            var t = r.Titre.Length > 0 ? r.Titre : c.Titre.Length > 0 ? c.Titre : (c.Lignes.FirstOrDefault(l => l.Trim().Length > 0) ?? "Sans titre");
            if (t.Length > 60) t = t[..60];
            return new Chant { Title = t, Sections = r.Sections };
        }).Where(c => c.Sections.Count > 0).ToList();
    }

    // ============================================================ pages web
    static readonly HashSet<string> VIDES = new() { "br", "img", "meta", "link", "input", "hr", "source", "area", "base", "col", "embed", "param", "track", "wbr" };
    static readonly HashSet<string> RETIRER = new() { "script", "style", "nav", "header", "footer", "aside", "form", "iframe", "button", "noscript", "svg", "template", "select" };
    static readonly string[] CLASSES_RETIRER = { "sharedaddy", "share", "related", "comments" };

    public static ChantsFormats.Noeud ParseHtml(string html)
    {
        html = Regex.Replace(html, @"<(script|style|noscript|svg|template)\b[\s\S]*?</\1\s*>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ");
        return ChantsFormats.ParseXml(html, VIDES);
    }

    static bool AClasse(ChantsFormats.Noeud n, string c) =>
        n.Attrs.TryGetValue("class", out var v) && v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals(c, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<ChantsFormats.Noeud> Tous(ChantsFormats.Noeud n)
    {
        foreach (var c in ChantsFormats.Elems(n)) { yield return c; foreach (var d in Tous(c)) yield return d; }
    }

    static void TexteBloc(object n, StringBuilder o)
    {
        switch (n)
        {
            case string s: o.Append(s); break;
            case ChantsFormats.Noeud x:
                if (RETIRER.Contains(x.Nom) || CLASSES_RETIRER.Any(c => AClasse(x, c)) || (x.Attrs.TryGetValue("id", out var id) && id == "comments")) return;
                if (x.Nom == "br") { o.Append('\n'); foreach (var e in x.Enfants) TexteBloc(e, o); return; }
                foreach (var e in x.Enfants) TexteBloc(e, o);
                if (x.Nom is "p" or "div" or "li" or "h2" or "h3" or "h4") o.Append("\n\n");
                break;
        }
    }

    public static string TexteBloc(ChantsFormats.Noeud bloc)
    {
        var o = new StringBuilder();
        TexteBloc(bloc, o);
        var t = o.ToString().Replace(' ', ' ');
        t = Regex.Replace(t, @"[ \t]+", " ");
        t = Regex.Replace(t, @"\n[ \t]+", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }

    /// <summary>Paroles d'une page web : titre (h1) et strophes du contenu principal.</summary>
    public static Chant? ChantDepuisPage(string html, string url)
    {
        var d = ParseHtml(html);
        var tous = Tous(d).ToList();
        var h1 = tous.FirstOrDefault(x => x.Nom == "h1" && (AClasse(x, "entry-title") || AClasse(x, "post-title")));
        if (h1 == null && tous.FirstOrDefault(x => x.Nom == "article" && ChantsFormats.Find(x, "h1") != null) is { } art) h1 = ChantsFormats.Find(art, "h1");
        h1 ??= tous.FirstOrDefault(x => x.Nom == "h1") ?? tous.FirstOrDefault(x => x.Nom == "title");
        var titre = h1 != null ? ChantsFormats.TextOf(h1) : url;
        var bloc = tous.FirstOrDefault(x => AClasse(x, "entry-content") || AClasse(x, "post-content") || AClasse(x, "song-content") || AClasse(x, "lyrics"))
                   ?? tous.FirstOrDefault(x => x.Nom == "article") ?? tous.FirstOrDefault(x => x.Nom == "main")
                   ?? tous.FirstOrDefault(x => x.Nom == "body") ?? d;
        var txt = TexteBloc(bloc);
        var sections = Strophes(txt.Split('\n'));
        if (sections.Count == 0) return null;
        return new Chant { Title = Texte.Espaces.Replace(titre.Trim(), " "), Sections = sections };
    }

    /// <summary>Liens vers les pages de chants d'une page qui en liste plusieurs (même site, sous la même adresse).</summary>
    public static List<string> LiensDeChants(string html, string url)
    {
        var d = ParseHtml(html);
        var b = new Uri(url);
        var prefixe = b.AbsolutePath.TrimEnd('/') + "/";
        var vus = new List<string>();
        foreach (var a in Tous(d).Where(x => x.Nom == "a"))
        {
            if (!a.Attrs.TryGetValue("href", out var href) || href.Length == 0) continue;
            if (!Uri.TryCreate(b, href, out var u)) continue;
            if (u.Scheme is not ("http" or "https") || u.Authority != b.Authority) continue;
            var sans = new UriBuilder(u) { Fragment = "" }.Uri;
            var p = sans.AbsolutePath;
            if (p.StartsWith(prefixe, StringComparison.Ordinal) && p.Length > prefixe.Length + 1 && !vus.Contains(sans.AbsoluteUri)) vus.Add(sans.AbsoluteUri);
        }
        return vus;
    }

    static readonly HttpClient Http = CreerHttp();
    static HttpClient CreerHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MyVerse/4");
        return h;
    }

    public static async Task<string> Telecharger(string url, CancellationToken ct = default)
    {
        using var r = await Http.GetAsync(url, ct);
        r.EnsureSuccessStatusCode();
        var octets = await r.Content.ReadAsByteArrayAsync(ct);
        var cs = r.Content.Headers.ContentType?.CharSet;
        if (cs != null)
            try { return Encoding.GetEncoding(cs.Trim('"')).GetString(octets); } catch (ArgumentException) { }
        return Texte.Decoder(octets);
    }

    /// <summary>
    /// Importe depuis une adresse : la page d'un chant, ou une page qui liste des chants (ils sont tous importés).
    /// progres : message à afficher pendant l'import.
    /// </summary>
    public static async Task<List<Chant>> DepuisWeb(string url, Action<string>? progres = null, CancellationToken ct = default)
    {
        url = url.Trim();
        if (!Regex.IsMatch(url, "^https?://", RegexOptions.IgnoreCase)) url = "https://" + url;
        progres?.Invoke("Téléchargement de la page…");
        var html = await Telecharger(url, ct);
        var liens = LiensDeChants(html, url);
        var res = new List<Chant>();
        if (liens.Count >= 3)
        {
            for (int i = 0; i < liens.Count && i < 1500; i++)
            {
                ct.ThrowIfCancellationRequested();
                progres?.Invoke($"Import des chants : {i + 1} sur {liens.Count}…");
                try { if (ChantDepuisPage(await Telecharger(liens[i], ct), liens[i]) is { } c) res.Add(c); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
            }
            return res;
        }
        if (ChantDepuisPage(html, url) is { } seul) res.Add(seul);
        return res;
    }
}
