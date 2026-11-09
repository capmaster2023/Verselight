namespace MyVerse.Ui;

/// <summary>
/// ✂ Découper : Entrée devant une ligne (ou dans un verset) = nouvelle diapositive.
/// Verset : une ligne = une diapositive. Strophe : une ligne vide sépare les diapositives.
/// </summary>
public partial class DecoupeWindow : FenetreDialogue
{
    readonly Cible cible;
    readonly bool verset;

    /// <summary>Message pour l'utilisateur après l'enregistrement.</summary>
    public string Message { get; private set; } = "";

    public DecoupeWindow(Cible c)
    {
        InitializeComponent();
        cible = c;
        verset = c is CibleVerset;
        if (c is CibleVerset v)
            Aide.Text = $"{Livres.Libelle(v.B, v.C, v.V1)} — placez le curseur dans le texte et appuyez sur Entrée : chaque ligne devient une diapositive.";
        else if (c is CibleChant cc && App.Regie.Trouver(cc.Id) is { } ch && cc.I < ch.Sections.Count)
            Aide.Text = $"{ch.Title} — {(ch.Sections[cc.I].Label.Length > 0 ? ch.Sections[cc.I].Label : "Strophe")} : appuyez sur Entrée devant une ligne pour commencer une nouvelle diapositive.";
        Texte.Text = App.Regie.TexteADecouper(c);
        Apercu_();
        Texte.TextChanged += (_, _) => Apercu_();
        UnSeul.Click += (_, _) =>
        {
            var t = Texte.Text.Replace("\r\n", "\n");
            Texte.Text = verset
                ? string.Join(" ", t.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                : string.Join("\n", t.Split('\n').Where(l => l.Trim().Length > 0));
        };
        Annuler.Click += (_, _) => Close();
        Enregistrer.Click += (_, _) =>
        {
            Message = App.Regie.Decouper(cible, Texte.Text);
            DialogResult = true;
        };
        Loaded += (_, _) => { Texte.Focus(); Texte.CaretIndex = 0; };
    }

    void Apercu_()
    {
        var parts = Regie.MorceauxTexte(Texte.Text, verset);
        Compte.Text = $"({parts.Count})";
        Dialogue.Miniatures(Apercu, parts.Select((p, i) => ((i + 1).ToString(), p)));
    }
}
