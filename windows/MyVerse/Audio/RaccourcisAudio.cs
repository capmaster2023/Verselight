using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les touches des raccourcis audio (MyChurch 4.0) : « Ctrl+Alt+Shift+Win+Touche », la touche étant le nom WPF (F13, Space, D1…)
/// ou un bouton de souris (Souris4, Souris5, SourisMilieu). Lues partout dans Windows, même quand MyChurch n'a pas le focus, comme les raccourcis d'OBS.
/// </summary>
public static class Touches
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int touche);

    sealed record Combinaison(bool Ctrl, bool Alt, bool Shift, bool Win, int Vk);
    static readonly Dictionary<string, Combinaison?> lues = new();

    static readonly Dictionary<string, int> souris = new() { ["SourisMilieu"] = 0x04, ["Souris4"] = 0x05, ["Souris5"] = 0x06 };

    static bool Modificateur(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Le texte enregistré d'une combinaison clavier (modificateurs dans un ordre fixe, puis la touche).</summary>
    public static string Texte(ModifierKeys m, Key k)
    {
        if (k == Key.None || Modificateur(k)) return "";
        var parties = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parties.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parties.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parties.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parties.Add("Win");
        parties.Add(k.ToString());
        return string.Join("+", parties);
    }

    /// <summary>Un bouton de souris (milieu, 4, 5) avec les modificateurs tenus.</summary>
    public static string Texte(ModifierKeys m, MouseButton b)
    {
        string nom = b switch { MouseButton.Middle => "SourisMilieu", MouseButton.XButton1 => "Souris4", MouseButton.XButton2 => "Souris5", _ => "" };
        if (nom.Length == 0) return "";
        string t = Texte(m, Key.F24);
        return t[..^"F24".Length] + nom;
    }

    static Combinaison? Lire(string texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        lock (lues)
        {
            if (lues.TryGetValue(texte, out var deja)) return deja;
            Combinaison? c = null;
            var parties = texte.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parties.Length > 0)
            {
                string touche = parties[^1];
                var mods = parties[..^1];
                int vk = souris.TryGetValue(touche, out int b) ? b : Enum.TryParse<Key>(touche, out var k) && k != Key.None ? KeyInterop.VirtualKeyFromKey(k) : 0;
                if (vk > 0) c = new Combinaison(mods.Contains("Ctrl"), mods.Contains("Alt"), mods.Contains("Shift"), mods.Contains("Win"), vk);
            }
            lues[texte] = c;
            return c;
        }
    }

    static bool Bas(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>La combinaison est enfoncée en ce moment (dans n'importe quel programme).</summary>
    public static bool Enfoncee(string texte)
    {
        if (Lire(texte) is not { } c || !Bas(c.Vk)) return false;
        if (c.Ctrl && !Bas(0x11)) return false;
        if (c.Alt && !Bas(0x12)) return false;
        if (c.Shift && !Bas(0x10)) return false;
        if (c.Win && !Bas(0x5B) && !Bas(0x5C)) return false;
        return true;
    }

    /// <summary>« Ctrl + Maj + F13 », « Souris 4 », « Aucune ».</summary>
    public static string Afficher(string texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return "Aucune";
        return string.Join(" + ", texte.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(p => p switch
        {
            "Shift" => "Maj",
            "Win" => "Windows",
            "SourisMilieu" => "Clic molette",
            "Souris4" => "Souris 4",
            "Souris5" => "Souris 5",
            "Space" => "Espace",
            "Return" or "Enter" => "Entrée",
            "Back" => "Retour arrière",
            "Escape" => "Échap",
            "Insert" => "Inser",
            "Delete" => "Suppr",
            "Home" => "Début",
            "End" => "Fin",
            "PageUp" or "Prior" => "Page préc.",
            "PageDown" or "Next" => "Page suiv.",
            "Up" => "Haut", "Down" => "Bas", "Left" => "Gauche", "Right" => "Droite",
            "Pause" => "Pause",
            "Capital" or "CapsLock" => "Verr. maj",
            _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1..],
            _ when p.StartsWith("NumPad", StringComparison.Ordinal) => "Pavé " + p["NumPad".Length..],
            _ => p,
        }));
    }
}

/// <summary>
/// Le bouton d'une touche de raccourci, comme dans OBS : un clic, puis la touche voulue (avec Ctrl, Alt, Maj ou Windows au besoin).
/// Échap annule, Retour arrière ou Suppr efface. Les boutons 4 et 5 de la souris et la molette marchent aussi.
/// </summary>
public sealed class BoutonTouche : Button
{
    string valeur;
    readonly Action<string> changer;

    /// <summary>Le bouton attend une touche : la fenêtre ne doit pas la traiter comme un raccourci de MyChurch.</summary>
    public bool EnAttente { get; private set; }

    public BoutonTouche(string valeur, Action<string> changer)
    {
        this.valeur = valeur ?? "";
        this.changer = changer;
        Style = MainWindow.Style_("Btn");
        MinWidth = 150;
        Height = 36;
        Padding = new Thickness(12, 0, 12, 0);
        FontSize = 13.5;
        ToolTip = "Cliquer puis appuyer sur la touche voulue (Échap : annuler · Retour arrière : effacer). Elle marche même quand MyChurch est en arrière-plan.";
        Content = Touches.Afficher(this.valeur);
        Click += (_, _) => { if (!EnAttente) Attendre(); };
        LostKeyboardFocus += (_, _) => Fin();
    }

    void Attendre()
    {
        EnAttente = true;
        Content = "Appuyez sur une touche…";
        Focus();
    }

    void Fin()
    {
        EnAttente = false;
        Content = Touches.Afficher(valeur);
    }

    void Choisir(string texte)
    {
        valeur = texte;
        changer(texte);
        Fin();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!EnAttente) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return;
        if (k == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { Fin(); return; }
        if ((k is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None) { Choisir(""); return; }
        string t = Touches.Texte(Keyboard.Modifiers, k);
        if (t.Length > 0) Choisir(t);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (EnAttente) { e.Handled = true; return; }
        base.OnPreviewKeyUp(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (EnAttente && (e.ChangedButton is MouseButton.Middle or MouseButton.XButton1 or MouseButton.XButton2))
        {
            e.Handled = true;
            Choisir(Touches.Texte(Keyboard.Modifiers, e.ChangedButton));
            return;
        }
        base.OnPreviewMouseDown(e);
    }
}
