using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// Transfert vers un autre ordinateur : les Bibles importées et/ou les chants dans un seul fichier
/// « .myverse » (un zip : myverse-export.json, chants.json, bibles/&lt;id&gt;.json).
/// </summary>
public static class Transfert
{
    public const string Extension = ".myverse";
    const string FManifeste = "myverse-export.json", FChants = "chants.json";

    public sealed class Manifeste
    {
        [JsonPropertyName("app")] public string App { get; set; } = "MyVerse";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("chants")] public int Chants { get; set; }
        [JsonPropertyName("recueils")] public List<string> Recueils { get; set; } = new();
        [JsonPropertyName("bibles")] public List<BibleImportee> Bibles { get; set; } = new();
    }

    public sealed record Bilan(int ChantsAjoutes, int ChantsMisAJour, int ChantsPareils, int BiblesAjoutees, int BiblesPresentes);

    /// <summary>Écrit le fichier d'export (les Bibles intégrées LSG et KJV ne sont pas copiées : chaque MyVerse les a).</summary>
    public static Manifeste Exporter(string fichier, Donnees d, bool chants, bool bibles, string version = "")
    {
        var m = new Manifeste { Version = version, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        var tmp = fichier + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            if (chants)
            {
                m.Chants = d.Chants.Count;
                m.Recueils = d.Chants.Select(c => (c.Author ?? "").Trim()).Where(x => x.Length > 0).Distinct()
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
                Ecrire(zip, FChants, JsonSerializer.Serialize(d.Chants, Donnees.Json));
            }
            if (bibles)
            {
                foreach (var b in d.Reglages.Bibles.Importees)
                {
                    var f = Path.Combine(d.DossierBibles, b.Id + ".json");
                    if (!File.Exists(f)) continue;
                    zip.CreateEntryFromFile(f, "bibles/" + b.Id + ".json", CompressionLevel.Optimal);
                    m.Bibles.Add(b);
                }
            }
            Ecrire(zip, FManifeste, JsonSerializer.Serialize(m, Donnees.Json));
        }
        File.Move(tmp, fichier, true);
        Journal.Info($"Export : {m.Chants} chants, {m.Bibles.Count} Bible(s) → {fichier}");
        return m;
    }

    /// <summary>Lit le contenu annoncé d'un fichier d'export (lève une exception si ce n'en est pas un).</summary>
    public static Manifeste Lire(string fichier)
    {
        using var zip = ZipFile.OpenRead(fichier);
        var e = zip.GetEntry(FManifeste) ?? throw new InvalidDataException("Ce fichier n'est pas un export MyVerse.");
        using var s = e.Open();
        return JsonSerializer.Deserialize<Manifeste>(s, Donnees.Json) ?? throw new InvalidDataException("Export MyVerse illisible.");
    }

    /// <summary>Ajoute le contenu du fichier : chants (un chant déjà là, même recueil et même titre, est remplacé)
    /// et Bibles (une Bible déjà là n'est pas ajoutée deux fois).</summary>
    public static Bilan Importer(string fichier, Regie r, Bibles bib)
    {
        var m = Lire(fichier);
        int aj = 0, maj = 0, pareils = 0, bAj = 0, bLa = 0;
        using var zip = ZipFile.OpenRead(fichier);
        if (zip.GetEntry(FChants) is { } ec)
        {
            List<Chant>? liste;
            using (var s = ec.Open()) liste = JsonSerializer.Deserialize<List<Chant>>(s, Donnees.Json);
            if (liste != null) (aj, maj, pareils) = r.FusionnerChants(liste);
        }
        foreach (var b in m.Bibles)
        {
            var e = zip.GetEntry("bibles/" + b.Id + ".json");
            if (e == null) continue;
            if (bib.Toutes().Any(x => !x.Integree && (x.Id == b.Id || (x.Nom == b.Name && x.Versets == b.Versets)))) { bLa++; continue; }
            using var ms = new MemoryStream();
            using (var s = e.Open()) s.CopyTo(ms);
            var bible = Bible.DepuisJson(b.Id, Texte.Decoder(ms.ToArray()));
            if (string.IsNullOrWhiteSpace(bible.Nom)) bible.Nom = b.Name;
            bib.Ajouter(bible, b.Short);
            bAj++;
        }
        Journal.Info($"Import MyVerse : {aj} chants ajoutés, {maj} mis à jour, {bAj} Bible(s) ajoutée(s)");
        return new Bilan(aj, maj, pareils, bAj, bLa);
    }

    static void Ecrire(ZipArchive zip, string nom, string texte)
    {
        var e = zip.CreateEntry(nom, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new System.Text.UTF8Encoding(false));
        w.Write(texte);
    }
}
