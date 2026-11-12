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
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
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
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import org.json.JSONObject

/** Champ de recherche (Bible, Chants). */
@Composable
fun ChampRecherche(
    valeur: String,
    changer: (String) -> Unit,
    indice: String,
    description: String,
    valider: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier.fillMaxWidth().height(52.dp).clip(RoundedCornerShape(16.dp)).background(C.Carte).border(1.dp, C.Trait, RoundedCornerShape(16.dp))
            .padding(start = 14.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Ico(I.loupe, C.Brume, 20.dp)
        BasicTextField(
            value = valeur,
            onValueChange = changer,
            modifier = Modifier.weight(1f).semantics { contentDescription = description },
            singleLine = true,
            textStyle = TextStyle(fontFamily = Manrope, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, color = C.Encre),
            cursorBrush = SolidColor(C.Or),
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.None, keyboardType = KeyboardType.Text, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { valider() }),
            decorationBox = { champ ->
                Box(contentAlignment = Alignment.CenterStart) {
                    if (valeur.isEmpty()) T(indice, taille = 16.sp, couleur = C.Brume, lignes = 1)
                    champ()
                }
            },
        )
        if (valeur.isNotEmpty()) {
            Touche(onClick = { changer("") }, modifier = Modifier.size(44.dp), fond = Color.Transparent, description = "Effacer la recherche", padding = PaddingValues(0.dp)) {
                Ico(I.croix, C.Brume, 18.dp)
            }
        }
    }
}

/** Pastille de choix (livres, recueils). */
@Composable
fun Puce(texte: String, on: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.heightIn(min = 44.dp).clip(RoundedCornerShape(12.dp))
            .background(if (on) C.Marine else C.Carte)
            .border(1.dp, if (on) C.Marine else C.Trait, RoundedCornerShape(12.dp))
            .clickable(role = Role.Button, onClick = onClick)
            .padding(horizontal = 14.dp),
        contentAlignment = Alignment.Center,
    ) { T(texte, taille = 14.sp, poids = FontWeight.Bold, couleur = if (on) Color.White else C.Encre, lignes = 1) }
}

@Composable
fun BibleOnglet(remote: Remote) {
    val focus = LocalFocusManager.current
    val haptique = LocalHapticFeedback.current
    val link by remote.link.collectAsState()
    val proto by remote.proto.collectAsState()
    val pret = link == Link.CONNECTED
    var q by rememberSaveable { mutableStateOf("") }
    var apercu by remember { mutableStateOf<JSONObject?>(null) }
    var resultats by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
    var message by remember { mutableStateOf("") }
    var choix by remember { mutableStateOf<Set<Int>>(emptySet()) }

    LaunchedEffect(q, pret) {
        if (q.isBlank()) {
            apercu = null; resultats = emptyList(); message = ""
            return@LaunchedEffect
        }
        delay(350)
        remote.send("bible.preview", "ref" to q)
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "preview" -> {
                    apercu = e
                    resultats = emptyList()
                    message = ""
                    val vs = e.optJSONArray("versets")
                    val n = vs?.length() ?: 0
                    // un passage précis : tous ses versets sont choisis ; un chapitre entier : on choisit soi-même
                    choix = if (n in 1..8) (0 until n).map { vs!!.getJSONObject(it).optInt("v") }.toSet() else emptySet()
                }
                "error" -> if (e.optString("message").startsWith("Référence")) {
                    apercu = null
                    val mots = q.trim()
                    if (mots.length >= 3 && mots.none { it.isDigit() }) {
                        message = "Recherche de « $mots »…"
                        remote.send("bible.search", "q" to mots)
                    } else {
                        resultats = emptyList()
                        message = e.optString("message")
                    }
                }
                "search" -> if (e.optString("q") == q.trim()) {
                    val a = e.optJSONArray("liste")
                    resultats = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
                    message = if (resultats.isEmpty()) "Aucun verset trouvé pour « ${q.trim()} »." else ""
                }
            }
        }
    }

    val p = apercu
    val livre = p?.optString("livre").orEmpty()
    val chapitre = p?.optInt("c") ?: 0
    val versets = remember(p) {
        val a = p?.optJSONArray("versets")
        if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
    }
    val tries = choix.sorted()
    val refChoisie = when {
        p == null -> ""
        tries.isEmpty() || livre.isBlank() -> p.optString("ref").ifBlank { q.trim() }
        tries.first() == tries.last() -> "$livre $chapitre:${tries.first()}"
        else -> "$livre $chapitre:${tries.first()}-${tries.last()}"
    }
    fun afficher() {
        if (q.isBlank()) return
        haptique.performHapticFeedback(HapticFeedbackType.LongPress)
        focus.clearFocus()
        if (remote.send("bible.show", "ref" to (refChoisie.ifBlank { q.trim() }))) Bulles.dire("${refChoisie.ifBlank { q.trim() }} à l’écran")
        else Bulles.dire("MyChurch n’est pas relié au PC.")
    }

    Column(Modifier.fillMaxSize()) {
        EnTete("Bible du PC · référence ou mots", "Bible")
        Column(Modifier.padding(horizontal = 16.dp).padding(top = 12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            ChampRecherche(q, { q = it }, "jn 3 16 · ps 23 · charité patiente", "Rechercher un passage", { afficher() })
            val livres = Books.suggest(q)
            if (livres.isNotEmpty()) {
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(livres) { nom -> Puce(nom, false) { q = "$nom " } }
                }
            }
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            when {
                p != null -> LazyColumn(
                    contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    item {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            T(p.optString("ref"), taille = 17.sp, poids = FontWeight.ExtraBold, modifier = Modifier.weight(1f), lignes = 1)
                            if (versets.size > 1) T("Touchez pour choisir", taille = 12.sp, couleur = C.Brume, lignes = 1)
                        }
                    }
                    items(versets, key = { it.optInt("v") }) { v ->
                        val n = v.optInt("v")
                        val on = n in choix
                        Row(
                            Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp))
                                .background(if (on) C.OrPale else C.Carte)
                                .border(1.5.dp, if (on) C.OrBord else C.Trait, RoundedCornerShape(16.dp))
                                .clickable(role = Role.Checkbox, onClickLabel = "Choisir le verset $n") { choix = if (on) choix - n else choix + n }
                                .padding(start = 14.dp, end = 12.dp, top = 12.dp, bottom = 12.dp),
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            T(n.toString(), taille = 13.sp, poids = FontWeight.ExtraBold, couleur = C.Or, modifier = Modifier.width(24.dp).padding(top = 2.dp))
                            T(v.optString("t"), taille = 15.5.sp, interligne = 23.sp, modifier = Modifier.weight(1f))
                            Box(
                                Modifier.size(24.dp).clip(CircleShape).background(if (on) C.Or else C.Carte)
                                    .border(1.5.dp, if (on) C.Or else C.Trait2, CircleShape),
                                contentAlignment = Alignment.Center,
                            ) { if (on) Ico(I.coche, Color.White, 14.dp) }
                        }
                    }
                }
                resultats.isNotEmpty() -> LazyColumn(
                    contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 12.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    item { T("${resultats.size} verset(s) · touchez pour l’ouvrir", taille = 12.5.sp, couleur = C.Brume) }
                    items(resultats) { r ->
                        Column(
                            Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.Carte).border(1.dp, C.Trait, RoundedCornerShape(16.dp))
                                .clickable(role = Role.Button) { q = r.optString("ref") }.padding(14.dp),
                            verticalArrangement = Arrangement.spacedBy(4.dp),
                        ) {
                            val version = r.optString("v")
                            T(r.optString("ref") + if (version.isNotBlank()) " · $version" else "", taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = C.Or)
                            T(r.optString("text"), taille = 14.sp, couleur = C.Encre2, lignes = 3, interligne = 20.sp)
                        }
                    }
                }
                else -> Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    T(
                        message.ifBlank { "Tapez une référence (« mt 3 2 », « jn 3 16-18 », « ps 23 ») ou des mots (« charité patiente »)." },
                        taille = 15.sp, couleur = C.Encre2, interligne = 22.sp,
                    )
                    if (message.isBlank()) DetecteurCarte(remote, pret)
                }
            }
        }
        if (p != null) {
            Row(
                Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, bottom = 10.dp)
                    .shadow(12.dp, RoundedCornerShape(20.dp)).clip(RoundedCornerShape(20.dp)).background(C.Carte)
                    .border(1.dp, C.Trait, RoundedCornerShape(20.dp)).padding(start = 16.dp, end = 10.dp, top = 10.dp, bottom = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Column(Modifier.weight(1f)) {
                    T(refChoisie, taille = 16.sp, poids = FontWeight.ExtraBold, lignes = 1)
                    val n = if (tries.isEmpty()) versets.size else tries.last() - tries.first() + 1
                    T(if (n > 1) "$n versets" else "1 verset", taille = 12.5.sp, couleur = C.Encre2, lignes = 1)
                }
                if (proto >= 2) {
                    Touche(
                        onClick = { if (remote.send("agenda.add", "type" to "bible", "ref" to refChoisie)) Bulles.dire("$refChoisie ajouté au culte") },
                        modifier = Modifier.size(48.dp), bord = C.OrBord, actif = pret, description = "Ajouter au culte", padding = PaddingValues(0.dp),
                    ) { Ico(I.listePlus, C.Or, 20.dp) }
                }
                Touche(onClick = { afficher() }, modifier = Modifier.height(48.dp), fond = C.Marine, actif = pret, padding = PaddingValues(horizontal = 18.dp)) {
                    Ico(I.regie, C.OrClair, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Afficher", taille = 15.sp, poids = FontWeight.ExtraBold, couleur = Color.White)
                }
            }
        } else BarreLive(remote)
    }
}
