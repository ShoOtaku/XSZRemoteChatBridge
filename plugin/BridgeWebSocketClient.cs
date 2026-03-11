using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XSZRemoteChatBridge;

public sealed class BridgeWebSocketClient : IDisposable
{
    private readonly BridgeOptions _options;
    private readonly Func<string, Task> _onCommand;
    private readonly Func<Task>? _onFallbackPull;
    private readonly Action<string>? _onInfo;
    private readonly Action<string>? _onWarn;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _loopTask;

    public BridgeWebSocketClient(
        BridgeOptions options,
        Func<string, Task> onCommand,
        Func<Task>? onFallbackPull = null,
        Action<string>? onInfo = null,
        Action<string>? onWarn = null)
    {
        _options = options;
        _onCommand = onCommand;
        _onFallbackPull = onFallbackPull;
        _onInfo = onInfo;
        _onWarn = onWarn;
    }

    public void Start()
    {
        if (_loopTask != null)
            return;
        _loopTask = Task.Run(() => RunAsync(_disposeCts.Token), _disposeCts.Token);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _disposeCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleSessionAsync(cancellationToken).ConfigureAwait(false);
                attempt = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _onWarn?.Invoke($"ws 会话异常: {ex.Message}，准备回退 pull 并重连");
                if (_onFallbackPull != null)
                    await _onFallbackPull().ConfigureAwait(false);
                attempt++;
            }

            var backoff = Math.Min(
                _options.WsReconnectBaseDelayMs * (int)Math.Pow(2, Math.Min(attempt, 5)),
                _options.WsReconnectMaxDelayMs);
            await Task.Delay(Math.Max(backoff, 500), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunSingleSessionAsync(CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_options.WebSocketEndpoint), cancellationToken).ConfigureAwait(false);
        _onInfo?.Invoke($"ws 已连接: {_options.WebSocketEndpoint}");

        var authFrame = BridgeProtocol.BuildWsAuthFrame(_options.BridgeKey, _options.BridgeSecret);
        await SendJsonAsync(ws, authFrame, cancellationToken).ConfigureAwait(false);

        var authResponse = await ReceiveTextFrameAsync(ws, cancellationToken).ConfigureAwait(false);
        var authOp = JsonNode.Parse(authResponse)?["op"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(authOp, "auth_ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("websocket auth failed");
        _onInfo?.Invoke("ws 鉴权成功");

        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var frame = await ReceiveTextFrameAsync(ws, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(frame))
                throw new WebSocketException("ws closed by peer");

            var node = JsonNode.Parse(frame);
            var op = node?["op"]?.GetValue<string>() ?? string.Empty;
            if (string.Equals(op, "push", StringComparison.OrdinalIgnoreCase))
            {
                var messageId = node?["message_id"]?.GetValue<string>() ?? string.Empty;
                var content = node?["content"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(content))
                    await _onCommand(content).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(messageId))
                {
                    var ack = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["op"] = "ack",
                        ["message_id"] = messageId
                    });
                    await SendJsonAsync(ws, ack, cancellationToken).ConfigureAwait(false);
                }
                continue;
            }

            if (string.Equals(op, "pong", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(op, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await SendJsonAsync(ws, "{\"op\":\"pong\"}", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendJsonAsync(ClientWebSocket ws, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReceiveTextFrameAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return string.Empty;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
