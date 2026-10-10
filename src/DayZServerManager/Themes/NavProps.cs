using System.Windows;
using System.Windows.Media;

namespace DayZServerManager.Themes;

/// <summary>Propriétés ajoutées aux boutons du menu : icône, compteur et mode réduit.</summary>
public static class NavProps
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(NavProps), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject obj) => (Geometry?)obj.GetValue(IconProperty);
    public static void SetIcon(DependencyObject obj, Geometry? value) => obj.SetValue(IconProperty, value);

    /// <summary>Petit compteur affiché à droite (vide = caché).</summary>
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.RegisterAttached(
        "Badge", typeof(string), typeof(NavProps), new FrameworkPropertyMetadata(""));

    public static string GetBadge(DependencyObject obj) => (string)obj.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject obj, string value) => obj.SetValue(BadgeProperty, value);

    /// <summary>Compteur orange (problème à régler) au lieu de cyan.</summary>
    public static readonly DependencyProperty BadgeWarnProperty = DependencyProperty.RegisterAttached(
        "BadgeWarn", typeof(bool), typeof(NavProps), new FrameworkPropertyMetadata(false));

    public static bool GetBadgeWarn(DependencyObject obj) => (bool)obj.GetValue(BadgeWarnProperty);
    public static void SetBadgeWarn(DependencyObject obj, bool value) => obj.SetValue(BadgeWarnProperty, value);

    /// <summary>Menu réduit : seule l'icône reste visible.</summary>
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(NavProps), new FrameworkPropertyMetadata(false));

    public static bool GetCompact(DependencyObject obj) => (bool)obj.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject obj, bool value) => obj.SetValue(CompactProperty, value);
}
