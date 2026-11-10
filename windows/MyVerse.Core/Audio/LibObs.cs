using System.Reflection;
using System.Runtime.InteropServices;

namespace MyVerse.Audio;

/// <summary>
/// Appels directs à libobs, la bibliothèque d'OBS Studio (obs.dll, licence GNU GPL v2 ou ultérieure).
/// MyChurch n'a aucun mélange audio à lui : sources, filtres, écoute, pistes et vumètres sont ceux d'OBS.
/// Signatures relevées dans libobs 33.0.1 (obs.h, obs-audio-controls.h, media-io/audio-io.h).
/// </summary>
public static class LibObs
{
    const string Lib = "obs";
    public const int MaxAudioChannels = 8;
    public const int MaxAvPlanes = 8;
    public const int Mixes = 6;
    public const uint FlagForceMono = 1 << 1;

    static IntPtr handle;
    static bool resolver;

    /// <summary>Charge obs.dll (ou libobs.so) depuis le chemin donné ; ses dépendances sont cherchées dans le même dossier.</summary>
    public static void Load(string path)
    {
        if (handle != IntPtr.Zero) return;
        if (OperatingSystem.IsWindows() && Path.IsPathRooted(path))
            try { SetDllDirectoryW(Path.GetDirectoryName(path)!); } catch (Exception) { }
        handle = NativeLibrary.Load(path);
        if (!resolver)
        {
            resolver = true;
            NativeLibrary.SetDllImportResolver(typeof(LibObs).Assembly, (name, _, _) => name == Lib ? handle : IntPtr.Zero);
        }
    }
    public static bool Loaded => handle != IntPtr.Zero;
    public static bool Has(string export) => handle != IntPtr.Zero && NativeLibrary.TryGetExport(handle, export, out _);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectoryW(string path);

    public static string? Str(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

    // ---------------------------------------------------------------- cœur
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioInfo { public uint SamplesPerSec; public int Speakers; }
    public const int SpeakersStereo = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoInfo
    {
        public IntPtr GraphicsModule;
        public uint FpsNum, FpsDen, BaseWidth, BaseHeight, OutputWidth, OutputHeight;
        public int OutputFormat;
        public uint Adapter;
        [MarshalAs(UnmanagedType.U1)] public bool GpuConversion;
        public int Colorspace, Range, ScaleType;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogHandler(int level, IntPtr format, IntPtr args, IntPtr param);
    [DllImport(Lib)] public static extern void base_set_log_handler(LogHandler? handler, IntPtr param);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool obs_startup([MarshalAs(UnmanagedType.LPUTF8Str)] string locale, [MarshalAs(UnmanagedType.LPUTF8Str)] string? moduleConfigPath, IntPtr store);
    [DllImport(Lib)] public static extern void obs_shutdown();
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool obs_initialized();
    [DllImport(Lib)] public static extern IntPtr obs_get_version_string();
    [DllImport(Lib)] public static extern void obs_add_data_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool obs_reset_audio(ref AudioInfo info);
    [DllImport(Lib)] public static extern int obs_reset_video(ref VideoInfo info);
    [DllImport(Lib)] public static extern int obs_open_module(out IntPtr module, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? dataPath);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool obs_init_module(IntPtr module);
    [DllImport(Lib)] public static extern void obs_post_load_modules();
    [DllImport(Lib)] public static extern IntPtr obs_get_audio();
    [DllImport(Lib)] public static extern void obs_set_output_source(uint channel, IntPtr source);

    // ---------------------------------------------------------------- réglages (obs_data)
    [DllImport(Lib)] public static extern IntPtr obs_data_create();
    [DllImport(Lib)] public static extern void obs_data_release(IntPtr data);
    [DllImport(Lib)] public static extern void obs_data_set_string(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib)] public static extern void obs_data_set_int(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);
    [DllImport(Lib)] public static extern void obs_data_set_double(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, double value);
    [DllImport(Lib)] public static extern void obs_data_set_bool(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.U1)] bool value);

    // ---------------------------------------------------------------- sources et filtres
    [DllImport(Lib)] public static extern IntPtr obs_get_latest_input_type_id([MarshalAs(UnmanagedType.LPUTF8Str)] string unversionedId);
    [DllImport(Lib)] public static extern IntPtr obs_source_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
    [DllImport(Lib)] public static extern void obs_source_release(IntPtr source);
    [DllImport(Lib)] public static extern void obs_source_remove(IntPtr source);
    [DllImport(Lib)] public static extern void obs_source_update(IntPtr source, IntPtr settings);
    [DllImport(Lib)] public static extern void obs_source_set_volume(IntPtr source, float volume);
    [DllImport(Lib)] public static extern void obs_source_set_muted(IntPtr source, [MarshalAs(UnmanagedType.U1)] bool muted);
    [DllImport(Lib)] public static extern void obs_source_set_flags(IntPtr source, uint flags);
    [DllImport(Lib)] public static extern void obs_source_set_audio_mixers(IntPtr source, uint mixers);
    [DllImport(Lib)] public static extern void obs_source_set_enabled(IntPtr source, [MarshalAs(UnmanagedType.U1)] bool enabled);
    [DllImport(Lib)] public static extern void obs_source_filter_add(IntPtr source, IntPtr filter);
    [DllImport(Lib)] public static extern void obs_source_filter_remove(IntPtr source, IntPtr filter);
    [DllImport(Lib)] public static extern void obs_source_media_restart(IntPtr source);
    /// <summary>libobs 33 et suivantes.</summary>
    [DllImport(Lib)] public static extern void obs_source_set_monitoring_enabled(IntPtr source, [MarshalAs(UnmanagedType.U1)] bool enabled);
    /// <summary>libobs 32 et précédentes : 0 = aucune écoute, 2 = écoute et sortie.</summary>
    [DllImport(Lib)] public static extern void obs_source_set_monitoring_type(IntPtr source, int type);

    // ---------------------------------------------------------------- périphériques
    [DllImport(Lib)] public static extern IntPtr obs_get_source_properties([MarshalAs(UnmanagedType.LPUTF8Str)] string id);
    [DllImport(Lib)] public static extern void obs_properties_destroy(IntPtr props);
    [DllImport(Lib)] public static extern IntPtr obs_properties_get(IntPtr props, [MarshalAs(UnmanagedType.LPUTF8Str)] string property);
    [DllImport(Lib)] public static extern UIntPtr obs_property_list_item_count(IntPtr property);
    [DllImport(Lib)] public static extern IntPtr obs_property_list_item_name(IntPtr property, UIntPtr index);
    [DllImport(Lib)] public static extern IntPtr obs_property_list_item_string(IntPtr property, UIntPtr index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
    public delegate bool EnumAudioDevice(IntPtr data, IntPtr name, IntPtr id);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool obs_audio_monitoring_available();
    [DllImport(Lib)] public static extern void obs_enum_audio_monitoring_devices(EnumAudioDevice callback, IntPtr data);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool obs_set_audio_monitoring_device([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

    // ---------------------------------------------------------------- vumètres
    public const int FaderLog = 2;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void VolmeterUpdated(IntPtr param, IntPtr magnitude, IntPtr peak, IntPtr inputPeak);
    [DllImport(Lib)] public static extern IntPtr obs_volmeter_create(int faderType);
    [DllImport(Lib)] public static extern void obs_volmeter_destroy(IntPtr volmeter);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool obs_volmeter_attach_source(IntPtr volmeter, IntPtr source);
    [DllImport(Lib)] public static extern void obs_volmeter_detach_source(IntPtr volmeter);
    [DllImport(Lib)] public static extern void obs_volmeter_add_callback(IntPtr volmeter, VolmeterUpdated callback, IntPtr param);
    [DllImport(Lib)] public static extern void obs_volmeter_remove_callback(IntPtr volmeter, VolmeterUpdated callback, IntPtr param);

    // ---------------------------------------------------------------- pistes (sortie du mélange)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioOutputCallback(IntPtr param, UIntPtr mixIndex, IntPtr audioData);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool audio_output_connect(IntPtr audio, UIntPtr mixIndex, IntPtr conversion, AudioOutputCallback callback, IntPtr param);
    [DllImport(Lib)] public static extern void audio_output_disconnect(IntPtr audio, UIntPtr mixIndex, AudioOutputCallback callback, IntPtr param);

    // ---------------------------------------------------------------- texte du journal d'OBS
    [DllImport("msvcrt", EntryPoint = "_vsnprintf", CallingConvention = CallingConvention.Cdecl)]
    static extern int vsnprintf_win(byte[] buffer, UIntPtr size, IntPtr format, IntPtr args);
    [DllImport("libc", EntryPoint = "vsnprintf", CallingConvention = CallingConvention.Cdecl)]
    static extern int vsnprintf_unix(byte[] buffer, UIntPtr size, IntPtr format, IntPtr args);

    [ThreadStatic] static byte[]? logBuffer;
    /// <summary>Met en forme un message d'OBS (printf) ; ne lève jamais d'exception.</summary>
    public static string Format(IntPtr format, IntPtr args)
    {
        try
        {
            var buffer = logBuffer ??= new byte[2048];
            int n = OperatingSystem.IsWindows()
                ? vsnprintf_win(buffer, (UIntPtr)(buffer.Length - 1), format, args)
                : vsnprintf_unix(buffer, (UIntPtr)(buffer.Length - 1), format, args);
            if (n < 0 || n > buffer.Length - 1) n = buffer.Length - 1;
            return System.Text.Encoding.UTF8.GetString(buffer, 0, n);
        }
        catch (Exception) { return Str(format) ?? ""; }
    }
}
