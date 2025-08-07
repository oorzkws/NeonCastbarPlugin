using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NeonCastbarPlugin.Windows;
using System;

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
        var addon = (AtkUnitBase*)GameGui.GetAddonByName(addonName).Address;
        if (addon is null)
            return;
        
        // 10 steps for CastBarEnemy, 1 otherwise
        var count = addonName == "CastBarEnemy" ? 10u : 1u;
        for (var i = count; i > 0; i--)
        {
            // Grab the image that's stretched to show castbar progress
            var castbarImage = GetCastbarFromAddon(addon, addonName, i);
            if (castbarImage is null)
                return;
            ResetImageColor(castbarImage);
        }
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
                //AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, addonName, OnUpdateAddon);
                AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, addonName, OnUpdateAddon);
                ResetAddonColor(addonName);
                continue;
            }

            // If we haven't unregistered, we register
            //AddonLifecycle.RegisterListener(AddonEvent.PreDraw, addonName, OnUpdateAddon);
            AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, addonName, OnUpdateAddon);
        }
    }
    
    private static unsafe AtkResNode* GetNodeByIndex(AtkResNode* parent, uint index) => parent is null ? null : parent->GetComponent()->UldManager.NodeList[index];
    private static unsafe AtkResNode* GetNodeByIndex(AtkUnitBase* parent, uint index) => parent is null ? null : parent->UldManager.NodeList[index];
    
    private static unsafe AtkImageNode* GetCastbarFromAddon(AtkUnitBase* addon, string addonName, uint subindex = 10)
    {
        // Get the AtkImageNode for the cast bar fill
        var castbarComponent = addonName switch
        {
            "_TargetInfo"        => GetNodeByIndex(addon, 43),
            "_TargetInfoCastBar" => GetNodeByIndex(addon,4), // Split-type cast bar
            "_FocusTargetInfo"   => GetNodeByIndex(addon,15),
            "CastBarEnemy"       => GetNodeByIndex(GetNodeByIndex(addon, subindex),3), // Boss nameplate castbar subcomponent
            _                    => GetNodeByIndex(addon,43),
        };
        return (AtkImageNode*) castbarComponent;
    }
    
    private static unsafe void ResetImageColor(AtkImageNode* imageNode)
    {
        imageNode->AddRed = 0;
        imageNode->AddGreen = 0;
        imageNode->AddBlue = 0;;
        imageNode->Color.R = 255;
        imageNode->Color.G = 255;
        imageNode->Color.B = 255;
        imageNode->Color.A = 255;
    }

    private static unsafe void SetImageColor(AtkImageNode* imageNode, float r, float g, float b)
    {
        imageNode->AddRed = (short)(r * 255);
        imageNode->AddGreen = (short)(g * 255);
        imageNode->AddBlue = (short)(b * 255);
        imageNode->Color.R = 0;
        imageNode->Color.G = 0;
        imageNode->Color.B = 0;
        imageNode->Color.A = 255;
    }
    
    // Where the actual magic happens
    private static unsafe void OnUpdateAddon(AddonEvent type, AddonArgs args)
    {
        // args can be cast as AddonUpdateArgs here if we need the data for some reason
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon is null)
            return;
        
        // 10 steps for CastBarEnemy, 1 otherwise
        var count = args.AddonName == "CastBarEnemy" ? 10u : 1u;
        
        if (!addon->IsVisible)
            return;


        for (var i = count; i > 0; i--)
        {
            // Grab the image that's stretched to show castbar progress
            var castbarImage = GetCastbarFromAddon(addon, args.AddonName, i);
            if (castbarImage is null)
                return;
            
            if (!castbarImage->IsVisible())
                return;
            
            if (count == 10) // Hide "CASTING" text on nameplate castbar 
                castbarImage->PrevSiblingNode->ToggleVisibility(false);
            
            // 0%: R = 0, G = 1.0, B = 1.0 || 100%: R = 1, G = 0, B = 0
            var progress = castbarImage->GetScaleX();
            // TODO: Make configurable
            SetImageColor(castbarImage, progress, 1.0f-progress, 1.0f-progress);
        }
    }
}
