using System.Runtime.InteropServices;
using MyVerse.Audio;
using NAudio.CoreAudioApi;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les périphériques audio de Windows (MyChurch 4.0) : relevés directement (WASAPI), sans attendre le moteur d'OBS,
/// pour voir tout de suite un micro ou une carte son qu'on branche ou qu'on débranche. Les identifiants sont ceux d'OBS.
/// </summary>
public static class PeripheriquesAudio
{
    /// <summary>Les entrées (micros, cartes son) et les sorties actives, avec leur nom Windows.</summary>
    public static (List<AudioDevice> Entrees, List<AudioDevice> Sorties) Lister()
    {
        using var e = new MMDeviceEnumerator();
        return (Liste(e, DataFlow.Capture), Liste(e, DataFlow.Render));
    }

    static List<AudioDevice> Liste(MMDeviceEnumerator e, DataFlow flux)
    {
        var liste = new List<AudioDevice>();
        foreach (var d in e.EnumerateAudioEndPoints(flux, DeviceState.Active))
        {
            try { liste.Add(new AudioDevice(d.ID, d.FriendlyName)); }
            catch (Exception) { }
            finally { d.Dispose(); }
        }
        return liste;
    }

    /// <summary>Mêmes périphériques (identifiants et noms) dans les deux listes.</summary>
    public static bool Memes(IReadOnlyList<AudioDevice> a, IReadOnlyList<AudioDevice> b) =>
        a.Count == b.Count && a.Select(d => d.Id + "\n" + d.Name).ToHashSet().SetEquals(b.Select(d => d.Id + "\n" + d.Name));
}

/// <summary>
/// « Désactiver l'atténuation audio de Windows » d'OBS : pendant un appel (Teams, Zoom…), Windows baisse le son des autres
/// programmes. MyChurch se retire de cette atténuation comme OBS (IAudioSessionControl2::SetDuckingPreference de sa session).
/// </summary>
public static class AttenuationWindows
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumerateur
    {
        void EnumAudioEndpoints();
        [PreserveSig] int GetDefaultAudioEndpoint(int flux, int role, out IPeripherique peripherique);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPeripherique
    {
        [PreserveSig] int Activate(ref Guid iid, int contexte, IntPtr parametres, [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObtenue);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGestionnaireSessions2
    {
        // IAudioSessionManager::GetAudioSessionControl (NULL = la session par défaut du programme)
        [PreserveSig] int GetAudioSessionControl(IntPtr guidSession, uint options, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISession2
    {
        // IAudioSessionControl : 9 méthodes, puis GetSessionIdentifier, GetSessionInstanceIdentifier, GetProcessId, IsSystemSoundsSession
        void R1(); void R2(); void R3(); void R4(); void R5(); void R6(); void R7(); void R8(); void R9();
        void R10(); void R11(); void R12(); void R13();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool sansAttenuation);
    }

    /// <summary>Comme OBS : vrai si Windows a pris la préférence (MyChurch n'est plus baissé pendant les appels, ou l'est de nouveau).</summary>
    public static bool Desactiver(bool desactiver)
    {
        object? enumerateur = null, peripherique = null, gestionnaire = null, session = null;
        try
        {
            enumerateur = new MMDeviceEnumeratorCom();
            if (((IEnumerateur)enumerateur).GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out var p) != 0 || p == null) return false;
            peripherique = p;
            var iid = typeof(IGestionnaireSessions2).GUID;
            if (p.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out gestionnaire) != 0 || gestionnaire == null) return false;
            if (((IGestionnaireSessions2)gestionnaire).GetAudioSessionControl(IntPtr.Zero, 0, out session) != 0 || session is not ISession2 s2) return false;
            return s2.SetDuckingPreference(desactiver) == 0;
        }
        catch (Exception e)
        {
            Journal.Erreur("atténuation audio de Windows", e);
            return false;
        }
        finally
        {
            foreach (var o in new[] { session, gestionnaire, peripherique, enumerateur })
                if (o != null && Marshal.IsComObject(o)) try { Marshal.ReleaseComObject(o); } catch (Exception) { }
        }
    }
}
