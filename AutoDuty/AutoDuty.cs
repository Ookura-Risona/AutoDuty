global using static AutoDuty.Data.Enums;
global using static AutoDuty.Data.Extensions;
global using static AutoDuty.Data.Classes;
global using static AutoDuty.AutoDuty;
global using AutoDuty.Managers;
global using ECommons.GameHelpers;
using System;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System.Collections.Generic;
using System.IO;
using ECommons;
using ECommons.DalamudServices;
using AutoDuty.Windows;
using AutoDuty.IPC;
using AutoDuty.External;
using AutoDuty.Helpers;
using ECommons.Throttlers;
using Dalamud.Game.ClientState.Objects.Types;
using System.Linq;
using ECommons.GameFunctions;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.IoC;
using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using AutoDuty.Properties;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Serilog.Events;
using AutoDuty.Updater;

namespace AutoDuty;

using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Data;
using ECommons.Configuration;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Pictomancy;
using static Data.Classes;
using TaskManager = ECommons.Automation.LegacyTaskManager.TaskManager;

// TODO:
// Scrapped interable list, going to implement an internal list that when a interactable step end in fail, the Dataid gets add to the list and is scanned for from there on out, if found we goto it and get it, then remove from list.
// Need to expand AutoRepair to include check for level and stuff to see if you are eligible for self repair. and check for dark matter
// make config saving per character
// drap drop on build is jacked when theres scrolling

// WISHLIST for VBM:
// Generic (Non Module) jousting respects navmesh out of bounds (or dynamically just adds forbiddenzones as Obstacles using Detour) (or at very least, vbm NavigationDecision can use ClosestPointonMesh in it's decision making) (or just spit balling here as no idea if even possible, add Everywhere non tiled as ForbiddenZones /shrug)

public sealed class AutoDuty : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    internal List<PathAction>           Actions       { get;       set; } = [];
    internal List<uint>                 Interactables { get;       set; } = [];
    internal int                        CurrentLoop                       = 0;
    internal KeyValuePair<ushort, Job?> CurrentPlayerItemLevelandClassJob = new(0, null);
    private  Content?                   currentTerritoryContent           = null;
    internal Content? CurrentTerritoryContent
    {
        get => this.Configuration.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist when this.States.HasFlag(PluginState.Looping) || !this.InDungeon => (this.PlaylistCurrent.Count >= 0 && this.PlaylistIndex < this.PlaylistCurrent.Count && this.PlaylistIndex >= 0) ?
                                                                                                                            this.PlaylistCurrent[this.PlaylistIndex].Content :
                                                                                                                            null,
            AutoDutyMode.Looping or _ => this.currentTerritoryContent
        };
        set
        {
            this.CurrentPlayerItemLevelandClassJob = PlayerHelper.IsValid ? new KeyValuePair<ushort, Job?>(InventoryHelper.CurrentItemLevel, Player.Job) : new KeyValuePair<ushort, Job?>(0, null);
            this.currentTerritoryContent           = value;
        }
    }

    internal uint CurrentTerritoryType = 0;
    internal int CurrentPath = -1;

    internal List<PlaylistEntry> PlaylistCurrent = [];
    internal int                 PlaylistIndex   = 0;
    internal PlaylistEntry?      PlaylistCurrentEntry => this.PlaylistIndex >= 0 && this.PlaylistIndex < this.PlaylistCurrent.Count ? 
                                                             this.PlaylistCurrent[this.PlaylistIndex] : null;

    internal bool SupportLevelingEnabled => this.LevelingModeEnum == LevelingMode.Support;
    internal bool TrustLevelingEnabled   => this.LevelingModeEnum.IsTrustLeveling();
    internal bool LevelingEnabled        => this.LevelingModeEnum != LevelingMode.None;

    internal static string         Name   => "AutoDuty";
    internal static AutoDuty       Plugin { get; private set; }
    internal        bool           StopForCombat = true;
    internal        DirectoryInfo  PathsDirectory;
    internal        FileInfo       AssemblyFileInfo;
    internal        FileInfo       ConfigFile;
    internal        DirectoryInfo? DalamudDirectory;
    internal        DirectoryInfo? AssemblyDirectoryInfo;

    internal Configuration Configuration => ConfigurationMain.Instance.GetCurrentConfig;
    internal WindowSystem  WindowSystem = new("AutoDuty");

    public   int   Version { get; set; }
    internal Stage PreviousStage = Stage.Stopped;
    internal Stage Stage
    {
        get => this._stage;
        set
        {
            switch (value)
            {
                case Stage.Stopped:
                    this.StopAndResetALL();
                    break;
                case Stage.Paused:
                    this.PreviousStage = this.Stage;
                    if (VNavmesh_IPCSubscriber.Path_NumWaypoints() > 0)
                        VNavmesh_IPCSubscriber.Path_Stop();
                    FollowHelper.SetFollow(null);
                    this.TaskManager.SetStepMode(true);
                    this.States |= PluginState.Paused;
                    break;
                case Stage.Action:
                    this.ActionInvoke();
                    break;
                case Stage.Condition:
                    this.Action = $"ConditionChange";
                    SchedulerHelper.ScheduleAction("ConditionChangeStageReadingPath", () => this._stage = Stage.Reading_Path, () => !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51] && !Svc.Condition[ConditionFlag.Jumping61]);
                    break;
                case Stage.Waiting_For_Combat:
                    BossMod_IPCSubscriber.SetRange(Plugin.Configuration.MaxDistanceToTargetFloat);
                    break;
                case Stage.Reading_Path:
                    if(this._stage is not Stage.Waiting_For_Combat and not Stage.Revived and not Stage.Looping and not Stage.Idle)
                        ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep = true;
                    break;
                case Stage.Idle:
                    if (VNavmesh_IPCSubscriber.Path_NumWaypoints() > 0)
                        VNavmesh_IPCSubscriber.Path_Stop();
                    break;
                case Stage.Looping:
                case Stage.Moving:
                case Stage.Dead:
                case Stage.Revived:
                case Stage.Interactable:
                default:
                    break;
            }
            Svc.Log.Debug($"Stage from {this._stage.ToCustomString()} to {value.ToCustomString()}");
            this._stage = value;
        }
    }
    internal LevelingMode LevelingModeEnum
    {
        get => this.levelingModeEnum;
        set
        {
            if (value != LevelingMode.None)
            {
                Svc.Log.Debug($"Setting Leveling mode to {value}");
                Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(value);

                if (duty != null)
                {
                    this.levelingModeEnum     = value;
                    MainTab.DutySelected      = ContentPathsManager.DictionaryPaths[duty.TerritoryType];
                    this.CurrentTerritoryContent = duty;
                    MainTab.DutySelected.SelectPath(out this.CurrentPath);
                    Svc.Log.Debug($"Leveling Mode: Setting duty to {duty.Name}");
                }
                else
                {
                    MainTab.DutySelected         = null;
                    this.MainListClicked         = false;
                    this.CurrentTerritoryContent = null;
                    this.levelingModeEnum        = LevelingMode.None;
                    Svc.Log.Debug($"Leveling Mode: No appropriate leveling duty found");
                }
            }
            else
            {
                MainTab.DutySelected         = null;
                this.MainListClicked         = false;
                this.CurrentTerritoryContent = null;
                this.levelingModeEnum           = LevelingMode.None;
            }
        }
    }
    internal PluginState States = PluginState.None;
    internal int Indexer = -1;
    internal bool MainListClicked = false;
    internal IBattleChara? BossObject;
    internal static IGameObject? ClosestObject => Svc.Objects.Where(o => o.IsTargetable && o.ObjectKind.EqualsAny(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj, Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)).OrderBy(ObjectHelper.GetDistanceToPlayer).TryGetFirst(out var gameObject) ? gameObject : null;
    internal OverrideCamera OverrideCamera;
    internal MainWindow MainWindow { get; init; }
    internal Overlay Overlay { get; init; }
    internal bool InDungeon => ContentHelper.DictionaryContent.ContainsKey(Svc.ClientState.TerritoryType);
    internal bool SkipTreasureCoffer = false;
    internal string Action = "";
    internal string PathFile = "";
    internal TaskManager TaskManager;
    internal Job JobLastKnown;
    internal DutyState DutyState = DutyState.None;
    internal PathAction PathAction = new();
    internal List<Data.Classes.LogMessage> DalamudLogEntries = [];
    private LevelingMode levelingModeEnum = LevelingMode.None;
    private Stage _stage = Stage.Stopped;
    private const string CommandName = "/autoduty";
    private readonly DirectoryInfo _configDirectory;
    private readonly ActionsManager _actions;
    private readonly SquadronManager _squadronManager;
    private readonly VariantManager _variantManager;
    private readonly OverrideAFK _overrideAFK;
    private readonly IPCProvider _ipcProvider;
    private IGameObject? treasureCofferGameObject = null;
    //private readonly TinyMessageBus _messageBusSend = new("AutoDutyBroadcaster");
    //private readonly TinyMessageBus _messageBusReceive = new("AutoDutyBroadcaster");
    private         bool           _recentlyWatchedCutscene = false;
    private         bool           _lootTreasure;
    private         SettingsActive _settingsActive         = SettingsActive.None;
    private         SettingsActive _bareModeSettingsActive = SettingsActive.None;
    private         DateTime       _lastRotationSetTime    = DateTime.MinValue;
    public readonly bool           isDev;

    private readonly (string[], string, Action<string[]>)[] commands;

    public AutoDuty()
    {
/*
#if !DEBUG
        if (PluginInterface.IsDev || (!PluginInterface.SourceRepository.Contains("gh.risona.top/pluginmaster.json") && !PluginInterface.SourceRepository.Contains("Ookura-Risona/DalamudPlugins")))
            return;
#endif
*/
        try
        {
            Plugin = this;
            ECommonsMain.Init(PluginInterface, Plugin, Module.DalamudReflector, Module.ObjectFunctions);
            PictoService.Initialize(PluginInterface);

            this.isDev = PluginInterface.IsDev;

            //EzConfig.Init<ConfigurationMain>();
            EzConfig.DefaultSerializationFactory = new AutoDutySerializationFactory();
            (ConfigurationMain.Instance = EzConfig.Init<ConfigurationMain>()).Init();



            //Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            
            ConfigTab.BuildManuals();
            this._configDirectory      = PluginInterface.ConfigDirectory;
            this.ConfigFile            = PluginInterface.ConfigFile;
            this.DalamudDirectory      = this.ConfigFile.Directory?.Parent;
            this.PathsDirectory        = new DirectoryInfo(this._configDirectory.FullName + "/paths");
            this.AssemblyFileInfo      = PluginInterface.AssemblyLocation;
            this.AssemblyDirectoryInfo = this.AssemblyFileInfo.Directory;

            this.Version = 
                ((PluginInterface.IsDev     ? new Version(0,0,0, 262) :
                  PluginInterface.IsTesting ? PluginInterface.Manifest.TestingAssemblyVersion ?? PluginInterface.Manifest.AssemblyVersion : PluginInterface.Manifest.AssemblyVersion)!).Revision;

            if (!this._configDirectory.Exists)
                this._configDirectory.Create();
            if (!this.PathsDirectory.Exists) 
                this.PathsDirectory.Create();

            this.TaskManager = new TaskManager
                               {
                                   AbortOnTimeout  = false,
                                   TimeoutSilently = true
                               };

            TrustHelper.PopulateTrustMembers();
            ContentHelper.PopulateDuties();
            RepairNPCHelper.PopulateRepairNPCs();
            FileHelper.Init();
            Patcher.Patch(startup: true);

            this._overrideAFK     = new OverrideAFK();
            this._ipcProvider     = new IPCProvider();
            this._squadronManager = new SquadronManager(this.TaskManager);
            this._variantManager  = new VariantManager(this.TaskManager);
            this._actions         = new ActionsManager(Plugin, this.TaskManager);
            BuildTab.ActionsList  = this._actions.ActionsList;
            this.OverrideCamera   = new OverrideCamera();
            this.Overlay          = new Overlay();
            this.MainWindow       = new MainWindow();
            this.WindowSystem.AddWindow(this.MainWindow);
            this.WindowSystem.AddWindow(this.Overlay);

            if (Svc.ClientState.IsLoggedIn) 
                this.ClientStateOnLogin();
            
            ActiveHelper.InvokeAllHelpers();

            this.commands = [
                (["config", "cfg"], "opens config window / modifies config", argsArray =>
                                                                             {
                                                                                 if (argsArray.Length < 2)
                                                                                     this.OpenConfigUI();
                                                                                 else if (argsArray[1].Equals("list"))
                                                                                     ConfigHelper.ListConfig();
                                                                                 else
                                                                                     ConfigHelper.ModifyConfig(argsArray[1], argsArray[2..]);
                                                                             }),
                (["start"], "starts autoduty when in a Duty", _ => this.StartNavigation()),
                (["stop"], "stops everything", _ => Plugin.Stage = Stage.Stopped),
                (["pause"], "pause route", _ => Plugin.Stage     = Stage.Paused),
                (["resume"], "resume route", _ =>
                                             {
                                                 if (Plugin.Stage == Stage.Paused)
                                                 {
                                                     Plugin.TaskManager.SetStepMode(false);
                                                     Plugin.Stage  =  Plugin.PreviousStage;
                                                     Plugin.States &= ~PluginState.Paused;
                                                 }
                                             }),
                (["dataid"], "Logs and copies your target's dataid to clipboard", argsArray =>
                                                                                  {
                                                                                      IGameObject? obj = null;
                                                                                      if (argsArray.Length == 2)
                                                                                          obj = Svc.Objects[int.TryParse(argsArray[1], out int index) ? index : -1] ?? null;
                                                                                      else
                                                                                          obj = ObjectHelper.GetObjectByName(Svc.Targets.Target?.Name.TextValue ?? "");

                                                                                      Svc.Log.Info($"{obj?.DataId}");
                                                                                      ImGui.SetClipboardText($"{obj?.DataId}");
                                                                                  }),
                (["queue"], "queues duty", argsArray =>
                                           {
                                               QueueHelper.Invoke(ContentHelper.DictionaryContent.FirstOrDefault(x => x.Value.Name!.Equals(string.Join(" ", argsArray).Replace("queue ", string.Empty), StringComparison.InvariantCultureIgnoreCase)).Value ?? null,
                                                                  this.Configuration.DutyModeEnum);
                                           }),
                (["overlay"], "opens overlay", argsArray =>
                                               {
                                                   if (argsArray.Length == 1)
                                                   {
                                                       this.Configuration.ShowOverlay = true;
                                                       this.Overlay.IsOpen            = true;

                                                       if (!Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating))
                                                           this.Configuration.HideOverlayWhenStopped = false;
                                                   }
                                                   else
                                                   {
                                                       switch (argsArray[1].ToLower())
                                                       {
                                                           case "lock":
                                                               if (this.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                   this.Overlay.Flags -= ImGuiWindowFlags.NoMove;
                                                               else
                                                                   this.Overlay.Flags |= ImGuiWindowFlags.NoMove;
                                                               break;
                                                           case "nobg":
                                                               if (this.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground))
                                                                   this.Overlay.Flags -= ImGuiWindowFlags.NoBackground;
                                                               else
                                                                   this.Overlay.Flags |= ImGuiWindowFlags.NoBackground;
                                                               break;
                                                       }
                                                   }
                                               }),
                (["skipstep"], "skips the current step", _ =>
                                                         {
                                                             if (this.States.HasFlag(PluginState.Navigating))
                                                             {
                                                                 this.Indexer++;
                                                                 this.Stage = Stage.Reading_Path;
                                                             }
                                                         }),
                (["movetoflag"], "moves to the flag map marker", _ => MapHelper.MoveToMapMarker()),
                (["ttfull"], "opens packs, registers cards and sells the rest", _ =>
                                                                                {
                                                                                    this.TaskManager.Enqueue(CofferHelper.Invoke);
                                                                                    this.TaskManager.Enqueue(() => CofferHelper.State == ActionState.None, 600000);
                                                                                    this.TaskManager.Enqueue(TripleTriadCardUseHelper.Invoke);
                                                                                    this.TaskManager.DelayNext(200);
                                                                                    this.TaskManager.Enqueue(() => TripleTriadCardUseHelper.State == ActionState.None, 600000);
                                                                                    this.TaskManager.DelayNext(200);
                                                                                    this.TaskManager.Enqueue(TripleTriadCardSellHelper.Invoke);
                                                                                    this.TaskManager.Enqueue(() => TripleTriadCardSellHelper.State == ActionState.None, 120000);
                                                                                }),
                (["run"], "starts auto duty in territory type specified", argsArray =>
                                                                          {
                                                                              const string failPreMessage  = "Run Error: Incorrect usage: ";
                                                                              const string failPostMessage = "\nCorrect usage: /autoduty run DutyMode TerritoryTypeInteger LoopTimesInteger (optional)BareModeBool\nexample: /autoduty run Support 1036 10 true\nYou can get the TerritoryTypeInteger from /autoduty tt name of territory (will be logged and copied to clipboard)";
                                                                              if (argsArray.Length < 4)
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument count must be at least 3, you inputted {argsArray.Length - 1}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!Enum.TryParse(argsArray[1], true, out DutyMode dutyMode))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 1 must be a DutyMode enum Type, you inputted {argsArray[1]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!uint.TryParse(argsArray[2], out uint territoryType))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 2 must be an unsigned integer, you inputted {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!int.TryParse(argsArray[3], out int loopTimes))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 3 must be an integer, you inputted {argsArray[3]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!ContentHelper.DictionaryContent.TryGetValue(territoryType, out Content? content))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 2 value was not in our ContentList or has no Path, you inputted {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!content.DutyModes.HasFlag(dutyMode))
                                                                              {
                                                                                  Svc.Log.Info($"{failPreMessage}Argument 2 value was not of type {dutyMode}, which you inputted in Argument 1, Argument 2 value was {argsArray[2]}{failPostMessage}");
                                                                                  return;
                                                                              }

                                                                              if (!content.CanRun(mode: dutyMode))
                                                                              {
                                                                                  string failReason = !UIState.IsInstanceContentCompleted(content.Id) ?
                                                                                                          "You dont have it unlocked" :
                                                                                                          (!ContentPathsManager.DictionaryPaths.ContainsKey(content.TerritoryType) ?
                                                                                                               "There is no path file" :
                                                                                                               (PlayerHelper.GetCurrentLevelFromSheet() < content.ClassJobLevelRequired ?
                                                                                                                    $"Your Lvl({PlayerHelper.GetCurrentLevelFromSheet()}) is less than {content.ClassJobLevelRequired}" :
                                                                                                                    (InventoryHelper.CurrentItemLevel < content.ItemLevelRequired ?
                                                                                                                         $"Your iLvl({InventoryHelper.CurrentItemLevel}) is less than {content.ItemLevelRequired}" :
                                                                                                                         "Your trust party is not of correct levels")));
                                                                                  Svc.Log.Info($"Unable to run {content.Name}, {failReason} {content.CanTrustRun()}");
                                                                                  return;
                                                                              }

                                                                              this.Configuration.DutyModeEnum = dutyMode;

                                                                              this.Run(territoryType, loopTimes, bareMode: argsArray.Length > 4 && bool.TryParse(argsArray[4], out bool parsedBool) && parsedBool);
                                                                          }),
            ];
            this.commands = this.commands.Concat(ActiveHelper.activeHelpers.Where(iah => iah.Commands != null).
                                                              Select<IActiveHelper, (string[], string, Action<string[]>)>(iah => (iah.Commands!, iah.CommandDescription!, iah.OnCommand))).ToArray();

            Svc.Commands.AddHandler("/ad", new CommandInfo(this.OnCommand));
            Svc.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
                                                 {
                                                     HelpMessage = string.Join("\n", this.commands.Select(tuple => $"/autoduty or /ad {string.Join(" / ", tuple.Item1)} -> {tuple.Item2}"))
                                                 });


            PluginInterface.UiBuilder.Draw         += this.DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUI;
            PluginInterface.UiBuilder.OpenMainUi   += this.OpenMainUI;

            Svc.Framework.Update             += this.Framework_Update;
            Svc.Framework.Update             += SchedulerHelper.ScheduleInvoker;
            Svc.ClientState.TerritoryChanged += this.ClientState_TerritoryChanged;
            Svc.ClientState.Login            += this.ClientStateOnLogin;
            Svc.Condition.ConditionChange    += this.Condition_ConditionChange;
            Svc.DutyState.DutyStarted        += this.DutyState_DutyStarted;
            Svc.DutyState.DutyWiped          += this.DutyState_DutyWiped;
            Svc.DutyState.DutyRecommenced    += this.DutyState_DutyRecommenced;
            Svc.DutyState.DutyCompleted      += this.DutyState_DutyCompleted;
            Svc.Log.MinimumLogLevel          =  LogEventLevel.Debug;
            PluginInterface.UiBuilder.Draw   += this.UiBuilderOnDraw;
        }
        catch (Exception e)
        {
            Svc.Log.Info($"Failed loading plugin\n{e}");
        }
    }

    private unsafe void OnCommand(string command, string args)
    {
        Match        match   = RegexHelper.ArgumentParserRegex().Match(args.ToLower());
        List<string> matches = [];

        while (match.Success)
        {
            matches.Add(match.Groups[match.Groups[1].Length > 0 ? 1 : 0].Value);
            match = match.NextMatch();
        }

        string[] argsArray = matches.Count > 0 ? matches.ToArray() : [string.Empty];
        string check = argsArray[0];

        Svc.Log.Debug("command with: " + args);

        foreach ((string[] keywords, _, Action<string[]> action) in this.commands)
            if (keywords.Any(key => check.StartsWith(key)))
            {
                Svc.Log.Debug("Activating command: " + string.Join(" / ", keywords));
                action(argsArray);
                return;
            }

        switch (argsArray[0])
        {
            case "moveto":
                string[] argss = args.Replace("moveto ", "").Split("|");
                string[] vs    = argss[1].Split(", ");
                Vector3      v3    = new Vector3(float.Parse(vs[0]), float.Parse(vs[1]), float.Parse(vs[2]));

                GotoHelper.Invoke(Convert.ToUInt32(argss[0]), [v3], argss.Length > 2 ? float.Parse(argss[2]) : 0.25f, argss.Length > 3 ? float.Parse(argss[3]) : 0.25f);
                break;
            case "spew":
                IGameObject? spewObj = null;
                spewObj = argsArray.Length == 2 ? 
                              ObjectHelper.GetObjectByDataId(uint.TryParse(argsArray[1], out uint dataId) ? dataId : 0) : 
                              ObjectHelper.GetObjectByName(Svc.Targets.Target?.Name.TextValue ?? "");

                if (spewObj == null) 
                    return;

                GameObject* gObj = spewObj.Struct();

                void PrintInfo(Func<string> info)
                {
                    try
                    {
                        Svc.Log.Info(info());
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Info($": {ex}");
                    }
                }

                PrintInfo(() => $"Spewing Object Information for: {gObj->NameString}");
                PrintInfo(() => $"Spewing Object Information for: {gObj->GetName()}");
                //DrawObject: {gObj->DrawObject}\n
                //LayoutInstance: { gObj->LayoutInstance}\n
                //EventHandler: { gObj->EventHandler}\n
                //LuaActor: {gObj->LuaActor}\n
                PrintInfo(() => $"DefaultPosition: {gObj->DefaultPosition}");
                PrintInfo(() => $"DefaultRotation: {gObj->DefaultRotation}");
                PrintInfo(() => $"EventState: {gObj->EventState}");
                PrintInfo(() => $"EntityId {gObj->EntityId}");
                PrintInfo(() => $"LayoutId: {gObj->LayoutId}");
                PrintInfo(() => $"BaseId {gObj->BaseId}");
                PrintInfo(() => $"OwnerId: {gObj->OwnerId}");
                PrintInfo(() => $"ObjectIndex: {gObj->ObjectIndex}");
                PrintInfo(() => $"ObjectKind {gObj->ObjectKind}");               
                PrintInfo(() => $"SubKind: {gObj->SubKind}");
                PrintInfo(() => $"Sex: {gObj->Sex}");
                PrintInfo(() => $"YalmDistanceFromPlayerX: {gObj->YalmDistanceFromPlayerX}");
                PrintInfo(() => $"TargetStatus: {gObj->TargetStatus}");
                PrintInfo(() => $"YalmDistanceFromPlayerZ: {gObj->YalmDistanceFromPlayerZ}");
                PrintInfo(() => $"TargetableStatus: {gObj->TargetableStatus}");
                PrintInfo(() => $"Position: {gObj->Position}");
                PrintInfo(() => $"Rotation: {gObj->Rotation}");
                PrintInfo(() => $"Scale: {gObj->Scale}");
                PrintInfo(() => $"Height: {gObj->Height}");
                PrintInfo(() => $"VfxScale: {gObj->VfxScale}");
                PrintInfo(() => $"HitboxRadius: {gObj->HitboxRadius}");
                PrintInfo(() => $"DrawOffset: {gObj->DrawOffset}");
                PrintInfo(() => $"EventId: {gObj->EventId.Id}");
                PrintInfo(() => $"FateId: {gObj->FateId}");
                PrintInfo(() => $"NamePlateIconId: {gObj->NamePlateIconId}");
                PrintInfo(() => $"RenderFlags: {gObj->RenderFlags}");
                PrintInfo(() => $"GetGameObjectId().ObjectId: {gObj->GetGameObjectId().ObjectId}");
                PrintInfo(() => $"GetGameObjectId().Type: {gObj->GetGameObjectId().Type}");
                PrintInfo(() => $"GetObjectKind: {gObj->GetObjectKind()}");
                PrintInfo(() => $"GetIsTargetable: {gObj->GetIsTargetable()}");
                PrintInfo(() => $"GetName: {gObj->GetName()}");
                PrintInfo(() => $"GetRadius: {gObj->GetRadius()}");
                PrintInfo(() => $"GetHeight: {gObj->GetHeight()}");
                PrintInfo(() => $"GetDrawObject: {*gObj->GetDrawObject()}");
                PrintInfo(() => $"GetNameId: {gObj->GetNameId()}");
                PrintInfo(() => $"IsDead: {gObj->IsDead()}");
                PrintInfo(() => $"IsNotMounted: {gObj->IsNotMounted()}");
                PrintInfo(() => $"IsCharacter: {gObj->IsCharacter()}");
                PrintInfo(() => $"IsReadyToDraw: {gObj->IsReadyToDraw()}");
                break;
            default:
                this.OpenMainUI();
                break;
        }
    }

    private void ClientStateOnLogin()
    {
        ConfigurationMain.Instance.SetProfileToDefault();

        SchedulerHelper.ScheduleAction("LoginConfig", () =>
                                                      {
                                                          if (this.Configuration.ShowOverlay &&
                                                              (!this.Configuration.HideOverlayWhenStopped || this.States.HasFlag(PluginState.Looping) ||
                                                               this.States.HasFlag(PluginState.Navigating)))
                                                              SchedulerHelper.ScheduleAction("ShowOverlay", () => this.Overlay.IsOpen = true, () => PlayerHelper.IsReady);

                                                          if (this.Configuration.ShowMainWindowOnStartup)
                                                              SchedulerHelper.ScheduleAction("ShowMainWindowOnStartup", this.OpenMainUI, () => PlayerHelper.IsReady);
                                                      }, () => ConfigurationMain.Instance.Initialized);
                                
    }

    private void UiBuilderOnDraw()
    {
        if (PlayerHelper.IsValid)
        {
            using PctDrawList? drawList = PictoService.Draw();

            if (drawList != null)
            {
                BuildTab.DrawHelper(drawList);

                if (Plugin.Configuration.PathDrawEnabled && this.CurrentTerritoryContent?.TerritoryType == Svc.ClientState.TerritoryType && this.Actions.Any() && 
                    (this.Indexer < 0 || !this.Actions[this.Indexer].Name.Equals("Boss") || this.Stage != Stage.Action))
                {
                    Vector3 lastPos         = Player.Position;
                    float   stepCountFactor = (1f / this.Configuration.PathDrawStepCount);

                    for (int index = Math.Clamp(this.Indexer, 0, this.Actions.Count-1); index < this.Actions.Count; index++)
                    {
                        PathAction action = this.Actions[index];
                        if (action.Position.LengthSquared() > 1)
                        {
                            float alpha = MathF.Max(0f, 1f - (index - this.Indexer) * stepCountFactor);

                            if (alpha > 0)
                            {
                                drawList.AddCircle(action.Position, 3, ImGui.GetColorU32(new Vector4(1f, 0.2f, 0f, alpha)), 0, 3);

                                if (index > 0)
                                    drawList.AddLine(lastPos, action.Position, 0f, ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, alpha)));
                                if (index == this.Indexer)
                                    drawList.AddLine(Player.Position, action.Position, 0, 0x00FFFFFF);

                                drawList.AddText(action.Position, ImGui.GetColorU32(new Vector4(alpha + 0.25f)), index.ToString(), 20f);
                            }

                            lastPos = action.Position;
                        }
                    }
                }
            }
        }
    }

    private void DutyState_DutyStarted(object?     sender, ushort e) => this.DutyState = DutyState.DutyStarted;
    private void DutyState_DutyWiped(object?       sender, ushort e) => this.DutyState = DutyState.DutyWiped;
    private void DutyState_DutyRecommenced(object? sender, ushort e) => this.DutyState = DutyState.DutyRecommenced;
    private void DutyState_DutyCompleted(object? sender, ushort e)
    {
        this.DutyState = DutyState.DutyComplete;
        this.CheckFinishing();
    }

    internal void ExitDuty() => this._actions.ExitDuty(new PathAction());

    internal void LoadPath()
    {
        try
        {
            if (this.CurrentTerritoryContent == null || (this.CurrentTerritoryContent != null && this.CurrentTerritoryContent.TerritoryType != Svc.ClientState.TerritoryType))
            {
                if (ContentHelper.DictionaryContent.TryGetValue(Svc.ClientState.TerritoryType, out Content? content))
                {
                    this.CurrentTerritoryContent = content;
                }
                else
                {
                    this.Actions.Clear();
                    this.PathFile = "";
                    return;
                }
            }

            this.Actions.Clear();
            if (!ContentPathsManager.DictionaryPaths.TryGetValue(Svc.ClientState.TerritoryType, out ContentPathsManager.ContentPathContainer? container))
            {
                this.PathFile = $"{this.PathsDirectory.FullName}{Path.DirectorySeparatorChar}({Svc.ClientState.TerritoryType}) {this.CurrentTerritoryContent?.EnglishName?.Replace(":", "")}.json";
                return;
            }

            if (this.States.HasFlag(PluginState.Looping) && this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
            {
                string? s = this.PlaylistCurrentEntry?.path ?? null;
                if(s != null) 
                    this.CurrentPath = container.Paths.IndexOf(dp => dp.FileName.Equals(s, StringComparison.InvariantCultureIgnoreCase));
            }

            ContentPathsManager.DutyPath? path = this.CurrentPath < 0 ?
                                                     container.SelectPath(out this.CurrentPath) :
                                                     container.Paths[this.CurrentPath > -1 ? this.CurrentPath : 0];

            this.PathFile = path?.FilePath ?? "";
            this.Actions     = [.. path?.Actions];
            //Svc.Log.Info($"Loading Path: {CurrentPath} {ListBoxPOSText.Count}");
        }
        catch (Exception e)
        {
            Svc.Log.Error(e.ToString());
            //throw;
        }
    }

    private unsafe bool StopLoop =>
        this.Configuration.EnableTerminationActions &&
        (this.CurrentTerritoryContent == null                                                                               ||
         (this.Configuration.StopLevel      && Player.Level                             >= this.Configuration.StopLevelInt) ||
         (this.Configuration.StopNoRestedXP && AgentHUD.Instance()->ExpRestedExperience == 0)                               ||
         (this.Configuration.StopItemQty && (this.Configuration.StopItemAll ?
                                                 this.Configuration.StopItemQtyItemDictionary.All(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value) :
                                                 this.Configuration.StopItemQtyItemDictionary.Any(x => InventoryManager.Instance()->GetInventoryItemCount(x.Key) >= x.Value.Value))));

    private void TrustLeveling()
    {
        if (this.TrustLevelingEnabled && TrustHelper.Members.Any(tm => tm.Value.Level < tm.Value.LevelCap))
        {
            this.TaskManager.Enqueue(() => Svc.Log.Debug($"Trust Leveling Enabled"),                     "TrustLeveling-Debug");
            this.TaskManager.Enqueue(() => TrustHelper.ClearCachedLevels(this.CurrentTerritoryContent!), "TrustLeveling-ClearCachedLevels");
            this.TaskManager.Enqueue(() => TrustHelper.GetLevels(this.CurrentTerritoryContent),          "TrustLeveling-GetLevels");
            this.TaskManager.DelayNext(50);
            this.TaskManager.Enqueue(() => TrustHelper.State != ActionState.Running, "TrustLeveling-RecheckingTrustLevels");
        }
    }

    private void ClientState_TerritoryChanged(ushort t)
    {
        if (ConfigurationMain.Instance.MultiBox)
        {
            bool isDuty = ContentHelper.DictionaryContent.ContainsKey(t);
            if (!ConfigurationMain.Instance.host)
            {
                if (isDuty)
                {
                    this.Run(t, 1);
                }
            } else
            {
                if(!isDuty)
                    ConfigurationMain.MultiboxUtility.Server.ExitDuty();
            }
        }

        if (this.Stage == Stage.Stopped)
            return;

        Svc.Log.Debug($"ClientState_TerritoryChanged: t={t}");

        this.CurrentTerritoryType    = t;
        this.MainListClicked            = false;
        this.Framework_Update_InDuty = _ => { };
        if (t == 0)
            return;
        this.CurrentPath = -1;

        this.LoadPath();

        if (!this.States.HasFlag(PluginState.Looping) || GCTurninHelper.State == ActionState.Running || RepairHelper.State == ActionState.Running || GotoHelper.State == ActionState.Running || GotoInnHelper.State == ActionState.Running || GotoBarracksHelper.State == ActionState.Running || GotoHousingHelper.State == ActionState.Running || this.CurrentTerritoryContent == null)
        {
            Svc.Log.Debug("We Changed Territories but are doing after loop actions or not running at all or in a Territory not supported by AutoDuty");
            return;
        }

        if (this.Configuration is { ShowOverlay: true, HideOverlayWhenStopped: true } && !this.States.HasFlag(PluginState.Looping))
        {
            this.Overlay.IsOpen = false;
            this.MainWindow.IsOpen = true;
        }

        this.Action = "";

        if (t != this.CurrentTerritoryContent.TerritoryType)
        {
            if (this.CurrentLoop < this.Configuration.LoopTimes || this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
            {
                this.TaskManager.Abort();
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.CurrentLoop} of {this.Configuration.LoopTimes}"), "Loop-Debug");
                this.TaskManager.Enqueue(() => { this.Stage =  Stage.Looping; },                                            "Loop-SetStage=99");
                this.TaskManager.Enqueue(() => { this.States   &= ~PluginState.Navigating; },                                  "Loop-RemoveNavigationState");
                this.TaskManager.Enqueue(() => PlayerHelper.IsReady,                         int.MaxValue, "Loop-WaitPlayerReady");
                if (this.Configuration.EnableBetweenLoopActions)
                {
                    this.TaskManager.Enqueue(() => { this.Action = $"Waiting {this.Configuration.WaitTimeBeforeAfterLoopActions}s"; },                                    "Loop-WaitTimeBeforeAfterLoopActionsActionSet");
                    this.TaskManager.Enqueue(() => EzThrottler.Throttle("Loop-WaitTimeBeforeAfterLoopActions", this.Configuration.WaitTimeBeforeAfterLoopActions * 1000), "Loop-WaitTimeBeforeAfterLoopActionsThrottle");
                    this.TaskManager.Enqueue(() => EzThrottler.Check("Loop-WaitTimeBeforeAfterLoopActions"),                                                              this.Configuration.WaitTimeBeforeAfterLoopActions * 1000, "Loop-WaitTimeBeforeAfterLoopActionsCheck");
                    this.TaskManager.Enqueue(() => { this.Action = $"After Loop Actions"; },                                                                                 "Loop-AfterLoopActionsSetAction");
                }

                this.TrustLeveling();

                this.TaskManager.Enqueue(() =>
                                         {
                                             if (this.StopLoop)
                                             {
                                                 this.TaskManager.Enqueue(() => Svc.Log.Info($"Loop Stop Condition Encountered, Stopping Loop"));
                                                 this.LoopsCompleteActions();
                                             }
                                             else
                                             {
                                                 this.LoopTasks();
                                             }
                                         }, "Loop-CheckStopLoop");

            }
            else
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"Loops Done"),                                                                                                   "Loop-Debug");
                this.TaskManager.Enqueue(() => { this.States &= ~PluginState.Navigating; },                                                                                    "Loop-RemoveNavigationState");
                this.TaskManager.Enqueue(() => PlayerHelper.IsReady,                                                                                                           int.MaxValue, "Loop-WaitPlayerReady");
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"Loop {this.CurrentLoop} == {this.Configuration.LoopTimes} we are done Looping, Invoking LoopsCompleteActions"), "Loop-Debug");
                this.TaskManager.Enqueue(() =>
                                         {
                                             if (this.Configuration.ExecuteBetweenLoopActionLastLoop)
                                                 this.LoopTasks(false);
                                             else
                                                 this.LoopsCompleteActions();
                                         },     "Loop-LoopCompleteActions");
            }
        }
    }

    private unsafe void Condition_ConditionChange(ConditionFlag flag, bool value)
    {
        if (this.Stage == Stage.Stopped) return;

        if (flag == ConditionFlag.Unconscious)
        {
            switch (value)
            {
                case true when (this.Stage != Stage.Dead || DeathHelper.DeathState != PlayerLifeState.Dead):
                    Svc.Log.Debug($"We Died, Setting Stage to Dead");
                    DeathHelper.DeathState = PlayerLifeState.Dead;
                    this.Stage             = Stage.Dead;
                    break;
                case false when (this.Stage != Stage.Revived || DeathHelper.DeathState != PlayerLifeState.Revived):
                    Svc.Log.Debug($"We Revived, Setting Stage to Revived");
                    DeathHelper.DeathState = PlayerLifeState.Revived;
                    this.Stage             = Stage.Revived;
                    break;
            }

            return;
        }
        //Svc.Log.Debug($"{flag} : {value}");
        if (this.Stage is not Stage.Dead and not Stage.Revived and not Stage.Action && !this._recentlyWatchedCutscene && !Conditions.Instance()->WatchingCutscene && 
            flag is not ConditionFlag.WatchingCutscene and not ConditionFlag.WatchingCutscene78 and not ConditionFlag.OccupiedInCutSceneEvent and 
                (ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51 or ConditionFlag.Jumping61) && 
            value && this.States.HasFlag(PluginState.Navigating))
        {
            Svc.Log.Info($"Condition_ConditionChange: Indexer Increase and Change Stage to Condition");
            this.Indexer++;
            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Condition;
        }
        if (Conditions.Instance()->WatchingCutscene || flag is ConditionFlag.WatchingCutscene or ConditionFlag.WatchingCutscene78 or ConditionFlag.OccupiedInCutSceneEvent)
        {
            this._recentlyWatchedCutscene = true;
            SchedulerHelper.ScheduleAction("RecentlyWatchedCutsceneTimer", () => this._recentlyWatchedCutscene = false, 5000);
        }
    }

    public void Run(uint territoryType = 0, int loops = 0, bool startFromZero = true, bool bareMode = false)
    {
        if(this.InDungeon)
            Plugin.Configuration.AutoDutyModeEnum = AutoDutyMode.Looping;

        Svc.Log.Debug($"Run: territoryType={territoryType} loops={loops} bareMode={bareMode}");
        if (territoryType > 0)
        {
            if (ContentHelper.DictionaryContent.TryGetValue(territoryType, out Content? content))
            {
                this.CurrentTerritoryContent = content;
            }
            else
            {
                Svc.Log.Error($"({territoryType}) is not in our Dictionary as a compatible Duty");
                return;
            }
        }

        if (this.CurrentTerritoryContent == null)
            return;

        if (loops > 0) this.Configuration.LoopTimes = loops;

        if (bareMode)
        {
            this._bareModeSettingsActive |= SettingsActive.BareMode_Active;
            if (this.Configuration.EnablePreLoopActions)
                this._bareModeSettingsActive |= SettingsActive.PreLoop_Enabled;
            if (this.Configuration.EnableBetweenLoopActions) 
                this._bareModeSettingsActive |= SettingsActive.BetweenLoop_Enabled;
            if (this.Configuration.EnableTerminationActions) 
                this._bareModeSettingsActive |= SettingsActive.TerminationActions_Enabled;
            this.Configuration.EnablePreLoopActions     = false;
            this.Configuration.EnableBetweenLoopActions = false;
            this.Configuration.EnableTerminationActions = false;
        }

        Svc.Log.Info($"Running AutoDuty in {this.CurrentTerritoryContent.EnglishName}, Looping {this.Configuration.LoopTimes} times{(bareMode ? " in BareMode (No Pre, Between or Termination Loop Actions)" : "")}");

        //MainWindow.OpenTab("Mini");
        if (this.Configuration.ShowOverlay)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.Stage =  Stage.Looping;
        this.States   |= PluginState.Looping;
        this.SetGeneralSettings(false);
        if (!VNavmesh_IPCSubscriber.Path_GetMovementAllowed())
            VNavmesh_IPCSubscriber.Path_SetMovementAllowed(true);
        this.TaskManager.Abort();
        Svc.Log.Info($"Running {this.CurrentTerritoryContent.Name} {this.Configuration.LoopTimes} Times");
        if (!this.InDungeon)
        {
            this.CurrentLoop = 0;
            if (this.Configuration.EnablePreLoopActions)
            {
                if (this.Configuration.ExecuteCommandsPreLoop)
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsPreLoop, executing {this.Configuration.CustomCommandsTermination.Count} commands"));
                    this.Configuration.CustomCommandsPreLoop.Each(x => this.TaskManager.Enqueue(() => Chat.ExecuteCommand(x), "Run-ExecuteCommandsPreLoop"));
                }

                if (this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist && Plugin.PlaylistCurrentEntry != null)
                    unsafe
                    {
                        if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                        {
                            this.TaskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                            this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull);
                        }
                    }

                this.AutoConsume();

                if (this.LevelingModeEnum == LevelingMode.None) 
                    this.AutoEquipRecommendedGear();

                if (this.Configuration.AutoRepair && InventoryHelper.CanRepair())
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"AutoRepair PreLoop Action"));
                    this.TaskManager.Enqueue(() => RepairHelper.Invoke(), "Run-AutoRepair");
                    this.TaskManager.DelayNext("Run-AutoRepairDelay50", 50);
                    this.TaskManager.Enqueue(() => RepairHelper.State != ActionState.Running, int.MaxValue, "Run-WaitAutoRepairComplete");
                    this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull, "Run-WaitAutoRepairIsReadyFull");
                }

                if (this.Configuration.DutyModeEnum != DutyMode.Squadron && this.Configuration.RetireMode)
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Retire PreLoop Action"));
                    if (this.Configuration.RetireLocationEnum == RetireLocation.GC_Barracks)
                        this.TaskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Run-GotoBarracksInvoke");
                    else if (this.Configuration.RetireLocationEnum == RetireLocation.Inn)
                        this.TaskManager.Enqueue(() => GotoInnHelper.Invoke(), "Run-GotoInnInvoke");
                    else
                        this.TaskManager.Enqueue(() => GotoHousingHelper.Invoke((Housing)this.Configuration.RetireLocationEnum), "Run-GotoHousingInvoke");
                    this.TaskManager.DelayNext("Run-RetireModeDelay50", 50);
                    this.TaskManager.Enqueue(() => GotoHousingHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, int.MaxValue, "Run-WaitGotoComplete");
                }
            }

            this.TaskManager.Enqueue(() => Svc.Log.Debug($"Queueing First Run"));
            this.Queue(this.CurrentTerritoryContent);
        }

        this.TaskManager.Enqueue(() => Svc.Log.Debug($"Done Queueing-WaitDutyStarted, NavIsReady"));
        this.TaskManager.Enqueue(() => Svc.DutyState.IsDutyStarted,          "Run-WaitDutyStarted");
        this.TaskManager.Enqueue(() => VNavmesh_IPCSubscriber.Nav_IsReady(), int.MaxValue, "Run-WaitNavIsReady");
        this.TaskManager.Enqueue(() => Svc.Log.Debug($"Start Navigation"));
        this.TaskManager.Enqueue(() => this.StartNavigation(startFromZero), "Run-StartNavigation");

        if (this.CurrentLoop == 0)
        {
            this.CurrentLoop = 1;
            if (this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist) 
                Plugin.Configuration.LoopTimes = Plugin.PlaylistCurrentEntry?.count ?? Plugin.Configuration.LoopTimes;
        }
    }

    internal unsafe void LoopTasks(bool queue = true)
    {
        if (this.CurrentTerritoryContent == null) return;

        if (this.Configuration.EnableBetweenLoopActions)
        {
            if (this.Configuration.ExecuteCommandsBetweenLoop)
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsBetweenLoops, executing {this.Configuration.CustomCommandsBetweenLoop.Count} commands"));
                this.Configuration.CustomCommandsBetweenLoop.Each(x => Chat.ExecuteCommand(x));
                this.TaskManager.DelayNext("Loop-DelayAfterCommands", 1000);
            }

            if (this.Configuration.AutoOpenCoffers)
                EnqueueActiveHelper<CofferHelper>();

            if (AutoRetainer_IPCSubscriber.RetainersAvailable())
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"AutoRetainer BetweenLoop Actions"));
                if (this.Configuration.EnableAutoRetainer)
                {
                    this.TaskManager.Enqueue(() => AutoRetainerHelper.Invoke(), "Loop-AutoRetainer");
                    this.TaskManager.DelayNext("Loop-Delay50", 50);
                    this.TaskManager.Enqueue(() => AutoRetainerHelper.State != ActionState.Running, int.MaxValue, "Loop-WaitAutoRetainerComplete");
                }
                else
                {
                    this.TaskManager.Enqueue(() => AutoRetainer_IPCSubscriber.IsBusy(),  15000,        "Loop-AutoRetainerIntegrationDisabledWait15sRetainerSense");
                    this.TaskManager.Enqueue(() => !AutoRetainer_IPCSubscriber.IsBusy(), int.MaxValue, "Loop-AutoRetainerIntegrationDisabledWaitARNotBusy");
                    this.TaskManager.Enqueue(() => AutoRetainerHelper.ForceStop(),       "Loop-AutoRetainerStop");
                }
            }
        }


        if (queue && this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
        {
            PlaylistEntry? currentEntry = Plugin.PlaylistCurrentEntry;
            if (currentEntry != null && ++currentEntry.curCount < currentEntry.count)
            {
                Svc.Log.Debug($"repeating the duty once more: {currentEntry.curCount + 1} of {currentEntry.count}");
            }
            else
            {
                Svc.Log.Debug("next playlist entry");
                Plugin.PlaylistIndex++;
                if (Plugin.PlaylistIndex >= Plugin.PlaylistCurrent.Count)
                {
                    Svc.Log.Debug("playlist done");
                    queue                = false;
                    Plugin.PlaylistIndex = 0;
                }
                else
                {
                    Plugin.PlaylistCurrentEntry!.curCount = 0;

                    if (Plugin.PlaylistCurrentEntry.gearset.HasValue && RaptureGearsetModule.Instance()->IsValidGearset(Plugin.PlaylistCurrentEntry.gearset.Value))
                    {
                        this.TaskManager.Enqueue(() => RaptureGearsetModule.Instance()->EquipGearset(Plugin.PlaylistCurrentEntry.gearset.Value));
                        this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull);
                    }
                }
            }
        }

        if (this.Configuration.EnableBetweenLoopActions)
        {
            this.AutoEquipRecommendedGear();

            if (this.Configuration.AutoRepair && InventoryHelper.CanRepair()) 
                EnqueueActiveHelper<RepairHelper>();

            if (this.Configuration.AutoExtract && QuestManager.IsQuestComplete(66174)) 
                EnqueueActiveHelper<ExtractHelper>();

            if (this.Configuration.AutoDesynth) 
                EnqueueActiveHelper<DesynthHelper>();

            if (this.Configuration.AutoGCTurnin && (!this.Configuration.AutoGCTurninSlotsLeftBool || InventoryManager.Instance()->GetEmptySlotsInBag() <= this.Configuration.AutoGCTurninSlotsLeft) && PlayerHelper.GetGrandCompanyRank() > 5)
                EnqueueActiveHelper<GCTurninHelper>();

            
            if (this.Configuration.TripleTriadRegister) 
                EnqueueActiveHelper<TripleTriadCardUseHelper>();
            if (this.Configuration.TripleTriadSell) 
                EnqueueActiveHelper<TripleTriadCardSellHelper>();
        

            if (this.Configuration.DiscardItems) 
                EnqueueActiveHelper<DiscardHelper>();

            if (this.Configuration.DutyModeEnum != DutyMode.Squadron && this.Configuration.RetireMode)
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"Retire Between Loop Action"));

                switch (this.Configuration.RetireLocationEnum)
                {
                    case RetireLocation.GC_Barracks:
                        this.TaskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Loop-GotoBarracksInvoke");
                        break;
                    case RetireLocation.Inn:
                        this.TaskManager.Enqueue(() => GotoInnHelper.Invoke(), "Loop-GotoInnInvoke");
                        break;
                    case RetireLocation.Apartment:
                    case RetireLocation.Personal_Home:
                    case RetireLocation.FC_Estate:
                    default:
                        Svc.Log.Info($"{(Housing)this.Configuration.RetireLocationEnum} {this.Configuration.RetireLocationEnum}");
                        this.TaskManager.Enqueue(() => GotoHousingHelper.Invoke((Housing)this.Configuration.RetireLocationEnum), "Loop-GotoHousingInvoke");
                        break;
                }

                this.TaskManager.DelayNext("Loop-Delay50", 50);
                this.TaskManager.Enqueue(() => GotoHousingHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, int.MaxValue, "Loop-WaitGotoComplete");
            }
        }

        void EnqueueActiveHelper<T>() where T : ActiveHelperBase<T>, new()
        {
            this.TaskManager.Enqueue(() => Svc.Log.Debug($"Enqueueing {typeof(T).Name}"), "Loop-ActiveHelper");
            this.TaskManager.Enqueue(() => ActiveHelperBase<T>.Invoke(), $"Loop-{typeof(T).Name}");
            this.TaskManager.DelayNext("Loop-Delay50", 50);
            this.TaskManager.Enqueue(() => ActiveHelperBase<T>.State != ActionState.Running, int.MaxValue, $"Loop-Wait-{typeof(T).Name}-Complete");
            this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull, "Loop-WaitIsReadyFull");
        }

        if (queue || ConfigurationMain.Instance is { MultiBox: true, host: false }) 
            this.AutoConsume();

        if (ConfigurationMain.Instance.MultiBox)
        {
            if (ConfigurationMain.Instance.host)
                ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep = true;
            else
                this.TaskManager.Enqueue(() => ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep = true);
        }

        if (!queue)
        {
            this.LoopsCompleteActions();
            return;
        }
        
        SchedulerHelper.ScheduleAction("LoopContinueTask", () =>
                                                           {
                                                               if (this.Configuration.AutoDutyModeEnum == AutoDutyMode.Looping && this.LevelingEnabled)
                                                               {
                                                                   Svc.Log.Info("Leveling Enabled");
                                                                   Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);
                                                                   if (duty != null)
                                                                   {
                                                                       if (this.LevelingModeEnum      == LevelingMode.Support && this.Configuration.PreferTrustOverSupportLeveling &&
                                                                           duty.ClassJobLevelRequired > 70)
                                                                       {
                                                                           this.levelingModeEnum        = LevelingMode.Trust_Solo;
                                                                           this.Configuration.dutyModeEnum = DutyMode.Trust;

                                                                           Content? dutyTrust = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);

                                                                           if (duty != dutyTrust)
                                                                           {
                                                                               this.levelingModeEnum           = LevelingMode.Support;
                                                                               this.Configuration.dutyModeEnum = DutyMode.Support;
                                                                           }
                                                                       }

                                                                       Svc.Log.Info("Next Leveling Duty: " + duty.Name);
                                                                       this.CurrentTerritoryContent = duty;
                                                                       ContentPathsManager.DictionaryPaths[duty.TerritoryType].SelectPath(out this.CurrentPath);
                                                                   }
                                                                   else
                                                                   {
                                                                       this.CurrentLoop = this.Configuration.LoopTimes;
                                                                       this.LoopsCompleteActions();
                                                                       return;
                                                                   }
                                                               }

                                                               this.TaskManager.Enqueue(() => Svc.Log.Debug($"Registering New Loop"));
                                                               this.Queue(this.CurrentTerritoryContent);
                                                               this.TaskManager.Enqueue(() =>
                                                                                            Svc.Log
                                                                                               .Debug($"Incrementing LoopCount, Setting Action Var, Wait for CorrectTerritory, PlayerIsValid, DutyStarted, and NavIsReady"));
                                                               this.TaskManager.Enqueue(() =>
                                                                                        {
                                                                                            if (this.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
                                                                                            {
                                                                                                this.CurrentLoop               = this.PlaylistCurrentEntry?.curCount ?? this.CurrentLoop + 1;
                                                                                                Plugin.Configuration.LoopTimes = this.PlaylistCurrentEntry?.count ?? Plugin.Configuration.LoopTimes;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                this.CurrentLoop ++;
                                                                                            }
                                                                                        }, "Loop-IncrementCurrentLoop");
                                                               this.TaskManager.Enqueue(() => { this.Action = $"Looping: {this.CurrentTerritoryContent.Name} {this.CurrentLoop} of {this.Configuration.LoopTimes}"; }, "Loop-SetAction");
                                                               this.TaskManager.Enqueue(() => Svc.ClientState.TerritoryType == this.CurrentTerritoryContent.TerritoryType,                                                int.MaxValue, "Loop-WaitCorrectTerritory");
                                                               this.TaskManager.Enqueue(() => PlayerHelper.IsValid,                 int.MaxValue, "Loop-WaitPlayerValid");
                                                               this.TaskManager.Enqueue(() => Svc.DutyState.IsDutyStarted,          int.MaxValue, "Loop-WaitDutyStarted");
                                                               this.TaskManager.Enqueue(() => VNavmesh_IPCSubscriber.Nav_IsReady(), int.MaxValue, "Loop-WaitNavReady");
                                                               this.TaskManager.Enqueue(() => Svc.Log.Debug($"StartNavigation"));
                                                               this.TaskManager.Enqueue(() => this.StartNavigation(true), "Loop-StartNavigation");
                                                           }, () => !ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep);
    }

    private void LoopsCompleteActions()
    {
        this.SetGeneralSettings(false);

        if (this.Configuration.EnableTerminationActions)
        {
            this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull);
            this.TaskManager.Enqueue(() => Svc.Log.Debug($"TerminationActions are Enabled"));
            if (this.Configuration.ExecuteCommandsTermination)
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"ExecutingCommandsTermination, executing {this.Configuration.CustomCommandsTermination.Count} commands"));
                this.Configuration.CustomCommandsTermination.Each(x => Chat.ExecuteCommand(x));
            }

            if (this.Configuration.PlayEndSound)
            {
                this.TaskManager.Enqueue(() => Svc.Log.Debug($"Playing End Sound"));
                SoundHelper.StartSound(this.Configuration.PlayEndSound, this.Configuration.CustomSound, this.Configuration.SoundEnum);
            }

            switch (this.Configuration.TerminationMethodEnum)
            {
                case TerminationMode.Kill_PC:
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Killing PC"));
                    if (!this.Configuration.TerminationKeepActive)
                    {
                        this.Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                        this.Configuration.Save();
                    }

                    this.TaskManager.Enqueue(() =>
                                             {
                                                 if (OperatingSystem.IsWindows())
                                                 {
                                                     ProcessStartInfo startinfo = new("shutdown.exe", "-s -t 20");
                                                     Process.Start(startinfo);
                                                 }
                                                 else if (OperatingSystem.IsLinux())
                                                 {
                                                     //Educated guess
                                                     ProcessStartInfo startinfo = new("shutdown", "-t 20");
                                                     Process.Start(startinfo);
                                                 }
                                                 else if (OperatingSystem.IsMacOS())
                                                 {
                                                     //hell if I know
                                                 }
                                             }, "Enqueuing SystemShutdown");
                    this.TaskManager.Enqueue(() => Chat.ExecuteCommand($"/xlkill"), "Killing the game");
                    break;
                }
                case TerminationMode.Kill_Client:
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Killing Client"));
                    if (!this.Configuration.TerminationKeepActive)
                    {
                        this.Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                        this.Configuration.Save();
                    }

                    this.TaskManager.Enqueue(() => Chat.ExecuteCommand($"/xlkill"), "Killing the game");
                    break;
                }
                case TerminationMode.Logout:
                {
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Logging Out"));
                    if (!this.Configuration.TerminationKeepActive)
                    {
                        this.Configuration.TerminationMethodEnum = TerminationMode.Do_Nothing;
                        this.Configuration.Save();
                    }

                    this.TaskManager.Enqueue(() => PlayerHelper.IsReady);
                    this.TaskManager.DelayNext(2000);
                    this.TaskManager.Enqueue(() => Chat.ExecuteCommand($"/logout"));
                    this.TaskManager.Enqueue(() => AddonHelper.ClickSelectYesno());
                    break;
                }
                case TerminationMode.Start_AR_Multi_Mode:
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Starting AR Multi Mode"));
                    this.TaskManager.Enqueue(() => Chat.ExecuteCommand($"/ays multi e"));
                    break;
                case TerminationMode.Start_AR_Night_Mode:
                    this.TaskManager.Enqueue(() => Svc.Log.Debug($"Starting AR Night Mode"));
                    this.TaskManager.Enqueue(() => Chat.ExecuteCommand($"/ays night e"));
                    break;
                case TerminationMode.Do_Nothing:
                default:
                    break;
            }
        }

        Svc.Log.Debug($"Removing Looping, Setting CurrentLoop to 0, and Setting Stage to Stopped");

        this.States   &= ~PluginState.Looping;
        this.CurrentLoop =  0;
        this.TaskManager.Enqueue(() => SchedulerHelper.ScheduleAction("SetStageStopped", () => this.Stage = Stage.Stopped, 1));
    }

    private void AutoEquipRecommendedGear()
    {
        if (this.Configuration.AutoEquipRecommendedGear)
        {
            this.TaskManager.Enqueue(() => Svc.Log.Debug($"AutoEquipRecommendedGear Between Loop Action"));
            this.TaskManager.Enqueue(() => AutoEquipHelper.Invoke(), "AutoEquipRecommendedGear-Invoke");
            this.TaskManager.DelayNext("AutoEquipRecommendedGear-Delay50", 50);
            this.TaskManager.Enqueue(() => AutoEquipHelper.State != ActionState.Running, int.MaxValue, "AutoEquipRecommendedGear-WaitAutoEquipComplete");
            this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull, "AutoEquipRecommendedGear-WaitANotIsOccupied");
        }
    }

    private void AutoConsume()
    {
        if (this.Configuration.AutoConsume)
        {
            this.TaskManager.Enqueue(() => Svc.Log.Debug($"AutoConsume PreLoop Action"));
            this.Configuration.AutoConsumeItemsList.Each(x =>
                                                         {
                                                             bool isAvailable = InventoryHelper.IsItemAvailable(x.Value.ItemId, x.Value.CanBeHq);
                                                             if (isAvailable)
                                                             {
                                                                 if (this.Configuration.AutoConsumeIgnoreStatus)
                                                                     this.TaskManager.Enqueue(() => InventoryHelper.UseItemUntilAnimationLock(x.Value.ItemId, x.Value.CanBeHq), $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
                                                                 else
                                                                     this.TaskManager.Enqueue(() => InventoryHelper.UseItemUntilStatus(x.Value.ItemId, x.Key, Plugin.Configuration.AutoConsumeTime * 60, x.Value.CanBeHq), $"AutoConsume - {x.Value.Name} is available: {isAvailable}");
                                                             }

                                                             this.TaskManager.DelayNext("AutoConsume-DelayNext50", 50);
                                                             this.TaskManager.Enqueue(() => PlayerHelper.IsReadyFull, "AutoConsume-WaitPlayerIsReadyFull");
                                                             this.TaskManager.DelayNext("AutoConsume-DelayNext250", 250);
                                                         });
        }
    }

    private void Queue(Content content)
    {
        if (this.Configuration.DutyModeEnum == DutyMode.Variant)
        {
            this._variantManager.RegisterVariantDuty(content);
        }
        else if (this.Configuration.DutyModeEnum.EqualsAny(DutyMode.Regular, DutyMode.Trial, DutyMode.Raid, DutyMode.Support, DutyMode.Trust))
        {
            this.TaskManager.Enqueue(() => QueueHelper.Invoke(content, this.Configuration.DutyModeEnum), "Queue-Invoke");
            this.TaskManager.DelayNext("Queue-Delay50", 50);
            this.TaskManager.Enqueue(() => QueueHelper.State != ActionState.Running, int.MaxValue, "Queue-WaitQueueComplete");
        }
        else if (this.Configuration.DutyModeEnum == DutyMode.Squadron)
        {
            this.TaskManager.Enqueue(() => GotoBarracksHelper.Invoke(), "Queue-GotoBarracksInvoke");
            this.TaskManager.DelayNext("Queue-GotoBarracksDelay50", 50);
            this.TaskManager.Enqueue(() => GotoBarracksHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running, int.MaxValue, "Queue-WaitGotoComplete");
            this._squadronManager.RegisterSquadron(content);
        }

        this.TaskManager.Enqueue(() => !PlayerHelper.IsValid, "Queue-WaitNotValid");
        this.TaskManager.Enqueue(() => PlayerHelper.IsValid, int.MaxValue, "Queue-WaitValid");
    }

    private void StageReadingPath()
    {
        if (!PlayerHelper.IsValid || !EzThrottler.Check("PathFindFailure") || this.Indexer == -1 || this.Indexer >= this.Actions.Count)
            return;

        if (ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep)
        {
            if (PartyHelper.PartyInCombat() && Plugin.StopForCombat)
            {
                if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false })
                    this.SetRotationPluginSettings(true);
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Waiting_For_Combat;
            }
            return;
        }


        this.Action = $"{(this.Actions.Count >= this.Indexer ? Plugin.Actions[this.Indexer].ToCustomString() : "")}";

        this.PathAction = this.Actions[this.Indexer];

        Svc.Log.Debug($"Starting Action {this.PathAction.ToCustomString()}");

        bool sync = !this.Configuration.Unsynced || !this.Configuration.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);
        if (this.PathAction.Tag.HasFlag(ActionTag.Unsynced) && sync)
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer]} because we are synced");
            this.Indexer++;
            return;
        }

        if (this.PathAction.Tag.HasFlag(ActionTag.W2W) && !this.Configuration.IsW2W(unsync: !sync))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer]} because we are not W2W-ing");
            this.Indexer++;
            return;
        }

        if (this.PathAction.Tag.HasFlag(ActionTag.Synced) && this.Configuration.Unsynced)
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer]} because we are unsynced");
            this.Indexer++;
            return;
        }

        if (this.PathAction.Tag.HasFlag(ActionTag.Comment))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer].Name} because it is a comment");
            this.Indexer++;
            return;
        }

        if (this.PathAction.Tag.HasFlag(ActionTag.Revival))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer].Name} because it is a Revival Tag");
            this.Indexer++;
            return;
        }

        if ((this.SkipTreasureCoffer || !this.Configuration.LootTreasure || this.Configuration.LootBossTreasureOnly) && this.PathAction.Tag.HasFlag(ActionTag.Treasure))
        {
            Svc.Log.Debug($"Skipping path entry {this.Actions[this.Indexer].Name} because we are either in revival mode, LootTreasure is off or BossOnly");
            this.Indexer++;
            return;
        }

        BossMod_IPCSubscriber.InBoss(this.PathAction.Name.Equals("Boss"));

        if(ConfigurationMain.Instance.host)
            ConfigurationMain.MultiboxUtility.MultiboxBlockingNextStep = false;

        if (this.PathAction.Position == Vector3.Zero)
        {
            this.Stage = Stage.Action;
            return;
        }

        if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress() && !VNavmesh_IPCSubscriber.Path_IsRunning())
        {
            Chat.ExecuteCommand("/automove off");
            VNavmesh_IPCSubscriber.Path_SetTolerance(0.25f);
            if (this.PathAction is { Name: "MoveTo", Arguments.Count: > 0 } && bool.TryParse(this.PathAction.Arguments[0], out bool useMesh) && !useMesh)
                VNavmesh_IPCSubscriber.Path_MoveTo([this.PathAction.Position], false);
            else
                VNavmesh_IPCSubscriber.SimpleMove_PathfindAndMoveTo(this.PathAction.Position, false);

            this.Stage = Stage.Moving;
        }
    }

    private void StageMoving()
    {
        if (!PlayerHelper.IsReady || this.Indexer == -1 || this.Indexer >= this.Actions.Count)
            return;

        this.Action = $"{Plugin.Actions[this.Indexer].ToCustomString()}";
        if (PartyHelper.PartyInCombat() && Plugin.StopForCombat)
        {
            if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) this.SetRotationPluginSettings(true);
            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Waiting_For_Combat;
            return;
        }

        unsafe
        {
            if (ActionManager.Instance()->CastActionId == 6)
                return;

            if (!PlayerHelper.IsCasting && StuckHelper.IsStuck(out byte stuckCount))
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                if (this.Configuration.StuckReturn && stuckCount >= this.Configuration.StuckReturnX)
                {
                    Svc.Log.Debug($"Using Stuck Return Action");
                    if (ActionManager.Instance()->GetActionStatus(ActionType.Action, 6) == 0)
                    {
                        BossMod_IPCSubscriber.SetMovement(false);
                        this.SetRotationPluginSettings(false, false, true);
                        ActionManager.Instance()->UseAction(ActionType.Action, 6); // Chat.ExecuteCommand("/return");
                        SchedulerHelper.ScheduleAction("StuckHelperReturnInsurance", () =>
                                                                                     {
                                                                                         VNavmesh_IPCSubscriber.Path_Stop();
                                                                                         ActionManager.Instance()->UseAction(ActionType.Action, 6); //Chat.ExecuteCommand("/return");
                                                                                     }, () => ActionManager.Instance()->CastActionId != 6 && 
                                                                                              ActionManager.Instance()->GetActionStatus(ActionType.Action, 6) == 0 && PlayerHelper.IsReady, false);

                        SchedulerHelper.ScheduleAction("StuckHelperReturn", () =>
                                                                            {
                                                                                VNavmesh_IPCSubscriber.Path_Stop();
                                                                                Plugin.Stage           = Stage.Revived;
                                                                                DeathHelper.DeathState = PlayerLifeState.Revived;

                                                                                SchedulerHelper.ScheduleAction("StuckHelperUnschedule", () => SchedulerHelper.DescheduleAction("StuckHelperReturnInsurance"),
                                                                                                               () => DeathHelper.DeathState == PlayerLifeState.Alive);
                                                                            }, () => ActionManager.Instance()->CastActionId != 6 && PlayerHelper.IsReady);
                        return;
                    }
                    else
                    {
                        Svc.Log.Debug("Return action not available");
                    }
                }
                else if (this.Configuration.RebuildNavmeshOnStuck && stuckCount >= this.Configuration.RebuildNavmeshAfterStuckXTimes)
                {
                    VNavmesh_IPCSubscriber.Nav_Rebuild();
                }

                this.Stage = Stage.Reading_Path;
                return;
            }
        }

        if ((!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress() && VNavmesh_IPCSubscriber.Path_NumWaypoints() == 0) || (!this.PathAction.Name.IsNullOrEmpty() && this.PathAction.Position != Vector3.Zero && ObjectHelper.GetDistanceToPlayer(this.PathAction.Position) <= (this.PathAction.Name.EqualsIgnoreCase("Interactable") ? 2f : 0.25f)))
        {
            if (this.PathAction.Name.IsNullOrEmpty() || this.PathAction.Name.Equals("MoveTo") || this.PathAction.Name.Equals("TreasureCoffer") || this.PathAction.Name.Equals("Revival"))
            {
                this.Stage = Stage.Reading_Path;
                this.Indexer++;
            }
            else
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
            }

            return;
        }

        if (EzThrottler.Throttle("BossChecker", 25) && this.PathAction.Equals("Boss") && this.PathAction.Position != Vector3.Zero && ObjectHelper.BelowDistanceToPlayer(this.PathAction.Position, 50, 10))
        {
            this.BossObject = ObjectHelper.GetBossObject(25);
            if (this.BossObject != null)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
                return;
            }
        }
    }

    private void StageAction()
    {
        if (this.Indexer == -1 || this.Indexer >= this.Actions.Count)
            return;
        
        if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false } && !Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]) this.SetRotationPluginSettings(true);
        
        if (!this.TaskManager.IsBusy)
        {
            this.Stage = Stage.Reading_Path;
            this.Indexer++;
            return;
        }
    }

    private void StageWaitingForCombat()
    {
        if (!EzThrottler.Throttle("CombatCheck", 250) || !PlayerHelper.IsReady || this.Indexer == -1 || this.Indexer >= this.Actions.Count || this.PathAction == null)
            return;

        this.Action = $"Waiting For Combat";

        
        if (ReflectionHelper.Avarice_Reflection.PositionalChanged(out Positional positional))
            BossMod_IPCSubscriber.SetPositional(positional);

        if (this.PathAction.Name.Equals("Boss") && this.PathAction.Position != Vector3.Zero && ObjectHelper.GetDistanceToPlayer(this.PathAction.Position) < 50)
        {
            this.BossObject = ObjectHelper.GetBossObject(25);
            if (this.BossObject != null)
            {
                VNavmesh_IPCSubscriber.Path_Stop();
                this.Stage = Stage.Action;
                return;
            }
        }

        if (PartyHelper.PartyInCombat())
        {
            if (Svc.Targets.Target == null)
            {
                //find and target closest attackable npc, if we are not targeting
                IGameObject? gos = ObjectHelper.GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)?.FirstOrDefault(o => o.GetNameplateKind() is NameplateKind.HostileEngagedSelfUndamaged or NameplateKind.HostileEngagedSelfDamaged && ObjectHelper.GetBattleDistanceToPlayer(o) <= 75);

                if (gos != null)
                    Svc.Targets.Target = gos;
            }
            if (this.Configuration.AutoManageBossModAISettings)
            {
                if (Svc.Targets.Target != null)
                {
                    int enemyCount = ObjectFunctions.GetAttackableEnemyCountAroundPoint(Svc.Targets.Target.Position, 15);

                    if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress() && VNavmesh_IPCSubscriber.Path_IsRunning())
                        VNavmesh_IPCSubscriber.Path_Stop();

                    if (enemyCount > 2)
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {this.Configuration.MaxDistanceToTargetAoEFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(this.Configuration.MaxDistanceToTargetAoEFloat);
                    }
                    else
                    {
                        Svc.Log.Debug($"Changing MaxDistanceToTarget to {this.Configuration.MaxDistanceToTargetFloat}, because enemy count = {enemyCount}");
                        BossMod_IPCSubscriber.SetRange(this.Configuration.MaxDistanceToTargetFloat);
                    }
                }
            }
            else if (!VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress() && VNavmesh_IPCSubscriber.Path_IsRunning())
            {
                VNavmesh_IPCSubscriber.Path_Stop();
            }
        }
        else if (!PartyHelper.PartyInCombat() && !VNavmesh_IPCSubscriber.SimpleMove_PathfindInProgress())
        {
            BossMod_IPCSubscriber.SetRange(this.Configuration.MaxDistanceToTargetFloat);

            VNavmesh_IPCSubscriber.Path_Stop();
            this.Stage = Stage.Reading_Path;
        }
    }

    public void StartNavigation(bool startFromZero = true)
    {
        Svc.Log.Debug($"StartNavigation: startFromZero={startFromZero}");
        if (ContentHelper.DictionaryContent.TryGetValue(Svc.ClientState.TerritoryType, out Content? content))
        {
            this.CurrentTerritoryContent = content;
            this.PathFile                   = $"{Plugin.PathsDirectory.FullName}/({Svc.ClientState.TerritoryType}) {content.EnglishName?.Replace(":", "")}.json";
            this.LoadPath();
        }
        else
        {
            this.CurrentTerritoryContent = null;
            this.PathFile                   = "";
            MainWindow.ShowPopup("Error", "Unable to load content for Territory");
            return;
        }
        //MainWindow.OpenTab("Mini");
        if (this.Configuration.ShowOverlay)
            //MainWindow.IsOpen = false;
            this.Overlay.IsOpen = true;

        this.MainListClicked =  false;
        this.Stage           =  Stage.Reading_Path;
        this.States          |= PluginState.Navigating;
        this.StopForCombat      =  true;
        if (this.Configuration.AutoManageVnavAlignCamera && !VNavmesh_IPCSubscriber.Path_GetAlignCamera())
            VNavmesh_IPCSubscriber.Path_SetAlignCamera(true);

        if (this.Configuration is { AutoManageBossModAISettings: true, BM_UpdatePresetsAutomatically: true })
        {
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty",         Resources.AutoDutyPreset);
            BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
        }

        if (this.Configuration.AutoManageBossModAISettings) this.SetBMSettings();
        if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) this.SetRotationPluginSettings(true);
        if (this.Configuration.LootTreasure)
        {
            if (PandorasBox_IPCSubscriber.IsEnabled)
                PandorasBox_IPCSubscriber.SetFeatureEnabled("Automatically Open Chests", this.Configuration.LootMethodEnum is LootMethod.Pandora or LootMethod.All);
            this._lootTreasure = this.Configuration.LootMethodEnum is LootMethod.AutoDuty or LootMethod.All;
        }
        else
        {
            if (PandorasBox_IPCSubscriber.IsEnabled)
                PandorasBox_IPCSubscriber.SetFeatureEnabled("Automatically Open Chests", false);
            this._lootTreasure = false;
        }
        Svc.Log.Info("Starting Navigation");
        if (startFromZero) this.Indexer = 0;
    }

    private void DoneNavigating()
    {
        this.States &= ~PluginState.Navigating;
        this.CheckFinishing();
    }

    private void CheckFinishing()
    {
        //we finished lets exit the duty or stop
        if ((this.Configuration.AutoExitDuty || this.CurrentLoop < this.Configuration.LoopTimes))
        {
            if (!this.Stage.EqualsAny(Stage.Stopped, Stage.Paused)                                  &&
                (!this.Configuration.OnlyExitWhenDutyDone || this.DutyState == DutyState.DutyComplete) &&
                !this.States.HasFlag(PluginState.Navigating))
            {
                if (ExitDutyHelper.State != ActionState.Running) this.ExitDuty();
                if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) this.SetRotationPluginSettings(false);
                if (this.Configuration.AutoManageBossModAISettings) 
                    BossMod_IPCSubscriber.DisablePresets();
            }
        }
        else
        {
            this.Stage = Stage.Stopped;
        }
    }

    private void GetGeneralSettings()
    {
        /*
        if (Configuration.AutoManageVnavAlignCamera && VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_GetAlignCamera())
            _settingsActive |= SettingsActive.Vnav_Align_Camera_Off;
        */
        if (YesAlready_IPCSubscriber.IsEnabled is true and true) this._settingsActive |= SettingsActive.YesAlready;

        if (PandorasBox_IPCSubscriber.IsEnabled && PandorasBox_IPCSubscriber.GetFeatureEnabled("Auto-interact with Objects in Instances")) this._settingsActive |= SettingsActive.Pandora_Interact_Objects;

        Svc.Log.Debug($"General Settings Active: {this._settingsActive}");
    }

    internal void SetGeneralSettings(bool on)
    {
        if (!on) this.GetGeneralSettings();

        if (this.Configuration.AutoManageVnavAlignCamera && this._settingsActive.HasFlag(SettingsActive.Vnav_Align_Camera_Off))
        {
            Svc.Log.Debug($"Setting VnavAlignCamera: {on}");
            VNavmesh_IPCSubscriber.Path_SetAlignCamera(on);
        }
        if (PandorasBox_IPCSubscriber.IsEnabled && this._settingsActive.HasFlag(SettingsActive.Pandora_Interact_Objects))
        {
            Svc.Log.Debug($"Setting PandorasBos Auto-interact with Objects in Instances: {on}");
            PandorasBox_IPCSubscriber.SetFeatureEnabled("Auto-interact with Objects in Instances", on);
        }
        if (YesAlready_IPCSubscriber.IsEnabled && this._settingsActive.HasFlag(SettingsActive.YesAlready))
        {
            Svc.Log.Debug($"Setting YesAlready Enabled: {on}");
            YesAlready_IPCSubscriber.SetState(on);
        }
    }

    internal void SetRotationPluginSettings(bool on, bool ignoreConfig = false, bool ignoreTimer = false)
    {
        // Only try to set the rotation state every few seconds
        if (on && (DateTime.Now - this._lastRotationSetTime).TotalSeconds < 5 && !ignoreTimer)
            return;
        
        if(on) 
            this._lastRotationSetTime = DateTime.Now;

        if (!ignoreConfig && !this.Configuration.AutoManageRotationPluginState)
            return;

        bool? EnableWrath(bool active)
        {
            if (Wrath_IPCSubscriber.IsEnabled)
            {
                bool wrathRotationReady = true;
                if (active)
                    wrathRotationReady = Wrath_IPCSubscriber.IsCurrentJobAutoRotationReady() ||
                                         ConfigurationMain.Instance.GetCurrentConfig.Wrath_AutoSetupJobs && Wrath_IPCSubscriber.SetJobAutoReady();

                if (!active || wrathRotationReady)
                {
                    Svc.Log.Debug("Wrath rotation:" + active);
                    Wrath_IPCSubscriber.SetAutoMode(active);

                    return true;
                }
                return false;
            }
            return null;
        }

        bool? EnableRSR(bool active)
        {
            if (RSR_IPCSubscriber.IsEnabled)
            {
                Svc.Log.Debug("RSR: " + active);
                if (active)
                    RSR_IPCSubscriber.RotationAuto();
                else
                    RSR_IPCSubscriber.RotationStop();
                return true;
            }
            return null;
        }

        bool? EnableBM(bool active, bool rotation)
        {
            if (BossMod_IPCSubscriber.IsEnabled)
            {
                if (active)
                {
                    BossMod_IPCSubscriber.SetRange(Plugin.Configuration.MaxDistanceToTargetFloat);
                    if (rotation)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty", Resources.AutoDutyPreset);
                    else if (ConfigurationMain.Instance.GetCurrentConfig.AutoManageBossModAISettings)
                        BossMod_IPCSubscriber.SetPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    return true;
                }
                else if (!rotation || ConfigurationMain.Instance.GetCurrentConfig.AutoManageBossModAISettings)
                {
                    BossMod_IPCSubscriber.DisablePresets();
                    return true;
                }
                return false;
            }
            return null;
        }

        bool act = on;

        bool wrathEnabled = this.Configuration.rotationPlugin is RotationPlugin.WrathCombo or RotationPlugin.All;
        bool? wrath        = EnableWrath(on && wrathEnabled);
        if (on && wrathEnabled && wrath.HasValue)
            act = !wrath.Value;
        
        bool rsrEnabled = this.Configuration.rotationPlugin is RotationPlugin.RotationSolverReborn or RotationPlugin.All;
        bool? rsr        = EnableRSR(act && on && rsrEnabled);
        if (on && rsrEnabled && rsr.HasValue) 
            act = !rsr.Value;

        EnableBM(on, act && this.Configuration.rotationPlugin is RotationPlugin.BossMod or RotationPlugin.All);
    }

    internal void SetBMSettings(bool defaults = false)
    {
        this.BMRoleChecks();

        if (defaults)
        {
            this.Configuration.MaxDistanceToTargetRoleBased = true;
            this.Configuration.PositionalRoleBased             = true;
        }

        BossMod_IPCSubscriber.SetMovement(true);
        BossMod_IPCSubscriber.SetRange(Plugin.Configuration.MaxDistanceToTargetFloat);
    }

    internal void BMRoleChecks()
    {
        //RoleBased Positional
        if (PlayerHelper.IsValid && this.Configuration.PositionalRoleBased && this.Configuration.PositionalEnum != (Player.Object.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any))
        {
            this.Configuration.PositionalEnum = (Player.Object.ClassJob.Value.GetJobRole() == JobRole.Melee ? Positional.Rear : Positional.Any);
            this.Configuration.Save();
        }

        ClassJob classJob = Player.Object.ClassJob.Value!;

        //RoleBased MaxDistanceToTarget
        float maxDistanceToTarget = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank ? 
                                         Plugin.Configuration.MaxDistanceToTargetRoleMelee : Plugin.Configuration.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && this.Configuration.MaxDistanceToTargetRoleBased && Math.Abs(this.Configuration.MaxDistanceToTargetFloat - maxDistanceToTarget) > 0.01f)
        {
            this.Configuration.MaxDistanceToTargetFloat = maxDistanceToTarget;
            this.Configuration.Save();
        }

        //RoleBased MaxDistanceToTargetAoE

        float maxDistanceToTargetAoE = (classJob.GetJobRole() is JobRole.Melee or JobRole.Tank or JobRole.Ranged_Physical || (classJob.GetJobRole() == JobRole.Healer && classJob.RowId != (uint) ClassJobType.Astrologian) ?
                                            Plugin.Configuration.MaxDistanceToTargetRoleMelee : Plugin.Configuration.MaxDistanceToTargetRoleRanged);
        if (PlayerHelper.IsValid && this.Configuration.MaxDistanceToTargetRoleBased && Math.Abs(this.Configuration.MaxDistanceToTargetAoEFloat - maxDistanceToTargetAoE) > 0.01f)
        {
            this.Configuration.MaxDistanceToTargetAoEFloat = maxDistanceToTargetAoE;
            this.Configuration.Save();
        }
    }

    private unsafe void ActionInvoke()
    {
        if (this.PathAction == null) 
            return;

        if (!this.TaskManager.IsBusy && !this.PathAction.Name.IsNullOrEmpty())
        {
            this._actions.InvokeAction(this.PathAction);
            this.PathAction = new PathAction();
        }
    }

    private void GetJobAndLevelingCheck()
    {
        Job curJob = Player.Object.GetJob();
        if (curJob != this.JobLastKnown)
            if (this.LevelingEnabled)
            {
                Svc.Log.Info($"{(this.Configuration.DutyModeEnum == DutyMode.Support || this.Configuration.DutyModeEnum == DutyMode.Trust) && (this.Configuration.DutyModeEnum == DutyMode.Support || this.SupportLevelingEnabled) && (this.Configuration.DutyModeEnum != DutyMode.Trust || this.TrustLevelingEnabled)} ({this.Configuration.DutyModeEnum == DutyMode.Support} || {this.Configuration.DutyModeEnum == DutyMode.Trust}) && ({this.Configuration.DutyModeEnum == DutyMode.Support} || {this.SupportLevelingEnabled}) && ({this.Configuration.DutyModeEnum != DutyMode.Trust} || {this.TrustLevelingEnabled})");
                Content? duty = LevelingHelper.SelectHighestLevelingRelevantDuty(this.LevelingModeEnum);
                if (duty != null)
                {
                    Plugin.CurrentTerritoryContent = duty;
                    this.MainListClicked           = true;
                    ContentPathsManager.DictionaryPaths[Plugin.CurrentTerritoryContent.TerritoryType].SelectPath(out this.CurrentPath);
                }
                else
                {
                    Plugin.CurrentTerritoryContent = null;
                    this.CurrentPath               = -1;
                }
            }

        this.JobLastKnown = curJob;
    }

    private void CheckRetainerWindow()
    {
        if (AutoRetainerHelper.State == ActionState.Running || AutoRetainer_IPCSubscriber.IsBusy() || AM_IPCSubscriber.IsRunning() || this.Stage == Stage.Paused)
            return;

        if (Svc.Condition[ConditionFlag.OccupiedSummoningBell])
            AutoRetainerHelper.Instance.CloseAddons();
    }

    private void InteractablesCheck()
    {
        if (this.Interactables.Count == 0) return;

        IEnumerable<IGameObject> list = Svc.Objects.Where(x => this.Interactables.Contains(x.DataId));

        if (!list.Any()) return;

        int index = this.Actions.Select((value, index) => (Value: value, Index: index))
                        .First(x => this.Interactables.Contains(x.Value.Arguments.Any(y => y.Any(z => z == ' ')) ? uint.Parse(x.Value.Arguments[0].Split(" ")[0]) : uint.Parse(x.Value.Arguments[0]))).Index;

        if (index > this.Indexer)
        {
            this.Indexer = index;
            this.Stage      = Stage.Reading_Path;
        }
    }

    private void PreStageChecks()
    {
        if (this.Stage == Stage.Stopped)
            return;

        this.CheckRetainerWindow();

        this.InteractablesCheck();

        if (EzThrottler.Throttle("OverrideAFK") && this.States.HasFlag(PluginState.Navigating) && PlayerHelper.IsValid) this._overrideAFK.ResetTimers();

        if (!Player.Available) 
            return;

        if (!this.InDungeon && this.CurrentTerritoryContent != null) this.GetJobAndLevelingCheck();

        if (!PlayerHelper.IsValid || !BossMod_IPCSubscriber.IsEnabled || !VNavmesh_IPCSubscriber.IsEnabled) 
            return;

        if (!RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !this.Configuration.UsingAlternativeRotationPlugin) 
            return;

        if (this.CurrentTerritoryType == 0 && Svc.ClientState.TerritoryType != 0 && this.InDungeon) this.ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);

        if (this.States.HasFlag(PluginState.Navigating) && this.Configuration.LootTreasure && (!this.Configuration.LootBossTreasureOnly || (this.PathAction?.Name == "Boss" && this.Stage == Stage.Action)) &&
            (this.treasureCofferGameObject = ObjectHelper.GetObjectsByObjectKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure)
                                                        ?.FirstOrDefault(x => ObjectHelper.GetDistanceToPlayer(x) < 2)) != null)
        {
            BossMod_IPCSubscriber.SetRange(30f);
            ObjectHelper.InteractWithObject(this.treasureCofferGameObject, false);
        }

        if (this.Indexer >= this.Actions.Count && this.Actions.Count > 0 && this.States.HasFlag(PluginState.Navigating)) this.DoneNavigating();

        if (this.Stage > Stage.Condition && !this.States.HasFlag(PluginState.Other)) this.Action = this.Stage.ToCustomString();
    }

    public void Framework_Update(IFramework framework)
    {
        this.PreStageChecks();

        this.Framework_Update_InDuty(framework);

        switch (this.Stage)
        {
            case Stage.Reading_Path:
                this.StageReadingPath();
                break;
            case Stage.Moving:
                this.StageMoving();
                break;
            case Stage.Action:
                this.StageAction();
                break;
            case Stage.Waiting_For_Combat:
                this.StageWaitingForCombat();
                break;
            case Stage.Stopped:
            case Stage.Looping:
            case Stage.Condition:
            case Stage.Paused:
            case Stage.Dead:
            case Stage.Revived:
            case Stage.Interactable:
            case Stage.Idle:
            default:
                break;
        }
    }

    public event IFramework.OnUpdateDelegate Framework_Update_InDuty = _ => {};

    private void StopAndResetALL()
    {
        if (this._bareModeSettingsActive != SettingsActive.None)
        {
            this.Configuration.EnablePreLoopActions     = this._bareModeSettingsActive.HasFlag(SettingsActive.PreLoop_Enabled);
            this.Configuration.EnableBetweenLoopActions = this._bareModeSettingsActive.HasFlag(SettingsActive.BetweenLoop_Enabled);
            this.Configuration.EnableTerminationActions = this._bareModeSettingsActive.HasFlag(SettingsActive.TerminationActions_Enabled);
            this._bareModeSettingsActive                   = SettingsActive.None;
        }

        this.States = PluginState.None;
        this.TaskManager?.SetStepMode(false);
        this.TaskManager?.Abort();
        this.MainListClicked              = false;
        this.Framework_Update_InDuty = _ => {};
        if (!this.InDungeon) this.CurrentLoop = 0;
        if (this.Configuration.AutoManageBossModAISettings) 
            BossMod_IPCSubscriber.DisablePresets();

        this.SetGeneralSettings(true);
        if (this.Configuration is { AutoManageRotationPluginState: true, UsingAlternativeRotationPlugin: false }) 
            this.SetRotationPluginSettings(false);
        if (this.Indexer > 0 && !this.MainListClicked)
            this.Indexer = -1;
        if (this.Configuration is { ShowOverlay: true, HideOverlayWhenStopped: true })
            this.Overlay.IsOpen = false;
        if (VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_GetTolerance() > 0.25F)
            VNavmesh_IPCSubscriber.Path_SetTolerance(0.25f);
        FollowHelper.SetFollow(null);

        if (VNavmesh_IPCSubscriber.IsEnabled && VNavmesh_IPCSubscriber.Path_IsRunning())
            VNavmesh_IPCSubscriber.Path_Stop();

        if (MapHelper.State == ActionState.Running)
            MapHelper.StopMoveToMapMarker();

        if (DeathHelper.DeathState == PlayerLifeState.Revived)
            DeathHelper.Stop();

        foreach (IActiveHelper helper in ActiveHelper.activeHelpers) 
            helper.StopIfRunning();

        Wrath_IPCSubscriber.Release();
        this.Action = "";
    }

    public void Dispose()
    {
        GitHubHelper.Dispose();
        this.StopAndResetALL();
        ConfigurationMain.Instance.MultiBox =  false;
        Svc.Framework.Update                -= this.Framework_Update;
        Svc.Framework.Update                -= SchedulerHelper.ScheduleInvoker;
        FileHelper.FileSystemWatcher.Dispose();
        FileHelper.FileWatcher.Dispose();
        this.WindowSystem.RemoveAllWindows();
        ECommonsMain.Dispose();
        this.MainWindow.Dispose();
        this.OverrideCamera.Dispose();
        Svc.ClientState.TerritoryChanged -= this.ClientState_TerritoryChanged;
        Svc.Condition.ConditionChange    -= this.Condition_ConditionChange;
        PictoService.Dispose();
        PluginInterface.UiBuilder.Draw   -= this.UiBuilderOnDraw;
        Svc.Commands.RemoveHandler(CommandName);
    }

    private void DrawUI() => this.WindowSystem.Draw();

    public void OpenConfigUI()
    {
        if (this.MainWindow != null)
        {
            this.MainWindow.IsOpen = true;
            MainWindow.OpenTab("Config");
        }
    }

    public void OpenMainUI()
    {
        if (this.MainWindow != null) 
            this.MainWindow.IsOpen = true;
    }
}
