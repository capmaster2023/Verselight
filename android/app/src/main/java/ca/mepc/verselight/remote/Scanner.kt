package ca.mepc.verselight.remote

import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.atomic.AtomicBoolean

/** Viseur + ML Kit : lit le code QR « verselight://pair?… » affiché par MyChurch sur le PC. Remplit son cadre. */
@Composable
fun QrScanner(onCode: (String) -> Unit, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val done = remember { AtomicBoolean(false) }
    val scanner = remember {
        BarcodeScanning.getClient(BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build())
    }
    val preview = remember { Preview.Builder().build() }
    val analysis = remember {
        ImageAnalysis.Builder().setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
    }
    DisposableEffect(Unit) {
        onDispose {
            val f = ProcessCameraProvider.getInstance(context)
            f.addListener({ f.get().unbind(preview, analysis) }, ContextCompat.getMainExecutor(context))
            scanner.close()
        }
    }
    AndroidView(modifier = modifier, factory = { c ->
        val view = PreviewView(c).apply { scaleType = PreviewView.ScaleType.FILL_CENTER }
        val future = ProcessCameraProvider.getInstance(c)
        future.addListener({
            val provider = future.get()
            preview.setSurfaceProvider(view.surfaceProvider)
            analysis.setAnalyzer(ContextCompat.getMainExecutor(c)) { proxy ->
                analyze(proxy, scanner) { code -> if (done.compareAndSet(false, true)) onCode(code) }
            }
            try {
                provider.unbind(preview, analysis)
                provider.bindToLifecycle(c as LifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (e: Exception) {
                Bulles.dire("Caméra indisponible : ${e.message ?: e.javaClass.simpleName}")
            }
        }, ContextCompat.getMainExecutor(c))
        view
    })
}

@androidx.annotation.OptIn(ExperimentalGetImage::class)
private fun analyze(proxy: ImageProxy, scanner: BarcodeScanner, onCode: (String) -> Unit) {
    val media = proxy.image
    if (media == null) {
        proxy.close()
        return
    }
    val image = InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
    scanner.process(image)
        .addOnSuccessListener { codes ->
            codes.firstOrNull { it.rawValue?.startsWith("verselight://") == true }?.rawValue?.let(onCode)
        }
        .addOnCompleteListener { proxy.close() }
}
