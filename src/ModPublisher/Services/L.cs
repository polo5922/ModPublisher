using System.Globalization;
using System.Windows.Markup;

namespace ModPublisher.Services;

/// <summary>
/// Textes de l'app : français si Windows est en français, anglais sinon. Les deux versions sont écrites côte à côte :
/// L.T("Bonjour", "Hello") dans le code, {l:T 'Bonjour', 'Hello'} dans le XAML.
/// </summary>
public static class L
{
    public static bool French { get; private set; } = IsFrench(CultureInfo.CurrentUICulture);

    public static bool IsFrench(CultureInfo culture) => culture.TwoLetterISOLanguageName == "fr";

    public static string T(string fr, string en) => French ? fr : en;

    /// <summary>« --lang fr|en » force la langue (captures d'écran, essais).</summary>
    public static void Parse(string[] args)
    {
        var i = Array.IndexOf(args, "--lang");
        if (i < 0 || i + 1 >= args.Length) return;
        var culture = CultureInfo.GetCultureInfo(args[i + 1] == "fr" ? "fr-FR" : "en-US");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        French = IsFrench(culture);
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string fr, string en) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(fr, en);
}
