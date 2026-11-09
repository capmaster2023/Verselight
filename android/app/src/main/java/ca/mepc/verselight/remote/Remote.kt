package ca.mepc.verselight.remote

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.util.Base64
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.concurrent.TimeUnit
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

enum class Link { NOT_PAIRED, PAIRING, CONNECTING, CONNECTED, OFFLINE }

/** Connexion au bridge VerseLight : jumelage, authentification HMAC, reconnexion automatique. */
class Remote(private val context: Context, val store: Store) {
    val link = MutableStateFlow(if (store.pairing() == null) Link.NOT_PAIRED else Link.OFFLINE)
    val info = MutableStateFlow("")                    // message affiché (erreur de jumelage, révocation…)
    val screen = MutableStateFlow<JSONObject?>(null)   // dernier « state » reçu
    val events = MutableSharedFlow<JSONObject>(extraBufferCapacity = 64) // preview, songs, song, error
    val pcName = MutableStateFlow(store.pairing()?.pcName ?: "")
    val update = MutableStateFlow<Pair<String, String>?>(null) // nouvelle version : (numéro, lien de l'APK)
    private var updateChecked = false

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.SECONDS)
        .build()
    private val wake = Channel<Unit>(Channel.CONFLATED)
    private val random = SecureRandom()
    @Volatile private var socket: WebSocket? = null
    @Volatile private var running = false
    private var job: Job? = null
    private var rid = 0

    private enum class Result { CLOSED, FAILED, REVOKED }
    private class Rejected(val code: String, msg: String) : Exception(msg)

    init {
        // retour du Wi-Fi : on reprend tout de suite, et on passe par le Wi-Fi même s'il n'a pas Internet
        val cm = context.getSystemService(ConnectivityManager::class.java)
        val req = NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build()
        cm.registerNetworkCallback(req, object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                cm.bindProcessToNetwork(network)
                wake.trySend(Unit)
            }

            override fun onLost(network: Network) {
                cm.bindProcessToNetwork(null)
                socket?.cancel()
            }
        })
    }

    // ---------------------------------------------------------------- cycle de vie

    fun start() {
        running = true
        if (job?.isActive == true) {
            wake.trySend(Unit)
            return
        }
        if (store.pairing() == null) {
            link.value = Link.NOT_PAIRED
            return
        }
        job = scope.launch { loop() }
    }

    fun stop() {
        running = false
        job?.cancel()
        job = null
        socket?.close(1000, null)
        socket = null
        if (link.value != Link.NOT_PAIRED) link.value = Link.OFFLINE
    }

    fun forget() {
        stop()
        store.forget()
        screen.value = null
        pcName.value = ""
        info.value = ""
        link.value = Link.NOT_PAIRED
    }

    /** Commande vers la régie. Renvoie false si le PC n'est pas joignable. */
    fun send(type: String, vararg fields: Pair<String, Any?>): Boolean {
        val s = socket ?: return false
        val o = JSONObject().put("t", type).put("rid", (++rid).toString())
        for ((k, v) in fields) o.put(k, v)
        return s.send(o.toString())
    }

    // ---------------------------------------------------------------- reconnexion

    private suspend fun loop() {
        var delay = 500L
        while (running) {
            val p = store.pairing()
            if (p == null) {
                link.value = Link.NOT_PAIRED
                return
            }
            link.value = Link.CONNECTING
            when (tryAll(p, null)) {
                Result.REVOKED -> {
                    store.forget()
                    pcName.value = ""
                    screen.value = null
                    link.value = Link.NOT_PAIRED
                    return
                }
                Result.CLOSED -> delay = 500L
                Result.FAILED -> {}
            }
            if (!running) return
            link.value = Link.OFFLINE
            withTimeoutOrNull(delay) { wake.receive() }
            delay = (delay * 2).coerceAtMost(5000L)
        }
    }

    /** Dernière IP connue, puis découverte UDP (même pcId), puis les IP du code QR. */
    private suspend fun tryAll(p: Pairing, pairing: ByteArray?): Result {
        val tried = mutableSetOf<String>()
        suspend fun attempt(host: String, port: Int): Result? {
            if (!tried.add("$host:$port")) return null
            val r = session(host, port, p, pairing != null)
            return if (r == Result.FAILED) null else r
        }
        p.lastHost?.let { attempt(it, p.lastPort)?.let { r -> return r } }
        for ((host, port) in discover(p.pcId)) attempt(host, port)?.let { return it }
        for (host in sortBySubnet(p.ips)) attempt(host, p.port)?.let { return it }
        return Result.FAILED
    }

    private suspend fun session(host: String, port: Int, p: Pairing, pair: Boolean): Result {
        val inbox = Channel<String>(Channel.UNLIMITED)
        val ws = client.newWebSocket(Request.Builder().url("ws://$host:$port/").build(), object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) { inbox.trySend("{\"t\":\"_open\"}") }
            override fun onMessage(webSocket: WebSocket, text: String) { inbox.trySend(text) }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { inbox.trySend("{\"t\":\"_closed\"}") }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { inbox.trySend("{\"t\":\"_closed\"}") }
        })
        suspend fun next(ms: Long): JSONObject? = withTimeoutOrNull(ms) { JSONObject(inbox.receive()) }
        try {
            if (next(4000)?.optString("t") != "_open") return Result.FAILED
            val cnonce = b64(ByteArray(16).also { random.nextBytes(it) })
            ws.send(
                JSONObject().put("t", "hello").put("v", 1).put("pcId", p.pcId).put("deviceId", store.deviceId)
                    .put("deviceName", deviceName()).put("pair", pair).put("cnonce", cnonce).toString()
            )
            val ch = next(5000) ?: return Result.FAILED
            if (ch.optString("t") == "auth.fail") throw Rejected(ch.optString("code"), ch.optString("message"))
            if (ch.optString("t") != "challenge") return Result.FAILED
            val nonce = ch.getString("nonce")
            // le PC prouve qu'il connaît la clé avant qu'on réponde
            val proof = hmac(p.key, "verselight-srv|$cnonce|$nonce")
            if (!MessageDigest.isEqual(proof.toByteArray(), ch.optString("proof").toByteArray())) return Result.FAILED
            ws.send(JSONObject().put("t", "auth").put("mac", hmac(p.key, nonce + store.deviceId)).toString())
            val w = next(5000) ?: return Result.FAILED
            if (w.optString("t") == "auth.fail") throw Rejected(w.optString("code"), w.optString("message"))
            if (w.optString("t") != "welcome") return Result.FAILED

            // connecté
            val name = w.optString("pcName", p.pcName)
            store.save(p.copy(pcName = name, lastHost = host, lastPort = port))
            pcName.value = name
            info.value = ""
            socket = ws
            link.value = Link.CONNECTED
            send("state.get")
            while (true) {
                val m = JSONObject(inbox.receive())
                when (m.optString("t")) {
                    "_closed" -> return Result.CLOSED
                    "state" -> screen.value = m
                    "auth.fail" -> throw Rejected(m.optString("code"), m.optString("message"))
                    else -> events.tryEmit(m)
                }
            }
        } catch (e: Rejected) {
            info.value = e.message ?: ""
            return when {
                e.code == "revoked" && !pair -> Result.REVOKED
                pair && e.code != "wrong_pc" -> Result.REVOKED // QR expiré ou invalide : on redemande un scan
                else -> Result.FAILED
            }
        } catch (e: Exception) {
            return Result.FAILED
        } finally {
            if (socket === ws) socket = null
            ws.cancel()
        }
    }

    // ---------------------------------------------------------------- jumelage (QR)

    /** verselight://pair?pcId=…&name=…&ips=…&port=…&secret=… */
    fun pair(qr: String): Boolean {
        val u = Uri.parse(qr)
        if (u.scheme != "verselight" || u.host != "pair") return false
        val pcId = u.getQueryParameter("pcId") ?: return false
        val secret = try {
            Base64.decode(u.getQueryParameter("secret") ?: return false, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)
        } catch (e: IllegalArgumentException) {
            return false
        }
        val key = hmacRaw(secret, "verselight-pair|$pcId|${store.deviceId}")
        val p = Pairing(
            pcId = pcId,
            pcName = u.getQueryParameter("name") ?: "PC",
            key = key,
            ips = (u.getQueryParameter("ips") ?: "").split(",").filter { it.isNotBlank() },
            port = u.getQueryParameter("port")?.toIntOrNull() ?: 47900,
            lastHost = null,
            lastPort = 47900,
        )
        job?.cancel()
        running = true
        info.value = ""
        link.value = Link.PAIRING
        job = scope.launch {
            var r = Result.FAILED
            for (i in 0 until 3) {
                r = tryAll(p, key)
                if (r != Result.FAILED || !running) break
            }
            when {
                store.pairing() != null && r != Result.REVOKED -> loop()
                else -> {
                    if (info.value.isEmpty()) {
                        val ip = sortBySubnet(p.ips).firstOrNull()
                        info.value = "PC introuvable. Vérifiez que MyVerse est ouvert sur le PC et que le téléphone est sur le même Wi-Fi." +
                            (if (ip != null) " Test : ouvrez http://$ip:${p.port} dans Chrome sur ce téléphone. Si la page « MyVerse ✓ » ne s'affiche pas, c'est le Wi-Fi ou le pare-feu du PC (MyVerse › Téléphone › Autoriser dans le pare-feu)." else "")
                    }
                    link.value = Link.NOT_PAIRED
                }
            }
        }
        return true
    }

    // ---------------------------------------------------------------- découverte UDP

    private fun discover(pcId: String): List<Pair<String, Int>> {
        val found = linkedSetOf<Pair<String, Int>>()
        try {
            DatagramSocket().use { s ->
                s.broadcast = true
                s.soTimeout = 300
                val data = "VERSELIGHT_DISCOVER $pcId".toByteArray()
                for (a in broadcastAddresses()) {
                    try {
                        s.send(DatagramPacket(data, data.size, a, 47901))
                    } catch (e: Exception) {
                    }
                }
                val buf = ByteArray(1024)
                val end = System.currentTimeMillis() + 1200
                while (System.currentTimeMillis() < end) {
                    try {
                        val pk = DatagramPacket(buf, buf.size)
                        s.receive(pk)
                        val o = JSONObject(String(pk.data, 0, pk.length))
                        if (o.optString("pcId") == pcId) found.add(pk.address.hostAddress!! to o.optInt("port", 47900))
                    } catch (e: SocketTimeoutException) {
                        if (found.isNotEmpty()) break
                    } catch (e: Exception) {
                    }
                }
            }
        } catch (e: Exception) {
        }
        return found.toList()
    }

    private fun broadcastAddresses(): List<InetAddress> {
        val out = mutableListOf<InetAddress>(InetAddress.getByName("255.255.255.255"))
        try {
            for (ni in NetworkInterface.getNetworkInterfaces()) {
                if (!ni.isUp || ni.isLoopback) continue
                for (ia in ni.interfaceAddresses) ia.broadcast?.let { out.add(it) }
            }
        } catch (e: Exception) {
        }
        return out
    }

    /** Les IP du même sous-réseau que le téléphone d'abord. */
    private fun sortBySubnet(ips: List<String>): List<String> {
        val mine = mutableListOf<String>()
        try {
            for (ni in NetworkInterface.getNetworkInterfaces()) {
                for (a in ni.inetAddresses) {
                    val h = a.hostAddress ?: continue
                    if (!a.isLoopbackAddress && h.count { it == '.' } == 3) mine.add(h.substringBeforeLast('.'))
                }
            }
        } catch (e: Exception) {
        }
        return ips.sortedBy { ip -> if (mine.contains(ip.substringBeforeLast('.'))) 0 else 1 }
    }

    // ---------------------------------------------------------------- mise à jour (GitHub Releases)

    fun checkUpdate() {
        if (updateChecked) return
        updateChecked = true
        scope.launch {
            try {
                val current = context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "0"
                val req = Request.Builder()
                    .url("https://api.github.com/repos/capmaster2023/Verselight/releases/latest")
                    .header("User-Agent", "MyVerse-Android")
                    .header("Accept", "application/vnd.github+json")
                    .build()
                val http = client.newBuilder().readTimeout(15, TimeUnit.SECONDS).build()
                http.newCall(req).execute().use { r ->
                    if (!r.isSuccessful) return@launch
                    val o = JSONObject(r.body?.string() ?: return@launch)
                    val latest = o.optString("tag_name").removePrefix("v")
                    val assets = o.optJSONArray("assets") ?: return@launch
                    for (i in 0 until assets.length()) {
                        val a = assets.getJSONObject(i)
                        if (a.optString("name") == "MyVerse.apk" && newer(latest, current)) {
                            update.value = latest to a.optString("browser_download_url")
                        }
                    }
                }
            } catch (e: Exception) {
            }
        }
    }

    private fun versionParts(v: String): List<Int> {
        val p = v.removePrefix("v").split(".").take(3).map { s -> s.takeWhile { it.isDigit() }.toIntOrNull() ?: 0 }
        return p + List(3 - p.size) { 0 }
    }

    private fun newer(a: String, b: String): Boolean {
        val pa = versionParts(a)
        val pb = versionParts(b)
        for (i in 0 until 3) if (pa[i] != pb[i]) return pa[i] > pb[i]
        return false
    }

    // ---------------------------------------------------------------- outils

    private fun deviceName(): String =
        Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)
            ?: "${Build.MANUFACTURER} ${Build.MODEL}"

    private fun b64(b: ByteArray) = Base64.encodeToString(b, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)

    private fun hmacRaw(key: ByteArray, msg: String): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(msg.toByteArray(Charsets.UTF_8))
    }

    private fun hmac(key: ByteArray, msg: String) = b64(hmacRaw(key, msg))
}
