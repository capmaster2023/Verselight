using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Importer sa propre Bible : Zefania XML, OSIS XML, USFX, JSON, ou texte « Livre 1:1 … » (toutes langues).</summary>
public static class BibleImport
{
    static readonly string[] OSIS = { "Gen", "Exod", "Lev", "Num", "Deut", "Josh", "Judg", "Ruth", "1Sam", "2Sam", "1Kgs", "2Kgs", "1Chr", "2Chr", "Ezra", "Neh", "Esth", "Job", "Ps", "Prov", "Eccl", "Song", "Isa", "Jer", "Lam", "Ezek", "Dan", "Hos", "Joel", "Amos", "Obad", "Jonah", "Mic", "Nah", "Hab", "Zeph", "Hag", "Zech", "Mal", "Matt", "Mark", "Luke", "John", "Acts", "Rom", "1Cor", "2Cor", "Gal", "Eph", "Phil", "Col", "1Thess", "2Thess", "1Tim", "2Tim", "Titus", "Phlm", "Heb", "Jas", "1Pet", "2Pet", "1John", "2John", "3John", "Jude", "Rev" };
    static readonly string[] USFM = { "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV" };
    static readonly string[] ANGLAIS = { "genesis", "exodus", "leviticus", "numbers", "deuteronomy", "joshua", "judges", "ruth", "1samuel", "2samuel", "1kings", "2kings", "1chronicles", "2chronicles", "ezra", "nehemiah", "esther", "job", "psalms", "proverbs", "ecclesiastes", "songofsolomon", "isaiah", "jeremiah", "lamentations", "ezekiel", "daniel", "hosea", "joel", "amos", "obadiah", "jonah", "micah", "nahum", "habakkuk", "zephaniah", "haggai", "zechariah", "malachi", "matthew", "mark", "luke", "john", "acts", "romans", "1corinthians", "2corinthians", "galatians", "ephesians", "philippians", "colossians", "1thessalonians", "2thessalonians", "1timothy", "2timothy", "titus", "philemon", "hebrews", "james", "1peter", "2peter", "1john", "2john", "3john", "jude", "revelation" };

    static readonly Dictionary<string, int> IdxOsis = OSIS.Select((o, i) => (o.ToLowerInvariant(), i)).ToDictionary(x => x.Item1, x => x.i);
    static readonly Dictionary<string, int> IdxUsfm = USFM.Select((o, i) => (o.ToLowerInvariant(), i)).ToDictionary(x => x.Item1, x => x.i);

    static string Compact(string? s) => Regex.Replace(Texte.Norm(s ?? ""), "[^a-z0-9]", "");

    /// <summary>Numéro du livre (0-65) d'après un nom ou un code connu (OSIS, USFM, anglais, français), sinon -1.</summary>
    public static int LivreConnu(string? nom)
    {
        var c = Compact(nom);
        if (c.Length == 0) return -1;
        var bas = (nom ?? "").Trim().ToLowerInvariant();
        if (IdxOsis.TryGetValue(bas, out var o)) return o;
        if (IdxUsfm.TryGetValue(bas, out var u)) return u;
        var en = Regex.Replace(c, "^(i{1,3})(?=[a-z])", m => m.Length.ToString());
        en = en == "psalm" ? "psalms" : en == "songofsongs" ? "songofsolomon" : en == "revelations" ? "revelation" : en;
        int i = Array.IndexOf(ANGLAIS, en);
        if (i >= 0) return i;
        var r = Livres.Lire(nom + " 1");
        if (r is Ref rr)
        {
            var fr = Compact(Livres.Nom(rr.B));
            if (fr.Length >= 4 && c.Length >= 4 && fr[..4] == c[..4]) return rr.B;
        }
        return -1;
    }

    sealed class Livre { public string Nom = ""; public List<List<string>> Ch = new(); }
    static List<Livre> Vide() => Enumerable.Range(0, 66).Select(_ => new Livre()).ToList();

    static void Poser(List<Livre> livres, int b, int c, int v, string t)
    {
        if (b < 0 || b > 65 || c < 1 || v < 1 || c > 200 || v > 250) return;
        var bk = livres[b];
        while (bk.Ch.Count < c) bk.Ch.Add(new List<string>());
        var ch = bk.Ch[c - 1];
        while (ch.Count < v) ch.Add("");
        ch[v - 1] = Texte.Espaces.Replace(t, " ").Trim();
    }

    static readonly Regex R_NOTES = new(@"<(note|NOTE|rf|f|x|DIV|div)\b[^>]*>[\s\S]*?</\1>", RegexOptions.Compiled);
    static readonly Regex R_BALISES = new("<[^>]+>", RegexOptions.Compiled);
    static string SansBalises(string s) => Texte.Entites(R_BALISES.Replace(R_NOTES.Replace(s, ""), "")).Replace('\'', '’');

    static string Attr(string tag, string a)
    {
        var m = Regex.Match(tag, @"\b" + a + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(tag, @"\b" + a + @"\s*=\s*'([^']*)'", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }
    static int Num(string s) => int.TryParse(s.Trim(), out var n) ? n : 0;

    // ------------------------------------------------------------ formats
    static (List<Livre>, string) Zefania(string x)
    {
        var livres = Vide();
        var titre = Regex.Match(x, @"<title>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
        foreach (Match mb in Regex.Matches(x, @"<BIBLEBOOK\b([^>]*)>([\s\S]*?)</BIBLEBOOK>", RegexOptions.IgnoreCase))
        {
            int b = Num(Attr(mb.Groups[1].Value, "bnumber")) - 1;
            if (b < 0 || b > 65) continue;
            livres[b].Nom = Attr(mb.Groups[1].Value, "bname");
            foreach (Match mc in Regex.Matches(mb.Groups[2].Value, @"<CHAPTER\b([^>]*)>([\s\S]*?)</CHAPTER>", RegexOptions.IgnoreCase))
            {
                int c = Num(Attr(mc.Groups[1].Value, "cnumber"));
                foreach (Match mv in Regex.Matches(mc.Groups[2].Value, @"<VERS\b([^>]*)>([\s\S]*?)</VERS>", RegexOptions.IgnoreCase))
                    Poser(livres, b, c, Num(Attr(mv.Groups[1].Value, "vnumber")), SansBalises(mv.Groups[2].Value));
            }
        }
        var nom = titre.Success ? SansBalises(titre.Groups[1].Value).Trim() : "";
        if (nom.Length == 0) nom = Attr(Regex.Match(x, @"<XMLBIBLE\b[^>]*>", RegexOptions.IgnoreCase).Value, "biblename");
        return (livres, nom);
    }

    static (List<Livre>, string) Osis(string x)
    {
        var livres = Vide();
        var titre = Regex.Match(x, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
        void ParId(string id, string t)
        {
            var m = Regex.Match(id.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "", @"^([^.]+)\.(\d+)\.(\d+)");
            if (m.Success) Poser(livres, IdxOsis.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var b) ? b : -1, Num(m.Groups[2].Value), Num(m.Groups[3].Value), SansBalises(t));
        }
        int n = 0;
        foreach (Match m in Regex.Matches(x, "<verse\\b([^>]*\\bosisID\\s*=\\s*\"[^\"]+\"[^>]*)>([\\s\\S]*?)</verse>", RegexOptions.IgnoreCase))
        {
            ParId(Attr(m.Groups[1].Value, "osisID"), m.Groups[2].Value);
            n++;
        }
        if (n == 0) // jalons <verse sID="Gen.1.1"/> … <verse eID="Gen.1.1"/>
            foreach (Match m in Regex.Matches(x, "<verse\\b[^>]*\\bsID\\s*=\\s*\"([^\"]+)\"[^>]*/>([\\s\\S]*?)<verse\\b[^>]*\\beID\\s*=\\s*\"\\1\"[^>]*/>", RegexOptions.IgnoreCase))
                ParId(m.Groups[1].Value, m.Groups[2].Value);
        return (livres, titre.Success ? SansBalises(titre.Groups[1].Value).Trim() : "");
    }

    static (List<Livre>, string) Usfx(string x)
    {
        var livres = Vide();
        foreach (Match mb in Regex.Matches(x, "<book\\b[^>]*\\bid\\s*=\\s*\"([^\"]+)\"[^>]*>([\\s\\S]*?)</book>", RegexOptions.IgnoreCase))
        {
            if (!IdxUsfm.TryGetValue(mb.Groups[1].Value.ToLowerInvariant(), out var b)) continue;
            int c = 0, v = 0;
            var buf = new System.Text.StringBuilder();
            void Fin() { if (c > 0 && v > 0 && buf.ToString().Trim().Length > 0) Poser(livres, b, c, v, SansBalises(buf.ToString())); buf.Clear(); }
            foreach (var p in Regex.Split(mb.Groups[2].Value, @"(<c\b[^>]*/>|<v\b[^>]*/>|<ve\s*/>)", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(p, @"^<c\b", RegexOptions.IgnoreCase)) { Fin(); c = Num(Attr(p, "id")); v = 0; }
                else if (Regex.IsMatch(p, @"^<v\b", RegexOptions.IgnoreCase)) { Fin(); v = Num(Attr(p, "id")); }
                else if (Regex.IsMatch(p, @"^<ve", RegexOptions.IgnoreCase)) { Fin(); v = 0; }
                else if (v > 0) buf.Append(p);
            }
            Fin();
        }
        return (livres, "");
    }

    static string? Prop(JsonElement e, params string[] noms)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in noms)
            if (e.TryGetProperty(n, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            }
        return null;
    }

    static (List<Livre>, string) Json(string texte)
    {
        var livres = Vide();
        using var doc = JsonDocument.Parse(texte, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var o = doc.RootElement;
        var titre = o.ValueKind == JsonValueKind.Object ? Prop(o, "name", "translation", "title") ?? "" : "";
        JsonElement liste = default;
        bool aListe = o.ValueKind == JsonValueKind.Array;
        if (aListe) liste = o;
        else if (o.ValueKind == JsonValueKind.Object && (o.TryGetProperty("books", out liste) || o.TryGetProperty("Books", out liste)) && liste.ValueKind == JsonValueKind.Array) aListe = true;
        if (aListe && liste.GetArrayLength() > 0)
        {
            int nb = liste.GetArrayLength(), i = 0;
            foreach (var bk in liste.EnumerateArray())
            {
                int b = bk.ValueKind == JsonValueKind.Object && bk.TryGetProperty("book_number", out var bn) && bn.ValueKind == JsonValueKind.Number
                    ? bn.GetInt32() - 1
                    : Prop(bk, "osis") is string os ? LivreConnu(os) : LivreConnu(Prop(bk, "name", "abbrev") ?? "");
                if (b < 0) b = nb == 27 ? 39 + i : i;
                if (b > 65) { i++; continue; }
                livres[b].Nom = Prop(bk, "name") ?? "";
                if (bk.ValueKind == JsonValueKind.Object && (bk.TryGetProperty("ch", out var chs) || bk.TryGetProperty("chapters", out chs)) && chs.ValueKind == JsonValueKind.Array)
                {
                    int ci = 0;
                    foreach (var ch in chs.EnumerateArray())
                    {
                        if (ch.ValueKind == JsonValueKind.Array)
                        {
                            int vi = 0;
                            foreach (var t in ch.EnumerateArray())
                            {
                                vi++;
                                Poser(livres, b, ci + 1, vi, t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : Prop(t, "text") ?? "");
                            }
                        }
                        else if (ch.ValueKind == JsonValueKind.Object && ch.TryGetProperty("verses", out var vs) && vs.ValueKind == JsonValueKind.Array)
                        {
                            int c = int.TryParse(Prop(ch, "chapter"), out var cc) ? cc : ci + 1, vi = 0;
                            foreach (var v in vs.EnumerateArray())
                            {
                                vi++;
                                Poser(livres, b, c, int.TryParse(Prop(v, "verse"), out var vv) ? vv : vi, Prop(v, "text") ?? "");
                            }
                        }
                        ci++;
                    }
                }
                i++;
            }
            return (livres, titre);
        }
        if (o.ValueKind == JsonValueKind.Object && (o.TryGetProperty("verses", out var versets) || o.TryGetProperty("Verses", out versets)) && versets.ValueKind == JsonValueKind.Array)
        {
            var ordre = new List<string>();
            foreach (var v in versets.EnumerateArray())
            {
                var nom = Prop(v, "book_name", "book", "bookname") ?? "";
                int b = v.TryGetProperty("book", out var bv) && bv.ValueKind == JsonValueKind.Number ? bv.GetInt32() - 1 : LivreConnu(nom);
                if (b < 0) { if (!ordre.Contains(nom)) ordre.Add(nom); b = ordre.IndexOf(nom); }
                Poser(livres, b, Num(Prop(v, "chapter") ?? ""), Num(Prop(v, "verse") ?? ""), Prop(v, "text") ?? "");
            }
        }
        return (livres, titre);
    }

    // texte : « Genèse 1:1 Au commencement… », « Itanguriro 1 1 … », ou « livre<TAB>chapitre<TAB>verset<TAB>texte »
    static (List<Livre>, string) Texte_(string t)
    {
        var livres = Vide();
        var ordre = new List<string>();
        string livreCourant = "";
        int Numero(string nom)
        {
            int b = LivreConnu(nom);
            if (b >= 0) return b;
            if (!ordre.Contains(nom)) ordre.Add(nom);
            return ordre.IndexOf(nom);
        }
        var lignes = t.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var vus = new HashSet<string>();
        foreach (var l in lignes)
        {
            var m0 = Regex.Match(l, @"^\s*(.+?)\s+\d+\s*[:.,\s]\s*\d+\s+\S");
            if (m0.Success) vus.Add(m0.Groups[1].Value.Trim());
        }
        bool nt = vus.Count == 27;
        int Place(string nom) => Numero(nom) + (nt && LivreConnu(nom) < 0 ? 39 : 0);
        foreach (var brut in lignes)
        {
            var l = brut.Trim();
            if (l.Length == 0) continue;
            var tabs = l.Split('\t');
            if (tabs.Length >= 4 && Regex.IsMatch(tabs[1], @"^\d+$") && Regex.IsMatch(tabs[2], @"^\d+$"))
            {
                Poser(livres, Place(tabs[0]), int.Parse(tabs[1]), int.Parse(tabs[2]), string.Join(" ", tabs.Skip(3)));
                continue;
            }
            var m = Regex.Match(l, @"^(.+?)\s+(\d+)\s*[:.,]\s*(\d+)\s+(.+)$");
            if (!m.Success) m = Regex.Match(l, @"^(\D.*?)\s+(\d+)\s+(\d+)\s+(.+)$");
            if (m.Success)
            {
                livreCourant = m.Groups[1].Value.Trim();
                Poser(livres, Place(livreCourant), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value);
                continue;
            }
            m = Regex.Match(l, @"^(\d+)\s*[:.]\s*(\d+)\s+(.+)$");
            if (m.Success && livreCourant.Length > 0)
            {
                Poser(livres, Place(livreCourant), int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value);
                continue;
            }
            if (!Regex.IsMatch(l, @"\d") && l.Length < 40) livreCourant = l; // titre de livre seul sur sa ligne
        }
        return (livres, "");
    }

    /// <summary>Lit un fichier de Bible (contenu texte déjà décodé). Lève une exception claire si rien n'est reconnu.</summary>
    public static Bible Analyser(string nomFichier, string contenu)
    {
        var s = contenu.TrimStart('\uFEFF');
        (List<Livre> livres, string titre) r;
        if (Regex.IsMatch(s, "<XMLBIBLE|<BIBLEBOOK", RegexOptions.IgnoreCase)) r = Zefania(s);
        else if (Regex.IsMatch(s, "<osis", RegexOptions.IgnoreCase)) r = Osis(s);
        else if (Regex.IsMatch(s, "<usfx", RegexOptions.IgnoreCase) || Regex.IsMatch(s, "<book\\b[^>]*id=\"GEN\"", RegexOptions.IgnoreCase)) r = Usfx(s);
        else if (Regex.IsMatch(s, @"^\s*[\[{]")) r = Json(s);
        else r = Texte_(s);
        int n = r.livres.Sum(b => b.Ch.Sum(c => c.Count(v => v.Length > 0)));
        if (n < 50) throw new InvalidDataException("aucun verset reconnu dans ce fichier (formats : Zefania XML, OSIS, USFX, JSON, texte « Livre 1:1 … »)");
        for (int i = 0; i < r.livres.Count; i++) if (string.IsNullOrEmpty(r.livres[i].Nom)) r.livres[i].Nom = Livres.Nom(i);
        var nomBase = (r.titre.Length > 0 ? r.titre : Path.GetFileNameWithoutExtension(nomFichier)).Trim();
        if (nomBase.Length > 60) nomBase = nomBase[..60];
        if (nomBase.Length == 0) nomBase = "Bible";
        var court = string.Concat(Regex.Replace(nomBase, "[^A-Za-zÀ-ÿ0-9 ]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0])).ToUpperInvariant();
        if (court.Length > 5) court = court[..5];
        if (court.Length == 0) court = "BIB";
        return new Bible
        {
            Nom = nomBase,
            Court = court,
            Livres = r.livres.Select(l => new Bible.Livre { Nom = l.Nom, Chapitres = l.Ch }).ToList(),
        };
    }
}
