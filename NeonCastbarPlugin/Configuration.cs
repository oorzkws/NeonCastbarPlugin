using Dalamud.Configuration;
using System;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool AffectsTargetCastbar { get; set; } = true;
    public bool AffectsFocusTargetCastbar { get; set; }

    public event Action? OnPostSave;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        NeonCastbarPlugin.PluginInterface.SavePluginConfig(this);
        OnPostSave?.Invoke();
    }
}
