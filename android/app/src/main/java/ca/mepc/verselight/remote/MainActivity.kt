package ca.mepc.verselight.remote

import android.os.Bundle
import android.view.KeyEvent
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.graphics.Color

/**
 * MyChurch mobile : télécommande de la régie (Régie, Bible, Chants, Culte) et MyCam (le téléphone devient une caméra du PC).
 */
class MainActivity : ComponentActivity() {
    private lateinit var remote: Remote

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.light(android.graphics.Color.TRANSPARENT, android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.light(android.graphics.Color.TRANSPARENT, android.graphics.Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        remote = MyChurch.remote(applicationContext)
        Ecoute.canaux(applicationContext)
        MyCam.attacher(this, remote)
        AppEtat.garderAllume.value = remote.store.keepScreenOn
        setContent {
            val allume by AppEtat.garderAllume.collectAsState()
            val diffuse by MyCam.diffuse.collectAsState()
            LaunchedEffect(allume, diffuse) {
                if (allume || diffuse) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
            MaterialTheme(
                colorScheme = lightColorScheme(
                    primary = C.Marine, onPrimary = Color.White, secondary = C.Or, onSecondary = Color.White,
                    background = C.Page, onBackground = C.Encre, surface = C.Carte, onSurface = C.Encre,
                    surfaceVariant = C.Sable, onSurfaceVariant = C.Encre2, outline = C.Trait2, error = C.RougeTexte,
                )
            ) {
                AppRacine(remote)
            }
        }
    }

    override fun onStart() {
        super.onStart()
        Detection.premierPlan = true
        remote.start()
        remote.checkUpdate()
    }

    override fun onStop() {
        super.onStop()
        Detection.premierPlan = false
        // détecteur de versets ou MyCam en cours : on reste connecté
        if (!isChangingConfigurations && !Detection.actif.value && !MyCam.diffuse.value) remote.stop()
    }

    override fun onDestroy() {
        MyCam.detacher(this)
        super.onDestroy()
    }

    /** Boutons de volume : + = Suivant, − = Précédent (pas dans MyCam, où l'on filme). */
    private fun volumeActif(): Boolean =
        remote.store.volumeKeys && remote.pret() && AppEtat.onglet.value != 4 && !MyCam.pleinEcran.value

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeActif()) {
            when (keyCode) {
                KeyEvent.KEYCODE_VOLUME_UP -> {
                    if (event.repeatCount == 0) remote.send("next")
                    return true
                }
                KeyEvent.KEYCODE_VOLUME_DOWN -> {
                    if (event.repeatCount == 0) remote.send("prev")
                    return true
                }
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeActif() && (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN)) return true
        return super.onKeyUp(keyCode, event)
    }
}
