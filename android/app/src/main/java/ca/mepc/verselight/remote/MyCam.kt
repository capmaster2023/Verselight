package ca.mepc.verselight.remote

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.graphics.ImageFormat
import android.graphics.Rect
import android.graphics.YuvImage
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CaptureRequest
import android.net.wifi.WifiManager
import android.os.BatteryManager
import android.os.Build
import android.os.SystemClock
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Range
import android.util.Size
import android.view.OrientationEventListener
import android.view.Surface
import androidx.activity.ComponentActivity
import androidx.camera.camera2.interop.Camera2CameraInfo
import androidx.camera.camera2.interop.Camera2Interop
import androidx.camera.camera2.interop.ExperimentalCamera2Interop
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * MyCam : le téléphone devient une caméra de MyChurch, comme DroidCam.
 * CameraX → vidéo H.264 de l'encodeur matériel (MyChurch 4.1 sur le PC), sinon images JPEG (en-tête « MC »)
 * → la connexion WebSocket déjà authentifiée avec le PC. La caméra est tenue à la cadence choisie, même dans la pénombre.
 * Le PC renvoie le témoin (rouge : à l'écran, orange : ensuite) et les messages de la régie.
 */
object MyCam {
    /** La diffusion vers le PC est lancée. */
    val diffuse = MutableStateFlow(false)
    /** Plein écran, téléphone à l'horizontale. */
    val pleinEcran = MutableStateFlow(false)
    /** Caméra avant (selfie) au lieu de la caméra arrière. */
    val avant = MutableStateFlow(false)
    val torche = MutableStateFlow(false)
    val zoom = MutableStateFlow(1f)
    val zoomMin = MutableStateFlow(1f)
    val zoomMax = MutableStateFlow(1f)
    val expo = MutableStateFlow(0)
    val expoMin = MutableStateFlow(0)
    val expoMax = MutableStateFlow(0)
    /** Images envoyées dans la dernière seconde. */
    val fps = MutableStateFlow(0)
    /** « 1280 × 720 » */
    val taille = MutableStateFlow("")
    val erreur = MutableStateFlow("")
    /** La caméra est ouverte (aperçu disponible). */
    val ouverte = MutableStateFlow(false)
    /** Début de la diffusion et du passage à l'écran (horloge système, ms). */
    val depuis = MutableStateFlow(0L)
    val enDirectDepuis = MutableStateFlow(0L)
    val batterie = MutableStateFlow(-1)
    val temperature = MutableStateFlow(0.0)
    /** Le téléphone est tenu à la verticale (l'image est alors recadrée en 16:9, comme le viseur). */
    val portrait = MutableStateFlow(true)
    /** Qualité JPEG du moment (elle baisse quand le Wi-Fi ne suit pas, puis remonte). */
    val qualiteImage = MutableStateFlow(0)
    /** Format envoyé : « H.264 » ou « JPEG » (vide sans diffusion). */
    val format = MutableStateFlow("")
    /** Débit H.264 du moment en kb/s (il baisse quand le Wi-Fi ne suit pas, puis remonte) ; 0 en JPEG. */
    val debit = MutableStateFlow(0)

    private var activite: ComponentActivity? = null
    private var remote: Remote? = null
    private var camera: Camera? = null
    private var apercu: Preview? = null
    private var analyse: ImageAnalysis? = null
    private var vueApercu: PreviewView? = null
    private var ecranVisible = false
    private var orientation: OrientationEventListener? = null
    private val executeur = Executors.newSingleThreadExecutor()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var boucles = false
    @Volatile private var rotationCible = Surface.ROTATION_0
    @Volatile private var dernierEnvoi = 0L
    private val envoyees = AtomicInteger(0)
    @Volatile private var infoDemandee = false
    /** Dernière image donnée par la caméra (même sautée) : la caméra vit. */
    @Volatile private var dernierCadre = 0L
    @Volatile private var ouverteDepuis = 0L
    @Volatile private var qualite = 0
    private var bonnes = 0
    private var derniereBaisse = 0L
    /** Octets en attente d'envoi au-delà desquels une image est sautée : au plus ~1 image d'avance, pour un retard minimal. */
    private const val SEUIL_FILE = 150_000L

    // H.264 (MyChurch 4.1)
    private const val H264_ENVOYE = 0
    private const val H264_SAUTE = 1
    private const val H264_IMPOSSIBLE = 2
    private val verrouEncodeur = Any()
    private var encodeur: EncodeurH264? = null
    /** H.264 abandonné pour cette diffusion : encodeur en panne, ou le PC demande le JPEG. */
    @Volatile private var h264Refuse = false
    /** Zone et cadence que l'encodeur de ce téléphone refuse (JPEG pour elles seulement). */
    @Volatile private var tailleRefusee = ""
    /** Le PC attend une image clé (début, reprise, image perdue). */
    @Volatile private var cleDemandee = false
    @Volatile private var rotationEnvoi = 0
    private var derniereCle = 0L
    private var debitCible = 0
    private var dernierReglage = 0L
    private var wifi: WifiManager.WifiLock? = null

    private class Tampon : ByteArrayOutputStream(256 * 1024) {
        val octets: ByteArray get() = buf
        val longueur: Int get() = count
    }

    private val tampon = Tampon()
    private var nv21 = ByteArray(0)
    private var ligneA = ByteArray(0)
    private var ligneB = ByteArray(0)

    // ------------------------------------------------------------ cycle de vie (activité)

    fun attacher(a: ComponentActivity, r: Remote) {
        activite = a
        remote = r
        orientation?.disable()
        orientation = object : OrientationEventListener(a) {
            override fun onOrientationChanged(o: Int) {
                if (o == OrientationEventListener.ORIENTATION_UNKNOWN) return
                val rot = when (o) {
                    in 45 until 135 -> Surface.ROTATION_270
                    in 135 until 225 -> Surface.ROTATION_180
                    in 225 until 315 -> Surface.ROTATION_90
                    else -> Surface.ROTATION_0
                }
                if (rot != rotationCible) {
                    rotationCible = rot
                    analyse?.setTargetRotation(rot)
                    portrait.value = rot == Surface.ROTATION_0 || rot == Surface.ROTATION_180
                }
            }
        }.also { it.enable() }
        if (!boucles) {
            boucles = true
            scope.launch { compter() }
            scope.launch { informer() }
            scope.launch { suivreTemoin(r) }
        }
        if (camera != null) {
            fermer()
            appliquer()
        }
        if (diffuse.value) wifiRapide(true)
    }

    fun detacher(a: ComponentActivity) {
        if (activite !== a) return
        orientation?.disable()
        orientation = null
        fermer()
        wifiRapide(false)
        activite = null
    }

    /** L'onglet MyCam est affiché : la caméra s'ouvre pour cadrer, même sans diffuser. */
    fun ecran(visible: Boolean) {
        ecranVisible = visible
        appliquer()
    }

    fun lierApercu(v: PreviewView?) {
        vueApercu = v
        apercu?.setSurfaceProvider(v?.surfaceProvider)
    }

    fun delierApercu(v: PreviewView?) {
        if (v != null && vueApercu === v) lierApercu(null)
    }

    // ------------------------------------------------------------ diffusion

    fun peutFilmer(c: Context) = ContextCompat.checkSelfPermission(c, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED

    fun demarrerDiffusion() {
        val r = remote ?: return
        if (r.proto.value < 2) {
            Bulles.dire("MyCam demande MyChurch 4.0 ou plus sur le PC.")
            return
        }
        diffuse.value = true
        depuis.value = System.currentTimeMillis()
        h264Refuse = false
        tailleRefusee = ""
        cleDemandee = true
        wifiRapide(true)
        appliquer()
        infoDemandee = true
        Bulles.dire("MyCam diffuse vers « ${r.pcName.value.ifBlank { "le PC" }} »")
    }

    fun arreterDiffusion() {
        if (!diffuse.value) return
        diffuse.value = false
        depuis.value = 0L
        fermerEncodeur()
        wifiRapide(false)
        format.value = ""
        infoDemandee = true
        envoyerInfo()
        appliquer()
    }

    /** Reconnexion au PC : on redit tout de suite qui on est (nom, image, batterie) ; la vidéo repart d'une image clé. */
    fun reconnecte() {
        infoDemandee = true
        cleDemandee = true
    }

    /** Le PC (MyChurch 4.1) attend une image clé : son décodeur démarre, reprend, ou a perdu le fil. */
    fun demanderCle() {
        cleDemandee = true
    }

    /** Le PC ne décode pas le H.264 (FFmpeg absent) : images JPEG pour cette diffusion. */
    fun formatDuPc(codec: String) {
        if (codec == "jpeg") refuserH264("le PC demande des images JPEG")
    }

    /** Résolution, cadence ou format changés dans les réglages. */
    fun reconfigurer() {
        h264Refuse = false
        tailleRefusee = ""
        fermerEncodeur()
        if (camera != null) {
            fermer()
            appliquer()
        }
        infoDemandee = true
    }

    fun basculerCamera() {
        avant.value = !avant.value
        torche.value = false
        zoom.value = 1f
        if (camera != null) {
            fermer()
            appliquer()
        }
        infoDemandee = true
    }

    fun reglerZoom(z: Float) {
        val v = z.coerceIn(zoomMin.value, zoomMax.value.coerceAtLeast(zoomMin.value))
        zoom.value = v
        camera?.cameraControl?.setZoomRatio(v)
    }

    fun basculerTorche() {
        val c = camera ?: return
        if (!c.cameraInfo.hasFlashUnit()) {
            Bulles.dire("Pas de lampe sur cette caméra.")
            return
        }
        torche.value = !torche.value
        c.cameraControl.enableTorch(torche.value)
    }

    fun reglerExpo(i: Int) {
        val v = i.coerceIn(expoMin.value, expoMax.value)
        expo.value = v
        camera?.cameraControl?.setExposureCompensationIndex(v)
    }

    /** Mise au point et exposition sur le point touché du viseur. */
    fun mettreAuPoint(x: Float, y: Float) {
        val v = vueApercu ?: return
        val c = camera ?: return
        val point = v.meteringPointFactory.createPoint(x, y)
        val action = FocusMeteringAction.Builder(point, FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE)
            .setAutoCancelDuration(4, TimeUnit.SECONDS)
            .build()
        c.cameraControl.startFocusAndMetering(action)
    }

    // ------------------------------------------------------------ caméra

    private fun appliquer() {
        val a = activite ?: return
        if (!ecranVisible && !diffuse.value) {
            fermer()
            return
        }
        if (!peutFilmer(a)) {
            erreur.value = "Autorisez la caméra pour MyCam."
            return
        }
        if (camera != null) return
        val f = ProcessCameraProvider.getInstance(a)
        f.addListener({
            val p = try {
                f.get()
            } catch (e: Exception) {
                erreur.value = "Caméra indisponible : ${e.message ?: e.javaClass.simpleName}"
                return@addListener
            }
            if (activite === a && camera == null && (ecranVisible || diffuse.value)) ouvrir(p, a)
        }, ContextCompat.getMainExecutor(a))
    }

    @androidx.annotation.OptIn(ExperimentalCamera2Interop::class)
    private fun ouvrir(p: ProcessCameraProvider, a: ComponentActivity) {
        val store = remote?.store ?: return
        val voulue = if (store.camResolution >= 1080) Size(1920, 1080) else Size(1280, 720)
        val choix = ResolutionSelector.Builder()
            .setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY)
            .setResolutionStrategy(ResolutionStrategy(voulue, ResolutionStrategy.FALLBACK_RULE_CLOSEST_LOWER_THEN_HIGHER))
            .build()
        val selecteur = if (avant.value) CameraSelector.DEFAULT_FRONT_CAMERA else CameraSelector.DEFAULT_BACK_CAMERA
        val prevB = Preview.Builder()
            .setResolutionSelector(ResolutionSelector.Builder().setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY).build())
        val anB = ImageAnalysis.Builder()
            .setResolutionSelector(choix)
            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
            .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_YUV_420_888)
            .setTargetRotation(rotationCible)
        // la cadence choisie tenue même dans la pénombre de l'église (sans ceci, l'exposition automatique descend vers 10-15 i/s)
        plageFps(p, selecteur, store.camFps.coerceIn(5, 30))?.let { plage ->
            Camera2Interop.Extender(prevB).setCaptureRequestOption(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, plage)
            Camera2Interop.Extender(anB).setCaptureRequestOption(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, plage)
        }
        val prev = prevB.build()
        val an = anB.build()
        an.setAnalyzer(executeur) { img -> traiter(img) }
        try {
            val cam = p.bindToLifecycle(a, selecteur, prev, an)
            camera = cam
            ouverteDepuis = SystemClock.elapsedRealtime()
            dernierCadre = ouverteDepuis
            apercu = prev
            analyse = an
            vueApercu?.let { prev.setSurfaceProvider(it.surfaceProvider) }
            cam.cameraInfo.zoomState.observe(a) { z ->
                zoomMin.value = z.minZoomRatio
                zoomMax.value = z.maxZoomRatio
                zoom.value = z.zoomRatio
            }
            val ex = cam.cameraInfo.exposureState
            if (ex.isExposureCompensationSupported) {
                expoMin.value = ex.exposureCompensationRange.lower
                expoMax.value = ex.exposureCompensationRange.upper
                cam.cameraControl.setExposureCompensationIndex(expo.value.coerceIn(expoMin.value, expoMax.value))
            } else {
                expoMin.value = 0
                expoMax.value = 0
            }
            if (zoom.value != 1f) cam.cameraControl.setZoomRatio(zoom.value)
            if (torche.value && cam.cameraInfo.hasFlashUnit()) cam.cameraControl.enableTorch(true)
            ouverte.value = true
            erreur.value = ""
        } catch (e: Exception) {
            erreur.value = "Caméra indisponible : ${e.message ?: e.javaClass.simpleName}"
            ouverte.value = false
        }
    }

    /**
     * Plage de cadence de l'exposition automatique : celle dont le haut est la cadence voulue (ou le plus proche au-dessus)
     * et le bas le plus haut possible — idéalement [30, 30] : la caméra garde 30 i/s et monte la sensibilité au lieu d'allonger la pose.
     */
    @androidx.annotation.OptIn(ExperimentalCamera2Interop::class)
    private fun plageFps(p: ProcessCameraProvider, selecteur: CameraSelector, voulu: Int): Range<Int>? = try {
        val info = selecteur.filter(p.availableCameraInfos).firstOrNull()
        val plages = info?.let { Camera2CameraInfo.from(it).getCameraCharacteristic(CameraCharacteristics.CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES) }
        plages?.filter { it.upper >= voulu }?.minWithOrNull(compareBy<Range<Int>>({ it.upper }, { -it.lower }))
    } catch (e: Exception) {
        null
    }

    private fun fermer() {
        val a = activite
        val c = camera
        camera = null
        ouverte.value = false
        fermerEncodeur()
        if (a != null && c != null) c.cameraInfo.zoomState.removeObservers(a)
        val prev = apercu
        val an = analyse
        apercu = null
        analyse = null
        an?.clearAnalyzer()
        if (a != null && (prev != null || an != null)) {
            val f = ProcessCameraProvider.getInstance(a)
            f.addListener({
                try {
                    val p = f.get()
                    if (prev != null) p.unbind(prev)
                    if (an != null) p.unbind(an)
                } catch (e: Exception) {
                }
            }, ContextCompat.getMainExecutor(a))
        }
    }

    // ------------------------------------------------------------ images (fil de la caméra)

    private fun traiter(img: ImageProxy) {
        try {
            val maintenant = SystemClock.elapsedRealtime()
            dernierCadre = maintenant
            val r = remote ?: return
            if (!diffuse.value) return
            val cadence = r.store.camFps.coerceIn(5, 30)
            if (maintenant - dernierEnvoi < 1000L / cadence - 4) return
            if (!r.pret()) return
            val w = img.width
            val h = img.height
            val rotation = (img.imageInfo.rotationDegrees / 90) and 3
            // téléphone à la verticale : la bande 16:9 du milieu, comme le viseur (l'image debout est en hauteur ;
            // sa bande horizontale est une bande verticale du capteur)
            val zone = if (rotation % 2 == 1 && r.store.camRemplir) {
                val bw = (h * 9 / 16) and 1.inv()
                val x0 = ((w - bw) / 2) and 1.inv()
                Rect(x0, 0, x0 + bw, h)
            } else Rect(0, 0, w, h)
            if (veutH264(r)) {
                when (h264(r, img, zone, rotation, cadence, maintenant)) {
                    H264_ENVOYE -> {
                        dernierEnvoi = maintenant
                        return
                    }
                    H264_SAUTE -> return
                }
            } else if (encodeur != null) fermerEncodeur()
            jpeg(r, img, zone, rotation, maintenant)
        } catch (e: Exception) {
            erreur.value = "Image : ${e.message ?: e.javaClass.simpleName}"
        } finally {
            img.close()
        }
    }

    /** Images JPEG une à une (PC en MyChurch 4.0, ou H.264 indisponible). */
    private fun jpeg(r: Remote, img: ImageProxy, zone: Rect, rotation: Int, maintenant: Long) {
        val w = img.width
        val h = img.height
        val max = if (h >= 1080 || w >= 1920) 70 else 76
        if (qualite == 0 || qualite > max) qualite = max
        if (format.value != "JPEG") format.value = "JPEG"
        // le Wi-Fi ne suit pas (plus d'une image en attente) : cette image est sautée et la qualité baisse, pour garder le direct
        if (r.fileEnvoi() > SEUIL_FILE) {
            if (maintenant - derniereBaisse > 400) {
                qualite = (qualite - 6).coerceAtLeast(40)
                derniereBaisse = maintenant
                bonnes = 0
            }
            return
        }
        val yuv = versNv21(img)
        tampon.reset()
        tampon.write(byteArrayOf('M'.code.toByte(), 'C'.code.toByte(), 1, rotation.toByte()))
        YuvImage(yuv, ImageFormat.NV21, w, h, null).compressToJpeg(zone, qualite, tampon)
        if (r.sendBinary(tampon.octets, tampon.longueur)) {
            dernierEnvoi = maintenant
            envoyees.incrementAndGet()
            // tout passe : la qualité remonte doucement
            if (++bonnes >= 45 && qualite < max) {
                qualite = (qualite + 3).coerceAtMost(max)
                bonnes = 0
            }
        }
        if (qualiteImage.value != qualite) qualiteImage.value = qualite
        afficherTaille(zone, rotation)
    }

    private fun afficherTaille(zone: Rect, rotation: Int) {
        val t = if (rotation % 2 == 1) "${zone.height()} × ${zone.width()}" else "${zone.width()} × ${zone.height()}"
        if (taille.value != t) taille.value = t
    }

    /** Le PC décode le H.264 (MyChurch 4.1), le réglage le demande, et l'encodeur de ce téléphone n'a pas lâché. */
    private fun veutH264(r: Remote) = r.store.camFormat != "jpeg" && !h264Refuse && "h264" in r.codecsPc.value

    /** Une image vers l'encodeur H.264 (fil de la caméra) : envoyée, sautée (Wi-Fi ou encodeur en retard), ou impossible (JPEG). */
    private fun h264(r: Remote, img: ImageProxy, voulue: Rect, rotation: Int, cadence: Int, maintenant: Long): Int {
        synchronized(verrouEncodeur) {
            var e = encodeur
            e?.panne?.let { p ->
                fermerEncodeurVerrouille()
                refuserH264("encodeur en panne : $p")
                return H264_IMPOSSIBLE
            }
            if (e != null && (e.voulueL != voulue.width() || e.voulueH != voulue.height() || e.fps != cadence)) {
                // téléphone tourné (autre zone) ou cadence changée : nouvel encodeur, qui commence par une image clé
                fermerEncodeurVerrouille()
                e = null
            }
            if (e == null) {
                if (!ouverte.value) return H264_SAUTE
                val cle = "${voulue.width()}×${voulue.height()}@$cadence"
                if (cle == tailleRefusee) return H264_IMPOSSIBLE
                e = try {
                    EncodeurH264.creer(voulue.width(), voulue.height(), cadence) { donnees, n, cleImage -> sortieH264(donnees, n, cleImage) }
                } catch (ex: Exception) {
                    tailleRefusee = cle
                    Bulles.dire("Ce téléphone refuse le H.264 en ${voulue.width()} × ${voulue.height()} : MyCam envoie des images JPEG.")
                    return H264_IMPOSSIBLE
                }
                encodeur = e
                debitCible = e.debit
                dernierReglage = maintenant
                cleDemandee = false
                debit.value = e.debit / 1000
                format.value = "H.264"
            }
            rotationEnvoi = rotation
            // le Wi-Fi ne suit pas (plus de ~1/8 s de vidéo en attente) : image sautée et débit en baisse ; il remonte quand tout passe
            if (r.fileEnvoi() > maxOf(48_000L, e.debit / 64L)) {
                if (maintenant - dernierReglage > 500) {
                    e.reglerDebit(maxOf(e.debitMin, e.debit * 3 / 4))
                    dernierReglage = maintenant
                    debit.value = e.debit / 1000
                }
                return H264_SAUTE
            }
            if (e.debit < debitCible && maintenant - dernierReglage > 4000) {
                e.reglerDebit(minOf(debitCible, e.debit * 5 / 4))
                dernierReglage = maintenant
                debit.value = e.debit / 1000
            }
            if (cleDemandee && maintenant - derniereCle > 500) {
                cleDemandee = false
                derniereCle = maintenant
                e.demanderCle()
            }
            val zone = e.zone(voulue)
            afficherTaille(zone, rotation)
            return if (e.encoder(img, zone, img.imageInfo.timestamp / 1000)) H264_ENVOYE else H264_SAUTE
        }
    }

    /** Une unité d'accès H.264 sortie de l'encodeur (son fil) : en-tête MyCam version 2, puis vers le PC. */
    private fun sortieH264(donnees: ByteArray, n: Int, cle: Boolean) {
        val r = remote ?: return
        if (!diffuse.value) return
        donnees[0] = 'M'.code.toByte()
        donnees[1] = 'C'.code.toByte()
        donnees[2] = 2
        donnees[3] = (rotationEnvoi or (if (cle) 8 else 0)).toByte()
        if (r.sendBinary(donnees, n)) envoyees.incrementAndGet()
        // une image perdue casse la suite des images P sur le PC : on repart d'une image clé
        else cleDemandee = true
    }

    private fun refuserH264(raison: String) {
        if (h264Refuse) return
        h264Refuse = true
        Bulles.dire("H.264 indisponible ($raison) : MyCam envoie des images JPEG.")
    }

    private fun fermerEncodeur() {
        synchronized(verrouEncodeur) { fermerEncodeurVerrouille() }
    }

    private fun fermerEncodeurVerrouille() {
        encodeur?.fermer()
        encodeur = null
        debit.value = 0
    }

    /** Pendant la diffusion, le Wi-Fi du téléphone ne s'assoupit pas entre deux images (sinon : saccades et retard). */
    private fun wifiRapide(actif: Boolean) {
        try {
            if (!actif) {
                wifi?.let { if (it.isHeld) it.release() }
                wifi = null
                return
            }
            if (wifi?.isHeld == true) return
            val wm = activite?.applicationContext?.getSystemService(WifiManager::class.java) ?: return
            @Suppress("DEPRECATION")
            val mode = if (Build.VERSION.SDK_INT >= 29) WifiManager.WIFI_MODE_FULL_LOW_LATENCY else WifiManager.WIFI_MODE_FULL_HIGH_PERF
            wifi = wm.createWifiLock(mode, "MyChurch:MyCam").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (e: Exception) {
        }
    }

    /** YUV_420_888 (pas de ligne et de pixel quelconques) → NV21 pour YuvImage. */
    private fun versNv21(img: ImageProxy): ByteArray {
        val w = img.width
        val h = img.height
        val cw = (w + 1) / 2
        val ch = (h + 1) / 2
        val total = w * h + 2 * cw * ch
        if (nv21.size != total) nv21 = ByteArray(total)
        val out = nv21
        val py = img.planes[0]
        val pu = img.planes[1]
        val pv = img.planes[2]
        val yb = py.buffer
        val yRow = py.rowStride
        val yPix = py.pixelStride
        var pos = 0
        if (yPix == 1 && yRow == w) {
            yb.rewind()
            yb.get(out, 0, w * h)
            pos = w * h
        } else {
            if (ligneA.size < yRow) ligneA = ByteArray(yRow)
            for (r in 0 until h) {
                yb.position(r * yRow)
                yb.get(ligneA, 0, minOf(yRow, yb.remaining()))
                if (yPix == 1) System.arraycopy(ligneA, 0, out, pos, w)
                else for (c in 0 until w) out[pos + c] = ligneA[c * yPix]
                pos += w
            }
        }
        val ub = pu.buffer
        val vb = pv.buffer
        val uRow = pu.rowStride
        val vRow = pv.rowStride
        val uPix = pu.pixelStride
        val vPix = pv.pixelStride
        if (ligneA.size < vRow) ligneA = ByteArray(vRow)
        if (ligneB.size < uRow) ligneB = ByteArray(uRow)
        for (r in 0 until ch) {
            vb.position(r * vRow)
            vb.get(ligneA, 0, minOf(vRow, vb.remaining()))
            ub.position(r * uRow)
            ub.get(ligneB, 0, minOf(uRow, ub.remaining()))
            var c = 0
            while (c < cw) {
                out[pos++] = ligneA[c * vPix]
                out[pos++] = ligneB[c * uPix]
                c++
            }
        }
        return out
    }

    // ------------------------------------------------------------ boucles : cadence, état vers le PC, témoin

    private suspend fun compter() {
        while (true) {
            delay(1000)
            val n = envoyees.getAndSet(0)
            fps.value = if (diffuse.value) n else 0
            // caméra bloquée : plus aucune image depuis 3 s pendant la diffusion (application au premier plan) → on la rouvre
            val maintenant = SystemClock.elapsedRealtime()
            val active = activite?.lifecycle?.currentState?.isAtLeast(Lifecycle.State.STARTED) == true
            if (diffuse.value && active && camera != null && maintenant - dernierCadre > 3000 && maintenant - ouverteDepuis > 4000) {
                erreur.value = ""
                fermer()
                appliquer()
            }
        }
    }

    private suspend fun informer() {
        var n = 0
        while (true) {
            delay(500)
            n++
            if (infoDemandee || (diffuse.value && n % 10 == 0)) {
                infoDemandee = false
                envoyerInfo()
            }
        }
    }

    private suspend fun suivreTemoin(r: Remote) {
        var avant = "off"
        r.temoin.collect { t ->
            if (t == "live" && avant != "live") {
                enDirectDepuis.value = System.currentTimeMillis()
                if (diffuse.value && r.store.camVibrer) vibrer()
            }
            if (t != "live") enDirectDepuis.value = 0L
            avant = t
        }
    }

    private fun vibrer() {
        val a = activite ?: return
        try {
            val v: Vibrator? = if (Build.VERSION.SDK_INT >= 31) a.getSystemService(VibratorManager::class.java)?.defaultVibrator
            else a.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
            v?.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 140, 90, 140), -1))
        } catch (e: Exception) {
        }
    }

    /** Nom de la source dans MyChurch. */
    fun nom(): String = remote?.store?.camNom?.takeIf { it.isNotBlank() } ?: "MyCam · ${Build.MODEL}"

    private fun envoyerInfo() {
        val r = remote ?: return
        val a = activite ?: return
        if (!r.pret() || r.proto.value < 2) return
        val b = try {
            ContextCompat.registerReceiver(a, null, IntentFilter(Intent.ACTION_BATTERY_CHANGED), ContextCompat.RECEIVER_NOT_EXPORTED)
        } catch (e: Exception) {
            null
        }
        val niveau = b?.let {
            val l = it.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val s = it.getIntExtra(BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
            if (l >= 0) l * 100 / s else -1
        } ?: -1
        val charge = (b?.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) ?: 0) != 0
        val temp = (b?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) ?: 0) / 10.0
        batterie.value = niveau
        temperature.value = temp
        r.send(
            "cam.info",
            "diffuse" to diffuse.value,
            "nom" to nom(),
            "appareil" to "${Build.MANUFACTURER} ${Build.MODEL}",
            "taille" to taille.value,
            "fps" to fps.value,
            "fpsCible" to r.store.camFps,
            "batterie" to niveau,
            "charge" to charge,
            "temperature" to temp,
            "lien" to r.lien(),
            "avant" to avant.value,
            "depuis" to depuis.value,
            "codec" to format.value,
            "debit" to debit.value,
        )
    }
}
