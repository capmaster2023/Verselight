package ca.mepc.verselight.remote

import android.Manifest
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.KeyEvent
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import kotlinx.coroutines.delay
import org.json.JSONObject

private val Encre = Color(0xFF0A1028)
private val Nuit = Color(0xFF10183A)
private val Nuit2 = Color(0xFF172252)
private val Or = Color(0xFFE4AF41)
private val Or2 = Color(0xFFF6D68A)
private val Blanc = Color(0xFFF7F4EC)
private val Brume = Color(0xFF8E99C7)
private val Rouge = Color(0xFFFF5A6A)
private val Vert = Color(0xFF2BD67B)

class MainActivity : ComponentActivity() {
    private lateinit var remote: Remote
    private val keepOn = mutableStateOf(true)
    private val volumeKeys = mutableStateOf(true)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        remote = MyVerse.remote(applicationContext)
        Ecoute.canaux(applicationContext)
        keepOn.value = remote.store.keepScreenOn
        volumeKeys.value = remote.store.volumeKeys
        setContent {
            MaterialTheme(
                colorScheme = darkColorScheme(
                    primary = Or, onPrimary = Color(0xFF2A1A00), secondary = Or2,
                    background = Encre, surface = Nuit, onSurface = Blanc, onBackground = Blanc,
                    surfaceVariant = Nuit2, onSurfaceVariant = Brume,
                )
            ) {
                LaunchedEffect(keepOn.value) {
                    if (keepOn.value) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                }
                Root(remote, keepOn.value, { keepOn.value = it; remote.store.keepScreenOn = it },
                    volumeKeys.value, { volumeKeys.value = it; remote.store.volumeKeys = it })
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
        // le détecteur de versets tourne : on reste connecté pour afficher depuis la notification
        if (!isChangingConfigurations && !Detection.actif.value) remote.stop()
    }

    // boutons de volume : + = Suivant, − = Précédent
    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeKeys.value && remote.pret()) {
            when (keyCode) {
                KeyEvent.KEYCODE_VOLUME_UP -> { if (event.repeatCount == 0) remote.send("next"); return true }
                KeyEvent.KEYCODE_VOLUME_DOWN -> { if (event.repeatCount == 0) remote.send("prev"); return true }
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeKeys.value && remote.pret() &&
            (keyCode == KeyEvent.KEYCODE_VOLUME_UP || keyCode == KeyEvent.KEYCODE_VOLUME_DOWN)
        ) return true
        return super.onKeyUp(keyCode, event)
    }
}

@Composable
private fun Root(
    remote: Remote,
    keepOn: Boolean, setKeepOn: (Boolean) -> Unit,
    volume: Boolean, setVolume: (Boolean) -> Unit,
) {
    val link by remote.link.collectAsState()
    Box(Modifier.fillMaxSize().background(Encre).safeDrawingPadding()) {
        if (link == Link.NOT_PAIRED || link == Link.PAIRING) PairScreen(remote, link == Link.PAIRING)
        else MainScreen(remote, link, keepOn, setKeepOn, volume, setVolume)
    }
}

// -------------------------------------------------------------------- jumelage

@Composable
private fun PairScreen(remote: Remote, busy: Boolean) {
    val context = LocalContext.current
    val info by remote.info.collectAsState()
    var scanning by remember { mutableStateOf(false) }
    var showLog by remember { mutableStateOf(false) }
    if (showLog) JournalDialog(remote) { showLog = false }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok ->
        if (ok) scanning = true else Toast.makeText(context, "La caméra est nécessaire pour scanner le code.", Toast.LENGTH_LONG).show()
    }
    if (scanning) {
        QrScanner(
            onCode = { code ->
                scanning = false
                if (!remote.pair(code)) Toast.makeText(context, "Ce code QR n'est pas un code VerseLight.", Toast.LENGTH_LONG).show()
            },
            onCancel = { scanning = false },
        )
        return
    }
    Column(
        Modifier.fillMaxSize().padding(28.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("MyVerse", color = Or, fontSize = 34.sp, fontWeight = FontWeight.Bold)
        Text("Télécommande", color = Brume, fontSize = 18.sp)
        Spacer(Modifier.height(32.dp))
        Text(
            "Sur le PC : MyVerse › bouton « Téléphone ». Scannez le code une seule fois ; ensuite l'app se reconnecte toute seule.",
            color = Blanc, fontSize = 17.sp,
        )
        if (info.isNotEmpty()) {
            Spacer(Modifier.height(16.dp))
            Text(info, color = Rouge, fontSize = 16.sp)
        }
        Spacer(Modifier.height(32.dp))
        if (busy) {
            CircularProgressIndicator(color = Or)
            Spacer(Modifier.height(12.dp))
            Text("Jumelage en cours…", color = Brume)
        } else {
            Button(
                onClick = {
                    if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) scanning = true
                    else permission.launch(Manifest.permission.CAMERA)
                },
                modifier = Modifier.fillMaxWidth().height(64.dp),
            ) { Text("Scanner le code QR", fontSize = 19.sp, fontWeight = FontWeight.Bold) }
        }
        Spacer(Modifier.height(16.dp))
        TextButton(onClick = { showLog = true }) { Text("Journal d'erreurs", color = Brume) }
    }
}

// -------------------------------------------------------------------- écran principal

@Composable
private fun MainScreen(
    remote: Remote, link: Link,
    keepOn: Boolean, setKeepOn: (Boolean) -> Unit,
    volume: Boolean, setVolume: (Boolean) -> Unit,
) {
    val context = LocalContext.current
    val pc by remote.pcName.collectAsState()
    val update by remote.update.collectAsState()
    val directEtat by remote.direct.etat.collectAsState()
    val pret = link == Link.CONNECTED || directEtat == Direct.Etat.PRET
    var tab by rememberSaveable { mutableStateOf(0) }
    var settings by remember { mutableStateOf(false) }
    var showLog by remember { mutableStateOf(false) }
    if (showLog) JournalDialog(remote) { showLog = false }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            if (e.optString("t") == "error") Toast.makeText(context, e.optString("message"), Toast.LENGTH_SHORT).show()
        }
    }
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 4.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("MyVerse", color = Or, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.width(10.dp))
            Text(pc, color = Brume, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            TextButton(onClick = { settings = true }) { Text("Réglages", color = Or2) }
        }
        update?.let { (version, url) ->
            Button(
                onClick = {
                    try {
                        context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                    } catch (e: Exception) {
                        Toast.makeText(context, "Aucun navigateur pour télécharger la mise à jour.", Toast.LENGTH_LONG).show()
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            ) { Text("Mise à jour $version disponible : télécharger", fontWeight = FontWeight.Bold) }
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("Bible", "Chants").forEachIndexed { i, label ->
                val on = tab == i
                Button(
                    onClick = { tab = i },
                    modifier = Modifier.weight(1f).height(46.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = if (on) Nuit2 else Nuit, contentColor = if (on) Or2 else Brume),
                ) { Text(label, fontSize = 17.sp, fontWeight = FontWeight.Bold) }
            }
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (tab == 0) BibleTab(remote, pret) else SongsTab(remote, pret)
        }
        LiveBar(remote, link, directEtat)
    }
    if (settings) {
        AlertDialog(
            onDismissRequest = { settings = false },
            confirmButton = { TextButton(onClick = { settings = false }) { Text("Fermer") } },
            dismissButton = {
                TextButton(onClick = { settings = false; remote.forget() }) { Text("Oublier ce PC", color = Rouge) }
            },
            title = { Text("Réglages") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    SettingRow("Boutons de volume : + Suivant, − Précédent", volume, setVolume)
                    SettingRow("Garder l'écran allumé", keepOn, setKeepOn)
                    var direct by remember { mutableStateOf(remote.store.obsDirect) }
                    SettingRow("Piloter OBS quand MyVerse est fermé sur le PC", direct) {
                        direct = it
                        remote.store.obsDirect = it
                        if (!it) remote.direct.pcPresent() else if (link != Link.CONNECTED) { remote.direct.reglagesChanges(); remote.direct.pcAbsent() }
                    }
                    val directMsg by remote.direct.message.collectAsState()
                    Text("OBS direct : " + when {
                        link == Link.CONNECTED -> "inutile pour l'instant (MyVerse est ouvert sur le PC et pilote OBS)"
                        directMsg.isNotBlank() -> directMsg
                        else -> "en attente"
                    }, color = Brume, fontSize = 13.sp)
                    var bip by remember { mutableStateOf(remote.store.couperBip) }
                    SettingRow("Détecteur de versets : couper le bip du micro", bip) { bip = it; remote.store.couperBip = it }
                    Text("PC : $pc", color = Brume, fontSize = 13.sp)
                    TextButton(onClick = { settings = false; showLog = true }) { Text("Journal d'erreurs", color = Or2) }
                }
            },
        )
    }
}

@Composable
private fun SettingRow(label: String, value: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f), fontSize = 15.sp)
        Switch(checked = value, onCheckedChange = onChange)
    }
}

/** Barre fixe : connexion, ce qui est à l'écran, Précédent / Suivant. */
@Composable
private fun LiveBar(remote: Remote, link: Link, direct: Direct.Etat) {
    val state by remote.screen.collectAsState()
    val haptic = LocalHapticFeedback.current
    val enDirect = link != Link.CONNECTED && direct == Direct.Etat.PRET
    val connected = link == Link.CONNECTED || enDirect
    val (line, next) = when {
        !connected -> (if (link == Link.CONNECTING) "Connexion au PC…" else if (direct == Direct.Etat.CONNEXION) "PC introuvable · connexion directe à OBS…" else "Hors ligne · recherche du PC…") to ""
        state == null -> (if (enDirect) "OBS direct (MyVerse fermé sur le PC)" else "Connecté") to ""
        else -> {
            val s = state!!
            val mode = s.optString("mode")
            val lignes = s.optJSONArray("lignes")
            val first = if (lignes != null && lignes.length() > 0) lignes.optString(0).lineSequence().first() else ""
            val now = when (mode) {
                "verset" -> s.optString("ref")
                "chant" -> listOf(s.optString("titre"), first).filter { it.isNotBlank() }.joinToString(" · ")
                else -> "Écran vide"
            }
            val ligne = if (s.optBoolean("ecranOuvert", true)) now else "$now (écran fermé)"
            (if (enDirect) "$ligne · OBS direct" else ligne) to s.optString("suivant")
        }
    }
    Row(
        Modifier.fillMaxWidth().background(Nuit).padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(12.dp).background(if (connected) Vert else if (link == Link.CONNECTING) Or else Rouge, CircleShape))
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Text(line, color = Blanc, fontSize = 15.sp, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (next.isNotBlank()) Text("Suivant : $next", color = Brume, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Spacer(Modifier.width(8.dp))
        OutlinedButton(
            onClick = { haptic.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("prev") },
            enabled = connected, modifier = Modifier.height(58.dp),
        ) { Text("‹", fontSize = 26.sp) }
        Spacer(Modifier.width(8.dp))
        Button(
            onClick = { haptic.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("next") },
            enabled = connected, modifier = Modifier.height(58.dp),
        ) { Text("Suivant ›", fontSize = 17.sp, fontWeight = FontWeight.Bold) }
    }
}

// -------------------------------------------------------------------- Bible

@Composable
private fun BibleTab(remote: Remote, connected: Boolean) {
    val focus = LocalFocusManager.current
    val haptic = LocalHapticFeedback.current
    var q by rememberSaveable { mutableStateOf("") }
    var preview by remember { mutableStateOf<JSONObject?>(null) }
    var results by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
    var error by remember { mutableStateOf("") }
    val state by remote.screen.collectAsState()

    LaunchedEffect(q, connected) {
        if (q.isBlank()) { preview = null; results = emptyList(); error = ""; return@LaunchedEffect }
        delay(350)
        remote.send("bible.preview", "ref" to q)
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "preview" -> { preview = e; results = emptyList(); error = "" }
                "error" -> if (e.optString("message").startsWith("Référence")) {
                    preview = null
                    val mots = q.trim()
                    // pas une référence : recherche par mots (« charité patiente »)
                    if (mots.length >= 3 && mots.none { it.isDigit() }) {
                        error = "Recherche de « $mots »…"
                        remote.send("bible.search", "q" to mots)
                    } else {
                        results = emptyList()
                        error = e.optString("message")
                    }
                }
                "search" -> if (e.optString("q") == q.trim()) {
                    val a = e.optJSONArray("liste")
                    results = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
                    error = if (results.isEmpty()) "Aucun verset trouvé pour « ${q.trim()} »." else ""
                }
            }
        }
    }
    fun show() {
        if (q.isBlank()) return
        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
        focus.clearFocus()
        remote.send("bible.show", "ref" to q)
    }

    Column(Modifier.fillMaxSize().padding(horizontal = 12.dp)) {
        OutlinedTextField(
            value = q, onValueChange = { q = it },
            modifier = Modifier.fillMaxWidth(),
            placeholder = { Text("jn 3 16", fontSize = 24.sp, color = Brume) },
            textStyle = MaterialTheme.typography.headlineSmall,
            singleLine = true,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.None, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { show() }),
            trailingIcon = { if (q.isNotEmpty()) TextButton(onClick = { q = "" }) { Text("✕", color = Brume) } },
        )
        DetecteurBar(remote, connected)
        val suggestions = Books.suggest(q)
        if (suggestions.isNotEmpty()) {
            LazyRow(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                items(suggestions) { name ->
                    Text(
                        name, color = Or2, fontSize = 15.sp,
                        modifier = Modifier.background(Nuit2, RoundedCornerShape(10.dp)).clickable { q = "$name " }
                            .padding(horizontal = 12.dp, vertical = 8.dp),
                    )
                }
            }
        }
        Box(
            Modifier.weight(1f).fillMaxWidth().padding(vertical = 10.dp)
                .background(Nuit, RoundedCornerShape(14.dp)).padding(14.dp)
        ) {
            val p = preview
            when {
                p != null -> LazyColumn {
                    item { Text(p.optString("ref"), color = Or, fontWeight = FontWeight.Bold, fontSize = 17.sp, modifier = Modifier.padding(bottom = 8.dp)) }
                    val vs = p.optJSONArray("versets")
                    items(vs?.length() ?: 0) { i ->
                        val v = vs!!.getJSONObject(i)
                        Row(Modifier.padding(vertical = 4.dp)) {
                            Text(v.optInt("v").toString(), color = Brume, fontSize = 13.sp, modifier = Modifier.width(30.dp).padding(top = 3.dp))
                            Text(v.optString("t"), color = Blanc, fontSize = 18.sp, lineHeight = 26.sp)
                        }
                    }
                }
                results.isNotEmpty() -> LazyColumn {
                    item { Text("${results.size} verset(s) · touchez pour l'ouvrir", color = Brume, fontSize = 13.sp, modifier = Modifier.padding(bottom = 6.dp)) }
                    items(results) { r ->
                        Column(Modifier.fillMaxWidth().clickable { q = r.optString("ref") }.padding(vertical = 8.dp)) {
                            val version = r.optString("v")
                            Text(r.optString("ref") + if (version.isNotBlank()) "  · $version" else "", color = Or, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                            Text(r.optString("text"), color = Blanc, fontSize = 15.sp, maxLines = 3, overflow = TextOverflow.Ellipsis)
                        }
                    }
                }
                error.isNotEmpty() -> Text(error, color = Brume, fontSize = 16.sp)
                else -> Text("Tapez une référence (« mt 3 2 », « jn 3 16-18 », « ps 23 ») ou des mots (« charité patiente »).", color = Brume, fontSize = 16.sp)
            }
        }
        NextCard(state, connected)
        Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(
                onClick = { show() }, enabled = connected && q.isNotBlank(),
                modifier = Modifier.weight(2f).height(68.dp),
            ) { Text("Afficher", fontSize = 21.sp, fontWeight = FontWeight.Bold) }
            ClearButton(remote, connected, Modifier.weight(1f).height(68.dp))
        }
    }
}

@Composable
private fun ClearButton(remote: Remote, enabled: Boolean, modifier: Modifier) {
    val haptic = LocalHapticFeedback.current
    Button(
        onClick = { haptic.performHapticFeedback(HapticFeedbackType.LongPress); remote.send("clear") },
        enabled = enabled, modifier = modifier,
        colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4A1720), contentColor = Color(0xFFFFC2C8)),
    ) { Text("Retirer", fontSize = 18.sp, fontWeight = FontWeight.Bold) }
}

// -------------------------------------------------------------------- Chants

@Composable
private fun SongsTab(remote: Remote, connected: Boolean) {
    val focus = LocalFocusManager.current
    val state by remote.screen.collectAsState()
    var q by rememberSaveable { mutableStateOf("") }
    var results by remember { mutableStateOf(listOf<JSONObject>()) }
    var song by remember { mutableStateOf<JSONObject?>(null) }

    LaunchedEffect(q, connected) {
        delay(300)
        remote.send("song.search", "q" to q)
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "songs" -> {
                    val a = e.optJSONArray("liste")
                    results = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
                }
                "song" -> song = e
            }
        }
    }

    val s = song
    if (s != null) {
        SongView(remote, s, state, connected) { song = null }
        return
    }
    Column(Modifier.fillMaxSize().padding(horizontal = 12.dp)) {
        OutlinedTextField(
            value = q, onValueChange = { q = it }, modifier = Modifier.fillMaxWidth(),
            placeholder = { Text("Titre, numéro ou paroles", color = Brume) }, singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { focus.clearFocus() }),
        )
        LazyColumn(Modifier.weight(1f).padding(top = 8.dp)) {
            items(results) { r ->
                Column(
                    Modifier.fillMaxWidth().clickable { remote.send("song.open", "id" to r.optString("id")) }
                        .padding(vertical = 12.dp, horizontal = 6.dp)
                ) {
                    Text(r.optString("title"), color = Blanc, fontSize = 17.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    if (r.optString("author").isNotBlank()) Text(r.optString("author"), color = Brume, fontSize = 13.sp)
                }
            }
            if (results.isEmpty()) item {
                Text(if (connected) "Aucun chant trouvé." else "En attente du PC (ou d'OBS)…", color = Brume, modifier = Modifier.padding(16.dp))
            }
        }
    }
}

@Composable
private fun SongView(remote: Remote, song: JSONObject, state: JSONObject?, connected: Boolean, onBack: () -> Unit) {
    val haptic = LocalHapticFeedback.current
    val id = song.optString("id")
    val a = song.optJSONArray("sections")
    val sections = if (a == null) emptyList() else (0 until a.length()).map { a.getJSONObject(it) }
    val live = state?.optJSONObject("chant")
    val current = if (live != null && live.optString("id") == id && state?.optString("mode") == "chant") live.optInt("i", -1) else -1
    val refrain = sections.indexOfFirst { Books.norm(it.optString("label")).let { l -> l.startsWith("refrain") || l.startsWith("choeur") || l.startsWith("chorus") } }
    fun showSection(i: Int) {
        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
        remote.send("song.show", "id" to id, "i" to i)
    }
    Column(Modifier.fillMaxSize().padding(horizontal = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text("‹ Liste", color = Or2, fontSize = 16.sp) }
            Text(song.optString("title"), color = Or, fontSize = 18.sp, fontWeight = FontWeight.Bold, maxLines = 2,
                overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        }
        LazyVerticalGrid(
            columns = GridCells.Fixed(2), modifier = Modifier.weight(1f).padding(vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            itemsIndexed(sections) { i, sec ->
                val on = i == current
                Column(
                    Modifier.fillMaxWidth().height(130.dp)
                        .background(if (on) Color(0xFF3A3010) else Nuit, RoundedCornerShape(12.dp))
                        .border(2.dp, if (on) Or else Nuit2, RoundedCornerShape(12.dp))
                        .clickable(enabled = connected) { showSection(i) }
                        .padding(10.dp)
                ) {
                    Text(sec.optString("label").ifBlank { "Strophe ${i + 1}" }, color = Or, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                    Text(sec.optString("text"), color = Blanc, fontSize = 14.sp, maxLines = 5, overflow = TextOverflow.Ellipsis)
                }
            }
        }
        NextCard(state, connected)
        Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(
                onClick = { showSection(refrain) }, enabled = connected && refrain >= 0,
                modifier = Modifier.weight(1f).height(60.dp),
            ) { Text("Refrain", fontSize = 18.sp, fontWeight = FontWeight.Bold) }
            ClearButton(remote, connected, Modifier.weight(1f).height(60.dp))
        }
    }
}

/** Journal d'erreurs : connexions, adresses essayées, refus, pertes de Wi-Fi. Bouton Copier pour l'envoyer. */
@Composable
private fun JournalDialog(remote: Remote, onClose: () -> Unit) {
    val context = LocalContext.current
    val lines by remote.journal.collectAsState()
    val text = remember(lines) { remote.journalText() }
    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Journal d'erreurs") },
        text = {
            LazyColumn(Modifier.fillMaxWidth().height(380.dp)) {
                items(text.lines()) { l ->
                    Text(l, fontFamily = FontFamily.Monospace, fontSize = 11.sp, lineHeight = 15.sp, color = if (l.contains("refusé") || l.contains("introuvable") || l.contains("erreur") || l.contains("perdu")) Rouge else Blanc)
                }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val cm = context.getSystemService(ClipboardManager::class.java)
                cm?.setPrimaryClip(ClipData.newPlainText("Journal MyVerse", text))
                Toast.makeText(context, "Journal copié : collez-le dans votre message.", Toast.LENGTH_LONG).show()
            }) { Text("Copier") }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Fermer") } },
    )
}

/** La diapositive qui viendra avec « Suivant » : verset suivant (ou morceau) ou couplet suivant, en entier. */
@Composable
private fun NextCard(state: JSONObject?, connected: Boolean) {
    val label = state?.optString("suivantLabel").orEmpty()
    val texte = state?.optString("suivantTexte").orEmpty()
    val mode = state?.optString("mode").orEmpty()
    if (!connected || texte.isBlank() || (mode != "verset" && mode != "chant")) return
    Column(
        Modifier.fillMaxWidth().padding(bottom = 8.dp)
            .background(Nuit2, RoundedCornerShape(12.dp)).padding(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Text("Suivant · $label", color = Or, fontSize = 13.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(texte, color = Blanc, fontSize = 15.sp, lineHeight = 21.sp, maxLines = 4, overflow = TextOverflow.Ellipsis)
    }
}

/** Détecteur de versets : écoute le prédicateur et propose les versets cités (bouton Afficher). */
@Composable
private fun DetecteurBar(remote: Remote, connected: Boolean) {
    val context = LocalContext.current
    val haptic = LocalHapticFeedback.current
    val actif by Detection.actif.collectAsState()
    val statut by Detection.statut.collectAsState()
    val entendu by Detection.entendu.collectAsState()
    val suggestions by Detection.suggestions.collectAsState()
    val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { res ->
        if (res[Manifest.permission.RECORD_AUDIO] == true || ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) Ecoute.demarrer(context)
        else Toast.makeText(context, "Le micro est nécessaire pour détecter les versets cités.", Toast.LENGTH_LONG).show()
    }
    fun basculer(on: Boolean) {
        if (!on) { Ecoute.arreter(context); return }
        val manque = ArrayList<String>()
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) manque.add(Manifest.permission.RECORD_AUDIO)
        if (android.os.Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) manque.add(Manifest.permission.POST_NOTIFICATIONS)
        if (manque.isEmpty()) Ecoute.demarrer(context) else permissions.launch(manque.toTypedArray())
    }
    Column(Modifier.fillMaxWidth().padding(top = 8.dp).background(Nuit, RoundedCornerShape(12.dp)).padding(horizontal = 12.dp, vertical = 6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("Détecteur de versets", color = Or2, fontSize = 15.sp, fontWeight = FontWeight.Bold)
                val info = when {
                    actif && entendu.isNotBlank() -> "« …${entendu.takeLast(70)} »"
                    actif -> statut.ifBlank { "À l'écoute…" }
                    statut.isNotBlank() -> statut
                    else -> "Écoute le prédicateur et propose les versets cités"
                }
                Text(info, color = Brume, fontSize = 12.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
            Switch(checked = actif, onCheckedChange = { basculer(it) })
        }
        suggestions.take(2).forEach { s ->
            Row(
                Modifier.fillMaxWidth().padding(top = 6.dp).background(Nuit2, RoundedCornerShape(10.dp)).padding(start = 10.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    Text(s.label, color = Or, fontSize = 15.sp, fontWeight = FontWeight.Bold)
                    Text(s.texte, color = Blanc, fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                Button(
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        if (remote.send("bible.show", "ref" to s.label)) Detection.retirer(s.id)
                        else Toast.makeText(context, "Ni le PC ni OBS ne sont joignables.", Toast.LENGTH_SHORT).show()
                    },
                    enabled = connected,
                    modifier = Modifier.padding(start = 6.dp).height(44.dp),
                ) { Text("Afficher", fontWeight = FontWeight.Bold) }
                TextButton(onClick = { Detection.retirer(s.id) }) { Text("✕", color = Brume) }
            }
        }
    }
}
