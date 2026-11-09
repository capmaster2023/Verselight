package ca.mepc.verselight.remote

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** PC jumelé. [key] = secret dérivé (HMAC), gardé chiffré par une clé Android Keystore. */
data class Pairing(
    val pcId: String,
    val pcName: String,
    val key: ByteArray,
    val ips: List<String>,
    val port: Int,
    val lastHost: String?,
    val lastPort: Int,
)

class Store(context: Context) {
    private val prefs = context.getSharedPreferences("verselight", Context.MODE_PRIVATE)

    val deviceId: String
        get() = prefs.getString("deviceId", null) ?: UUID.randomUUID().toString().also {
            prefs.edit().putString("deviceId", it).apply()
        }

    var volumeKeys: Boolean
        get() = prefs.getBoolean("volumeKeys", true)
        set(v) = prefs.edit().putBoolean("volumeKeys", v).apply()

    var keepScreenOn: Boolean
        get() = prefs.getBoolean("keepScreenOn", true)
        set(v) = prefs.edit().putBoolean("keepScreenOn", v).apply()

    /** Détecteur de versets : couper le bip de relance de l'écoute. */
    var couperBip: Boolean
        get() = prefs.getBoolean("couperBip", true)
        set(v) = prefs.edit().putBoolean("couperBip", v).apply()

    /** Réglages de l'écran reçus du PC (JSON : principale, seconde, theme, taille, presentation). */
    var reglages: String
        get() = prefs.getString("reglages", "{}") ?: "{}"
        set(v) = prefs.edit().putString("reglages", v).apply()

    fun pairing(): Pairing? {
        val pcId = prefs.getString("pcId", null) ?: return null
        val key = decrypt(prefs.getString("key", null) ?: return null) ?: return null
        return Pairing(
            pcId = pcId,
            pcName = prefs.getString("pcName", "PC") ?: "PC",
            key = key,
            ips = (prefs.getString("ips", "") ?: "").split(",").filter { it.isNotBlank() },
            port = prefs.getInt("port", 47900),
            lastHost = prefs.getString("lastHost", null),
            lastPort = prefs.getInt("lastPort", 47900),
        )
    }

    fun save(p: Pairing) {
        prefs.edit()
            .putString("pcId", p.pcId)
            .putString("pcName", p.pcName)
            .putString("key", encrypt(p.key))
            .putString("ips", p.ips.joinToString(","))
            .putInt("port", p.port)
            .putString("lastHost", p.lastHost)
            .putInt("lastPort", p.lastPort)
            .apply()
    }

    fun setLast(host: String, port: Int) {
        prefs.edit().putString("lastHost", host).putInt("lastPort", port).apply()
    }

    fun forget() {
        prefs.edit().remove("pcId").remove("pcName").remove("key").remove("ips")
            .remove("port").remove("lastHost").remove("lastPort")
            .remove("obsActif").remove("obsPort").remove("obsMdp").remove("obsDirect").remove("reglages").remove("chantsStamp").apply()
    }

    // ---------------------------------------------------------------- Android Keystore (AES-GCM)

    private fun secretKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return gen.generateKey()
    }

    private fun encrypt(plain: ByteArray): String {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, secretKey())
        return Base64.encodeToString(c.iv + c.doFinal(plain), Base64.NO_WRAP)
    }

    private fun decrypt(s: String): ByteArray? = try {
        val all = Base64.decode(s, Base64.NO_WRAP)
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(128, all.copyOfRange(0, 12)))
        c.doFinal(all.copyOfRange(12, all.size))
    } catch (e: Exception) {
        null
    }

    private companion object {
        const val ALIAS = "verselight-pairing"
    }
}
