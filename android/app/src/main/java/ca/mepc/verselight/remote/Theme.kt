package ca.mepc.verselight.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import java.util.Locale

/** Couleurs de MyChurch mobile : ivoire, bleu nuit et or (télécommande) ; sombre pour MyCam. */
object C {
    val Page = Color(0xFFFAF8F4)
    val Carte = Color(0xFFFFFFFF)
    val Trait = Color(0xFFECE5DA)
    val Trait2 = Color(0xFFD9D1C3)
    val Sable = Color(0xFFF3EFE7)
    val Passe = Color(0xFFF6F2EB)
    val Encre = Color(0xFF172036)
    val Encre2 = Color(0xFF3C465B)
    val Brume = Color(0xFF646C7E)
    val Inactif = Color(0xFF4A5468)
    val Or = Color(0xFF8A5A0B)
    val Or2 = Color(0xFF6E4708)
    val OrVif = Color(0xFFC8962E)
    val OrClair = Color(0xFFE3B762)
    val OrPale = Color(0xFFFFF6E3)
    val OrBord = Color(0xFFE2BE73)
    val Marine = Color(0xFF1B2742)
    val SurMarine = Color(0xFFC7CEDA)
    val Rouge = Color(0xFFD92D20)
    val RougeTexte = Color(0xFFB42318)
    val RougeBord = Color(0xFFF1C4BE)
    val RougeVif = Color(0xFFEF4444)
    val RougeClair = Color(0xFFFCA5A5)
    val Ambre = Color(0xFFB45309)
    val AmbreTexte = Color(0xFF9A4508)
    val AmbreAnneau = Color(0xFFE59A2F)
    val AmbrePale = Color(0xFFFFF1DC)
    val VertFond = Color(0xFFE8F5EC)
    val VertTexte = Color(0xFF166534)
    val VertPoint = Color(0xFF16A34A)

    // types du déroulé : texte, fond de l'icône
    val Chant = Color(0xFF1D4ED8) to Color(0xFFEAF1FE)
    val Lecture = Color(0xFF9A4508) to Color(0xFFFEF1DE)
    val Accueil = Color(0xFF475569) to Color(0xFFEEF1F5)
    val Duo = Color(0xFF0F766E) to Color(0xFFE1F4F1)
    val Principal = Color(0xFF8A5A0B) to Color(0xFFFFF1D2)
    val Texte = Color(0xFF6D28D9) to Color(0xFFF1EAFE)

    // MyCam (sombre)
    val CamFond = Color(0xFF0B0F1A)
    val CamSurface = Color(0xFF141A29)
    val CamSurface2 = Color(0xFF1C2436)
    val CamBord = Color(0xFF263049)
    val CamBord2 = Color(0xFF2B3654)
    val CamTexte = Color(0xFFF3F1EC)
    val CamBrume = Color(0xFFA3ABBD)
    val CamBrume2 = Color(0xFFC9CFDA)
    val CamBarre = Color(0xFF0F1422)
    val CamRouge = Color(0xFFDC2626)
    val CamAmbreVif = Color(0xFFF59E0B)
    val CamVert = Color(0xFF22C55E)
    val CamPiste = Color(0xFF3A4560)
    val SurOr = Color(0xFF1B1505)
}

@OptIn(ExperimentalTextApi::class)
private fun manrope(w: Int) = Font(R.font.manrope, FontWeight(w), variationSettings = FontVariation.Settings(FontVariation.weight(w)))

@OptIn(ExperimentalTextApi::class)
private fun cormorant(w: Int) = Font(R.font.cormorant, FontWeight(w), variationSettings = FontVariation.Settings(FontVariation.weight(w)))

/** Texte de l'app. */
val Manrope = FontFamily(manrope(400), manrope(500), manrope(600), manrope(700), manrope(800))

/** Texte projeté (aperçu de l'écran de l'église). */
val Cormorant = FontFamily(cormorant(500), cormorant(600), cormorant(700))

/** Texte de l'app (Manrope), coupé avec « … » au besoin. */
@Composable
fun T(
    texte: String,
    modifier: Modifier = Modifier,
    taille: TextUnit = 14.sp,
    poids: FontWeight = FontWeight.SemiBold,
    couleur: Color = C.Encre,
    lignes: Int = Int.MAX_VALUE,
    centre: Boolean = false,
    interligne: TextUnit = TextUnit.Unspecified,
    espacement: TextUnit = TextUnit.Unspecified,
    majuscules: Boolean = false,
    famille: FontFamily = Manrope,
) {
    Text(
        text = if (majuscules) texte.uppercase(Locale.FRANCE) else texte,
        modifier = modifier,
        color = couleur,
        fontSize = taille,
        fontWeight = poids,
        fontFamily = famille,
        letterSpacing = espacement,
        textAlign = if (centre) TextAlign.Center else TextAlign.Start,
        lineHeight = interligne,
        overflow = TextOverflow.Ellipsis,
        maxLines = lignes,
    )
}

/** Petite étiquette en capitales (« À L'ÉCRAN », « ENSUITE »). */
@Composable
fun Surtitre(texte: String, couleur: Color = C.Brume, modifier: Modifier = Modifier) =
    T(texte, modifier, taille = 11.sp, poids = FontWeight.ExtraBold, couleur = couleur, lignes = 1, espacement = 0.8.sp, majuscules = true)

/** Icônes au trait (dessinées ici : aucune dépendance d'icônes). */
object I {
    private fun c(cx: Float, cy: Float, r: Float) = "M${cx - r} ${cy}a$r $r 0 1 0 ${2 * r} 0a$r $r 0 1 0 ${-2 * r} 0"
    private fun r(x: Float, y: Float, w: Float, h: Float, rx: Float) =
        "M${x + rx} ${y}h${w - 2 * rx}a$rx $rx 0 0 1 $rx ${rx}v${h - 2 * rx}a$rx $rx 0 0 1 ${-rx} ${rx}h${-(w - 2 * rx)}a$rx $rx 0 0 1 ${-rx} ${-rx}v${-(h - 2 * rx)}a$rx $rx 0 0 1 $rx ${-rx}z"

    private fun v(vararg d: String, w: Float = 1.9f): ImageVector {
        val b = ImageVector.Builder(defaultWidth = 24.dp, defaultHeight = 24.dp, viewportWidth = 24f, viewportHeight = 24f)
        for (x in d) {
            b.addPath(
                pathData = addPathNodes(x),
                stroke = SolidColor(Color.Black),
                strokeLineWidth = w,
                strokeLineCap = StrokeCap.Round,
                strokeLineJoin = StrokeJoin.Round,
            )
        }
        return b.build()
    }

    val regie by lazy { v(r(2.5f, 3.5f, 19f, 13f, 2f), "M8 20.5h8M12 16.5v4", "M10 7.5l4.5 2.5-4.5 2.5z") }
    val bible by lazy {
        v(
            "M3 5.5A1.5 1.5 0 0 1 4.5 4H9a3 3 0 0 1 3 3v13a2.5 2.5 0 0 0-2.5-2.5H3z",
            "M21 5.5A1.5 1.5 0 0 0 19.5 4H15a3 3 0 0 0-3 3v13a2.5 2.5 0 0 1 2.5-2.5H21z",
        )
    }
    val chants by lazy { v("M9 18V5l11-2v13", c(6f, 18f, 3f), c(17f, 16f, 3f)) }
    val culte by lazy { v("M9 6.5h11M9 12h11M9 17.5h11", c(4.5f, 6.5f, 1.2f), c(4.5f, 12f, 1.2f), c(4.5f, 17.5f, 1.2f)) }
    val cam by lazy { v("M16 13l5 3.3V7.7L16 11", r(2.5f, 6f, 13.5f, 12f, 2f)) }
    val gauche by lazy { v("M14.5 6l-6 6 6 6", w = 2.4f) }
    val droite by lazy { v("M9.5 6l6 6-6 6", w = 2.4f) }
    val coche by lazy { v("M5 12.5l4.5 4.5L19 7.5", w = 2.6f) }
    val plus by lazy { v("M12 5v14M5 12h14", w = 2.2f) }
    val moins by lazy { v("M5 12h14", w = 2.2f) }
    val croix by lazy { v("M6 6l12 12M18 6L6 18", w = 2.2f) }
    val loupe by lazy { v(c(11f, 11f, 6.5f), "M20 20l-4.2-4.2", w = 2f) }
    val micro by lazy { v(r(9f, 3f, 6f, 11f, 3f), "M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21", w = 2f) }
    val ecran by lazy { v(r(3f, 4f, 18f, 12f, 2f), "M8 20h8M12 16v4") }
    val epingle by lazy { v("M9 3.5h6l-1 6.5 3.5 3.5h-11L10 10z", "M12 13.5V21", w = 2f) }
    val horloge by lazy { v(c(12f, 12f, 8.5f), "M12 7.5V12l3 2") }
    val duo by lazy { v(r(3f, 4.5f, 13f, 10f, 2f), r(9f, 10f, 12f, 9.5f, 2f)) }
    val reglages by lazy { v("M4 7h10M18 7h2M4 17h4M12 17h8", c(16f, 7f, 2f), c(10f, 17f, 2f), w = 2f) }
    val agrandir by lazy { v("M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5", w = 2f) }
    val reduire by lazy { v("M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5", w = 2f) }
    val retourner by lazy { v("M4 9.5A8 8 0 0 1 18.5 7M20 14.5A8 8 0 0 1 5.5 17", "M18.5 3v4h-4M5.5 21v-4h4", w = 2f) }
    val eclair by lazy { v("M13 2.5L5 13.5h6l-1 8 8-11h-6z", w = 2f) }
    val soleil by lazy { v(c(12f, 12f, 4f), "M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4", w = 2f) }
    val bulle by lazy { v("M5 5h14a1.5 1.5 0 0 1 1.5 1.5v9A1.5 1.5 0 0 1 19 17h-7l-4.5 3.5V17H5a1.5 1.5 0 0 1-1.5-1.5v-9A1.5 1.5 0 0 1 5 5z") }
    val wifi by lazy { v("M2.5 9a14 14 0 0 1 19 0M5.5 12.5a9.5 9.5 0 0 1 13 0M8.8 16a5 5 0 0 1 6.4 0", c(12f, 19.2f, 0.7f), w = 2f) }
    val usb by lazy { v("M8 9h8v4.5a4 4 0 0 1-8 0z", "M10 9V4.5M14 9V4.5M12 17.5V21", w = 2f) }
    val bouclier by lazy { v("M12 3L5 6v5.5c0 4.2 3 7.8 7 9 4-1.2 7-4.8 7-9V6z", w = 2f) }
    val scan by lazy { v("M4 8.5v-3A1.5 1.5 0 0 1 5.5 4h3M15.5 4h3A1.5 1.5 0 0 1 20 5.5v3M20 15.5v3a1.5 1.5 0 0 1-1.5 1.5h-3M8.5 20h-3A1.5 1.5 0 0 1 4 18.5v-3", "M4 12h16", w = 2f) }
    val listePlus by lazy { v("M4 6.5h11M4 12h11M4 17.5h7", "M18 14v7M14.5 17.5h7", w = 2f) }
    val noir by lazy { v(r(3f, 4.5f, 18f, 12f, 2f), "M8 20h8", "M7 8.5l10 4M7 12.5l6 2.4", w = 2f) }
    val telecharger by lazy { v("M12 4v11M7.5 10.5L12 15l4.5-4.5M5 19.5h14", w = 2f) }
    val journal by lazy { v(r(5f, 3f, 14f, 18f, 2f), "M9 8h6M9 12h6M9 16h4") }
    val sortir by lazy { v("M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3", "M10 17l5-5-5-5", "M15 12H3", w = 2f) }
    val grille by lazy { v("M9 3v18M15 3v18M3 9h18M3 15h18", w = 1.7f) }
    val actualiser by lazy { v("M20 11a8 8 0 0 0-14.5-4.5L4 8M4 4v4h4M4 13a8 8 0 0 0 14.5 4.5L20 16M20 20v-4h-4", w = 2f) }
    val image by lazy { v(r(3f, 4.5f, 18f, 15f, 2f), c(9f, 10f, 2f), "M21 16l-5-5-9 8.5") }
    val texte by lazy { v("M5 6.5V5h14v1.5M12 5v14M9 19h6", w = 2f) }
    val fenetre by lazy { v(r(3f, 4.5f, 18f, 15f, 2f), "M3 9h18") }
    val volume by lazy { v("M4 9.5h3.5L12 5.5v13l-4.5-4H4z", "M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11", w = 2f) }
    val oreille by lazy { v("M7 9a5 5 0 0 1 10 0c0 3-3 4-3 7a2.5 2.5 0 0 1-5 0", "M10 9.5a2 2 0 0 1 4 0", w = 2f) }
    val vibrer by lazy { v(r(8f, 4f, 8f, 16f, 1.5f), "M4 9v6M20 9v6", w = 2f) }
    val lune by lazy { v("M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z", w = 2f) }
}

/** Icône au trait, de la couleur donnée. Décorative sans [description]. */
@Composable
fun Ico(v: ImageVector, couleur: Color, taille: Dp = 22.dp, modifier: Modifier = Modifier, description: String? = null) =
    Icon(v, contentDescription = description, tint = couleur, modifier = modifier.size(taille))

/** Carte blanche à bord fin. */
@Composable
fun Carte(
    modifier: Modifier = Modifier,
    fond: Color = C.Carte,
    bord: Color = C.Trait,
    rayon: Dp = 20.dp,
    padding: PaddingValues = PaddingValues(12.dp),
    espace: Dp = 10.dp,
    contenu: @Composable ColumnScope.() -> Unit,
) {
    val forme = RoundedCornerShape(rayon)
    Column(
        modifier.clip(forme).background(fond, forme).border(1.dp, bord, forme).padding(padding),
        verticalArrangement = Arrangement.spacedBy(espace),
        content = contenu,
    )
}

/** Bouton sur mesure (cible tactile d'au moins 44 dp, rôle « bouton » pour TalkBack). */
@Composable
fun Touche(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    fond: Color = C.Carte,
    bord: Color? = null,
    epaisseur: Dp = 1.5.dp,
    rayon: Dp = 14.dp,
    actif: Boolean = true,
    description: String? = null,
    padding: PaddingValues = PaddingValues(horizontal = 14.dp),
    alignement: Arrangement.Horizontal = Arrangement.Center,
    contenu: @Composable RowScope.() -> Unit,
) {
    val forme = RoundedCornerShape(rayon)
    var m = modifier.heightIn(min = 44.dp).clip(forme).background(if (actif) fond else fond.copy(alpha = 0.5f), forme)
    if (bord != null) m = m.border(epaisseur, bord, forme)
    m = m.clickable(enabled = actif, role = Role.Button, onClick = onClick)
    if (description != null) m = m.semantics { contentDescription = description }
    Row(m.padding(padding), horizontalArrangement = alignement, verticalAlignment = Alignment.CenterVertically, content = contenu)
}

/** Point de couleur (état, direct). */
@Composable
fun Point(couleur: Color, taille: Dp = 8.dp, anneau: Color? = null) {
    val m = Modifier.size(taille).clip(CircleShape).background(couleur, CircleShape)
    Box(if (anneau != null) m.border(2.dp, anneau, CircleShape) else m)
}

/** Interrupteur maison (piste or ou ardoise), pour une ligne de réglage entière cliquable. */
@Composable
fun Piste(on: Boolean, sombre: Boolean = false) {
    val piste = if (on) C.OrVif else if (sombre) C.CamPiste else C.Trait2
    Box(Modifier.size(width = 48.dp, height = 30.dp).clip(RoundedCornerShape(15.dp)).background(piste)) {
        Box(
            Modifier.padding(3.dp).offset(x = if (on) 18.dp else 0.dp).size(24.dp)
                .shadow(2.dp, CircleShape).clip(CircleShape).background(Color.White)
        )
    }
}

/** Ligne de réglage : titre, détail, interrupteur. Toute la ligne bascule. */
@Composable
fun LigneReglage(titre: String, detail: String, on: Boolean, sombre: Boolean = false, onChange: (Boolean) -> Unit) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable(role = Role.Switch, onClickLabel = titre) { onChange(!on) }
            .semantics { contentDescription = "$titre : " + if (on) "activé" else "désactivé" }
            .padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Column(Modifier.weight(1f)) {
            T(titre, taille = 14.5.sp, poids = FontWeight.Bold, couleur = if (sombre) C.CamTexte else C.Encre)
            if (detail.isNotEmpty()) T(detail, taille = 12.5.sp, couleur = if (sombre) C.CamBrume else C.Encre2)
        }
        Piste(on, sombre)
    }
}

/** Messages brefs en bas de l'écran (« Jean 3:16 à l'écran »). */
object Bulles {
    val texte = MutableStateFlow<String?>(null)
    private var n = 0
    val numero = MutableStateFlow(0)
    fun dire(s: String) {
        texte.value = s
        numero.value = ++n
    }
}

@Composable
fun BulleHote(modifier: Modifier = Modifier, sombre: Boolean = false) {
    val t by Bulles.texte.collectAsState()
    val n by Bulles.numero.collectAsState()
    LaunchedEffect(n) {
        if (Bulles.texte.value != null) {
            delay(3200)
            Bulles.texte.value = null
        }
    }
    val s = t ?: return
    Row(
        modifier.fillMaxWidth().padding(horizontal = 16.dp)
            .shadow(10.dp, RoundedCornerShape(14.dp))
            .clip(RoundedCornerShape(14.dp))
            .background(if (sombre) C.CamTexte else C.Encre)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Ico(I.coche, if (sombre) C.Or else C.OrClair, 18.dp)
        T(s, taille = 13.5.sp, poids = FontWeight.Bold, couleur = if (sombre) C.Encre else Color.White, lignes = 3)
    }
}
