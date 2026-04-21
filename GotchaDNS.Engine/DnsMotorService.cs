using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GotchaDNS.Engine;

public class DnsMotorService : BackgroundService
{
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private const int BufferSize = 512;
    private static readonly IPEndPoint Upstream = new(IPAddress.Parse("1.1.1.1"), 53);
    // Timeout para respostas upstream (ms)
    private const int UpstreamTimeoutMs = 2000;
    // Limita o número de pacotes processados concorrente para evitar spike de tasks
    private static readonly SemaphoreSlim _concurrencyLimiter = new(100);

    // Domínios de teste bloqueados (exemplo)
    private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "minerador-cripto.net",
        "ads.doubleclick.net"
    };

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
            byte[] buffer = _bufferPool.Rent(BufferSize);

            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint);

                // Dispatch processing and immediately go back to listening
                _ = Task.Run(async () =>
                {
                    await _concurrencyLimiter.WaitAsync();
                    try
                    {
                        await ProcessPacketAsync(socket, buffer, result.ReceivedBytes, result.RemoteEndPoint);
                    }
                    finally
                    {
                        _concurrencyLimiter.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                // If ReceiveFromAsync failed, return the buffer here
                _bufferPool.Return(buffer);
                Console.WriteLine($"Erro no socket (receiving): {ex.Message}");
            }
        }
    }

    private async Task ProcessPacketAsync(Socket socket, byte[] buffer, int receivedBytes, EndPoint clientEndPoint)
    {
            try
            {
                ReadOnlySpan<byte> packetSpan = new ReadOnlySpan<byte>(buffer, 0, receivedBytes);
                string requestedDomain = ParseDomainName(packetSpan);

                // 1. FILTRO DE RUÍDO: Ignora requisições internas do Windows
                if (string.IsNullOrEmpty(requestedDomain) || requestedDomain.EndsWith(".lan") || requestedDomain.EndsWith(".local"))
                {
                    return;
                }

                // 2. REGRA DE BLOQUEIO (Focada nos domínios de teste)
                bool isBlocked = BlockedDomains.Contains(requestedDomain);

                // Antes de bloquear, verifica se o domínio está em whitelist persistida
                if (WhitelistStore.IsWhitelisted(requestedDomain))
                {
                    await ForwardToUpstreamAsync(socket, buffer, receivedBytes, clientEndPoint);
                    return;
                }

                if (isBlocked)
                {
                    await HandleBlockedAsync(requestedDomain, buffer, receivedBytes, socket, clientEndPoint);
                }
                else
                {
                    await ForwardToUpstreamAsync(socket, buffer, receivedBytes, clientEndPoint);
                }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro no processamento do pacote: {ex.Message}");
        }
        finally
        {
            // Devolve a memória pro pool, garantindo a estabilidade
            _bufferPool.Return(buffer);
        }
    }

    private async Task ForwardToUpstreamAsync(Socket socket, byte[] buffer, int receivedBytes, EndPoint clientEndPoint)
    {
        using var upstreamClient = new UdpClient();
        try
        {
            await upstreamClient.SendAsync(buffer.AsMemory(0, receivedBytes), Upstream);

            var receiveTask = upstreamClient.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(UpstreamTimeoutMs));
            if (completed != receiveTask)
            {
                Console.WriteLine("Upstream timeout");
                return; // drop
            }

            var upstreamResponse = await receiveTask;
            await socket.SendToAsync(upstreamResponse.Buffer, SocketFlags.None, clientEndPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro no forwarding upstream: {ex.Message}");
        }
    }

    private async Task HandleBlockedAsync(string requestedDomain, byte[] buffer, int receivedBytes, Socket socket, EndPoint clientEndPoint)
    {
        string reason = requestedDomain == "minerador-cripto.net" ? "Malware" : "Ads";
        Console.WriteLine($"[BLOQUEADO] {requestedDomain}");

        // Joga o log pra fila que a API vai ler
        BlockedLogs.Enqueue(new DnsLogEntry(
            id: Interlocked.Increment(ref _logIdCounter),
            timestamp: DateTime.Now.ToString("o"), // Hora limpa
            domain: requestedDomain,
            clientIp: clientEndPoint is IPEndPoint ie ? ie.Address.ToString() : string.Empty,
            reason: reason
        ));

        // Limita a 50 itens pra não estourar a RAM
        if (BlockedLogs.Count > 50) BlockedLogs.TryDequeue(out _);

        // Registra também na base de dados de forma assíncrona (não bloqueante)
        _ = Task.Run(async () =>
        {
            try
            {
                var optionsBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Data.GotchaDbContext>();
                optionsBuilder.UseSqlite("Data Source=gotcha.db");
                using var db = new Data.GotchaDbContext(optionsBuilder.Options);
                db.DnsLogs.Add(new Data.DnsLogEntity
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Domain = requestedDomain,
                    ClientIp = clientEndPoint is IPEndPoint ie ? ie.Address.ToString() : string.Empty,
                    Reason = reason
                });
                await db.SaveChangesAsync();
            }
            catch { /* swallow logging errors */ }
        });

        // Envia resposta DNS forjada com A = 0.0.0.0 para o cliente
        try
        {
            var packetSpan = new ReadOnlySpan<byte>(buffer, 0, receivedBytes);
            var fake = BuildZeroIpResponse(packetSpan);
            await socket.SendToAsync(fake, SocketFlags.None, clientEndPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao enviar resposta forjada: {ex.Message}");
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

    // Constrói uma resposta DNS simples copiando o ID e flags do pacote original
    // e adicionando uma resposta do tipo A com 0.0.0.0.
    private byte[] BuildZeroIpResponse(ReadOnlySpan<byte> requestPacket)
    {
        if (requestPacket.Length < 12) return Array.Empty<byte>();

        // ID (2 bytes) + Flags (2 bytes) + QDCOUNT(2) + ANCOUNT(2) + NSCOUNT(2) + ARCOUNT(2)
        Span<byte> header = stackalloc byte[12];
        requestPacket.Slice(0, 12).CopyTo(header);

        // Set response flag (0x8000) and set ANCOUNT = 1
        header[2] = (byte)(header[2] | 0x80); // QR = 1
        header[3] = header[3];
        // QDCOUNT remains as in request
        // Set ANCOUNT = 1
        header[6] = 0x00;
        header[7] = 0x01;

        // Copy question section from request
        int offset = 12;
        while (offset < requestPacket.Length && requestPacket[offset] != 0)
        {
            offset++; // percorre labels
        }
        // include the zero length byte and QTYPE(2) + QCLASS(2)
        int qlen = Math.Min(requestPacket.Length - 12, offset - 12 + 5);
        var qsection = requestPacket.Slice(12, qlen).ToArray();

        // Build answer: name pointer to offset 0xC00C, type A(1), class IN(1), TTL 60s, RDLENGTH 4, RDATA 0.0.0.0
        var answer = new byte[12];
        // NAME pointer 0xC00C
        answer[0] = 0xC0;
        answer[1] = 0x0C;
        // TYPE A
        answer[2] = 0x00;
        answer[3] = 0x01;
        // CLASS IN
        answer[4] = 0x00;
        answer[5] = 0x01;
        // TTL (60s)
        answer[6] = 0x00;
        answer[7] = 0x00;
        answer[8] = 0x00;
        answer[9] = 0x3C;
        // RDLENGTH = 4
        answer[10] = 0x00;
        answer[11] = 0x04;

        var response = new byte[header.Length + qsection.Length + answer.Length + 4];
        int pos = 0;
        header.CopyTo(response.AsSpan(pos, header.Length)); pos += header.Length;
        qsection.CopyTo(response.AsSpan(pos, qsection.Length)); pos += qsection.Length;
        answer.CopyTo(response.AsSpan(pos, answer.Length)); pos += answer.Length;
        // RDATA 0.0.0.0
        response[pos++] = 0x00;
        response[pos++] = 0x00;
        response[pos++] = 0x00;
        response[pos++] = 0x00;

        return response;
    }
}