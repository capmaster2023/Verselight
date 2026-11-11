using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MyVerse.Ui;

/// <summary>
/// Petite étiquette en capitales légèrement espacées (« BIBLIOTHÈQUE », « EN DIRECT », « ENREGISTREMENTS »), comme dans la maquette 3.1.
/// WPF n'a pas d'espacement des lettres : une espace très fine (U+200A) est glissée entre elles.
/// </summary>
public sealed class Capitales : TextBlock
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public static readonly DependencyProperty LibelleProperty = DependencyProperty.Register(nameof(Libelle), typeof(string), typeof(Capitales),
        new PropertyMetadata("", (d, e) => ((Capitales)d).Text = Espacer(e.NewValue as string ?? "")));

    /// <summary>Le texte, écrit normalement (« Bibliothèque ») : il est affiché en capitales espacées.</summary>
    public string Libelle
    {
        get => (string)GetValue(LibelleProperty);
        set => SetValue(LibelleProperty, value);
    }

    public Capitales() => SetResourceReference(StyleProperty, "CarteTitre");

    /// <summary>« Bibliothèque » → « B I B L I O T H È Q U E » avec des espaces très fines (les vraies espaces restent).</summary>
    public static string Espacer(string texte)
    {
        var t = texte.ToUpper(Fr);
        var sb = new StringBuilder(t.Length * 2);
        for (int i = 0; i < t.Length; i++)
        {
            sb.Append(t[i]);
            if (i < t.Length - 1 && !char.IsWhiteSpace(t[i]) && !char.IsWhiteSpace(t[i + 1])) sb.Append(' ');
        }
        return sb.ToString();
    }
}
