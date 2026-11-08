package ca.mepc.verselight.remote

import java.text.Normalizer

/** Livres de la Bible et abréviations : les mêmes que bible.js dans VerseLight. */
object Books {
    private val BOOKS = listOf(
        "Genèse" to "gn gen ge genese", "Exode" to "ex exo exod exode", "Lévitique" to "lv lev levit levitique",
        "Nombres" to "nb nom nomb nombres", "Deutéronome" to "dt deut deuteronome", "Josué" to "jos josue",
        "Juges" to "jg jug juges", "Ruth" to "rt ru ruth", "1 Samuel" to "1s 1sa 1sam 1samuel", "2 Samuel" to "2s 2sa 2sam 2samuel",
        "1 Rois" to "1r 1ro 1rois", "2 Rois" to "2r 2ro 2rois", "1 Chroniques" to "1ch 1chr 1chron 1chroniques",
        "2 Chroniques" to "2ch 2chr 2chron 2chroniques", "Esdras" to "esd esdr esdras", "Néhémie" to "ne neh nehemie",
        "Esther" to "est esth esther", "Job" to "jb job", "Psaumes" to "ps psa psau psaume psaumes",
        "Proverbes" to "pr pro prov proverbe proverbes", "Ecclésiaste" to "ec ecc eccl ecclesiaste qo",
        "Cantique des cantiques" to "ct ca cant cantique cantiques cantiquedescantiques", "Ésaïe" to "es esa esaie is isaie",
        "Jérémie" to "jr jer jerem jeremie", "Lamentations" to "lm lam lament lamentations", "Ézéchiel" to "ez eze ezech ezechiel",
        "Daniel" to "dn da dan daniel", "Osée" to "os osee", "Joël" to "jl joe joel", "Amos" to "am amo amos",
        "Abdias" to "ab abd abdias", "Jonas" to "jon jonas", "Michée" to "mi mic michee", "Nahum" to "na nah nahum",
        "Habacuc" to "ha hab habacuc", "Sophonie" to "so sop soph sophonie", "Aggée" to "ag agg aggee",
        "Zacharie" to "za zac zach zacharie", "Malachie" to "ml mal malachie", "Matthieu" to "mt mat matt matthieu",
        "Marc" to "mc mr marc", "Luc" to "lc lu luc", "Jean" to "jn jea jean", "Actes" to "ac act actes",
        "Romains" to "rm ro rom romains", "1 Corinthiens" to "1co 1cor 1corinthiens", "2 Corinthiens" to "2co 2cor 2corinthiens",
        "Galates" to "ga gal galates", "Éphésiens" to "ep eph ephes ephesiens", "Philippiens" to "ph phi phil philip philippiens",
        "Colossiens" to "col colos colossiens", "1 Thessaloniciens" to "1th 1thes 1thess 1thessaloniciens",
        "2 Thessaloniciens" to "2th 2thes 2thess 2thessaloniciens", "1 Timothée" to "1tm 1ti 1tim 1timothee",
        "2 Timothée" to "2tm 2ti 2tim 2timothee", "Tite" to "tt tit tite", "Philémon" to "phm phl philem philemon",
        "Hébreux" to "he heb hebr hebreux", "Jacques" to "jc jq jac jacq jacques", "1 Pierre" to "1p 1pi 1pie 1pierre",
        "2 Pierre" to "2p 2pi 2pie 2pierre", "1 Jean" to "1jn 1j 1jean", "2 Jean" to "2jn 2j 2jean", "3 Jean" to "3jn 3j 3jean",
        "Jude" to "jud jude", "Apocalypse" to "ap apo apoc apocalypse",
    ).map { (name, ab) -> Triple(name, norm(name).replace(Regex("[^a-z0-9]"), ""), ab.split(" ")) }

    fun norm(s: String): String =
        Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "")

    /** Livres proposés pendant qu'on tape le nom (avant le numéro du chapitre). */
    fun suggest(input: String): List<String> {
        val s = norm(input).trim()
        if (!Regex("^[123]?\\s*[a-z][a-z ]*$").matches(s)) return emptyList()
        val key = s.replace(" ", "")
        return BOOKS.filter { (_, compact, ab) -> compact.startsWith(key) || ab.any { it.startsWith(key) } }
            .map { it.first }
            .take(8)
    }
}
