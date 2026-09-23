namespace AutoDuty.Configurations;

using System;
using System.Collections.Generic;
using System.Numerics;
using Windows;
using Dalamud.Bindings.ImGui;
using Data;
using ECommons;
using ECommons.Configuration;
using ECommons.ExcelServices;
using ECommons.IPC.Subscribers.AutoRetainer;
using ECommons.IPC.Subscribers.RotationSolverReborn;
using Helpers;
using Multibox;
using Serilog.Events;

[Serializable]
[Obsolete("Use ConfigurationProfileV2 instead")]
public class Configuration
{
    //Meta
    public HashSet<string>                                    DoNotUpdatePathFiles = [];
    public Dictionary<uint, Dictionary<string, JobWithRole>?> PathSelectionsByPath = [];

    //LogOptions
    public bool          AutoScroll    = true;
    public LogEventLevel LogEventLevel = LogEventLevel.Debug;

    //General Options
    internal AutoDutyMode autoDutyModeEnum = AutoDutyMode.Looping;
    public AutoDutyMode AutoDutyModeEnum
    {
        get => this.autoDutyModeEnum;
        set
        {
            this.autoDutyModeEnum          = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected           = null;
            Plugin.LevelingModeEnum        = LevelingMode.None;
        }
    }


    public   int      LoopTimes    = 1;
    internal DutyMode dutyModeEnum = DutyMode.Support;
    public DutyMode DutyModeEnum
    {
        get => this.AutoDutyModeEnum switch
        {
            AutoDutyMode.Playlist => Plugin.PlaylistCurrentEntry?.DutyMode ?? this.dutyModeEnum,
            AutoDutyMode.Looping or _ => this.dutyModeEnum
        };
        set
        {
            this.dutyModeEnum              = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected           = null;
            Plugin.LevelingModeEnum        = LevelingMode.None;
        }
    }


    
    public bool Unsynced                       = false;
    public bool HideUnavailableDuties          = false;
    public bool PreferTrustOverSupportLeveling = false;
    public bool SquadronAssignLowestMembers    = true;

    public bool ShowMainWindowOnStartup = false;

    
#region OverlayConfig
    internal bool showOverlay = true;
    public bool ShowOverlay
    {
        get => this.showOverlay;
        set
        {
            this.showOverlay       = value;
            Plugin.Overlay?.IsOpen = value;
        }
    }
    internal bool hideOverlayWhenStopped = false;
    public bool HideOverlayWhenStopped
    {
        get => this.hideOverlayWhenStopped;
        set 
        {
            this.hideOverlayWhenStopped = value;
            if (Plugin.Overlay != null) 
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.States.HasAnyFlag(PluginState.Looping, PluginState.Navigating), () => Plugin.Overlay != null);
        }
    }
    internal bool lockOverlay = false;
    public bool LockOverlay
    {
        get => this.lockOverlay;
        set 
        {
            this.lockOverlay = value;
            if (value)
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                    {
                                                                        if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                            Plugin.Overlay.Flags |= ImGuiWindowFlags.NoMove;
                                                                    }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () =>
                                                                    {
                                                                        if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove))
                                                                            Plugin.Overlay.Flags -= ImGuiWindowFlags.NoMove;
                                                                    }, () => Plugin.Overlay != null);
        }
    }
    internal bool overlayNoBG = false;
    public bool OverlayNoBG
    {
        get => this.overlayNoBG;
        set
        {
            this.overlayNoBG = value;
            if (value)
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                    {
                                                                        if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) 
                                                                            Plugin.Overlay.Flags |= ImGuiWindowFlags.NoBackground;
                                                                    }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () =>
                                                                    {
                                                                        if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) 
                                                                            Plugin.Overlay.Flags -= ImGuiWindowFlags.NoBackground;
                                                                    }, () => Plugin.Overlay != null);
        }
    }

    public bool OverlayAnchorBottom    = false;
    public bool ShowDutyLoopText       = true;
    public bool ShowActionText         = true;
    public bool UseSliderInputs        = false;
    public bool OverrideOverlayButtons = true;
    public bool GotoButton             = true;
    public bool TurninButton           = true;
    public bool DesynthButton          = true;
    public bool ExtractButton          = true;
    public bool RepairButton           = true;
    public bool EquipButton            = true;
    public bool CofferButton           = true;
    public bool TTButton               = true;
#endregion

#region DutyConfig
    //Duty Config Options
    public bool           AutoExitDuty                  = true;
    public bool           OnlyExitWhenDutyDone          = false;
    public bool           AutoManageRotationPluginState = true;
    public RotationPlugin rotationPlugin                = RotationPlugin.All;

#region Wrath
    public bool                                Wrath_AutoSetupJobs { get; set; } = true;
    public WrathCombo.API.Enum.DPSRotationMode Wrath_TargetingTank    = WrathCombo.API.Enum.DPSRotationMode.Highest_Max;
    public WrathCombo.API.Enum.DPSRotationMode Wrath_TargetingNonTank = WrathCombo.API.Enum.DPSRotationMode.Lowest_Current;
#endregion

#region RSR

    public RotationSolverRebornIPC.TargetHostileType RSR_TargetHostileType    = RotationSolverRebornIPC.TargetHostileType.AllTargetsCanAttack;
    public RotationSolverRebornIPC.TargetingType     RSR_TargetingTypeTank    = RotationSolverRebornIPC.TargetingType.HighHP;
    public RotationSolverRebornIPC.TargetingType     RSR_TargetingTypeNonTank = RotationSolverRebornIPC.TargetingType.LowHP;
#endregion



    internal bool autoManageBossModAISettings   = true;
    public bool AutoManageBossModAISettings
    {
        get => this.autoManageBossModAISettings;
        set
        {
            this.autoManageBossModAISettings = value;
            this.HideBossModAIConfig         = !value;
        }
    }

#region BossMod
    public bool HideBossModAIConfig           = false;
    public bool BM_UpdatePresetsAutomatically = true;


    internal bool maxDistanceToTargetRoleBased = true;
    public bool MaxDistanceToTargetRoleBased
    {
        get => this.maxDistanceToTargetRoleBased;
        set
        {
            this.maxDistanceToTargetRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("MaxDistanceToTargetRoleBasedBMRoleChecks", () => BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetFloat    = 2.6f;
    public float MaxDistanceToTargetAoEFloat = 12;

    internal bool positionalRoleBased = true;
    public bool PositionalRoleBased
    {
        get => this.positionalRoleBased;
        set
        {
            this.positionalRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("PositionalRoleBasedBMRoleChecks", () => BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetRoleMelee  = 2.6f;
    public float MaxDistanceToTargetRoleRanged = 10f;
#endregion

    internal bool       positionalAvarice = true;
    public   Positional PositionalEnum    = Positional.Any;

    public bool       AutoManageVnavAlignCamera      = true;
    public bool       LootTreasure                   = true;
    public LootMethod LootMethodEnum                 = LootMethod.AutoDuty;
    public bool       LootBossTreasureOnly           = false;
    public int        TreasureCofferScanDistance     = 25;
    public bool       RebuildNavmeshOnStuck          = true;
    public byte       RebuildNavmeshAfterStuckXTimes = 5;
    public int        MinStuckTime                   = 500;
    public bool       StuckOnStep                    = true;
    public bool       stuckReturn                    = true;
    public int        StuckReturnX                   = 10;
    public bool StuckReturn
    {
        get => this.stuckReturn && !MultiboxUtility.Config.MultiBox;
        set => this.stuckReturn = value;
    }

    public bool PathDrawEnabled   = false;
    public int  PathDrawStepCount = 5;

    public bool DisableRenderWhileActive = false;

    public bool OverridePartyValidation        = false;
    public bool UsingAlternativeRotationPlugin = false;
    public bool UsingAlternativeMovementPlugin = false;
    public bool UsingAlternativeBossPlugin     = false;

    public bool        TreatUnsyncAsW2W = true;
    public JobWithRole W2WJobs          = JobWithRole.Tanks;

    public bool IsW2W(Job? job = null, bool? unsync = null)
    {
        job ??= PlayerHelper.GetJob();

        if (this.W2WJobs.HasJob(job.Value))
            return true;

        unsync ??= this.Unsynced && this.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

        return MultiboxUtility.Config.MultiBox || unsync.Value && this.TreatUnsyncAsW2W;
    }

    public bool LevelingListExperimentalEntries
    {
        get;
        set
        {
            if(field != value)
                LevelingHelper.ResetLevelingDuties();
            field = value;
        }
    } = false;

#endregion

#region PreLoop
    public bool                                                                    EnablePreLoopActions     = true;
    public bool                                                                    ExecuteCommandsPreLoop   = false;
    public List<string>                                                            CustomCommandsPreLoop    = [];
    public bool                                                                    RetireMode               = false;
    public RetireLocation                                                          RetireLocationEnum       = RetireLocation.Inn;
    public List<Vector3>                                                           PersonalHomeEntrancePath = [];
    public List<Vector3>                                                           FCEstateEntrancePath     = [];
    public bool                                                                    AutoEquipRecommendedGear;
    public GearsetUpdateSource                                                     AutoEquipRecommendedGearSource;
    public bool                                                                    AutoEquipRecommendedGearGearsetterOldToInventory;
    public bool                                                                    AutoRepair              = false;
    public uint                                                                    AutoRepairPct           = 50;
    public bool                                                                    AutoRepairSelf          = false;
    public RepairNPCHelper.RepairNpcData?                                          PreferredRepairNPC      = null;
    public bool                                                                    AutoConsume             = false;
    public bool                                                                    AutoConsumeIgnoreStatus = false;
    public int                                                                     AutoConsumeTime         = 29;
    public List<KeyValuePair<ushort, ConsumeItemsLoopActionConfig.ConsumableItem>> AutoConsumeItemsList    = [];
#endregion


#region BetweenLoop
    public bool         EnableBetweenLoopActions         = true;
    public bool         ExecuteBetweenLoopActionLastLoop = false;
    public int          WaitTimeBeforeAfterLoopActions   = 0;
    public bool         ExecuteCommandsBetweenLoop       = false;
    public List<string> CustomCommandsBetweenLoop        = [];
    public bool         AutoExtract                      = false;

    public bool                     AutoOpenCoffers = false;
    public byte?                    AutoOpenCoffersGearset;
    public bool                     AutoOpenCoffersBlacklistUse;
    public Dictionary<uint, string> AutoOpenCoffersBlacklist = [];

    internal bool autoExtractAll = false;
    public bool AutoExtractAll
    {
        get => this.autoExtractAll;
        set => this.autoExtractAll = value;
    }
    internal bool autoDesynth = false;
    public bool AutoDesynth
    {
        get => this.autoDesynth;
        set
        {
            this.autoDesynth = value;
            if (value && !this.AutoDesynthSkillUp)
                this.AutoGCTurnin = false;
        }
    }
    internal bool autoDesynthSkillUp = false;
    public bool AutoDesynthSkillUp
    {
        get => this.autoDesynthSkillUp;
        set
        {
            this.autoDesynthSkillUp = value;
            if (!value && this.AutoGCTurnin)
                this.AutoDesynth = false;
        }
    }
    public int   AutoDesynthSkillUpLimit = 50;
    public bool  AutoDesynthNQOnly       = false;
    public bool  AutoDesynthNoGearset    = true;
    public ulong AutoDesynthCategories   = 0x1;

    internal bool autoGCTurnin            = false;
    public bool AutoGCTurnin
    {
        get => this.autoGCTurnin;
        set
        {
            this.autoGCTurnin = value;
            if (value && !this.AutoDesynthSkillUp)
                this.AutoDesynth = false;
        }
    }

    public int  AutoGCTurninSlotsLeft     = 5;
    public bool AutoGCTurninSlotsLeftBool = false;
    public bool AutoGCTurninUseTicket     = false;

    public bool ArmoireEntrust      = false;
    public bool GlamourChestEntrust = false;

    public bool TripleTriadRegister;
    public bool TripleTriadSell;
    public int  TripleTriadSellMinItemCount = 1;
    public int  TripleTriadSellMinSlotCount = 1;

    public bool DiscardItems;

    public bool                   EnableAutoRetainer         = false;
    public SummoningBellLocations PreferredSummoningBellEnum = 0;
    public long                   AutoRetainer_RemainingTime = 0L;

    public bool          EnableAutoRetainerMultiMode = false;
    public MultiModeType AutoRetainerMultiModeType   = MultiModeType.Everything;
    
#endregion

#region Termination
    public bool                                        EnableTerminationActions      = true;
    public bool                                        StopLevel                     = false;
    public int                                         StopLevelInt                  = 1;
    public bool                                        StopNoRestedXP                = false;
    public bool                                        StopItemQty                   = false;
    public bool                                        StopItemAll                   = false;
    public Dictionary<uint, KeyValuePair<string, int>> StopItemQtyItemDictionary     = [];
    public int                                         StopItemQtyInt                = 1;
    public bool                                        StopWhenDutyGathered          = false;
    public bool                                        TerminationBLUSpellsEnabled   = false;
    public List<uint>                                  TerminationBLUSpells          = [];
    public bool                                        TerminationBLUSpellsAll       = false;
    public bool                                        TerminationInventoryFree      = false;
    public int                                         TerminationInventoryFreeSlots = 0;
    public bool                                        TerminationiLvl               = false;
    public int                                         TerminationiLvlInt            = 0;

    public bool            ExecuteCommandsTermination = false;
    public List<string>    CustomCommandsTermination  = [];
    public bool            PlayEndSound               = false;
    public bool            CustomSound                = false;
    public float           CustomSoundVolume          = 0.5f;
    public Sounds          SoundEnum                  = Sounds.None;
    public string          SoundPath                  = "";
    public TerminationMode TerminationMethodEnum      = TerminationMode.Do_Nothing;
    public bool            TerminationKeepActive      = true;
#endregion

    public TrustMemberName?[] SelectedTrustMembers = new TrustMemberName?[3];


    public static void Save()
    {
        if (!ConfigOverrideHelper.HasOverrides)
            EzConfig.Save();
    }
}