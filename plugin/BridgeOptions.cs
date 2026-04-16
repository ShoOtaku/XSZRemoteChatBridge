using Dalamud.Game.Text;

namespace XSZRemoteChatBridge;

public enum BridgeKeywordMatchMode
{
    Any = 0,
    All = 1
}

public sealed class BridgeKeywordChannelRule
{
    public string Keyword { get; set; } = string.Empty;
    public HashSet<XivChatType> ChannelAllowList { get; set; } = [];
    public HashSet<int> CustomChannelAllowList { get; set; } = [];
}

public sealed class BridgeOptions
{
    public bool Enabled { get; set; } = true;
    public bool EnableUpstream { get; set; } = true;
    public bool EnableBotPush { get; set; } = true;
    public bool EnableServerChanPush { get; set; }
    public bool EnableDownstream { get; set; } = true;
    public bool EnableWebSocketDownstream { get; set; } = true;
    public bool EnableDisconnectReminder { get; set; }

    public string IngestEndpoint { get; set; } = "http://127.0.0.1:8080/ff14/bridge/ingest";
    public string ServerChanSendUrl { get; set; } = string.Empty;
    public string PullEndpoint { get; set; } = "http://127.0.0.1:8080/ff14/bridge/pull";
    public string WebSocketEndpoint { get; set; } = "ws://127.0.0.1:8080/ff14/bridge/ws";

    public string BridgeKey { get; set; } = "xsztoolbox";
    public string BridgeSecret { get; set; } = string.Empty;

    public int HttpTimeoutMs { get; set; } = 5000;
    public int MaxRetryCount { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 800;
    public int MinUploadIntervalMs { get; set; } = 300;

    public HashSet<XivChatType> ChannelAllowList { get; set; } = [XivChatType.Party];
    public HashSet<XivChatType> UploadAllChannelList { get; set; } = [];
    public HashSet<int> UploadAllCustomChannelList { get; set; } = [];
    public List<string> KeywordRules { get; set; } = [];
    public List<BridgeKeywordChannelRule> KeywordChannelRules { get; set; } = [];
    public BridgeKeywordMatchMode KeywordMatchMode { get; set; } = BridgeKeywordMatchMode.Any;
    public bool KeywordCaseSensitive { get; set; }
    public bool KeywordUseRegex { get; set; }
    public bool LogDroppedMessages { get; set; }
    public bool LogAllChatMessages { get; set; }

    public int PullIntervalMs { get; set; } = 1500;
    public int PullBatchSize { get; set; } = 5;
    public int PullRetryCount { get; set; } = 1;
    public int PullRetryDelayMs { get; set; } = 300;
    public string DownlinkPrefix { get; set; } = string.Empty;
    public bool LogDownlinkMessages { get; set; } = true;

    public int WsPingIntervalSeconds { get; set; } = 30;
    public int WsPongTimeoutSeconds { get; set; } = 90;
    public int WsReconnectBaseDelayMs { get; set; } = 1000;
    public int WsReconnectMaxDelayMs { get; set; } = 30000;

    public void Normalize()
    {
        IngestEndpoint = (IngestEndpoint ?? string.Empty).Trim();
        ServerChanSendUrl = (ServerChanSendUrl ?? string.Empty).Trim();
        PullEndpoint = (PullEndpoint ?? string.Empty).Trim();
        WebSocketEndpoint = (WebSocketEndpoint ?? string.Empty).Trim();
        BridgeKey = (BridgeKey ?? string.Empty).Trim();
        BridgeSecret = (BridgeSecret ?? string.Empty).Trim();
        DownlinkPrefix = DownlinkPrefix ?? string.Empty;

        HttpTimeoutMs = Math.Clamp(HttpTimeoutMs, 500, 60000);
        MaxRetryCount = Math.Clamp(MaxRetryCount, 0, 10);
        RetryDelayMs = Math.Clamp(RetryDelayMs, 0, 30000);
        MinUploadIntervalMs = Math.Clamp(MinUploadIntervalMs, 0, 60000);

        PullIntervalMs = Math.Clamp(PullIntervalMs, 500, 60000);
        PullBatchSize = Math.Clamp(PullBatchSize, 1, 20);
        PullRetryCount = Math.Clamp(PullRetryCount, 0, 10);
        PullRetryDelayMs = Math.Clamp(PullRetryDelayMs, 0, 30000);

        WsPingIntervalSeconds = Math.Clamp(WsPingIntervalSeconds, 5, 300);
        WsPongTimeoutSeconds = Math.Clamp(WsPongTimeoutSeconds, 30, 600);
        WsReconnectBaseDelayMs = Math.Clamp(WsReconnectBaseDelayMs, 500, 60000);
        WsReconnectMaxDelayMs = Math.Clamp(WsReconnectMaxDelayMs, WsReconnectBaseDelayMs, 120000);

        if (KeywordMatchMode == BridgeKeywordMatchMode.Any)
            KeywordUseRegex = true;

        ChannelAllowList ??= [];
        ChannelAllowList = [];
        UploadAllChannelList ??= [];
        UploadAllChannelList = [.. UploadAllChannelList];
        UploadAllCustomChannelList = NormalizeCustomChannelIds(UploadAllCustomChannelList);
        KeywordRules = (KeywordRules ?? [])
            .Select(rule => (rule ?? string.Empty).Trim())
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Distinct(KeywordCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToList();

        KeywordChannelRules = (KeywordChannelRules ?? [])
            .Select(rule => new BridgeKeywordChannelRule
            {
                Keyword = (rule?.Keyword ?? string.Empty).Trim(),
                ChannelAllowList = rule?.ChannelAllowList == null ? [] : [.. rule.ChannelAllowList],
                CustomChannelAllowList = NormalizeCustomChannelIds(rule?.CustomChannelAllowList)
            })
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Keyword))
            .GroupBy(rule => rule.Keyword, KeywordCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var merged = new HashSet<XivChatType>();
                var mergedCustom = new HashSet<int>();
                foreach (var item in group)
                {
                    merged.UnionWith(item.ChannelAllowList);
                    mergedCustom.UnionWith(item.CustomChannelAllowList);
                }

                return new BridgeKeywordChannelRule
                {
                    Keyword = group.First().Keyword,
                    ChannelAllowList = merged,
                    CustomChannelAllowList = mergedCustom
                };
            })
            .ToList();

        if (KeywordChannelRules.Count == 0 && KeywordRules.Count > 0)
        {
            KeywordChannelRules = KeywordRules
                .Select(keyword => new BridgeKeywordChannelRule
                {
                    Keyword = keyword,
                    ChannelAllowList = [.. Enum.GetValues<XivChatType>()],
                    CustomChannelAllowList = []
                })
                .ToList();
        }

        if (KeywordRules.Count == 0 && KeywordChannelRules.Count > 0)
        {
            KeywordRules = KeywordChannelRules
                .Select(rule => rule.Keyword)
                .Distinct(KeywordCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static HashSet<int> NormalizeCustomChannelIds(IEnumerable<int>? channelIds)
    {
        return (channelIds ?? [])
            .Where(channelId => channelId >= 0)
            .Distinct()
            .ToHashSet();
    }
}
