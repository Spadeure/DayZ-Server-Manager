using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DayZServerManager.Services;

namespace DayZServerManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledError;
        UpdateService.CleanupOldVersion();

        // Les champs numériques (style NumberBox) n'acceptent que des chiffres.
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewTextInputEvent,
            new TextCompositionEventHandler((sender, args) =>
            {
                if (sender is TextBox { Tag: "number" } && !args.Text.All(char.IsDigit)) args.Handled = true;
            }));
        base.OnStartup(e);
    }

    // Toute erreur imprévue est enregistrée dans un fichier, pour pouvoir l'envoyer.
    private void OnUnhandledError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logFile = Path.Combine(AppSettings.DataFolder, "erreurs.log");
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch
        {
            // Impossible d'écrire le journal : on affiche quand même le message.
        }

        MessageBox.Show(
            $"Une erreur est survenue :\n\n{e.Exception.Message}\n\nLe détail est enregistré dans :\n{logFile}",
            "DayZ Server Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
