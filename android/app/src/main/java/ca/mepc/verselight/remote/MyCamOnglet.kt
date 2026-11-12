package ca.mepc.verselight.remote

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.ActivityInfo
import android.os.SystemClock
import android.provider.Settings
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.view.PreviewView
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import kotlinx.coroutines.delay
import org.json.JSONObject
import java.util.Locale
import kotlin.math.abs
import kotlin.math.roundToInt

/** État du témoin tel que l'affiche le téléphone. */
private class Temoin(val titre: String, val droite: String, val fond: Color, val bord: Color, val point: Color, val anneau: Color?)

@Composable
private fun temoinActuel(remote: Remote): Temoin {
    val t by remote.temoin.collectAsState()
    val diffuse by MyCam.diffuse.collectAsState()
    val link by remote.link.collectAsState()
    val proto by remote.proto.collectAsState()
    val depuis by MyCam.enDirectDepuis.collectAsState()
    var maintenant by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(depuis) {
        while (depuis > 0) {
            maintenant = System.currentTimeMillis()
            delay(1000)
        }
    }
    val sombre = Temoin("", "", C.CamSurface2, C.CamBord2, C.CamBrume, null)
    return when {
        link != Link.CONNECTED -> Temoin("PC introuvable", "reconnexion…", sombre.fond, sombre.bord, C.CamAmbreVif, null)
        proto < 2 -> Temoin("MyChurch 3.4 requis sur le PC", "mise à jour", sombre.fond, sombre.bord, C.CamAmbreVif, null)
        !diffuse -> Temoin("Diffusion arrêtée", "rien n’est envoyé", sombre.fond, sombre.bord, C.CamBrume, null)
        t == "live" -> {
            val s = if (depuis > 0) ((maintenant - depuis) / 1000).coerceAtLeast(0) else 0
            Temoin("À l’écran dans l’église", String.format(Locale.FRANCE, "%d:%02d", s / 60, s % 60), C.CamRouge, C.RougeVif, Color.White, C.RougeVif)
        }
        t == "next" -> Temoin("Vous passez ensuite", "préparez le plan", C.Ambre, C.CamAmbreVif, Color.White, C.CamAmbreVif)
        else -> Temoin("En ligne, pas à l’écran", "témoin éteint", sombre.fond, sombre.bord, C.CamVert, null)
    }
}

@Composable
private fun BandeauTemoin(t: Temoin) {
    Row(
        Modifier.fillMaxWidth().height(46.dp).clip(RoundedCornerShape(14.dp)).background(t.fond).border(1.dp, t.bord, RoundedCornerShape(14.dp))
            .semantics { contentDescription = "Témoin : ${t.titre}" }
            .padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Point(t.point, 10.dp)
        T(t.titre, taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = Color.White, modifier = Modifier.weight(1f), lignes = 1)
        T(t.droite, taille = 13.sp, poids = FontWeight.Bold, couleur = if (t.anneau != null) Color.White else C.CamBrume2, lignes = 1)
    }
}

/** Viseur : aperçu de la caméra, grille des tiers, mise au point au toucher, zoom à deux doigts. */
@Composable
private fun Viseur(modifier: Modifier, grille: Boolean, contenu: @Composable BoxScope.() -> Unit) {
    val ref = remember { arrayOfNulls<PreviewView>(1) }
    var focus by remember { mutableStateOf<Offset?>(null) }
    DisposableEffect(Unit) { onDispose { MyCam.delierApercu(ref[0]) } }
    LaunchedEffect(focus) {
        if (focus != null) {
            delay(1500)
            focus = null
        }
    }
    Box(
        modifier.background(Color.Black)
            .pointerInput(Unit) { detectTapGestures { o -> MyCam.mettreAuPoint(o.x, o.y); focus = o } }
            .pointerInput(Unit) { detectTransformGestures { _, _, z, _ -> if (z != 1f) MyCam.reglerZoom(MyCam.zoom.value * z) } },
    ) {
        AndroidView(
            factory = { c ->
                PreviewView(c).apply {
                    implementationMode = PreviewView.ImplementationMode.COMPATIBLE
                    scaleType = PreviewView.ScaleType.FILL_CENTER
                }.also {
                    ref[0] = it
                    MyCam.lierApercu(it)
                }
            },
            modifier = Modifier.fillMaxSize(),
        )
        if (grille) {
            Canvas(Modifier.fillMaxSize()) {
                val c = Color.White.copy(alpha = 0.4f)
                for (k in 1..2) {
                    drawLine(c, Offset(size.width * k / 3f, 0f), Offset(size.width * k / 3f, size.height), 1.dp.toPx())
                    drawLine(c, Offset(0f, size.height * k / 3f), Offset(size.width, size.height * k / 3f), 1.dp.toPx())
                }
            }
        }
        focus?.let { o ->
            Box(
                Modifier.offset { IntOffset((o.x - 28.dp.toPx()).roundToInt(), (o.y - 28.dp.toPx()).roundToInt()) }
                    .size(56.dp).border(1.5.dp, C.OrClair, RoundedCornerShape(6.dp))
            )
        }
        contenu()
    }
}

/** Petite pastille posée sur l'image (« 1280 × 720 · 30 i/s »). */
@Composable
private fun Info(texte: String, modifier: Modifier = Modifier) {
    Box(Modifier.then(modifier).height(24.dp).clip(RoundedCornerShape(8.dp)).background(Color(0xA80B0F1A)).padding(horizontal = 8.dp), contentAlignment = Alignment.Center) {
        T(texte, taille = 11.sp, poids = FontWeight.Bold, couleur = Color.White, lignes = 1)
    }
}

/** Assombrit l'écran du téléphone après 25 s sans toucher pendant la diffusion (la caméra continue). */
@Composable
private fun Veille(actif: Boolean, contenu: @Composable () -> Unit) {
    val activite = LocalContext.current as? Activity
    var dernier by remember { mutableLongStateOf(SystemClock.elapsedRealtime()) }
    var dort by remember { mutableStateOf(false) }
    LaunchedEffect(actif, dernier) {
        dort = false
        if (actif) {
            delay(25_000)
            dort = true
        }
    }
    DisposableEffect(dort) {
        activite?.window?.let { w ->
            val p = w.attributes
            p.screenBrightness = if (dort) 0.01f else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
            w.attributes = p
        }
        onDispose {
            activite?.window?.let { w ->
                val p = w.attributes
                p.screenBrightness = WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
                w.attributes = p
            }
        }
    }
    Box(
        Modifier.fillMaxSize().pointerInput(Unit) {
            awaitPointerEventScope {
                while (true) {
                    awaitPointerEvent(PointerEventPass.Initial)
                    dernier = SystemClock.elapsedRealtime()
                }
            }
        }
    ) {
        contenu()
        if (dort) {
            Box(
                Modifier.fillMaxSize().background(Color.Black)
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {
                        dort = false
                        dernier = SystemClock.elapsedRealtime()
                    },
                contentAlignment = Alignment.Center,
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Point(Color(0xFF7F1D1D), 10.dp)
                    T("MyCam diffuse", taille = 14.sp, poids = FontWeight.Bold, couleur = Color(0xFF5B6275))
                    T("Touchez pour rallumer l’écran", taille = 12.sp, couleur = Color(0xFF4A5063))
                }
            }
        }
    }
}

/** Message de la régie au caméraman, avec « Vu ». */
@Composable
private fun CarteMessage(remote: Remote, m: JSONObject, compacte: Boolean = false) {
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(if (compacte) Color(0xD10B0F1A) else C.CamSurface)
            .border(1.dp, C.CamBord, RoundedCornerShape(16.dp)).padding(start = 12.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (!compacte) Box(Modifier.size(36.dp).clip(RoundedCornerShape(12.dp)).background(Color(0x29E3B762)), contentAlignment = Alignment.Center) {
            Ico(I.bulle, C.OrClair, 20.dp)
        }
        Column(Modifier.weight(1f)) {
            T("Message de la régie · " + m.optString("heure"), taille = 11.5.sp, poids = FontWeight.Bold, couleur = C.CamBrume, lignes = 1)
            T(m.optString("texte"), taille = 14.sp, poids = FontWeight.Bold, couleur = C.CamTexte, lignes = 3)
        }
        Touche(onClick = { remote.vuMessage(m.optString("id")) }, fond = Color.Transparent, bord = C.OrClair, rayon = 12.dp) {
            T("Vu", couleur = C.OrClair, poids = FontWeight.ExtraBold)
        }
    }
}

/** Bouton rond de la rangée de commandes. */
@Composable
private fun Rond(icone: androidx.compose.ui.graphics.vector.ImageVector, libelle: String, on: Boolean = false, actif: Boolean = true, onClick: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(
            Modifier.size(56.dp).clip(CircleShape).background(if (on) C.OrClair else C.CamSurface2)
                .clickable(enabled = actif, role = Role.Button, onClickLabel = libelle, onClick = onClick),
            contentAlignment = Alignment.Center,
        ) { Ico(icone, if (on) C.SurOr else if (actif) C.CamTexte else C.CamBrume, 22.dp) }
        T(libelle, taille = 12.sp, poids = FontWeight.Bold, couleur = C.CamBrume2, lignes = 1)
    }
}

@Composable
fun MyCamOnglet(remote: Remote) {
    val context = LocalContext.current
    var reglages by rememberSaveable { mutableStateOf(false) }
    var permis by remember { mutableStateOf(MyCam.peutFilmer(context)) }
    val demande = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok -> permis = ok }
    if (reglages) {
        MyCamReglages(remote) { reglages = false }
        return
    }
    DisposableEffect(permis) {
        MyCam.ecran(permis)
        onDispose { MyCam.ecran(false) }
    }
    val diffuse by MyCam.diffuse.collectAsState()
    val ouverte by MyCam.ouverte.collectAsState()
    val erreur by MyCam.erreur.collectAsState()
    val zoom by MyCam.zoom.collectAsState()
    val zMin by MyCam.zoomMin.collectAsState()
    val zMax by MyCam.zoomMax.collectAsState()
    val avant by MyCam.avant.collectAsState()
    val torche by MyCam.torche.collectAsState()
    val expo by MyCam.expo.collectAsState()
    val eMin by MyCam.expoMin.collectAsState()
    val eMax by MyCam.expoMax.collectAsState()
    val fps by MyCam.fps.collectAsState()
    val taille by MyCam.taille.collectAsState()
    val batterie by MyCam.batterie.collectAsState()
    val messages by remote.messagesCam.collectAsState()
    val pc by remote.pcName.collectAsState()
    var grille by remember { mutableStateOf(remote.store.camGrille) }
    val t = temoinActuel(remote)

    Veille(actif = diffuse && remote.store.camAssombrir) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 16.dp, top = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    T("Caméra pour MyChurch · " + pc.ifBlank { "PC" }, taille = 12.sp, poids = FontWeight.Bold, couleur = C.CamBrume, lignes = 1)
                    Row {
                        T("My", taille = 28.sp, poids = FontWeight.ExtraBold, couleur = C.CamTexte)
                        T("Cam", taille = 28.sp, poids = FontWeight.ExtraBold, couleur = C.OrClair)
                    }
                }
                Touche(onClick = { reglages = true }, modifier = Modifier.size(44.dp), fond = C.CamSurface2, rayon = 22.dp, description = "Réglages MyCam", padding = PaddingValues(0.dp)) {
                    Ico(I.reglages, C.CamTexte, 20.dp)
                }
            }
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                BandeauTemoin(t)
                val anneau = t.anneau
                Box(
                    Modifier.fillMaxWidth().aspectRatio(16f / 9f)
                        .then(if (anneau != null) Modifier.border(3.dp, anneau, RoundedCornerShape(18.dp)) else Modifier)
                        .clip(RoundedCornerShape(18.dp))
                ) {
                    if (!permis) {
                        Column(Modifier.fillMaxSize().background(C.CamSurface), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterVertically)) {
                            Ico(I.cam, C.OrClair, 36.dp)
                            T("MyCam a besoin de la caméra", couleur = C.CamTexte, poids = FontWeight.Bold)
                            Touche(onClick = { demande.launch(Manifest.permission.CAMERA) }, fond = C.OrClair, rayon = 12.dp) {
                                T("Autoriser la caméra", couleur = C.SurOr, poids = FontWeight.ExtraBold)
                            }
                        }
                    } else {
                        Viseur(Modifier.fillMaxSize(), grille) {
                            Info(listOf(taille, if (diffuse) "$fps i/s" else "").filter { it.isNotBlank() }.joinToString(" · ").ifBlank { "MyCam" }, Modifier.align(Alignment.TopStart).padding(8.dp))
                            if (batterie >= 0) Info("$batterie %", Modifier.align(Alignment.TopEnd).padding(8.dp))
                            Info(if (remote.store.camUsb) "Câble USB" else "Wi-Fi", Modifier.align(Alignment.BottomStart).padding(8.dp))
                            Touche(
                                onClick = { MyCam.pleinEcran.value = true },
                                modifier = Modifier.align(Alignment.BottomEnd).padding(4.dp).size(44.dp),
                                fond = Color(0x990B0F1A), rayon = 12.dp, description = "Plein écran à l’horizontale", padding = PaddingValues(0.dp),
                            ) { Ico(I.agrandir, Color.White, 20.dp) }
                            if (!ouverte || erreur.isNotBlank()) {
                                Box(Modifier.fillMaxSize().background(Color(0xC70B0F1A)).padding(16.dp), contentAlignment = Alignment.Center) {
                                    T(erreur.ifBlank { "Ouverture de la caméra…" }, couleur = C.CamTexte, poids = FontWeight.Bold, centre = true)
                                }
                            } else if (!diffuse) {
                                Box(Modifier.align(Alignment.Center).clip(RoundedCornerShape(12.dp)).background(Color(0xB30B0F1A)).padding(horizontal = 14.dp, vertical = 8.dp)) {
                                    T("Cadrez, puis touchez Diffuser", couleur = Color.White, poids = FontWeight.Bold)
                                }
                            }
                        }
                    }
                }

                // zoom : 0,5× (grand-angle si le téléphone en a un), 1×, 2×, 3×
                val niveaux = buildList {
                    if (zMin <= 0.6f) add(0.5f)
                    add(1f)
                    if (zMax >= 2f) add(2f)
                    if (zMax >= 3f) add(3f)
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally)) {
                    niveaux.forEach { n ->
                        val on = abs(zoom - n) < 0.05f
                        Box(
                            Modifier.size(width = 56.dp, height = 44.dp).clip(RoundedCornerShape(22.dp)).background(if (on) C.OrClair else C.CamSurface2)
                                .selectable(selected = on, role = Role.RadioButton) { MyCam.reglerZoom(n) },
                            contentAlignment = Alignment.Center,
                        ) {
                            T(if (n < 1f) "0,5×" else "${n.toInt()}×", taille = 14.sp, poids = FontWeight.ExtraBold, couleur = if (on) C.SurOr else C.CamTexte)
                        }
                    }
                }

                if (remote.store.camMessages) messages.forEach { m -> CarteMessage(remote, m) }

                Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.Top) {
                    Rond(I.retourner, if (avant) "Avant" else "Arrière", actif = permis) { MyCam.basculerCamera() }
                    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Box(
                            Modifier.size(78.dp).clip(CircleShape).border(3.dp, C.CamTexte, CircleShape)
                                .clickable(enabled = permis, role = Role.Button, onClickLabel = if (diffuse) "Arrêter la diffusion" else "Diffuser vers le PC") {
                                    if (diffuse) MyCam.arreterDiffusion() else MyCam.demarrerDiffusion()
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            if (diffuse) Box(Modifier.size(28.dp).clip(RoundedCornerShape(7.dp)).background(C.RougeVif))
                            else Box(Modifier.size(58.dp).clip(CircleShape).background(C.RougeVif))
                        }
                        T(if (diffuse) "Arrêter" else "Diffuser", taille = 12.sp, poids = FontWeight.ExtraBold, couleur = C.CamTexte)
                    }
                    Rond(I.eclair, "Lampe", on = torche, actif = permis && !avant) { MyCam.basculerTorche() }
                }

                Column(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.CamSurface).border(1.dp, C.CamBord, RoundedCornerShape(16.dp))
                        .padding(horizontal = 12.dp, vertical = 10.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Ico(I.soleil, C.CamBrume2, 18.dp)
                        T("Exposition", taille = 13.5.sp, poids = FontWeight.Bold, couleur = C.CamTexte, modifier = Modifier.weight(1f))
                        T(if (expo > 0) "+$expo" else if (expo < 0) "−${-expo}" else "0", taille = 14.sp, poids = FontWeight.ExtraBold, couleur = C.OrClair)
                        Touche(onClick = { MyCam.reglerExpo(expo - 1) }, modifier = Modifier.size(44.dp), fond = C.CamSurface2, rayon = 12.dp, actif = eMin < expo, description = "Moins clair", padding = PaddingValues(0.dp)) {
                            Ico(I.moins, C.CamTexte, 18.dp)
                        }
                        Touche(onClick = { MyCam.reglerExpo(expo + 1) }, modifier = Modifier.size(44.dp), fond = C.CamSurface2, rayon = 12.dp, actif = expo < eMax, description = "Plus clair", padding = PaddingValues(0.dp)) {
                            Ico(I.plus, C.CamTexte, 18.dp)
                        }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Touche(
                            onClick = { grille = !grille; remote.store.camGrille = grille },
                            modifier = Modifier.weight(1f), fond = if (grille) Color(0x29E3B762) else C.CamSurface2, bord = if (grille) C.OrClair else C.CamBord, epaisseur = 1.dp, rayon = 12.dp,
                        ) {
                            Ico(I.grille, if (grille) C.OrClair else C.CamTexte, 18.dp)
                            Spacer(Modifier.width(8.dp))
                            T("Grille", couleur = if (grille) C.OrClair else C.CamTexte, poids = FontWeight.ExtraBold)
                        }
                        Touche(
                            onClick = { MyCam.pleinEcran.value = true },
                            modifier = Modifier.weight(1f), fond = C.CamSurface2, bord = C.CamBord, epaisseur = 1.dp, rayon = 12.dp, actif = permis,
                        ) {
                            Ico(I.agrandir, C.CamTexte, 18.dp)
                            Spacer(Modifier.width(8.dp))
                            T("Plein écran", couleur = C.CamTexte, poids = FontWeight.ExtraBold)
                        }
                    }
                }
                T(
                    "Sur le PC : Display › Ajouter › MyCam. Gardez l’app ouverte pendant le culte ; l’écran s’assombrit tout seul.",
                    taille = 12.sp, couleur = C.CamBrume, interligne = 17.sp,
                )
            }
        }
    }
}

/** Tournage à l'horizontale : viseur plein écran, témoin tout autour, commandes sur les côtés. */
@Composable
fun MyCamPleinEcran(remote: Remote) {
    val activite = LocalContext.current as? Activity
    val vue = LocalView.current
    DisposableEffect(Unit) {
        activite?.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        val controle = activite?.window?.let { WindowCompat.getInsetsController(it, vue) }
        controle?.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        controle?.hide(WindowInsetsCompat.Type.systemBars())
        MyCam.ecran(true)
        onDispose {
            controle?.show(WindowInsetsCompat.Type.systemBars())
            activite?.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
            MyCam.ecran(false)
        }
    }
    BackHandler { MyCam.pleinEcran.value = false }
    val diffuse by MyCam.diffuse.collectAsState()
    val zoom by MyCam.zoom.collectAsState()
    val zMin by MyCam.zoomMin.collectAsState()
    val zMax by MyCam.zoomMax.collectAsState()
    val torche by MyCam.torche.collectAsState()
    val avant by MyCam.avant.collectAsState()
    val expo by MyCam.expo.collectAsState()
    val eMin by MyCam.expoMin.collectAsState()
    val eMax by MyCam.expoMax.collectAsState()
    val fps by MyCam.fps.collectAsState()
    val taille by MyCam.taille.collectAsState()
    val batterie by MyCam.batterie.collectAsState()
    val temperature by MyCam.temperature.collectAsState()
    val messages by remote.messagesCam.collectAsState()
    val t = temoinActuel(remote)

    Veille(actif = diffuse && remote.store.camAssombrir) {
        Row(Modifier.fillMaxSize().background(Color.Black).windowInsetsPadding(WindowInsets.safeDrawing)) {
            Column(
                Modifier.width(80.dp).fillMaxHeight(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(14.dp, Alignment.CenterVertically),
            ) {
                T(if (t.anneau == C.RougeVif) "DIRECT" else if (diffuse) "EN LIGNE" else "ARRÊT", taille = 10.5.sp, poids = FontWeight.ExtraBold, couleur = if (t.anneau == C.RougeVif) C.RougeClair else C.CamBrume, espacement = 0.8.sp)
                if (t.anneau == C.RougeVif) T(t.droite, taille = 14.sp, poids = FontWeight.ExtraBold, couleur = Color.White)
                T(taille.ifBlank { "—" }, taille = 11.5.sp, poids = FontWeight.Bold, couleur = C.CamBrume2, centre = true)
                if (diffuse) T("$fps i/s", taille = 11.5.sp, poids = FontWeight.Bold, couleur = C.CamBrume2)
                T(if (remote.store.camUsb) "USB" else "Wi-Fi", taille = 11.5.sp, poids = FontWeight.Bold, couleur = C.CamBrume2)
                if (batterie >= 0) T("$batterie %", taille = 11.5.sp, poids = FontWeight.Bold, couleur = C.CamBrume2)
                if (temperature > 0) T(String.format(Locale.FRANCE, "%.0f °C", temperature), taille = 11.5.sp, poids = FontWeight.Bold, couleur = if (temperature >= 42) C.CamAmbreVif else C.CamBrume2)
            }
            Box(Modifier.weight(1f).fillMaxHeight(), contentAlignment = Alignment.Center) {
                Viseur(Modifier.fillMaxHeight().aspectRatio(16f / 9f, matchHeightConstraintsFirst = true), remote.store.camGrille) {
                    val anneau = t.anneau
                    if (anneau != null) Box(Modifier.fillMaxSize().border(5.dp, anneau))
                    Row(
                        Modifier.align(Alignment.TopCenter).padding(top = 12.dp).height(32.dp).clip(RoundedCornerShape(16.dp)).background(if (anneau != null) t.fond else Color(0xB30B0F1A)).padding(horizontal = 14.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Point(t.point, 8.dp)
                        T(t.titre + " · " + MyCam.nom(), taille = 13.sp, poids = FontWeight.ExtraBold, couleur = Color.White, lignes = 1)
                    }
                    val dernier = messages.lastOrNull()
                    if (dernier != null && remote.store.camMessages) {
                        Box(Modifier.align(Alignment.BottomCenter).padding(12.dp).fillMaxWidth(0.8f)) { CarteMessage(remote, dernier, compacte = true) }
                    }
                }
            }
            Column(
                Modifier.width(80.dp).fillMaxHeight(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(9.dp, Alignment.CenterVertically),
            ) {
                val niveaux = buildList {
                    if (zMin <= 0.6f) add(0.5f)
                    add(1f)
                    if (zMax >= 2f) add(2f)
                    if (zMax >= 3f) add(3f)
                }
                val i = niveaux.indexOfFirst { abs(zoom - it) < 0.05f }
                val suivant = niveaux[(i + 1).mod(niveaux.size)]
                Touche(
                    onClick = { MyCam.reglerZoom(suivant) }, modifier = Modifier.size(48.dp), fond = C.CamSurface2, rayon = 24.dp,
                    description = "Zoom", padding = PaddingValues(0.dp),
                ) { T(if (zoom < 0.95f) "0,5×" else String.format(Locale.FRANCE, "%.0f×", zoom), taille = 13.sp, poids = FontWeight.ExtraBold, couleur = C.CamTexte) }
                Touche(
                    onClick = { MyCam.reglerExpo(if (expo < eMax) expo + 1 else eMin) }, modifier = Modifier.size(48.dp), fond = C.CamSurface2, rayon = 24.dp,
                    description = "Exposition $expo", padding = PaddingValues(0.dp), actif = eMax > eMin,
                ) { Ico(I.soleil, C.CamTexte, 20.dp) }
                Touche(
                    onClick = { MyCam.basculerTorche() }, modifier = Modifier.size(48.dp), fond = if (torche) C.OrClair else C.CamSurface2, rayon = 24.dp,
                    description = "Lampe", padding = PaddingValues(0.dp), actif = !avant,
                ) { Ico(I.eclair, if (torche) C.SurOr else C.CamTexte, 20.dp) }
                Touche(onClick = { MyCam.basculerCamera() }, modifier = Modifier.size(48.dp), fond = C.CamSurface2, rayon = 24.dp, description = "Changer de caméra", padding = PaddingValues(0.dp)) {
                    Ico(I.retourner, C.CamTexte, 20.dp)
                }
                Box(
                    Modifier.size(56.dp).clip(CircleShape).border(3.dp, C.CamTexte, CircleShape)
                        .clickable(role = Role.Button, onClickLabel = if (diffuse) "Arrêter la diffusion" else "Diffuser") {
                            if (diffuse) MyCam.arreterDiffusion() else MyCam.demarrerDiffusion()
                        },
                    contentAlignment = Alignment.Center,
                ) {
                    if (diffuse) Box(Modifier.size(20.dp).clip(RoundedCornerShape(5.dp)).background(C.RougeVif))
                    else Box(Modifier.size(42.dp).clip(CircleShape).background(C.RougeVif))
                }
                Touche(onClick = { MyCam.pleinEcran.value = false }, modifier = Modifier.size(44.dp), fond = C.CamSurface2, rayon = 22.dp, description = "Quitter le plein écran", padding = PaddingValues(0.dp)) {
                    Ico(I.reduire, C.CamTexte, 20.dp)
                }
            }
        }
    }
}

/** Segments (720p / 1080p, 15 / 24 / 30 i/s). */
@Composable
private fun Segments(options: List<String>, choisi: Int, choisir: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(C.CamFond).padding(3.dp), horizontalArrangement = Arrangement.spacedBy(3.dp)) {
        options.forEachIndexed { i, o ->
            val on = i == choisi
            Box(
                Modifier.weight(1f).height(44.dp).clip(RoundedCornerShape(10.dp)).background(if (on) C.OrClair else Color.Transparent)
                    .selectable(selected = on, role = Role.RadioButton) { choisir(i) },
                contentAlignment = Alignment.Center,
            ) { T(o, taille = 14.sp, poids = FontWeight.ExtraBold, couleur = if (on) C.SurOr else C.CamTexte, lignes = 1) }
        }
    }
}

@Composable
private fun TitreSection(texte: String) = Surtitre(texte, C.CamBrume, Modifier.padding(start = 4.dp, top = 6.dp))

/** Réglages MyCam : nom de la source, image, connexion, comportement pendant le culte. */
@Composable
private fun MyCamReglages(remote: Remote, fermer: () -> Unit) {
    val store = remote.store
    val context = LocalContext.current
    val focus = LocalFocusManager.current
    var nom by remember { mutableStateOf(store.camNom) }
    var resolution by remember { mutableStateOf(store.camResolution) }
    var fps by remember { mutableStateOf(store.camFps) }
    var usb by remember { mutableStateOf(store.camUsb) }
    var assombrir by remember { mutableStateOf(store.camAssombrir) }
    var vibrer by remember { mutableStateOf(store.camVibrer) }
    var messages by remember { mutableStateOf(store.camMessages) }
    var grille by remember { mutableStateOf(store.camGrille) }
    BackHandler { fermer() }
    fun enregistrerNom() {
        store.camNom = nom
        MyCam.reconnecte()
        focus.clearFocus()
    }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 12.dp, end = 16.dp, top = 12.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Touche(onClick = { enregistrerNom(); fermer() }, modifier = Modifier.size(44.dp), fond = C.CamSurface2, rayon = 22.dp, description = "Retour à MyCam", padding = PaddingValues(0.dp)) {
                Ico(I.gauche, C.CamTexte, 20.dp)
            }
            Column {
                T("Réglages MyCam", taille = 21.sp, poids = FontWeight.ExtraBold, couleur = C.CamTexte)
                T(android.os.Build.MANUFACTURER.replaceFirstChar { it.uppercase() } + " " + android.os.Build.MODEL, taille = 12.5.sp, couleur = C.CamBrume, lignes = 1)
            }
        }
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            TitreSection("Nom dans MyChurch")
            BasicTextField(
                value = nom,
                onValueChange = { nom = it.take(40) },
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).clip(RoundedCornerShape(14.dp)).background(C.CamSurface)
                    .border(1.dp, C.CamBord2, RoundedCornerShape(14.dp)).padding(horizontal = 14.dp, vertical = 13.dp)
                    .semantics { contentDescription = "Nom de la source dans MyChurch" },
                singleLine = true,
                textStyle = TextStyle(fontFamily = Manrope, fontSize = 15.5.sp, fontWeight = FontWeight.Bold, color = C.CamTexte),
                cursorBrush = SolidColor(C.OrClair),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { enregistrerNom() }),
                decorationBox = { champ ->
                    Box {
                        if (nom.isEmpty()) T(MyCam.nom(), taille = 15.5.sp, couleur = C.CamBrume, lignes = 1)
                        champ()
                    }
                },
            )
            T("C’est le nom de la source dans Display et dans le déroulé (ex. « Caméra · Chaire »).", taille = 12.5.sp, couleur = C.CamBrume, interligne = 17.sp)

            TitreSection("Image")
            Column(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.CamSurface).border(1.dp, C.CamBord, RoundedCornerShape(16.dp)).padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                T("Résolution", taille = 13.5.sp, poids = FontWeight.Bold, couleur = C.CamTexte)
                Segments(listOf("720p", "1080p"), if (resolution >= 1080) 1 else 0) { i ->
                    resolution = if (i == 1) 1080 else 720
                    store.camResolution = resolution
                    MyCam.reconfigurer()
                }
                T("Images par seconde", taille = 13.5.sp, poids = FontWeight.Bold, couleur = C.CamTexte)
                val cadences = listOf(15, 24, 30)
                Segments(cadences.map { "$it" }, cadences.indexOf(fps).coerceAtLeast(0)) { i ->
                    fps = cadences[i]
                    store.camFps = fps
                    MyCam.reconfigurer()
                }
                T("720p à 30 i/s : le bon choix sur le Wi-Fi de l’église. 1080p demande un Wi-Fi 5 GHz ou le câble.", taille = 12.sp, couleur = C.CamBrume, interligne = 17.sp)
            }

            TitreSection("Connexion")
            listOf(false, true).forEach { estUsb ->
                val on = usb == estUsb
                Row(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(if (on) Color(0x1FE3B762) else C.CamSurface)
                        .border(1.5.dp, if (on) C.OrClair else C.CamBord, RoundedCornerShape(16.dp))
                        .selectable(selected = on, role = Role.RadioButton) {
                            usb = estUsb
                            remote.choisirUsb(estUsb)
                        }
                        .padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(C.CamSurface2), contentAlignment = Alignment.Center) {
                        Ico(if (estUsb) I.usb else I.wifi, C.OrClair, 20.dp)
                    }
                    Column(Modifier.weight(1f)) {
                        T(if (estUsb) "Câble USB" else "Wi-Fi", taille = 14.5.sp, poids = FontWeight.ExtraBold, couleur = C.CamTexte)
                        T(
                            if (estUsb) "Plus stable, et le téléphone se recharge. Activez le partage de connexion USB." else "Même réseau que le PC, sans fil",
                            taille = 12.5.sp, couleur = C.CamBrume, interligne = 17.sp,
                        )
                    }
                    Box(Modifier.size(22.dp).clip(CircleShape).border(2.dp, if (on) C.OrClair else C.CamBrume, CircleShape), contentAlignment = Alignment.Center) {
                        if (on) Box(Modifier.size(10.dp).clip(CircleShape).background(C.OrClair))
                    }
                }
            }
            if (usb) {
                Touche(
                    onClick = {
                        val ouvert = try {
                            context.startActivity(Intent().setClassName("com.android.settings", "com.android.settings.TetherSettings").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                            true
                        } catch (e: Exception) {
                            false
                        }
                        if (!ouvert) try {
                            context.startActivity(Intent(Settings.ACTION_WIRELESS_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                        } catch (e: Exception) {
                            Bulles.dire("Ouvrez Paramètres › Point d’accès et partage de connexion.")
                        }
                    },
                    modifier = Modifier.fillMaxWidth(), fond = C.CamSurface2, bord = C.CamBord, epaisseur = 1.dp,
                ) {
                    Ico(I.usb, C.OrClair, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Ouvrir le partage de connexion", couleur = C.CamTexte, poids = FontWeight.ExtraBold)
                }
            }

            TitreSection("Pendant le culte")
            Column(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.CamSurface).border(1.dp, C.CamBord, RoundedCornerShape(16.dp)).padding(horizontal = 12.dp, vertical = 2.dp),
            ) {
                LigneReglage("Assombrir l’écran du téléphone", "Après 25 s sans toucher ; la caméra continue", assombrir, sombre = true) { assombrir = it; store.camAssombrir = it }
                LigneReglage("Vibrer quand je passe à l’écran", "Le témoin devient rouge", vibrer, sombre = true) { vibrer = it; store.camVibrer = it }
                LigneReglage("Messages de la régie", "Les consignes envoyées par le PC", messages, sombre = true) { messages = it; store.camMessages = it }
                LigneReglage("Grille des tiers", "Aide à cadrer", grille, sombre = true) { grille = it; store.camGrille = it }
            }
        }
    }
}
