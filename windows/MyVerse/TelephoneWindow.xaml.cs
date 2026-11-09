using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace MyVerse.Ui;

/// <summary>
/// Fenêtre « Téléphone » : code QR de jumelage, téléphones jumelés (révoquer), pare-feu Windows.
/// Le code QR n'est valable que tant que cette fenêtre est ouverte.
/// </summary>
public partial class TelephoneWindow : FenetreDialogue
{
    bool demande;

    public TelephoneWindow()
    {
        InitializeComponent();
        var t = App.Telephone;
        if (t.Port == 0)
        {
            QrAttente.Text = "Le module téléphone n'a pas pu démarrer (ports 47900 à 47909 occupés ?). Fermez les autres copies de MyVerse puis rouvrez MyVerse.";
        }
        else
        {
            var url = t.OuvrirJumelage();
            try
            {
                Qr.Source = CodeQr(url);
                QrAttente.Visibility = Visibility.Collapsed;
            }
            catch (Exception x)
            {
                QrAttente.Text = "Code QR impossible : " + x.Message;
                Journal.Erreur("code QR", x);
            }
            var ip = Telephone.AdressesLocales().FirstOrDefault();
            Info.Text = ip != null
                ? $"{t.NomPc} · test depuis le téléphone : ouvrez http://{ip}:{t.Port} dans Chrome (la page « MyVerse ✓ » doit s'afficher)"
                : $"{t.NomPc} · aucune adresse réseau : branchez le PC au Wi-Fi ou au réseau";
        }
        Appareils();
        MajParefeu();
        t.Change += Changement;
        Closed += (_, _) =>
        {
            t.Change -= Changement;
            t.FermerJumelage();
        };
        Fermer.Click += (_, _) => Close();
        ParefeuBtn.Click += (_, _) => DemanderParefeu();
        // comme la 3.x : à l'ouverture, la demande Windows s'affiche si l'autorisation manque
        Loaded += (_, _) => { if (App.Parefeu_ == "missing" && App.AutoTest == null) DemanderParefeu(); };
    }

    void Changement() => Dispatcher.BeginInvoke(new Action(Appareils));

    /// <summary>Code QR (noir sur blanc, bien lisible par l'appareil photo).</summary>
    public static BitmapImage CodeQr(string texte)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(texte, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(png);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    void Appareils()
    {
        Liste.Children.Clear();
        var connectes = App.Telephone.Connectes();
        var liste = App.Telephone.Store.Liste();
        if (liste.Count == 0) { Liste.Children.Add(new TextBlock { Text = "Aucun téléphone jumelé.", Style = Style_("Muet") }); return; }
        foreach (var a in liste)
        {
            bool en = connectes.Contains(a.DeviceId);
            var ligne = new DockPanel();
            var x = new Button { Style = Style_("BtnDanger"), Content = "Révoquer" };
            var id = a.DeviceId;
            var nom = a.Nom;
            x.Click += (_, _) =>
            {
                if (Dialogue.Confirmer(this, $"Révoquer « {nom} » ? Il devra scanner un nouveau code.", "Révoquer")) App.Telephone.Revoquer(id);
                Appareils();
            };
            DockPanel.SetDock(x, Dock.Right);
            ligne.Children.Add(x);
            var point = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Pinceau(en ? "Vert" : "Brume2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(point, Dock.Left);
            ligne.Children.Add(point);
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run(a.Nom.Length > 0 ? a.Nom : "Téléphone") { FontWeight = FontWeights.Bold });
            string vu = en ? " connecté" : DateTimeOffset.TryParse(a.VuLe, out var d) ? " vu le " + d.LocalDateTime.ToString("d MMMM yyyy à HH:mm", new System.Globalization.CultureInfo("fr-CA")) : "";
            t.Inlines.Add(new System.Windows.Documents.Run(vu) { Foreground = Pinceau(en ? "Vert" : "Brume2"), FontSize = 13 });
            ligne.Children.Add(t);
            Liste.Children.Add(new Border { Background = Pinceau("Nuit2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), Child = ligne });
        }
    }

    void MajParefeu()
    {
        var p = App.Parefeu_;
        Parefeu.Text = p switch
        {
            "ok" => "Pare-feu Windows : autorisé.",
            "refused" => "Pare-feu : autorisation refusée. Le téléphone ne pourra pas se connecter. Cliquez « Autoriser dans le pare-feu » puis acceptez la demande Windows.",
            "error" => "Pare-feu : la règle n'a pas pu être ajoutée. Cliquez « Autoriser dans le pare-feu ».",
            "missing" => "Pare-feu : autorisation nécessaire pour le téléphone. Acceptez la demande de Windows (ou cliquez « Autoriser dans le pare-feu »).",
            "demande" => "Pare-feu : acceptez la demande de Windows…",
            _ => "Pare-feu : vérification…",
        };
        Parefeu.Foreground = p is "refused" or "error" or "missing" ? Pinceau("RougeTexte") : Pinceau("Brume2");
        ParefeuBtn.Visibility = p is "ok" or "demande" ? Visibility.Collapsed : Visibility.Visible;
    }

    async void DemanderParefeu()
    {
        if (demande) return;
        demande = true;
        App.Parefeu_ = "demande";
        MajParefeu();
        var r = await Task.Run(MyVerse.Parefeu.Demander);
        if (r == "ok") App.Telephone.Store.Parefeu = MyVerse.Parefeu.Marque;
        Journal.Info("pare-feu Windows : " + r);
        App.Parefeu_ = r;
        demande = false;
        if (IsLoaded) MajParefeu();
        (Owner as MainWindow)?.MajTelephone();
    }
}
