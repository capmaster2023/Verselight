using System.ComponentModel;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Les couleurs de l'interface (MyChurch 3.1) : thème clair par défaut, thème sombre au choix (Réglages › Apparence).
/// Chaque pinceau de Theme.xaml est lié à une couleur d'ici : changer de thème met à jour d'un coup toute l'interface,
/// fenêtres ouvertes et éléments construits en C# compris (ils utilisent les mêmes pinceaux).
/// Les noms viennent des versions précédentes : « Blanc » est le texte, « Encre » et « Nuit » le fond des cartes, « Or » l'accent.
/// </summary>
public sealed class Palette : INotifyPropertyChanged
{
    /// <summary>Clé → (clair, sombre). Déclaré avant <see cref="Actuelle"/> : les champs statiques s'initialisent dans l'ordre.</summary>
    static readonly Dictionary<string, (string Clair, string Sombre)> Couleurs = new()
    {
        // fonds
        ["Page"] = ("#F3F4F6", "#111317"),
        ["Rail"] = ("#FFFFFF", "#0B0C0F"),
        ["RailActif"] = ("#F1F2F4", "#1D2026"),
        ["Encre"] = ("#FFFFFF", "#1A1D22"),
        ["Nuit"] = ("#FFFFFF", "#1A1D22"),
        ["Nuit2"] = ("#F2F3F5", "#23272E"),
        ["Nuit3"] = ("#E1E4E9", "#2C3038"),
        ["Trait"] = ("#E7E9ED", "#2A2E35"),
        ["Trait2"] = ("#C7CCD4", "#3D434C"),
        // textes
        ["Blanc"] = ("#16181D", "#F2F3F5"),
        ["Encre2"] = ("#3A404A", "#C8CCD2"),
        ["Brume"] = ("#5F6672", "#9AA0AA"),
        ["Brume2"] = ("#8A909A", "#6E747E"),
        // accent ambre
        ["Or"] = ("#C77A06", "#F5A524"),
        ["Or2"] = ("#A86200", "#FFC15A"),
        ["OrVif"] = ("#F5A524", "#F5A524"),
        ["OrLeger"] = ("#FFF4E0", "#2A2316"),
        ["SurOr"] = ("#1E1404", "#1E1404"),
        ["OrDegrade"] = ("#F5A524", "#F5A524"),
        ["OrDegradeClair"] = ("#E99812", "#FFB53F"),
        // en direct, sourdine, erreurs
        ["Rouge"] = ("#DC2626", "#F04848"),
        ["Direct"] = ("#D92D20", "#F25555"),
        ["RougeLeger"] = ("#FFF6F5", "#241517"),
        ["RougeBord"] = ("#F2B8B2", "#5B262A"),
        ["RougeTexte"] = ("#B42318", "#FF7070"),
        ["Danger"] = ("#B42318", "#FF7070"),
        // projection et moteur actifs
        ["Vert"] = ("#16A34A", "#3DD68C"),
        ["VertLeger"] = ("#E9F7EF", "#13261C"),
        // divers
        ["BleuFonce"] = ("#152C91", "#9DB0FF"),
        ["Marque"] = ("#FFE9C2", "#3A2E14"),
        ["Surligne"] = ("#FFE3A3", "#4A3A12"),
        ["TuileFond"] = ("#14161B", "#0D0F13"),
        ["TuileSource"] = ("#262B33", "#23272E"),
        ["TuileTexte"] = ("#C9CED6", "#9AA0AA"),
        ["VuFond"] = ("#E4E7EC", "#262A31"),
        ["VuCrete"] = ("#F7D9D6", "#3F2427"),
        ["DamierA"] = ("#F3F4F6", "#1C1F24"),
        ["DamierB"] = ("#E5E7EB", "#24282E"),
    };

    public static Palette Actuelle { get; } = new();

    readonly Dictionary<string, Color> actuelles = new();

    Palette() => Remplir(false);

    public bool Sombre { get; private set; }
    /// <summary>« clair » ou « sombre » (Reglages.Apparence).</summary>
    public string Nom => Sombre ? "sombre" : "clair";
    /// <summary>Toutes les clés de couleur (pour le test automatique).</summary>
    public static IReadOnlyCollection<string> Cles => Couleurs.Keys;
    /// <summary>La couleur d'une clé dans un thème donné (aperçus des Réglages, test automatique).</summary>
    public static Color De(string cle, bool sombre) => Lire(sombre ? Couleurs[cle].Sombre : Couleurs[cle].Clair);

    /// <summary>Le thème a changé (après la mise à jour des pinceaux).</summary>
    public static event Action? Change;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applique « clair » ou « sombre » (toute autre valeur : clair). Sans effet si c'est déjà le thème.</summary>
    public void Appliquer(string? nom)
    {
        bool sombre = nom == "sombre";
        if (sombre == Sombre) return;
        Remplir(sombre);
        // nom vide : toutes les couleurs ont changé
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Change?.Invoke();
    }

    void Remplir(bool sombre)
    {
        Sombre = sombre;
        foreach (var (cle, (clair, fonce)) in Couleurs) actuelles[cle] = Lire(sombre ? fonce : clair);
    }

    static Color Lire(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    Color C(string cle) => actuelles[cle];

    public Color Page => C(nameof(Page));
    public Color Rail => C(nameof(Rail));
    public Color RailActif => C(nameof(RailActif));
    public Color Encre => C(nameof(Encre));
    public Color Nuit => C(nameof(Nuit));
    public Color Nuit2 => C(nameof(Nuit2));
    public Color Nuit3 => C(nameof(Nuit3));
    public Color Trait => C(nameof(Trait));
    public Color Trait2 => C(nameof(Trait2));
    public Color Blanc => C(nameof(Blanc));
    public Color Encre2 => C(nameof(Encre2));
    public Color Brume => C(nameof(Brume));
    public Color Brume2 => C(nameof(Brume2));
    public Color Or => C(nameof(Or));
    public Color Or2 => C(nameof(Or2));
    public Color OrVif => C(nameof(OrVif));
    public Color OrLeger => C(nameof(OrLeger));
    public Color SurOr => C(nameof(SurOr));
    public Color OrDegrade => C(nameof(OrDegrade));
    public Color OrDegradeClair => C(nameof(OrDegradeClair));
    public Color Rouge => C(nameof(Rouge));
    public Color Direct => C(nameof(Direct));
    public Color RougeLeger => C(nameof(RougeLeger));
    public Color RougeBord => C(nameof(RougeBord));
    public Color RougeTexte => C(nameof(RougeTexte));
    public Color Danger => C(nameof(Danger));
    public Color Vert => C(nameof(Vert));
    public Color VertLeger => C(nameof(VertLeger));
    public Color BleuFonce => C(nameof(BleuFonce));
    public Color Marque => C(nameof(Marque));
    public Color Surligne => C(nameof(Surligne));
    public Color TuileFond => C(nameof(TuileFond));
    public Color TuileSource => C(nameof(TuileSource));
    public Color TuileTexte => C(nameof(TuileTexte));
    public Color VuFond => C(nameof(VuFond));
    public Color VuCrete => C(nameof(VuCrete));
    public Color DamierA => C(nameof(DamierA));
    public Color DamierB => C(nameof(DamierB));
}
