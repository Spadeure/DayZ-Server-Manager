using System.IO;
using System.Text.Json;

namespace DayZServerManager.Services;

/// <summary>Réglages de l'application, enregistrés dans %AppData%\DayZServerManager.</summary>
public class AppSettings
{
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayZServerManager");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public static AppSettings Current { get; } = Load();

    /// <summary>Dossier principal choisi par l'utilisateur (SteamCMD + serveur).</summary>
    public string ServerFolder { get; set; } = "";

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Fichier abîmé : on repart de réglages vides.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
