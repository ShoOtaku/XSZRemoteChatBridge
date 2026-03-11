using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XSZRemoteChatBridge;

public sealed class BridgeWebSocketClient : IDisposable
{
    private const int ConflictWarnCooldownMs = 60_000;

    private readonly BridgeOptions _options;
    private readonly Func<string, Task> _onCommand;
    private readonly Func<Task>? _onFallbackPull;
    private readonly Action<string>? _onInfo;
    private readonly Action<string>? _onWarn;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _loopTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _lastConflictWarnAtMs;
    private string _lastConflictWarnReason = string.Empty;
    private int _suppressedConflictWarnCount;

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

        _sendLock.Dispose();
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
                var sessionConflict = IsSessionConflict(ex);
                if (sessionConflict)
                {
                    EmitSessionConflictWarning(ex.Message);
                }
                else
                {
                    _onWarn?.Invoke($"ws 会话异常: {ex.Message}，准备回退 pull 并重连");
                }
                if (_onFallbackPull != null)
                    await _onFallbackPull().ConfigureAwait(false);
                attempt = sessionConflict ? Math.Max(attempt + 1, 4) : attempt + 1;
            }

            var backoff = ComputeReconnectDelayMs(attempt);
            await Task.Delay(Math.Max(backoff, 500), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunSingleSessionAsync(CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(_options.WsPingIntervalSeconds);
        await ws.ConnectAsync(new Uri(_options.WebSocketEndpoint), cancellationToken).ConfigureAwait(false);
        _onInfo?.Invoke($"ws 已连接: {_options.WebSocketEndpoint}");

        var authFrame = BridgeProtocol.BuildWsAuthFrame(_options.BridgeKey, _options.BridgeSecret);
        await SendJsonAsync(ws, authFrame, cancellationToken).ConfigureAwait(false);

        var authFrameResult = await ReceiveFrameAsync(ws, cancellationToken).ConfigureAwait(false);
        if (authFrameResult.IsCloseFrame)
            throw new WebSocketException($"ws 鉴权阶段被关闭: {authFrameResult.CloseDescription}");

        var authResponse = authFrameResult.Text;
        var authNode = JsonNode.Parse(authResponse);
        var authOp = authNode?["op"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(authOp, "auth_ok", StringComparison.OrdinalIgnoreCase))
        {
            var reason = authNode?["reason"]?.GetValue<string>() ?? authResponse;
            throw new InvalidOperationException($"websocket auth failed: {reason}");
        }
        _onInfo?.Invoke("ws 鉴权成功");

        var lastInboundAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = Task.Run(
            () => PingLoopAsync(ws, sessionCts.Token, () => Interlocked.Read(ref lastInboundAtMs)),
            sessionCts.Token);

        try
        {
            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var frameResult = await ReceiveFrameAsync(ws, cancellationToken).ConfigureAwait(false);
                if (frameResult.IsCloseFrame)
                    throw new WebSocketException($"ws closed by peer: {frameResult.CloseDescription}");

                var frame = frameResult.Text;
                Interlocked.Exchange(ref lastInboundAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

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
        finally
        {
            sessionCts.Cancel();
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }
    }

    private async Task PingLoopAsync(
        ClientWebSocket ws,
        CancellationToken cancellationToken,
        Func<long> lastInboundAtMsGetter)
    {
        var interval = TimeSpan.FromSeconds(_options.WsPingIntervalSeconds);
        var timeoutMs = _options.WsPongTimeoutSeconds * 1000L;

        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || ws.State != WebSocketState.Open)
                return;

            var idleMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastInboundAtMsGetter();
            if (idleMs >= timeoutMs)
            {
                ws.Abort();
                throw new TimeoutException($"ws 心跳超时: {idleMs}ms 未收到服务端数据");
            }

            await SendJsonAsync(ws, "{\"op\":\"ping\"}", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendJsonAsync(ClientWebSocket ws, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<WsFrameResult> ReceiveFrameAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                var status = result.CloseStatus?.ToString() ?? "unknown";
                var description = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                    ? "无描述"
                    : result.CloseStatusDescription;
                return WsFrameResult.FromClose($"status={status}, desc={description}");
            }
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return WsFrameResult.FromText(Encoding.UTF8.GetString(ms.ToArray()));
        }
    }

    private readonly record struct WsFrameResult(bool IsCloseFrame, string Text, string CloseDescription)
    {
        public static WsFrameResult FromText(string text) => new(false, text, string.Empty);

        public static WsFrameResult FromClose(string closeDescription) => new(true, string.Empty, closeDescription);
    }

    private static bool IsSessionConflict(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("replaced", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("already_connected", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("status=1012", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("status=1013", StringComparison.OrdinalIgnoreCase);
    }

    private int ComputeReconnectDelayMs(int attempt)
    {
        return Math.Min(
            _options.WsReconnectBaseDelayMs * (int)Math.Pow(2, Math.Min(attempt, 5)),
            _options.WsReconnectMaxDelayMs);
    }

    private void EmitSessionConflictWarning(string message)
    {
        var reason = string.IsNullOrWhiteSpace(message) ? "unknown" : message.Trim();
        var nowMs = Environment.TickCount64;
        var isSameReason = string.Equals(reason, _lastConflictWarnReason, StringComparison.OrdinalIgnoreCase);
        var withinCooldown = nowMs - _lastConflictWarnAtMs < ConflictWarnCooldownMs;

        if (isSameReason && withinCooldown)
        {
            _suppressedConflictWarnCount++;
            return;
        }

        if (_suppressedConflictWarnCount > 0)
        {
            _onInfo?.Invoke($"ws 会话冲突持续出现，最近已抑制 {_suppressedConflictWarnCount} 条重复告警");
            _suppressedConflictWarnCount = 0;
        }

        _lastConflictWarnReason = reason;
        _lastConflictWarnAtMs = nowMs;
        _onWarn?.Invoke($"ws 会话被同 bridge_key 的其他连接占用: {reason}。将延迟重连并回退 pull");
    }
}
