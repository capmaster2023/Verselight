package ca.mepc.verselight.remote

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.AudioManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.flow.MutableStateFlow

/** Une seule connexion au PC pour toute l'app (écran, détecteur de versets, notifications). */
object MyVerse {
    @Volatile private var r: Remote? = null
    fun remote(ctx: Context): Remote = r ?: synchronized(this) {
        r ?: Remote(ctx.applicationContext, Store(ctx.applicationContext)).also { r = it }
    }
}

/** État du détecteur de versets, partagé entre le service d'écoute et l'écran de l'app. */
object Detection {
    data class Suggestion(val id: Int, val label: String, val texte: String, val quand: Long)

    val actif = MutableStateFlow(false)
    val statut = MutableStateFlow("")
    val entendu = MutableStateFlow("")
    val suggestions = MutableStateFlow<List<Suggestion>>(emptyList())
    @Volatile var premierPlan = false
    private var n = 0

    @Synchronized
    fun ajouter(label: String, texte: String): Suggestion {
        val s = Suggestion(++n, label, texte, System.currentTimeMillis())
        suggestions.value = (listOf(s) + suggestions.value).take(6)
        return s
    }

    fun retirer(id: Int) { suggestions.value = suggestions.value.filter { it.id != id } }
}

/**
 * Détecteur de versets : écoute le prédicateur (reconnaissance vocale du téléphone, en français) et propose
 * les références qu'il cite. App ouverte : la suggestion apparaît dans l'app ; sinon : notification « Afficher ».
 */
class Ecoute : Service() {
    private var sr: SpeechRecognizer? = null
    private val main = Handler(Looper.getMainLooper())
    @Volatile private var actif = false
    private var erreurs = 0
    private var muetParNous = false
    private val vus = HashMap<String, Long>()
    private var contexte: Ref? = null
    private var contexteQuand = 0L
    @Volatile private var bible: BibleTexte? = null
    @Volatile private var noms: List<Pair<List<String>, Int>> = emptyList() // noms des livres dans les Bibles importées (kirundi…)
    private val relance = Runnable { if (actif) ecouter() }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        canaux(this)
        val app = applicationContext
        Thread {
            try {
                val store = MyVerse.remote(app).store
                bible = Donnees.principale(app, store)
                noms = Detecteur.nomsDepuis(Donnees.pourRecherche(app, store))
            } catch (e: Exception) {
            }
        }.start()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_ARRET) {
            stopSelf()
            return START_NOT_STICKY
        }
        try {
            ServiceCompat.startForeground(
                this, NOTIF_ECOUTE, notifEcoute(),
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE else 0,
            )
        } catch (e: Exception) {
            Detection.statut.value = "Écoute impossible : ${e.message ?: e.javaClass.simpleName}"
            stopSelf()
            return START_NOT_STICKY
        }
        if (!SpeechRecognizer.isRecognitionAvailable(this)) {
            Detection.statut.value = "Pas de reconnaissance vocale sur ce téléphone (installez ou activez l'application Google)."
            stopSelf()
            return START_NOT_STICKY
        }
        if (!actif) {
            actif = true
            Detection.actif.value = true
            Detection.statut.value = "Démarrage de l'écoute…"
            MyVerse.remote(this).start() // la connexion reste ouverte pour pouvoir afficher depuis la notification
            ecouter()
        }
        // pas de redémarrage automatique en arrière-plan (Android 14 l'interdit pour le micro) : on relance depuis l'app
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        actif = false
        main.removeCallbacks(relance)
        try { sr?.destroy() } catch (e: Exception) {}
        sr = null
        bip(true)
        Detection.actif.value = false
        Detection.statut.value = ""
        Detection.entendu.value = ""
        if (!Detection.premierPlan) MyVerse.remote(this).stop()
        super.onDestroy()
    }

    // ---------------------------------------------------------------- reconnaissance vocale en continu

    private fun ecouter() {
        if (!actif) return
        val r = sr ?: try {
            SpeechRecognizer.createSpeechRecognizer(this).also { it.setRecognitionListener(ecouteur); sr = it }
        } catch (e: Exception) {
            Detection.statut.value = "Reconnaissance vocale indisponible : ${e.message}"
            relancer(5000)
            return
        }
        val i = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE, "fr-FR")
            .putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
            .putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 3)
            .putExtra(RecognizerIntent.EXTRA_CALLING_PACKAGE, packageName)
            .putExtra("android.speech.extra.DICTATION_MODE", true)
        bip(false)
        try {
            r.startListening(i)
        } catch (e: Exception) {
            bip(true)
            relancer(1000)
        }
    }

    private fun relancer(ms: Long) {
        main.removeCallbacks(relance)
        if (actif) main.postDelayed(relance, ms)
    }

    private val ecouteur = object : RecognitionListener {
        override fun onReadyForSpeech(params: Bundle?) {
            erreurs = 0
            main.postDelayed({ bip(true) }, 400)
            Detection.statut.value = "À l'écoute du prédicateur…"
        }
        override fun onBeginningOfSpeech() {}
        override fun onRmsChanged(rmsdB: Float) {}
        override fun onBufferReceived(buffer: ByteArray?) {}
        override fun onEndOfSpeech() {}
        override fun onEvent(eventType: Int, params: Bundle?) {}
        override fun onPartialResults(partialResults: Bundle?) { analyser(partialResults, false) }
        override fun onResults(results: Bundle?) {
            analyser(results, true)
            relancer(120)
        }
        override fun onError(error: Int) {
            bip(true)
            when (error) {
                SpeechRecognizer.ERROR_NO_MATCH, SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> relancer(120)
                SpeechRecognizer.ERROR_RECOGNIZER_BUSY -> { try { sr?.cancel() } catch (e: Exception) {}; relancer(700) }
                SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> {
                    Detection.statut.value = "Micro refusé : autorisez le micro pour MyVerse (Paramètres › Applis › MyVerse)."
                    stopSelf()
                }
                SpeechRecognizer.ERROR_NETWORK, SpeechRecognizer.ERROR_NETWORK_TIMEOUT, SpeechRecognizer.ERROR_SERVER -> {
                    Detection.statut.value = "Reconnaissance vocale : pas d'Internet, nouvel essai…"
                    relancer(3000)
                }
                else -> {
                    erreurs++
                    if (erreurs >= 3) { try { sr?.destroy() } catch (e: Exception) {}; sr = null }
                    if (erreurs >= 3) Detection.statut.value = "La reconnaissance vocale redémarre (erreur $error)…"
                    relancer(if (erreurs > 6) 5000 else 800)
                }
            }
        }
    }

    private fun analyser(b: Bundle?, final: Boolean) {
        val textes = b?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)?.filter { it.isNotBlank() } ?: return
        var texte = textes.firstOrNull() ?: return
        Detection.entendu.value = texte.takeLast(160)
        val bi = bible ?: return
        val maintenant = System.currentTimeMillis()
        val ctx = contexte?.takeIf { maintenant - contexteQuand < 180_000 }
        var trouvees = Detecteur.trouver(texte, bi, ctx, noms)
        // rien dans la meilleure phrase : les autres phrases possibles entendues par le téléphone
        if (trouvees.isEmpty() && final) for (autre in textes.drop(1)) {
            val r = Detecteur.trouver(autre, bi, ctx, noms)
            if (r.isNotEmpty()) { trouvees = r; texte = autre; break }
        }
        val nb = Detecteur.mots(texte).size
        for (f in trouvees) {
            // phrase pas finie : on attend que le nombre soit complet (« Romains 8 2… » → « Romains 8 28 »)
            if (!final && (!f.avecVerset || f.fin >= nb)) continue
            contexte = f.ref
            contexteQuand = maintenant
            proposer(f, bi)
        }
    }

    private fun proposer(f: Detecteur.Trouvaille, bi: BibleTexte) {
        val r = f.ref
        val cle = "${r.b}.${r.c}.${r.v1}.${r.v2}"
        val maintenant = System.currentTimeMillis()
        vus.entries.removeAll { maintenant - it.value > 120_000 }
        if (vus.containsKey(cle)) return
        if (!f.avecVerset && vus.keys.any { it.startsWith("${r.b}.${r.c}.") }) return
        vus[cle] = maintenant
        val label = Books.name(r.b) + " " + r.c + if (f.avecVerset) ":" + r.v1 + (if (r.v2 > r.v1) "-" + r.v2 else "") else ""
        val texte = if (f.avecVerset) bi.texte(r.b, r.c, r.v1, r.v2) else bi.texte(r.b, r.c, 1, 1)
        val s = Detection.ajouter(label, texte)
        MyVerse.remote(this).log("détecteur : $label proposé")
        if (!Detection.premierPlan) notifier(this, s)
    }

    /** Coupe le petit bip que certains téléphones jouent à chaque relance de l'écoute (son des médias). */
    private fun bip(remettre: Boolean) {
        if (!MyVerse.remote(this).store.couperBip) return
        val am = getSystemService(AudioManager::class.java) ?: return
        try {
            if (!remettre) {
                if (!am.isStreamMute(AudioManager.STREAM_MUSIC)) {
                    am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_MUTE, 0)
                    muetParNous = true
                }
            } else if (muetParNous) {
                am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_UNMUTE, 0)
                muetParNous = false
            }
        } catch (e: Exception) {
        }
    }

    private fun notifEcoute() = NotificationCompat.Builder(this, CANAL_ECOUTE)
        .setSmallIcon(R.drawable.ic_notif)
        .setContentTitle("Détecteur de versets actif")
        .setContentText("MyVerse écoute le prédicateur et vous propose les versets cités.")
        .setOngoing(true)
        .setPriority(NotificationCompat.PRIORITY_LOW)
        .setContentIntent(ouvrirApp(this))
        .addAction(0, "Arrêter", PendingIntent.getService(this, 1, Intent(this, Ecoute::class.java).setAction(ACTION_ARRET), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT))
        .build()

    companion object {
        const val CANAL_ECOUTE = "ecoute"
        const val CANAL_VERSETS = "versets"
        const val ACTION_ARRET = "ca.mepc.verselight.remote.ARRET"
        private const val NOTIF_ECOUTE = 1

        fun demarrer(ctx: Context) {
            try {
                ContextCompat.startForegroundService(ctx, Intent(ctx, Ecoute::class.java))
            } catch (e: Exception) {
                Detection.statut.value = "Écoute impossible : ${e.message ?: e.javaClass.simpleName}"
            }
        }

        fun arreter(ctx: Context) { ctx.stopService(Intent(ctx, Ecoute::class.java)) }

        fun canaux(ctx: Context) {
            val nm = ctx.getSystemService(NotificationManager::class.java) ?: return
            nm.createNotificationChannel(NotificationChannel(CANAL_ECOUTE, "Écoute du prédicateur", NotificationManager.IMPORTANCE_LOW))
            nm.createNotificationChannel(NotificationChannel(CANAL_VERSETS, "Versets détectés", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Un verset cité par le prédicateur, prêt à afficher"
            })
        }

        private fun ouvrirApp(ctx: Context): PendingIntent = PendingIntent.getActivity(
            ctx, 0, Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

        /** Notification « Verset détecté » avec le bouton Afficher (app fermée ou en arrière-plan). */
        fun notifier(ctx: Context, s: Detection.Suggestion) {
            val afficher = PendingIntent.getBroadcast(
                ctx, s.id, Intent(ctx, AfficherRecu::class.java).putExtra("ref", s.label).putExtra("id", s.id),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val n = NotificationCompat.Builder(ctx, CANAL_VERSETS)
                .setSmallIcon(R.drawable.ic_notif)
                .setContentTitle("Verset cité : ${s.label}")
                .setContentText(s.texte)
                .setStyle(NotificationCompat.BigTextStyle().bigText(s.texte))
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION)
                .setAutoCancel(true)
                .setTimeoutAfter(180_000)
                .setContentIntent(ouvrirApp(ctx))
                .addAction(0, "Afficher", afficher)
                .build()
            try {
                NotificationManagerCompat.from(ctx).notify(1000 + s.id, n)
            } catch (e: SecurityException) {
            }
        }
    }
}

/** Bouton « Afficher » de la notification : le verset part à l'écran (par MyVerse sur le PC). */
class AfficherRecu : BroadcastReceiver() {
    override fun onReceive(ctx: Context, intent: Intent) {
        val ref = intent.getStringExtra("ref") ?: return
        val id = intent.getIntExtra("id", 0)
        NotificationManagerCompat.from(ctx).cancel(1000 + id)
        Detection.retirer(id)
        if (!MyVerse.remote(ctx).send("bible.show", "ref" to ref)) {
            Toast.makeText(ctx, "MyVerse n'est pas connecté au PC : impossible d'afficher $ref", Toast.LENGTH_LONG).show()
        }
    }
}
