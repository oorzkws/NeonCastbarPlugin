using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(NeonCastbarPlugin neonCastbarPlugin) : base("NeonCastbars Configuration###Configuration Window")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(232, 90);
        SizeCondition = ImGuiCond.Always;

        configuration = neonCastbarPlugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        Flags &= ~ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        // can't ref a property, so use a local copy
        var affectsTargetCastbar = configuration.AffectsTargetCastbar;
        if (ImGui.Checkbox("Color Target Castbar", ref affectsTargetCastbar))
        {
            configuration.AffectsTargetCastbar = affectsTargetCastbar;
            // can save immediately on change, if you don't want to provide a "Save and Close" button
            configuration.Save();
        }

        var affectsFocusTargetCastbar = configuration.AffectsFocusTargetCastbar;
        if (ImGui.Checkbox("Color Focus Target Castbar", ref affectsFocusTargetCastbar))
        {
            configuration.AffectsFocusTargetCastbar = affectsFocusTargetCastbar;
            configuration.Save();
        }
    }
}
