using System.Net.Http;

namespace XSZRemoteChatBridge;

public sealed class ServerChanPushClient
{
    private readonly HttpClient _httpClient;

    public ServerChanPushClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Builds the Server酱 title for a chat message.
    /// </summary>
    public static string BuildChatTitle(string channelName, string senderName)
    {
        var normalizedChannel = string.IsNullOrWhiteSpace(channelName) ? "未知频道" : channelName.Trim();
        var normalizedSender = string.IsNullOrWhiteSpace(senderName) ? "未知发送者" : senderName.Trim();
        return $"{normalizedChannel} | {normalizedSender}";
    }

    /// <summary>
    /// Builds the Server酱 markdown description for a chat message.
    /// </summary>
    public static string BuildChatDescription(string channelName, string senderName, string worldName, string content)
    {
        var normalizedChannel = string.IsNullOrWhiteSpace(channelName) ? "未知频道" : channelName.Trim();
        var normalizedSender = string.IsNullOrWhiteSpace(senderName) ? "未知发送者" : senderName.Trim();
        var normalizedWorld = string.IsNullOrWhiteSpace(worldName) ? "未知世界" : worldName.Trim();
        var normalizedContent = NormalizeMultilineText(content);
        return
            $"**频道**：{normalizedChannel}\n\n" +
            $"**发送者**：{normalizedSender}\n\n" +
            $"**世界**：{normalizedWorld}\n\n" +
            "---\n\n" +
            normalizedContent;
    }

    /// <summary>
    /// Builds the Server酱 title for a disconnect reminder.
    /// </summary>
    public static string BuildDisconnectTitle()
    {
        return "掉线提醒 | 连接中断";
    }

    /// <summary>
    /// Builds the Server酱 markdown description for a disconnect reminder.
    /// </summary>
    public static string BuildDisconnectDescription(string playerDisplay, string worldName, string keyword)
    {
        var normalizedPlayer = string.IsNullOrWhiteSpace(playerDisplay) ? "当前角色" : playerDisplay.Trim();
        var normalizedWorld = string.IsNullOrWhiteSpace(worldName) ? "未知世界" : worldName.Trim();
        var normalizedKeyword = NormalizeMultilineText(keyword);
        return
            $"**角色**：{normalizedPlayer}\n\n" +
            $"**世界**：{normalizedWorld}\n\n" +
            $"**事件**：检测到连接中断弹窗\n\n" +
            "---\n\n" +
            normalizedKeyword;
    }

    /// <summary>
    /// Sends a Server酱 message to the configured send URL.
    /// </summary>
    public async Task SendAsync(string sendUrl, string title, string description, CancellationToken cancellationToken)
    {
        var normalizedUrl = (sendUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUrl))
            throw new InvalidOperationException("Server酱 Send URL 为空");

        using var request = new HttpRequestMessage(HttpMethod.Post, normalizedUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = title ?? string.Empty,
                ["title"] = title ?? string.Empty,
                ["desp"] = description ?? string.Empty
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Server酱 推送失败: status={(int)response.StatusCode}, body={responseBody}");
    }

    private static string NormalizeMultilineText(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "（空）" : normalized;
    }
}
