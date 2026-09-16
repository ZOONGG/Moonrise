using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Moonrise.Services;

/// <summary>
/// Relays only BWH's authenticated player request from loopback. This keeps the
/// request in a process that is not routed through ExitLag with Minecraft.
/// </summary>
public sealed partial class BwhApiRelayService : IDisposable
{
    public const int Port = 45678;
    private const int MaximumHeaderBytes = 16 * 1024;
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private readonly Action<string>? _diagnostic;
    private readonly HttpClient _httpClient;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptLoop;

    public BwhApiRelayService(
        Action<string>? diagnostic = null,
        HttpMessageHandler? handler = null,
        int port = Port)
    {
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _diagnostic = diagnostic;
        handler ??= new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6)
        };
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
    }

    public bool IsRunning => _listener is not null;
    public int ListeningPort { get; private set; }

    public void Start()
    {
        if (_listener is not null)
            return;

        var listener = new TcpListener(IPAddress.Loopback, _port);
        try
        {
            listener.Start(64);
        }
        catch (SocketException exception)
        {
            listener.Stop();
            throw new InvalidOperationException(
                $"BWH network bridge could not bind to 127.0.0.1:{_port}. Close the program using that port and retry.",
                exception);
        }

        _listener = listener;
        ListeningPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cancellation = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(listener, _cancellation.Token);
        _diagnostic?.Invoke($"BWH ExitLag network bridge listening on 127.0.0.1:{ListeningPort}; API keys are never logged.");
    }

    internal static bool TryBuildUpstreamUri(string requestTarget, out Uri? upstream)
    {
        upstream = null;
        if (!Uri.TryCreate("http://127.0.0.1" + requestTarget, UriKind.Absolute, out var local))
            return false;
        if (!string.Equals(local.AbsolutePath, "/v2/player/", StringComparison.Ordinal))
            return false;

        var query = local.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        string? uuid = null;
        foreach (var item in query)
        {
            var pair = item.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "uuid", StringComparison.OrdinalIgnoreCase))
                uuid = Uri.UnescapeDataString(pair[1]);
        }
        if (uuid is null || !UuidPattern().IsMatch(uuid))
            return false;

        upstream = new Uri("https://api.hypixel.net/v2/player?uuid=" + Uri.EscapeDataString(uuid));
        return true;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"BWH network bridge stopped unexpectedly: {exception.GetType().Name}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serviceToken)
    {
        using (client)
        {
            client.NoDelay = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stream = client.GetStream();
            try
            {
                var headerBytes = await ReadHeadersAsync(stream, timeout.Token).ConfigureAwait(false);
                if (headerBytes is null)
                {
                    await WriteJsonAsync(stream, 431, "Request Header Fields Too Large", "invalid request", timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }

                var lines = Encoding.ASCII.GetString(headerBytes).Split("\r\n", StringSplitOptions.None);
                var request = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (request.Length != 3 || !string.Equals(request[0], "GET", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(stream, 405, "Method Not Allowed", "unsupported request", timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }
                if (!TryBuildUpstreamUri(request[1], out var upstream))
                {
                    await WriteJsonAsync(stream, 404, "Not Found", "unsupported endpoint", timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }

                string? apiKey = null;
                foreach (var line in lines.Skip(1))
                {
                    var separator = line.IndexOf(':');
                    if (separator <= 0)
                        continue;
                    if (string.Equals(line[..separator].Trim(), "API-Key", StringComparison.OrdinalIgnoreCase))
                        apiKey = line[(separator + 1)..].Trim();
                }
                if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 512)
                {
                    await WriteJsonAsync(stream, 401, "Unauthorized", "missing API key", timeout.Token)
                        .ConfigureAwait(false);
                    return;
                }

                using var outbound = new HttpRequestMessage(HttpMethod.Get, upstream);
                outbound.Headers.TryAddWithoutValidation("API-Key", apiKey);
                outbound.Headers.UserAgent.Add(new ProductInfoHeaderValue("Moonrise-BWH-Relay", "1.0"));
                using var response = await _httpClient.SendAsync(
                    outbound,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                if (body.Length > MaximumResponseBytes)
                    throw new InvalidDataException("Hypixel response exceeded the relay limit.");

                await WriteResponseAsync(
                    stream,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? "Response",
                    response.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8",
                    body,
                    timeout.Token).ConfigureAwait(false);
                _diagnostic?.Invoke($"BWH API relay response: HTTP {(int)response.StatusCode}; bytes={body.Length}.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                await TryWriteGatewayErrorAsync(stream, "API request timed out").ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnostic?.Invoke($"BWH API relay request failed: {exception.GetType().Name}");
                await TryWriteGatewayErrorAsync(stream, "API request failed").ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]?> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var single = new byte[1];
        var matched = 0;
        var terminator = new byte[] { 13, 10, 13, 10 };
        while (buffer.Length < MaximumHeaderBytes)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;
            buffer.WriteByte(single[0]);
            matched = single[0] == terminator[matched] ? matched + 1 : single[0] == terminator[0] ? 1 : 0;
            if (matched == terminator.Length)
                return buffer.ToArray();
        }
        return null;
    }

    private static Task WriteJsonAsync(
        NetworkStream stream,
        int status,
        string reason,
        string cause,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"success\":false,\"cause\":\"{cause}\"}}");
        return WriteResponseAsync(stream, status, reason, "application/json; charset=utf-8", body, cancellationToken);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string reason,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken)
    {
        reason = reason.Replace('\r', ' ').Replace('\n', ' ');
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryWriteGatewayErrorAsync(NetworkStream stream, string cause)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await WriteJsonAsync(stream, 502, "Bad Gateway", cause, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        _listener = null;
        ListeningPort = 0;
        _cancellation?.Dispose();
        _cancellation = null;
        _httpClient.Dispose();
    }

    [GeneratedRegex("^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();
}
