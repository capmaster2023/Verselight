using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyVerse;

/// <summary>
/// Sortie OBS sans fenêtre : dossier %APPDATA%\VerseLight-OBS avec ecran.html (source navigateur « Fichier local »),
/// qui relit etat.json (écrit ici à chaque changement).
/// </summary>
public sealed class SortieObs
{
    readonly string modele;
    Timer? minuterie;
    EtatEcran? enAttente;
    readonly object verrou = new();

    public string Dossier { get; }
    public string Page => Path.Combine(Dossier, "ecran.html");
    public bool Prete { get; private set; }

    /// <param name="modele">Dossier des fichiers de la page (Ressources\obs : ecran.html, css, fonts).</param>
    public SortieObs(string modele, string? dossier = null)
    {
        this.modele = modele;
        Dossier = dossier ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VerseLight-OBS");
    }

    /// <summary>Copie la page et ses polices dans le dossier OBS (si elles ont changé).</summary>
    public bool Preparer()
    {
        try
        {
            if (!Directory.Exists(modele)) { Journal.Erreur("page OBS introuvable : " + modele); return false; }
            foreach (var f in Directory.GetFiles(modele, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(modele, f);
                var dest = Path.Combine(Dossier, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var fi = new FileInfo(dest);
                if (!fi.Exists || fi.Length != new FileInfo(f).Length || fi.LastWriteTimeUtc < File.GetLastWriteTimeUtc(f))
                    File.Copy(f, dest, true);
            }
            Prete = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Journal.Erreur("préparation de la sortie OBS", e);
        }
        return Prete;
    }

    /// <summary>Écrit l'état pour la page OBS (regroupé : au plus une écriture toutes les 30 ms).</summary>
    public void Ecrire(EtatEcran e)
    {
        if (!Prete) return;
        lock (verrou)
        {
            enAttente = e;
            minuterie ??= new Timer(_ => Vider(), null, Timeout.Infinite, Timeout.Infinite);
            minuterie.Change(30, Timeout.Infinite);
        }
    }

    void Vider()
    {
        EtatEcran? e;
        lock (verrou) { e = enAttente; enAttente = null; }
        if (e == null) return;
        var n = JsonSerializer.SerializeToNode(e, Donnees.Json)!.AsObject();
        n["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int essai = 0; essai < 3; essai++)
        {
            try
            {
                var tmp = Path.Combine(Dossier, "etat.json.tmp");
                File.WriteAllText(tmp, n.ToJsonString(Donnees.Json), new UTF8Encoding(false));
                File.Move(tmp, Path.Combine(Dossier, "etat.json"), true);
                return;
            }
            catch (IOException) { Thread.Sleep(15); } // OBS est peut-être en train de le lire
            catch (UnauthorizedAccessException) { Thread.Sleep(15); }
        }
    }
}

/// <summary>
/// Pilotage d'OBS (obs-websocket 5, inclus dans OBS 28 et plus) :
/// « Afficher » met la source MyVerse au premier plan de la scène en cours (créée si besoin),
/// « Retirer » la supprime et remet la scène comme avant.
/// </summary>
public sealed class Obs : IDisposable
{
    public const string AUTO = "MyChurch (temporaire)";

    readonly Func<ReglagesObs> cfg;
    readonly SortieObs sortie;
    readonly CancellationTokenSource fin = new();
    readonly Dictionary<string, TaskCompletionSource<JsonElement?>> attente = new();
    readonly Dictionary<string, (string Scene, int Id, int Index, bool Visible)> places = new();
    readonly Dictionary<string, bool> estMyVerse = new();
    readonly SemaphoreSlim envoi = new(1, 1);
    readonly object verrou = new();
    ClientWebSocket? ws;
    CancellationTokenSource? connexion;
    Task file = Task.CompletedTask;
    int n = 1;
    bool ok, mauvaisMdp;

    public bool Connecte => ok;
    public bool Actif => cfg().Actif;
    public string Statut { get; private set; } = "OBS : désactivé";
    /// <summary>Le statut a changé (thread quelconque).</summary>
    public event Action? Change;

    public Obs(Func<ReglagesObs> reglages, SortieObs sortie)
    {
        cfg = reglages;
        this.sortie = sortie;
        _ = Boucle();
    }

    void Statuer(string s, bool connecte)
    {
        bool avant = ok;
        ok = connecte;
        if (s == Statut && avant == connecte) return;
        Statut = s;
        try { Change?.Invoke(); } catch { }
    }

    /// <summary>À appeler après un changement de réglages : nouvelle connexion.</summary>
    public void Reconnecter()
    {
        mauvaisMdp = false;
        lock (verrou) { connexion?.Cancel(); reveil.Cancel(); }
    }
    CancellationTokenSource reveil = new();

    async Task Boucle()
    {
        while (!fin.IsCancellationRequested)
        {
            var c = cfg();
            if (!c.Actif) { Statuer("OBS : désactivé", false); await Pause(1000); continue; }
            if (mauvaisMdp) { await Pause(1000); continue; }
            Statuer("OBS : connexion…", false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
            lock (verrou) connexion = cts;
            try { await Session(c, cts.Token); }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or JsonException or HttpRequestException) { }
            catch (Exception e) { Journal.Erreur("OBS", e); }
            bool etait = ok;
            if (etait) Journal.Info("OBS déconnecté");
            Annuler();
            if (!mauvaisMdp) Statuer(c.Actif ? "OBS : non connecté" : "OBS : désactivé", false);
            lock (verrou) connexion = null;
            if (!cts.IsCancellationRequested) await Pause(5000);
        }
    }

    /// <summary>Attente, interrompue par Reconnecter() ou la fermeture.</summary>
    async Task Pause(int ms)
    {
        CancellationTokenSource r;
        lock (verrou) r = reveil;
        using (var l = CancellationTokenSource.CreateLinkedTokenSource(fin.Token, r.Token))
            try { await Task.Delay(ms, l.Token); } catch (OperationCanceledException) { }
        lock (verrou) if (r.IsCancellationRequested && reveil == r) reveil = new CancellationTokenSource();
    }

    void Annuler()
    {
        lock (attente)
        {
            foreach (var t in attente.Values) t.TrySetResult(null);
            attente.Clear();
        }
        lock (places) { estMyVerse.Clear(); }
    }

    static string Sha(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>Clé d'authentification d'obs-websocket 5.</summary>
    public static string Authentification(string mdp, string sel, string defi) => Sha(Sha(mdp + sel) + defi);

    async Task Session(ReglagesObs c, CancellationToken t)
    {
        using var s = new ClientWebSocket();
        ws = s;
        using (var delai = CancellationTokenSource.CreateLinkedTokenSource(t))
        {
            delai.CancelAfter(4000);
            await s.ConnectAsync(new Uri($"ws://127.0.0.1:{c.Port}"), delai.Token);
        }
        var buf = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (!t.IsCancellationRequested && s.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await s.ReceiveAsync(buf, t);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    if ((int?)r.CloseStatus == 4009)
                    {
                        mauvaisMdp = true;
                        Journal.Erreur("OBS : mot de passe WebSocket incorrect");
                        Statuer("OBS : mot de passe incorrect", false);
                    }
                    return;
                }
                ms.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            using var doc = JsonDocument.Parse(ms.ToArray());
            var m = doc.RootElement;
            int op = m.GetProperty("op").GetInt32();
            var d = m.GetProperty("d");
            if (op == 0)
            {
                var id = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
                if (d.TryGetProperty("authentication", out var au))
                    id["authentication"] = Authentification(c.Mdp ?? "", au.GetProperty("salt").GetString() ?? "", au.GetProperty("challenge").GetString() ?? "");
                await Envoyer(new JsonObject { ["op"] = 1, ["d"] = id }, t);
            }
            else if (op == 2)
            {
                Statuer("OBS : connecté", true);
                Journal.Info($"OBS connecté (port {c.Port})");
            }
            else if (op == 7)
            {
                var rid = d.GetProperty("requestId").GetString() ?? "";
                TaskCompletionSource<JsonElement?>? tcs;
                lock (attente) { if (attente.Remove(rid, out tcs)) { } }
                tcs?.TrySetResult(d.Clone());
            }
        }
    }

    async Task Envoyer(JsonObject o, CancellationToken t)
    {
        var s = ws;
        if (s == null || s.State != WebSocketState.Open) return;
        var b = Encoding.UTF8.GetBytes(o.ToJsonString());
        await envoi.WaitAsync(t);
        try { await s.SendAsync(b, WebSocketMessageType.Text, true, t); }
        finally { envoi.Release(); }
    }

    /// <summary>Requête obs-websocket ; null si OBS ne répond pas.</summary>
    public async Task<JsonElement?> Req(string type, JsonObject? data = null)
    {
        if (!ok) return null;
        var id = Interlocked.Increment(ref n).ToString();
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (attente) attente[id] = tcs;
        try
        {
            await Envoyer(new JsonObject { ["op"] = 6, ["d"] = new JsonObject { ["requestType"] = type, ["requestId"] = id, ["requestData"] = data ?? new JsonObject() } }, fin.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            lock (attente) attente.Remove(id);
            return null;
        }
        var fini = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        lock (attente) attente.Remove(id);
        return fini == tcs.Task ? tcs.Task.Result : null;
    }

    static bool Reussi(JsonElement? r) => r is { } x && x.TryGetProperty("requestStatus", out var st) && st.TryGetProperty("result", out var ok) && ok.ValueKind == JsonValueKind.True;
    static JsonElement? Reponse(JsonElement? r) => r is { } x && x.TryGetProperty("responseData", out var d) ? d : null;

    // ------------------------------------------------------------ afficher / retirer
    string FichierPage => sortie.Page.Replace('\\', '/');

    async Task<bool> SourceMyVerse(string nom, string genre)
    {
        if (genre is not ("window_capture" or "game_capture" or "browser_source")) return false;
        lock (places) if (estMyVerse.TryGetValue(nom, out var v)) return v;
        var r = Reponse(await Req("GetInputSettings", new JsonObject { ["inputName"] = nom }));
        var s = r is { } x && x.TryGetProperty("inputSettings", out var i) ? i : default;
        string Champ(string k) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        bool oui = System.Text.RegularExpressions.Regex.IsMatch(Champ("window") + " " + Champ("local_file") + " " + Champ("url"), "MyVerse|VerseLight", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        lock (places) estMyVerse[nom] = oui;
        return oui;
    }

    async Task<string?> ScenePrincipale()
    {
        var r = Reponse(await Req("GetCurrentProgramScene"));
        if (r is not { } d) return null;
        foreach (var k in new[] { "currentProgramSceneName", "sceneName" })
            if (d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        return null;
    }

    async Task<List<JsonElement>> Elements(string scene)
    {
        var r = Reponse(await Req("GetSceneItemList", new JsonObject { ["sceneName"] = scene }));
        return r is { } d && d.TryGetProperty("sceneItems", out var it) && it.ValueKind == JsonValueKind.Array ? it.EnumerateArray().ToList() : new List<JsonElement>();
    }

    async Task PremierPlan(string scene, int id)
    {
        int nb = (await Elements(scene)).Count;
        if (nb > 0) await Req("SetSceneItemIndex", new JsonObject { ["sceneName"] = scene, ["sceneItemId"] = id, ["sceneItemIndex"] = nb - 1 });
    }

    /// <summary>La source temporaire pointe vers la page de ce PC (et non une ancienne page).</summary>
    async Task RemettreFichier()
    {
        var r = Reponse(await Req("GetInputSettings", new JsonObject { ["inputName"] = AUTO }));
        if (r is { } d && d.TryGetProperty("inputSettings", out var s) && s.TryGetProperty("local_file", out var lf) && lf.GetString() == FichierPage
            && s.TryGetProperty("is_local_file", out var il) && il.ValueKind == JsonValueKind.True) return;
        await Req("SetInputSettings", new JsonObject { ["inputName"] = AUTO, ["inputSettings"] = new JsonObject { ["is_local_file"] = true, ["local_file"] = FichierPage } });
    }

    async Task Montrer()
    {
        var scene = await ScenePrincipale();
        if (scene == null) return;
        var items = await Elements(scene);
        int? id = null;
        foreach (var it in items)
            if (it.GetProperty("sourceName").GetString() == AUTO) { id = it.GetProperty("sceneItemId").GetInt32(); await RemettreFichier(); break; }
        if (id == null)
            foreach (var it in items)
            {
                var nom = it.GetProperty("sourceName").GetString() ?? "";
                var genre = it.TryGetProperty("inputKind", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
                if (nom == cfg().Source || await SourceMyVerse(nom, genre))
                {
                    int sid = it.GetProperty("sceneItemId").GetInt32();
                    bool visible = it.TryGetProperty("sceneItemEnabled", out var en) && en.ValueKind == JsonValueKind.True;
                    lock (places)
                    {
                        var cle = scene + "|" + sid;
                        if (!places.ContainsKey(cle)) places[cle] = (scene, sid, it.TryGetProperty("sceneItemIndex", out var ix) ? ix.GetInt32() : 0, visible);
                    }
                    if (!visible) await Req("SetSceneItemEnabled", new JsonObject { ["sceneName"] = scene, ["sceneItemId"] = sid, ["sceneItemEnabled"] = true });
                    id = sid;
                    break;
                }
            }
        if (id == null)
        {
            // aucune source MyVerse dans cette scène : source navigateur temporaire
            if (Reussi(await Req("GetInputSettings", new JsonObject { ["inputName"] = AUTO })))
            {
                await RemettreFichier();
                var r = await Req("CreateSceneItem", new JsonObject { ["sceneName"] = scene, ["sourceName"] = AUTO, ["sceneItemEnabled"] = true });
                if (Reussi(r) && Reponse(r) is { } d) id = d.GetProperty("sceneItemId").GetInt32();
            }
            else
            {
                if (!sortie.Prete && !sortie.Preparer()) return;
                var r = await Req("CreateInput", new JsonObject
                {
                    ["sceneName"] = scene, ["inputName"] = AUTO, ["inputKind"] = "browser_source", ["sceneItemEnabled"] = true,
                    ["inputSettings"] = new JsonObject { ["is_local_file"] = true, ["local_file"] = FichierPage, ["width"] = 1920, ["height"] = 1080 },
                });
                if (Reussi(r) && Reponse(r) is { } d) id = d.GetProperty("sceneItemId").GetInt32();
            }
        }
        if (id is int x) await PremierPlan(scene, x);
    }

    async Task Cacher()
    {
        await Req("RemoveInput", new JsonObject { ["inputName"] = AUTO });
        List<(string Scene, int Id, int Index, bool Visible)> liste;
        lock (places) { liste = places.Values.ToList(); places.Clear(); }
        foreach (var p in liste)
        {
            await Req("SetSceneItemEnabled", new JsonObject { ["sceneName"] = p.Scene, ["sceneItemId"] = p.Id, ["sceneItemEnabled"] = false });
            await Req("SetSceneItemIndex", new JsonObject { ["sceneName"] = p.Scene, ["sceneItemId"] = p.Id, ["sceneItemIndex"] = p.Index });
        }
    }

    /// <summary>Afficher (vrai) ou retirer (faux) : une commande à la fois, dans l'ordre.</summary>
    public Task Oeil(bool visible)
    {
        if (!Actif || !ok) return Task.CompletedTask;
        lock (verrou)
        {
            file = file.ContinueWith(async _ =>
            {
                try { if (visible) await Montrer(); else await Cacher(); }
                catch (Exception e) { Journal.Erreur("OBS : " + (visible ? "afficher" : "retirer"), e); }
            }, TaskScheduler.Default).Unwrap();
            return file;
        }
    }

    public void Dispose()
    {
        fin.Cancel();
        try { ws?.Abort(); } catch { }
    }
}
