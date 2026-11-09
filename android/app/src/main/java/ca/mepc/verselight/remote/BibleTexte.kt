package ca.mepc.verselight.remote

import java.io.BufferedReader
import java.io.Writer

/**
 * Une Bible gardée sur le téléphone (Louis Segond intégrée, ou version reçue du PC).
 * Format de fichier « MVB1 » : une ligne d'en-tête, puis « #B<tab>nom du livre », « #C » (nouveau chapitre)
 * et une ligne « |texte » par verset. Simple, rapide à lire, sans bibliothèque.
 */
class BibleTexte(val id: String, val name: String, val short: String, val livres: List<Livre>) {
    class Livre(val name: String, val chapitres: List<List<String>>)

    fun chapitres(b: Int): Int = livres.getOrNull(b)?.chapitres?.size ?: 0
    fun versets(b: Int, c: Int): List<String> = livres.getOrNull(b)?.chapitres?.getOrNull(c - 1) ?: emptyList()
    fun texte(b: Int, c: Int, v1: Int, v2: Int): String {
        val vs = versets(b, c)
        return (v1..v2).mapNotNull { vs.getOrNull(it - 1)?.takeIf { t -> t.isNotBlank() } }.joinToString(" ")
    }

    /** Référence avec les limites de cette Bible : chapitre entier → tous ses versets. Null si elle n'existe pas. */
    fun borner(r: Ref): Ref? {
        val n = versets(r.b, r.c).size
        if (n == 0) return null
        if (r.v1 <= 0) return Ref(r.b, r.c, 1, n)
        if (r.v1 > n) return null
        return Ref(r.b, r.c, r.v1, r.v2.coerceIn(r.v1, n))
    }

    fun ecrire(w: Writer) {
        w.write("MVB1\t${name.replace('\t', ' ')}\t${short.replace('\t', ' ')}\n")
        for (l in livres) {
            w.write("#B\t${l.name.replace('\t', ' ').replace('\n', ' ')}\n")
            for (ch in l.chapitres) {
                w.write("#C\n")
                for (v in ch) { w.write("|"); w.write(v.replace('\n', ' ').replace('\r', ' ')); w.write("\n") }
            }
        }
    }

    companion object {
        fun lire(id: String, r: BufferedReader): BibleTexte? {
            val tete = r.readLine() ?: return null
            val t = tete.split('\t')
            if (t.firstOrNull() != "MVB1") return null
            val livres = ArrayList<Livre>(66)
            var nom: String? = null
            var chs = ArrayList<List<String>>()
            var vs: ArrayList<String>? = null
            fun finChapitre() { vs?.let { chs.add(it) }; vs = null }
            fun finLivre() { finChapitre(); nom?.let { livres.add(Livre(it, chs)) }; nom = null; chs = ArrayList() }
            while (true) {
                val l = r.readLine() ?: break
                when {
                    l.startsWith("|") -> (vs ?: ArrayList<String>().also { vs = it }).add(l.substring(1))
                    l == "#C" -> { finChapitre(); vs = ArrayList() }
                    l.startsWith("#B") -> { finLivre(); nom = l.substringAfter('\t', "") }
                }
            }
            finLivre()
            return BibleTexte(id, t.getOrElse(1) { id }, t.getOrElse(2) { id.uppercase() }, livres)
        }
    }
}
