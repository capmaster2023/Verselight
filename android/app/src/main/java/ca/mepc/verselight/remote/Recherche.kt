package ca.mepc.verselight.remote

import java.text.Normalizer
import kotlin.math.abs
import kotlin.math.ln

/**
 * Recherche par mots dans les Bibles du téléphone (même méthode que recherche.js sur le PC) :
 * toutes les langues, fautes tolérées (lettres oubliées, inversées, en trop ; mots écrits « à l'oreille »),
 * versets classés par pertinence.
 */
object Recherche {
    class Resultat(val b: Int, val c: Int, val v: Int, val texte: String, val note: Double, val version: String, val principale: Boolean)

    private val MOT = Regex("[\\p{L}\\p{N}]+")
    fun norm(s: String): String =
        Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "")
            .replace("œ", "oe").replace("æ", "ae").replace("ß", "ss")
    fun mots(s: String): List<String> = MOT.findAll(norm(s)).map { it.value }.toList()

    // ------------------------------------------------------------ index (un par Bible)
    private class Index(val refs: IntArray, val termes: HashMap<String, IntArray>, val parLongueur: Array<MutableList<String>?>) {
        var sons: HashMap<String, MutableList<String>>? = null
        val n get() = refs.size
    }
    private val index = java.util.WeakHashMap<BibleTexte, Index>()

    @Synchronized
    private fun indexer(bible: BibleTexte): Index {
        index[bible]?.let { return it }
        val refs = ArrayList<Int>(32000)
        val listes = HashMap<String, ArrayList<Int>>()
        bible.livres.forEachIndexed { b, livre ->
            livre.chapitres.forEachIndexed { c, vs ->
                vs.forEachIndexed { v, t ->
                    if (t.isNotBlank()) {
                        val id = refs.size
                        refs.add(b * 1_000_000 + (c + 1) * 1000 + (v + 1))
                        for (w in mots(t).toSet()) listes.getOrPut(w) { ArrayList() }.add(id)
                    }
                }
            }
        }
        val termes = HashMap<String, IntArray>(listes.size * 2)
        var max = 0
        for ((w, l) in listes) { termes[w] = l.toIntArray(); if (w.length > max) max = w.length }
        val parLongueur = arrayOfNulls<MutableList<String>>(max + 1)
        for (w in termes.keys) (parLongueur[w.length] ?: ArrayList<String>().also { parLongueur[w.length] = it }).add(w)
        return Index(refs.toIntArray(), termes, parLongueur).also { index[bible] = it }
    }

    // ------------------------------------------------------------ comparaison de mots
    /** Distance de Damerau-Levenshtein bornée (au-delà de max : max + 1). */
    fun distance(a: String, b: String, max: Int): Int {
        val la = a.length
        val lb = b.length
        if (abs(la - lb) > max) return max + 1
        var p2 = IntArray(lb + 1)
        var p = IntArray(lb + 1) { it }
        var c = IntArray(lb + 1)
        for (i in 1..la) {
            c[0] = i
            var min = i
            val ai = a[i - 1]
            for (j in 1..lb) {
                val bj = b[j - 1]
                var v = p[j - 1] + if (ai == bj) 0 else 1
                if (p[j] + 1 < v) v = p[j] + 1
                if (c[j - 1] + 1 < v) v = c[j - 1] + 1
                if (i > 1 && j > 1 && ai == b[j - 2] && a[i - 2] == bj && p2[j - 2] + 1 < v) v = p2[j - 2] + 1
                c[j] = v
                if (v < min) min = v
            }
            if (min > max) return max + 1
            val t = p2; p2 = p; p = c; c = t
        }
        return p[lb]
    }

    private val R_SON = listOf(
        Regex("ph") to "f", Regex("ch(?=[rl])") to "k", Regex("ch") to "x", Regex("qu|q") to "k",
        Regex("c(?=[eiy])") to "s", Regex("c") to "k", Regex("g(?=[eiy])") to "j", Regex("je(?=[aou])") to "j",
        Regex("gu(?=[eiy])") to "g", Regex("h") to "", Regex("y") to "i", Regex("w") to "v", Regex("(.)\\1+") to "$1",
        Regex("eau|au") to "o", Regex("oeu|oe|eu") to "e", Regex("ou") to "u",
        Regex("[ae]i[nm](?![aeiou])|[iu][nm](?![aeiou])") to "1", Regex("[ae][nm](?![aeiou])") to "2", Regex("o[nm](?![aeiou])") to "3",
        Regex("ai|ei") to "e", Regex("pt") to "t",
    )
    private val R_FIN_VERBE = Regex("(?<=...)(er|ez)$")
    private val R_FIN1 = Regex("(?<=..)(s|x|t|d)$")
    private val R_FIN2 = Regex("(?<=..)e$")

    /** Mot écrit comme il se prononce (keur → coeur, sein → saint, batème → baptême). */
    fun son(w: String): String {
        var s = R_FIN_VERBE.replaceFirst(w, "e")
        for ((r, x) in R_SON) s = r.replace(s, x)
        s = R_FIN1.replaceFirst(s, "")
        s = R_FIN2.replaceFirst(s, "")
        return R_FIN1.replaceFirst(s, "")
    }
    private fun sonUtile(s: String) = s.length >= 3 || (s.length == 2 && s.any { it.isDigit() })
    private fun tolerance(n: Int) = if (n <= 3) 0 else if (n <= 6) 1 else 2

    /** Mots de la Bible qui correspondent à un mot tapé : terme → qualité (1 = exact). */
    private fun candidats(ix: Index, q: String): HashMap<String, Double> {
        val out = HashMap<String, Double>()
        fun ajouter(t: String, s: Double) { if ((out[t] ?: 0.0) < s) out[t] = s }
        val L = q.length
        if (ix.termes.containsKey(q)) ajouter(q, 1.0)
        val k = tolerance(L)
        for (n in maxOf(1, L - k) until ix.parLongueur.size) {
            val liste = ix.parLongueur[n] ?: continue
            val prefixe = L >= 3 && n > L
            val flou = k > 0 && n <= L + k
            if (!prefixe && !flou) continue
            for (t in liste) {
                if (prefixe && t.startsWith(q)) { ajouter(t, if (n - L <= 3) 0.9 else 0.7); continue }
                if (flou && t != q) {
                    val e = distance(q, t, k)
                    // une faute sur la première lettre est plus rare : un peu moins de points
                    if (e <= k) ajouter(t, (if (e == 1) 0.75 else 0.55) * (if (t[0] == q[0]) 1.0 else 0.85))
                }
            }
        }
        if (L >= 4 && q.any { it in 'a'..'z' }) {
            val sons = ix.sons ?: HashMap<String, MutableList<String>>().also { m ->
                for (t in ix.termes.keys) if (t.length >= 3) { val s = son(t); if (sonUtile(s)) m.getOrPut(s) { ArrayList() }.add(t) }
                ix.sons = m
            }
            val s = son(q)
            if (sonUtile(s)) sons[s]?.forEach { ajouter(it, 0.85) }
        }
        return out
    }

    private class Trouve(val b: Int, val c: Int, val v: Int, val texte: String, val note: Double)

    private fun chercherDans(bible: BibleTexte, requete: String): List<Trouve> {
        val ix = indexer(bible)
        val qs = mots(requete).distinct().filter { it.length > 1 || it.any { c -> c.isDigit() } }
        if (qs.isEmpty() || ix.n == 0) return emptyList()
        val n = ix.n
        val score = DoubleArray(n)
        class Groupe(val w: Double, val termes: Set<String>)
        val groupes = ArrayList<Groupe>()
        var total = 0.0
        for (q in qs) {
            val cands = candidats(ix, q)
            val meilleur = DoubleArray(n)
            var df = 0
            for ((t, s) in cands) for (id in ix.termes[t]!!) { if (meilleur[id] == 0.0) df++; if (s > meilleur[id]) meilleur[id] = s }
            // les mots rares (« patiente ») comptent plus que les mots courants (« et », « le »)
            val w = ln(1.0 + n.toDouble() / (df + 1))
            total += w
            if (df > 0) for (id in 0 until n) if (meilleur[id] > 0) score[id] += w * meilleur[id]
            groupes.add(Groupe(w, cands.keys))
        }
        var best = 0.0
        val ids = ArrayList<Int>()
        for (id in 0 until n) if (score[id] > 0) { ids.add(id); if (score[id] > best) best = score[id] }
        if (best == 0.0) return emptyList()
        ids.sortWith { a, b -> if (score[b] != score[a]) score[b].compareTo(score[a]) else a.compareTo(b) }
        return ids.take(600).filter { score[it] >= best * 0.6 && score[it] >= total * 0.34 }.map { id ->
            val code = ix.refs[id]
            val b = code / 1_000_000
            val c = (code / 1000) % 1000
            val v = code % 1000
            val texte = bible.versets(b, c).getOrElse(v - 1) { "" }
            var bonus = 0.0
            if (groupes.size > 1) {
                // mots tapés dans le même ordre, l'un après l'autre (« tant aimé le monde ») : bonus
                val ws = mots(texte)
                for (g in 0 until groupes.size - 1) {
                    val A = groupes[g].termes
                    val B = groupes[g + 1].termes
                    for (p in 0 until ws.size - 1) if (ws[p] in A && ws[p + 1] in B) { bonus += 0.3 * (groupes[g].w + groupes[g + 1].w) / 2; break }
                }
            }
            Trouve(b, c, v, texte, (score[id] + bonus) / total)
        }
    }

    /** Cherche des mots (même mal écrits) dans plusieurs Bibles ; la première est la version principale. */
    fun chercher(bibles: List<BibleTexte>, requete: String, limite: Int = 60): List<Resultat> {
        val parRef = LinkedHashMap<Int, Resultat>()
        val rang = HashMap<Int, Int>()
        bibles.forEachIndexed { i, bible ->
            for (x in chercherDans(bible, requete)) {
                val cle = x.b * 1_000_000 + x.c * 1000 + x.v
                val p = parRef[cle]
                if (p == null || x.note > p.note + 0.05) {
                    parRef[cle] = Resultat(x.b, x.c, x.v, x.texte, x.note, bible.short, i == 0)
                    rang[cle] = i
                }
            }
        }
        val best = parRef.values.maxOfOrNull { it.note } ?: return emptyList()
        return parRef.entries.filter { it.value.note >= best * 0.6 }
            .sortedWith(compareByDescending<Map.Entry<Int, Resultat>> { it.value.note }.thenBy { rang[it.key] ?: 0 }.thenBy { it.key })
            .take(limite).map { it.value }
    }

    /** Prépare l'index à l'avance (la première recherche est alors instantanée). */
    fun preparer(bible: BibleTexte) { indexer(bible) }
}
