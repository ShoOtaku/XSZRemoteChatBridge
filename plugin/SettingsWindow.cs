using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;

namespace XSZRemoteChatBridge;

public sealed class SettingsWindow
{
    private const int AutoApplyDebounceMs = 500;
    private const string ServerChanDocsUrl = "https://doc.sc3.ft07.com/zh/serverchan3";
    private const string ServerChanApiUrl = "https://doc.sc3.ft07.com/zh/serverchan3/server/api";
    private const string ServerChanInstallUrl = "https://doc.sc3.ft07.com/zh/serverchan3/app/install";
    private const string ServerChanSendKeyUrl = "https://sc3.ft07.com/sendkey";
    private const string ServerChanHomeUrl = "https://sc3.ft07.com";

    private readonly Action<BridgeOptions> _autoApplyAction;
    private readonly Action _reloadAction;
    private readonly Action<string> _openUrlAction;

    private BridgeOptions _draft = new();
    private int _selectedKeywordRuleIndex = -1;
    private string _keywordChannelFilter = string.Empty;
    private string _uploadAllChannelFilter = string.Empty;
    private string _keywordCustomChannelInput = string.Empty;
    private string _uploadAllCustomChannelInput = string.Empty;
    private bool _hasPendingApply;
    private long _nextAutoApplyAtMs;

    public bool IsOpen { get; set; }

    public SettingsWindow(
        BridgeOptions source,
        Action<BridgeOptions> autoApplyAction,
        Action reloadAction,
        Action<string> openUrlAction)
    {
        _autoApplyAction = autoApplyAction;
        _reloadAction = reloadAction;
        _openUrlAction = openUrlAction;
        LoadFrom(source);
    }

    public void LoadFrom(BridgeOptions source)
    {
        _draft = Clone(source);
        _draft.Normalize();
        EnsureSelectedKeywordRuleIndex();
        _keywordChannelFilter = string.Empty;
        _uploadAllChannelFilter = string.Empty;
        _keywordCustomChannelInput = string.Empty;
        _uploadAllCustomChannelInput = string.Empty;
        _hasPendingApply = false;
        _nextAutoApplyAtMs = 0;
    }

    public BridgeOptions BuildOptions()
    {
        var options = Clone(_draft);
        options.KeywordRules = options.KeywordChannelRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Keyword))
            .Select(rule => rule.Keyword.Trim())
            .Distinct(options.KeywordCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.Normalize();
        return options;
    }

    public void Draw()
    {
        if (!IsOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(860, 820), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        if (!ImGui.Begin("XSZRemoteChatBridge 设置", ref open))
        {
            ImGui.End();
            IsOpen = open;
            return;
        }

        ImGui.TextWrapped("命令打开: /xszrcb");
        ImGui.Separator();

        if (ImGui.BeginTabBar("rcb_settings_tabs"))
        {
            if (ImGui.BeginTabItem("基础"))
            {
                DrawBasicPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("高级"))
            {
                DrawAdvancedPage();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Server酱"))
            {
                DrawServerChanPage();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        TryAutoApply(false);

        ImGui.Separator();

        if (ImGui.Button("恢复当前配置", new Vector2(140, 0)))
        {
            _reloadAction();
            _hasPendingApply = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("关闭", new Vector2(100, 0)))
        {
            TryAutoApply(true);
            open = false;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("设置已自动保存并应用");

        ImGui.End();
        IsOpen = open;
    }

    private void DrawBasicPage()
    {
        if (ImGui.CollapsingHeader("基础开关", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSwitches();

        if (ImGui.CollapsingHeader("接口与签名", ImGuiTreeNodeFlags.DefaultOpen))
            DrawEndpoints();

        if (ImGui.CollapsingHeader("上行过滤", ImGuiTreeNodeFlags.DefaultOpen))
            DrawFilterSettings();

        if (ImGui.CollapsingHeader("下行基础设置", ImGuiTreeNodeFlags.DefaultOpen))
            DrawBasicDownstreamSettings();
    }

    private void DrawAdvancedPage()
    {
        if (ImGui.CollapsingHeader("上行重试相关", ImGuiTreeNodeFlags.DefaultOpen))
            DrawUpstreamRetrySettings();

        if (ImGui.CollapsingHeader("下行设置（Pull 回退）", ImGuiTreeNodeFlags.DefaultOpen))
            DrawPullRetrySettings();

        if (ImGui.CollapsingHeader("WebSocket 保活与重连", ImGuiTreeNodeFlags.DefaultOpen))
            DrawWebSocketRetrySettings();

        if (ImGui.CollapsingHeader("调试日志", ImGuiTreeNodeFlags.DefaultOpen))
            DrawDebugSettings();
    }

    private void DrawServerChanPage()
    {
        ImGui.TextWrapped("这里整理了 Server酱 接入本插件时最常用的说明和入口。点击下方网址会直接在系统浏览器中打开。");
        ImGui.Spacing();

        ImGui.TextWrapped("使用步骤");
        ImGui.BulletText("1. 打开 SendKey 页面并登录，复制官方提供的 API URL，最省事。");
        ImGui.BulletText("2. 回到本插件的“基础 -> 接口与签名”，启用“Server酱 推送目标”。");
        ImGui.BulletText("3. 将复制到的 API URL 填入“Server酱 Send URL”。");
        ImGui.BulletText("4. 如需手机正常收信，按官方 APP 安装与配置文档完成权限设置。");
        ImGui.BulletText("5. 需要了解 `title / desp` 格式或调试接口时，查看 API 文档。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("推荐直接复制的地址格式");
        ImGui.TextDisabled("https://<uid>.push.ft07.com/send/<sendkey>.send");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawExternalLink("官方首页", ServerChanHomeUrl);
        DrawExternalLink("SendKey 页面", ServerChanSendKeyUrl);
        DrawExternalLink("使用说明书", ServerChanDocsUrl);
        DrawExternalLink("APP 安装和配置", ServerChanInstallUrl);
        DrawExternalLink("服务器端 API", ServerChanApiUrl);
    }

    private void DrawSwitches()
    {
        EditBool("启用桥接", _draft.Enabled, value => _draft.Enabled = value);
        EditBool("启用上行消息推送", _draft.EnableUpstream, value => _draft.EnableUpstream = value);
        EditBool("启用机器人推送目标", _draft.EnableBotPush, value => _draft.EnableBotPush = value);
        EditBool("启用 Server酱 推送目标", _draft.EnableServerChanPush, value => _draft.EnableServerChanPush = value);
        EditBool("启用下行（机器人 -> 游戏聊天）", _draft.EnableDownstream, value => _draft.EnableDownstream = value);
        EditBool("优先 WebSocket 下行", _draft.EnableWebSocketDownstream, value => _draft.EnableWebSocketDownstream = value);
        EditBool("掉线提醒（检测到连接中断弹窗时推送到已启用目标）", _draft.EnableDisconnectReminder, value => _draft.EnableDisconnectReminder = value);
    }

    private void DrawEndpoints()
    {
        EditText("机器人上行地址 IngestEndpoint", _draft.IngestEndpoint, 512, value => _draft.IngestEndpoint = value);
        EditText("Server酱 Send URL", _draft.ServerChanSendUrl, 512, value => _draft.ServerChanSendUrl = value);
        ImGui.TextDisabled("填写官方 send URL，例如 https://<uid>.push.ft07.com/send/<sendkey>.send");
        EditText("下行 Pull 地址", _draft.PullEndpoint, 512, value => _draft.PullEndpoint = value);
        EditText("下行 WebSocket 地址", _draft.WebSocketEndpoint, 512, value => _draft.WebSocketEndpoint = value);
        EditText("Bridge Key", _draft.BridgeKey, 128, value => _draft.BridgeKey = value);
        EditText("Bridge Secret", _draft.BridgeSecret, 256, value => _draft.BridgeSecret = value);
    }

    private void DrawFilterSettings()
    {
        var mode = (int)_draft.KeywordMatchMode;
        if (ImGui.Combo("关键词匹配模式", ref mode, "任意命中\0全部命中\0"))
        {
            _draft.KeywordMatchMode = (BridgeKeywordMatchMode)Math.Clamp(mode, 0, 1);
            if (_draft.KeywordMatchMode == BridgeKeywordMatchMode.Any)
                _draft.KeywordUseRegex = true;
            QueueAutoApply();
        }

        EditBool("关键词区分大小写", _draft.KeywordCaseSensitive, value => _draft.KeywordCaseSensitive = value);
        if (_draft.KeywordMatchMode == BridgeKeywordMatchMode.Any)
        {
            if (!_draft.KeywordUseRegex)
            {
                _draft.KeywordUseRegex = true;
                QueueAutoApply();
            }

            var alwaysRegex = true;
            ImGui.BeginDisabled();
            ImGui.Checkbox("关键词按正则表达式匹配（任意命中时默认启用）", ref alwaysRegex);
            ImGui.EndDisabled();
        }
        else
        {
            EditBool("关键词按正则表达式匹配", _draft.KeywordUseRegex, value => _draft.KeywordUseRegex = value);
        }
        DrawKeywordChannelRulesEditor();

        ImGui.Separator();
        if (ImGui.CollapsingHeader("指定频道消息全部上传（无视关键词）", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (DrawChannelSelector(
                    _draft.UploadAllChannelList,
                    _draft.UploadAllCustomChannelList,
                    ref _uploadAllChannelFilter,
                    ref _uploadAllCustomChannelInput,
                    "频道ID",
                    "channel_select_upload_all",
                    170))
            {
                QueueAutoApply();
            }
        }
    }

    private void DrawKeywordChannelRulesEditor()
    {
        EnsureSelectedKeywordRuleIndex();

        ImGui.TextWrapped("关键词管理：左侧维护关键词，右侧为当前关键词配置频道。");
        var region = ImGui.GetContentRegionAvail();
        var panelHeight = MathF.Max(280f, MathF.Min(420f, region.Y));
        var leftWidth = MathF.Max(220f, region.X * 0.4f);

        ImGui.BeginChild("keyword_panel_left", new Vector2(leftWidth, panelHeight), true);
        DrawKeywordRuleListPane();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("keyword_panel_right", new Vector2(0, panelHeight), true);
        DrawKeywordRuleDetailPane();
        ImGui.EndChild();
    }

    private void DrawKeywordRuleListPane()
    {
        var buttonAreaHeight = 46f;
        ImGui.BeginChild("keyword_rule_items", new Vector2(-1, -buttonAreaHeight), false);
        if (_draft.KeywordChannelRules.Count == 0)
        {
            ImGui.TextDisabled("暂无关键词");
        }
        else
        {
            for (var i = 0; i < _draft.KeywordChannelRules.Count; i++)
            {
                var text = (_draft.KeywordChannelRules[i].Keyword ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    text = "(未命名关键词)";

                var selected = i == _selectedKeywordRuleIndex;
                var itemWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
                ImGui.PushID(i);
                if (ImGui.Selectable(text, selected, ImGuiSelectableFlags.None, new Vector2(itemWidth, 34)))
                {
                    _selectedKeywordRuleIndex = i;
                    _keywordChannelFilter = string.Empty;
                }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("+", new Vector2(36, 30)))
            AddKeywordRule();

        ImGui.SameLine();
        var canRemove = _selectedKeywordRuleIndex >= 0 && _selectedKeywordRuleIndex < _draft.KeywordChannelRules.Count;
        if (!canRemove)
            ImGui.BeginDisabled();
        if (ImGui.Button("-", new Vector2(36, 30)))
            RemoveSelectedKeywordRule();
        if (!canRemove)
            ImGui.EndDisabled();
    }

    private void DrawKeywordRuleDetailPane()
    {
        EnsureSelectedKeywordRuleIndex();
        if (_selectedKeywordRuleIndex < 0 || _selectedKeywordRuleIndex >= _draft.KeywordChannelRules.Count)
        {
            ImGui.TextDisabled("请先在左侧点击 + 新增关键词。");
            return;
        }

        var rule = _draft.KeywordChannelRules[_selectedKeywordRuleIndex];
        rule.ChannelAllowList ??= [];

        ImGui.Text("关键词");
        var keyword = rule.Keyword ?? string.Empty;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##selected_keyword", ref keyword, 256))
        {
            rule.Keyword = keyword;
            QueueAutoApply();
        }

        ImGui.Spacing();
        if (DrawChannelSelector(
                rule.ChannelAllowList,
                rule.CustomChannelAllowList,
                ref _keywordChannelFilter,
                ref _keywordCustomChannelInput,
                "频道ID",
                "channel_select_keyword_detail",
                220))
            QueueAutoApply();

        if (ImGui.Button("全选频道", new Vector2(88, 0)))
        {
            rule.ChannelAllowList = [.. Enum.GetValues<XivChatType>()];
            QueueAutoApply();
        }

        ImGui.SameLine();
        if (ImGui.Button("清空频道", new Vector2(88, 0)))
        {
            rule.ChannelAllowList.Clear();
            rule.CustomChannelAllowList.Clear();
            QueueAutoApply();
        }
    }

    private void AddKeywordRule()
    {
        var keyword = BuildDefaultKeywordName();
        _draft.KeywordChannelRules.Add(new BridgeKeywordChannelRule
        {
            Keyword = keyword,
            ChannelAllowList = [.. Enum.GetValues<XivChatType>()],
            CustomChannelAllowList = []
        });
        _selectedKeywordRuleIndex = _draft.KeywordChannelRules.Count - 1;
        _keywordChannelFilter = string.Empty;
        _keywordCustomChannelInput = string.Empty;
        QueueAutoApply();
    }

    private void RemoveSelectedKeywordRule()
    {
        if (_selectedKeywordRuleIndex < 0 || _selectedKeywordRuleIndex >= _draft.KeywordChannelRules.Count)
            return;

        _draft.KeywordChannelRules.RemoveAt(_selectedKeywordRuleIndex);
        if (_draft.KeywordChannelRules.Count == 0)
            _selectedKeywordRuleIndex = -1;
        else
            _selectedKeywordRuleIndex = Math.Clamp(_selectedKeywordRuleIndex, 0, _draft.KeywordChannelRules.Count - 1);
        _keywordChannelFilter = string.Empty;
        _keywordCustomChannelInput = string.Empty;
        QueueAutoApply();
    }

    private string BuildDefaultKeywordName()
    {
        var comparer = _draft.KeywordCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        for (var index = 1; index <= 9999; index++)
        {
            var candidate = $"关键词{index}";
            var exists = _draft.KeywordChannelRules.Any(rule =>
                comparer.Equals((rule.Keyword ?? string.Empty).Trim(), candidate));
            if (!exists)
                return candidate;
        }

        return $"关键词{DateTimeOffset.Now.ToUnixTimeSeconds()}";
    }

    private void EnsureSelectedKeywordRuleIndex()
    {
        if (_draft.KeywordChannelRules.Count == 0)
        {
            _selectedKeywordRuleIndex = -1;
            return;
        }

        _selectedKeywordRuleIndex = Math.Clamp(_selectedKeywordRuleIndex, 0, _draft.KeywordChannelRules.Count - 1);
    }

    private static bool DrawChannelSelector(
        HashSet<XivChatType> selectedChannels,
        HashSet<int> selectedCustomChannels,
        ref string channelFilter,
        ref string customChannelInput,
        string filterLabel,
        string childId,
        float childHeight)
    {
        selectedChannels ??= [];
        selectedCustomChannels ??= [];
        var changed = false;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(filterLabel, ref channelFilter, 64);
        ImGui.BeginChild(childId, new Vector2(-1, childHeight), true);
        foreach (var chatType in Enum.GetValues<XivChatType>().OrderBy(value => (int)value))
        {
            var searchableText = $"{BridgeProtocol.GetChatTypeDisplayName(chatType)} {chatType} {(int)chatType}";
            if (!string.IsNullOrWhiteSpace(channelFilter) &&
                searchableText.IndexOf(channelFilter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var selected = selectedChannels.Contains(chatType);
            if (!ImGui.Checkbox(BuildChannelDisplayLabel(chatType), ref selected))
                continue;

            if (selected)
                selectedChannels.Add(chatType);
            else
                selectedChannels.Remove(chatType);
            changed = true;
        }

        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.TextDisabled("自定义频道 ID");
        ImGui.SetNextItemWidth(-90);
        ImGui.InputText($"##custom_channel_input_{childId}", ref customChannelInput, 16);
        ImGui.SameLine();
        if (ImGui.Button($"+##custom_channel_add_{childId}", new Vector2(70, 0)) &&
            TryParseCustomChannelId(customChannelInput, out var parsedCustomChannelId))
        {
            changed |= selectedCustomChannels.Add(parsedCustomChannelId);
            customChannelInput = string.Empty;
        }

        if (selectedCustomChannels.Count > 0)
        {
            ImGui.BeginChild($"{childId}_custom_selected", new Vector2(-1, 72), true);
            foreach (var customChannelId in selectedCustomChannels.OrderBy(value => value).ToArray())
            {
                ImGui.PushID(customChannelId);
                ImGui.TextUnformatted(customChannelId.ToString());
                ImGui.SameLine();
                if (ImGui.SmallButton("移除"))
                {
                    selectedCustomChannels.Remove(customChannelId);
                    changed = true;
                }

                ImGui.PopID();
            }

            ImGui.EndChild();
        }

        return changed;
    }

    private static bool TryParseCustomChannelId(string text, out int channelId)
    {
        if (int.TryParse((text ?? string.Empty).Trim(), out channelId) && channelId >= 0)
            return true;

        channelId = 0;
        return false;
    }

    private void DrawBasicDownstreamSettings()
    {
        EditText("下行聊天前缀", _draft.DownlinkPrefix, 64, value => _draft.DownlinkPrefix = value);
    }

    private void DrawUpstreamRetrySettings()
    {
        EditInt("HTTP 超时(ms)", _draft.HttpTimeoutMs, value => _draft.HttpTimeoutMs = value);
        EditInt("最小上报间隔(ms)", _draft.MinUploadIntervalMs, value => _draft.MinUploadIntervalMs = value);
        EditInt("最大重试次数", _draft.MaxRetryCount, value => _draft.MaxRetryCount = value);
        EditInt("重试等待(ms)", _draft.RetryDelayMs, value => _draft.RetryDelayMs = value);
    }

    private void DrawPullRetrySettings()
    {
        EditInt("Pull 间隔(ms)", _draft.PullIntervalMs, value => _draft.PullIntervalMs = value);
        EditInt("Pull 批量大小", _draft.PullBatchSize, value => _draft.PullBatchSize = value);
        EditInt("Pull 最大重试次数", _draft.PullRetryCount, value => _draft.PullRetryCount = value);
        EditInt("Pull 重试等待(ms)", _draft.PullRetryDelayMs, value => _draft.PullRetryDelayMs = value);
    }

    private void DrawWebSocketRetrySettings()
    {
        EditInt("Ping 间隔(秒)", _draft.WsPingIntervalSeconds, value => _draft.WsPingIntervalSeconds = value);
        EditInt("Pong 超时(秒)", _draft.WsPongTimeoutSeconds, value => _draft.WsPongTimeoutSeconds = value);
        EditInt("重连基础延迟(ms)", _draft.WsReconnectBaseDelayMs, value => _draft.WsReconnectBaseDelayMs = value);
        EditInt("重连最大延迟(ms)", _draft.WsReconnectMaxDelayMs, value => _draft.WsReconnectMaxDelayMs = value);
    }

    private void DrawDebugSettings()
    {
        EditBool("输出聊天栏所有消息（频道名称与ID）", _draft.LogAllChatMessages, value => _draft.LogAllChatMessages = value);
        EditBool("记录丢弃消息日志", _draft.LogDroppedMessages, value => _draft.LogDroppedMessages = value);
        EditBool("记录下行执行日志", _draft.LogDownlinkMessages, value => _draft.LogDownlinkMessages = value);
    }

    private void DrawExternalLink(string label, string url)
    {
        ImGui.TextUnformatted(label);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.38f, 0.64f, 0.96f, 1f));
        if (ImGui.Selectable($"{url}##{label}", false))
            _openUrlAction(url);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("点击打开链接");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static string BuildChannelDisplayLabel(XivChatType chatType)
    {
        var displayName = BridgeProtocol.GetChatTypeDisplayName(chatType);
        var originalName = chatType.ToString();
        return string.Equals(displayName, originalName, StringComparison.Ordinal)
            ? $"{displayName} ({(int)chatType})"
            : $"{displayName} ({originalName}/{(int)chatType})";
    }

    private void EditBool(string label, bool currentValue, Action<bool> setter)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            QueueAutoApply();
        }
    }

    private void EditInt(string label, int currentValue, Action<int> setter)
    {
        var value = currentValue;
        if (ImGui.InputInt(label, ref value))
        {
            setter(value);
            QueueAutoApply();
        }
    }

    private void EditText(string label, string currentValue, int maxLength, Action<string> setter)
    {
        var value = currentValue ?? string.Empty;
        if (ImGui.InputText(label, ref value, maxLength))
        {
            setter(value);
            QueueAutoApply();
        }
    }

    private void QueueAutoApply()
    {
        _hasPendingApply = true;
        _nextAutoApplyAtMs = Environment.TickCount64 + AutoApplyDebounceMs;
    }

    private void TryAutoApply(bool force)
    {
        if (!_hasPendingApply)
            return;

        if (!force && Environment.TickCount64 < _nextAutoApplyAtMs)
            return;

        _hasPendingApply = false;
        _autoApplyAction(BuildOptions());
    }

    private static BridgeOptions Clone(BridgeOptions source)
    {
        return new BridgeOptions
        {
            Enabled = source.Enabled,
            EnableUpstream = source.EnableUpstream,
            EnableBotPush = source.EnableBotPush,
            EnableServerChanPush = source.EnableServerChanPush,
            EnableDownstream = source.EnableDownstream,
            EnableWebSocketDownstream = source.EnableWebSocketDownstream,
            EnableDisconnectReminder = source.EnableDisconnectReminder,
            IngestEndpoint = source.IngestEndpoint ?? string.Empty,
            ServerChanSendUrl = source.ServerChanSendUrl ?? string.Empty,
            PullEndpoint = source.PullEndpoint ?? string.Empty,
            WebSocketEndpoint = source.WebSocketEndpoint ?? string.Empty,
            BridgeKey = source.BridgeKey ?? string.Empty,
            BridgeSecret = source.BridgeSecret ?? string.Empty,
            HttpTimeoutMs = source.HttpTimeoutMs,
            MaxRetryCount = source.MaxRetryCount,
            RetryDelayMs = source.RetryDelayMs,
            MinUploadIntervalMs = source.MinUploadIntervalMs,
            ChannelAllowList = source.ChannelAllowList == null ? [] : [.. source.ChannelAllowList],
            UploadAllChannelList = source.UploadAllChannelList == null ? [] : [.. source.UploadAllChannelList],
            UploadAllCustomChannelList = source.UploadAllCustomChannelList == null ? [] : [.. source.UploadAllCustomChannelList],
            KeywordRules = source.KeywordRules == null ? [] : [.. source.KeywordRules],
            KeywordChannelRules = source.KeywordChannelRules == null
                ? []
                : source.KeywordChannelRules.Select(rule => new BridgeKeywordChannelRule
                {
                    Keyword = rule.Keyword ?? string.Empty,
                    ChannelAllowList = rule.ChannelAllowList == null ? [] : [.. rule.ChannelAllowList],
                    CustomChannelAllowList = rule.CustomChannelAllowList == null ? [] : [.. rule.CustomChannelAllowList]
                }).ToList(),
            KeywordMatchMode = source.KeywordMatchMode,
            KeywordCaseSensitive = source.KeywordCaseSensitive,
            KeywordUseRegex = source.KeywordUseRegex,
            LogDroppedMessages = source.LogDroppedMessages,
            LogAllChatMessages = source.LogAllChatMessages,
            PullIntervalMs = source.PullIntervalMs,
            PullBatchSize = source.PullBatchSize,
            PullRetryCount = source.PullRetryCount,
            PullRetryDelayMs = source.PullRetryDelayMs,
            DownlinkPrefix = source.DownlinkPrefix ?? string.Empty,
            LogDownlinkMessages = source.LogDownlinkMessages,
            WsPingIntervalSeconds = source.WsPingIntervalSeconds,
            WsPongTimeoutSeconds = source.WsPongTimeoutSeconds,
            WsReconnectBaseDelayMs = source.WsReconnectBaseDelayMs,
            WsReconnectMaxDelayMs = source.WsReconnectMaxDelayMs
        };
    }
}
