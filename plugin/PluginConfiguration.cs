using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace XSZRemoteChatBridge;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public BridgeOptions Options { get; set; } = new();

    [JsonIgnore]
    private IDalamudPluginInterface? PluginInterface { get; set; }

    public void Init(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        Options ??= new BridgeOptions();
        Options.Normalize();
    }

    public void Save()
    {
        PluginInterface?.SavePluginConfig(this);
    }
}
