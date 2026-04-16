using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace XSZRemoteChatBridge;

public sealed class RemoteChatBridgeModule : IDisposable
{
    private const string DisconnectDialogAddonName = "Dialogue";
    private const string DisconnectDialogKeyword = "失去了与服务器的连接。";
    private const int DisconnectNodeScanMaxCount = 2048;

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
    private readonly ServerChanPushClient _serverChanPushClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _startGate = new();
    private readonly HashSet<nint> _disconnectReminderTriggeredAddons = [];
    private Task? _pullLoopTask;
    private BridgeWebSocketClient? _wsClient;
    private long _lastUploadAtMs;
    private bool _started;

    public RemoteChatBridgeModule(PluginServices services, BridgeOptions options)
    {
        _services = services;
        _options = options;
        _options.Normalize();
        _serverChanPushClient = new ServerChanPushClient(HttpClient);
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

        if (_options.EnableDisconnectReminder)
        {
            _services.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, DisconnectDialogAddonName, OnDialoguePostDraw);
            _services.AddonLifecycle.RegisterListener(AddonEvent.PostHide, DisconnectDialogAddonName, OnDialogueLifecycleCleanup);
            _services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, DisconnectDialogAddonName, OnDialogueLifecycleCleanup);
            _services.Log.Information(
                $"[RemoteChatBridge] 已启用掉线提醒（监听 {DisconnectDialogAddonName} / {AddonEvent.PostDraw},{AddonEvent.PostHide},{AddonEvent.PreFinalize}），" +
                $"Targets={BuildEnabledPushTargetsSummary()}");
        }

        if (_options.EnableUpstream || _options.LogAllChatMessages)
        {
            _services.Chat.ChatMessage += OnChatMessage;
            if (_options.EnableUpstream)
                _services.Log.Information($"[RemoteChatBridge] 已启用聊天上行桥接，Targets={BuildEnabledPushTargetsSummary()}");
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

        try
        {
            _services.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, DisconnectDialogAddonName, OnDialoguePostDraw);
            _services.AddonLifecycle.UnregisterListener(AddonEvent.PostHide, DisconnectDialogAddonName, OnDialogueLifecycleCleanup);
            _services.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, DisconnectDialogAddonName, OnDialogueLifecycleCleanup);
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

        if (string.IsNullOrWhiteSpace(content))
            return;

        IReadOnlyCollection<string> resolvedKeywordRules = [];
        var uploadAllByChannel = BridgeProtocol.IsChannelAllowed(
            type,
            _options.UploadAllChannelList,
            _options.UploadAllCustomChannelList);
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
        _ = Task.Run(
            () => DispatchChatPushTargetsAsync(chatTypeDisplay, senderName, worldName, content, payload, _disposeCts.Token),
            _disposeCts.Token);
    }

    private void OnDialoguePostDraw(AddonEvent eventType, AddonArgs args)
    {
        if (!_options.Enabled || !_options.EnableDisconnectReminder || _disposeCts.IsCancellationRequested)
            return;

        var addonAddress = args.Addon.Address;
        if (addonAddress == 0)
            return;

        if (_disconnectReminderTriggeredAddons.Contains(addonAddress))
            return;

        if (!ContainsDisconnectMarker(args.Addon))
            return;

        _disconnectReminderTriggeredAddons.Add(addonAddress);
        _ = Task.Run(() => PushDisconnectReminderAsync(_disposeCts.Token), _disposeCts.Token);
    }

    private void OnDialogueLifecycleCleanup(AddonEvent eventType, AddonArgs args)
    {
        var addonAddress = args.Addon.Address;
        if (addonAddress == 0)
            return;

        _disconnectReminderTriggeredAddons.Remove(addonAddress);
    }

    private bool ContainsDisconnectMarker(Dalamud.Game.NativeWrapper.AtkUnitBasePtr addonPtr)
    {
        return TryMatchDisconnectKeywordInNodeText(addonPtr) || TryMatchDisconnectKeywordInAtkValues(addonPtr);
    }

    private bool TryMatchDisconnectKeywordInAtkValues(Dalamud.Game.NativeWrapper.AtkUnitBasePtr addonPtr)
    {
        foreach (var valuePtr in addonPtr.AtkValues)
        {
            if (valuePtr.IsNull)
                continue;

            object? rawValue;
            try
            {
                rawValue = valuePtr.GetValue();
            }
            catch
            {
                continue;
            }

            var text = rawValue switch
            {
                string plainText => plainText,
                ReadOnlySeString seString => seString.ExtractText(),
                _ => rawValue?.ToString() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (IsDisconnectKeywordMatched(text))
                return true;
        }

        return false;
    }

    private unsafe bool TryMatchDisconnectKeywordInNodeText(Dalamud.Game.NativeWrapper.AtkUnitBasePtr addonPtr)
    {
        if (addonPtr.IsNull)
            return false;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon == null || addon->RootNode == null)
            return false;

        var scannedTreeNodeCount = 0;
        var scannedNodeListNodeCount = 0;
        var stack = new Stack<nint>();
        var visitedTreeNodeAddress = new HashSet<nint>();
        var visitedNodeListAddress = new HashSet<nint>();

        bool TryMatchTextNode(AtkResNode* node)
        {
            if (node->Type != NodeType.Text)
                return false;

            var textNode = node->GetAsAtkTextNode();
            if (textNode == null)
                return false;

            string nodeText = string.Empty;
            string originalText = string.Empty;
            try
            {
                nodeText = textNode->NodeText.ExtractText();
            }
            catch
            {
                // ignore
            }

            try
            {
                originalText = textNode->OriginalTextPointer.ExtractText();
            }
            catch
            {
                // ignore
            }

            if (string.IsNullOrWhiteSpace(nodeText) && string.IsNullOrWhiteSpace(originalText))
                return false;

            if (IsDisconnectKeywordMatched(nodeText))
                return true;

            if (IsDisconnectKeywordMatched(originalText))
                return true;

            return false;
        }

        bool TryScanNodeList(AtkUldManager* uldManager)
        {
            if (uldManager == null || uldManager->NodeList == null || uldManager->NodeListCount == 0)
                return false;

            var nodeListAddress = (nint)uldManager->NodeList;
            if (!visitedNodeListAddress.Add(nodeListAddress))
                return false;

            for (var i = 0; i < uldManager->NodeListCount && scannedNodeListNodeCount < DisconnectNodeScanMaxCount; i++)
            {
                var node = uldManager->NodeList[i];
                if (node == null)
                    continue;

                scannedNodeListNodeCount++;
                if (TryMatchTextNode(node))
                    return true;

                var componentNode = node->GetAsAtkComponentNode();
                if (componentNode == null || componentNode->Component == null)
                    continue;

                if (TryScanNodeList(&componentNode->Component->UldManager))
                    return true;
            }

            return false;
        }

        stack.Push((nint)addon->RootNode);
        while (stack.Count > 0 && scannedTreeNodeCount < DisconnectNodeScanMaxCount)
        {
            var nodeAddress = stack.Pop();
            if (nodeAddress == 0 || !visitedTreeNodeAddress.Add(nodeAddress))
                continue;

            var node = (AtkResNode*)nodeAddress;
            scannedTreeNodeCount++;

            if (node->NextSiblingNode != null)
                stack.Push((nint)node->NextSiblingNode);
            if (node->ChildNode != null)
                stack.Push((nint)node->ChildNode);

            if (TryMatchTextNode(node))
                return true;

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            if (componentNode->Component->UldManager.RootNode != null)
                stack.Push((nint)componentNode->Component->UldManager.RootNode);

            if (TryScanNodeList(&componentNode->Component->UldManager))
                return true;
        }

        if (TryScanNodeList(&addon->UldManager))
            return true;
        return false;
    }

    private async Task PushDisconnectReminderAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var localPlayerName = _services.ObjectTable.LocalPlayer?.Name.TextValue?.Trim() ?? string.Empty;
        var worldName = _services.ObjectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString() ?? string.Empty;
        var playerDisplay = BuildPlayerDisplay(localPlayerName, worldName);
        var content = $"【掉线提醒】检测到连接中断：{DisconnectDialogKeyword}（{playerDisplay}）";
        var payload = BridgeProtocol.BuildPayload(XivChatType.SystemMessage, "系统", worldName, content);
        await DispatchDisconnectReminderTargetsAsync(payload, playerDisplay, worldName, cancellationToken).ConfigureAwait(false);
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

    private async Task DispatchChatPushTargetsAsync(
        string chatTypeDisplay,
        string senderName,
        string worldName,
        string content,
        BridgePayload payload,
        CancellationToken cancellationToken)
    {
        var serverChanTitle = ServerChanPushClient.BuildChatTitle(chatTypeDisplay, senderName);
        var serverChanDescription = ServerChanPushClient.BuildChatDescription(chatTypeDisplay, senderName, worldName, content);
        await DispatchPushTargetsAsync(payload, serverChanTitle, serverChanDescription, "聊天上行", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DispatchDisconnectReminderTargetsAsync(
        BridgePayload payload,
        string playerDisplay,
        string worldName,
        CancellationToken cancellationToken)
    {
        var serverChanTitle = ServerChanPushClient.BuildDisconnectTitle();
        var serverChanDescription = ServerChanPushClient.BuildDisconnectDescription(
            playerDisplay,
            worldName,
            DisconnectDialogKeyword);
        await DispatchPushTargetsAsync(payload, serverChanTitle, serverChanDescription, "掉线提醒", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DispatchPushTargetsAsync(
        BridgePayload payload,
        string serverChanTitle,
        string serverChanDescription,
        string scenarioName,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(2);
        if (IsBotPushAvailable())
            tasks.Add(UploadWithRetryAsync(payload, cancellationToken));
        if (IsServerChanPushAvailable())
            tasks.Add(SendServerChanAsync(serverChanTitle, serverChanDescription, scenarioName, cancellationToken));

        if (tasks.Count == 0)
        {
            HandleDropped($"{scenarioName}未配置可用推送目标，跳过推送");
            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task SendServerChanAsync(
        string title,
        string description,
        string scenarioName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.HttpTimeoutMs);
            await _serverChanPushClient
                .SendAsync(_options.ServerChanSendUrl, title, description, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _services.Log.Warning($"[RemoteChatBridge] Server酱 {scenarioName}超时，推送结束");
        }
        catch (Exception ex)
        {
            _services.Log.Warning($"[RemoteChatBridge] Server酱 {scenarioName}失败: {ex.Message}");
        }
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
                    _services.Log.Warning($"[RemoteChatBridge] 机器人推送失败: status={statusCode}, body={content}");
                    return;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning("[RemoteChatBridge] 机器人推送超时，重试结束");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempt)
                {
                    _services.Log.Warning($"[RemoteChatBridge] 机器人推送异常，重试结束: {ex.Message}");
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

    private static bool IsDisconnectKeywordMatched(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Contains(DisconnectDialogKeyword, StringComparison.Ordinal);
    }

    private bool IsBotPushAvailable()
    {
        return _options.EnableBotPush && GetBotPushMissingFields().Count == 0;
    }

    private bool IsServerChanPushAvailable()
    {
        return _options.EnableServerChanPush && GetServerChanPushMissingFields().Count == 0;
    }

    private List<string> GetBotPushMissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.IngestEndpoint))
            missing.Add(nameof(_options.IngestEndpoint));
        if (string.IsNullOrWhiteSpace(_options.BridgeKey))
            missing.Add(nameof(_options.BridgeKey));
        if (string.IsNullOrWhiteSpace(_options.BridgeSecret))
            missing.Add(nameof(_options.BridgeSecret));
        return missing;
    }

    private List<string> GetServerChanPushMissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.ServerChanSendUrl))
            missing.Add(nameof(_options.ServerChanSendUrl));
        return missing;
    }

    private string BuildEnabledPushTargetsSummary()
    {
        var targets = new List<string>();
        if (_options.EnableBotPush)
            targets.Add("bot");
        if (_options.EnableServerChanPush)
            targets.Add("serverchan");
        return targets.Count == 0 ? "none" : string.Join(",", targets);
    }

    private static string BuildPlayerDisplay(string localPlayerName, string worldName)
    {
        if (string.IsNullOrWhiteSpace(localPlayerName))
            return "当前角色";

        return string.IsNullOrWhiteSpace(worldName)
            ? localPlayerName
            : $"{localPlayerName}@{worldName}";
    }

    private void ValidateBridgeConfiguration()
    {
        if (_options.EnableUpstream)
            ValidatePushTargets(
                "聊天上行",
                "当前聊天消息不会推送到任何目标。");

        if (_options.EnableDisconnectReminder)
            ValidatePushTargets(
                "掉线提醒",
                "检测到掉线弹窗时将不会推送到任何目标。");

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

    private void ValidatePushTargets(string scenarioName, string noTargetMessage)
    {
        if (!_options.EnableBotPush && !_options.EnableServerChanPush)
        {
            _services.Log.Warning($"[RemoteChatBridge] {scenarioName}未启用任何推送目标。{noTargetMessage}");
            return;
        }

        if (_options.EnableBotPush)
        {
            var botMissing = GetBotPushMissingFields();
            if (botMissing.Count > 0)
            {
                _services.Log.Warning(
                    $"[RemoteChatBridge] {scenarioName}的机器人推送配置不完整: {string.Join(", ", botMissing)}。");
            }
        }

        if (_options.EnableServerChanPush)
        {
            var serverChanMissing = GetServerChanPushMissingFields();
            if (serverChanMissing.Count > 0)
            {
                _services.Log.Warning(
                    $"[RemoteChatBridge] {scenarioName}的 Server酱 配置不完整: {string.Join(", ", serverChanMissing)}。");
            }
        }
    }
}
