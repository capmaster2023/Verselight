package ca.mepc.verselight.remote

import android.content.Context
import android.util.Base64
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

/**
 * Mode « OBS direct » : MyVerse est fermé sur le PC (ou injoignable), le téléphone pilote OBS lui-même avec ce
 * qu'il a reçu du PC lors des connexions précédentes (réglages OBS, chants, Bibles, thème de l'écran).
 * Il répond aux mêmes commandes que la régie du PC : l'interface de l'app ne change pas.
 */
class Direct(private val ctx: Context, private val remote: Remote) {
    enum class Etat { ARRET, CONNEXION, PRET, ERREUR }

    val etat = MutableStateFlow(Etat.ARRET)
    val message = MutableStateFlow("")

    private val obs = Obs { remote.log(it) }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val file = Channel<Pair<String, Map<String, Any?>>>(Channel.UNLIMITED)
    private var connexion: Job? = null
    @Volatile private var pcAbsent = false
    @Volatile private var refus: String? = null // réglages refusés par OBS (mauvais mot de passe) : on n'insiste pas

    private sealed class Cible {
        data class Verset(val b: Int, val c: Int, val v1: Int, val v2: Int) : Cible()
        data class Chant(val id: String, val i: Int) : Cible()
    }
    private var cible: Cible? = null
    private var ecran = JSONObject().put("mode", "vide").put("lignes", JSONArray())

    init {
        obs.surPerte = {
            etat.value = Etat.ERREUR
            message.value = "connexion à OBS perdue"
            if (pcAbsent) { connexion?.cancel(); connexion = scope.launch { delay(2000); boucle() } }
        }
        scope.launch {
            for ((t, f) in file) {
                try {
                    traiter(t, f)
                } catch (e: Exception) {
                    remote.log("OBS direct : « $t » a échoué : ${e.javaClass.simpleName} ${e.message ?: ""}")
                    erreur("Erreur : ${e.message ?: e.javaClass.simpleName}")
                }
            }
        }
    }

    fun pret() = etat.value == Etat.PRET && obs.pret

    /** Le PC ne répond pas : le téléphone se connecte lui-même à OBS (si MyVerse lui a donné les réglages). */
    fun pcAbsent() {
        pcAbsent = true
        if (connexion?.isActive == true || obs.pret) return
        connexion = scope.launch { boucle() }
    }

    /** Le PC est là (ou l'app passe en arrière-plan) : c'est MyVerse sur le PC qui pilote OBS. */
    fun pcPresent() {
        pcAbsent = false
        connexion?.cancel()
        connexion = null
        obs.fermer()
        etat.value = Etat.ARRET
        message.value = ""
    }

    /** Nouveaux réglages OBS reçus du PC : on pourra réessayer. */
    fun reglagesChanges() { refus = null }

    fun commande(type: String, f: Map<String, Any?>) { file.trySend(type to f) }

    private suspend fun boucle() {
        while (pcAbsent && !obs.pret) {
            val store = remote.store
            val c = store.obs()
            val p = store.pairing()
            if (!store.obsDirect) { etat.value = Etat.ARRET; message.value = "désactivé dans les réglages"; return }
            if (c == null || !c.actif || p == null) {
                etat.value = Etat.ARRET
                message.value = "pas encore configuré : activez OBS dans MyVerse sur le PC (bouton OBS), puis connectez le téléphone au PC une fois"
                return
            }
            val host = p.lastHost ?: p.ips.firstOrNull() ?: return
            val cle = "$host:${c.port}:${c.mdp.hashCode()}"
            if (refus == cle) return
            etat.value = Etat.CONNEXION
            message.value = "connexion à OBS ($host:${c.port})…"
            val err = obs.connecter(host, c.port, c.mdp)
            if (err == null) {
                etat.value = Etat.PRET
                message.value = "OBS ($host:${c.port})"
                remote.log("OBS direct : connecté à $host:${c.port} (MyVerse fermé sur le PC)")
                remote.screen.value = etatTel()
                return
            }
            etat.value = Etat.ERREUR
            message.value = err
            if (err.contains("mot de passe")) { refus = cle; remote.log("OBS direct : $err"); return }
            delay(5000)
        }
    }

    // ---------------------------------------------------------------- commandes (mêmes que la régie du PC)

    private fun envoyer(m: JSONObject) { remote.events.tryEmit(m) }
    private fun erreur(msg: String) = envoyer(JSONObject().put("t", "error").put("message", msg))
    private fun principale() = Donnees.principale(ctx, remote.store)
    private fun seconde() = Donnees.seconde(ctx, remote.store)
    private fun etiquette() = principale().short + (seconde()?.let { " · " + it.short } ?: "")
    private fun label(r: Ref, entier: Boolean = false) =
        Books.name(r.b) + " " + r.c + if (entier) "" else ":" + r.v1 + if (r.v2 > r.v1) "-" + r.v2 else ""
    private fun lireRef(txt: String): Pair<Ref, Boolean>? {
        val p = Books.parseRef(txt) ?: return null
        val r = principale().borner(p) ?: return null
        return r to (p.v1 == 0)
    }

    private suspend fun traiter(t: String, f: Map<String, Any?>) {
        fun s(k: String) = f[k]?.toString().orEmpty()
        when (t) {
            "bible.preview" -> {
                val (r, entier) = lireRef(s("ref")) ?: return erreur("Référence introuvable : « ${s("ref")} »")
                val bi = principale()
                val vs = JSONArray()
                for (v in r.v1..r.v2) vs.put(JSONObject().put("v", v).put("t", bi.versets(r.b, r.c).getOrElse(v - 1) { "" }))
                envoyer(JSONObject().put("t", "preview").put("ref", label(r, entier) + " (${etiquette()})")
                    .put("lignes", JSONArray().put(bi.texte(r.b, r.c, r.v1, r.v2))).put("versets", vs).put("livre", Books.name(r.b)).put("c", r.c))
            }
            "bible.search" -> {
                val q = s("q").trim()
                val bibles = listOfNotNull(principale(), seconde()).distinct()
                val res = if (q.length >= 3) Recherche.chercher(bibles, q, 60) else emptyList()
                val l = JSONArray()
                res.forEach { x -> l.put(JSONObject().put("ref", "${Books.name(x.b)} ${x.c}:${x.v}").put("text", x.texte).put("v", if (x.principale) "" else x.version)) }
                envoyer(JSONObject().put("t", "search").put("q", q).put("liste", l))
            }
            "bible.show" -> {
                val (r, entier) = lireRef(s("ref")) ?: return erreur("Référence introuvable : « ${s("ref")} »")
                afficherVerset(Ref(r.b, r.c, r.v1, if (entier) r.v1 else r.v2))
            }
            "song.search" -> envoyer(JSONObject().put("t", "songs").put("liste", chercherChants(s("q"))))
            "song.open" -> {
                val c = chant(s("id")) ?: return erreur("Chant introuvable.")
                envoyer(JSONObject(c.toString()).put("t", "song").put("title", c.optString("title").replace(Regex("\\s+"), " ")))
            }
            "song.show" -> afficherSection(s("id"), s("i").toDoubleOrNull()?.toInt() ?: 0)
            "next" -> bouger(1)
            "prev" -> bouger(-1)
            "clear" -> {
                ecran = base().put("mode", "vide").put("lignes", JSONArray()).put("ref", "")
                obs.cacher()
                publier()
            }
            "state.get" -> publier()
        }
    }

    private fun base(): JSONObject {
        val r = Donnees.reglages(remote.store)
        return JSONObject().put("theme", r.optString("theme", "nuit").ifBlank { "nuit" }).put("taille", r.optDouble("taille", 1.0))
            .put("presentation", r.optString("presentation", "plein").ifBlank { "plein" })
    }

    private suspend fun afficherVerset(r: Ref) {
        val bi = principale()
        val t2 = seconde()?.texte(r.b, r.c, r.v1, r.v2).orEmpty()
        cible = Cible.Verset(r.b, r.c, r.v1, r.v2)
        ecran = base().put("mode", "verset").put("lignes", JSONArray().put(bi.texte(r.b, r.c, r.v1, r.v2)))
            .put("ref", label(r) + " (${etiquette()})").put("second", if (t2.isNotEmpty()) JSONObject().put("texte", t2) else JSONObject.NULL)
        montrer()
    }

    private suspend fun afficherSection(id: String, i: Int) {
        val c = chant(id) ?: return erreur("Chant introuvable.")
        val sec = c.optJSONArray("sections")?.optJSONObject(i) ?: return
        cible = Cible.Chant(id, i)
        val lignes = JSONArray()
        sec.optString("text").split('\n').filter { it.isNotBlank() }.forEach { lignes.put(it) }
        ecran = base().put("mode", "chant").put("lignes", lignes).put("ref", "").put("second", JSONObject.NULL)
        montrer()
    }

    private suspend fun bouger(d: Int) {
        when (val c = cible) {
            is Cible.Chant -> {
                val n = chant(c.id)?.optJSONArray("sections")?.length() ?: 0
                if (c.i + d in 0 until n) afficherSection(c.id, c.i + d)
            }
            is Cible.Verset -> {
                val bi = principale()
                var ch = c.c
                var v = if (d > 0) c.v2 + 1 else c.v1 - 1
                if (v > bi.versets(c.b, ch).size && ch < bi.chapitres(c.b)) { ch++; v = 1 }
                if (v < 1 && ch > 1) { ch--; v = bi.versets(c.b, ch).size }
                if (v >= 1 && v <= bi.versets(c.b, ch).size) afficherVerset(Ref(c.b, ch, v, v))
            }
            null -> {}
        }
    }

    private suspend fun montrer() {
        if (!obs.pret) { erreur("OBS n'est pas joignable : ${message.value}"); publier(); return }
        if (!obs.montrer(ecran) { page(it) }) erreur("OBS n'a pas affiché la diapositive (voir le journal).")
        publier()
    }

    // page de la source navigateur : écran de MyVerse, avec l'état à afficher, dans une adresse « data: »
    private val modele: String by lazy { ctx.assets.open("ecran-obs.html").bufferedReader().use { it.readText() } }
    private fun page(e: JSONObject): String =
        "data:text/html;charset=utf-8;base64," + Base64.encodeToString(modele.replace("/*ETAT*/null", e.toString()).toByteArray(Charsets.UTF_8), Base64.NO_WRAP)

    // ---------------------------------------------------------------- chants reçus du PC
    private fun chant(id: String) = Donnees.chants(ctx).firstOrNull { it.optString("id") == id }

    private fun chercherChants(q0: String): JSONArray {
        val tous = Donnees.chants(ctx)
        val q = q0.trim()
        val res: List<JSONObject> = if (q.isNotEmpty() && q.all { it.isDigit() }) {
            val n = q.toInt()
            tous.filter { Regex("^\\s*(\\d+)").find(it.optString("title"))?.groupValues?.get(1)?.toIntOrNull() == n }
        } else {
            val nq = Books.norm(q)
            val titres = if (nq.isEmpty()) tous else tous.filter { Books.norm(it.optString("title")).contains(nq) }
            if (nq.isNotEmpty() && titres.size < 50) {
                val ids = titres.map { it.optString("id") }.toSet()
                titres + tous.filter { c ->
                    c.optString("id") !in ids && c.optJSONArray("sections")?.let { a -> (0 until a.length()).any { Books.norm(a.optJSONObject(it)?.optString("text").orEmpty()).contains(nq) } } == true
                }
            } else titres
        }
        val out = JSONArray()
        res.take(60).forEach { c -> out.put(JSONObject().put("id", c.optString("id")).put("title", c.optString("title").replace(Regex("\\s+"), " ")).put("author", c.optString("author"))) }
        return out
    }

    // ---------------------------------------------------------------- état pour la barre du bas et « Suivant »
    private fun suivante(): Pair<String, String>? {
        return when (val c = cible) {
            is Cible.Chant -> chant(c.id)?.optJSONArray("sections")?.optJSONObject(c.i + 1)?.let { s ->
                s.optString("label").ifBlank { "Strophe" } to s.optString("text")
            }
            is Cible.Verset -> {
                val bi = principale()
                var ch = c.c
                var v = c.v2 + 1
                if (v > bi.versets(c.b, ch).size && ch < bi.chapitres(c.b)) { ch++; v = 1 }
                bi.versets(c.b, ch).getOrNull(v - 1)?.let { "${Books.name(c.b)} $ch:$v" to it }
            }
            null -> null
        }
    }

    private fun etatTel(): JSONObject {
        val o = JSONObject().put("mode", ecran.optString("mode", "vide")).put("lignes", ecran.optJSONArray("lignes") ?: JSONArray())
            .put("ref", ecran.optString("ref")).put("ecranOuvert", true).put("direct", true)
        val c = cible
        if (c is Cible.Chant && ecran.optString("mode") == "chant") {
            o.put("titre", chant(c.id)?.optString("title").orEmpty()).put("chant", JSONObject().put("id", c.id).put("i", c.i))
        }
        if (ecran.optString("mode") != "vide") suivante()?.let { (l, t) ->
            o.put("suivantLabel", l).put("suivantTexte", t).put("suivant", l + " · " + t.lineSequence().first())
        }
        return o
    }

    private fun publier() { remote.screen.value = etatTel() }
}
