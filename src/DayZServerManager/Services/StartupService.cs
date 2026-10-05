using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace DayZServerManager.Services;

/// <summary>
/// Lancement automatique à l'ouverture de session. L'application demande les droits administrateur :
/// Windows bloque ce genre d'application au démarrage classique, on passe donc par le Planificateur de tâches.
/// </summary>
public static class StartupService
{
    private const string TaskName = "Stryxhost Manager";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void Enable()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Emplacement de l'application introuvable.");
        var user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Lance Stryxhost Manager à l'ouverture de session.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>--minimized</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), "stryxhost-startup.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        try
        {
            if (RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F") != 0)
                throw new InvalidOperationException("Windows a refusé de créer la tâche de démarrage.");
        }
        finally
        {
            try { File.Delete(file); } catch { /* sans importance */ }
        }
    }

    public static void Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static int RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Impossible de lancer le Planificateur de tâches.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
