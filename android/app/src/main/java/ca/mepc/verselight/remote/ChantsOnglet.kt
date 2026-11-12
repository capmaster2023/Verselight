package ca.mepc.verselight.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import org.json.JSONObject

@Composable
fun ChantsOnglet(remote: Remote) {
    val focus = LocalFocusManager.current
    val link by remote.link.collectAsState()
    val pret = link == Link.CONNECTED
    val demande by AppEtat.chercherChant.collectAsState()
    var q by rememberSaveable { mutableStateOf("") }
    var resultats by remember { mutableStateOf(listOf<JSONObject>()) }
    var chant by remember { mutableStateOf<JSONObject?>(null) }
    var charge by remember { mutableStateOf(false) }

    // « chant 154 » tapé dans la Régie
    LaunchedEffect(demande) {
        if (demande.isNotBlank()) {
            q = demande
            chant = null
            AppEtat.chercherChant.value = ""
        }
    }
    LaunchedEffect(q, pret) {
        delay(300)
        remote.send("song.search", "q" to q)
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "songs" -> if (e.optString("q") == q) {
                    val a = e.optJSONArray("liste")
                    resultats = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
                    charge = true
                }
                "song" -> chant = e
            }
        }
    }

    val c = chant
    if (c != null) {
        VueChant(remote, c, pret) { chant = null }
        return
    }
    Column(Modifier.fillMaxSize()) {
        EnTete("Recueils du PC · titre, numéro ou paroles", "Chants")
        ChampRecherche(q, { q = it }, "Titre, numéro ou paroles", "Rechercher un chant", { focus.clearFocus() }, Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp))
        LazyColumn(
            Modifier.weight(1f).fillMaxWidth(),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(resultats, key = { it.optString("id") }) { r ->
                val numero = Regex("^\\s*(\\d{1,4})").find(r.optString("title"))?.groupValues?.get(1)
                Row(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.Carte).border(1.dp, C.Trait, RoundedCornerShape(16.dp))
                        .clickable(role = Role.Button) { remote.send("song.open", "id" to r.optString("id")) }
                        .padding(10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(Modifier.size(44.dp).clip(RoundedCornerShape(12.dp)).background(C.Chant.second), contentAlignment = Alignment.Center) {
                        if (numero != null) T(numero, taille = 14.sp, poids = FontWeight.ExtraBold, couleur = C.Chant.first, lignes = 1)
                        else Ico(I.chants, C.Chant.first, 20.dp)
                    }
                    Column(Modifier.weight(1f)) {
                        T(r.optString("title"), taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 2)
                        if (r.optString("author").isNotBlank()) T(r.optString("author"), taille = 12.5.sp, couleur = C.Encre2, lignes = 1)
                    }
                    Ico(I.droite, C.Brume, 18.dp)
                }
            }
            if (resultats.isEmpty()) item {
                T(
                    when {
                        !pret -> "En attente du PC…"
                        !charge -> "Recherche…"
                        else -> "Aucun chant trouvé."
                    },
                    taille = 15.sp, couleur = C.Encre2, modifier = Modifier.padding(8.dp),
                )
            }
        }
        BarreLive(remote)
    }
}

@Composable
private fun VueChant(remote: Remote, chant: JSONObject, pret: Boolean, retour: () -> Unit) {
    val haptique = LocalHapticFeedback.current
    val s by remote.screen.collectAsState()
    val proto by remote.proto.collectAsState()
    val id = chant.optString("id")
    val a = chant.optJSONArray("sections")
    val sections = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
    val live = s?.optJSONObject("chant")
    val courant = if (live != null && live.optString("id") == id && s?.optString("mode") == "chant" && s?.optBoolean("noir") != true) live.optInt("i", -1) else -1
    val refrain = sections.indexOfFirst { Books.norm(it.optString("label")).let { l -> l.startsWith("refrain") || l.startsWith("choeur") || l.startsWith("chorus") || l.startsWith("ref") } }
    fun montrer(i: Int) {
        if (i < 0) return
        haptique.performHapticFeedback(HapticFeedbackType.LongPress)
        remote.send("song.show", "id" to id, "i" to i)
    }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 12.dp, end = 16.dp, top = 12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Touche(onClick = retour, modifier = Modifier.size(44.dp), fond = C.Carte, bord = C.Trait, rayon = 22.dp, description = "Retour à la liste", padding = PaddingValues(0.dp)) {
                Ico(I.gauche, C.Encre, 20.dp)
            }
            Column(Modifier.weight(1f)) {
                T(chant.optString("title"), taille = 19.sp, poids = FontWeight.ExtraBold, lignes = 2)
                val auteur = chant.optString("author")
                T((if (auteur.isNotBlank()) "$auteur · " else "") + "${sections.size} partie" + (if (sections.size > 1) "s" else ""), taille = 12.5.sp, couleur = C.Encre2, lignes = 1)
            }
        }
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, top = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Touche(onClick = { montrer(refrain) }, modifier = Modifier.weight(1f), bord = C.OrBord, actif = pret && refrain >= 0) {
                Ico(I.actualiser, C.Or, 18.dp)
                Spacer(Modifier.width(8.dp))
                T("Refrain", couleur = C.Or, poids = FontWeight.ExtraBold)
            }
            if (proto >= 2) Touche(
                onClick = { if (remote.send("agenda.add", "type" to "chant", "id" to id)) Bulles.dire("« ${chant.optString("title")} » ajouté au culte") },
                modifier = Modifier.weight(1f), bord = C.Trait2, actif = pret,
            ) {
                Ico(I.listePlus, C.Encre, 18.dp)
                Spacer(Modifier.width(8.dp))
                T("Au culte", poids = FontWeight.ExtraBold)
            }
        }
        LazyVerticalGrid(
            columns = GridCells.Fixed(2),
            modifier = Modifier.weight(1f).fillMaxWidth(),
            contentPadding = PaddingValues(16.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            itemsIndexed(sections) { i, sec ->
                val enDirect = i == courant
                val suivant = courant >= 0 && i == courant + 1
                val bord = when {
                    enDirect -> C.Rouge
                    suivant -> C.AmbreAnneau
                    else -> C.Trait
                }
                Column(
                    Modifier.fillMaxWidth().heightIn(min = 132.dp).clip(RoundedCornerShape(16.dp)).background(C.Carte)
                        .border(if (enDirect || suivant) 2.dp else 1.dp, bord, RoundedCornerShape(16.dp))
                        .clickable(enabled = pret, role = Role.Button) { montrer(i) }
                        .semantics { if (enDirect) stateDescription = "à l’écran" else if (suivant) stateDescription = "suivant" }
                        .padding(12.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        T(
                            sec.optString("label").ifBlank { "Strophe ${i + 1}" }, taille = 12.sp, poids = FontWeight.ExtraBold, majuscules = true, espacement = 0.4.sp,
                            couleur = if (enDirect) C.RougeTexte else if (suivant) C.AmbreTexte else C.Brume, lignes = 1, modifier = Modifier.weight(1f),
                        )
                        if (enDirect) Etiquette("À l’écran", C.Rouge, Color.White)
                        else if (suivant) Etiquette("Suivant", C.AmbrePale, C.AmbreTexte)
                    }
                    T(sec.optString("text"), taille = 13.sp, couleur = C.Encre2, interligne = 19.sp, lignes = 5)
                }
            }
        }
        BarreLive(remote)
    }
}

/** Petite étiquette arrondie (« À l'écran », « Suivant », « Par défaut »). */
@Composable
fun Etiquette(texte: String, fond: Color, couleur: Color, bord: Color? = null) {
    var m = Modifier.height(22.dp).clip(RoundedCornerShape(11.dp)).background(fond)
    if (bord != null) m = m.border(1.dp, bord, RoundedCornerShape(11.dp))
    Box(m.padding(horizontal = 8.dp), contentAlignment = Alignment.Center) {
        T(texte, taille = 10.5.sp, poids = FontWeight.ExtraBold, couleur = couleur, lignes = 1, majuscules = true, espacement = 0.3.sp)
    }
}
