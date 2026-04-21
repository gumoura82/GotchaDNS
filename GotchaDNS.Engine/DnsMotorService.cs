using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace GotchaDNS.Engine;

public class DnsMotorService : BackgroundService
{
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    // Fila em memória para os logs (O SQLite entrará aqui no futuro)
    public static ConcurrentQueue<DnsLogEntry> BlockedLogs { get; } = new();
    private static int _logIdCounter = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 53));
        Console.WriteLine("GotchaDNS Engine: Escutando UDP/53 (Zero-Allocation mode)...");

        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (!stoppingToken.IsCancellationRequested)
        {
            byte[] buffer = _bufferPool.Rent(512);

            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint);
                ReadOnlySpan<byte> packetSpan = new ReadOnlySpan<byte>(buffer, 0, result.ReceivedBytes);
                string requestedDomain = ParseDomainName(packetSpan);

                // 1. FILTRO DE RUÍDO: Ignora requisições internas do Windows
                if (requestedDomain.EndsWith(".lan") || requestedDomain.EndsWith(".local") || string.IsNullOrEmpty(requestedDomain))
                {
                    continue; // Pula para a próxima iteração silenciosamente
                }

                // 2. REGRA DE BLOQUEIO (Focada nos domínios de teste)
                bool isBlocked = requestedDomain == "minerador-cripto.net" || requestedDomain == "ads.doubleclick.net";

                if (isBlocked)
                {
                    string reason = requestedDomain == "minerador-cripto.net" ? "Malware" : "Ads";
                    Console.WriteLine($"[BLOQUEADO] {requestedDomain}");

                    // Joga o log pra fila que a API vai ler
                    BlockedLogs.Enqueue(new DnsLogEntry(
                        id: Interlocked.Increment(ref _logIdCounter),
                        timestamp: DateTime.Now.ToString("o"), // Hora limpa
                        domain: requestedDomain,
                        clientIp: ((IPEndPoint)result.RemoteEndPoint).Address.ToString(),
                        reason: reason
                    ));

                    // Limita a 50 itens pra não estourar a RAM
                    if (BlockedLogs.Count > 50) BlockedLogs.TryDequeue(out _);
                }
                else
                {
                    // 3. FORWARDING: Domínio limpo vai para a Cloudflare
                    using var upstreamClient = new UdpClient();
                    await upstreamClient.SendAsync(buffer.AsMemory(0, result.ReceivedBytes), new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
                    var upstreamResponse = await upstreamClient.ReceiveAsync();
                    await socket.SendToAsync(upstreamResponse.Buffer, SocketFlags.None, result.RemoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro no socket: {ex.Message}");
            }
            finally
            {
                // Devolve a memória pro pool, garantindo a estabilidade
                _bufferPool.Return(buffer);
            }
        }
    }

    private string ParseDomainName(ReadOnlySpan<byte> packet)
    {
        if (packet.Length <= 12) return string.Empty;
        int offset = 12;
        Span<char> domainChars = stackalloc char[256];
        int charIndex = 0;

        while (offset < packet.Length)
        {
            byte labelLength = packet[offset++];
            if (labelLength == 0 || offset + labelLength > packet.Length) break;
            if (charIndex > 0) domainChars[charIndex++] = '.';

            for (int i = 0; i < labelLength; i++)
                domainChars[charIndex++] = (char)packet[offset++];
        }
        return new string(domainChars.Slice(0, charIndex));
    }
}