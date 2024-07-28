using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SamplePlugin.Windows;

namespace SamplePlugin;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class NeonCastbarPlugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog PluginLog { get; private set; } = null!;

    private const string CommandName = "/pneoncastbars";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("NeonCastbarPlugin");
    private ConfigWindow ConfigWindow { get; init; }

    private static readonly Dictionary<string, int> NodeIndices = new()
    {
        ["_TargetInfo"] = 43,
        ["_TargetInfoCastBar"] = 4, //Split target info
        ["_FocusTargetInfo"] = 15
    };

    public NeonCastbarPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Display the configuration interface for Neon Castbars"
        });

        PluginInterface.UiBuilder.Draw += DrawUi;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        // This hooks saving to update our hooks
        Configuration.OnPostSave += OnConfigurationChanged;

        OnConfigurationChanged();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        OnConfigurationChanged(true);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleConfigUi();
    }

    private void DrawUi()
    {
        WindowSystem.Draw();
    }

    public void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }

    private static unsafe void ResetAddonColor(string addonName)
    {
        var castbar = (AtkUnitBase*)GameGui.GetAddonByName(addonName);
        if (castbar is null) return;
        var castbarImage = (AtkImageNode*)castbar->UldManager.NodeList[NodeIndices[addonName]];
        castbarImage->Color.R = 0xFF;
        castbarImage->Color.G = 0xFF;
        castbarImage->Color.B = 0xFF;
    }

    private void OnConfigurationChanged()
    {
        OnConfigurationChanged(false);
    }

    private void OnConfigurationChanged(bool shutdown)
    {
        // Hook the update for each of our castbars
        foreach (var addonName in NodeIndices.Keys)
        {
            // If we're shutting down or our config doesn't apply, we unregister
            if (shutdown || (addonName == "_FocusTargetInfo" && !Configuration.AffectsFocusTargetCastbar) ||
                (addonName != "_FocusTargetInfo" && !Configuration.AffectsTargetCastbar))
            {
                AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, addonName, OnUpdateAddon);
                ResetAddonColor(addonName);
                continue;
            }

            // If we haven't unregistered, we register
            AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, addonName, OnUpdateAddon);
        }
    }

    // Where the actual magic happens
    private static unsafe void OnUpdateAddon(AddonEvent type, AddonArgs args)
    {
        // args can be cast as AddonUpdateArgs here if we need the data for some reason
        var castbar = (AtkUnitBase*)args.Addon;
        if (!castbar->IsVisible)
            return;

        // Grab the image that's stretched to show castbar progress
        var castbarImage = (AtkImageNode*)castbar->UldManager.NodeList[NodeIndices[args.AddonName]];
        // 0%: R = 0, G = 1.0, B = 1.0 || 100%: R = 1, G = 0, B = 0
        var progress = castbarImage->GetScaleX();
        // TODO: Make configurable
        castbarImage->Color.R = (byte)(progress * 255);
        castbarImage->Color.G = (byte)(1.0f - (progress * 255));
        castbarImage->Color.B = (byte)(1.0f - (progress * 255));
    }
}
