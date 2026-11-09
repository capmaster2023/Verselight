using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>Téléphones jumelés (%APPDATA%\VerseLight\devices.json, même fichier que la 3.x : les téléphones restent jumelés).</summary>
public sealed class AppareilsJumeles
{
    public sealed class Appareil
    {
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("key")] public string Key { get; set; } = ""; // secret dérivé (base64url), jamais transmis
        [JsonPropertyName("paired")] public string Paired { get; set; } = "";
        [JsonPropertyName("lastSeen"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? LastSeen { get; set; }
    }

    sealed class Fichier
    {
        [JsonPropertyName("pcId")] public string PcId { get; set; } = "";
        [JsonPropertyName("firewall2"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? Firewall { get; set; }
        [JsonPropertyName("devices")] public List<Appareil> Devices { get; set; } = new();
    }

    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly object verrou = new();
    readonly string chemin;
    Fichier d = new();

    public AppareilsJumeles(string dossier)
    {
        Directory.CreateDirectory(dossier);
        chemin = Path.Combine(dossier, "devices.json");
        try
        {
            if (File.Exists(chemin)) d = JsonSerializer.Deserialize<Fichier>(File.ReadAllText(chemin)) ?? new Fichier();
        }
        catch (JsonException)
        {
            try { File.Move(chemin, chemin + ".abime", true); } catch (IOException) { }
            d = new Fichier();
        }
        d.Devices ??= new List<Appareil>();
        if (string.IsNullOrEmpty(d.PcId)) { d.PcId = Guid.NewGuid().ToString(); Sauver(); }
    }

    void Sauver()
    {
        try
        {
            var tmp = chemin + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, Opt));
            File.Move(tmp, chemin, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Journal.Erreur("enregistrement des téléphones", e); }
    }

    static string Maintenant() => DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");

    public string PcId { get { lock (verrou) return d.PcId; } }
    public string? Parefeu { get { lock (verrou) return d.Firewall; } set { lock (verrou) { d.Firewall = value; Sauver(); } } }

    public byte[]? Cle(string deviceId)
    {
        lock (verrou)
        {
            var a = d.Devices.FirstOrDefault(x => x.DeviceId == deviceId);
            if (a == null) return null;
            try { return Telephone.B64Dec(a.Key); } catch (FormatException) { return null; }
        }
    }

    public void Ajouter(string deviceId, string nom, byte[] cle)
    {
        lock (verrou)
        {
            var now = Maintenant();
            d.Devices.RemoveAll(x => x.DeviceId == deviceId);
            d.Devices.Add(new Appareil { DeviceId = deviceId, Name = nom, Key = Telephone.B64(cle), Paired = now, LastSeen = now });
            Sauver();
        }
    }

    public void Vu(string deviceId, string nom)
    {
        lock (verrou)
        {
            foreach (var a in d.Devices.Where(x => x.DeviceId == deviceId))
            {
                a.LastSeen = Maintenant();
                if (nom.Length > 0) a.Name = nom;
            }
            Sauver();
        }
    }

    public bool Revoquer(string deviceId)
    {
        lock (verrou)
        {
            int n = d.Devices.RemoveAll(x => x.DeviceId == deviceId);
            if (n > 0) Sauver();
            return n > 0;
        }
    }

    /// <summary>Copie sans les clés.</summary>
    public List<(string DeviceId, string Nom, string Jumele, string? VuLe)> Liste()
    {
        lock (verrou) return d.Devices.Select(a => (a.DeviceId, a.Name, a.Paired, a.LastSeen)).ToList();
    }
}

/// <summary>
/// Télécommande Android : serveur WebSocket sur le réseau local (port 47900 à 47909), découverte UDP (47901),
/// jumelage par code QR et authentification HMAC. Même protocole que le module réseau de la 3.x.
/// </summary>
public sealed class Telephone : IDisposable
{
    public const int PremierPort = 47900, DernierPort = 47909, PortDecouverte = 47901, Protocole = 1;

    readonly Regie regie;
    readonly Func<Func<Task>, Task> surUi;
    readonly AppareilsJumeles store;
    readonly CancellationTokenSource fin = new();
    readonly List<Connexion> phones = new();
    TcpListener? ecoute;
    UdpClient? udp;
    byte[]? cleJumelage;
    DateTime jumelageFin;
    string dernierEtat = "";
    // page « suivre » de l'assemblée (lecture seule, sans jumelage)
    readonly object verrouPublic = new();
    string etatPublic = "{\"v\":0,\"mode\":\"vide\",\"lignes\":[]}", dernierPublic = "";
    int versionPublic;
    TaskCompletionSource changementPublic = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Dictionary<string, DateTime> suiveurs = new();
    Timer? minuterieEtat;

    public string NomPc { get; }
    public int Port { get; private set; }
    public string PcId => store.PcId;
    public AppareilsJumeles Store => store;
    /// <summary>OBS (actif, connecté), pour l'état envoyé aux téléphones.</summary>
    public Func<(bool Actif, bool Connecte)> EtatObs { get; set; } = () => (false, false);
    /// <summary>Téléphones jumelés ou connectés : la liste a changé (thread quelconque).</summary>
    public event Action? Change;

    sealed class Connexion
    {
        public required WebSocket Ws;
        public required string DeviceId;
        public required string Nom;
        public readonly SemaphoreSlim Envoi = new(1, 1);
    }

    /// <param name="surUi">Exécute une action sur le fil de l'interface (la régie n'est utilisée que là).</param>
    public Telephone(string dossier, Regie regie, Func<Func<Task>, Task> surUi, string? nomPc = null)
    {
        this.regie = regie;
        this.surUi = surUi;
        store = new AppareilsJumeles(dossier);
        NomPc = nomPc ?? Environment.GetEnvironmentVariable("COMPUTERNAME") ?? Dns.GetHostName();
    }

    // ------------------------------------------------------------ outils
    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] B64Dec(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
    static string Hasard(int n) => B64(RandomNumberGenerator.GetBytes(n));
    public static byte[] DeriverCle(byte[] secret, string pcId, string deviceId) => HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"verselight-pair|{pcId}|{deviceId}"));
    public static string HmacB64(byte[] cle, string msg) => B64(HMACSHA256.HashData(cle, Encoding.UTF8.GetBytes(msg)));
    public static string PreuveServeur(byte[] cle, string cnonce, string nonce) => HmacB64(cle, $"verselight-srv|{cnonce}|{nonce}");
    public static string MacClient(byte[] cle, string nonce, string deviceId) => HmacB64(cle, nonce + deviceId);

    /// <summary>Adresses IPv4 privées, les cartes Wi-Fi/Ethernet réelles d'abord.</summary>
    public static List<string> AdressesLocales()
    {
        var cs = new List<(string Ip, int Score)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var nom = (ni.Name + " " + ni.Description).ToLowerInvariant();
                int score = 0;
                foreach (var v in new[] { "vethernet", "virtualbox", "vmware", "hyper-v", "wsl", "docker", "vpn", "tailscale", "zerotier", "tap", "bluetooth", "loopback", "virtual" })
                    if (nom.Contains(v)) score += 10;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nom.Contains("wi-fi") || nom.Contains("wlan") || nom.Contains("wireless")) score--;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = a.Address.GetAddressBytes();
                    bool prive = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                    if (prive) cs.Add((a.Address.ToString(), score));
                }
            }
        }
        catch (NetworkInformationException) { }
        return cs.OrderBy(c => c.Score).Select(c => c.Ip).Distinct().ToList();
    }

    // ------------------------------------------------------------ démarrage
    public void Demarrer()
    {
        for (int p = PremierPort; p <= DernierPort; p++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, p);
                l.Start();
                ecoute = l;
                Port = p;
                break;
            }
            catch (SocketException) { }
        }
        if (ecoute == null)
        {
            Journal.Erreur($"téléphone : aucun port libre entre {PremierPort} et {DernierPort}");
            return;
        }
        Journal.Info($"écoute des téléphones sur le port {Port}");
        _ = Accepter(ecoute);
        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, PortDecouverte));
            _ = Decouverte(udp);
        }
        catch (SocketException e) { Journal.Erreur("découverte UDP indisponible : " + e.Message); }
        regie.Change += EtatChange;
    }

    async Task Accepter(TcpListener l)
    {
        while (!fin.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(fin.Token); }
            catch (Exception) when (fin.IsCancellationRequested) { return; }
            catch (SocketException) { await Task.Delay(200); continue; }
            catch (ObjectDisposedException) { return; }
            _ = Task.Run(() => Client(c));
        }
    }

    async Task Decouverte(UdpClient u)
    {
        var vus = new Dictionary<string, DateTime>();
        while (!fin.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await u.ReceiveAsync(fin.Token); }
            catch (Exception) when (fin.IsCancellationRequested) { return; }
            catch (SocketException) { continue; } // Windows : ICMP « port injoignable » d'un envoi précédent
            catch (ObjectDisposedException) { return; }
            if (!Encoding.UTF8.GetString(r.Buffer).StartsWith("VERSELIGHT_DISCOVER")) continue;
            var ip = r.RemoteEndPoint.Address.ToString();
            if (!vus.TryGetValue(ip, out var t) || DateTime.Now - t > TimeSpan.FromMinutes(1))
            {
                vus[ip] = DateTime.Now;
                Journal.Info($"recherche du PC par le téléphone {ip} : réponse envoyée");
            }
            var rep = Encoding.UTF8.GetBytes(new JsonObject { ["t"] = "verselight", ["v"] = Protocole, ["pcId"] = PcId, ["name"] = NomPc, ["port"] = Port }.ToJsonString());
            try { await u.SendAsync(rep, rep.Length, r.RemoteEndPoint); } catch (SocketException) { }
        }
    }

    // ------------------------------------------------------------ jumelage
    /// <summary>Ouvre le jumelage (10 minutes) et renvoie l'adresse du code QR.</summary>
    public string OuvrirJumelage()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        lock (phones)
        {
            cleJumelage = secret;
            jumelageFin = DateTime.UtcNow.AddMinutes(10);
        }
        var ips = AdressesLocales();
        return "verselight://pair?pcId=" + Uri.EscapeDataString(PcId) + "&name=" + Uri.EscapeDataString(NomPc) +
               "&ips=" + string.Join(",", ips) + "&port=" + Port + "&secret=" + B64(secret);
    }

    public void FermerJumelage() { lock (phones) cleJumelage = null; }

    public List<string> Connectes() { lock (phones) return phones.Select(p => p.DeviceId).Distinct().ToList(); }

    public void Revoquer(string deviceId)
    {
        store.Revoquer(deviceId);
        List<Connexion> a;
        lock (phones) a = phones.Where(p => p.DeviceId == deviceId).ToList();
        foreach (var p in a)
        {
            _ = Envoyer(p, new JsonObject { ["t"] = "auth.fail", ["code"] = "revoked", ["message"] = "Ce téléphone a été retiré dans MyVerse." })
                .ContinueWith(_ => { try { p.Ws.Abort(); } catch { } });
        }
        Prevenir();
    }

    void Prevenir() { try { Change?.Invoke(); } catch (Exception e) { Journal.Erreur("téléphone", e); } }

    // ------------------------------------------------------------ connexion d'un téléphone
    static async Task<string?> LireEntete(Stream s, CancellationToken t)
    {
        var b = new List<byte>(512);
        var un = new byte[1];
        while (b.Count < 16384)
        {
            int n = await s.ReadAsync(un, t);
            if (n == 0) return null;
            b.Add(un[0]);
            int k = b.Count;
            if (k >= 4 && b[k - 4] == '\r' && b[k - 3] == '\n' && b[k - 2] == '\r' && b[k - 1] == '\n') return Encoding.ASCII.GetString(b.ToArray());
        }
        return null;
    }

    async Task Client(TcpClient c)
    {
        var qui = c.Client.RemoteEndPoint?.ToString() ?? "?";
        using var _c = c;
        try
        {
            c.NoDelay = true;
            try
            {
                // téléphone disparu (Wi-Fi coupé) : la connexion est fermée en moins d'une minute
                c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                c.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 20);
                c.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
                c.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 4);
            }
            catch (Exception e) when (e is SocketException or PlatformNotSupportedException) { }
            var s = c.GetStream();
            string? entete;
            using (var delai = CancellationTokenSource.CreateLinkedTokenSource(fin.Token))
            {
                delai.CancelAfter(10000);
                entete = await LireEntete(s, delai.Token);
            }
            if (entete == null) return;
            var lignes = entete.Split("\r\n");
            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in lignes.Skip(1))
            {
                int i = l.IndexOf(':');
                if (i > 0) h[l[..i].Trim()] = l[(i + 1)..].Trim();
            }
            if (!(h.TryGetValue("Upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase)) || !h.TryGetValue("Sec-WebSocket-Key", out var cle))
            {
                var demande = lignes[0].Split(' ');
                var chemin = demande.Length > 1 ? demande[1] : "/";
                if (chemin == "/suivre" || chemin.StartsWith("/suivre/", StringComparison.Ordinal) || chemin.StartsWith("/suivre?", StringComparison.Ordinal))
                    await Suivre(s, chemin, qui);
                else await PageTest(s);
                return;
            }
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(cle.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var rep = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
            await s.WriteAsync(rep, fin.Token);
            using var ws = WebSocket.CreateFromStream(s, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(15) });
            await Session(ws, qui);
        }
        catch (Exception e) when (e is IOException or SocketException or WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { Journal.Erreur($"téléphone {qui}", e); }
    }

    // ------------------------------------------------------------ l'assemblée suit sur son téléphone
    /// <summary>Adresse de la page « suivre » (pour le code QR), ou null sans réseau.</summary>
    public string? AdresseSuivre() => Port > 0 && AdressesLocales().FirstOrDefault() is { } ip ? $"http://{ip}:{Port}/suivre" : null;

    /// <summary>Téléphones qui suivent la page en ce moment (vus dans les 45 dernières secondes).</summary>
    public int Suiveurs()
    {
        lock (verrouPublic)
        {
            var limite = DateTime.UtcNow.AddSeconds(-45);
            foreach (var k in suiveurs.Where(x => x.Value < limite).Select(x => x.Key).ToList()) suiveurs.Remove(k);
            return suiveurs.Count;
        }
    }

    /// <summary>Ce que voit l'assemblée (fil de l'interface) : texte, référence, 2e version.</summary>
    string EtatPublicCorps()
    {
        var e = regie.Etat;
        var titre = regie.Cible is CibleChant cc && regie.Trouver(cc.Id) is { } s ? s.Title : regie.ElementCourant?.Titre ?? "";
        var bib = regie.Bibles;
        var p = bib.InfoDe(bib.IdPrincipale)?.Court ?? "";
        var sec = bib.IdSeconde.Length > 0 ? bib.InfoDe(bib.IdSeconde)?.Court ?? "" : "";
        return new JsonObject
        {
            ["mode"] = e.Mode,
            ["lignes"] = new JsonArray(e.Lignes.Select(l => (JsonNode?)l).ToArray()),
            ["ref"] = e.Ref,
            ["second"] = e.Second?.Texte ?? "",
            ["principale"] = e.Mode == "verset" ? p : "",
            ["seconde"] = e.Mode == "verset" && e.Second != null ? sec : "",
            ["titre"] = e.Mode == "chant" ? titre : "",
        }.ToJsonString();
    }

    void EtatPublicChange()
    {
        string corps;
        try { corps = EtatPublicCorps(); }
        catch (Exception e) { Journal.Erreur("page suivre", e); return; }
        TaskCompletionSource ancien;
        lock (verrouPublic)
        {
            if (corps == dernierPublic) return;
            dernierPublic = corps;
            versionPublic++;
            etatPublic = "{\"v\":" + versionPublic + "," + corps[1..];
            ancien = changementPublic;
            changementPublic = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        ancien.TrySetResult();
    }

    async Task Suivre(Stream s, string chemin, string qui)
    {
        int i = chemin.IndexOf('?');
        var route = (i >= 0 ? chemin[..i] : chemin).TrimEnd('/');
        var param = new Dictionary<string, string>();
        if (i >= 0)
            foreach (var morceau in chemin[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = morceau.Split('=', 2);
                try { param.TryAdd(Uri.UnescapeDataString(kv[0]), kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : ""); } catch (UriFormatException) { }
            }
        if (route == "/suivre/etat")
        {
            var id = param.TryGetValue("id", out var x) && x.Length is > 0 and <= 40 ? x : qui.Split(':')[0];
            int v = param.TryGetValue("v", out var vs) && int.TryParse(vs, out var vv) ? vv : -1;
            Task attente;
            lock (verrouPublic)
            {
                suiveurs[id] = DateTime.UtcNow;
                attente = v == versionPublic ? changementPublic.Task : Task.CompletedTask;
            }
            // attente longue : la réponse part dès que l'écran change (au plus 20 s)
            if (!attente.IsCompleted) await Task.WhenAny(attente, Task.Delay(20000, fin.Token));
            string corps;
            lock (verrouPublic) { corps = etatPublic; suiveurs[id] = DateTime.UtcNow; }
            await Repondre(s, "application/json; charset=utf-8", corps);
            return;
        }
        await Repondre(s, "text/html; charset=utf-8", PageSuivre);
    }

    async Task Repondre(Stream s, string type, string texte)
    {
        var corps = Encoding.UTF8.GetBytes(texte);
        var tete = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {corps.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await s.WriteAsync(tete, fin.Token);
        await s.WriteAsync(corps, fin.Token);
        await s.FlushAsync(fin.Token);
    }

    const string PageSuivre = """
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="theme-color" content="#FBF8F2">
<title>MyVerse · en direct</title>
<style>
*{box-sizing:border-box}
html,body{margin:0;background:#FBF8F2;color:#1F1B16;font-family:"Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
header{position:sticky;top:0;display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;padding:12px 16px;background:#FFFFFF;border-bottom:1px solid #EAE3D6}
.marque{font-weight:700;font-size:20px;font-family:Georgia,"Times New Roman",serif}
.direct{display:inline-flex;align-items:center;gap:6px;font-size:12px;font-weight:800;color:#B42318;letter-spacing:.06em}
.direct i{width:8px;height:8px;border-radius:9px;background:#D92D20;display:inline-block}
.outils{margin-left:auto;display:flex;gap:6px;flex-wrap:wrap}
button{font:inherit;font-size:14px;font-weight:600;min-height:40px;padding:0 12px;border:1px solid #E3D9C7;border-radius:12px;background:#FFFFFF;color:#1F1B16}
button[aria-pressed="true"]{background:#E8B04B;border-color:#E8B04B;color:#2A1E05}
main{max-width:860px;margin:0 auto;padding:28px 20px 60px}
#titre{margin:0 0 14px;font-size:13px;font-weight:800;color:#8A5A12;text-transform:uppercase;letter-spacing:.08em}
#texte{margin:0;white-space:pre-line;line-height:1.5;font-family:Georgia,"Times New Roman",serif}
#second{margin:18px 0 0;white-space:pre-line;line-height:1.5;color:#4A433B;padding-top:16px;border-top:1px solid #EAE3D6}
#ref{margin:18px 0 0;font-size:14px;font-weight:800;color:#8A5A12}
#vide{color:#6B6257;font-size:18px}
footer{position:fixed;bottom:0;left:0;right:0;padding:8px 16px;font-size:12px;color:#6B6257;background:#FBF8F2;border-top:1px solid #EAE3D6}
</style>
</head>
<body>
<header>
  <span class="marque">MyVerse</span>
  <span class="direct"><i></i>EN DIRECT</span>
  <div class="outils">
    <span id="langues" hidden>
      <button type="button" id="l1" aria-pressed="false"></button>
      <button type="button" id="l2" aria-pressed="false"></button>
      <button type="button" id="l3" aria-pressed="true">Les deux</button>
    </span>
    <button type="button" id="moins" aria-label="Texte plus petit">A−</button>
    <button type="button" id="plus" aria-label="Texte plus grand">A+</button>
  </div>
</header>
<main>
  <p id="titre"></p>
  <p id="texte"></p>
  <p id="second" hidden></p>
  <p id="ref"></p>
  <p id="vide">Rien à l'écran pour le moment.</p>
</main>
<footer id="etat">Même Wi-Fi que l'église · le texte change tout seul.</footer>
<script>
var lire = function (k, d) { try { return localStorage.getItem(k) || d; } catch (e) { return d; } };
var ecrire = function (k, v) { try { localStorage.setItem(k, v); } catch (e) { } };
var id = lire('mv-id', ''); if (!id) { id = Math.random().toString(36).slice(2, 12); ecrire('mv-id', id); }
var langue = lire('mv-langue', 'deux'), taille = parseFloat(lire('mv-taille', '1')) || 1, v = -1, dernier = null;
function $(x) { return document.getElementById(x); }
function tailles() { $('texte').style.fontSize = (26 * taille) + 'px'; $('second').style.fontSize = (19 * taille) + 'px'; }
function montrer(e) {
  dernier = e;
  var vide = !e || e.mode === 'vide' || !e.lignes || !e.lignes.length;
  $('vide').hidden = !vide; $('texte').hidden = vide; $('ref').hidden = vide || !e.ref; $('titre').hidden = vide || !e.titre;
  if (vide) { $('second').hidden = true; $('langues').hidden = true; return; }
  var deux = e.mode === 'verset' && e.second;
  $('langues').hidden = !deux;
  if (deux) { $('l1').textContent = e.principale || '1'; $('l2').textContent = e.seconde || '2'; }
  var p = e.lignes.join(e.mode === 'verset' ? ' ' : String.fromCharCode(10));
  $('titre').textContent = e.titre || '';
  $('texte').textContent = deux && langue === 'seconde' ? e.second : p;
  $('texte').hidden = false;
  $('second').textContent = deux ? e.second : '';
  $('second').hidden = !(deux && langue === 'deux');
  $('ref').textContent = e.ref || '';
  ['l1', 'l2', 'l3'].forEach(function (b, i) { $(b).setAttribute('aria-pressed', String(['principale', 'seconde', 'deux'][i] === langue)); });
}
$('l1').onclick = function () { langue = 'principale'; ecrire('mv-langue', langue); montrer(dernier); };
$('l2').onclick = function () { langue = 'seconde'; ecrire('mv-langue', langue); montrer(dernier); };
$('l3').onclick = function () { langue = 'deux'; ecrire('mv-langue', langue); montrer(dernier); };
$('moins').onclick = function () { taille = Math.max(0.7, taille - 0.1); ecrire('mv-taille', String(taille)); tailles(); };
$('plus').onclick = function () { taille = Math.min(2.2, taille + 0.1); ecrire('mv-taille', String(taille)); tailles(); };
tailles();
function attendre(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }
async function boucle() {
  for (;;) {
    try {
      var r = await fetch('/suivre/etat?v=' + v + '&id=' + encodeURIComponent(id), { cache: 'no-store' });
      var e = await r.json();
      v = e.v; montrer(e);
      $('etat').textContent = "Même Wi-Fi que l'église · le texte change tout seul.";
    } catch (x) {
      $('etat').textContent = 'Connexion perdue… nouvel essai.';
      await attendre(2000);
    }
  }
}
boucle();
</script>
</body>
</html>
""";

    async Task PageTest(Stream s)
    {
        var html = $"<!doctype html><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><body style=\"font:20px sans-serif;background:#0A1028;color:#F7F4EC;padding:24px\"><h1 style=\"color:#E4AF41\">MyVerse ✓</h1><p>Le PC <b>{WebUtility.HtmlEncode(NomPc)}</b> est joignable sur le port {Port}.</p><p>Si l'app ne se connecte pas, ouvrez MyVerse sur le PC et scannez de nouveau le code QR (bouton « Téléphone »).</p></body>";
        var corps = Encoding.UTF8.GetBytes(html);
        var tete = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corps.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await s.WriteAsync(tete, fin.Token);
        await s.WriteAsync(corps, fin.Token);
        await s.FlushAsync(fin.Token);
    }

    static async Task<JsonObject?> Recevoir(WebSocket ws, CancellationToken t, int max = 1 << 20)
    {
        var buf = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, t);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
            if (ms.Length > max) throw new WebSocketException("message trop long");
            if (r.EndOfMessage) break;
        }
        try { return JsonNode.Parse(ms.ToArray()) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    static async Task EnvoyerBrut(WebSocket ws, string texte, CancellationToken t) =>
        await ws.SendAsync(Encoding.UTF8.GetBytes(texte), WebSocketMessageType.Text, true, t);

    async Task Envoyer(Connexion p, JsonNode msg) => await Envoyer(p, msg.ToJsonString());

    async Task Envoyer(Connexion p, string texte)
    {
        try
        {
            await p.Envoi.WaitAsync(fin.Token);
            try
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
                delai.CancelAfter(15000);
                await EnvoyerBrut(p.Ws, texte, delai.Token);
            }
            finally { p.Envoi.Release(); }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            try { p.Ws.Abort(); } catch { }
        }
    }

    async Task Session(WebSocket ws, string qui)
    {
        async Task Refus(string code, string message)
        {
            Journal.Info($"téléphone {qui} refusé : {code} ({message})");
            try { await EnvoyerBrut(ws, new JsonObject { ["t"] = "auth.fail", ["code"] = code, ["message"] = message }.ToJsonString(), fin.Token); } catch (WebSocketException) { }
        }

        // 1. bonjour
        JsonObject? hello;
        using (var d = CancellationTokenSource.CreateLinkedTokenSource(fin.Token))
        {
            d.CancelAfter(10000);
            hello = await Recevoir(ws, d.Token);
        }
        if (hello == null) return;
        string Champ(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        var deviceId = Champ(hello, "deviceId");
        var cnonce = Champ(hello, "cnonce");
        if (Champ(hello, "t") != "hello" || deviceId.Length == 0 || cnonce.Length == 0)
        {
            await Refus("protocol", "Message de connexion invalide.");
            return;
        }
        var nomTel = Champ(hello, "deviceName").Trim();
        qui = $"« {nomTel} » ({qui})";
        if (Champ(hello, "pcId") != PcId)
        {
            await Refus("wrong_pc", "Ce n'est pas le PC jumelé.");
            return;
        }
        bool jumelage = hello["pair"] is JsonValue pv && pv.TryGetValue<bool>(out var pb) && pb;
        byte[]? cle;
        if (jumelage)
        {
            lock (phones) cle = cleJumelage != null && DateTime.UtcNow < jumelageFin ? DeriverCle(cleJumelage, PcId, deviceId) : null;
            if (cle == null)
            {
                await Refus("pairing_closed", "Code QR expiré : ouvrez « Téléphone » dans MyVerse et scannez de nouveau.");
                return;
            }
        }
        else
        {
            cle = store.Cle(deviceId);
            if (cle == null)
            {
                await Refus("revoked", "Ce téléphone n'est plus jumelé avec ce PC.");
                return;
            }
        }

        // 2. défi : nonce + preuve que le PC connaît la clé
        var nonce = Hasard(32);
        await EnvoyerBrut(ws, new JsonObject { ["t"] = "challenge", ["v"] = Protocole, ["nonce"] = nonce, ["proof"] = PreuveServeur(cle, cnonce, nonce), ["pcName"] = NomPc }.ToJsonString(), fin.Token);

        // 3. réponse HMAC(clé, nonce + deviceId)
        JsonObject? auth;
        using (var d = CancellationTokenSource.CreateLinkedTokenSource(fin.Token))
        {
            d.CancelAfter(10000);
            auth = await Recevoir(ws, d.Token);
        }
        if (auth == null) return;
        var attendu = Encoding.ASCII.GetBytes(MacClient(cle, nonce, deviceId));
        if (Champ(auth, "t") != "auth" || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Champ(auth, "mac")), attendu))
        {
            if (jumelage) await Refus("bad_secret", "Code QR invalide : scannez le nouveau code.");
            else await Refus("revoked", "Ce téléphone n'est plus jumelé avec ce PC.");
            return;
        }
        var nom = nomTel.Length > 0 ? nomTel : "Téléphone";
        if (jumelage)
        {
            store.Ajouter(deviceId, nom, cle);
            Journal.Info($"nouveau téléphone jumelé : {nom}");
        }
        else store.Vu(deviceId, nom);

        Journal.Info($"téléphone connecté : {qui}");
        var p = new Connexion { Ws = ws, DeviceId = deviceId, Nom = nom };
        lock (phones) phones.Add(p);
        try
        {
            await Envoyer(p, new JsonObject { ["t"] = "welcome", ["v"] = Protocole, ["pcName"] = NomPc, ["paired"] = jumelage });
            if (dernierEtat.Length > 0) await Envoyer(p, dernierEtat);
            Prevenir();

            // 4. commandes
            while (ws.State == WebSocketState.Open && !fin.IsCancellationRequested)
            {
                var m = await Recevoir(ws, fin.Token);
                if (m == null) break;
                var t = Champ(m, "t");
                if (t.Length == 0) continue;
                if (t == "ping") { await Envoyer(p, new JsonObject { ["t"] = "pong" }); continue; }
                _ = Traiter(p, t, m);
            }
        }
        finally
        {
            lock (phones) phones.Remove(p);
            Journal.Info($"téléphone déconnecté : {qui}");
            Prevenir();
        }
        // fermeture demandée par le téléphone : on la confirme
        if (ws.State == WebSocketState.CloseReceived)
        {
            await p.Envoi.WaitAsync();
            try
            {
                using var d = new CancellationTokenSource(2000);
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", d.Token);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
            finally { p.Envoi.Release(); }
        }
    }

    // ------------------------------------------------------------ commandes reçues des téléphones
    async Task Traiter(Connexion p, string t, JsonObject d)
    {
        var rid = d["rid"]?.DeepClone();
        Task Rep(JsonObject msg)
        {
            var o = new JsonObject();
            if (rid != null) o["rid"] = rid.DeepClone();
            foreach (var (k, v) in msg) o[k] = v?.DeepClone();
            return Envoyer(p, o);
        }
        Task Erreur(string message) => Rep(new JsonObject { ["t"] = "error", ["message"] = message });
        string Ch(string k) => d[k] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : "";
        try
        {
            switch (t)
            {
                case "bible.preview":
                {
                    Apercu? a = null;
                    await surUi(() => { a = regie.ApercuRef(Ch("ref")); return Task.CompletedTask; });
                    if (a == null) { await Erreur($"Référence introuvable : « {Ch("ref")} »"); break; }
                    var vs = new JsonArray();
                    foreach (var (v, tx) in a.Versets) vs.Add(new JsonObject { ["v"] = v, ["t"] = tx });
                    await Rep(new JsonObject { ["t"] = "preview", ["ref"] = a.Ref, ["lignes"] = new JsonArray(a.Lignes.Select(x => (JsonNode?)x).ToArray()), ["versets"] = vs, ["livre"] = a.Livre, ["c"] = a.C });
                    break;
                }
                case "bible.search":
                {
                    var q = Ch("q").Trim();
                    List<Bible> versions = new();
                    await surUi(() => { versions = regie.Bibles.Versions(); return Task.CompletedTask; });
                    // mots même mal écrits, dans toutes les Bibles installées ; « v » = version où le verset a été trouvé
                    var res = q.Length >= 3 ? await Task.Run(() => Recherche.Chercher(versions, q, 60, out _)) : new List<Recherche.Resultat>();
                    var liste = new JsonArray();
                    foreach (var x in res) liste.Add(new JsonObject { ["ref"] = Livres.Libelle(x.B, x.C, x.V), ["text"] = x.Texte, ["v"] = x.Principale ? "" : x.Version });
                    await Rep(new JsonObject { ["t"] = "search", ["q"] = q, ["liste"] = liste });
                    break;
                }
                case "bible.show":
                {
                    bool ok = false;
                    await surUi(() => { ok = regie.AfficherRef(Ch("ref")); return Task.CompletedTask; });
                    if (!ok) await Erreur($"Référence introuvable : « {Ch("ref")} »");
                    break;
                }
                case "song.search":
                {
                    var liste = new JsonArray();
                    await surUi(() =>
                    {
                        foreach (var c in regie.ChercherChants(Ch("q")))
                            liste.Add(new JsonObject { ["id"] = c.Id, ["title"] = Texte.Espaces.Replace(c.Title, " "), ["author"] = c.Author ?? "" });
                        return Task.CompletedTask;
                    });
                    await Rep(new JsonObject { ["t"] = "songs", ["q"] = Ch("q"), ["liste"] = liste });
                    break;
                }
                case "song.open":
                {
                    JsonObject? r = null;
                    await surUi(() =>
                    {
                        var c = regie.Trouver(Ch("id"));
                        if (c != null)
                            r = new JsonObject
                            {
                                ["t"] = "song", ["id"] = c.Id, ["title"] = Texte.Espaces.Replace(c.Title, " "), ["author"] = c.Author ?? "",
                                ["sections"] = new JsonArray(c.Sections.Select(s => (JsonNode?)new JsonObject { ["label"] = s.Label ?? "", ["text"] = s.Text }).ToArray()),
                            };
                        return Task.CompletedTask;
                    });
                    if (r != null) await Rep(r); else await Erreur("Chant introuvable.");
                    break;
                }
                case "song.show":
                {
                    bool ok = false;
                    int i = d["i"] is JsonValue iv && (iv.TryGetValue<int>(out var ii) || (iv.TryGetValue<string>(out var si) && int.TryParse(si, out ii))) ? ii : 0;
                    await surUi(() =>
                    {
                        if (regie.Trouver(Ch("id")) != null) { ok = true; regie.AfficherSection(Ch("id"), i); }
                        return Task.CompletedTask;
                    });
                    if (!ok) await Erreur("Chant introuvable.");
                    break;
                }
                case "next": await surUi(() => { regie.Bouger(1); return Task.CompletedTask; }); break;
                case "prev": await surUi(() => { regie.Bouger(-1); return Task.CompletedTask; }); break;
                case "clear": await surUi(() => { regie.Effacer(); return Task.CompletedTask; }); break;
                case "state.get":
                {
                    string etat = "";
                    await surUi(() => { etat = EtatTelephone(); return Task.CompletedTask; });
                    dernierEtat = etat;
                    await Envoyer(p, etat);
                    break;
                }
                // le téléphone garde une copie des Bibles (version principale, noms des livres) pour le détecteur de versets
                case "sync.get":
                {
                    JsonObject? r = null;
                    await surUi(() =>
                    {
                        var e = regie.Etat;
                        var bibles = new JsonArray();
                        foreach (var v in regie.Bibles.Toutes()) bibles.Add(new JsonObject { ["id"] = v.Id, ["short"] = v.Court, ["name"] = v.Nom });
                        r = new JsonObject
                        {
                            ["t"] = "sync",
                            ["reglages"] = new JsonObject
                            {
                                ["principale"] = regie.Bibles.IdPrincipale, ["seconde"] = regie.Bibles.IdSeconde, ["theme"] = e.Theme, ["taille"] = e.Taille, ["presentation"] = e.Presentation,
                            },
                            ["bibles"] = bibles,
                        };
                        return Task.CompletedTask;
                    });
                    await Rep(r!);
                    break;
                }
                case "bible.get":
                {
                    Bible? b = null;
                    Bibles.Info? info = null;
                    var id = Ch("id");
                    await surUi(() => { info = regie.Bibles.InfoDe(id); b = info != null ? (id == regie.Bibles.IdPrincipale ? regie.Bibles.Principale : regie.Bibles.Charger(id)) : null; return Task.CompletedTask; });
                    if (b == null || info == null) { await Erreur("Bible introuvable sur le PC."); break; }
                    var gz = await Task.Run(() =>
                    {
                        var copie = new Bible { Nom = info.Nom, Court = info.Court, Livres = b.Livres };
                        using var ms = new MemoryStream();
                        using (var z = new GZipStream(ms, CompressionLevel.Optimal, true))
                        {
                            var octets = Encoding.UTF8.GetBytes(copie.VersJson());
                            z.Write(octets);
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    });
                    await Rep(new JsonObject { ["t"] = "bible", ["id"] = info.Id, ["short"] = info.Court, ["name"] = info.Nom, ["gz"] = gz });
                    Journal.Info($"Bible « {info.Court} » envoyée au téléphone {p.Nom}");
                    break;
                }
                default:
                    Journal.Erreur("commande inconnue du téléphone : " + t);
                    await Erreur("Commande inconnue : " + t);
                    break;
            }
        }
        catch (Exception e)
        {
            Journal.Erreur($"commande du téléphone « {t} »", e);
        }
    }

    // ------------------------------------------------------------ état envoyé aux téléphones
    /// <summary>État de la projection pour les téléphones (à appeler sur le fil de l'interface).</summary>
    public string EtatTelephone()
    {
        var e = regie.Etat;
        var c = regie.Cible;
        var titre = c is CibleChant cc && regie.Trouver(cc.Id) is { } s ? s.Title : "";
        var n = regie.Prochaine();
        var (obsActif, obsConnecte) = EtatObs();
        return new JsonObject
        {
            ["t"] = "state",
            ["mode"] = e.Mode,
            ["lignes"] = new JsonArray(e.Lignes.Select(l => (JsonNode?)l).ToArray()),
            ["ref"] = e.Ref,
            ["titre"] = titre,
            ["suivant"] = n != null ? n.Label + " · " + n.Texte.Split('\n')[0] : "",
            ["suivantLabel"] = n?.Label ?? "",
            ["suivantTexte"] = n?.Texte ?? "",
            ["chant"] = c is CibleChant ch ? new JsonObject { ["id"] = ch.Id, ["i"] = ch.I } : null,
            ["ecranOuvert"] = regie.EcranOuvert(),
            ["obsActif"] = obsActif,
            ["obsConnecte"] = obsConnecte,
        }.ToJsonString();
    }

    /// <summary>La régie a changé (fil de l'interface) : l'état part vers les téléphones (regroupé, sans doublon).</summary>
    void EtatChange()
    {
        EtatPublicChange();
        string etat;
        try { etat = EtatTelephone(); }
        catch (Exception e) { Journal.Erreur("état pour les téléphones", e); return; }
        if (etat == dernierEtat) return;
        dernierEtat = etat;
        minuterieEtat ??= new Timer(_ => Diffuser(), null, Timeout.Infinite, Timeout.Infinite);
        minuterieEtat.Change(60, Timeout.Infinite);
    }

    /// <summary>À appeler quand l'écran s'ouvre ou se ferme, ou qu'OBS change (fil de l'interface).</summary>
    public void Actualiser() => EtatChange();

    void Diffuser()
    {
        var e = dernierEtat;
        List<Connexion> a;
        lock (phones) a = phones.ToList();
        foreach (var p in a) _ = Envoyer(p, e);
    }

    public void Dispose()
    {
        fin.Cancel();
        regie.Change -= EtatChange;
        try { ecoute?.Stop(); } catch { }
        try { udp?.Dispose(); } catch { }
        List<Connexion> a;
        lock (phones) a = phones.ToList();
        foreach (var p in a) try { p.Ws.Abort(); } catch { }
        minuterieEtat?.Dispose();
    }
}
