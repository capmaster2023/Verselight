package ca.mepc.verselight.remote

import android.Manifest
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

private var shared: Remote? = null

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
        remote = shared ?: Remote(applicationContext, Store(applicationContext)).also { shared = it }
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
        remote.start()
    }

    override fun onStop() {
        super.onStop()
        if (!isChangingConfigurations) remote.stop()
    }

    // boutons de volume : + = Suivant, − = Précédent
    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeKeys.value && remote.link.value == Link.CONNECTED) {
            when (keyCode) {
                KeyEvent.KEYCODE_VOLUME_UP -> { if (event.repeatCount == 0) remote.send("next"); return true }
                KeyEvent.KEYCODE_VOLUME_DOWN -> { if (event.repeatCount == 0) remote.send("prev"); return true }
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        if (volumeKeys.value && remote.link.value == Link.CONNECTED &&
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
    var tab by rememberSaveable { mutableStateOf(0) }
    var settings by remember { mutableStateOf(false) }
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
            if (tab == 0) BibleTab(remote, link) else SongsTab(remote, link)
        }
        LiveBar(remote, link)
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
                    Text("PC : $pc", color = Brume, fontSize = 13.sp)
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
private fun LiveBar(remote: Remote, link: Link) {
    val state by remote.screen.collectAsState()
    val haptic = LocalHapticFeedback.current
    val connected = link == Link.CONNECTED
    val (line, next) = when {
        !connected -> (if (link == Link.CONNECTING) "Connexion au PC…" else "Hors ligne · recherche du PC…") to ""
        state == null -> "Connecté" to ""
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
            (if (s.optBoolean("ecranOuvert", true)) now else "$now (écran fermé)") to s.optString("suivant")
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
private fun BibleTab(remote: Remote, link: Link) {
    val focus = LocalFocusManager.current
    val haptic = LocalHapticFeedback.current
    var q by rememberSaveable { mutableStateOf("") }
    var preview by remember { mutableStateOf<JSONObject?>(null) }
    var error by remember { mutableStateOf("") }
    val connected = link == Link.CONNECTED

    LaunchedEffect(q, connected) {
        if (q.isBlank()) { preview = null; error = ""; return@LaunchedEffect }
        delay(350)
        remote.send("bible.preview", "ref" to q)
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "preview" -> { preview = e; error = "" }
                "error" -> if (e.optString("message").startsWith("Référence")) { preview = null; error = e.optString("message") }
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
                error.isNotEmpty() -> Text(error, color = Brume, fontSize = 16.sp)
                else -> Text("Tapez une référence : « mt 3 2 », « jn 3 16-18 », « ps 23 ».", color = Brume, fontSize = 16.sp)
            }
        }
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
private fun SongsTab(remote: Remote, link: Link) {
    val focus = LocalFocusManager.current
    val state by remote.screen.collectAsState()
    var q by rememberSaveable { mutableStateOf("") }
    var results by remember { mutableStateOf(listOf<JSONObject>()) }
    var song by remember { mutableStateOf<JSONObject?>(null) }
    val connected = link == Link.CONNECTED

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
                Text(if (connected) "Aucun chant trouvé." else "En attente du PC…", color = Brume, modifier = Modifier.padding(16.dp))
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
        Row(Modifier.fillMaxWidth().padding(bottom = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Button(
                onClick = { showSection(refrain) }, enabled = connected && refrain >= 0,
                modifier = Modifier.weight(1f).height(60.dp),
            ) { Text("Refrain", fontSize = 18.sp, fontWeight = FontWeight.Bold) }
            ClearButton(remote, connected, Modifier.weight(1f).height(60.dp))
        }
    }
}
