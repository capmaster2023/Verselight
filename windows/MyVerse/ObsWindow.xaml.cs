namespace MyVerse.Ui;

/// <summary>Réglages OBS : piloter OBS par obs-websocket (port, mot de passe, nom de la source).</summary>
public partial class ObsWindow : FenetreDialogue
{
    public ObsWindow()
    {
        InitializeComponent();
        var c = App.Donnees.Reglages.Obs;
        Actif.IsChecked = c.Actif;
        Source.Text = c.Source;
        Port.Text = c.Port.ToString();
        Mdp.Password = c.Mdp;
        Statut.Text = App.Obs.Statut;
        Annuler.Click += (_, _) => Close();
        Enregistrer.Click += (_, _) =>
        {
            var r = App.Donnees.Reglages.Obs;
            r.Actif = Actif.IsChecked == true;
            r.Source = Source.Text.Trim().Length > 0 ? Source.Text.Trim() : "BIBLE";
            r.Port = int.TryParse(Port.Text.Trim(), out var p) && p > 0 && p < 65536 ? p : 4455;
            r.Mdp = Mdp.Password;
            App.Donnees.SauverReglages();
            DialogResult = true;
        };
    }
}
