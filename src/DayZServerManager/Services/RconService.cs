using System.Text.RegularExpressions;

namespace DayZServerManager.Services;

public record PlayerInfo(int Id, string Name, string Ip, int Ping, string Guid, bool InLobby);

public record BanInfo(int Index, string Target, string Remaining, string Reason);

/// <summary>Connexion RCon partagée par toute l'application, avec les commandes d'administration.</summary>
public sealed class RconService
{
    public static RconService Instance { get; } = new();

    private static readonly Regex PlayerRegex = new(@"^\s*(\d+)\s+(\S+?):(\d+)\s+(-?\d+)\s+(\S+)\s+(.*?)\s*$");
    private static readonly Regex BanRegex = new(@"^\s*(\d+)\s+(\S+)\s+(\S+)\s*(.*)$");

    private readonly RconClient _client = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private RconService()
    {
        _client.Disconnected += () => StatusChanged?.Invoke();
        _client.ServerMessage += message => ServerMessage?.Invoke(message);
    }

    public bool IsConnected => _client.IsConnected;
    public string LastError { get; private set; } = "";

    /// <summary>Connecté ou déconnecté (appelé depuis n'importe quel fil).</summary>
    public event Action? StatusChanged;

    /// <summary>Message du serveur : chat, connexions… (appelé depuis n'importe quel fil).</summary>
    public event Action<string>? ServerMessage;

    /// <summary>Se connecte si besoin. Renvoie false (avec LastError) si c'est impossible.</summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (_client.IsConnected) return true;
        await _connectLock.WaitAsync();
        try
        {
            if (_client.IsConnected) return true;
            if (!ServerManager.Instance.IsRunning)
            {
                LastError = "Le serveur n'est pas lancé.";
                return false;
            }
            var password = BattlEyeConfig.ReadPassword();
            if (string.IsNullOrWhiteSpace(password))
            {
                LastError = "Aucun mot de passe RCon : renseigne-le dans l'onglet Configuration, puis redémarre le serveur.";
                return false;
            }

            await _client.ConnectAsync("127.0.0.1", AppSettings.Current.RconPort, password);
            LastError = "";
            StatusChanged?.Invoke();
            return true;
        }
        catch (TimeoutException) when (!BattlEyeConfig.IsActive())
        {
            LastError = "BattlEye n'a pas activé le RCon : redémarre le serveur depuis l'application (Tableau de bord → Redémarrer), "
                        + "puis attends 2 minutes avant de te connecter.";
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public void Disconnect()
    {
        _client.Dispose();
        StatusChanged?.Invoke();
    }

    private async Task<string> CommandAsync(string command)
    {
        if (!await EnsureConnectedAsync()) throw new InvalidOperationException(LastError);
        return await _client.SendCommandAsync(command);
    }

    // ===== Joueurs =====

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        var output = await CommandAsync("players");
        var players = new List<PlayerInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = PlayerRegex.Match(line.TrimEnd('\r'));
            if (!match.Success) continue;

            var guid = match.Groups[5].Value;
            var parenthesis = guid.IndexOf('(');
            if (parenthesis >= 0) guid = guid[..parenthesis];

            var name = match.Groups[6].Value;
            bool lobby = name.EndsWith("(Lobby)", StringComparison.OrdinalIgnoreCase);
            if (lobby) name = name[..^"(Lobby)".Length].Trim();

            players.Add(new PlayerInfo(
                int.Parse(match.Groups[1].Value), name, match.Groups[2].Value,
                int.TryParse(match.Groups[4].Value, out var ping) ? ping : 0, guid, lobby));
        }
        return players;
    }

    public Task SayAllAsync(string message) => CommandAsync($"say -1 {Clean(message)}");

    public Task SayAsync(int playerId, string message) => CommandAsync($"say {playerId} {Clean(message)}");

    public Task KickAsync(int playerId, string reason) =>
        CommandAsync($"kick {playerId} {Clean(reason)}".TrimEnd());

    /// <summary>Bannit un joueur connecté. minutes = 0 : définitif.</summary>
    public async Task BanAsync(int playerId, int minutes, string reason)
    {
        await CommandAsync($"ban {playerId} {minutes} {Clean(reason)}".TrimEnd());
        await CommandAsync("writeBans");
    }

    // ===== Bannis =====

    public async Task<List<BanInfo>> GetBansAsync()
    {
        var output = await CommandAsync("bans");
        var bans = new List<BanInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = BanRegex.Match(line.TrimEnd('\r'));
            if (!match.Success) continue;
            var remaining = match.Groups[3].Value;
            remaining = remaining.Equals("perm", StringComparison.OrdinalIgnoreCase) ? "Définitif" : $"{remaining} min";
            bans.Add(new BanInfo(int.Parse(match.Groups[1].Value), match.Groups[2].Value, remaining, match.Groups[4].Value.Trim()));
        }
        return bans;
    }

    public async Task RemoveBanAsync(int index)
    {
        await CommandAsync($"removeBan {index}");
        await CommandAsync("writeBans");
    }

    private static string Clean(string text) => text.Replace('\n', ' ').Replace('\r', ' ').Trim();
}
