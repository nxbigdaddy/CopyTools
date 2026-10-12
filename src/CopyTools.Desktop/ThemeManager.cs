using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CopyTools.Desktop;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var dark = theme == "Dark" || theme == "System" && IsSystemDark();
        Set("PageBackgroundBrush", dark ? "#0F172A" : "#F3F6FA");
        Set("PanelBrush", dark ? "#172033" : "#FFFFFF");
        Set("ControlBrush", dark ? "#1E293B" : "#FFFFFF");
        Set("TextBrush", dark ? "#E5E7EB" : "#172033");
        Set("MutedTextBrush", dark ? "#9CA3AF" : "#68758A");
        Set("BorderBrush", dark ? "#334155" : "#DDE4EE");
        Set("AlternateRowBrush", dark ? "#1B263B" : "#F8FAFD");
        Set("WarningBrush", dark ? "#FBBF24" : "#B45309");
        Set("BadgeBrush", dark ? "#1E3A5F" : "#E8F0FF");
        Set("BadgeTextBrush", dark ? "#93C5FD" : "#1D4ED8");
        Set("TitleBarBrush", dark ? "#0B1220" : "#132238");
        Set("TitleBarHoverBrush", dark ? "#1E293B" : "#20354F");
        Set("SidebarBrush", dark ? "#111C2E" : "#EAF0F7");
        Set("SidebarSelectedBrush", dark ? "#1E293B" : "#FFFFFF");
        Set("SuccessBrush", dark ? "#34D399" : "#14804A");
    }

    private static void Set(string key, string value) =>
        System.Windows.Application.Current.Resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    private static bool IsSystemDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch { return false; }
    }
}
