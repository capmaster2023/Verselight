package ca.mepc.verselight.remote

import android.content.Context
import android.util.Base64
import android.util.JsonReader
import android.util.JsonToken
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.File
import java.io.InputStreamReader
import java.util.zip.GZIPInputStream

/**
 * Ce que le téléphone garde pour travailler sans le PC : la Louis Segond (intégrée à l'app), les Bibles et les
 * chants reçus du PC, et les réglages de l'écran (version principale, 2e version, thème, taille).
 */
object Donnees {
    private val bibles = HashMap<String, BibleTexte>()
    @Volatile private var chantsCache: List<JSONObject>? = null

    private fun dossier(ctx: Context) = File(ctx.filesDir, "bibles").apply { mkdirs() }
    private fun fichierBible(ctx: Context, id: String) = File(dossier(ctx), id.replace(Regex("[^A-Za-z0-9_-]"), "_") + ".mvb")
    private fun fichierChants(ctx: Context) = File(ctx.filesDir, "chants.json")

    fun aBible(ctx: Context, id: String) = id == "lsg" || fichierBible(ctx, id).exists()

    /** Bible du téléphone (null si elle n'a pas encore été reçue du PC). */
    @Synchronized
    fun bible(ctx: Context, id: String): BibleTexte? {
        bibles[id]?.let { return it }
        val b = try {
            if (id == "lsg") ctx.assets.open("lsg.mvb").bufferedReader().use { BibleTexte.lire("lsg", it) }
            else fichierBible(ctx, id).takeIf { it.exists() }?.bufferedReader()?.use { BibleTexte.lire(id, it) }
        } catch (e: Exception) {
            null
        }
        if (b != null) bibles[id] = b
        return b
    }

    fun lsg(ctx: Context): BibleTexte = bible(ctx, "lsg")!!

    // ---------------------------------------------------------------- réglages reçus du PC
    fun reglages(store: Store): JSONObject = try { JSONObject(store.reglages) } catch (e: Exception) { JSONObject() }
    fun principale(ctx: Context, store: Store): BibleTexte = bible(ctx, reglages(store).optString("principale", "lsg").ifBlank { "lsg" }) ?: lsg(ctx)
    fun seconde(ctx: Context, store: Store): BibleTexte? = reglages(store).optString("seconde").takeIf { it.isNotBlank() }?.let { bible(ctx, it) }

    // ---------------------------------------------------------------- chants
    fun chants(ctx: Context): List<JSONObject> {
        chantsCache?.let { return it }
        val l = try {
            val a = JSONArray(fichierChants(ctx).readText())
            (0 until a.length()).mapNotNull { a.optJSONObject(it) }
        } catch (e: Exception) {
            emptyList()
        }
        chantsCache = l
        return l
    }

    private fun gunzip(b64: String): InputStreamReader =
        InputStreamReader(GZIPInputStream(ByteArrayInputStream(Base64.decode(b64, Base64.DEFAULT))), Charsets.UTF_8)

    /** Réponse « sync » du PC : réglages, chants (s'ils ont changé). Renvoie les Bibles à demander au PC. */
    fun recevoirSync(ctx: Context, store: Store, m: JSONObject): List<String> {
        m.optJSONObject("reglages")?.let { store.reglages = it.toString() }
        m.optJSONObject("chants")?.let { c ->
            val gz = c.optString("gz")
            if (gz.isNotEmpty()) {
                val texte = gunzip(gz).use { it.readText() }
                JSONArray(texte) // vérifie avant d'écrire
                fichierChants(ctx).writeText(texte)
                chantsCache = null
                store.chantsStamp = c.optString("stamp")
            }
        }
        val r = reglages(store)
        return listOf(r.optString("principale"), r.optString("seconde"))
            .filter { it.isNotBlank() && it != "lsg" && !aBible(ctx, it) }.distinct()
    }

    /** Réponse « bible » du PC : la Bible (JSON compressé) est gardée au format MVB1. */
    fun recevoirBible(ctx: Context, m: JSONObject): String? {
        val id = m.optString("id").ifBlank { return null }
        val livres = ArrayList<BibleTexte.Livre>()
        var name = m.optString("name", id)
        var short = m.optString("short", id.uppercase())
        fun texte(r: JsonReader): String = if (r.peek() == JsonToken.NULL) { r.nextNull(); "" } else r.nextString()
        JsonReader(gunzip(m.optString("gz"))).use { r ->
            r.beginObject()
            while (r.hasNext()) {
                when (r.nextName()) {
                    "name" -> name = texte(r)
                    "short" -> short = texte(r)
                    "books" -> {
                        r.beginArray()
                        while (r.hasNext()) {
                            if (r.peek() == JsonToken.NULL) { r.nextNull(); livres.add(BibleTexte.Livre("", emptyList())); continue }
                            var nom = ""
                            val chs = ArrayList<List<String>>()
                            r.beginObject()
                            while (r.hasNext()) {
                                when (r.nextName()) {
                                    "name" -> nom = texte(r)
                                    "ch" -> {
                                        r.beginArray()
                                        while (r.hasNext()) {
                                            val vs = ArrayList<String>()
                                            if (r.peek() == JsonToken.NULL) r.nextNull()
                                            else {
                                                r.beginArray()
                                                while (r.hasNext()) vs.add(texte(r))
                                                r.endArray()
                                            }
                                            chs.add(vs)
                                        }
                                        r.endArray()
                                    }
                                    else -> r.skipValue()
                                }
                            }
                            r.endObject()
                            livres.add(BibleTexte.Livre(nom, chs))
                        }
                        r.endArray()
                    }
                    else -> r.skipValue()
                }
            }
            r.endObject()
        }
        val b = BibleTexte(id, name, short, livres)
        val f = fichierBible(ctx, id)
        val tmp = File(f.path + ".tmp")
        tmp.bufferedWriter().use { b.ecrire(it) }
        tmp.renameTo(f)
        synchronized(this) { bibles[id] = b }
        return short
    }

    /** Oublier ce PC : les données reçues de lui disparaissent aussi (la Louis Segond reste). */
    fun effacer(ctx: Context) {
        dossier(ctx).listFiles()?.forEach { it.delete() }
        fichierChants(ctx).delete()
        chantsCache = null
        synchronized(this) { bibles.keys.retainAll(setOf("lsg")) }
    }
}
