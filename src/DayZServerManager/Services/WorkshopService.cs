using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

public record WorkshopItem(string Id, string Title, string PreviewUrl, long Subscriptions, long FileSize, DateTime Updated)
{
    public string Url => $"https://steamcommunity.com/sharedfiles/filedetails/?id={Id}";
}

/// <summary>Recherche des mods DayZ sur le Steam Workshop (sans clé d'API).</summary>
public static class WorkshopService
{
    private const string DayZAppId = "221100";
    private const string DetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9,en;q=0.8");
        return client;
    }

    /// <summary>Recherche par nom. Sans texte, renvoie les mods les plus populaires.</summary>
    public static async Task<List<WorkshopItem>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? $"https://steamcommunity.com/workshop/browse/?appid={DayZAppId}&browsesort=totaluniquesubscribers&actualsort=totaluniquesubscribers&p=1"
            : $"https://steamcommunity.com/workshop/browse/?appid={DayZAppId}&searchtext={Uri.EscapeDataString(query.Trim())}&browsesort=textsearch&actualsort=textsearch&p=1";

        var html = await Http.GetStringAsync(url, ct);
        var ids = Regex.Matches(html, @"sharedfiles/filedetails/\?id=(\d+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Take(30)
            .ToList();

        return await GetDetailsAsync(ids, ct);
    }

    /// <summary>Informations détaillées (titre, image, abonnés, taille) pour une liste de mods.</summary>
    public static async Task<List<WorkshopItem>> GetDetailsAsync(IList<string> ids, CancellationToken ct = default)
    {
        var items = new List<WorkshopItem>();
        if (ids.Count == 0) return items;

        var form = new List<KeyValuePair<string, string>> { new("itemcount", ids.Count.ToString()) };
        for (int i = 0; i < ids.Count; i++)
            form.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", ids[i]));

        using var response = await Http.PostAsync(DetailsUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        if (!doc.RootElement.TryGetProperty("response", out var root) ||
            !root.TryGetProperty("publishedfiledetails", out var details))
            return items;

        foreach (var item in details.EnumerateArray())
        {
            if (ReadLong(item, "result") != 1) continue;
            if (ReadLong(item, "consumer_app_id").ToString() != DayZAppId) continue;

            items.Add(new WorkshopItem(
                ReadString(item, "publishedfileid"),
                ReadString(item, "title"),
                ReadString(item, "preview_url"),
                ReadLong(item, "subscriptions"),
                ReadLong(item, "file_size"),
                DateTimeOffset.FromUnixTimeSeconds(ReadLong(item, "time_updated")).LocalDateTime));
        }
        return items;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    // Steam renvoie certains nombres sous forme de texte : on accepte les deux.
    private static long ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return 0;
    }
}
