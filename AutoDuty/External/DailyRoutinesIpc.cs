using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDuty.External;

using System;
using System.Collections.Generic;
using System.Linq;

internal static class DailyRoutinesIpc
{
    internal const string AutoCutsceneSkipModule = "AutoCutsceneSkip";

    private static IDalamudPluginInterface? _pluginInterface;
    private static HashSet<string> _temporarilyEnabledModules = [];

    private static ICallGateSubscriber<string, bool?>? _isModuleEnabled;
    private static ICallGateSubscriber<Version?>? _dailyRoutinesVersion;
    private static ICallGateSubscriber<string, bool, bool>? _loadModule;
    private static ICallGateSubscriber<string, bool, bool, bool>? _unloadModule;

    internal static bool IsDailyRoutinesEnabled =>
        _pluginInterface?.InstalledPlugins.Any(x => x is { InternalName: "DailyRoutines", IsLoaded: true }) == true;

    internal static bool ShouldIncludePortaDecumana =>
        IPCSubscriber_Common.IsReady("SkipCutscene") || IsDailyRoutinesEnabled;

    internal static string PortaDecumanaHelpText =>
        IsDailyRoutinesEnabled && !IPCSubscriber_Common.IsReady("SkipCutscene")
            ? "DailyRoutines detected. AutoDuty will temporarily enable AutoCutsceneSkip while running Porta Decumana."
            : "CutsceneSkip detected. Please keep it actually on.";

    internal static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface ??= pluginInterface;
        _isModuleEnabled ??= pluginInterface.GetIpcSubscriber<string, bool?>("DailyRoutines.IsModuleEnabled");
        _dailyRoutinesVersion ??= pluginInterface.GetIpcSubscriber<Version?>("DailyRoutines.Version");
        _loadModule ??= pluginInterface.GetIpcSubscriber<string, bool, bool>("DailyRoutines.LoadModule");
        _unloadModule ??= pluginInterface.GetIpcSubscriber<string, bool, bool, bool>("DailyRoutines.UnloadModule");
    }

    internal static void EnsureAutoCutsceneSkipForCurrentRun()
    {
        if (Plugin.CurrentTerritoryContent?.TerritoryType != 1048u)
            return;

        try
        {
            EnsureAutoCutsceneSkip();
        }
        catch
        {
            // ignored
        }
    }

    internal static void RecoverTemporarilyEnabledModules()
    {
        if (!IsDailyRoutinesEnabled || _temporarilyEnabledModules.Count <= 0)
            return;

        foreach (string moduleName in _temporarilyEnabledModules)
            if (UnloadModule(moduleName, false, false))
                Svc.Log.Info($"DailyRoutines IPC: restored module state for {moduleName}");

        _temporarilyEnabledModules = [];
    }

    internal static void Dispose()
    {
        RecoverTemporarilyEnabledModules();
    }

    private static void EnsureAutoCutsceneSkip()
    {
        if (!IsDailyRoutinesEnabled || _dailyRoutinesVersion?.InvokeFunc() == null)
            return;

        bool? moduleState = IsModuleEnabled(AutoCutsceneSkipModule);
        if (moduleState == false && LoadModule(AutoCutsceneSkipModule, false))
        {
            _temporarilyEnabledModules.Add(AutoCutsceneSkipModule);
            Svc.Log.Info($"DailyRoutines IPC: temporarily enabled {AutoCutsceneSkipModule}");
        }
    }

    private static bool? IsModuleEnabled(string moduleName) =>
        !IsDailyRoutinesEnabled || _isModuleEnabled == null ? null : _isModuleEnabled.InvokeFunc(moduleName);

    private static bool LoadModule(string moduleName, bool affectConfig) =>
        IsDailyRoutinesEnabled && _loadModule != null && _loadModule.InvokeFunc(moduleName, affectConfig);

    private static bool UnloadModule(string moduleName, bool affectConfig, bool force) =>
        IsDailyRoutinesEnabled && _unloadModule != null && _unloadModule.InvokeFunc(moduleName, affectConfig, force);
}
