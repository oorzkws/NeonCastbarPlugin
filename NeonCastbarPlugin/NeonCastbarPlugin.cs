using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NeonCastbarPlugin.Windows;

namespace NeonCastbarPlugin;

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

    private static readonly List<string> SubscribedAddons = new()
    {
        "_TargetInfo",
        "_TargetInfoCastBar",
        "_FocusTargetInfo",
        "CastBarEnemy",
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
        var castbar = (AtkUnitBase*)GameGui.GetAddonByName(addonName).Address;
        if (castbar is null)
        {
            PluginLog.Information($"No addon found with name {addonName}");
            return;
        }
        var castbarImage = GetCastbarFromAddon(castbar, addonName);
        if (castbarImage is null)
        {
            PluginLog.Information($"No AtkImageNode found for addon {addonName}");
            return;
        }
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
        foreach (var addonName in SubscribedAddons)
        {
            // If we're shutting down or our config doesn't apply, we unregister
            var addonDisabled = addonName switch
                                {
                                    "_FocusTargetInfo"   => !Configuration.AffectsFocusTargetCastbar,
                                    "_TargetInfo"        => !Configuration.AffectsTargetCastbar,
                                    "_TargetInfoCastBar" => !Configuration.AffectsTargetCastbar,
                                    "CastBarEnemy"       => !Configuration.AffectsTargetCastbar,
                                    _                    => false,
                                };
            if (shutdown || addonDisabled)
            {
                AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, addonName, OnUpdateAddon);
                ResetAddonColor(addonName);
                continue;
            }

            // If we haven't unregistered, we register
            AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, addonName, OnUpdateAddon);
        }
    }
    
    private static unsafe AtkResNode* GetNodeByIndex(AtkResNode* parent, uint index) => parent is null ? null : parent->GetComponent()->UldManager.NodeList[index];
    private static unsafe AtkResNode* GetNodeByIndex(AtkUnitBase* parent, uint index) => parent is null ? null : parent->UldManager.NodeList[index];
    
    private static unsafe AtkImageNode* GetCastbarFromAddon(AtkUnitBase* addon, string addonName)
    {
        // Get the AtkImageNode for the cast bar fill
        var castbarComponent = addonName switch
        {
            "_TargetInfo"        => GetNodeByIndex(addon, 43),
            "_TargetInfoCastBar" => GetNodeByIndex(addon,4), // Split-type cast bar
            "_FocusTargetInfo"   => GetNodeByIndex(addon,15),
            "CastBarEnemy"       => GetNodeByIndex(GetNodeByIndex(addon,10),3), // Boss nameplate castbar subcomponent
            _                    => GetNodeByIndex(addon,43),
        };
        return (AtkImageNode*) castbarComponent;
    }
    
    // Where the actual magic happens
    private static unsafe void OnUpdateAddon(AddonEvent type, AddonArgs args)
    {
        // args can be cast as AddonUpdateArgs here if we need the data for some reason
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null)
            return;
        if (!addon->IsVisible)
            return;

        // Grab the image that's stretched to show castbar progress
        var castbarImage = GetCastbarFromAddon(addon, args.AddonName);
        if (castbarImage is null)
            return;
        if (!castbarImage->IsVisible())
            return;
        PluginLog.Verbose($"Writing state to addon {args.AddonName}: {castbarImage->NodeId}");
        // 0%: R = 0, G = 1.0, B = 1.0 || 100%: R = 1, G = 0, B = 0
        var progress = castbarImage->GetScaleX();
        // TODO: Make configurable
        castbarImage->Color.R = (byte)(progress * 255);
        castbarImage->Color.G = (byte)(1.0f - (progress * 255));
        castbarImage->Color.B = (byte)(1.0f - (progress * 255));
    }
}
