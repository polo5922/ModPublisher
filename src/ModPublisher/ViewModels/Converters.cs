using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ModPublisher.ViewModels;

/// <summary>Couleurs d'état communes (vert = bon, orange = à faire, gris = inconnu).</summary>
public static class Tones
{
    public static readonly Brush Ok = Frozen("#22C55E");
    public static readonly Brush Warn = Frozen("#F59E0B");
    public static readonly Brush Unknown = Frozen("#8B8B8B");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Texte d'état de comparaison → couleur de la pastille.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        { } s when s.StartsWith('✔') => Tones.Ok,
        { } s when s.Contains("diff") || s.Contains("jamais") || s.Contains("never") => Tones.Warn,
        _ => Tones.Unknown,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Booléen (compte connecté…) → vert ou gris.</summary>
public sealed class BoolBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Tones.Ok : Tones.Unknown;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible si la valeur n'est pas nulle (paramètre « invert » : l'inverse).</summary>
public sealed class NullToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null || value as string == "" || value is 0) ^ (parameter as string == "invert") ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tag d'un onglet → icône Segoe Fluent Icons.</summary>
public sealed class TabGlyph : IValueConverter
{
    public static readonly TabGlyph Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "page" => "\uE8A5",
        "versions" => "\uE7B8",
        "galerie" => "\uEB9F",
        "reglages" => "\uE713",
        "comptes" => "\uE77B",
        "stats" => "\uE9D2",
        _ => "",
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
