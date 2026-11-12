package ca.mepc.verselight.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONObject

/** Ce que montre l'écran de l'église, tiré de l'état envoyé par le PC. */
data class VueEcran(
    val mode: String,            // vide, verset, chant, media, fond, noir
    val lignes: List<String>,
    val ref: String,
    val titre: String,
    val theme: String,
    val media: String,           // camera, mycam, image, fenetre, video
) {
    companion object {
        val VIDE = VueEcran("vide", emptyList(), "", "", "nuit", "")

        private fun lignes(s: JSONObject): List<String> {
            val a = s.optJSONArray("lignes") ?: return emptyList()
            return (0 until a.length()).flatMap { a.optString(it).split('\n') }.map { it.trim() }.filter { it.isNotEmpty() }
        }

        fun de(s: JSONObject?): VueEcran {
            if (s == null) return VIDE
            // « ecran » (PC 4.0) : le vrai mode, « fond » compris ; « mode » : celui des anciennes apps
            val mode = if (s.optBoolean("noir")) "noir" else s.optString("ecran").ifBlank { s.optString("mode", "vide") }.ifBlank { "vide" }
            return VueEcran(mode, lignes(s), s.optString("ref"), s.optString("titre"), s.optString("theme", "nuit").ifBlank { "nuit" }, s.optString("mediaType"))
        }

        /** La diapositive qui viendra avec Suivant (versets et chants). */
        fun suivante(s: JSONObject?): VueEcran? {
            if (s == null) return null
            val texte = s.optString("suivantTexte")
            if (texte.isBlank()) return null
            val verset = s.optString("mode") == "verset" || s.optString("suivantType") == "verset"
            return VueEcran(
                if (verset) "verset" else "chant",
                texte.split('\n').map { it.trim() }.filter { it.isNotEmpty() },
                if (verset) s.optString("suivantLabel") else "",
                "", s.optString("theme", "nuit").ifBlank { "nuit" }, "",
            )
        }
    }
}

/** Titre court de ce qui est à l'écran. */
fun titreLive(s: JSONObject?, link: Link): String {
    if (link != Link.CONNECTED) return if (link == Link.CONNECTING) "Connexion au PC…" else "Hors ligne"
    if (s == null) return "Connecté"
    if (s.optBoolean("noir")) return "Écran noir"
    val premiere = s.optJSONArray("lignes")?.optString(0)?.lineSequence()?.firstOrNull()?.trim().orEmpty()
    return when (s.optString("ecran").ifBlank { s.optString("mode") }) {
        "verset" -> s.optString("ref").ifBlank { "Verset" }
        "chant" -> listOf(s.optString("titre"), premiere).filter { it.isNotBlank() }.joinToString(" · ").ifBlank { "Texte" }
        "media" -> s.optString("titre").ifBlank { "Display" }
        "fond" -> s.optJSONObject("defaut")?.optString("nom")?.takeIf { it.isNotBlank() } ?: "Fond seul"
        else -> "Écran vide"
    }
}

/** « Ensuite : … » */
fun ensuiteLive(s: JSONObject?): String {
    if (s == null) return "—"
    val label = s.optString("suivantLabel")
    val texte = s.optString("suivantTexte").lineSequence().firstOrNull()?.trim().orEmpty()
    return when {
        label.isNotBlank() && texte.isNotBlank() && s.optString("mode") == "chant" -> "$label · $texte"
        label.isNotBlank() -> label
        texte.isNotBlank() -> texte
        else -> "fin"
    }
}

private class Peau(val fond: Brush, val texte: Color, val or: Color, val ref: Color, val lueur: Color?)

private fun peau(theme: String): Peau = when (theme) {
    "aube" -> Peau(Brush.verticalGradient(listOf(Color(0xFF5A3A12), Color(0xFF0A0604))), Color(0xFFF7F1E6), Color(0xFFE3B762), Color(0xFFE8BE6A), Color(0x88FFC460))
    "lin" -> Peau(SolidColor(Color(0xFFF1EEE8)), Color(0xFF1B2742), Color(0xFFB7832A), Color(0xFF8A5A0B), Color(0x66FFDFA0))
    "ciel" -> Peau(Brush.verticalGradient(listOf(Color(0xFFEEF5FD), Color(0xFFDCE9F8))), Color(0xFF1B2742), Color(0xFF3B6FB6), Color(0xFF2F5A96), null)
    "noir" -> Peau(SolidColor(Color.Black), Color(0xFFF7F4EC), Color(0xFFE3B762), Color(0xFFE8BE6A), null)
    "vert" -> Peau(SolidColor(Color(0xFF00B140)), Color.White, Color.White, Color.White, null)
    "transparent" -> Peau(SolidColor(Color(0xFF2A2F3A)), Color.White, Color(0xFFE3B762), Color(0xFFE8BE6A), null)
    else -> Peau(Brush.verticalGradient(listOf(Color(0xFF14213B), Color(0xFF0B1427))), Color(0xFFF7F1E6), Color(0xFFE3B762), Color(0xFFE8BE6A), Color(0x99FFC460))
}

/**
 * Aperçu 16:9 de l'écran de l'église (habillage, texte en Cormorant, référence en or).
 * Tout est proportionnel à la largeur : l'aperçu reste juste en vignette comme en grand.
 */
@Composable
fun EcranApercu(vue: VueEcran, modifier: Modifier = Modifier, rayon: Dp = 12.dp, anneau: Color? = null, legende: Boolean = true) {
    val forme = RoundedCornerShape(rayon)
    var m = modifier.fillMaxWidth().aspectRatio(16f / 9f)
    if (anneau != null) m = m.border(2.dp, anneau, forme)
    BoxWithConstraints(m.clip(forme)) {
        val l = maxWidth
        val densite = LocalDensity.current
        fun sp(f: Float) = with(densite) { (l * f).toSp() }
        val p = peau(vue.theme)
        val fondScene = when (vue.mode) {
            "noir" -> SolidColor(Color.Black)
            "media" -> Brush.verticalGradient(listOf(Color(0xFF2A3346), Color(0xFF111827)))
            else -> p.fond
        }
        val lueur = if (vue.mode in listOf("verset", "chant", "fond", "vide")) p.lueur else null
        Box(
            Modifier.fillMaxSize().drawBehind {
                drawRect(fondScene)
                if (lueur != null) drawRect(
                    Brush.radialGradient(
                        listOf(lueur, Color.Transparent),
                        center = Offset(size.width * 0.9f, -size.height * 0.16f),
                        radius = size.width * 0.75f,
                    )
                )
                if (vue.mode == "vide") drawRect(Color(0x66000000))
            },
            contentAlignment = Alignment.Center,
        ) {
            when (vue.mode) {
                "verset", "chant" -> {
                    val n = vue.lignes.size.coerceAtLeast(1)
                    val longue = vue.lignes.maxOfOrNull { it.length }?.coerceAtLeast(1) ?: 1
                    val f = minOf(0.052f, 0.34f / (1.24f * n), 0.78f / (0.42f * longue))
                    Column(
                        Modifier.fillMaxSize().padding(horizontal = l * 0.08f, vertical = l * 0.04f),
                        verticalArrangement = Arrangement.spacedBy(l * 0.022f, Alignment.CenterVertically),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            vue.lignes.forEach { ligne ->
                                T(ligne, taille = sp(f), poids = FontWeight.SemiBold, couleur = p.texte, centre = true, famille = Cormorant, interligne = sp(f * 1.24f), lignes = 2)
                            }
                        }
                        if (vue.mode == "verset" && vue.ref.isNotBlank()) {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(l * 0.014f)) {
                                Box(Modifier.width(l * 0.08f).height(1.dp).background(p.or.copy(alpha = 0.7f)))
                                Box(Modifier.size(l * 0.012f).background(p.or, RoundedCornerShape(1.dp)))
                                Box(Modifier.width(l * 0.08f).height(1.dp).background(p.or.copy(alpha = 0.7f)))
                            }
                            T(vue.ref, taille = sp(0.016f), poids = FontWeight.Bold, couleur = p.ref, espacement = sp(0.005f), majuscules = true, lignes = 1)
                        }
                    }
                }
                "media" -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(l * 0.02f)) {
                    val icone = when (vue.media) {
                        "camera", "mycam" -> I.cam
                        "fenetre" -> I.fenetre
                        "video" -> I.regie
                        else -> I.image
                    }
                    Ico(icone, Color(0xFFE3B762), l * 0.11f)
                    if (vue.titre.isNotBlank()) T(vue.titre, taille = sp(0.036f), poids = FontWeight.Bold, couleur = Color.White, lignes = 1, centre = true)
                    val sorte = when (vue.media) {
                        "mycam" -> "MyCam · en direct"
                        "camera" -> "Caméra · en direct"
                        "fenetre" -> "Capture de fenêtre"
                        "video" -> "Vidéo"
                        else -> "Image"
                    }
                    T(sorte, taille = sp(0.024f), poids = FontWeight.Bold, couleur = Color(0xFFC7CEDA), lignes = 1, majuscules = true, espacement = sp(0.004f))
                }
                "noir" -> if (legende) T("Écran noir", taille = sp(0.03f), couleur = Color(0xFF8A93A6), poids = FontWeight.Bold) else {}
                "vide" -> if (legende) T("Écran vide", taille = sp(0.03f), couleur = p.texte.copy(alpha = 0.6f), poids = FontWeight.Bold) else {}
                else -> {}
            }
        }
    }
}

/**
 * Barre « À l'écran » (Bible, Chants, Culte) : ce qui passe, ce qui vient, Précédent et Suivant.
 */
@Composable
fun BarreLive(remote: Remote, modifier: Modifier = Modifier) {
    val s by remote.screen.collectAsState()
    val link by remote.link.collectAsState()
    val haptique = LocalHapticFeedback.current
    val pret = link == Link.CONNECTED
    val vue = VueEcran.de(s)
    val enDirect = pret && vue.mode !in listOf("vide", "noir")
    Row(
        modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, bottom = 10.dp)
            .shadow(12.dp, RoundedCornerShape(18.dp))
            .clip(RoundedCornerShape(18.dp))
            .background(C.Marine)
            .padding(8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(Modifier.width(68.dp)) {
            EcranApercu(vue, rayon = 7.dp, anneau = if (enDirect) C.RougeVif else null, legende = false)
        }
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                Point(if (enDirect) C.RougeVif else C.SurMarine, 6.dp)
                T(if (pret) "À l’écran" else "Régie", taille = 10.sp, poids = FontWeight.ExtraBold, couleur = if (enDirect) C.RougeClair else C.SurMarine, majuscules = true, espacement = 0.8.sp, lignes = 1)
            }
            T(titreLive(s, link), taille = 14.sp, poids = FontWeight.ExtraBold, couleur = Color.White, lignes = 1)
            T("Ensuite : " + ensuiteLive(s), taille = 11.5.sp, couleur = C.SurMarine, lignes = 1)
        }
        Touche(
            onClick = { haptique.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("prev") },
            modifier = Modifier.size(44.dp), fond = Color.White.copy(alpha = 0.1f), rayon = 12.dp, actif = pret,
            description = "Précédent", padding = androidx.compose.foundation.layout.PaddingValues(0.dp),
        ) { Ico(I.gauche, Color.White, 20.dp) }
        Touche(
            onClick = { haptique.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("next") },
            modifier = Modifier.size(width = 52.dp, height = 44.dp), fond = C.OrClair, rayon = 12.dp, actif = pret,
            description = "Suivant", padding = androidx.compose.foundation.layout.PaddingValues(0.dp),
        ) { Ico(I.droite, C.Marine, 20.dp) }
    }
}

/** Espace vertical. */
@Composable
fun Vide(h: Dp) = Spacer(Modifier.height(h))
