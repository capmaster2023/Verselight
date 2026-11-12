package ca.mepc.verselight.remote

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.pm.PackageManager
import android.speech.RecognizerIntent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import kotlinx.coroutines.delay
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Commande tapée ou dictée : « jn 3 16 », « chant 154 », « noir », « retirer », « suivant ». */
fun executerCommande(remote: Remote, texte: String) {
    val t = texte.trim()
    if (t.isEmpty()) return
    val n = Books.norm(t)
    when {
        n == "noir" || n == "ecran noir" -> remote.send("noir")
        n in listOf("retirer", "effacer", "vider", "enlever") -> remote.send("clear")
        n in listOf("suivant", "suivante", "+") -> remote.send("next")
        n in listOf("precedent", "precedente", "-") -> remote.send("prev")
        n.startsWith("chant") || n.startsWith("cantique") || n.startsWith("hymne") || t.all { it.isDigit() } -> {
            AppEtat.chercherChant.value = t.replace(Regex("^(?i)(chants?|cantiques?|hymnes?)\\s*"), "")
            AppEtat.onglet.value = 2
        }
        else -> if (!remote.send("bible.show", "ref" to t)) Bulles.dire("MyChurch n’est pas relié au PC.")
    }
}

@Composable
fun RegieOnglet(remote: Remote, ouvrirReglages: () -> Unit) {
    val s by remote.screen.collectAsState()
    val link by remote.link.collectAsState()
    val proto by remote.proto.collectAsState()
    val agenda by remote.agenda.collectAsState()
    val haptique = LocalHapticFeedback.current
    val pret = link == Link.CONNECTED

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        EnTete(surtitreRegie(s, agenda), "Régie") {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                PastilleConnexion(remote)
                Touche(onClick = ouvrirReglages, modifier = Modifier.size(44.dp), fond = C.Carte, bord = C.Trait, rayon = 22.dp, description = "Réglages", padding = PaddingValues(0.dp)) {
                    Ico(I.reglages, C.Encre, 20.dp)
                }
            }
        }
        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            BarreCommande(remote)
            CarteDirect(s, link, proto)

            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Touche(
                    onClick = { haptique.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("prev") },
                    modifier = Modifier.weight(1f).height(64.dp), bord = C.Trait2, rayon = 18.dp, actif = pret,
                    padding = PaddingValues(horizontal = 8.dp),
                ) {
                    Ico(I.gauche, C.Encre, 20.dp)
                    T("Précédent", taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                }
                Touche(
                    onClick = { haptique.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("next") },
                    modifier = Modifier.weight(2f).height(64.dp), fond = C.Marine, rayon = 18.dp, actif = pret,
                    padding = PaddingValues(start = 18.dp, end = 14.dp), alignement = Arrangement.SpaceBetween,
                ) {
                    Column(Modifier.weight(1f)) {
                        T("Suivant", taille = 17.sp, poids = FontWeight.ExtraBold, couleur = Color.White, lignes = 1)
                        T(ensuiteLive(s), taille = 12.sp, couleur = C.SurMarine, lignes = 1)
                    }
                    Box(Modifier.size(36.dp).clip(RoundedCornerShape(12.dp)).background(C.OrClair), contentAlignment = Alignment.Center) {
                        Ico(I.droite, C.Marine, 20.dp)
                    }
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                val noir = s?.optBoolean("noir") == true
                Touche(
                    onClick = { remote.send("noir") },
                    modifier = Modifier.weight(1f).height(48.dp).semantics { contentDescription = if (noir) "Noir, activé" else "Noir" },
                    fond = if (noir) C.Marine else C.Carte, bord = if (noir) C.Marine else C.Trait2, actif = pret && proto >= 2,
                ) {
                    Ico(I.noir, if (noir) Color.White else C.Encre, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Noir", taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = if (noir) Color.White else C.Encre)
                }
                Touche(onClick = { remote.send("clear") }, modifier = Modifier.weight(1f).height(48.dp), bord = C.RougeBord, actif = pret) {
                    Ico(I.croix, C.RougeTexte, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Retirer", taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = C.RougeTexte)
                }
            }
            if (remote.store.volumeKeys) T("Boutons de volume : + Suivant · − Précédent", taille = 12.sp, couleur = C.Brume, centre = true, modifier = Modifier.fillMaxWidth())
            if (proto >= 2) EcransParDefaut(remote, s, pret)
            DetecteurCarte(remote, pret)
        }
    }
}

/** « Dim. 15 novembre · étape 6 sur 8 », ou le nom du déroulé. */
private fun surtitreRegie(s: JSONObject?, agenda: JSONObject?): String {
    val elements = agenda?.optJSONArray("elements")
    val id = s?.optString("element").orEmpty()
    if (elements != null && elements.length() > 0) {
        val i = (0 until elements.length()).firstOrNull { elements.getJSONObject(it).optString("id") == id }
        val nom = agenda?.optString("nom").orEmpty().ifBlank { "Culte" }
        return if (i != null) "$nom · étape ${i + 1} sur ${elements.length()}" else nom
    }
    return SimpleDateFormat("EEEE d MMMM", Locale.FRANCE).format(Date()).replaceFirstChar { it.uppercase() }
}

@Composable
private fun BarreCommande(remote: Remote) {
    val context = LocalContext.current
    val focus = LocalFocusManager.current
    var commande by rememberSaveable { mutableStateOf("") }
    fun envoyer(t: String) {
        executerCommande(remote, t)
        commande = ""
        focus.clearFocus()
    }
    val dictee = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        r.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()?.let { envoyer(it) }
    }
    val micro = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    fun dicter() {
        val i = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE, "fr-CA")
            .putExtra(RecognizerIntent.EXTRA_PROMPT, "Dites « Jean 3 16 », « chant 154 » ou « noir »")
        try {
            dictee.launch(i)
        } catch (e: ActivityNotFoundException) {
            Bulles.dire("Pas de dictée vocale sur ce téléphone.")
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) micro.launch(Manifest.permission.RECORD_AUDIO)
        }
    }
    Row(
        Modifier.fillMaxWidth().height(52.dp).clip(RoundedCornerShape(16.dp)).background(C.Carte).border(1.dp, C.Trait, RoundedCornerShape(16.dp))
            .padding(start = 14.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Ico(I.loupe, C.Brume, 20.dp)
        BasicTextField(
            value = commande,
            onValueChange = { commande = it },
            modifier = Modifier.weight(1f).semantics { contentDescription = "Commande rapide" },
            singleLine = true,
            textStyle = TextStyle(fontFamily = Manrope, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = C.Encre),
            cursorBrush = SolidColor(C.Or),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Text, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { envoyer(commande) }),
            decorationBox = { champ ->
                Box(contentAlignment = Alignment.CenterStart) {
                    if (commande.isEmpty()) T("jn 3 16 · chant 154 · noir", taille = 15.sp, couleur = C.Brume, lignes = 1)
                    champ()
                }
            },
        )
        Touche(onClick = { dicter() }, modifier = Modifier.size(44.dp), fond = C.OrPale, rayon = 12.dp, description = "Dicter une commande", padding = PaddingValues(0.dp)) {
            Ico(I.micro, C.Or, 20.dp)
        }
    }
}

/** La grande carte : ce qui est à l'écran, avec la diapositive suivante. */
@Composable
private fun CarteDirect(s: JSONObject?, link: Link, proto: Int) {
    val pret = link == Link.CONNECTED
    val vue = VueEcran.de(s)
    val surDefaut = s?.optBoolean("surDefaut") == true
    val (badge, couleur) = when {
        !pret -> (if (link == Link.CONNECTING) "Connexion" else "Hors ligne") to C.Brume
        vue.mode == "noir" -> "Écran noir" to C.Marine
        surDefaut && vue.mode != "media" -> "Écran par défaut" to C.Brume
        vue.mode == "vide" -> "Écran vide" to C.Brume
        else -> "En direct" to C.Rouge
    }
    val enDirect = couleur == C.Rouge
    var heure by remember { mutableStateOf(SimpleDateFormat("HH:mm", Locale.FRANCE).format(Date())) }
    LaunchedEffect(Unit) {
        while (true) {
            heure = SimpleDateFormat("HH:mm", Locale.FRANCE).format(Date())
            delay(15_000)
        }
    }
    Carte(Modifier.shadow(6.dp, RoundedCornerShape(22.dp), ambientColor = Color(0x14000000), spotColor = Color(0x14000000)), rayon = 22.dp) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Row(
                Modifier.height(26.dp).clip(RoundedCornerShape(13.dp)).background(couleur).padding(horizontal = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Point(Color.White, 7.dp)
                T(badge, taille = 11.5.sp, poids = FontWeight.ExtraBold, couleur = Color.White, majuscules = true, espacement = 0.6.sp, lignes = 1)
            }
            Spacer(Modifier.weight(1f))
            T("Projecteur · $heure", taille = 12.sp, poids = FontWeight.Bold, couleur = C.Brume, lignes = 1)
        }
        EcranApercu(vue, anneau = if (enDirect) C.Rouge else C.Trait)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Column(Modifier.weight(1f)) {
                Surtitre("À l’écran")
                T(titreLive(s, link), taille = 18.sp, poids = FontWeight.ExtraBold, interligne = 22.sp, lignes = 2)
                val sous = s?.optString("sous").orEmpty()
                if (sous.isNotBlank()) T(sous, taille = 12.5.sp, couleur = C.Encre2, lignes = 2)
                else if (pret && proto < 2) T("Mettez MyChurch à jour sur le PC (3.4)", taille = 12.5.sp, couleur = C.Encre2, lignes = 2)
            }
            Column(Modifier.width(112.dp)) {
                Surtitre("Ensuite", C.AmbreTexte, Modifier.padding(bottom = 4.dp))
                val suivante = VueEcran.suivante(s)
                if (suivante != null) EcranApercu(suivante, rayon = 8.dp, anneau = C.AmbreAnneau, legende = false)
                else Box(
                    Modifier.fillMaxWidth().aspectRatio(16f / 9f).clip(RoundedCornerShape(8.dp)).background(C.Sable),
                    contentAlignment = Alignment.Center,
                ) { T(if (pret) ensuiteLive(s) else "—", taille = 11.sp, poids = FontWeight.Bold, couleur = C.Brume, lignes = 2, centre = true, modifier = Modifier.padding(4.dp)) }
            }
        }
    }
}

/** Écran par défaut : Retirer revient direct dessus (noir, fond seul ou un écran principal du déroulé). */
@Composable
private fun EcransParDefaut(remote: Remote, s: JSONObject?, pret: Boolean) {
    if (s == null) return
    val a = s.optJSONArray("defauts") ?: return
    val choix = (0 until a.length()).map { a.getJSONObject(it) }
    if (choix.isEmpty()) return
    val actuel = s.optJSONObject("defaut")
    val idActuel = actuel?.optString("id").orEmpty()
    val theme = s.optString("theme", "nuit").ifBlank { "nuit" }
    Carte(rayon = 20.dp, padding = PaddingValues(start = 12.dp, end = 12.dp, top = 14.dp, bottom = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Ico(I.epingle, C.Or, 18.dp)
            T("Écran par défaut", taille = 14.5.sp, poids = FontWeight.ExtraBold, modifier = Modifier.weight(1f), lignes = 1)
            T("Retour direct : " + (actuel?.optString("nom").orEmpty().ifBlank { "Noir" }), taille = 12.sp, poids = FontWeight.Bold, couleur = C.Or, lignes = 1)
        }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            choix.forEach { d ->
                val id = d.optString("id")
                val on = id == idActuel
                val sorte = d.optString("sorte")
                val vue = when (sorte) {
                    "noir" -> VueEcran("noir", emptyList(), "", "", theme, "")
                    "fond" -> VueEcran("fond", emptyList(), "", "", theme, "")
                    "texte" -> VueEcran("chant", listOf(d.optString("nom")), "", "", theme, "")
                    else -> VueEcran("media", emptyList(), "", "", theme, sorte)
                }
                Column(
                    Modifier.width(84.dp).clip(RoundedCornerShape(12.dp)).background(if (on) C.OrPale else C.Carte)
                        .border(1.5.dp, if (on) C.OrVif else C.Trait, RoundedCornerShape(12.dp))
                        .selectable(selected = on, enabled = pret, role = Role.RadioButton) { remote.send("default.set", "id" to id) }
                        .padding(start = 5.dp, end = 5.dp, top = 5.dp, bottom = 7.dp),
                    verticalArrangement = Arrangement.spacedBy(5.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    EcranApercu(vue, rayon = 7.dp, anneau = C.Trait, legende = false)
                    T(d.optString("nom"), taille = 11.5.sp, poids = FontWeight.ExtraBold, lignes = 1, centre = true)
                }
            }
        }
    }
}

/** Détecteur de versets : écoute le prédicateur et propose les versets cités (bouton Afficher). */
@Composable
fun DetecteurCarte(remote: Remote, pret: Boolean) {
    val context = LocalContext.current
    val haptique = LocalHapticFeedback.current
    val actif by Detection.actif.collectAsState()
    val statut by Detection.statut.collectAsState()
    val entendu by Detection.entendu.collectAsState()
    val suggestions by Detection.suggestions.collectAsState()
    val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { res ->
        if (res[Manifest.permission.RECORD_AUDIO] == true || ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) Ecoute.demarrer(context)
        else Bulles.dire("Le micro est nécessaire pour détecter les versets cités.")
    }
    fun basculer(on: Boolean) {
        if (!on) {
            Ecoute.arreter(context)
            return
        }
        val manque = ArrayList<String>()
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) manque.add(Manifest.permission.RECORD_AUDIO)
        if (android.os.Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) manque.add(Manifest.permission.POST_NOTIFICATIONS)
        if (manque.isEmpty()) Ecoute.demarrer(context) else permissions.launch(manque.toTypedArray())
    }
    Carte(padding = PaddingValues(horizontal = 14.dp, vertical = 6.dp), espace = 6.dp) {
        val info = when {
            actif && entendu.isNotBlank() -> "« …${entendu.takeLast(70)} »"
            actif -> statut.ifBlank { "À l’écoute…" }
            statut.isNotBlank() -> statut
            else -> "Écoute le prédicateur et propose les versets cités"
        }
        LigneReglage("Détecteur de versets", info, actif) { basculer(it) }
        suggestions.take(2).forEach { sug ->
            Row(
                Modifier.fillMaxWidth().padding(bottom = 6.dp).clip(RoundedCornerShape(14.dp)).background(C.OrPale).padding(start = 12.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp),
            ) {
                Column(Modifier.weight(1f)) {
                    T(sug.label, taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = C.Or, lignes = 1)
                    T(sug.texte, taille = 12.5.sp, couleur = C.Encre2, lignes = 2)
                }
                Touche(
                    onClick = {
                        haptique.performHapticFeedback(HapticFeedbackType.LongPress)
                        if (remote.send("bible.show", "ref" to sug.label)) Detection.retirer(sug.id)
                        else Bulles.dire("MyChurch n’est pas relié au PC.")
                    },
                    fond = C.Marine, rayon = 12.dp, actif = pret,
                ) { T("Afficher", couleur = Color.White, poids = FontWeight.ExtraBold) }
                Touche(onClick = { Detection.retirer(sug.id) }, modifier = Modifier.size(44.dp), fond = Color.Transparent, description = "Ignorer", padding = PaddingValues(0.dp)) {
                    Ico(I.croix, C.Brume, 18.dp)
                }
            }
        }
    }
}
