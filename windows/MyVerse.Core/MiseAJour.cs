using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace MyVerse;

/// <summary>
/// Mise à jour de MyVerse : dernière version publiée sur GitHub Releases,
/// téléchargement de l'installateur puis installation silencieuse (/S) qui rouvre MyVerse.
/// </summary>
public static class MiseAJour
{
    public const string Api = "https://api.github.com/repos/capmaster2023/Verselight/releases/latest";
    public const string Prefixe = "https://github.com/capmaster2023/Verselight/releases/download/";

    public sealed record Resultat(bool Disponible, string Version, string Url, string? Erreur);

    static readonly HttpClient Http = Creer();
    static HttpClient Creer()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MyVerse-Updater");
        return h;
    }

    static int[] Parties(string v)
    {
        var p = new int[3];
        var morceaux = v.Trim().TrimStart('v', 'V').Split('.', 3);
        for (int i = 0; i < morceaux.Length && i < 3; i++)
        {
            var chiffres = new string(morceaux[i].SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            int.TryParse(chiffres, out p[i]);
        }
        return p;
    }

    /// <summary>a est plus récent que b.</summary>
    public static bool PlusRecente(string a, string b)
    {
        var pa = Parties(a);
        var pb = Parties(b);
        for (int i = 0; i < 3; i++) if (pa[i] != pb[i]) return pa[i] > pb[i];
        return false;
    }

    public static async Task<Resultat> Verifier(string actuelle, CancellationToken t = default)
    {
        HttpResponseMessage r;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Api);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(t);
            delai.CancelAfter(15000);
            r = await Http.SendAsync(req, delai.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new Resultat(false, "", "", "Impossible de vérifier les mises à jour : pas de connexion Internet.");
        }
        using (r)
        {
            if (r.StatusCode == HttpStatusCode.NotFound)
                return new Resultat(false, "", "", "Mises à jour inaccessibles : le dépôt GitHub de MyVerse est privé (il doit être public pour que Outils › Mise à jour fonctionne).");
            if (!r.IsSuccessStatusCode)
                return new Resultat(false, "", "", $"Impossible de vérifier les mises à jour (GitHub : {(int)r.StatusCode}). Réessayez plus tard.");
            try
            {
                using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(t));
                var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                var url = "";
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                    {
                        var nom = a.GetProperty("name").GetString() ?? "";
                        var u = a.GetProperty("browser_download_url").GetString() ?? "";
                        if (nom.StartsWith("MyVerse-Setup-") && nom.EndsWith(".exe") && u.StartsWith(Prefixe)) url = u;
                    }
                var derniere = tag.TrimStart('v');
                return new Resultat(url.Length > 0 && PlusRecente(derniere, actuelle), derniere, url, null);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return new Resultat(false, "", "", "Réponse de GitHub illisible.");
            }
        }
    }

    /// <summary>Télécharge l'installateur (progression 0-100) puis le lance en silencieux : il ferme MyVerse, installe et le rouvre.</summary>
    public static async Task Installer(string url, IProgress<int>? progres, CancellationToken t = default)
    {
        if (!url.StartsWith(Prefixe) || !url.EndsWith(".exe")) throw new InvalidOperationException("Adresse de mise à jour refusée.");
        using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, t);
        r.EnsureSuccessStatusCode();
        var chemin = Path.Combine(Path.GetTempPath(), "MyVerse-Setup.exe");
        long total = r.Content.Headers.ContentLength ?? -1, fait = 0;
        await using (var f = File.Create(chemin))
        await using (var s = await r.Content.ReadAsStreamAsync(t))
        {
            var buf = new byte[64 * 1024];
            var dernier = DateTime.MinValue;
            int n;
            while ((n = await s.ReadAsync(buf, t)) > 0)
            {
                await f.WriteAsync(buf.AsMemory(0, n), t);
                fait += n;
                if (total > 0 && DateTime.Now - dernier > TimeSpan.FromMilliseconds(300))
                {
                    dernier = DateTime.Now;
                    progres?.Report((int)(fait * 100 / total));
                }
            }
        }
        if (fait < 100000 || (total > 0 && fait != total)) throw new IOException($"fichier incomplet ({fait} octets)");
        progres?.Report(100);
        Process.Start(new ProcessStartInfo(chemin, "/S") { UseShellExecute = true });
    }
}
