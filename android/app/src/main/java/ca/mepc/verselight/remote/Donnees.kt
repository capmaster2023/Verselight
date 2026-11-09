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
 * Bibles gardées sur le téléphone pour le détecteur de versets : la Louis Segond (intégrée à l'app) et les Bibles
 * reçues du PC (version principale, noms des livres en kirundi, swahili…).
 */
object Donnees {
    private val bibles = HashMap<String, BibleTexte>()

    private fun dossier(ctx: Context) = File(ctx.filesDir, "bibles").apply { mkdirs() }
    private fun fichierBible(ctx: Context, id: String) = File(dossier(ctx), id.replace(Regex("[^A-Za-z0-9_-]"), "_") + ".mvb")

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

    /** Bibles où chercher des mots sans le PC : principale, 2e version, puis les Bibles importées sur le PC. */
    fun pourRecherche(ctx: Context, store: Store): List<BibleTexte> {
        val ids = reglages(store).optJSONArray("importees")
        val autres = if (ids == null) emptyList() else (0 until ids.length()).mapNotNull { bible(ctx, ids.optString(it)) }
        return (listOf(principale(ctx, store)) + listOfNotNull(seconde(ctx, store)) + autres).distinctBy { it.id }
    }

    private fun gunzip(b64: String): InputStreamReader =
        InputStreamReader(GZIPInputStream(ByteArrayInputStream(Base64.decode(b64, Base64.DEFAULT))), Charsets.UTF_8)

    /** Réponse « sync » du PC : réglages des Bibles. Renvoie les Bibles à demander au PC. */
    fun recevoirSync(ctx: Context, store: Store, m: JSONObject): List<String> {
        // Bibles importées sur le PC (kirundi, swahili…) : copiées aussi, pour la recherche sans le PC
        val importees = ArrayList<String>()
        m.optJSONArray("bibles")?.let { a ->
            for (i in 0 until a.length()) a.optJSONObject(i)?.optString("id")?.let { if (it.isNotBlank() && it != "lsg" && it != "kjv") importees.add(it) }
        }
        m.optJSONObject("reglages")?.let { store.reglages = it.put("importees", JSONArray(importees)).toString() }
        // Bibles supprimées sur le PC : supprimées ici aussi
        val gardees = importees.toSet() + listOf(reglages(store).optString("principale"), reglages(store).optString("seconde"))
        dossier(ctx).listFiles()?.forEach { f ->
            val id = f.name.removeSuffix(".mvb")
            if (f.name.endsWith(".mvb") && id !in gardees) { f.delete(); synchronized(this) { bibles.remove(id) } }
        }
        val r = reglages(store)
        return (listOf(r.optString("principale"), r.optString("seconde")) + importees)
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
        File(ctx.filesDir, "chants.json").delete()
        synchronized(this) { bibles.keys.retainAll(setOf("lsg")) }
    }
}
