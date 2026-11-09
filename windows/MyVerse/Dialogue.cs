using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>Fenêtre de dialogue de MyVerse : fond sombre, barre de titre sombre, Échap pour fermer.</summary>
public class FenetreDialogue : Window
{
    public FenetreDialogue()
    {
        Style = (Style)Application.Current.FindResource("Dialogue");
        SourceInitialized += (_, _) => Natif.TitreSombre(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (Keyboard.FocusedElement is ComboBoxItem || (Keyboard.FocusedElement is ComboBox cb && cb.IsDropDownOpen)) return; // ferme d'abord la liste
            e.Handled = true;
            Close();
        };
    }

    protected static Brush Pinceau(string cle) => MainWindow.Pinceau(cle);
    protected static Style Style_(string cle) => MainWindow.Style_(cle);
}

/// <summary>Petites questions : confirmer, demander un texte (au style de MyVerse).</summary>
public static class Dialogue
{
    static (FenetreDialogue W, StackPanel Corps, StackPanel Boutons) Base(Window? owner, string titre)
    {
        var w = new FenetreDialogue { Title = titre, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, MinWidth = 380, MaxWidth = 640 };
        if (owner != null && owner.IsLoaded) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var corps = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var tout = new StackPanel();
        tout.Children.Add(corps);
        corps.Children.Add(boutons);
        w.Content = tout;
        return (w, corps, boutons);
    }

    static TextBlock Texte(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = MainWindow.Pinceau("Blanc"), MaxWidth = 580 };

    /// <summary>Question oui / non. Renvoie vrai si l'utilisateur confirme.</summary>
    public static bool Confirmer(Window? owner, string message, string ok = "OK", string annuler = "Annuler")
    {
        var (w, corps, boutons) = Base(owner, "MyVerse");
        corps.Children.Insert(0, Texte(message));
        bool res = false;
        var non = new Button { Style = MainWindow.Style_("Btn"), Content = annuler, IsCancel = true, MinWidth = 100 };
        var oui = new Button { Style = MainWindow.Style_("BtnOr"), Content = ok, IsDefault = true, Margin = new Thickness(10, 0, 0, 0), MinWidth = 100 };
        non.Click += (_, _) => w.Close();
        oui.Click += (_, _) => { res = true; w.Close(); };
        boutons.Children.Add(non);
        boutons.Children.Add(oui);
        w.Loaded += (_, _) => oui.Focus();
        w.ShowDialog();
        return res;
    }

    /// <summary>Message simple avec un bouton OK.</summary>
    public static void Informer(Window? owner, string message)
    {
        var (w, corps, boutons) = Base(owner, "MyVerse");
        corps.Children.Insert(0, Texte(message));
        var ok = new Button { Style = MainWindow.Style_("BtnOr"), Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 100 };
        ok.Click += (_, _) => w.Close();
        boutons.Children.Add(ok);
        w.Loaded += (_, _) => ok.Focus();
        w.ShowDialog();
    }

    /// <summary>Demande un texte court. Renvoie null si l'utilisateur annule.</summary>
    public static string? Demander(Window? owner, string question, string defaut, string titre = "MyVerse")
    {
        var (w, corps, boutons) = Base(owner, titre);
        corps.Children.Insert(0, Texte(question));
        var champ = new TextBox { Style = MainWindow.Style_("Champ"), Text = defaut, Margin = new Thickness(0, 12, 0, 0), MinWidth = 300 };
        corps.Children.Insert(1, champ);
        string? res = null;
        var non = new Button { Style = MainWindow.Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 100 };
        var oui = new Button { Style = MainWindow.Style_("BtnOr"), Content = "OK", IsDefault = true, Margin = new Thickness(10, 0, 0, 0), MinWidth = 100 };
        non.Click += (_, _) => w.Close();
        oui.Click += (_, _) => { res = champ.Text.Trim(); w.Close(); };
        boutons.Children.Add(non);
        boutons.Children.Add(oui);
        w.Loaded += (_, _) => { champ.Focus(); champ.SelectAll(); };
        w.ShowDialog();
        return res;
    }

    /// <summary>Diapositives en miniature (éditeur, découpe) : numéro, nom, texte ; en rouge si trop longue.</summary>
    public static void Miniatures(WrapPanel boite, IEnumerable<(string Titre, string Texte)> diapos, double largeur = 196)
    {
        boite.Children.Clear();
        foreach (var (titre, texte) in diapos)
        {
            bool longue = texte.Split('\n').Length > 6;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = titre, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = MainWindow.Pinceau("Or"), Margin = new Thickness(0, 0, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis });
            p.Children.Add(new TextBlock { Text = texte, FontSize = 12.5, LineHeight = 17.5, TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Pinceau("Blanc") });
            boite.Children.Add(new Border
            {
                Background = MainWindow.Pinceau("Nuit2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8), Width = largeur,
                BorderBrush = longue ? new SolidColorBrush(Color.FromRgb(0xC0, 0x50, 0x4D)) : Brushes.Transparent, BorderThickness = new Thickness(longue ? 2 : 0),
                ToolTip = longue ? "Diapositive longue : appuyez sur Entrée devant une ligne pour la couper" : null,
                Child = p,
            });
        }
    }
}
