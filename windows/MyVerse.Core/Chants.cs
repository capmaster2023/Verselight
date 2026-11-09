using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyVerse;

public sealed class Section
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

/// <summary>Un chant : titre, recueil (« author », comme dans la 3.x), strophes (une diapositive par strophe).</summary>
public sealed class Chant
{
    [JsonPropertyName("id")] public string Id { get; set; } = NouvelId();
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("sections")] public List<Section> Sections { get; set; } = new();
    [JsonPropertyName("copyright"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Copyright { get; set; }
    [JsonPropertyName("ccli"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ccli { get; set; }

    public static string NouvelId()
    {
        const string a = "abcdefghijklmnopqrstuvwxyz0123456789";
        var r = Random.Shared;
        return new string(Enumerable.Range(0, 8).Select(_ => a[r.Next(a.Length)]).ToArray());
    }
}

/// <summary>
/// Lecture des fichiers de chants : VideoPsalm, OpenLyrics (OpenLP), OpenSong, ChordPro, SongSelect (CCLI),
/// ProPresenter 6 et texte simple. Même logique que chants-formats.js de la 3.x.
/// </summary>
public static class ChantsFormats
{
    public const string Filtre = "Chants|*.json;*.txt;*.xml;*.vpc;*.vpsong;*.cho;*.chordpro;*.chopro;*.crd;*.pro;*.pro6;*.song;*.usr|Tous les fichiers|*.*";

    // ============================================================ outils texte
    public static string Clean(string? text) =>
        Regex.Replace(Regex.Replace((text ?? "").TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n'), "[​‌‍⁠]", ""), " {2,}", " ");

    public static string Tidy(string text)
    {
        var lines = Clean(text).Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    static string StripChordsInline(string line) => Regex.Replace(line, @"\[[^\]\n]*\]", "");

    // ============================================================ étiquettes en français
    static readonly (Regex R, string Fr)[] LABELS =
    {
        (new Regex("^(v|vs|verse|vers|couplet|strophe|cplt|cpl)$"), "Couplet"),
        (new Regex("^(c|ch|chorus|refrain|ref|r|refr)$"), "Refrain"),
        (new Regex("^(p|pc|pre-?chorus|pre-?refrain|pré-?refrain|prechorus|pré refrain|pre refrain)$"), "Pré-refrain"),
        (new Regex("^(b|br|bridge|pont)$"), "Pont"),
        (new Regex("^(e|end|ending|outro|fin|coda)$"), "Fin"),
        (new Regex("^(i|intro|introduction)$"), "Intro"),
        (new Regex("^(t|tag|final|finale)$"), "Final"),
        (new Regex("^(instrumental|interlude|inst)$"), "Interlude"),
    };

    public static string FrLabel(string? raw)
    {
        var s = Regex.Replace((raw ?? "").Trim(), @"[:：]\s*$", "");
        if (s.Length == 0) return "";
        var m = Regex.Match(s.ToLowerInvariant(), @"^([a-zéèêàçùû][a-zéèêàçùû\- ]*?)\s*(?:(\d+)\s*([a-z])?)?$");
        if (m.Success)
            foreach (var (re, fr) in LABELS)
                if (re.IsMatch(m.Groups[1].Value.Trim()))
                    return m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? $"{fr} {int.Parse(m.Groups[2].Value)}" : fr;
        return char.ToUpper(s[0]) + s[1..];
    }

    /// <summary>Numérote « Couplet » quand il y en a plusieurs sans numéro ; ôte le numéro s'il n'y a qu'un couplet 1.</summary>
    public static List<Section> FinishLabels(List<Section> sections)
    {
        var plain = sections.Where(s => s.Label == "Couplet").ToList();
        var used = new HashSet<int>(sections.Select(s => Regex.Match(s.Label, @"^Couplet (\d+)$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)));
        var distinct = new HashSet<string>(sections.Where(s => Regex.IsMatch(s.Label, @"^Couplet( \d+)?$")).Select(s => s.Label));
        if (plain.Count > 1 || (plain.Count > 0 && used.Count > 0))
        {
            int n = 0;
            foreach (var s in sections)
            {
                if (s.Label != "Couplet") continue;
                do n++; while (used.Contains(n));
                s.Label = $"Couplet {n}";
            }
        }
        else if (distinct.Count == 1 && distinct.Contains("Couplet 1"))
            foreach (var s in sections) if (s.Label == "Couplet 1") s.Label = "Couplet";
        foreach (var s in sections) if (string.IsNullOrEmpty(s.Label)) s.Label = "Couplet";
        return sections;
    }

    sealed class Brouillon
    {
        public string Title = "", Author = "", Copyright = "", Ccli = "", Book = "";
        public List<Section> Sections = new();
    }

    static Chant MakeSong(Brouillon o)
    {
        var sections = FinishLabels(o.Sections
            .Select(s => new Section { Label = FrLabel(s.Label) is { Length: > 0 } l ? l : "Couplet", Text = Tidy(s.Text) })
            .Where(s => s.Text.Length > 0).ToList());
        var c = new Chant { Title = o.Title.Trim() is { Length: > 0 } t ? t : "Sans titre", Author = o.Author.Trim(), Sections = sections };
        if (o.Copyright.Trim().Length > 0) c.Copyright = o.Copyright.Trim();
        if (o.Ccli.Trim().Length > 0) c.Ccli = o.Ccli.Trim();
        return c;
    }

    // ============================================================ petit analyseur XML tolérant
    public sealed class Noeud
    {
        public string Nom = "";
        public Dictionary<string, string> Attrs = new();
        public List<object> Enfants = new(); // Noeud ou string
    }

    static readonly Regex R_XML = new(@"<!--[\s\S]*?-->|<!\[CDATA\[([\s\S]*?)\]\]>|<\?[\s\S]*?\?>|<!DOCTYPE[^>\[]*(?:\[[\s\S]*?\])?\s*>|<\/\s*([^\s>]+)\s*>|<([^\s/>!?]+)((?:\s+[^\s=/>]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+))?)*)\s*(\/?)>", RegexOptions.Compiled);
    static readonly Regex R_ATTR = new(@"([^\s=/>]+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+)))?", RegexOptions.Compiled);
    static string Local(string n) { int i = n.IndexOf(':'); return (i >= 0 ? n[(i + 1)..] : n).ToLowerInvariant(); }

    /// <summary>Analyse XML tolérante (balises mal fermées acceptées). vides : éléments sans fin en HTML (br, img…).</summary>
    public static Noeud ParseXml(string text, IReadOnlySet<string>? vides = null)
    {
        var root = new Noeud { Nom = "#document" };
        var stack = new List<Noeud> { root };
        var src = Clean(text);
        int last = 0;
        foreach (Match m in R_XML.Matches(src))
        {
            var top = stack[^1];
            if (m.Index > last) top.Enfants.Add(Texte.Entites(src[last..m.Index]));
            last = m.Index + m.Length;
            if (m.Groups[1].Success) { top.Enfants.Add(m.Groups[1].Value); continue; }
            if (m.Groups[2].Success)
            {
                var name = Local(m.Groups[2].Value);
                for (int i = stack.Count - 1; i > 0; i--)
                    if (stack[i].Nom == name) { stack.RemoveRange(i, stack.Count - i); break; }
                continue;
            }
            if (m.Groups[3].Success)
            {
                var node = new Noeud { Nom = Local(m.Groups[3].Value) };
                foreach (Match a in R_ATTR.Matches(m.Groups[4].Value))
                    node.Attrs[Local(a.Groups[1].Value)] = Texte.Entites(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value);
                top.Enfants.Add(node);
                if (m.Groups[5].Value.Length == 0 && (vides == null || !vides.Contains(node.Nom))) stack.Add(node);
            }
        }
        if (last < src.Length) stack[^1].Enfants.Add(Texte.Entites(src[last..]));
        return root;
    }

    public static IEnumerable<Noeud> Elems(Noeud? n) => n == null ? Enumerable.Empty<Noeud>() : n.Enfants.OfType<Noeud>();
    public static List<Noeud> FindAll(Noeud? n, string name, List<Noeud>? o = null)
    {
        o ??= new List<Noeud>();
        foreach (var c in Elems(n)) { if (c.Nom == name) o.Add(c); FindAll(c, name, o); }
        return o;
    }
    public static Noeud? Find(Noeud? n, string name) => FindAll(n, name).FirstOrDefault();
    public static string TextOf(object? n) => n switch
    {
        null => "",
        string s => s,
        Noeud x => string.Concat(x.Enfants.Select(TextOf)),
        _ => "",
    };
    public static string Attr(Noeud n, string a) => n.Attrs.TryGetValue(a, out var v) ? v : "";

    // ============================================================ VideoPsalm (JSON « relâché »)
    static JsonDocument LooseJson(string text)
    {
        var t = Clean(text).TrimStart('﻿');
        var opt = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        try { return JsonDocument.Parse(t, opt); } catch (JsonException) { }
        // JSON « relâché » de VideoPsalm : clés sans guillemets, retours à la ligne dans les textes, virgules finales
        var o = new StringBuilder(t.Length + 64);
        int i = 0;
        bool inStr = false;
        char q = '"';
        while (i < t.Length)
        {
            char c = t[i];
            if (inStr)
            {
                if (c == '\\') { o.Append(c); if (i + 1 < t.Length) o.Append(t[i + 1]); i += 2; continue; }
                if (c == q) { inStr = false; o.Append('"'); i++; continue; }
                if (c == '\n') o.Append("\\n");
                else if (c == '\r') { }
                else if (c == '\t') o.Append("\\t");
                else if (c == '"') o.Append("\\\"");
                else if (c < ' ') o.Append(c == '\v' || c == '\f' ? "\\n" : " ");
                else o.Append(c);
                i++;
                continue;
            }
            if (c == '"' || c == '\'') { inStr = true; q = c; o.Append('"'); i++; continue; }
            if (char.IsAsciiLetter(c) || c == '_' || c == '$')
            {
                int j = i;
                while (j < t.Length && (char.IsAsciiLetterOrDigit(t[j]) || t[j] == '_' || t[j] == '$')) j++;
                var word = t[i..j];
                int k = j;
                while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
                if (k < t.Length && t[k] == ':') o.Append('"').Append(word).Append('"'); else o.Append(word);
                i = j;
                continue;
            }
            o.Append(c);
            i++;
        }
        return JsonDocument.Parse(Regex.Replace(o.ToString(), @",\s*([}\]])", "$1"), opt);
    }

    static string? JStr(JsonElement e, params string[] noms)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in noms)
            if (e.TryGetProperty(n, out var v))
            {
                if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s) return s;
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            }
        return null;
    }
    static bool JArr(JsonElement e, out JsonElement a, params string[] noms)
    {
        a = default;
        if (e.ValueKind != JsonValueKind.Object) return false;
        foreach (var n in noms) if (e.TryGetProperty(n, out a) && a.ValueKind == JsonValueKind.Array) return true;
        return false;
    }

    static List<Chant> ParseVideoPsalm(string text)
    {
        JsonDocument doc;
        try { doc = LooseJson(text); }
        catch (Exception) { throw new InvalidDataException("Ce fichier ne ressemble pas à un recueil VideoPsalm valide."); }
        using var _ = doc;
        var obj = doc.RootElement;
        var arr = new List<JsonElement>();
        if (JArr(obj, out var songs, "Songs", "songs", "Song")) arr.AddRange(songs.EnumerateArray());
        else if (obj.ValueKind == JsonValueKind.Array) arr.AddRange(obj.EnumerateArray());
        else arr.Add(obj);
        var book = JStr(obj, "Text", "Name") ?? "";
        var res = new List<Chant>();
        foreach (var s in arr)
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            var vs = JArr(s, out var verses, "Verses", "verses", "Lyrics", "Sections") ? verses.EnumerateArray().ToList() : new List<JsonElement>();
            int couplet = 0;
            var sections = new List<Section>();
            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i];
                var txt = JStr(v, "Text", "text") ?? "";
                string tag = v.ValueKind == JsonValueKind.Object && (v.TryGetProperty("Tag", out var tg) || v.TryGetProperty("tag", out tg)) ? tg.ToString() : "";
                string label = tag == "2" || Regex.IsMatch(txt.ToLowerInvariant(), @"^(refrain|chorus)\b") ? "Refrain" : tag == "3" ? "Pont" : "Couplet";
                if (label == "Couplet") label = $"Couplet {++couplet}";
                sections.Add(new Section { Label = label, Text = txt });
            }
            var c = MakeSong(new Brouillon
            {
                Title = JStr(s, "Text", "Title", "title", "Name") ?? "Sans titre",
                Author = JStr(s, "Author", "author") ?? "",
                Copyright = JStr(s, "Copyright", "copyright") ?? "",
                Ccli = JStr(s, "CCLI", "Ccli") ?? "",
                Book = JStr(s, "Book") ?? book,
                Sections = sections,
            });
            if (c.Sections.Count > 0) res.Add(c);
        }
        return res;
    }

    // ============================================================ OpenLyrics (OpenLP)
    static string OlLines(object n) => n switch
    {
        string s => Regex.Replace(s, @"\s*\n\s*", " "),
        Noeud x when x.Nom == "br" => "\n",
        Noeud x when x.Nom == "comment" => "",
        Noeud x when x.Nom == "line" => string.Concat(x.Enfants.Select(OlLines)) + "\n",
        Noeud x => string.Concat(x.Enfants.Select(OlLines)),
        _ => "",
    };

    static string OlName(string name)
    {
        var m = Regex.Match(name.ToLowerInvariant(), @"^([a-z]+)(\d*)([a-z]?)$");
        if (!m.Success) return FrLabel(name);
        return FrLabel(m.Groups[1].Value + (m.Groups[2].Value.Length > 0 ? " " + m.Groups[2].Value : ""));
    }

    static List<Chant> ParseOpenLyrics(string text)
    {
        var doc = ParseXml(text);
        var song = Find(doc, "song") ?? throw new InvalidDataException("Ce fichier ne ressemble pas à un chant OpenLyrics valide.");
        var props = Find(song, "properties");
        var title = TextOf(Find(props, "title")).Trim();
        var authors = FindAll(props, "author").Select(a => TextOf(a).Trim()).Where(a => a.Length > 0);
        var verses = FindAll(Find(song, "lyrics"), "verse");
        var lang = verses.Select(v => Attr(v, "lang")).FirstOrDefault(l => l.Length > 0);
        var byName = new Dictionary<string, (string Key, string Label, string Text)>();
        var ordered = new List<(string Key, string Label, string Text)>();
        foreach (var v in verses)
        {
            if (lang != null && Attr(v, "lang").Length > 0 && Attr(v, "lang") != lang) continue;
            var name = Attr(v, "name").Trim();
            var parts = FindAll(v, "lines").Select(OlLines).ToList();
            var txt = string.Join("\n", parts.Count > 0 ? parts : new List<string> { OlLines(v) });
            var sec = (name.ToLowerInvariant(), OlName(name), txt);
            byName.TryAdd(sec.Item1, sec);
            ordered.Add(sec);
        }
        var order = TextOf(Find(props, "verseorder")).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var sections = ordered;
        if (order.Length > 0)
        {
            var seq = order.Where(byName.ContainsKey).Select(k => byName[k]).ToList();
            if (seq.Count > 0) sections = seq;
        }
        var c = MakeSong(new Brouillon
        {
            Title = title.Length > 0 ? title : "Sans titre",
            Author = string.Join(", ", authors),
            Copyright = TextOf(Find(props, "copyright")).Trim(),
            Ccli = TextOf(Find(props, "cclino")).Trim(),
            Sections = sections.Select(s => new Section { Label = s.Label, Text = s.Text }).ToList(),
        });
        return c.Sections.Count > 0 ? new List<Chant> { c } : new List<Chant>();
    }

    // ============================================================ OpenSong
    static string OsName(string key)
    {
        var m = Regex.Match(key, @"^([A-Z]+)(\d*)$");
        return m.Success ? FrLabel(m.Groups[1].Value.ToLowerInvariant() + (m.Groups[2].Value.Length > 0 ? " " + m.Groups[2].Value : "")) : FrLabel(key);
    }

    static List<Chant> ParseOpenSong(string text)
    {
        var doc = ParseXml(text);
        var song = Find(doc, "song") ?? throw new InvalidDataException("Ce fichier ne ressemble pas à un chant OpenSong valide.");
        var lyrics = Clean(TextOf(Find(song, "lyrics")));
        var blocks = new List<(string Key, List<string> Lines)>();
        (string Key, List<string> Lines)? cur = null;
        foreach (var raw in lyrics.Split('\n'))
        {
            var line = raw.TrimEnd();
            var head = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
            if (head.Success) { cur = (head.Groups[1].Value.Trim().ToUpperInvariant(), new List<string>()); blocks.Add(cur.Value); continue; }
            if (line.StartsWith('.') || line.StartsWith(';')) continue; // accords, commentaires
            if (cur == null) { cur = ("V", new List<string>()); blocks.Add(cur.Value); }
            cur.Value.Lines.Add(Regex.Replace(line, "^ ", "").Replace("||", "\n").Replace("|", "\n").Replace("_", ""));
        }
        var sections = new Dictionary<string, Section>();
        var order = new List<string>();
        foreach (var b in blocks)
        {
            var numbered = b.Lines.Where(l => Regex.IsMatch(l, @"^\d\s")).ToList();
            if (numbered.Count > 0 && numbered.Count >= b.Lines.Count(l => l.Trim().Length > 0) / 2.0)
            {
                var per = new List<(string N, List<string> L)>();
                foreach (var l in b.Lines)
                {
                    var m = Regex.Match(l, @"^(\d)\s(.*)$");
                    if (!m.Success) continue;
                    var e = per.FindIndex(p => p.N == m.Groups[1].Value);
                    if (e < 0) { per.Add((m.Groups[1].Value, new List<string>())); e = per.Count - 1; }
                    per[e].L.Add(m.Groups[2].Value);
                }
                foreach (var (n, ls) in per)
                {
                    var key = Regex.Replace(b.Key, @"\d+$", "") + n;
                    sections[key] = new Section { Label = OsName(key), Text = string.Join("\n", ls) };
                    order.Add(key);
                }
            }
            else
            {
                sections[b.Key] = new Section { Label = OsName(b.Key), Text = string.Join("\n", b.Lines) };
                order.Add(b.Key);
            }
        }
        var pres = TextOf(Find(song, "presentation")).Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var seq = (pres.Length > 0 ? pres.ToList() : order).Where(sections.ContainsKey).Select(k => new Section { Label = sections[k].Label, Text = sections[k].Text }).ToList();
        var c = MakeSong(new Brouillon
        {
            Title = TextOf(Find(song, "title")).Trim() is { Length: > 0 } t ? t : "Sans titre",
            Author = TextOf(Find(song, "author")).Trim(),
            Copyright = TextOf(Find(song, "copyright")).Trim(),
            Ccli = TextOf(Find(song, "ccli")).Trim(),
            Sections = seq,
        });
        return c.Sections.Count > 0 ? new List<Chant> { c } : new List<Chant>();
    }

    // ============================================================ ChordPro
    static List<Chant> ParseChordPro(string text)
    {
        var songs = new List<Brouillon>();
        Brouillon song = null!;
        (string Kind, string Label, List<string> Lines)? sec = null;
        string? env = null;
        string pendingLabel = "";
        Section? lastChorus = null;
        void NewSong() { song = new Brouillon(); songs.Add(song); sec = null; env = null; lastChorus = null; }
        void Close()
        {
            if (sec is { } s && s.Lines.Any(l => l.Trim().Length > 0))
            {
                var x = new Section { Label = s.Label, Text = string.Join("\n", s.Lines) };
                song.Sections.Add(x);
                if (s.Kind == "chorus") lastChorus = x;
            }
            sec = null;
        }
        void Open(string kind, string label)
        {
            Close();
            sec = (kind, label.Length > 0 ? label : pendingLabel.Length > 0 ? pendingLabel : kind == "chorus" ? "Refrain" : kind == "bridge" ? "Pont" : "", new List<string>());
            pendingLabel = "";
        }
        NewSong();
        foreach (var raw in Clean(text).Split('\n'))
        {
            var line = raw.TrimEnd();
            var d = Regex.Match(line, @"^\s*\{\s*([a-z_]+)\s*(?:[:\s]\s*(.*?))?\s*\}\s*$", RegexOptions.IgnoreCase);
            if (d.Success)
            {
                var k = d.Groups[1].Value.ToLowerInvariant();
                var v = d.Groups[2].Value.Trim();
                if (k is "new_song" or "ns") { Close(); NewSong(); continue; }
                if (k is "title" or "t") { song.Title = v; continue; }
                if (k is "artist" or "composer" or "lyricist" or "subtitle" or "st") { if (song.Author.Length == 0) song.Author = v; continue; }
                if (k == "copyright") { song.Copyright = v; continue; }
                if (k == "ccli") { song.Ccli = v; continue; }
                if (k is "start_of_chorus" or "soc") { env = "chorus"; Open("chorus", v.Length > 0 ? FrLabel(v) : "Refrain"); continue; }
                if (k is "start_of_verse" or "sov") { env = "verse"; Open("verse", v.Length > 0 ? FrLabel(v) : ""); continue; }
                if (k is "start_of_bridge" or "sob") { env = "bridge"; Open("bridge", v.Length > 0 ? FrLabel(v) : "Pont"); continue; }
                if (k is "start_of_tab" or "sot" or "start_of_grid" or "sog") { Close(); env = "skip"; continue; }
                if (Regex.IsMatch(k, "^end_of_|^eo[cvbtg]$")) { Close(); env = null; continue; }
                if (k == "chorus") { Close(); if (lastChorus != null) song.Sections.Add(new Section { Label = lastChorus.Label, Text = lastChorus.Text }); continue; }
                if (k is "comment" or "c" or "comment_italic" or "ci" or "comment_box" or "cb")
                {
                    if (v.Length > 0 && Regex.IsMatch(v, @"^(verse|vers|couplet|chorus|refrain|bridge|pont|pre-?chorus|pré-?refrain|intro|outro|ending|fin|tag|final)\b", RegexOptions.IgnoreCase))
                    {
                        Close();
                        pendingLabel = FrLabel(v);
                    }
                    continue;
                }
                continue; // autres directives ignorées
            }
            if (env == "skip") continue;
            if (Regex.IsMatch(line, @"^\s*#")) continue;
            if (line.Trim().Length == 0) { if (env == null) Close(); else sec?.Lines.Add(""); continue; }
            // étiquette seule sur une ligne : « Verse 1: », « Refrain : »
            if (Regex.IsMatch(line.Trim(), @"^(verse|vers|couplet|chorus|refrain|bridge|pont|pre-?chorus|pré-?refrain|intro|outro|ending|tag|final)\s*\d*\s*:?$", RegexOptions.IgnoreCase) && env == null)
            {
                Close();
                pendingLabel = FrLabel(line.Trim());
                continue;
            }
            if (sec == null) Open(env ?? "verse", "");
            sec!.Value.Lines.Add(StripChordsInline(line));
        }
        Close();
        return songs.Select(MakeSong).Where(s => s.Sections.Count > 0).ToList();
    }

    // ============================================================ SongSelect (CCLI, texte)
    static readonly Regex SS_HEAD = new(@"^(verse|chorus|bridge|pre-?chorus|tag|ending|intro|outro|instrumental|interlude|misc|vamp|channel|couplet|refrain|pont|pré-?refrain|fin|strophe)(\s+\d+[a-z]?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SS_FOOT = new(@"^(ccli song|ccli-liednummer|ccli song number|chant ccli|ccli license|ccli-lizenz|licence ccli|for use solely|pour une utilisation|nur zur verwendung)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static List<Chant> ParseSongSelect(string text)
    {
        var lines = Clean(text).Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        var o = new Brouillon { Title = lines.Count > 0 ? lines[0].Trim() : "" };
        if (lines.Count > 0) lines.RemoveAt(0);
        int footAt = lines.FindIndex(l => SS_FOOT.IsMatch(l.Trim()));
        if (footAt >= 0)
        {
            int start = footAt;
            while (start > 0 && Regex.IsMatch(lines[start - 1].Trim(), @"\s\|\s|^©|^\(c\)|^copyright|^public domain|^domaine public", RegexOptions.IgnoreCase)) start--;
            // un seul auteur (sans « | ») sur une ligne à part, juste avant le pied de page
            int debut = start;
            while (debut > 0 && start - debut < 2 && lines[debut - 1].Trim().Length > 0 && !SS_HEAD.IsMatch(lines[debut - 1].Trim())) debut--;
            if (debut < start && (debut == 0 || lines[debut - 1].Trim().Length == 0)) start = debut;
            var foot = lines.Skip(start).ToList();
            lines.RemoveRange(start, lines.Count - start);
            foreach (var l in foot)
            {
                var t = l.Trim();
                if (t.Length == 0) continue;
                var n = Regex.Match(t, @"(\d{3,})");
                if (Regex.IsMatch(t, "^(ccli song|ccli-liednummer|ccli song number|chant ccli)", RegexOptions.IgnoreCase)) { if (n.Success) o.Ccli = n.Groups[1].Value; }
                else if (SS_FOOT.IsMatch(t)) continue;
                else if (Regex.IsMatch(t, @"^(©|\(c\)|copyright|public domain|domaine public)", RegexOptions.IgnoreCase)) o.Copyright = Regex.Replace(t, @"^copyright\s*", "", RegexOptions.IgnoreCase);
                else if (o.Author.Length == 0) o.Author = string.Join(", ", Regex.Split(t, @"\s*\|\s*"));
                else if (o.Copyright.Length == 0) o.Copyright = t;
            }
        }
        var secs = new List<(string Label, List<string> Lines)>();
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (SS_HEAD.IsMatch(t)) { secs.Add((FrLabel(t), new List<string>())); continue; }
            if (t.Length == 0) { if (secs.Count > 0 && secs[^1].Lines.Count > 0) secs[^1].Lines.Add(""); continue; }
            if (secs.Count == 0) secs.Add(("Couplet", new List<string>()));
            secs[^1].Lines.Add(StripChordsInline(t));
        }
        // une ligne vide au milieu d'une section SongSelect = nouvelle diapositive de la même section
        o.Sections = secs.SelectMany(s => Regex.Split(string.Join("\n", s.Lines), @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0)
            .Select(p => new Section { Label = s.Label, Text = p })).ToList();
        var c = MakeSong(o);
        return c.Sections.Count > 0 ? new List<Chant> { c } : new List<Chant>();
    }

    // ============================================================ texte simple
    /// <summary>Texte avec des strophes séparées par une ligne vide ; « [Refrain] » ou « Refrain : » en tête de strophe.</summary>
    public static List<Section> SplitSections(string text)
    {
        var blocks = Regex.Split(Clean(text), @"\n\s*\n").Select(b => b.Trim()).Where(b => b.Length > 0);
        var o = new List<Section>();
        foreach (var b in blocks)
        {
            var lines = b.Split('\n').ToList();
            var label = "";
            var first = lines[0].Trim();
            Match m;
            if ((m = Regex.Match(first, @"^\[(.+?)\]\s*(.*)$")).Success)
            {
                label = FrLabel(m.Groups[1].Value);
                lines[0] = m.Groups[2].Value;
            }
            else if ((m = Regex.Match(first, @"^(refrain|chorus|couplet|verse|pont|bridge|pré-?refrain|pre-?chorus|intro|fin|final|coda)(\s*\d+)?\s*[:.\-–]\s*(.*)$", RegexOptions.IgnoreCase)).Success)
            {
                label = FrLabel(m.Groups[1].Value + m.Groups[2].Value);
                lines[0] = m.Groups[3].Value;
            }
            else if (SS_HEAD.IsMatch(first) && lines.Count > 1)
            {
                label = FrLabel(first);
                lines.RemoveAt(0);
            }
            var body = Tidy(string.Join("\n", lines));
            if (body.Length > 0) o.Add(new Section { Label = label.Length > 0 ? label : "Couplet", Text = body });
        }
        return FinishLabels(o);
    }

    static List<Chant> ParsePlainText(string name, string text)
    {
        var lines = Clean(text).Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        var title = lines.Count > 0 ? lines[0].Trim() : "";
        if (Regex.IsMatch(title, @"^\[.+\]$") || title.Length == 0) title = BaseName(name);
        else lines.RemoveAt(0);
        var c = MakeSong(new Brouillon { Title = title, Sections = SplitSections(string.Join("\n", lines)) });
        return c.Sections.Count > 0 ? new List<Chant> { c } : new List<Chant>();
    }

    // ============================================================ ProPresenter 6 (.pro6)
    /// <summary>RTF → texte brut (suffisant pour les diapositives de ProPresenter).</summary>
    public static string RtfToText(string rtf)
    {
        var o = new StringBuilder();
        int i = 0, uc = 1, skipping = 0, n = rtf.Length;
        var skip = new Stack<int>();
        while (i < n)
        {
            char ch = rtf[i];
            if (ch == '{') { skip.Push(skipping); i++; continue; }
            if (ch == '}') { skipping = skip.Count > 0 ? skip.Pop() : 0; i++; continue; }
            if (ch == '\\')
            {
                char next = i + 1 < n ? rtf[i + 1] : '\0';
                if (next is '\\' or '{' or '}') { if (skipping == 0) o.Append(next); i += 2; continue; }
                if (next == '\'')
                {
                    if (i + 4 <= n && skipping == 0 && int.TryParse(rtf.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var code))
                        o.Append(Texte.Latin1252.GetString(new[] { (byte)code }));
                    i += 4;
                    continue;
                }
                if (next == '*') { skipping = 1; i += 2; continue; }
                var m = Regex.Match(rtf.Substring(i, Math.Min(40, n - i)), @"^\\([a-zA-Z]+)(-?\d+)? ?");
                if (!m.Success) { i += 2; continue; }
                var word = m.Groups[1].Value;
                int? arg = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : null;
                i += m.Length;
                if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "expandedcolortbl" or "listtable" or "listoverridetable") skipping = 1;
                else if (word is "par" or "line" or "sect") { if (skipping == 0) o.Append('\n'); }
                else if (word == "tab") { if (skipping == 0) o.Append(' '); }
                else if (word == "uc") uc = arg ?? 1;
                else if (word == "u" && arg != null)
                {
                    if (skipping == 0) o.Append((char)(arg < 0 ? arg + 65536 : arg));
                    int k = uc;
                    while (k > 0 && i < n)
                    {
                        if (rtf[i] == '\\' && i + 1 < n && rtf[i + 1] == '\'') i += 4;
                        else if (rtf[i] is '{' or '}') break;
                        else i++;
                        k--;
                    }
                }
                continue;
            }
            if (ch is '\r' or '\n') { i++; continue; }
            if (skipping == 0) o.Append(ch);
            i++;
        }
        return o.ToString();
    }

    static byte[] B64(string s) { try { return Convert.FromBase64String(Regex.Replace(s, @"\s+", "")); } catch (FormatException) { return Array.Empty<byte>(); } }

    static List<Chant> ParsePro6(string name, string text)
    {
        var doc = ParseXml(text);
        var pres = Find(doc, "rvpresentationdocument") ?? throw new InvalidDataException("Ce fichier ne ressemble pas à une présentation ProPresenter 6.");
        var sections = new List<Section>();
        foreach (var g in FindAll(pres, "rvslidegrouping"))
        {
            var label = Attr(g, "name");
            foreach (var s in FindAll(g, "rvdisplayslide"))
            {
                if (Attr(s, "enabled") == "false") continue;
                var texts = new List<string>();
                foreach (var el in FindAll(s, "rvtextelement"))
                {
                    var strings = FindAll(el, "nsstring");
                    var plain = strings.FirstOrDefault(x => Attr(x, "rvxmlivarname").ToLowerInvariant() == "plaintext");
                    var rtf = strings.FirstOrDefault(x => Attr(x, "rvxmlivarname").ToLowerInvariant() == "rtfdata");
                    var val = "";
                    if (plain != null && TextOf(plain).Trim().Length > 0) val = Encoding.UTF8.GetString(B64(TextOf(plain)));
                    else if (rtf != null && TextOf(rtf).Trim().Length > 0) val = RtfToText(Texte.Latin1252.GetString(B64(TextOf(rtf))));
                    else if (Attr(el, "rtfdata").Length > 0) val = RtfToText(Texte.Latin1252.GetString(B64(Attr(el, "rtfdata"))));
                    if (val.Trim().Length > 0) texts.Add(val);
                }
                var txt = string.Join("\n", texts);
                if (txt.Trim().Length > 0) sections.Add(new Section { Label = label, Text = txt });
            }
        }
        var year = Attr(pres, "cclicopyrightyear") is { Length: > 0 } y ? y : Attr(pres, "cclicopyrightinfo");
        var c = MakeSong(new Brouillon
        {
            Title = Attr(pres, "cclisongtitle") is { Length: > 0 } t ? t : BaseName(name),
            Author = Attr(pres, "ccliauthor") is { Length: > 0 } a ? a : Attr(pres, "artist"),
            Copyright = string.Join(" ", new[] { year, Attr(pres, "cclipublisher") }.Where(x => x.Length > 0)),
            Ccli = Attr(pres, "cclisongnumber"),
            Sections = sections,
        });
        return c.Sections.Count > 0 ? new List<Chant> { c } : new List<Chant>();
    }

    // ============================================================ détection et point d'entrée
    public static string BaseName(string? name)
    {
        var b = Path.GetFileNameWithoutExtension((name ?? "").Split('\\', '/').Last()).Trim();
        return b.Length > 0 ? b : "Sans titre";
    }

    public static string DetectFormat(string name, string text)
    {
        var n = (name ?? "").ToLowerInvariant();
        var t = Clean(text).TrimStart();
        string debut(int k) => t.Length > k ? t[..k] : t;
        if (n.EndsWith(".pro6") || Regex.IsMatch(debut(2000), "<RVPresentationDocument", RegexOptions.IgnoreCase)) return "propresenter";
        if (n.EndsWith(".pro") && !Regex.IsMatch(t, @"^\s*[{<]")) return "propresenter7";
        if (Regex.IsMatch(debut(200), @"^\{\s*$|^\{\s*""?[A-Za-z]") && Regex.IsMatch(debut(5000), @"""?Songs""?\s*:|""?Verses""?\s*:")) return "videopsalm";
        if (Regex.IsMatch(n, @"\.(json|vpc|vpsong)$")) return "videopsalm";
        if (Regex.IsMatch(debut(3000), @"openlyrics\.info/namespace", RegexOptions.IgnoreCase)) return "openlyrics";
        if (Regex.IsMatch(t, @"^(<\?xml[^>]*>\s*)?(<!--[\s\S]*?-->\s*)*<song[\s>]", RegexOptions.IgnoreCase))
            return Regex.IsMatch(t, @"<lyrics[\s>]", RegexOptions.IgnoreCase) && !Regex.IsMatch(t, @"<verse[\s>]", RegexOptions.IgnoreCase) ? "opensong" : "openlyrics";
        if (Regex.IsMatch(n, @"\.(cho|chordpro|chopro|crd)$") || Regex.IsMatch(t, @"^\s*\{\s*(title|t|start_of_chorus|soc)\s*[:}]", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return "chordpro";
        if (Regex.IsMatch(t, @"^(ccli song|ccli-liednummer|ccli song number|chant ccli)\s*#?", RegexOptions.IgnoreCase | RegexOptions.Multiline) || Regex.IsMatch(t, "for use solely with the songselect", RegexOptions.IgnoreCase)) return "songselect";
        return "texte";
    }

    /// <summary>Lit un fichier de chants (contenu déjà décodé). Lève une exception claire si rien n'est trouvé.</summary>
    public static List<Chant> ParseSongFile(string name, string text)
    {
        var songs = DetectFormat(name, text) switch
        {
            "videopsalm" => ParseVideoPsalm(text),
            "openlyrics" => ParseOpenLyrics(text),
            "opensong" => ParseOpenSong(text),
            "chordpro" => ParseChordPro(text),
            "songselect" => ParseSongSelect(text),
            "propresenter" => ParsePro6(name, text),
            "propresenter7" => throw new InvalidDataException("Les fichiers ProPresenter 7 (.pro) ne sont pas lisibles : dans ProPresenter, exportez le chant en texte ou en ProPresenter 6."),
            _ => ParsePlainText(name, text),
        };
        if (songs.Count == 0) throw new InvalidDataException("Aucun chant trouvé dans ce fichier.");
        return songs;
    }

    public static List<Chant> LireFichier(string chemin) => ParseSongFile(Path.GetFileName(chemin), Texte.Decoder(File.ReadAllBytes(chemin)));

    /// <summary>Forme texte de l'éditeur : [Nom] + paroles, une ligne vide entre les diapositives.</summary>
    public static string VersTexte(IEnumerable<Section> sections) => string.Join("\n\n", sections.Select(s => $"[{s.Label}]\n{s.Text}"));
}
