using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace XSZRemoteChatBridge;

public sealed class PluginServices
{
    [PluginService] public IChatGui Chat { get; private set; } = null!;
    [PluginService] public ICommandManager Command { get; private set; } = null!;
    [PluginService] public IFramework Framework { get; private set; } = null!;
    [PluginService] public IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public IPluginLog Log { get; private set; } = null!;
}
