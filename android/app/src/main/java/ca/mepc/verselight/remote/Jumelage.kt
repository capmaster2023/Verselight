package ca.mepc.verselight.remote

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat

/** Croix et vagues : le logo MyChurch (au trait, teinté par l'appelant). */
val LogoMyChurch: ImageVector by lazy {
    val b = ImageVector.Builder(defaultWidth = 48.dp, defaultHeight = 48.dp, viewportWidth = 48f, viewportHeight = 48f)
    listOf("M24 6v20M17.5 12.5h13" to 3.2f, "M9 32c5-3.2 10-3.2 15 0s10 3.2 15 0M9 38.5c5-3.2 10-3.2 15 0s10 3.2 15 0" to 2.6f).forEach { (d, w) ->
        b.addPath(pathData = addPathNodes(d), stroke = SolidColor(Color.Black), strokeLineWidth = w, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round)
    }
    b.build()
}

/** Le logo dans sa tuile bleu nuit. */
@Composable
fun TuileLogo(taille: Dp = 64.dp) {
    Box(Modifier.size(taille).clip(RoundedCornerShape(taille * 0.31f)).background(C.Marine), contentAlignment = Alignment.Center) {
        Ico(LogoMyChurch, C.OrClair, taille * 0.62f)
    }
}

/** Premier lancement : jumelage avec le PC par code QR (une seule fois). */
@Composable
fun Jumelage(remote: Remote, enCours: Boolean) {
    val context = LocalContext.current
    val info by remote.info.collectAsState()
    var scan by remember { mutableStateOf(false) }
    var journal by remember { mutableStateOf(false) }
    if (journal) JournalDialog(remote) { journal = false }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok ->
        if (ok) scan = true else Bulles.dire("La caméra est nécessaire pour scanner le code.")
    }
    fun lancer() {
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) scan = true
        else permission.launch(Manifest.permission.CAMERA)
    }
    Box(Modifier.fillMaxSize().background(C.Page)) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).windowInsetsPadding(WindowInsets.safeDrawing)
                .padding(horizontal = 16.dp, vertical = 28.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp),
        ) {
            Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                TuileLogo()
                Row {
                    T("My", taille = 32.sp, poids = FontWeight.ExtraBold)
                    T("Church", taille = 32.sp, poids = FontWeight.ExtraBold, couleur = C.Or)
                }
                T("Télécommande et caméra pour le culte", taille = 14.5.sp, couleur = C.Encre2, centre = true)
            }

            Carte(rayon = 24.dp, padding = PaddingValues(18.dp), espace = 14.dp) {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    T("Jumeler avec le PC de l’église", taille = 18.sp, poids = FontWeight.ExtraBold)
                    T("Sur le PC : MyChurch › Téléphone › Jumeler… puis visez le code.", taille = 13.5.sp, couleur = C.Encre2, interligne = 19.sp)
                }
                Box(
                    Modifier.fillMaxWidth().height(230.dp).clip(RoundedCornerShape(18.dp)).background(Color(0xFF111827)),
                    contentAlignment = Alignment.Center,
                ) {
                    when {
                        scan -> QrScanner(
                            onCode = { code ->
                                scan = false
                                if (!remote.pair(code)) Bulles.dire("Ce code QR n’est pas un code MyChurch.")
                            },
                            modifier = Modifier.fillMaxSize(),
                        )
                        enCours -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            CircularProgressIndicator(color = C.OrClair)
                            T("Jumelage en cours…", couleur = Color.White, poids = FontWeight.Bold)
                        }
                        else -> Ico(I.scan, C.OrClair, 56.dp)
                    }
                    CoinsVisee(scan)
                }
                if (!enCours) {
                    Touche(
                        onClick = { if (scan) scan = false else lancer() },
                        modifier = Modifier.fillMaxWidth().height(52.dp),
                        fond = if (scan) C.Carte else C.Marine,
                        bord = if (scan) C.Trait2 else null,
                        rayon = 16.dp,
                    ) {
                        Ico(if (scan) I.croix else I.scan, if (scan) C.Encre else C.OrClair, 20.dp)
                        Spacer(Modifier.width(8.dp))
                        T(if (scan) "Annuler" else "Scanner le code QR", taille = 15.5.sp, poids = FontWeight.ExtraBold, couleur = if (scan) C.Encre else Color.White)
                    }
                }
                if (info.isNotEmpty()) T(info, taille = 13.5.sp, couleur = C.RougeTexte, interligne = 19.sp)
            }

            Column(Modifier.padding(horizontal = 4.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Note(I.coche, "Une seule fois : ensuite le téléphone se reconnecte tout seul.")
                Note(I.wifi, "Même Wi-Fi que le PC, ou câble USB pour MyCam.")
                Note(I.bouclier, "Le PC peut retirer un téléphone à tout moment.")
            }
            Touche(onClick = { journal = true }, modifier = Modifier.fillMaxWidth(), fond = Color.Transparent) {
                Ico(I.journal, C.Or, 18.dp)
                Spacer(Modifier.width(8.dp))
                T("Journal d’erreurs", couleur = C.Or, poids = FontWeight.Bold)
            }
        }
        BulleHote(Modifier.align(Alignment.BottomCenter).padding(bottom = 40.dp))
    }
}

@Composable
private fun Note(icone: ImageVector, texte: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Ico(icone, C.Or, 18.dp)
        T(texte, taille = 13.sp, couleur = C.Encre2, interligne = 19.sp, modifier = Modifier.weight(1f))
    }
}

/** Coins dorés du viseur (et la ligne de lecture pendant le scan). */
@Composable
private fun CoinsVisee(lecture: Boolean) {
    Canvas(Modifier.fillMaxSize()) {
        val w = 180.dp.toPx()
        val h = 176.dp.toPx()
        val l = 26.dp.toPx()
        val e = 3.dp.toPx()
        val x0 = (size.width - w) / 2
        val y0 = (size.height - h) / 2
        val or = Color(0xFFE3B762)
        fun coin(x: Float, y: Float, dx: Float, dy: Float) {
            drawLine(or, Offset(x, y), Offset(x + dx * l, y), e, StrokeCap.Round)
            drawLine(or, Offset(x, y), Offset(x, y + dy * l), e, StrokeCap.Round)
        }
        coin(x0, y0, 1f, 1f)
        coin(x0 + w, y0, -1f, 1f)
        coin(x0, y0 + h, 1f, -1f)
        coin(x0 + w, y0 + h, -1f, -1f)
        if (lecture) drawLine(or.copy(alpha = 0.85f), Offset(x0 + 10.dp.toPx(), size.height / 2), Offset(x0 + w - 10.dp.toPx(), size.height / 2), 2.dp.toPx(), StrokeCap.Round)
    }
}
