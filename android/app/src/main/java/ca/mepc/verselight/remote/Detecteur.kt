package ca.mepc.verselight.remote

import java.text.Normalizer

/**
 * Détecteur de versets : trouve les références bibliques dans une phrase entendue (reconnaissance vocale)
 * — « ouvrez vos Bibles dans Jean chapitre trois verset seize », « Romains 8 28 », « première aux Corinthiens 13 »,
 * « Psaume 23 », « au verset 17 » (même livre et chapitre que la référence précédente).
 */
object Detecteur {
    /** Une référence entendue. avecVerset = false : chapitre seul (« Psaume 23 »). fin : position du mot qui suit. */
    data class Trouvaille(val ref: Ref, val avecVerset: Boolean, val fin: Int = 0)

    private val UNITES = mapOf(
        "zero" to 0, "un" to 1, "une" to 1, "deux" to 2, "trois" to 3, "quatre" to 4, "cinq" to 5, "six" to 6, "sept" to 7,
        "huit" to 8, "neuf" to 9, "dix" to 10, "onze" to 11, "douze" to 12, "treize" to 13, "quatorze" to 14, "quinze" to 15, "seize" to 16,
    )
    private val DIZAINES = mapOf(
        "vingt" to 20, "vingts" to 20, "trente" to 30, "quarante" to 40, "cinquante" to 50, "soixante" to 60,
        "septante" to 70, "octante" to 80, "huitante" to 80, "nonante" to 90,
    )
    private val PREMIER = setOf("premier", "premiere", "1er", "1re", "1ere")

    /** Nombre écrit en chiffres ou en lettres à partir de t[i0] : (valeur, position suivante). */
    fun nombre(t: List<String>, i0: Int): Pair<Int, Int>? {
        if (i0 >= t.size) return null
        t[i0].toIntOrNull()?.let { return if (it in 1..999) it to i0 + 1 else null }
        if (t[i0] in PREMIER) return 1 to i0 + 1
        var i = i0
        var total = 0
        var etat = 0 // 0 début, 1 unité (1-9), 2 dix à seize, 3 dizaine, 4 cent, 5 fini
        var dernier = 0
        while (i < t.size && etat != 5) {
            var j = i
            // « vingt et un », « soixante et onze »
            if (t[j] == "et" && etat == 3 && j + 1 < t.size && t[j + 1] in setOf("un", "une", "onze")) j++
            val w = t[j]
            val u = UNITES[w]
            val d = DIZAINES[w]
            val cent = w == "cent" || w == "cents"
            val permis = when {
                cent -> etat == 0 || etat == 1
                d != null -> etat == 0 || etat == 4 || (etat == 1 && dernier == 4 && w.startsWith("vingt"))
                u != null && u <= 9 -> etat == 0 || etat == 3 || etat == 4 || (etat == 2 && dernier == 10 && u >= 7)
                u != null -> etat == 0 || etat == 4 || (etat == 3 && (dernier == 60 || dernier == 80))
                else -> false
            }
            if (!permis) break
            when {
                cent -> { total = (if (total == 0) 1 else total) * 100; etat = 4; dernier = 100 }
                d != null -> { if (etat == 1) { total += 76; dernier = 80 } else { total += d; dernier = d }; etat = 3 }
                u != null && u <= 9 -> { total += u; dernier = u; etat = if (etat == 0 || etat == 4) 1 else 5 }
                u != null -> { total += u; dernier = u; etat = 2 }
            }
            i = j + 1
        }
        return if (i > i0 && total in 1..999) total to i else null
    }

    // ------------------------------------------------------------ noms des livres tels qu'on les dit
    private class Nom(val mots: List<String>, val livre: Int, val numerote: Boolean)

    private val NOMS: List<Nom> by lazy {
        val dits = listOf(
            0 to "genese", 1 to "exode", 2 to "levitique", 3 to "nombres", 4 to "deuteronome", 5 to "josue", 6 to "juges", 7 to "ruth",
            14 to "esdras", 15 to "nehemie", 16 to "esther", 17 to "job", 18 to "psaumes", 18 to "psaume", 19 to "proverbes", 19 to "proverbe",
            20 to "ecclesiaste", 20 to "qohelet", 21 to "cantique des cantiques", 21 to "cantique", 22 to "esaie", 22 to "isaie",
            23 to "jeremie", 24 to "lamentations", 25 to "ezechiel", 26 to "daniel", 27 to "osee", 28 to "joel", 29 to "amos", 30 to "abdias",
            31 to "jonas", 32 to "michee", 33 to "nahum", 33 to "nahoum", 34 to "habacuc", 34 to "habakuk", 35 to "sophonie", 36 to "aggee",
            37 to "zacharie", 38 to "malachie", 39 to "matthieu", 39 to "mathieu", 40 to "marc", 41 to "luc",
            43 to "actes des apotres", 43 to "actes", 44 to "romains", 47 to "galates", 48 to "ephesiens", 49 to "philippiens",
            50 to "colossiens", 55 to "tite", 56 to "philemon", 57 to "hebreux", 58 to "jacques", 64 to "jude", 65 to "apocalypse",
        ).map { (b, n) -> Nom(n.split(" "), b, false) }
        // livres numérotés : le premier du groupe (1 Samuel…), le numéro dit avant (« premier », « deuxième aux »…)
        val numerotes = listOf(8 to "samuel", 10 to "rois", 12 to "chroniques", 45 to "corinthiens", 51 to "thessaloniciens", 53 to "timothee", 59 to "pierre", 61 to "jean")
            .map { (b, n) -> Nom(listOf(n), b, true) }
        (dits + numerotes).sortedByDescending { it.mots.size }
    }
    private val MAX_NUMERO = mapOf(8 to 2, 10 to 2, 12 to 2, 45 to 2, 51 to 2, 53 to 2, 59 to 2, 61 to 3)
    private val ORDINAUX = mapOf(
        "1" to 1, "premier" to 1, "premiere" to 1, "1er" to 1, "1re" to 1, "1ere" to 1, "i" to 1,
        "2" to 2, "deuxieme" to 2, "second" to 2, "seconde" to 2, "2e" to 2, "2eme" to 2, "ii" to 2,
        "3" to 3, "troisieme" to 3, "3e" to 3, "3eme" to 3, "iii" to 3,
    )
    private val LIENS_LIVRE = setOf("epitre", "epitres", "lettre", "livre", "de", "des", "du", "aux", "au", "a", "l", "selon", "la")
    /** Prénoms courants : sans le mot « chapitre » ni de verset, « Jean 3 » n'est pas une référence (« Jean 3 fois »). */
    private val PRENOMS = setOf(7, 8, 9, 16, 17, 26, 29, 31, 37, 39, 40, 41, 42, 53, 54, 55, 58, 59, 60, 64)
    private val AVANT_CHAPITRE = setOf("au", "le", "du", "a", "en", "chapitre", "chapitres", "chap", "ch")
    private val AVANT_VERSET = setOf("et", "au", "aux", "le", "les", "du", "a", "verset", "versets", "v", "vs")
    private val JUSQUE = setOf("a", "au", "jusqu", "jusque", "jusquau", "tiret", "et")

    fun mots(texte: String): List<String> {
        val s = Normalizer.normalize(texte.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "")
            .replace(Regex("(\\d)\\s*[:.,]\\s*(?=\\d)"), "$1 ")
            .replace(Regex("[^a-z0-9]+"), " ")
        return s.trim().split(' ').filter { it.isNotEmpty() }
    }

    // noms entendus de travers par la reconnaissance vocale (« jeunesse » pour Genèse, « gens » pour Jean,
    // « romain » pour Romains) : comparés à l'oreille (même prononciation) ou à une lettre près
    private val UNMOT: List<Nom> by lazy { NOMS.filter { it.mots.size == 1 && it.mots[0].length >= 4 } }
    private val SONS: List<Pair<String, Nom>> by lazy { UNMOT.map { Recherche.son(it.mots[0]) to it }.filter { it.first.length >= 2 } }

    /** Livre trouvé à la position i : sur = nom français exact ; sinon (entendu de travers, autre langue) il faut le verset. */
    private class Livre(val livre: Int, val longueur: Int, val numerote: Boolean, val sur: Boolean)

    private fun livreEn(t: List<String>, i: Int, extra: List<Pair<List<String>, Int>>): Livre? {
        NOMS.firstOrNull { n -> i + n.mots.size <= t.size && n.mots.indices.all { t[i + it] == n.mots[it] } }
            ?.let { return Livre(it.livre, it.mots.size, it.numerote, true) }
        extra.firstOrNull { (m, _) -> i + m.size <= t.size && m.indices.all { t[i + it] == m[it] } }
            ?.let { return Livre(it.second, it.first.size, false, false) }
        val w = t[i]
        if (w.length < 4 || w.any { it.isDigit() }) return null
        val s = Recherche.son(w)
        SONS.firstOrNull { it.first == s }?.let { return Livre(it.second.livre, 1, it.second.numerote, false) }
        val k = if (w.length >= 7) 2 else 1
        var meilleur: Nom? = null
        var d0 = k + 1
        for (n in UNMOT) {
            val d = Recherche.distance(w, n.mots[0], k)
            if (d < d0) { d0 = d; meilleur = n }
        }
        return meilleur?.let { Livre(it.livre, 1, it.numerote, false) }
    }

    /** Noms des livres tels qu'écrits dans les Bibles du téléphone (kirundi, swahili…), en mots comme mots(). */
    fun nomsDepuis(bibles: List<BibleTexte>): List<Pair<List<String>, Int>> {
        val out = ArrayList<Pair<List<String>, Int>>()
        val vus = HashSet<String>()
        for (bi in bibles) bi.livres.forEachIndexed { i, l ->
            val m = mots(l.name)
            if (m.any { w -> w.length >= 3 && w.any { it in 'a'..'z' } } && vus.add(m.joinToString(" "))) out.add(m to i)
        }
        return out.sortedByDescending { it.first.size }
    }

    /**
     * Références trouvées dans une phrase. bible sert à vérifier qu'elles existent (chapitre et verset).
     * contexte : livre et chapitre de la dernière référence entendue (pour « au verset 17 », « chapitre 4 »).
     * extra : noms des livres dans d'autres langues (Bibles importées), voir nomsDepuis.
     */
    fun trouver(texte: String, bible: BibleTexte?, contexte: Ref? = null, extra: List<Pair<List<String>, Int>> = emptyList()): List<Trouvaille> {
        val t = mots(texte)
        val out = ArrayList<Trouvaille>()
        var dernierLivre = -100 // position du dernier livre reconnu
        var ctx = contexte

        // verset à partir de k0 : « 16 », « verset 16 », « et au verset 16 », « 16 à 18 » → (v1, v2, position suivante)
        fun verset(k0: Int): Triple<Int, Int, Int>? {
            var v: Pair<Int, Int>? = nombre(t, k0)
            if (v == null) {
                var m = k0
                var n = 0
                var mot = false
                while (m < t.size && t[m] in AVANT_VERSET && n < 4) { if (t[m].startsWith("v")) mot = true; m++; n++ }
                if (mot) v = nombre(t, m)
            }
            val premier = v ?: return null
            var fin = premier.first
            var k = premier.second
            if (k < t.size && t[k] in JUSQUE) nombre(t, k + 1)?.let { if (it.first > premier.first) { fin = it.first; k = it.second } }
            return Triple(premier.first, fin, k)
        }
        fun ajouter(r: Ref, avecVerset: Boolean, fin: Int): Boolean {
            val borne = bible?.borner(r)
            val existe = bible == null || (borne != null && (r.v1 == 0 || borne.v1 == r.v1))
            if (existe) {
                out.add(Trouvaille(if (r.v1 == 0) r else (borne ?: r), avecVerset, fin))
                ctx = r
            }
            return existe
        }

        var i = 0
        while (i < t.size) {
            val trouve = livreEn(t, i, extra)
            if (trouve == null) {
                val c0 = ctx
                if (c0 != null && i - dernierLivre > 8) {
                    // « au verset 17 » : même livre, même chapitre que la référence précédente
                    if (t[i] == "verset" || t[i] == "versets") {
                        val v = verset(i)
                        if (v != null && ajouter(Ref(c0.b, c0.c, v.first, v.second), true, v.third)) { i = v.third; continue }
                    }
                    // « au chapitre 4 (verset 2) » : même livre
                    if (t[i] == "chapitre" || t[i] == "chapitres") {
                        val c = nombre(t, i + 1)
                        if (c != null) {
                            val v = verset(c.second)
                            val fin = v?.third ?: c.second
                            if (ajouter(Ref(c0.b, c.first, v?.first ?: 0, v?.second ?: 0), v != null, fin)) { i = fin; continue }
                        }
                    }
                }
                i++
                continue
            }
            var livre = trouve.livre
            if (trouve.numerote) {
                // numéro dit avant le nom : « premier Jean », « deuxième épître aux Corinthiens », « 1 Samuel »
                var k = i - 1
                var numero = 0
                var sauts = 0
                while (k >= 0 && sauts <= 3) {
                    val o = ORDINAUX[t[k]]
                    if (o != null) { numero = o; break }
                    if (t[k] !in LIENS_LIVRE) break
                    k--; sauts++
                }
                val max = MAX_NUMERO[trouve.livre] ?: 1
                livre = when {
                    trouve.livre == 61 && numero == 0 -> 42 // « Jean » seul : l'évangile
                    numero in 1..max -> trouve.livre + numero - 1
                    else -> trouve.livre
                }
            }
            var k = i + trouve.longueur
            var motChapitre = false
            var sauts = 0
            while (k < t.size && t[k] in AVANT_CHAPITRE && sauts < 3) { if (t[k].startsWith("ch")) motChapitre = true; k++; sauts++ }
            val chap = nombre(t, k)
            if (chap == null) { i += trouve.longueur; continue }
            dernierLivre = i
            val v = verset(chap.second)
            val fin = v?.third ?: chap.second
            // nom pas sûr (entendu de travers, autre langue) ou prénom courant (« Jean 3 fois ») : il faut le verset ou « chapitre »
            if (v == null && !motChapitre && (!trouve.sur || livre in PRENOMS)) { i = fin; continue }
            ajouter(Ref(livre, chap.first, v?.first ?: 0, v?.second ?: 0), v != null, fin)
            i = fin
        }
        return out
    }
}
