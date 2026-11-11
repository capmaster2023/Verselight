using System.ComponentModel;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Les couleurs de l'interface (MyChurch 3.2, design « Pupitre ») : thème clair par défaut, thème sombre au choix (Réglages › Apparence ou barre de gauche).
/// Chaque pinceau de Theme.xaml est lié à une couleur d'ici : changer de thème met à jour d'un coup toute l'interface,
/// fenêtres ouvertes et éléments construits en C# compris (ils utilisent les mêmes pinceaux).
/// Les noms viennent des versions précédentes : « Blanc » est le texte, « Encre » et « Nuit » le fond des cartes, « Or » l'accent.
/// </summary>
public sealed class Palette : INotifyPropertyChanged
{
    /// <summary>Clé → (clair, sombre). Déclaré avant <see cref="Actuelle"/> : les champs statiques s'initialisent dans l'ordre.</summary>
    static readonly Dictionary<string, (string Clair, string Sombre)> Couleurs = new()
    {
        // fonds (design « Pupitre » : crème chaud en clair, brun très foncé en sombre)
        ["Page"] = ("#FAF8F4", "#14120F"),
        ["Rail"] = ("#F2EEE7", "#100E0B"),
        ["RailActif"] = ("#FFFFFF", "#1E1B16"),
        ["Encre"] = ("#FFFFFF", "#1E1B16"),
        ["Nuit"] = ("#FFFFFF", "#1E1B16"),
        ["Nuit2"] = ("#F4F0E9", "#27231D"),
        ["Nuit3"] = ("#E8E2D8", "#332D25"),
        ["Trait"] = ("#EEE8DF", "#2A251F"),
        ["Trait2"] = ("#D6CDBF", "#4A4237"),
        // textes
        ["Blanc"] = ("#1F1A14", "#F3EEE6"),
        ["Encre2"] = ("#5B5247", "#C9BFB1"),
        ["Brume"] = ("#73695C", "#A99F92"),
        ["Brume2"] = ("#8C8275", "#8F8576"),
        // accent ambre
        ["Or"] = ("#B86E00", "#F5A524"),
        ["Or2"] = ("#9A5B00", "#FFC15A"),
        ["OrVif"] = ("#E8920C", "#F5A524"),
        ["OrLeger"] = ("#FFF3DF", "#2B2215"),
        ["SurOr"] = ("#1E1404", "#1E1404"),
        ["OrDegrade"] = ("#E8920C", "#F5A524"),
        ["OrDegradeClair"] = ("#F2A12A", "#FFB53F"),
        // en direct, sourdine, erreurs
        ["Rouge"] = ("#D92D20", "#F04438"),
        ["Direct"] = ("#D92D20", "#F04438"),
        ["RougeLeger"] = ("#FFF5F3", "#2A1715"),
        ["RougeBord"] = ("#F3C2BC", "#5B2722"),
        ["RougeTexte"] = ("#B42318", "#FF7A6E"),
        ["Danger"] = ("#B42318", "#FF7A6E"),
        // projection et moteur actifs
        ["Vert"] = ("#16A34A", "#3DD68C"),
        ["VertLeger"] = ("#E9F7EF", "#13261C"),
        // types d'éléments du déroulé (trait des cartes, titres des groupes de vignettes)
        ["TypeAccueil"] = ("#64748B", "#94A3B8"),
        ["TypeChant"] = ("#2563EB", "#60A5FA"),
        ["TypeLecture"] = ("#D97706", "#F59E0B"),
        ["TypeDisplay"] = ("#DB2777", "#F472B6"),
        ["TypeDuo"] = ("#0D9488", "#2DD4BF"),
        // divers
        ["BleuFonce"] = ("#152C91", "#9DB0FF"),
        ["Marque"] = ("#FFE9C2", "#3A2E14"),
        ["Surligne"] = ("#FFE3A3", "#4A3A12"),
        ["TuileFond"] = ("#17130E", "#0E0C09"),
        ["TuileSource"] = ("#2A241C", "#26211A"),
        ["TuileTexte"] = ("#D9CFC0", "#A99F92"),
        ["VuFond"] = ("#ECE6DC", "#2B261F"),
        ["VuCrete"] = ("#F7D9D6", "#3F2427"),
        ["DamierA"] = ("#F4F0E9", "#1C1914"),
        ["DamierB"] = ("#E8E2D8", "#26221C"),
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
    public Color TypeAccueil => C(nameof(TypeAccueil));
    public Color TypeChant => C(nameof(TypeChant));
    public Color TypeLecture => C(nameof(TypeLecture));
    public Color TypeDisplay => C(nameof(TypeDisplay));
    public Color TypeDuo => C(nameof(TypeDuo));
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
