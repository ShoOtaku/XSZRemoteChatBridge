using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;

namespace XSZRemoteChatBridge;

public sealed class SettingsWindow
{
    private const int AutoApplyDebounceMs = 500;

    private readonly Action<BridgeOptions> _autoApplyAction;
    private readonly Action _reloadAction;

    private BridgeOptions _draft = new();
    private int _selectedKeywordRuleIndex = -1;
    private string _keywordChannelFilter = string.Empty;
    private string _fallbackChannelFilter = string.Empty;
    private bool _hasPendingApply;
    private long _nextAutoApplyAtMs;

    public bool IsOpen { get; set; }

    public SettingsWindow(BridgeOptions source, Action<BridgeOptions> autoApplyAction, Action reloadAction)
    {
        _autoApplyAction = autoApplyAction;
        _reloadAction = reloadAction;
        LoadFrom(source);
    }

    public void LoadFrom(BridgeOptions source)
    {
        _draft = Clone(source);
        _draft.Normalize();
        EnsureSelectedKeywordRuleIndex();
        _keywordChannelFilter = string.Empty;
        _fallbackChannelFilter = string.Empty;
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

    private void DrawSwitches()
    {
        EditBool("启用桥接", _draft.Enabled, value => _draft.Enabled = value);
        EditBool("启用上行（游戏聊天 -> 机器人）", _draft.EnableUpstream, value => _draft.EnableUpstream = value);
        EditBool("启用下行（机器人 -> 游戏聊天）", _draft.EnableDownstream, value => _draft.EnableDownstream = value);
        EditBool("优先 WebSocket 下行", _draft.EnableWebSocketDownstream, value => _draft.EnableWebSocketDownstream = value);
    }

    private void DrawEndpoints()
    {
        EditText("上行地址 IngestEndpoint", _draft.IngestEndpoint, 512, value => _draft.IngestEndpoint = value);
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
            QueueAutoApply();
        }

        EditBool("关键词区分大小写", _draft.KeywordCaseSensitive, value => _draft.KeywordCaseSensitive = value);
        EditBool("关键词按正则表达式匹配", _draft.KeywordUseRegex, value => _draft.KeywordUseRegex = value);
        DrawKeywordChannelRulesEditor();

        ImGui.Separator();
        if (ImGui.CollapsingHeader("全局频道白名单（仅在未配置关键词映射时生效）", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (DrawChannelSelector(_draft.ChannelAllowList, ref _fallbackChannelFilter, "频道ID", "channel_select_fallback", 170))
                QueueAutoApply();
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
        if (DrawChannelSelector(rule.ChannelAllowList, ref _keywordChannelFilter, "频道ID", "channel_select_keyword_detail", 220))
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
            QueueAutoApply();
        }
    }

    private void AddKeywordRule()
    {
        var keyword = BuildDefaultKeywordName();
        _draft.KeywordChannelRules.Add(new BridgeKeywordChannelRule
        {
            Keyword = keyword,
            ChannelAllowList = [.. _draft.ChannelAllowList]
        });
        _selectedKeywordRuleIndex = _draft.KeywordChannelRules.Count - 1;
        _keywordChannelFilter = string.Empty;
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
        ref string channelFilter,
        string filterLabel,
        string childId,
        float childHeight)
    {
        selectedChannels ??= [];
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
        return changed;
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
            EnableDownstream = source.EnableDownstream,
            EnableWebSocketDownstream = source.EnableWebSocketDownstream,
            IngestEndpoint = source.IngestEndpoint ?? string.Empty,
            PullEndpoint = source.PullEndpoint ?? string.Empty,
            WebSocketEndpoint = source.WebSocketEndpoint ?? string.Empty,
            BridgeKey = source.BridgeKey ?? string.Empty,
            BridgeSecret = source.BridgeSecret ?? string.Empty,
            HttpTimeoutMs = source.HttpTimeoutMs,
            MaxRetryCount = source.MaxRetryCount,
            RetryDelayMs = source.RetryDelayMs,
            MinUploadIntervalMs = source.MinUploadIntervalMs,
            ChannelAllowList = source.ChannelAllowList == null ? [] : [.. source.ChannelAllowList],
            KeywordRules = source.KeywordRules == null ? [] : [.. source.KeywordRules],
            KeywordChannelRules = source.KeywordChannelRules == null
                ? []
                : source.KeywordChannelRules.Select(rule => new BridgeKeywordChannelRule
                {
                    Keyword = rule.Keyword ?? string.Empty,
                    ChannelAllowList = rule.ChannelAllowList == null ? [] : [.. rule.ChannelAllowList]
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
