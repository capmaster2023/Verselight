package ca.mepc.verselight.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONObject

private class Sorte(val nom: String, val icone: ImageVector, val couleur: Color, val fond: Color)

private fun sorte(e: JSONObject): Sorte = when (e.optString("type")) {
    "chant" -> Sorte("Chant", I.chants, C.Chant.first, C.Chant.second)
    "bible" -> Sorte("Lecture", I.bible, C.Lecture.first, C.Lecture.second)
    "compte" -> Sorte("Accueil", I.horloge, C.Accueil.first, C.Accueil.second)
    "principal" -> Sorte("Écran principal", if (e.optString("source") in listOf("camera", "mycam")) I.cam else I.epingle, C.Principal.first, C.Principal.second)
    "display" -> Sorte("Display", if (e.optString("source") in listOf("camera", "mycam")) I.cam else I.ecran, C.Duo.first, C.Duo.second)
    else -> Sorte("Texte", I.texte, C.Texte.first, C.Texte.second)
}

@Composable
fun CulteOnglet(remote: Remote) {
    val proto by remote.proto.collectAsState()
    var vue by rememberSaveable { mutableStateOf(0) }
    val agenda by remote.agenda.collectAsState()
    Column(Modifier.fillMaxSize()) {
        EnTete(agenda?.optString("nom").orEmpty().ifBlank { "Déroulé et sources du PC" }, "Culte")
        Row(
            Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp).fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(C.Sable).padding(3.dp),
            horizontalArrangement = Arrangement.spacedBy(3.dp),
        ) {
            listOf("Déroulé", "Display").forEachIndexed { i, nom ->
                val on = i == vue
                Box(
                    Modifier.weight(1f).height(44.dp).clip(RoundedCornerShape(11.dp)).background(if (on) C.Carte else Color.Transparent)
                        .selectable(selected = on, role = Role.Tab) { vue = i },
                    contentAlignment = Alignment.Center,
                ) { T(nom, taille = 14.sp, poids = FontWeight.ExtraBold, couleur = if (on) C.Encre else C.Inactif) }
            }
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (vue == 0) {
                if (proto >= 2) Deroule(remote)
                else Info("Mettez MyChurch à jour sur le PC (3.4 ou plus) pour suivre le déroulé ici. Les sources Display marchent déjà.")
            } else Sources(remote)
        }
        BarreLive(remote)
    }
}

@Composable
private fun Info(texte: String) {
    Box(Modifier.fillMaxSize().padding(24.dp)) { T(texte, taille = 15.sp, couleur = C.Encre2, interligne = 22.sp) }
}

@Composable
private fun Deroule(remote: Remote) {
    val agenda by remote.agenda.collectAsState()
    val s by remote.screen.collectAsState()
    val link by remote.link.collectAsState()
    val pret = link == Link.CONNECTED
    LaunchedEffect(pret) { if (pret && remote.agenda.value == null) remote.send("agenda.get") }
    val a = agenda?.optJSONArray("elements")
    val elements = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
    if (elements.isEmpty()) {
        Info(if (!pret) "En attente du PC…" else if (agenda == null) "Chargement du déroulé…" else "Le déroulé est vide. Ajoutez des chants et des lectures depuis Bible et Chants (bouton « au culte »), ou dans MyChurch sur le PC.")
        return
    }
    val courantId = s?.optString("element").orEmpty()
    val defautId = s?.optJSONObject("defaut")?.optString("id").orEmpty()
    val courant = elements.indexOfFirst { it.optString("id") == courantId }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            T(if (courant >= 0) "Étape ${courant + 1} sur ${elements.size}" else "${elements.size} étapes", taille = 12.5.sp, poids = FontWeight.ExtraBold, lignes = 1)
            Box(Modifier.weight(1f).height(6.dp).clip(RoundedCornerShape(3.dp)).background(C.Trait)) {
                val part = if (courant >= 0) (courant + 1f) / elements.size else 0f
                Box(Modifier.fillMaxHeight().fillMaxWidth(part).clip(RoundedCornerShape(3.dp)).background(C.OrVif))
            }
            val fin = elements.lastOrNull { it.optString("heure").isNotBlank() }?.optString("heure").orEmpty()
            if (fin.isNotBlank()) T("Dernier : $fin", taille = 12.sp, poids = FontWeight.Bold, couleur = C.Brume, lignes = 1)
        }
        LazyColumn(
            Modifier.weight(1f).fillMaxWidth(),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            itemsIndexed(elements, key = { _, e -> e.optString("id") }) { i, e ->
                val enDirect = i == courant && s?.optBoolean("noir") != true
                val parDefaut = e.optString("id") == defautId
                val passe = courant >= 0 && i < courant
                val so = sorte(e)
                val bord = when {
                    enDirect -> C.Rouge
                    parDefaut -> Color(0xFFEBCB8B)
                    else -> C.Trait
                }
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 64.dp).clip(RoundedCornerShape(16.dp))
                        .background(if (passe) C.Passe else C.Carte)
                        .border(if (enDirect) 2.dp else 1.dp, bord, RoundedCornerShape(16.dp))
                        .clickable(enabled = pret, role = Role.Button) { remote.send("agenda.show", "id" to e.optString("id")) }
                        .semantics { stateDescription = if (enDirect) "à l’écran" else if (passe) "terminé" else "" }
                        .padding(start = 10.dp, end = 12.dp, top = 10.dp, bottom = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    T(e.optString("heure"), taille = 12.sp, poids = FontWeight.ExtraBold, couleur = C.Brume, modifier = Modifier.width(40.dp), lignes = 1)
                    Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(so.fond), contentAlignment = Alignment.Center) {
                        Ico(so.icone, so.couleur, 20.dp)
                    }
                    Column(Modifier.weight(1f)) {
                        T(so.nom, taille = 10.5.sp, poids = FontWeight.ExtraBold, couleur = so.couleur, majuscules = true, espacement = 0.7.sp, lignes = 1)
                        T(e.optString("titre"), taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                        val sous = e.optString("sous")
                        if (sous.isNotBlank()) T(sous, taille = 12.sp, couleur = C.Encre2, lignes = 1)
                    }
                    when {
                        enDirect -> Etiquette("À l’écran", C.Rouge, Color.White)
                        parDefaut -> Etiquette("Par défaut", Color(0xFFFFF1D2), C.Or, Color(0xFFEBCB8B))
                        passe -> Box(Modifier.size(22.dp).clip(CircleShape).background(C.Trait), contentAlignment = Alignment.Center) { Ico(I.coche, C.Brume, 14.dp) }
                        else -> {}
                    }
                }
            }
        }
    }
}

@Composable
private fun Sources(remote: Remote) {
    val liste by remote.displays.collectAsState()
    val s by remote.screen.collectAsState()
    val link by remote.link.collectAsState()
    val pret = link == Link.CONNECTED
    LaunchedEffect(pret) { if (pret) remote.send("display.list") }
    val sources = liste.orEmpty()
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 12.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            T("Textes, images, caméras, MyCam et fenêtres de Display", taille = 12.5.sp, couleur = C.Encre2, modifier = Modifier.weight(1f), lignes = 2)
            Touche(onClick = { remote.send("display.list") }, fond = Color.Transparent, actif = pret, description = "Actualiser la liste") {
                Ico(I.actualiser, C.Or, 18.dp)
            }
        }
        LazyColumn(
            Modifier.weight(1f).fillMaxWidth(),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            itemsIndexed(sources, key = { _, x -> x.optString("id") }) { _, x ->
                val enDirect = s?.optString("displayId") == x.optString("id") && s?.optString("mode") != "vide"
                val type = x.optString("type")
                val icone = when (type) {
                    "camera", "mycam" -> I.cam
                    "fenetre" -> I.fenetre
                    "image" -> I.image
                    "video" -> I.regie
                    "duo" -> I.duo
                    else -> I.texte
                }
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 64.dp).clip(RoundedCornerShape(16.dp)).background(C.Carte)
                        .border(if (enDirect) 2.dp else 1.dp, if (enDirect) C.Rouge else C.Trait, RoundedCornerShape(16.dp))
                        .clickable(enabled = pret, role = Role.Button) { remote.send("display.show", "id" to x.optString("id")) }
                        .padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(C.Duo.second), contentAlignment = Alignment.Center) { Ico(icone, C.Duo.first, 20.dp) }
                    Column(Modifier.weight(1f)) {
                        T(x.optString("titre"), taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                        T(x.optString("description"), taille = 12.sp, couleur = C.Encre2, lignes = 2)
                    }
                    if (enDirect) Etiquette("À l’écran", C.Rouge, Color.White)
                }
            }
            if (sources.isEmpty()) item {
                T(if (!pret) "En attente du PC…" else if (liste == null) "Chargement…" else "Aucune source. Ajoutez-en dans Display sur le PC.", taille = 15.sp, couleur = C.Encre2, modifier = Modifier.padding(8.dp))
            }
        }
    }
}
