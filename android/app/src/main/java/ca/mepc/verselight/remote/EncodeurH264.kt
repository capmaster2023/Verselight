package ca.mepc.verselight.remote

import android.graphics.Rect
import android.media.Image
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import androidx.camera.core.ImageProxy
import java.io.ByteArrayOutputStream
import java.util.concurrent.ConcurrentLinkedQueue

/**
 * MyCam en H.264 (MyChurch 4.1) : l'encodeur vidéo matériel du téléphone (MediaCodec), comme DroidCam.
 * Les images YUV de la caméra (recadrées au besoin) deviennent des unités d'accès H.264 (Annex B) ; les SPS/PPS précèdent
 * chaque image clé, pour que le PC prenne le flux en route. Environ 10 fois plus léger que le JPEG : le Wi-Fi suit à 30 i/s.
 * Profil Baseline (pas d'images B), priorité temps réel : chaque image sort dès qu'elle est encodée.
 */
internal class EncodeurH264 private constructor(
    /** Zone demandée (caméra) : l'encodeur est recréé quand elle change. */
    val voulueL: Int,
    val voulueH: Int,
    /** Taille encodée : la zone demandée, arrondie à ce que l'encodeur accepte. */
    val largeur: Int,
    val hauteur: Int,
    val fps: Int,
    debitInitial: Int,
    /** Débit plancher quand le Wi-Fi faiblit (b/s). */
    val debitMin: Int,
    private val codec: MediaCodec,
    private val fil: HandlerThread,
) {
    /** Débit du moment (b/s). */
    @Volatile var debit = debitInitial
        private set
    /** Erreur de l'encodeur : MyCam repasse en JPEG. */
    @Volatile var panne: String? = null
        private set
    @Volatile private var ferme = false
    @Volatile private var parametres: ByteArray? = null
    private val libres = ConcurrentLinkedQueue<Int>()
    /** Entrelacement des plans U et V de l'encodeur : -1 pas encore sondé, 0 plans séparés, 1 U puis V (NV12), 2 V puis U (NV21). */
    private var ordre = -1
    private var ligne = ByteArray(0)
    private var ligneU = ByteArray(0)
    private var ligneV = ByteArray(0)
    private var melange = ByteArray(0)

    companion object {
        /** Octets laissés libres devant chaque unité d'accès pour l'en-tête MyCam (« M », « C », version, drapeaux). */
        const val ENTETE = 4

        /**
         * Un encodeur pour une zone de la caméra. [sortie] reçoit chaque unité d'accès (fil de l'encodeur) : le tableau commence par
         * [ENTETE] octets libres, puis les données ; la longueur totale ; image clé ou non. Lance une exception si le téléphone refuse.
         */
        fun creer(voulueL: Int, voulueH: Int, fps: Int, sortie: (ByteArray, Int, Boolean) -> Unit): EncodeurH264 {
            val type = MediaFormat.MIMETYPE_VIDEO_AVC
            val codec = MediaCodec.createEncoderByType(type)
            val fil = HandlerThread("MyCam-H264").also { it.start() }
            try {
                val caps = codec.codecInfo.getCapabilitiesForType(type)
                val v = caps.videoCapabilities
                val al = v.widthAlignment.coerceAtLeast(2)
                val ah = v.heightAlignment.coerceAtLeast(2)
                var l = voulueL / al * al
                val h = voulueH / ah * ah
                // largeur multiple de 16 : certains encodeurs n'acceptent rien d'autre
                if (l % 16 != 0 && l > 16) l = l / 16 * 16
                if (l < 64 || h < 64 || !v.isSizeSupported(l, h)) throw IllegalArgumentException("taille $l × $h refusée par l'encodeur")
                // ≈ 0,14 bit par pixel : 720p à 30 i/s ≈ 3,9 Mb/s, 1080p ≈ 8 Mb/s
                val plage = v.bitrateRange
                val debit = (l.toLong() * h * fps * 14 / 100).toInt().coerceIn(1_500_000, 8_000_000).coerceIn(plage.lower, plage.upper)
                val min = (debit / 4).coerceAtLeast(400_000).coerceIn(plage.lower, plage.upper)
                val e = EncodeurH264(voulueL, voulueH, l, h, fps, debit, min, codec, fil)
                e.demarrer(caps, sortie)
                return e
            } catch (ex: Exception) {
                try {
                    codec.release()
                } catch (_: Exception) {
                }
                fil.quitSafely()
                throw ex
            }
        }

        /** Les SPS et PPS (NAL 7 et 8, codes de début compris) d'un tampon Annex B ; null s'il n'y en a pas. */
        fun parametresDe(b: ByteArray): ByteArray? {
            val debuts = ArrayList<Int>()
            var i = 0
            while (i + 3 <= b.size) {
                if (b[i] == 0.toByte() && b[i + 1] == 0.toByte() && b[i + 2] == 1.toByte()) {
                    debuts.add(if (i > 0 && b[i - 1] == 0.toByte()) i - 1 else i)
                    i += 3
                } else i++
            }
            val sortie = ByteArrayOutputStream()
            for (k in debuts.indices) {
                val s = debuts[k]
                val e = if (k + 1 < debuts.size) debuts[k + 1] else b.size
                val tete = if (b[s + 2] == 1.toByte()) s + 3 else s + 4
                if (tete < e) {
                    val t = b[tete].toInt() and 0x1F
                    if (t == 7 || t == 8) sortie.write(b, s, e - s)
                }
            }
            return if (sortie.size() > 0) sortie.toByteArray() else null
        }

        /** Type de la première NAL d'un tampon Annex B (7 : SPS), -1 si illisible. */
        private fun premiereNal(b: ByteArray, debut: Int): Int {
            var i = debut
            while (i < b.size && b[i] == 0.toByte()) i++
            return if (i + 1 < b.size && b[i] == 1.toByte()) b[i + 1].toInt() and 0x1F else -1
        }
    }

    private fun format(complet: Boolean, cbr: Boolean) = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, largeur, hauteur).apply {
        setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible)
        setInteger(MediaFormat.KEY_BIT_RATE, debit)
        setInteger(MediaFormat.KEY_FRAME_RATE, fps)
        setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 2)
        if (cbr) setInteger(MediaFormat.KEY_BITRATE_MODE, MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR)
        if (complet) {
            // temps réel, une image sortie par image entrée, pas d'images B
            setInteger(MediaFormat.KEY_PRIORITY, 0)
            setInteger(MediaFormat.KEY_LATENCY, 1)
            if (Build.VERSION.SDK_INT >= 29) setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
            setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
            setInteger(MediaFormat.KEY_LEVEL, MediaCodecInfo.CodecProfileLevel.AVCLevel4)
            // l'image de la caméra est en plein écart (0-255), comme le JPEG ; le PC la lit ainsi
            setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_FULL)
            setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT601_PAL)
            setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO)
        }
    }

    private fun demarrer(caps: MediaCodecInfo.CodecCapabilities, sortie: (ByteArray, Int, Boolean) -> Unit) {
        val rappel = object : MediaCodec.Callback() {
            override fun onInputBufferAvailable(c: MediaCodec, index: Int) {
                if (!ferme) libres.add(index)
            }

            override fun onOutputBufferAvailable(c: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
                if (ferme) return
                try {
                    val b = c.getOutputBuffer(index)
                    if (b != null && info.size > 0) {
                        b.position(info.offset)
                        b.limit(info.offset + info.size)
                        if (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0 && info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME == 0) {
                            val brut = ByteArray(info.size).also { b.get(it) }
                            parametres = parametresDe(brut) ?: brut
                        } else {
                            val cle = info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0
                            val out = ByteArray(ENTETE + info.size)
                            b.get(out, ENTETE, info.size)
                            val p = parametres
                            if (cle && p != null && premiereNal(out, ENTETE) != 7) {
                                // SPS/PPS devant l'image clé : le PC peut commencer à décoder ici
                                val avec = ByteArray(ENTETE + p.size + info.size)
                                System.arraycopy(p, 0, avec, ENTETE, p.size)
                                System.arraycopy(out, ENTETE, avec, ENTETE + p.size, info.size)
                                sortie(avec, avec.size, true)
                            } else {
                                if (cle && p == null) parametres = parametresDe(out)
                                sortie(out, out.size, cle)
                            }
                        }
                    }
                } catch (_: Exception) {
                } finally {
                    try {
                        c.releaseOutputBuffer(index, false)
                    } catch (_: Exception) {
                    }
                }
            }

            override fun onError(c: MediaCodec, e: MediaCodec.CodecException) {
                panne = e.diagnosticInfo ?: e.message ?: "erreur de l’encodeur"
            }

            override fun onOutputFormatChanged(c: MediaCodec, format: MediaFormat) {
                // certains encodeurs ne donnent les SPS/PPS que dans le format (csd-0, csd-1)
                if (parametres != null) return
                val a = format.getByteBuffer("csd-0") ?: return
                val b = format.getByteBuffer("csd-1")
                val o = ByteArray(a.remaining() + (b?.remaining() ?: 0))
                a.duplicate().get(o, 0, a.remaining())
                b?.duplicate()?.get(o, a.remaining(), b.remaining())
                parametres = o
            }
        }
        val cbr = try {
            caps.encoderCapabilities.isBitrateModeSupported(MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR)
        } catch (_: Exception) {
            false
        }
        var erreur: Exception? = null
        for (complet in listOf(true, false)) {
            try {
                codec.setCallback(rappel, Handler(fil.looper))
                codec.configure(format(complet, cbr), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
                erreur = null
                break
            } catch (e: Exception) {
                // réglages fins refusés (profil, couleurs…) : on réessaie avec l'essentiel
                erreur = e
                try {
                    codec.reset()
                } catch (_: Exception) {
                }
            }
        }
        erreur?.let { throw it }
        codec.start()
    }

    /** La zone de la caméra à encoder : la zone demandée, centrée, à la taille de l'encodeur. */
    fun zone(z: Rect): Rect {
        val x = (z.left + (z.width() - largeur) / 2) and 1.inv()
        val y = (z.top + (z.height() - hauteur) / 2) and 1.inv()
        return Rect(x, y, x + largeur, y + hauteur)
    }

    /** Une image de la caméra vers l'encodeur (fil de la caméra). Faux si l'encodeur n'a pas de place : l'image est sautée. */
    fun encoder(img: ImageProxy, zone: Rect, tempsUs: Long): Boolean {
        if (ferme || panne != null) return false
        val index = libres.poll() ?: return false
        return try {
            val capacite = codec.getInputBuffer(index)?.capacity() ?: 0
            val dst = codec.getInputImage(index)
            if (dst == null || capacite <= 0) {
                panne = "image d’entrée indisponible"
                codec.queueInputBuffer(index, 0, 0, tempsUs, 0)
                return false
            }
            copier(img, zone, dst)
            codec.queueInputBuffer(index, 0, capacite, tempsUs, 0)
            true
        } catch (e: Exception) {
            panne = e.message ?: e.javaClass.simpleName
            false
        }
    }

    /** Débit (b/s) changé à la volée : il baisse quand le Wi-Fi faiblit, puis remonte. */
    fun reglerDebit(b: Int) {
        if (ferme || b == debit) return
        debit = b
        try {
            codec.setParameters(Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, b) })
        } catch (_: Exception) {
        }
    }

    /** Image clé tout de suite (le PC démarre ou a perdu le fil). */
    fun demanderCle() {
        if (ferme) return
        try {
            codec.setParameters(Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0) })
        } catch (_: Exception) {
        }
    }

    fun fermer() {
        if (ferme) return
        ferme = true
        try {
            codec.stop()
        } catch (_: Exception) {
        }
        try {
            codec.release()
        } catch (_: Exception) {
        }
        fil.quitSafely()
        libres.clear()
    }

    /** YUV_420_888 de la caméra (zone [z]) → image d'entrée de l'encodeur (I420, NV12 ou NV21), rangée par rangée. */
    private fun copier(src: ImageProxy, z: Rect, dst: Image) {
        val w = z.width()
        val h = z.height()
        // luminance : chaque rangée d'un bloc (pas de pixel de 1, garanti pour Y)
        val sy = src.planes[0]
        val dy = dst.planes[0]
        val sb = sy.buffer
        val db = dy.buffer
        if (ligne.size < w) ligne = ByteArray(w)
        for (r in 0 until h) {
            sb.position((z.top + r) * sy.rowStride + z.left)
            sb.get(ligne, 0, w)
            db.position(r * dy.rowStride)
            db.put(ligne, 0, w)
        }
        // chrominance (moitié de la taille)
        val cw = w / 2
        val ch = h / 2
        val cx = z.left / 2
        val cy = z.top / 2
        val su = src.planes[1]
        val sv = src.planes[2]
        val du = dst.planes[1]
        val dv = dst.planes[2]
        val sup = su.pixelStride
        val svp = sv.pixelStride
        val ub = su.buffer
        val vb = sv.buffer
        val dub = du.buffer
        val dvb = dv.buffer
        val dup = du.pixelStride
        val dvp = dv.pixelStride
        val duRow = du.rowStride
        val dvRow = dv.rowStride
        if (ordre < 0) ordre = sonder(du, dv)
        val longU = (cw - 1) * sup + 1
        val longV = (cw - 1) * svp + 1
        if (ligneU.size < longU) ligneU = ByteArray(longU)
        if (ligneV.size < longV) ligneV = ByteArray(longV)
        if (melange.size < 2 * cw) melange = ByteArray(2 * cw)
        for (r in 0 until ch) {
            ub.position((cy + r) * su.rowStride + cx * sup)
            ub.get(ligneU, 0, minOf(longU, ub.remaining()))
            vb.position((cy + r) * sv.rowStride + cx * svp)
            vb.get(ligneV, 0, minOf(longV, vb.remaining()))
            if (dup == 1 && dvp == 1) {
                // plans séparés (I420)
                for (c in 0 until cw) {
                    melange[c] = ligneU[c * sup]
                    melange[cw + c] = ligneV[c * svp]
                }
                dub.position(r * duRow)
                dub.put(melange, 0, cw)
                dvb.position(r * dvRow)
                dvb.put(melange, cw, cw)
            } else if (ordre == 1 || ordre == 2) {
                // semi-planaire : la rangée entrelacée écrite d'un bloc par le premier plan en mémoire
                val nv12 = ordre == 1
                var k = 0
                for (c in 0 until cw) {
                    val u = ligneU[c * sup]
                    val v = ligneV[c * svp]
                    melange[k++] = if (nv12) u else v
                    melange[k++] = if (nv12) v else u
                }
                val premier = if (nv12) dub else dvb
                val debut = r * (if (nv12) duRow else dvRow)
                val n = minOf(2 * cw, premier.limit() - debut)
                premier.position(debut)
                premier.put(melange, 0, n)
                if (n < 2 * cw) {
                    // dernière rangée : son dernier octet n'est visible que par l'autre plan
                    val autre = if (nv12) dvb else dub
                    autre.put(r * (if (nv12) dvRow else duRow) + (cw - 1) * 2, melange[2 * cw - 1])
                }
            } else {
                for (c in 0 until cw) {
                    dub.put(r * duRow + c * dup, ligneU[c * sup])
                    dvb.put(r * dvRow + c * dvp, ligneV[c * svp])
                }
            }
        }
    }

    /** Ordre des plans U et V entrelacés de l'encodeur, sondé en écrivant dans l'un et en relisant l'autre (avant d'y copier l'image). */
    private fun sonder(du: Image.Plane, dv: Image.Plane): Int {
        if (du.pixelStride != 2 || dv.pixelStride != 2) return 0
        val u = du.buffer
        val v = dv.buffer
        if (u.limit() < 2 || v.limit() < 2) return 0
        u.put(1, 0x15)
        v.put(0, 0x2A)
        if (u.get(1) == 0x2A.toByte()) return 1
        v.put(1, 0x15)
        u.put(0, 0x2A)
        if (v.get(1) == 0x2A.toByte()) return 2
        return 0
    }
}
