using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DayZServerManager.Services;

/// <summary>Envoie des alertes dans un salon Discord grâce à un webhook.</summary>
public static class DiscordNotifier
{
    public const int Green = 0x2FE3F2;
    public const int Red = 0xFF5C7A;
    public const int Orange = 0xFFB547;
    public const int Purple = 0xCB52FF;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Envoie une alerte si les alertes Discord sont activées (les erreurs sont ignorées).</summary>
    public static async Task NotifyAsync(string title, string description, int color)
    {
        var settings = AppSettings.Current;
        if (!settings.DiscordEnabled || string.IsNullOrWhiteSpace(settings.DiscordWebhook)) return;
        try { await SendAsync(settings.DiscordWebhook, title, description, color); }
        catch { /* une alerte manquée ne doit pas gêner le serveur */ }
    }

    /// <summary>Envoie un message (les erreurs remontent, pour le bouton « Tester »).</summary>
    public static async Task SendAsync(string webhook, string title, string description, int color)
    {
        var payload = new
        {
            username = "Stryxhost Manager",
            embeds = new[]
            {
                new
                {
                    title,
                    description = $"**{ServerName()}**\n{description}",
                    color,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    footer = new { text = "Stryxhost Manager" },
                },
            },
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(webhook.Trim(), content);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Discord a refusé le message (code {(int)response.StatusCode}). Vérifie l'URL du webhook.");
    }

    private static string ServerName()
    {
        try
        {
            var path = System.IO.Path.Combine(ServerManager.ServerFolder, "serverDZ.cfg");
            if (System.IO.File.Exists(path))
            {
                var name = ServerConfigFile.Load(path).Get("hostname");
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // Nom illisible.
        }
        return "Serveur DayZ";
    }
}
