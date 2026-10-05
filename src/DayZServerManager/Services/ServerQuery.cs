using System.Net.Sockets;
using System.Text;

namespace DayZServerManager.Services;

public record QueryInfo(string Name, string Map, int Players, int MaxPlayers);

/// <summary>Interroge le serveur comme la liste des serveurs du jeu (requête Steam « A2S_INFO »).</summary>
public static class ServerQuery
{
    public static async Task<QueryInfo?> QueryAsync(int queryPort, int timeoutMs = 2000)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect("127.0.0.1", queryPort);

            await udp.SendAsync(BuildRequest(null));
            var response = await ReceiveAsync(udp, timeoutMs);

            // Le serveur peut d'abord demander une « clé » à renvoyer avec la requête.
            if (response is { Length: >= 9 } && response[4] == 0x41)
            {
                await udp.SendAsync(BuildRequest(response[5..9]));
                response = await ReceiveAsync(udp, timeoutMs);
            }
            if (response == null || response.Length < 6 || response[4] != 0x49) return null;

            int index = 6; // en-tête (4) + type (1) + protocole (1)
            var name = ReadString(response, ref index);
            var map = ReadString(response, ref index);
            ReadString(response, ref index); // dossier
            ReadString(response, ref index); // jeu
            index += 2; // identifiant du jeu
            if (index + 1 >= response.Length) return null;
            return new QueryInfo(name, map, response[index], response[index + 1]);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildRequest(byte[]? challenge)
    {
        var bytes = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
        bytes.AddRange(Encoding.ASCII.GetBytes("Source Engine Query"));
        bytes.Add(0);
        if (challenge != null) bytes.AddRange(challenge);
        return bytes.ToArray();
    }

    private static async Task<byte[]?> ReceiveAsync(UdpClient udp, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var result = await udp.ReceiveAsync(cts.Token);
            return result.Buffer;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(byte[] data, ref int index)
    {
        int start = index;
        while (index < data.Length && data[index] != 0) index++;
        var text = Encoding.UTF8.GetString(data, start, index - start);
        index++;
        return text;
    }
}
