using System.Text;
using System.Text.Json;

namespace MyVerse;

/// <summary>Une Bible : 66 livres, chapitres, versets (Louis Segond, King James, ou Bible importée).</summary>
public sealed class Bible
{
    public sealed class Livre
    {
        public string Nom { get; set; } = "";
        public List<List<string>> Chapitres { get; set; } = new();
    }

    public string Id { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Court { get; set; } = "";
    public List<Livre> Livres { get; set; } = new();

    public int NombreChapitres(int b) => b >= 0 && b < Livres.Count ? Livres[b].Chapitres.Count : 0;

    public IReadOnlyList<string> Versets(int b, int c)
    {
        if (b < 0 || b >= Livres.Count) return Array.Empty<string>();
        var chs = Livres[b].Chapitres;
        return c >= 1 && c <= chs.Count ? chs[c - 1] : Array.Empty<string>();
    }

    /// <summary>Texte des versets v1 à v2 (les versets vides sont sautés).</summary>
    public string Texte(int b, int c, int v1, int v2)
    {
        var vs = Versets(b, c);
        var sb = new StringBuilder();
        for (int v = v1; v <= v2 && v <= vs.Count; v++)
        {
            if (v < 1 || string.IsNullOrWhiteSpace(vs[v - 1])) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(vs[v - 1]);
        }
        return sb.ToString();
    }

    /// <summary>La référence dans les limites de cette Bible (chapitre entier → tous ses versets) ; null si elle n'existe pas.</summary>
    public Ref? Borner(Ref r)
    {
        int n = Versets(r.B, r.C).Count;
        if (n == 0) return null;
        if (r.V1 <= 0) return new Ref(r.B, r.C, 1, n);
        if (r.V1 > n) return null;
        return new Ref(r.B, r.C, r.V1, Math.Clamp(r.V2, r.V1, n));
    }

    /// <summary>
    /// Comme la 3.x : chapitre ramené dans les limites (chapitre entier si hors limites), verset trop grand → chapitre entier.
    /// Livre d'un seul chapitre (Jude, Philémon…) : « jude 3 » = verset 3.
    /// </summary>
    public Ref Ajuster(Ref r)
    {
        int nc = NombreChapitres(r.B);
        if (nc == 0) return new Ref(r.B, 1, 0, 0);
        if (nc == 1 && r.C > 1 && r.V1 == 0) r = new Ref(r.B, 1, r.C, Math.Max(r.C, r.V2));
        if (r.C < 1 || r.C > nc) return new Ref(r.B, Math.Clamp(r.C, 1, nc), 0, 0);
        int n = Versets(r.B, r.C).Count;
        if (r.V1 > n) return new Ref(r.B, r.C, 0, 0);
        return new Ref(r.B, r.C, r.V1, Math.Min(Math.Max(r.V2, r.V1), n));
    }

    public int NombreVersets() => Livres.Sum(l => l.Chapitres.Sum(c => c.Count(v => !string.IsNullOrWhiteSpace(v))));

    // ------------------------------------------------------------ JSON { name, short, books: [{ name, ch: [[…]] }] }

    public static Bible DepuisJson(string id, string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var r = doc.RootElement;
        var b = new Bible
        {
            Id = id,
            Nom = Chaine(r, "name") ?? id,
            Court = Chaine(r, "short") ?? id.ToUpperInvariant(),
        };
        if (r.TryGetProperty("books", out var books) && books.ValueKind == JsonValueKind.Array)
        {
            foreach (var bk in books.EnumerateArray())
            {
                var l = new Livre();
                if (bk.ValueKind == JsonValueKind.Object)
                {
                    l.Nom = Chaine(bk, "name") ?? "";
                    if (bk.TryGetProperty("ch", out var chs) && chs.ValueKind == JsonValueKind.Array)
                        foreach (var ch in chs.EnumerateArray())
                        {
                            var vs = new List<string>();
                            if (ch.ValueKind == JsonValueKind.Array)
                                foreach (var v in ch.EnumerateArray()) vs.Add(v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "");
                            l.Chapitres.Add(vs);
                        }
                }
                b.Livres.Add(l);
            }
        }
        while (b.Livres.Count < 66) b.Livres.Add(new Livre());
        return b;
    }

    public string VersJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("name", Nom);
            w.WriteString("short", Court);
            w.WriteStartArray("books");
            foreach (var l in Livres)
            {
                w.WriteStartObject();
                w.WriteString("name", l.Nom);
                w.WriteStartArray("ch");
                foreach (var ch in l.Chapitres)
                {
                    w.WriteStartArray();
                    foreach (var v in ch) w.WriteStringValue(v ?? "");
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static string? Chaine(JsonElement e, string nom) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
