package ca.mepc.verselight.remote

import android.util.Base64
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * Pilotage direct d'OBS par le téléphone (obs-websocket 5, inclus dans OBS 28 et plus), quand MyVerse est fermé
 * sur le PC. Même principe que obs.js : « Afficher » met la source « MyVerse (temporaire) » au premier plan de la
 * scène en cours, « Retirer » la supprime et la scène redevient comme avant.
 */
class Obs(private val log: (String) -> Unit) {
    private val client = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(4, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.SECONDS)
        .build()
    @Volatile private var ws: WebSocket? = null
    @Volatile var pret = false
        private set
    private val attente = ConcurrentHashMap<String, CompletableDeferred<JSONObject>>()
    private val numero = AtomicInteger(1)
    /** Appelé quand la connexion à OBS se perd. */
    var surPerte: (() -> Unit)? = null

    private fun sha64(s: String): String =
        Base64.encodeToString(MessageDigest.getInstance("SHA-256").digest(s.toByteArray(Charsets.UTF_8)), Base64.NO_WRAP)

    /** Connexion et authentification. Renvoie null si tout va bien, sinon le message d'erreur. */
    suspend fun connecter(host: String, port: Int, mdp: String): String? {
        fermer()
        val fini = CompletableDeferred<String>() // "" : connecté ; sinon le message d'erreur
        val socket = client.newWebSocket(Request.Builder().url("ws://$host:$port/").build(), object : WebSocketListener() {
            override fun onMessage(webSocket: WebSocket, text: String) {
                val m = try { JSONObject(text) } catch (e: Exception) { return }
                val d = m.optJSONObject("d") ?: JSONObject()
                when (m.optInt("op", -1)) {
                    0 -> { // Hello : authentification éventuelle
                        val id = JSONObject().put("rpcVersion", 1).put("eventSubscriptions", 0)
                        d.optJSONObject("authentication")?.let { a ->
                            id.put("authentication", sha64(sha64(mdp + a.optString("salt")) + a.optString("challenge")))
                        }
                        webSocket.send(JSONObject().put("op", 1).put("d", id).toString())
                    }
                    2 -> { pret = true; fini.complete("") } // Identified
                    7 -> attente.remove(d.optString("requestId"))?.complete(d)
                }
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = perdu(webSocket, code, reason)
            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) { webSocket.close(code, null) }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) =
                perdu(webSocket, 0, t.message ?: t.javaClass.simpleName)

            private fun perdu(webSocket: WebSocket, code: Int, reason: String) {
                val etait = pret && ws === webSocket
                if (ws === webSocket) { pret = false; ws = null }
                val msg = when (code) {
                    4009 -> "mot de passe OBS incorrect (OBS › Outils › Paramètres du serveur WebSocket)"
                    0 -> "OBS injoignable sur $host:$port ($reason) : OBS est-il ouvert, avec le serveur WebSocket activé ?"
                    else -> "OBS a fermé la connexion ($code ${reason.ifBlank { "" }})".trim()
                }
                fini.complete(msg)
                attente.values.forEach { it.complete(JSONObject()) }
                attente.clear()
                if (etait) { log("OBS direct : connexion perdue ($msg)"); surPerte?.invoke() }
            }
        })
        ws = socket
        val r = withTimeoutOrNull(6000) { fini.await() } ?: "OBS ne répond pas sur $host:$port"
        if (r.isEmpty()) return null
        socket.cancel()
        if (ws === socket) ws = null
        pret = false
        return r
    }

    fun fermer() {
        pret = false
        ws?.close(1000, null)
        ws = null
    }

    /** Requête obs-websocket : la réponse (d), ou null si OBS ne répond pas. */
    suspend fun req(type: String, data: JSONObject = JSONObject()): JSONObject? {
        val s = ws ?: return null
        if (!pret) return null
        val id = numero.getAndIncrement().toString()
        val d = CompletableDeferred<JSONObject>()
        attente[id] = d
        val msg = JSONObject().put("op", 6).put("d", JSONObject().put("requestType", type).put("requestId", id).put("requestData", data))
        if (!s.send(msg.toString())) { attente.remove(id); return null }
        val r = withTimeoutOrNull(4000) { d.await() }
        attente.remove(id)
        return r
    }

    private fun ok(r: JSONObject?) = r?.optJSONObject("requestStatus")?.optBoolean("result") == true
    private fun donnees(r: JSONObject?) = r?.optJSONObject("responseData") ?: JSONObject()

    private suspend fun scene(): String? {
        val d = donnees(req("GetCurrentProgramScene"))
        return d.optString("currentProgramSceneName").ifEmpty { d.optString("sceneName") }.ifEmpty { null }
    }

    private suspend fun elements(scene: String): JSONArray =
        donnees(req("GetSceneItemList", JSONObject().put("sceneName", scene))).optJSONArray("sceneItems") ?: JSONArray()

    /**
     * Afficher : la source temporaire passe au premier plan de la scène en cours (créée si besoin) et montre [etat].
     * [page] fabrique la page de la source navigateur avec l'état à afficher. Renvoie false si OBS n'a pas suivi.
     */
    suspend fun montrer(etat: JSONObject, page: (JSONObject) -> String): Boolean {
        val sc = scene() ?: return false
        val items = elements(sc)
        var id: Int? = null
        for (i in 0 until items.length()) {
            val it = items.optJSONObject(i) ?: continue
            if (it.optString("sourceName") == AUTO) id = it.optInt("sceneItemId")
        }
        val reglages = req("GetInputSettings", JSONObject().put("inputName", AUTO))
        if (ok(reglages)) {
            val s = donnees(reglages).optJSONObject("inputSettings") ?: JSONObject()
            val notrePage = !s.optBoolean("is_local_file", false) && s.optString("url").startsWith("data:text/html")
            // notre page est déjà là : on lui envoie le nouvel état (sans recharger) ; sinon on la met
            val envoye = notrePage && ok(req("CallVendorRequest", JSONObject().put("vendorName", "obs-browser").put("requestType", "emit_event")
                .put("requestData", JSONObject().put("event_name", "myverse").put("event_data", etat))))
            if (!envoye) req("SetInputSettings", JSONObject().put("inputName", AUTO).put("inputSettings", JSONObject().put("is_local_file", false).put("url", page(etat))))
            if (id == null) {
                val r = req("CreateSceneItem", JSONObject().put("sceneName", sc).put("sourceName", AUTO).put("sceneItemEnabled", true))
                if (ok(r)) id = donnees(r).optInt("sceneItemId")
            }
        } else {
            val v = donnees(req("GetVideoSettings"))
            val w = v.optInt("baseWidth", 1920).takeIf { it > 0 } ?: 1920
            val h = v.optInt("baseHeight", 1080).takeIf { it > 0 } ?: 1080
            val r = req("CreateInput", JSONObject().put("sceneName", sc).put("inputName", AUTO).put("inputKind", "browser_source").put("sceneItemEnabled", true)
                .put("inputSettings", JSONObject().put("is_local_file", false).put("url", page(etat)).put("width", w).put("height", h).put("reroute_audio", false)))
            if (!ok(r)) {
                log("OBS direct : création de la source impossible (${r?.optJSONObject("requestStatus")?.optString("comment") ?: "pas de réponse"})")
                return false
            }
            id = donnees(r).optInt("sceneItemId")
        }
        val item = id ?: return false
        req("SetSceneItemEnabled", JSONObject().put("sceneName", sc).put("sceneItemId", item).put("sceneItemEnabled", true))
        val n = elements(sc).length()
        if (n > 0) req("SetSceneItemIndex", JSONObject().put("sceneName", sc).put("sceneItemId", item).put("sceneItemIndex", n - 1))
        return true
    }

    /** Retirer : la source temporaire est supprimée, la scène redevient comme avant. */
    suspend fun cacher() {
        req("RemoveInput", JSONObject().put("inputName", AUTO))
    }

    companion object {
        const val AUTO = "MyVerse (temporaire)"
    }
}
