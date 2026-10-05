using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using DayZServerManager.Services;

namespace DayZServerManager.Views;

public partial class ShareModsWindow : Window
{
    public ShareModsWindow()
    {
        InitializeComponent();
        ModsText.Text = BuildText();
    }

    private static string BuildText()
    {
        var mods = AppSettings.Current.Mods.Where(m => m.Enabled && !m.ServerSide).ToList();
        var text = new StringBuilder();
        text.AppendLine($"Mods du serveur « {ServerName()} »");
        text.AppendLine("Abonne-toi à ces mods sur le Steam Workshop / Subscribe to these mods:");
        text.AppendLine();

        if (mods.Count == 0)
        {
            text.AppendLine("Aucun mod : le serveur est en version de base (vanilla).");
            return text.ToString();
        }

        for (int i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            text.AppendLine($"{i + 1}. {mod.Name}");
            text.AppendLine(mod.Id.Length > 0
                ? $"   https://steamcommunity.com/sharedfiles/filedetails/?id={mod.Id}"
                : "   (mod ajouté à la main, pas sur le Workshop)");
        }

        text.AppendLine();
        text.AppendLine("Paramètre de lancement / Launch parameter:");
        text.AppendLine($"-mod={string.Join(';', mods.Select(m => m.Folder))}");
        return text.ToString();
    }

    private static string ServerName()
    {
        try
        {
            var path = Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
            if (File.Exists(path))
            {
                var name = ServerConfigFile.Load(path).Get("hostname");
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // Nom illisible.
        }
        return "DayZ";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ModsText.Text);
            StatusText.Text = "Copié ! Tu peux le coller sur Discord.";
        }
        catch
        {
            StatusText.Text = "Impossible de copier pour le moment, réessaie.";
        }
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
