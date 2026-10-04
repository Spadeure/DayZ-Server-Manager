using System.Diagnostics;

namespace DayZServerManager.Services;

public record NetshResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Gère les règles du pare-feu Windows du serveur DayZ (via netsh).</summary>
public static class FirewallService
{
    public const string GameRule = "DayZ Server Manager - Jeu";
    public const string QueryRule = "DayZ Server Manager - Requete Steam";
    public const string RconRule = "DayZ Server Manager - RCon BattlEye";
    public const string ProgramRule = "DayZ Server Manager - Programme serveur";

    public static readonly string[] AllRules = [GameRule, QueryRule, RconRule, ProgramRule];

    public static async Task<bool> RuleExistsAsync(string name) =>
        (await RunNetshAsync($"advfirewall firewall show rule name=\"{name}\"")).Success;

    public static async Task<Dictionary<string, bool>> GetStatusAsync()
    {
        var status = new Dictionary<string, bool>();
        foreach (var rule in AllRules)
            status[rule] = await RuleExistsAsync(rule);
        return status;
    }

    public static async Task<NetshResult> OpenPortAsync(string name, string protocol, string ports)
    {
        await DeleteRuleAsync(name);
        return await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={ports} profile=any");
    }

    public static async Task<NetshResult> AllowProgramAsync(string name, string exePath)
    {
        await DeleteRuleAsync(name);
        return await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any");
    }

    public static Task<NetshResult> DeleteRuleAsync(string name) =>
        RunNetshAsync($"advfirewall firewall delete rule name=\"{name}\"");

    private static async Task<NetshResult> RunNetshAsync(string arguments)
    {
        var psi = new ProcessStartInfo("netsh.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Impossible de lancer netsh.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new NetshResult(process.ExitCode, ((await output) + (await error)).Trim());
    }
}
