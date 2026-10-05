using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DayZServerManager.Services;

/// <summary>Client du protocole RCon de BattlEye (UDP).</summary>
public sealed class RconClient : IDisposable
{
    private sealed class PendingCommand
    {
        public readonly TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string?[]? Parts;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly object _lock = new();
    private readonly Dictionary<byte, PendingCommand> _pending = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _login;
    private byte _sequence;
    private DateTime _lastReceived;

    public bool IsConnected { get; private set; }

    /// <summary>Message envoyé par le serveur (chat, connexions…).</summary>
    public event Action<string>? ServerMessage;

    /// <summary>La connexion a été perdue.</summary>
    public event Action? Disconnected;

    public async Task ConnectAsync(string host, int port, string password)
    {
        Close();
        _cts = new CancellationTokenSource();
        _udp = new UdpClient();
        try
        {
            // Sous Windows, ignore les erreurs « port injoignable » renvoyées tant que le serveur démarre.
            const int SIO_UDP_CONNRESET = -1744830452;
            _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
            // Option non disponible : sans importance.
        }
        _udp.Connect(new IPEndPoint(IPAddress.Parse(host), port));

        _login = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _cts.Token;
        _ = Task.Run(() => ReceiveLoopAsync(token));

        await SendAsync(BuildPacket(0x00, Encoding.UTF8.GetBytes(password)));
        var finished = await Task.WhenAny(_login.Task, Task.Delay(5000));
        if (finished != _login.Task)
        {
            Close();
            throw new TimeoutException("Le serveur ne répond pas au RCon. Il est peut-être encore en train de démarrer (1 à 2 minutes).");
        }
        if (!await _login.Task)
        {
            Close();
            throw new InvalidOperationException("Mot de passe RCon refusé. Vérifie-le dans l'onglet Configuration.");
        }

        IsConnected = true;
        _lastReceived = DateTime.UtcNow;
        _ = Task.Run(() => KeepAliveLoopAsync(token));
    }

    public async Task<string> SendCommandAsync(string command, int timeoutMs = 5000)
    {
        if (!IsConnected || _udp == null) throw new InvalidOperationException("RCon non connecté.");

        var pending = new PendingCommand();
        byte sequence;
        lock (_lock)
        {
            sequence = _sequence++;
            _pending[sequence] = pending;
        }

        var text = Encoding.UTF8.GetBytes(command);
        var payload = new byte[text.Length + 1];
        payload[0] = sequence;
        text.CopyTo(payload, 1);
        var packet = BuildPacket(0x01, payload);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            await SendAsync(packet);
            var finished = await Task.WhenAny(pending.Result.Task, Task.Delay(timeoutMs));
            if (finished == pending.Result.Task) return await pending.Result.Task;
        }

        lock (_lock) _pending.Remove(sequence);
        throw new TimeoutException("Le serveur n'a pas répondu à la commande RCon.");
    }

    // ===== Réception =====

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte[] data;
            try
            {
                var result = await _udp!.ReceiveAsync(token);
                data = result.Buffer;
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            catch (NullReferenceException) { break; }

            if (data.Length < 8 || data[0] != (byte)'B' || data[1] != (byte)'E' || data[6] != 0xFF) continue;
            _lastReceived = DateTime.UtcNow;

            switch (data[7])
            {
                case 0x00:
                    if (data.Length >= 9) _login?.TrySetResult(data[8] == 0x01);
                    break;
                case 0x01:
                    HandleCommandResponse(data);
                    break;
                case 0x02:
                    if (data.Length >= 9)
                    {
                        // Le serveur attend un accusé de réception pour chaque message.
                        _ = SendAsync(BuildPacket(0x02, new[] { data[8] }));
                        ServerMessage?.Invoke(Encoding.UTF8.GetString(data, 9, data.Length - 9));
                    }
                    break;
            }
        }
    }

    private void HandleCommandResponse(byte[] data)
    {
        if (data.Length < 9) return;
        byte sequence = data[8];
        PendingCommand? pending;
        lock (_lock) _pending.TryGetValue(sequence, out pending);
        if (pending == null) return;

        string? complete = null;
        if (data.Length >= 12 && data[9] == 0x00)
        {
            // Réponse découpée en plusieurs paquets.
            int count = data[10], index = data[11];
            pending.Parts ??= new string?[count];
            if (index < pending.Parts.Length)
                pending.Parts[index] = Encoding.UTF8.GetString(data, 12, data.Length - 12);
            if (pending.Parts.All(part => part != null)) complete = string.Concat(pending.Parts);
        }
        else
        {
            complete = Encoding.UTF8.GetString(data, 9, data.Length - 9);
        }

        if (complete == null) return;
        lock (_lock) _pending.Remove(sequence);
        pending.Result.TrySetResult(complete);
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { return; }

            try { await SendCommandAsync("", 3000); }
            catch { /* vérifié juste après */ }

            if (DateTime.UtcNow - _lastReceived > TimeSpan.FromSeconds(75))
            {
                Close();
                Disconnected?.Invoke();
                return;
            }
        }
    }

    // ===== Envoi =====

    private async Task SendAsync(byte[] packet)
    {
        var udp = _udp;
        if (udp == null) return;
        try { await udp.SendAsync(packet, packet.Length); }
        catch { /* connexion fermée */ }
    }

    private static byte[] BuildPacket(byte type, byte[] payload)
    {
        var body = new byte[payload.Length + 2];
        body[0] = 0xFF;
        body[1] = type;
        payload.CopyTo(body, 2);

        var crc = BitConverter.GetBytes(Crc32(body));
        var packet = new byte[body.Length + 6];
        packet[0] = (byte)'B';
        packet[1] = (byte)'E';
        Array.Copy(crc, 0, packet, 2, 4);
        body.CopyTo(packet, 6);
        return packet;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    // ===== Fermeture =====

    private void Close()
    {
        IsConnected = false;
        try { _cts?.Cancel(); } catch { /* déjà annulé */ }
        try { _udp?.Dispose(); } catch { /* déjà fermé */ }
        _udp = null;
        lock (_lock)
        {
            foreach (var pending in _pending.Values) pending.Result.TrySetCanceled();
            _pending.Clear();
        }
    }

    public void Dispose() => Close();
}
