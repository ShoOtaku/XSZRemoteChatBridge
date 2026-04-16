using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Plugin;

namespace XSZRemoteChatBridge;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "XSZRemoteChatBridge";
    private const string OpenUiCommand = "/xszrcb";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly PluginServices _services;
    private readonly PluginConfiguration _configuration;
    private readonly SettingsWindow _settingsWindow;
    private RemoteChatBridgeModule _module;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        _services = new PluginServices();
        _pluginInterface.Inject(_services);

        _configuration = _pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        _configuration.Init(_pluginInterface);

        _settingsWindow = new SettingsWindow(
            _configuration.Options,
            autoApplyAction: ApplyAndRestartBridge,
            reloadAction: () => _settingsWindow.LoadFrom(_configuration.Options),
            openUrlAction: OpenExternalUrl);

        _pluginInterface.UiBuilder.Draw += OnDrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;

        _services.Command.AddHandler(OpenUiCommand, new CommandInfo(OnOpenUiCommand)
        {
            HelpMessage = "打开 XSZRemoteChatBridge 设置面板"
        });

        _module = new RemoteChatBridgeModule(_services, _configuration.Options);
        _module.Init();
        _services.Log.Information("[RemoteChatBridge] 插件初始化完成");
    }

    private void OnDrawUi()
    {
        _settingsWindow.Draw();
    }

    private void OnOpenConfigUi()
    {
        _settingsWindow.IsOpen = true;
    }

    private void OnOpenUiCommand(string command, string args)
    {
        _settingsWindow.IsOpen = true;
    }

    private void ApplyAndRestartBridge(BridgeOptions options)
    {
        options.Normalize();
        _configuration.Options = options;
        _configuration.Save();

        _module.Dispose();
        _module = new RemoteChatBridgeModule(_services, _configuration.Options);
        _module.Init();

        _services.Log.Information("[RemoteChatBridge] 配置已保存并重新加载");
    }

    private void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _services.Log.Warning($"[RemoteChatBridge] 打开外部链接失败: {ex.Message}, url={url}");
        }
    }

    public void Dispose()
    {
        _pluginInterface.UiBuilder.Draw -= OnDrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;

        _services.Command.RemoveHandler(OpenUiCommand);

        _module.Dispose();
        _configuration.Save();
        _services.Log.Information("[RemoteChatBridge] 插件已卸载");
    }
}
