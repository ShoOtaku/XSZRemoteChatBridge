using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;

namespace XSZRemoteChatBridge;

public sealed class WsAuthPayload
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = "auth";

    [JsonPropertyName("bridge_key")]
    public string BridgeKey { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

public sealed class WsPushMessage
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class BridgePayload
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "xszremotechatbridge";

    [JsonPropertyName("chat_type")]
    public string ChatType { get; set; } = string.Empty;

    [JsonPropertyName("player")]
    public string Player { get; set; } = string.Empty;

    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("sent_at")]
    public string SentAt { get; set; } = string.Empty;
}

public sealed class BridgePullRequest
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 5;
}

public sealed class BridgeDownlinkMessage
{
    [JsonPropertyName("message_id")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public double CreatedAt { get; set; }

    [JsonPropertyName("sender_user_id")]
    public string SenderUserId { get; set; } = string.Empty;
}

public sealed class BridgePullResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("messages")]
    public List<BridgeDownlinkMessage> Messages { get; set; } = [];
}

public static class BridgeProtocol
{
    private static readonly IReadOnlyDictionary<XivChatType, string> ChatTypeDisplayNames =
        new Dictionary<XivChatType, string>
        {
            [XivChatType.Say] = "说话",
            [XivChatType.Yell] = "呼喊",
            [XivChatType.Shout] = "喊话",
            [XivChatType.TellIncoming] = "私聊-接收",
            [XivChatType.TellOutgoing] = "私聊-发送",
            [XivChatType.Party] = "小队",
            [XivChatType.Alliance] = "团队",
            [XivChatType.FreeCompany] = "部队",
            [XivChatType.NoviceNetwork] = "新人频道",
            [XivChatType.Echo] = "默语",
            [XivChatType.Ls1] = "通讯贝1",
            [XivChatType.Ls2] = "通讯贝2",
            [XivChatType.Ls3] = "通讯贝3",
            [XivChatType.Ls4] = "通讯贝4",
            [XivChatType.Ls5] = "通讯贝5",
            [XivChatType.Ls6] = "通讯贝6",
            [XivChatType.Ls7] = "通讯贝7",
            [XivChatType.Ls8] = "通讯贝8",
            [XivChatType.CrossLinkShell1] = "跨服通讯贝1",
            [XivChatType.CrossLinkShell2] = "跨服通讯贝2",
            [XivChatType.CrossLinkShell3] = "跨服通讯贝3",
            [XivChatType.CrossLinkShell4] = "跨服通讯贝4",
            [XivChatType.CrossLinkShell5] = "跨服通讯贝5",
            [XivChatType.CrossLinkShell6] = "跨服通讯贝6",
            [XivChatType.CrossLinkShell7] = "跨服通讯贝7",
            [XivChatType.CrossLinkShell8] = "跨服通讯贝8",
            [XivChatType.StandardEmote] = "情感表情",
            [XivChatType.SystemMessage] = "系统消息",
            [XivChatType.RetainerSale] = "雇员出售",
            [XivChatType.CustomEmote] = "自定义表情"
        };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string CreateEventId() => Guid.NewGuid().ToString("N");

    public static string CreateNonce() => Guid.NewGuid().ToString("N");

    public static string UnixTimestampNow() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static string ComputeSignature(string timestamp, string rawBody, string secret)
    {
        timestamp ??= string.Empty;
        rawBody ??= string.Empty;
        secret ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }

    public static bool IsChannelAllowed(XivChatType chatType, IReadOnlyCollection<XivChatType> allowList)
    {
        if (allowList == null || allowList.Count == 0)
            return false;
        return allowList.Contains(chatType);
    }

    public static bool TryResolveKeywordRules(
        XivChatType chatType,
        IReadOnlyCollection<BridgeKeywordChannelRule> keywordChannelRules,
        IReadOnlyCollection<XivChatType> fallbackChannels,
        IReadOnlyCollection<string> fallbackKeywordRules,
        bool caseSensitive,
        out IReadOnlyCollection<string> matchedKeywordRules)
    {
        matchedKeywordRules = [];
        var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        if (keywordChannelRules != null && keywordChannelRules.Count > 0)
        {
            var scopedKeywords = keywordChannelRules
                .Where(rule => rule != null &&
                               !string.IsNullOrWhiteSpace(rule.Keyword) &&
                               IsChannelAllowed(chatType, rule.ChannelAllowList))
                .Select(rule => rule.Keyword.Trim())
                .Distinct(comparer)
                .ToArray();

            if (scopedKeywords.Length == 0)
                return false;

            matchedKeywordRules = scopedKeywords;
            return true;
        }

        if (!IsChannelAllowed(chatType, fallbackChannels))
            return false;

        matchedKeywordRules = fallbackKeywordRules ?? [];
        return true;
    }

    public static bool IsKeywordMatched(
        string message,
        IReadOnlyCollection<string> keywordRules,
        BridgeKeywordMatchMode matchMode,
        bool caseSensitive,
        bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        if (keywordRules == null || keywordRules.Count == 0)
            return true;

        var normalizedKeywords = keywordRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Select(rule => rule.Trim())
            .ToArray();
        if (normalizedKeywords.Length == 0)
            return true;

        if (useRegex)
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            bool IsMatchedByRegex(string rule)
            {
                try
                {
                    return Regex.IsMatch(
                        message,
                        rule,
                        options,
                        TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            return matchMode == BridgeKeywordMatchMode.All
                ? normalizedKeywords.All(IsMatchedByRegex)
                : normalizedKeywords.Any(IsMatchedByRegex);
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return matchMode == BridgeKeywordMatchMode.All
            ? normalizedKeywords.All(rule => message.Contains(rule, comparison))
            : normalizedKeywords.Any(rule => message.Contains(rule, comparison));
    }

    public static string GetChatTypeDisplayName(XivChatType chatType)
    {
        return ChatTypeDisplayNames.TryGetValue(chatType, out var displayName)
            ? displayName
            : chatType.ToString();
    }

    public static BridgePayload BuildPayload(XivChatType chatType, string sender, string worldName, string content)
    {
        return new BridgePayload
        {
            EventId = CreateEventId(),
            Source = "xszremotechatbridge",
            ChatType = chatType.ToString(),
            Player = NormalizeSender(sender),
            World = (worldName ?? string.Empty).Trim(),
            Content = content.Trim(),
            SentAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    public static string BuildWsAuthBody(string bridgeKey, string nonce)
    {
        var body = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["bridge_key"] = bridgeKey ?? string.Empty,
            ["nonce"] = nonce ?? string.Empty
        };
        return JsonSerializer.Serialize(body, SerializerOptions);
    }

    public static string BuildWsAuthFrame(string bridgeKey, string secret)
    {
        var nonce = CreateNonce();
        var timestamp = UnixTimestampNow();
        var authBody = BuildWsAuthBody(bridgeKey, nonce);
        var signature = ComputeSignature(timestamp, authBody, secret);
        var auth = new WsAuthPayload
        {
            BridgeKey = bridgeKey ?? string.Empty,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        };
        return JsonSerializer.Serialize(auth, SerializerOptions);
    }

    private static string NormalizeSender(string sender)
    {
        return string.IsNullOrWhiteSpace(sender) ? string.Empty : sender.Trim();
    }
}
