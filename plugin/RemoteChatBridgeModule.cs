using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace XSZRemoteChatBridge;

public sealed class RemoteChatBridgeModule : IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<XivChatType> AllChatTypes = [.. Enum.GetValues<XivChatType>()];

    private readonly PluginServices _services;
    private readonly BridgeOptions _options;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _startGate = new();
    private Task? _pullLoopTask;
    private BridgeWebSocketClient? _wsClient;
    private long _lastUploadAtMs;
    private bool _started;

    public RemoteChatBridgeModule(PluginServices services, BridgeOptions options)
    {
        _services = services;
        _options = options;
        _options.Normalize();
    }

    public void Init()
    {
        lock (_startGate)
        {
            if (_started)
                return;
            _started = true;
        }

        if (!_options.Enabled)
        {
            _services.Log.Information("[RemoteChatBridge] 插件已启用但桥接开关关闭");
            return;
        }

        ValidateBridgeConfiguration();

        if (_options.EnableUpstream || _options.LogAllChatMessages)
        {
            _services.Chat.ChatMessage += OnChatMessage;
            if (_options.EnableUpstream)
                _services.Log.Information("[RemoteChatBridge] 已启用聊天上行桥接");
            else
                _services.Log.Information("[RemoteChatBridge] 已启用聊天调试日志（仅记录，不上行）");
        }

        if (_options.EnableDownstream)
        {
            if (_options.EnableWebSocketDownstream)
            {
                _wsClient = new BridgeWebSocketClient(
                    _options,
                    DispatchDownlinkContentAsync,
                    () => PullAndDispatchAsync(_disposeCts.Token),
                    message => _services.Log.Information($"[RemoteChatBridge] {message}"),
                    message => _services.Log.Warning($"[RemoteChatBridge] {message}"));
                _wsClient.Start();
                _services.Log.Information("[RemoteChatBridge] 已启用 WebSocket 下行");
            }
            else
            {
                _pullLoopTask = Task.Run(() => PullLoopAsync(_disposeCts.Token), _disposeCts.Token);
                _services.Log.Information("[RemoteChatBridge] 已启用 pull 下行");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _services.Chat.ChatMessage -= OnChatMessage;
        }
        catch
        {
            // ignore
        }

        _disposeCts.Cancel();
        _wsClient?.Dispose();
        _wsClient = null;

        if (_pullLoopTask != null)
        {
            try
            {
                _pullLoopTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore
            }
        }

        _disposeCts.Dispose();
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        var senderName = sender.TextValue?.Trim() ?? string.Empty;
        var content = message.TextValue?.Trim() ?? string.Empty;
        var chatTypeDisplay = BridgeProtocol.GetChatTypeDisplayName(type);

        if (_options.LogAllChatMessages)
        {
            _services.Log.Information(
                $"[RemoteChatBridge] 聊天调试: 频道={chatTypeDisplay}({type}/{(int)type}) 发送者={senderName} 内容={content}");
        }

        if (!_options.Enabled || !_options.EnableUpstream)
            return;

        if (string.IsNullOrWhiteSpace(_options.BridgeSecret) ||
            string.IsNullOrWhiteSpace(_options.BridgeKey) ||
            string.IsNullOrWhiteSpace(_options.IngestEndpoint))
        {
            HandleDropped("上行配置不完整，跳过消息");
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
            return;

        IReadOnlyCollection<string> resolvedKeywordRules = [];
        var uploadAllByChannel = _options.UploadAllChannelList.Contains(type);
        if (!uploadAllByChannel && !BridgeProtocol.TryResolveKeywordRules(
                type,
                _options.KeywordChannelRules,
                AllChatTypes,
                _options.KeywordRules,
                _options.KeywordCaseSensitive,
                out resolvedKeywordRules))
        {
            HandleDropped($"频道未命中: {type}");
            return;
        }

        if (!uploadAllByChannel && resolvedKeywordRules.Count == 0)
        {
            HandleDropped("关键词规则为空");
            return;
        }

        var useRegex = _options.KeywordMatchMode == BridgeKeywordMatchMode.Any || _options.KeywordUseRegex;
        if (!uploadAllByChannel && !BridgeProtocol.IsKeywordMatched(
                content,
                resolvedKeywordRules,
                _options.KeywordMatchMode,
                _options.KeywordCaseSensitive,
                useRegex))
        {
            HandleDropped("关键词未命中");
            return;
        }

        if (!TryPassRateLimit(_options.MinUploadIntervalMs))
        {
            HandleDropped($"触发节流: {_options.MinUploadIntervalMs}ms");
            return;
        }

        var worldName = _services.ObjectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString() ?? string.Empty;
        var payload = BridgeProtocol.BuildPayload(type, senderName, worldName, content);
        _ = Task.Run(() => UploadWithRetryAsync(payload, _disposeCts.Token), _disposeCts.Token);
    }

    private async Task PullLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PullAndDispatchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _services.Log.Warning($"[RemoteChatBridge] pull 循环异常: {ex.Message}");
            }

            try
            {
                await Task.Delay(_options.PullIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task PullAndDispatchAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableDownstream || string.IsNullOrWhiteSpace(_options.PullEndpoint))
            return;

        var requestBody = new BridgePullRequest
        {
            Limit = Math.Clamp(_options.PullBatchSize, 1, 20)
        };

        var rawBody = JsonSerializer.Serialize(requestBody, SerializerOptions);

        var maxAttempt = _options.PullRetryCount + 1;
        for (var attempt = 1; attempt <= maxAttempt; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signature = BridgeProtocol.ComputeSignature(timestamp, rawBody, _options.BridgeSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.PullEndpoint)
            {
                Content = new StringContent(rawBody, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Bridge-Key", _options.BridgeKey);
            request.Headers.TryAddWithoutValidation("X-Bridge-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Bridge-Signature", signature);
            request.Headers.TryAddWithoutValidation("X-Bridge-Nonce", BridgeProtocol.CreateEventId());

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.HttpTimeoutMs);
                using var response = await HttpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt >= maxAttempt || !ShouldRetry(response.StatusCode))
                        return;
                }
                else
                {
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(responseText))
                        return;

                    BridgePullResponse? payload;
                    try
                    {
                        payload = JsonSerializer.Deserialize<BridgePullResponse>(responseText, DeserializerOptions);
                    }
                    catch (Exception ex)
                    {
                        _services.Log.Warning($"[RemoteChatBridge] pull 响应解析失败: {ex.Message}");
                        return;
                    }

                    if (payload?.Messages == null || payload.Messages.Count == 0)
                        return;

                    foreach (var item in payload.Messages)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;
                        await DispatchDownlinkContentAsync(item.Content).ConfigureAwait(false);
                    }

                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning("[RemoteChatBridge] pull 超时，重试结束");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning($"[RemoteChatBridge] pull 异常，重试结束: {ex.Message}");
                    return;
                }
            }

            if (_options.PullRetryDelayMs > 0)
                await Task.Delay(_options.PullRetryDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchDownlinkContentAsync(string content)
    {
        var normalizedCommand = BuildDownlinkCommand(content, _options.DownlinkPrefix);
        if (string.IsNullOrWhiteSpace(normalizedCommand))
            return;

        try
        {
            await _services.Framework.RunOnFrameworkThread(() =>
            {
                var textToSend = NormalizeDownlinkText(normalizedCommand);
                if (string.IsNullOrWhiteSpace(textToSend))
                    return;

                var isSlashCommand = textToSend.StartsWith("/", StringComparison.Ordinal);
                var succeeded = isSlashCommand
                    ? GameChatSender.TrySendCommand(textToSend, out var sendError)
                    : GameChatSender.TrySendMessage(GameChatSender.SanitiseText(textToSend), saveToHistory: false, out sendError);

                if (!succeeded)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(sendError)
                        ? "unknown downlink send failure"
                        : sendError);
                }
            }).ConfigureAwait(false);

            if (_options.LogDownlinkMessages)
                _services.Log.Information($"[RemoteChatBridge] 下行执行成功: {normalizedCommand}");
        }
        catch (Exception ex)
        {
            _services.Log.Warning($"[RemoteChatBridge] 下行执行失败: {ex.Message}, command={normalizedCommand}");
        }
    }

    private static string BuildDownlinkCommand(string content, string prefix)
    {
        var normalized = (content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.StartsWith("/", StringComparison.Ordinal))
            return normalized;

        var normalizedPrefix = (prefix ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
            return normalized;

        return normalizedPrefix.StartsWith("/", StringComparison.Ordinal)
            ? $"{normalizedPrefix} {normalized}"
            : $"{normalizedPrefix}{normalized}";
    }

    private static string NormalizeDownlinkText(string content)
    {
        return (content ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private async Task UploadWithRetryAsync(BridgePayload payload, CancellationToken cancellationToken)
    {
        var rawBody = JsonSerializer.Serialize(payload, SerializerOptions);
        var maxAttempt = _options.MaxRetryCount + 1;

        for (var attempt = 1; attempt <= maxAttempt; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signature = BridgeProtocol.ComputeSignature(timestamp, rawBody, _options.BridgeSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.IngestEndpoint)
            {
                Content = new StringContent(rawBody, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Bridge-Key", _options.BridgeKey);
            request.Headers.TryAddWithoutValidation("X-Bridge-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Bridge-Signature", signature);
            request.Headers.TryAddWithoutValidation("X-Bridge-Nonce", payload.EventId);

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.HttpTimeoutMs);
                using var response = await HttpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return;

                var canRetry = ShouldRetry(response.StatusCode) && attempt < maxAttempt;
                if (!canRetry)
                {
                    var statusCode = (int)response.StatusCode;
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _services.Log.Warning($"[RemoteChatBridge] 上行失败: status={statusCode}, body={content}");
                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning("[RemoteChatBridge] 上行超时，重试结束");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning($"[RemoteChatBridge] 上行异常，重试结束: {ex.Message}");
                    return;
                }
            }

            if (_options.RetryDelayMs > 0)
                await Task.Delay(_options.RetryDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == (HttpStatusCode)429 || code >= 500;
    }

    private bool TryPassRateLimit(int minIntervalMs)
    {
        if (minIntervalMs <= 0)
            return true;

        while (true)
        {
            var nowMs = Environment.TickCount64;
            var lastMs = Interlocked.Read(ref _lastUploadAtMs);
            if (nowMs - lastMs < minIntervalMs)
                return false;

            if (Interlocked.CompareExchange(ref _lastUploadAtMs, nowMs, lastMs) == lastMs)
                return true;
        }
    }

    private void HandleDropped(string reason)
    {
        if (_options.LogDroppedMessages)
            _services.Log.Debug($"[RemoteChatBridge] 丢弃消息: {reason}");
    }

    private void ValidateBridgeConfiguration()
    {
        if (_options.EnableUpstream)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_options.IngestEndpoint))
                missing.Add(nameof(_options.IngestEndpoint));
            if (string.IsNullOrWhiteSpace(_options.BridgeKey))
                missing.Add(nameof(_options.BridgeKey));
            if (string.IsNullOrWhiteSpace(_options.BridgeSecret))
                missing.Add(nameof(_options.BridgeSecret));
            if (missing.Count > 0)
            {
                _services.Log.Warning(
                    $"[RemoteChatBridge] 上行配置不完整: {string.Join(", ", missing)}。当前聊天消息不会上行到机器人。");
            }
        }

        if (_options.EnableDownstream)
        {
            if (_options.EnableWebSocketDownstream)
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(_options.WebSocketEndpoint))
                    missing.Add(nameof(_options.WebSocketEndpoint));
                if (string.IsNullOrWhiteSpace(_options.BridgeKey))
                    missing.Add(nameof(_options.BridgeKey));
                if (string.IsNullOrWhiteSpace(_options.BridgeSecret))
                    missing.Add(nameof(_options.BridgeSecret));
                if (missing.Count > 0)
                {
                    _services.Log.Warning(
                        $"[RemoteChatBridge] WebSocket 下行配置不完整: {string.Join(", ", missing)}。将持续触发重连并回退 pull。");
                }
            }

            if (string.IsNullOrWhiteSpace(_options.PullEndpoint))
            {
                _services.Log.Warning("[RemoteChatBridge] PullEndpoint 为空，WS 异常时将无法回退 pull。");
            }
        }
    }
}
