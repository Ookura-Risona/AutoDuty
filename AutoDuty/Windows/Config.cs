using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using FFXIVClientStructs.FFXIV.Client.UI;
using ImGuiNET;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static AutoDuty.Helpers.RepairNPCHelper;
using static AutoDuty.Windows.ConfigTab;

namespace AutoDuty.Windows;

using Data;
using ECommons.Configuration;
using ECommons.ExcelServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using Properties;
using System.IO;
using System.Numerics;
using System.Text;
using ReflectionHelper = Helpers.ReflectionHelper;
using Vector2 = FFXIVClientStructs.FFXIV.Common.Math.Vector2;

[JsonObject(MemberSerialization.OptIn)]
public class ConfigurationMain : IEzConfig
{
    public const string CONFIGNAME_BARE = "Bare";

    public static ConfigurationMain Instance;

    [JsonProperty]
    public string DefaultConfigName = CONFIGNAME_BARE;

    [JsonProperty]
    private string activeProfileName = CONFIGNAME_BARE;
    
    public  string ActiveProfileName => this.activeProfileName;

    [JsonProperty]
    private readonly HashSet<ProfileData> profileData = [];

    private readonly Dictionary<string, ProfileData> profileByName = [];
    private readonly Dictionary<ulong, string> profileByCID = [];

    [JsonProperty]
    public readonly Dictionary<ulong, CharData> charByCID = [];

    [JsonObject(MemberSerialization.OptOut)]
    public struct CharData
    {
        public required ulong  CID;
        public          string Name;
        public          string World;

        public string GetName() => this.Name.Any() ? $"{this.Name}@{this.World}" : CID.ToString();

        public override int GetHashCode() => this.CID.GetHashCode();
    }

    [JsonProperty]
    //Dev Options
    internal bool updatePathsOnStartup = true;
    public bool UpdatePathsOnStartup
    {
        get => !Plugin.isDev || this.updatePathsOnStartup;
        set => this.updatePathsOnStartup = value;
    }


    public IEnumerable<string> ConfigNames => this.profileByName.Keys;
     
    public ProfileData GetCurrentProfile
    {
        get
        {
            if (!this.profileByName.TryGetValue(this.ActiveProfileName, out ProfileData? profiles))
            {
                this.SetProfileToDefault();
                return this.GetCurrentProfile;
            }

            return profiles;
        }
    }

    public Configuration GetCurrentConfig => this.GetCurrentProfile.Config;

    public void Init()
    {
        if (this.profileData.Count == 0)
        {
            if (Svc.PluginInterface.ConfigFile.Exists)
            {
                Configuration? configuration = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName, Encoding.UTF8));
                if (configuration != null)
                {
                    this.CreateProfile("Migrated", configuration);
                    this.SetProfileAsDefault();
                }
            }
        }

        void RegisterProfileData(ProfileData profile)
        {
            if (profile.CIDs.Any())
                foreach (ulong cid in profile.CIDs)
                    this.profileByCID[cid] = profile.Name;
            this.profileByName[profile.Name] = profile;

            if(profile.Config.LootMethodEnum == LootMethod.RotationSolver) //RSR removed
                profile.Config.LootMethodEnum = LootMethod.All;
        }

        foreach (ProfileData profile in this.profileData)
            if(profile.Name != CONFIGNAME_BARE)
                RegisterProfileData(profile);

        RegisterProfileData(new ProfileData
                            {
                                Name = CONFIGNAME_BARE,
                                Config = new Configuration
                                         {
                                             EnablePreLoopActions     = false,
                                             EnableBetweenLoopActions = false,
                                             EnableTerminationActions = false,
                                             LootTreasure             = false
                                         }
                            });

        this.SetProfileToDefault();
    }

    public bool SetProfile(string name)
    {
        DebugLog("Changing profile to: " + name);
        if (this.profileByName.ContainsKey(name))
        {
            this.activeProfileName = name;
            EzConfig.Save();
            return true;
        }
        return false;
    }

    public void SetProfileAsDefault()
    {
        if (this.profileByName.ContainsKey(this.ActiveProfileName))
        {
            this.DefaultConfigName = this.ActiveProfileName;
            EzConfig.Save();
        }
    }

    public void SetProfileToDefault()
    {
        this.SetProfile(CONFIGNAME_BARE);
        Svc.Framework.RunOnTick(() =>
        {
            DebugLog($"Setting to default profile for {Player.Name} ({Player.CID}) {PlayerHelper.IsValid}");

            if (Player.Available && this.profileByCID.TryGetValue(Player.CID, out string? charProfile))
                if (this.SetProfile(charProfile))
                    return;
            DebugLog("No char default found. Using general default");
            if (!this.SetProfile(this.DefaultConfigName))
            {
                DebugLog("Fallback, using bare");
                this.DefaultConfigName = CONFIGNAME_BARE;
                this.SetProfile(CONFIGNAME_BARE);
            }
        });
    }

    public void CreateNewProfile() => 
        this.CreateProfile("Profile" + (this.profileByName.Count - 1).ToString(CultureInfo.InvariantCulture));

    public void CreateProfile(string name) => 
        this.CreateProfile(name, new Configuration());

    public void CreateProfile(string name, Configuration config)
    {
        DebugLog($"Creating new Profile: {name}");

        ProfileData profile = new()
                           {
                               Name   = name,
                               Config = config
                           };

        this.profileData.Add(profile);
        this.profileByName.Add(name, profile);
        this.SetProfile(name);
    }

    public void DuplicateCurrentProfile()
    {
        string name;
        int    counter = 0;

        string templateName = this.ActiveProfileName.EndsWith("_Copy") ? this.ActiveProfileName : $"{this.ActiveProfileName}_Copy";

        do
            name = counter++ > 0 ? $"{templateName}{counter}" : templateName;
        while (this.profileByName.ContainsKey(name));

        string?        oldConfig = EzConfig.DefaultSerializationFactory.Serialize(this.GetCurrentConfig);
        if(oldConfig != null)
        {
            Configuration? newConfig = EzConfig.DefaultSerializationFactory.Deserialize<Configuration>(oldConfig);
            if(newConfig != null)
                this.CreateProfile(name, newConfig);
        }
    }

    public void RemoveCurrentProfile()
    {
        DebugLog("Removing " + this.ActiveProfileName);
        this.profileData.Remove(this.GetCurrentProfile);
        this.profileByName.Remove(this.ActiveProfileName);
        this.SetProfileToDefault();
    }

    public bool RenameCurrentProfile(string newName)
    {
        if (this.profileByName.ContainsKey(newName))
            return false;

        ProfileData config = this.GetCurrentProfile;
        this.profileByName.Remove(this.ActiveProfileName);
        this.profileByName[newName] = config;
        config.Name                 = newName;
        this.activeProfileName      = newName;

        EzConfig.Save();

        return true;
    }

    public ProfileData? GetProfile(string name) => 
        this.profileByName.GetValueOrDefault(name);

    public void SetCharacterDefault()
    {
        Svc.Framework.RunOnTick(() =>
                          {

                              if (!PlayerHelper.IsValid)
                                  return;

                              ulong cid = Player.CID;

                              if (this.profileByCID.TryGetValue(cid, out string? oldProfile))
                                  this.profileByName[oldProfile].CIDs.Remove(cid);

                              this.GetCurrentProfile.CIDs.Add(cid);
                              this.profileByCID.Add(cid, this.ActiveProfileName);
                              this.charByCID[cid] = new CharData
                                                    {
                                                        CID  = cid,
                                                        Name = Player.Name,
                                                        World = Player.CurrentWorld
                              };

                              EzConfig.Save();
                          });
    }

    public void RemoveCharacterDefault()
    {
        Svc.Framework.RunOnTick(() =>
                                {
                                    if (!PlayerHelper.IsValid)
                                        return;

                                    ulong cid = Player.CID;

                                    this.profileByName[this.ActiveProfileName].CIDs.Remove(cid);
                                    this.profileByCID.Remove(cid);

                                    EzConfig.Save();
                                });
    }

    public static void DebugLog(string message)
    {
        Svc.Log.Debug($"Configuration Main: {message}");
    }
}

[JsonObject(MemberSerialization.OptOut)]
public class ProfileData
{
    public required string         Name;
    public          HashSet<ulong> CIDs = [];
    public required Configuration  Config;
}

public class AutoDutySerializationFactory : DefaultSerializationFactory, ISerializationFactory
{
    public override string DefaultConfigFileName { get; } = "AutoDutyConfig.json";

    public new string Serialize(object config) => 
        base.Serialize(config, true);

    public override byte[] SerializeAsBin(object config) => 
        Encoding.UTF8.GetBytes(this.Serialize(config));
}



[Serializable]
public class Configuration
{
    //Meta
    public HashSet<string>                                    DoNotUpdatePathFiles = [];
    public Dictionary<uint, Dictionary<string, JobWithRole>?> PathSelectionsByPath = [];

    //LogOptions
    public bool AutoScroll = true;
    public LogEventLevel LogEventLevel = LogEventLevel.Debug;

    //General Options
    public int LoopTimes = 1;
    internal DutyMode dutyModeEnum = DutyMode.None;
    public DutyMode DutyModeEnum
    {
        get => dutyModeEnum;
        set
        {
            dutyModeEnum = value;
            Plugin.CurrentTerritoryContent = null;
            MainTab.DutySelected = null;
            Plugin.LevelingModeEnum = LevelingMode.None;
        }
    }
    
    public bool Unsynced                       = false;
    public bool HideUnavailableDuties          = false;
    public bool PreferTrustOverSupportLeveling = false;

    public bool ShowMainWindowOnStartup = false;

    //Overlay Config Options
    internal bool showOverlay = true;
    public bool ShowOverlay
    {
        get => showOverlay;
        set
        {
            showOverlay = value;
            if (Plugin.Overlay != null)
                Plugin.Overlay.IsOpen = value;
        }
    }
    internal bool hideOverlayWhenStopped = false;
    public bool HideOverlayWhenStopped
    {
        get => hideOverlayWhenStopped;
        set 
        {
            hideOverlayWhenStopped = value;
            if (Plugin.Overlay != null)
            {
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => Plugin.Overlay.IsOpen = !value || Plugin.States.HasFlag(PluginState.Looping) || Plugin.States.HasFlag(PluginState.Navigating), () => Plugin.Overlay != null);
            }
        }
    }
    internal bool lockOverlay = false;
    public bool LockOverlay
    {
        get => lockOverlay;
        set 
        {
            lockOverlay = value;
            if (value)
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => { if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove)) Plugin.Overlay.Flags |= ImGuiWindowFlags.NoMove; }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("LockOverlaySetter", () => { if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoMove)) Plugin.Overlay.Flags -= ImGuiWindowFlags.NoMove; }, () => Plugin.Overlay != null);
        }
    }
    internal bool overlayNoBG = false;
    public bool OverlayNoBG
    {
        get => overlayNoBG;
        set
        {
            overlayNoBG = value;
            if (value)
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () => { if (!Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) Plugin.Overlay.Flags |= ImGuiWindowFlags.NoBackground; }, () => Plugin.Overlay != null);
            else
                SchedulerHelper.ScheduleAction("OverlayNoBGSetter", () => { if (Plugin.Overlay.Flags.HasFlag(ImGuiWindowFlags.NoBackground)) Plugin.Overlay.Flags -= ImGuiWindowFlags.NoBackground; }, () => Plugin.Overlay != null);
        }
    }
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


    //Duty Config Options
    public   bool AutoExitDuty                  = true;
    public   bool OnlyExitWhenDutyDone          = false;
    public   bool AutoManageRotationPluginState = true;
    internal bool autoManageBossModAISettings   = true;
    public bool AutoManageBossModAISettings
    {
        get => autoManageBossModAISettings;
        set
        {
            autoManageBossModAISettings = value;
            HideBossModAIConfig = !value;
        }
    }
    public bool       AutoManageVnavAlignCamera      = true;
    public bool       LootTreasure                   = true;
    public LootMethod LootMethodEnum                 = LootMethod.AutoDuty;
    public bool       LootBossTreasureOnly           = false;
    public int        TreasureCofferScanDistance     = 25;
    public bool       RebuildNavmeshOnStuck          = true;
    public byte       RebuildNavmeshAfterStuckXTimes = 5;
    public int        MinStuckTime                   = 500;

    public bool PathDrawEnabled   = false;
    public int  PathDrawStepCount = 5;

    public bool       OverridePartyValidation        = false;
    public bool       UsingAlternativeRotationPlugin = false;
    public bool       UsingAlternativeMovementPlugin = false;
    public bool       UsingAlternativeBossPlugin     = false;

    public bool        TreatUnsyncAsW2W = true;
    public JobWithRole W2WJobs          = JobWithRole.Tanks;

    public bool IsW2W(Job? job = null, bool? unsync = null)
    {
        job ??= PlayerHelper.GetJob();

        if (this.W2WJobs.HasJob(job.Value))
            return true;

        unsync ??= this.Unsynced && this.DutyModeEnum.EqualsAny(DutyMode.Raid, DutyMode.Regular, DutyMode.Trial);

        return unsync.Value && this.TreatUnsyncAsW2W;
    }


    //PreLoop Config Options
    public bool                                       EnablePreLoopActions     = true;
    public bool                                       ExecuteCommandsPreLoop   = false;
    public List<string>                               CustomCommandsPreLoop    = [];
    public bool                                       RetireMode               = false;
    public RetireLocation                             RetireLocationEnum       = RetireLocation.Inn;
    public List<System.Numerics.Vector3>              PersonalHomeEntrancePath = [];
    public List<System.Numerics.Vector3>              FCEstateEntrancePath     = [];
    public bool                                       AutoEquipRecommendedGear;
    public bool                                       AutoEquipRecommendedGearGearsetter;
    public bool                                       AutoEquipRecommendedGearGearsetterOldToInventory;
    public bool                                       AutoRepair              = false;
    public uint                                       AutoRepairPct           = 50;
    public bool                                       AutoRepairSelf          = false;
    public RepairNpcData?                             PreferredRepairNPC      = null;
    public bool                                       AutoConsume             = false;
    public bool                                       AutoConsumeIgnoreStatus = false;
    public int                                        AutoConsumeTime         = 29;
    public List<KeyValuePair<ushort, ConsumableItem>> AutoConsumeItemsList    = [];

    //Between Loop Config Options
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
        get => autoExtractAll;
        set => autoExtractAll = value;
    }
    internal bool autoDesynth = false;
    public bool AutoDesynth
    {
        get => autoDesynth;
        set
        {
            autoDesynth = value;
            if (value && !AutoDesynthSkillUp)
                AutoGCTurnin = false;
        }
    }
    internal bool autoDesynthSkillUp = false;
    public bool AutoDesynthSkillUp
    {
        get => autoDesynthSkillUp;
        set
        {
            autoDesynthSkillUp = value;
            if (!value && AutoGCTurnin)
                AutoDesynth = false;
        }
    }
    public int AutoDesynthSkillUpLimit = 50;
    internal bool autoGCTurnin = false;
    public bool AutoGCTurnin
    {
        get => autoGCTurnin;
        set
        {
            autoGCTurnin = value;
            if (value && !AutoDesynthSkillUp)
                AutoDesynth = false;
        }
    }
    public int AutoGCTurninSlotsLeft = 5;
    public bool AutoGCTurninSlotsLeftBool = false;
    public bool AutoGCTurninUseTicket = false;

    public bool TripleTriadEnabled;
    public bool TripleTriadRegister;
    public bool TripleTriadSell;

    public bool DiscardItems;

    public bool EnableAutoRetainer = false;
    public SummoningBellLocations PreferredSummoningBellEnum = 0;
    //Termination Config Options
    public bool EnableTerminationActions = true;
    public bool StopLevel = false;
    public int StopLevelInt = 1;
    public bool StopNoRestedXP = false;
    public bool StopItemQty = false;
    public bool StopItemAll = false;
    public Dictionary<uint, KeyValuePair<string, int>> StopItemQtyItemDictionary = [];
    public int StopItemQtyInt = 1;
    public bool ExecuteCommandsTermination = false;
    public List<string> CustomCommandsTermination = [];
    public bool PlayEndSound = false;
    public bool CustomSound = false;
    public float CustomSoundVolume = 0.5f;
    public Sounds SoundEnum = Sounds.None;
    public string SoundPath = "";
    public TerminationMode TerminationMethodEnum = TerminationMode.Do_Nothing;
    public bool TerminationKeepActive = true;
    
    //BMAI Config Options
    public bool HideBossModAIConfig           = false;
    public bool BM_UpdatePresetsAutomatically = true;


    internal bool maxDistanceToTargetRoleBased = true;
    public bool MaxDistanceToTargetRoleBased
    {
        get => maxDistanceToTargetRoleBased;
        set
        {
            maxDistanceToTargetRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("MaxDistanceToTargetRoleBasedBMRoleChecks", () => Plugin.BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetFloat = 2.6f;
    public float MaxDistanceToTargetAoEFloat = 12;
    
    internal bool positionalRoleBased = true;
    public bool PositionalRoleBased
    {
        get => positionalRoleBased;
        set
        {
            positionalRoleBased = value;
            if (value)
                SchedulerHelper.ScheduleAction("PositionalRoleBasedBMRoleChecks", () => Plugin.BMRoleChecks(), () => PlayerHelper.IsReady);
        }
    }
    public float MaxDistanceToTargetRoleMelee  = 2.6f;
    public float MaxDistanceToTargetRoleRanged = 10f;


    internal bool       positionalAvarice = true;
    public   Positional PositionalEnum    = Positional.Any;

    #region Wrath

    public   bool                                                       Wrath_AutoSetupJobs { get; set; } = true;
    public Wrath_IPCSubscriber.DPSRotationMode    Wrath_TargetingTank    = Wrath_IPCSubscriber.DPSRotationMode.Highest_Max;
    public Wrath_IPCSubscriber.DPSRotationMode    Wrath_TargetingNonTank = Wrath_IPCSubscriber.DPSRotationMode.Lowest_Current;


    #endregion


    public void Save()
    {
        EzConfig.Save();
    }

    public TrustMemberName?[] SelectedTrustMembers = new TrustMemberName?[3];
}

public static class ConfigTab
{
    internal static string FollowName = "";

    private static Configuration Configuration => Plugin.Configuration;
    private static string preLoopCommand = string.Empty;
    private static string betweenLoopCommand = string.Empty;
    private static string terminationCommand = string.Empty;
    private static Dictionary<uint, Item> Items { get; set; } = Svc.Data.GetExcelSheet<Item>()?.Where(x => !x.Name.ToString().IsNullOrEmpty()).ToDictionary(x => x.RowId, x => x) ?? [];
    private static string stopItemQtyItemNameInput = "";
    private static KeyValuePair<uint, string> stopItemQtySelectedItem = new(0, "");

    private static string                     autoOpenCoffersNameInput    = "";
    private static KeyValuePair<uint, string> autoOpenCoffersSelectedItem = new(0, "");

    public class ConsumableItem
    {
        public uint ItemId;
        public string Name = string.Empty;
        public bool CanBeHq;
        public ushort StatusId;
    }

    private static List<ConsumableItem> ConsumableItems { get; set; } = Svc.Data.GetExcelSheet<Item>()?.Where(x => !x.Name.ToString().IsNullOrEmpty() && x.ItemUICategory.ValueNullable?.RowId is 44 or 45 or 46 && x.ItemAction.ValueNullable?.Data[0] is 48 or 49).Select(x => new ConsumableItem() { StatusId = x.ItemAction.Value!.Data[0], ItemId = x.RowId, Name = x.Name.ToString(), CanBeHq = x.CanBeHq }).ToList() ?? [];

    private static string consumableItemsItemNameInput = "";
    private static ConsumableItem consumableItemsSelectedItem = new();

    private static string profileRenameInput = "";

    private static readonly Sounds[] _validSounds = ((Sounds[])Enum.GetValues(typeof(Sounds))).Where(s => s != Sounds.None && s != Sounds.Unknown).ToArray();

    private static bool overlayHeaderSelected      = false;
    private static bool devHeaderSelected          = false;
    private static bool dutyConfigHeaderSelected   = false;
    private static bool bmaiSettingHeaderSelected  = false;
    private static bool wrathSettingHeaderSelected = false;
    private static bool w2wSettingHeaderSelected   = false;
    private static bool advModeHeaderSelected      = false;
    private static bool preLoopHeaderSelected      = false;
    private static bool betweenLoopHeaderSelected  = false;
    private static bool terminationHeaderSelected  = false;

    public static void BuildManuals()
    {
        ConsumableItems.Add(new ConsumableItem { StatusId = 1086, ItemId = 14945, Name = "Squadron Enlistment Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1080, ItemId = 14948, Name = "Squadron Battle Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1081, ItemId = 14949, Name = "Squadron Survival Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1082, ItemId = 14950, Name = "Squadron Engineering Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1083, ItemId = 14951, Name = "Squadron Spiritbonding Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1084, ItemId = 14952, Name = "Squadron Rationing Manual", CanBeHq = false });
        ConsumableItems.Add(new ConsumableItem { StatusId = 1085, ItemId = 14953, Name = "Squadron Gear Maintenance Manual", CanBeHq = false });
    }

    public static void Draw()
    {
        if (MainWindow.CurrentTabName != "Config")
            MainWindow.CurrentTabName = "Config";

        //Start of Profile Selection
        ImGui.AlignTextToFramePadding();
        ImGui.Text("当前选择的配置文件: ");
        ImGui.SameLine();
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
        if (ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.Instance.DefaultConfigName)
            ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 180 * ImGuiHelpers.GlobalScale);
        ImGui.SetItemAllowOverlap();
        using (ImRaii.IEndObject configCombo = ImRaii.Combo("##ConfigCombo", ConfigurationMain.Instance.ActiveProfileName))
        {
            if (configCombo)
                foreach (string key in ConfigurationMain.Instance.ConfigNames)
                {
                    float selectableX = ImGui.GetCursorPosX();
                    if (key == ConfigurationMain.CONFIGNAME_BARE)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.Lock);
                    if (key == ConfigurationMain.Instance.DefaultConfigName)
                        ImGuiHelper.DrawIcon(FontAwesomeIcon.CheckCircle);

                    float textX = ImGui.GetCursorPosX();
                        
                    ImGui.SetCursorPosX(selectableX);
                    ImGui.SetItemAllowOverlap();
                    if (ImGui.Selectable($"###{key}ConfigSelectable"))
                        ConfigurationMain.Instance.SetProfile(key);
                    ImGui.SameLine(textX);
                    ImGui.Text(key);

                    ProfileData? profile = ConfigurationMain.Instance.GetProfile(key);
                    if(profile?.CIDs.Any() ?? false)
                    {
                        ImGui.SameLine();
                        ImGuiEx.TextWrapped(ImGuiHelper.VersionColor, string.Join(", ", profile.CIDs.Select(cid => ConfigurationMain.Instance.charByCID.TryGetValue(cid, out ConfigurationMain.CharData cd) ? cd.GetName() : cid.ToString())));
                    }
                }
        }

        ImGui.PopItemWidth();
        ImGui.SameLine();

        if (ImGui.IsPopupOpen("##RenameProfile"))
        {
            bool    open     = true;
            Vector2 textSize = ImGui.CalcTextSize(profileRenameInput);
            ImGui.SetNextWindowSize(new Vector2(textSize.X + 200, textSize.Y + 120) * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginPopupModal($"##RenameProfile", ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove))
            {
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize("新配置文件名称").X);
                ImGui.Text("新配置文件名称");
                ImGui.NewLine();
                ImGui.SameLine(50);
                ImGui.SetNextItemWidth((textSize.X + 100) * ImGuiHelpers.GlobalScale);

                ImGui.InputText("##RenameProfileInput", ref profileRenameInput, 100);
                ImGui.Spacing();
                ImGuiHelper.CenterNextElement(ImGui.CalcTextSize("Change Profile Name").X);
                if (ImGui.Button("更改配置文件名称"))
                {
                    if (ConfigurationMain.Instance.RenameCurrentProfile(profileRenameInput))
                    {
                        open = false;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndPopup();
            }
        }



        bool bareProfile = ConfigurationMain.Instance.ActiveProfileName == ConfigurationMain.CONFIGNAME_BARE;

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            ConfigurationMain.Instance.CreateNewProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("创建新配置文件");

        ImGui.SameLine(0, 15f);
        using (ImRaii.Disabled(bareProfile))
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Pen))
            {
                profileRenameInput = ConfigurationMain.Instance.ActiveProfileName;
                ImGui.OpenPopup("##RenameProfile");
            }

        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip("重命名配置文件");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            ConfigurationMain.Instance.DuplicateCurrentProfile();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("复制配置文件");

        ImGui.SameLine();
        using (ImRaii.Disabled(ImGui.GetIO().KeyCtrl ? ConfigurationMain.Instance.GetCurrentProfile.CIDs.Contains(Player.CID) != ImGui.GetIO().KeyShift : ConfigurationMain.Instance.DefaultConfigName == ConfigurationMain.Instance.ActiveProfileName))
            if (ImGuiComponents.IconButton(FontAwesomeIcon.CheckCircle))
                if(ImGui.GetIO().KeyCtrl)
                    if (ImGui.GetIO().KeyShift)
                        ConfigurationMain.Instance.RemoveCharacterDefault();
                    else
                        ConfigurationMain.Instance.SetCharacterDefault();
                else
                    ConfigurationMain.Instance.SetProfileAsDefault();
        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip("设为默认\n按住 Ctrl 键设为当前角色的默认值\nCtrl+Shift 组合键取消当前角色的默认设置");


        ImGui.SameLine();
        using (ImRaii.Disabled(bareProfile || !ImGui.GetIO().KeyCtrl))
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
                ConfigurationMain.Instance.RemoveCurrentProfile();
        if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax()))
            ImGui.SetTooltip("删除配置\n按住ctrl启用");

        if (bareProfile)
            ImGuiEx.TextWrapped("基础配置文件只是为了运行任务，您可以复制它进行编辑");
        using ImRaii.IEndObject _ = ImRaii.Disabled(bareProfile);

        //Start of Window & Overlay Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var overlayHeader = ImGui.Selectable("窗口与悬浮窗设定", overlayHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();      
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (overlayHeader)
            overlayHeaderSelected = !overlayHeaderSelected;

        if (overlayHeaderSelected == true)
        {
            if (ImGui.Checkbox("显示悬浮窗", ref Configuration.showOverlay))
            {
                Configuration.ShowOverlay = Configuration.showOverlay;
                Configuration.Save();
            }
            ImGuiComponents.HelpMarker("请注意，快速操作按钮（军票/分解/等）需要启用各自的配置！\n或启用覆盖悬浮窗按钮的选项。");
            if (Configuration.ShowOverlay)
            {
                ImGui.Indent();
                ImGui.Columns(2, "##OverlayColumns", false);

                //ImGui.SameLine(0, 53);
                if (ImGui.Checkbox("停止时隐藏", ref Configuration.hideOverlayWhenStopped))
                {
                    Configuration.HideOverlayWhenStopped = Configuration.hideOverlayWhenStopped;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                if (ImGui.Checkbox("锁定悬浮窗", ref Configuration.lockOverlay))
                {
                    Configuration.LockOverlay = Configuration.lockOverlay;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                //ImGui.SameLine(0, 57);
                
                if (ImGui.Checkbox("显示副本与运行次数", ref Configuration.ShowDutyLoopText))
                    Configuration.Save();
                ImGui.NextColumn();
                if (ImGui.Checkbox("使用透明背景", ref Configuration.overlayNoBG))
                {
                    Configuration.OverlayNoBG = Configuration.overlayNoBG;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                if (ImGui.Checkbox("覆盖悬浮窗按钮", ref Configuration.OverrideOverlayButtons))
                    Configuration.Save();
                ImGuiComponents.HelpMarker("默认情况下，悬浮窗按钮会在其配置启用时自动启用\n此选项将允许您选择启用哪些按钮");
                ImGui.NextColumn();
                if (ImGui.Checkbox("显示运行状况", ref Configuration.ShowActionText))
                    Configuration.Save();
                if (Configuration.OverrideOverlayButtons)
                {
                    ImGui.Indent();
                    ImGui.Columns(3, "##OverlayButtonColumns", false);
                    if (ImGui.Checkbox("前往", ref Configuration.GotoButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("军票", ref Configuration.TurninButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("分解", ref Configuration.DesynthButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("精炼", ref Configuration.ExtractButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("修理", ref Configuration.RepairButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("装备", ref Configuration.EquipButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("装备箱", ref Configuration.CofferButton))
                        Configuration.Save();
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("九宫幻卡##TTButton", ref Configuration.TTButton))
                        Configuration.Save();
                    ImGui.Unindent();
                }
                ImGui.Unindent();
            }
            ImGui.Columns(1);
            if (ImGui.Checkbox("启动时显示主窗口", ref Configuration.ShowMainWindowOnStartup))
                Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Checkbox("滑块输入", ref Configuration.UseSliderInputs))
                Configuration.Save();
            
        }

        if (Plugin.isDev)
        {
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            var devHeader = ImGui.Selectable("开发选项", devHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (devHeader)
                devHeaderSelected = !devHeaderSelected;

            if (devHeaderSelected)
            {
                if (ImGui.Checkbox("启动时更新配置", ref ConfigurationMain.Instance.updatePathsOnStartup))
                    Configuration.Save();

                if (ImGui.Button("打印模块列表")) 
                    Svc.Log.Info(string.Join("\n", PluginInterface.InstalledPlugins.Where(pl => pl.IsLoaded).GroupBy(pl => pl.Manifest.InstalledFromUrl).OrderByDescending(g => g.Count()).Select(g => g.Key+"\n\t"+string.Join("\n\t", g.Select(pl => pl.Name)))));

                if (ImGui.CollapsingHeader("Available Duty Support"))//ImGui.Button("check duty support?"))
                {
                    if(GenericHelpers.TryGetAddonMaster<AddonMaster.DawnStory>(out AddonMaster.DawnStory? m))
                    {
                        if (m.IsAddonReady)
                        {
                            ImGuiEx.Text("Selected: " + m.Reader.CurrentSelection);

                            ImGuiEx.Text($"Cnt: {m.Reader.EntryCount}");
                            foreach (var x in m.Entries)
                            {
                                ImGuiEx.Text($"{x.Name} / {x.ReaderEntry.Callback} / {x.Index}");
                                if (ImGuiEx.HoveredAndClicked() && x.Status != 2)
                                {
                                    x.Select();
                                }
                            }
                        }
                    }
                }

                if (ImGui.CollapsingHeader("可用的九宫幻卡"))
                {
                    unsafe
                    {
                        if (GenericHelpers.TryGetAddonByName("TripleTriadCoinExchange", out AtkUnitBase* exchangeAddon))
                        {
                            if (exchangeAddon->IsReady)
                            {
                                ReaderTripleTriadCoinExchange exchange = new(exchangeAddon);

                                ImGuiEx.Text($"Cnt: {exchange.EntryCount}");
                                foreach (var x in exchange.Entries)
                                {
                                    ImGuiEx.Text($"({x.Id}) {x.Name} | {x.Count} | {x.Value} | {x.InDeck}");
                                    if (ImGuiEx.HoveredAndClicked())
                                    {
                                        //x.Select();
                                    }
                                }
                            }
                        }
                    }
                }



                if (ImGui.Button("开启循环"))
                {
                    Plugin.SetRotationPluginSettings(true, ignoreTimer: true);
                }

                ImGui.SameLine();
                if (ImGui.Button("关闭循环"))
                {
                    Plugin.SetRotationPluginSettings(false);
                    if(Wrath_IPCSubscriber.IsEnabled)
                        Wrath_IPCSubscriber.Release();
                }

                if (ImGui.Button("BetweenLoopActions##DevBetweenLoops"))
                {
                    Plugin.CurrentTerritoryContent =  ContentHelper.DictionaryContent.Values.First();
                    Plugin.States                  |= PluginState.Other;
                    Plugin.LoopTasks(false);
                }

                if (ImGui.CollapsingHeader("teleport playthings"))
                {
                    if (ImGui.CollapsingHeader("Warps"))
                    {
                        ImGui.Indent();
                        foreach (Warp warp in Svc.Data.GameData.GetExcelSheet<Warp>())
                        {
                            if (warp.TerritoryType.RowId != 152)
                                continue;

                            if (ImGui.CollapsingHeader($"{warp.Name} {warp.Question} to {warp.TerritoryType.ValueNullable?.PlaceName.ValueNullable?.Name.ToString()}##{warp.RowId}"))
                            {
                                if (warp.PopRange.ValueNullable is { } level)
                                {
                                    ImGui.Text($"{level.X} {level.Y} {level.Z} in {level.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ToString()}");
                                    ImGui.Text($"{(new Vector3(level.X, level.Y, level.Z) - Player.Position)}");
                                }
                            }
                        }

                        ImGui.Unindent();
                    }

                    if (ImGui.CollapsingHeader("LevelTest"))
                    {
                        foreach ((Level lvl, Vector3, Vector3) level in Svc.Data.GameData.GetExcelSheet<Level>().Where(lvl => lvl.Territory.RowId == 152)
                                                                           .Select(lvl => (lvl, (new Vector3(lvl.X, lvl.Y, lvl.Z))))
                                                                           .Select(tuple => (tuple.lvl, tuple.Item2, (tuple.Item2 - Player.Position))).OrderBy(lvl => lvl.Item3.LengthSquared()))
                        {
                            ImGui.Text($"{level.lvl.RowId} {level.Item2} {level.Item3} {string.Join(" | ", level.lvl.Object.GetType().GenericTypeArguments.Select(t => t.FullName))}: {level.lvl.Object.RowId}");
                        }
                    }

                    ImGuiEx.Text($"{typeof(Achievement).Assembly.GetTypes().Where(x => x.FullName.StartsWith("Lumina.Excel.Sheets")).Select(x => (x, x.GetProperties().Where(f => f.PropertyType.Name == "RowRef`1" && f.PropertyType.GenericTypeArguments[0].FullName == typeof(Map).FullName))).Where(x => x.Item2.Any()).Select(x => $"{x.Item1} references {x.Item2.Select(x => x.Name).Print(", ")}").Print("\n")}");
                }
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var dutyConfigHeader = ImGui.Selectable("副本配置设定", dutyConfigHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (dutyConfigHeader)
            dutyConfigHeaderSelected = !dutyConfigHeaderSelected;

        if (dutyConfigHeaderSelected == true)
        {
            ImGui.Columns(2, "##DutyConfigHeaderColumns");
            if (ImGui.Checkbox("自动退出副本", ref Configuration.AutoExitDuty))
                Configuration.Save();
            ImGuiComponents.HelpMarker("将在完成路径后自动退出副本。");
            ImGui.NextColumn();
            if (ImGui.Checkbox("在完成任务前阻止离开副本", ref Configuration.OnlyExitWhenDutyDone))
                Configuration.Save();
            //ImGuiComponents.HelpMarker("Blocks leaving dungeon before duty is completed");
            ImGui.Columns(1);
            if (ImGui.Checkbox("自动管理循环插件状态", ref Configuration.AutoManageRotationPluginState))
                Configuration.Save();
            ImGuiComponents.HelpMarker("Autoduty 会在每个任务开始时启用循环插件\n*仅在使用 Wrath Combo、 Rotation Solver 或 BossMod AutoRotation 时生效\n**AutoDuty 会自动确定您正在使用的插件");

            if (Configuration.AutoManageRotationPluginState)
            {
                if (Wrath_IPCSubscriber.IsEnabled)
                {
                    ImGui.Indent();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                    var wrathSettingHeader = ImGui.Selectable("> Wrath Combo 配置选项 <", wrathSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemHovered())
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (wrathSettingHeader)
                        wrathSettingHeaderSelected = !wrathSettingHeaderSelected;

                    if (wrathSettingHeaderSelected)
                    {
                        bool wrath_AutoSetupJobs = Configuration.Wrath_AutoSetupJobs;
                        if (ImGui.Checkbox("自动设置自动循环", ref wrath_AutoSetupJobs))
                        {
                            Configuration.Wrath_AutoSetupJobs = wrath_AutoSetupJobs;
                            Configuration.Save();
                        }
                        ImGuiComponents.HelpMarker("如果没有启用此功能并且在 Wrath Combo 中没有设置作业，AD 将改为使用 RSR 或 bm 自动循环。");

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Targeting | Tank: ");
                        ImGui.SameLine(0, 5);
                        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                        if (ImGui.BeginCombo("##ConfigWrathTargetingTank", Configuration.Wrath_TargetingTank.ToCustomString()))
                        {
                            foreach (Wrath_IPCSubscriber.DPSRotationMode targeting in Enum.GetValues(typeof(Wrath_IPCSubscriber.DPSRotationMode)))
                            {
                                if(targeting == Wrath_IPCSubscriber.DPSRotationMode.Tank_Target)
                                    continue;

                                if (ImGui.Selectable(targeting.ToCustomString()))
                                {
                                    Configuration.Wrath_TargetingTank = targeting;
                                    Configuration.Save();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Targeting | Non-Tank: ");
                        ImGui.SameLine(0, 5);
                        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                        if (ImGui.BeginCombo("##ConfigWrathTargetingNonTank", Configuration.Wrath_TargetingNonTank.ToCustomString()))
                        {
                            foreach (Wrath_IPCSubscriber.DPSRotationMode targeting in Enum.GetValues(typeof(Wrath_IPCSubscriber.DPSRotationMode)))
                            {
                                if (ImGui.Selectable(targeting.ToCustomString()))
                                {
                                    Configuration.Wrath_TargetingNonTank = targeting;
                                    Configuration.Save();
                                }
                            }
                            ImGui.EndCombo();
                        }

                        ImGui.Separator();
                    }
                    ImGui.Unindent();
                }
            }

            if (ImGui.Checkbox("自动管理 BossMod AI 设置", ref Configuration.autoManageBossModAISettings))
                Configuration.Save();
            ImGuiComponents.HelpMarker("Autoduty 会在每个任务开始时启用 BMAI 及您配置的选项");

            if (Configuration.autoManageBossModAISettings)
            {
                ImGui.Indent();
                ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                var bmaiSettingHeader = ImGui.Selectable("> BMAI 配置选项 <", bmaiSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (bmaiSettingHeader)
                    bmaiSettingHeaderSelected = !bmaiSettingHeaderSelected;
            
                if (bmaiSettingHeaderSelected == true)
                {
                    if (ImGui.Button("更新预设"))
                    {
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty", Resources.AutoDutyPreset);
                        BossMod_IPCSubscriber.RefreshPreset("AutoDuty Passive", Resources.AutoDutyPassivePreset);
                    }
                    if (ImGui.Checkbox("自动更新预设", ref Configuration.BM_UpdatePresetsAutomatically)) 
                        Configuration.Save();
                    if (ImGui.Checkbox("根据职能设置到目标的最大距离", ref Configuration.maxDistanceToTargetRoleBased))
                    {
                        Configuration.MaxDistanceToTargetRoleBased = Configuration.maxDistanceToTargetRoleBased;
                        Configuration.Save();
                    }
                    using (ImRaii.Disabled(Configuration.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);
                        if (ImGui.SliderFloat("到目标的最大距离", ref Configuration.MaxDistanceToTargetFloat, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetFloat = Math.Clamp(Configuration.MaxDistanceToTargetFloat, 1, 30);
                            Configuration.Save();
                        }
                        if (ImGui.SliderFloat("到目标的最大距离(使用AOE时)", ref Configuration.MaxDistanceToTargetAoEFloat, 1, 10))
                        {
                            Configuration.MaxDistanceToTargetAoEFloat = Math.Clamp(Configuration.MaxDistanceToTargetAoEFloat, 1, 10);
                            Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                    }
                    using (ImRaii.Disabled(!Configuration.MaxDistanceToTargetRoleBased))
                    {
                        ImGui.PushItemWidth(195 * ImGuiHelpers.GlobalScale);
                        if (ImGui.SliderFloat("到目标的最大距离 | 近战", ref Configuration.MaxDistanceToTargetRoleMelee, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetRoleMelee = Math.Clamp(Configuration.MaxDistanceToTargetRoleMelee, 1, 30);
                            Configuration.Save();
                        }
                        if (ImGui.SliderFloat("到目标的最大距离 | 远程", ref Configuration.MaxDistanceToTargetRoleRanged, 1, 30))
                        {
                            Configuration.MaxDistanceToTargetRoleRanged = Math.Clamp(Configuration.MaxDistanceToTargetRoleRanged, 1, 30);
                            Configuration.Save();
                        }
                        ImGui.PopItemWidth();
                    }
                    if (ImGui.Checkbox("根据玩家职能设置身位", ref Configuration.positionalRoleBased))
                    {
                        Configuration.PositionalRoleBased = Configuration.positionalRoleBased;
                        Plugin.BMRoleChecks();
                        Configuration.Save();
                    }
                    using (ImRaii.Disabled(Configuration.positionalRoleBased))
                    {
                        ImGui.SameLine(0, 10);
                        if (ImGui.Button(Configuration.PositionalEnum.GetDescription()))
                            ImGui.OpenPopup("PositionalPopup");
            
                        if (ImGui.BeginPopup("PositionalPopup"))
                        {
                            foreach (Positional positional in Enum.GetValues(typeof(Positional)))
                            {
                                if (ImGui.Selectable(positional.GetDescription()))
                                {
                                    Configuration.PositionalEnum = positional;
                                    Configuration.Save();
                                }
                            }
                            ImGui.EndPopup();
                        }
                    }
                    if (ImGui.Button("使用默认BMAI设置"))
                    {
                        Configuration.maxDistanceToTargetRoleBased = true;
                        Configuration.positionalRoleBased = true;
                        Configuration.Save();
                    }
                    ImGuiComponents.HelpMarker("点击此按钮将把您的 BMAI 配置重置为 AD 的默认推荐设置。");

                    ImGui.Separator();
                }
                ImGui.Unindent();
            }
            if (ImGui.Checkbox("自动管理 Vnav 对齐相机设置", ref Configuration.AutoManageVnavAlignCamera))
                Configuration.Save();
            ImGuiComponents.HelpMarker("Autoduty 会在每个任务开始时启用 VNav 的 AlignCamera 功能，并在任务结束后禁用该功能（如果之前未设置）");

            if (ImGui.Checkbox("开启宝箱", ref Configuration.LootTreasure))
                Configuration.Save();

            if (Configuration.LootTreasure)
            {
                ImGui.Indent();
                ImGui.Text("选择模式: ");
                ImGui.SameLine(0, 5);
                ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                if (ImGui.BeginCombo("##ConfigLootMethod", Configuration.LootMethodEnum.ToCustomString()))
                {
                    foreach (LootMethod lootMethod in Enum.GetValues(typeof(LootMethod)))
                    {
                        if(lootMethod == LootMethod.RotationSolver)
                            continue;
                        using (ImRaii.Disabled((lootMethod == LootMethod.Pandora && !PandorasBox_IPCSubscriber.IsEnabled)))
                        {
                            if (ImGui.Selectable(lootMethod.ToCustomString()))
                            {
                                Configuration.LootMethodEnum = lootMethod;
                                Configuration.Save();
                            }
                        }
                    }
                    ImGui.EndCombo();
                }
                
                if (ImGui.Checkbox("仅开启BOSS宝箱", ref Configuration.LootBossTreasureOnly))
                        Configuration.Save();

                ImGuiComponents.HelpMarker("AutoDuty将绕过非BOSS宝箱，仅开启BOSS宝箱。\n部分路径可能无法适配。");
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }
            ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("认为卡住前的最短时间（毫秒）", ref Configuration.MinStuckTime))
            {
                Configuration.MinStuckTime = Math.Max(250, Configuration.MinStuckTime);
                Configuration.Save();
            }

            if (ImGui.Checkbox("当卡住时重新构建导航", ref Configuration.RebuildNavmeshOnStuck))
                Configuration.Save();

            if (Configuration.RebuildNavmeshOnStuck)
            {
                ImGui.SameLine();
                int rebuildX = Configuration.RebuildNavmeshAfterStuckXTimes;
                if(ImGui.InputInt("times##RebuildNavmeshAfterStuckXTimes", ref rebuildX))
                {
                    Configuration.RebuildNavmeshAfterStuckXTimes = (byte) Math.Clamp(rebuildX, byte.MinValue+1, byte.MaxValue);
                    Configuration.Save();
                }
            }

            if(ImGui.Checkbox("在路线中绘制下一步", ref Configuration.PathDrawEnabled))
                Configuration.Save();
            ImGui.PopItemWidth();
            if (Configuration.PathDrawEnabled)
            {
                ImGui.Indent();
                ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt("绘制x步##PathDrawStepCount", ref Configuration.PathDrawStepCount, 1))
                {
                    Configuration.PathDrawStepCount = Math.Max(1, Configuration.PathDrawStepCount);
                    Configuration.Save();
                }
                ImGui.PopItemWidth();
                ImGui.Unindent();
            }



            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            bool w2wSettingHeader = ImGui.Selectable($"> {PathIdentifiers.W2W} 设置 <", w2wSettingHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (w2wSettingHeader)
                w2wSettingHeaderSelected = !w2wSettingHeaderSelected;

            if (w2wSettingHeaderSelected)
            {
                if(ImGui.Checkbox("Treat Unsync as W2W", ref Configuration.TreatUnsyncAsW2W))
                    Configuration.Save();
                ImGuiComponents.HelpMarker("Only works in paths with W2W tags on steps");


                ImGui.BeginListBox("##W2WConfig", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 300));
                JobWithRoleHelper.DrawCategory(JobWithRole.All, ref Configuration.W2WJobs);
                ImGui.EndListBox();
            }

            if (ImGui.Checkbox("禁用队伍验证", ref Configuration.OverridePartyValidation))
                Configuration.Save();
            ImGuiComponents.HelpMarker("AutoDuty 在排本时将忽略您的队伍配置\n此功能仅适用于多开场景\n*不建议与其他玩家游戏时使用 AutoDuty*");


            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
            var advModeHeader = ImGui.Selectable("高级配置选项", advModeHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
            ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (advModeHeader)
                advModeHeaderSelected = !advModeHeaderSelected;

            if (advModeHeaderSelected == true)
            {
                if (ImGui.Checkbox("使用替代的循环插件", ref Configuration.UsingAlternativeRotationPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker("使用 Wrath Combo, Rotation Solver 或 BossMod AutoRotation以外的循环插件时勾选");

                if (ImGui.Checkbox("使用替代的移动插件", ref Configuration.UsingAlternativeMovementPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker("使用 Vnavmesh 以外的其他移动插件时勾选");

                if (ImGui.Checkbox("使用替代的机制插件", ref Configuration.UsingAlternativeBossPlugin))
                    Configuration.Save();
                ImGuiComponents.HelpMarker("使用 BossMod/BMR 以外的机制插件时勾选");
            }
        }

        //Start of Pre-Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var preLoopHeader = ImGui.Selectable("循环前设置", preLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (preLoopHeader)
            preLoopHeaderSelected = !preLoopHeaderSelected;

        if (preLoopHeaderSelected == true)
        {
            if (ImGui.Checkbox("开启###PreLoopEnable", ref Configuration.EnablePreLoopActions))
                Configuration.Save();

            using (ImRaii.Disabled(!Configuration.EnablePreLoopActions))
            {
                ImGui.Separator();
                MakeCommands("在循环开始时执行命令",
                             ref Configuration.ExecuteCommandsPreLoop, ref Configuration.CustomCommandsPreLoop, ref preLoopCommand);

                ImGui.Separator();

                ImGui.TextColored(ImGuiHelper.VersionColor,
                                  $"如果启用了循环间设置，以下内容也会在每次循环之间执行 (当前{(Configuration.EnableBetweenLoopActions ? "启用" : "禁用")})");

                if (ImGui.Checkbox("返回至", ref Configuration.RetireMode))
                    Configuration.Save();

                using (ImRaii.Disabled(!Configuration.RetireMode))
                {
                    ImGui.SameLine(0, 5);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##RetireLocation", Configuration.RetireLocationEnum.GetDescription()))
                    {
                        foreach (RetireLocation retireLocation in Enum.GetValues(typeof(RetireLocation)))
                        {
                            if (ImGui.Selectable(retireLocation.GetDescription()))
                            {
                                Configuration.RetireLocationEnum = retireLocation;
                                Configuration.Save();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    if (Configuration is { RetireMode: true, RetireLocationEnum: RetireLocation.Personal_Home })
                    {
                        if (ImGui.Button("添加当前位置"))
                        {
                            Configuration.PersonalHomeEntrancePath.Add(Player.Position);
                            Configuration.Save();
                        }

                        ImGuiComponents
                           .HelpMarker("对于大多数房屋，传送位置直接面对大门时无需设置路径；在少数情况下，如果大门需要路径才能到达，您可以在此创建路径。或者，如果您的大门似乎比邻居的传送位置更远，只需前往大门并点击‘添加当前位置’即可。");

                        using (ImRaii.ListBox("##PersonalHomeVector3List", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X,
                                                                                                       (ImGui.GetTextLineHeightWithSpacing() * Configuration.PersonalHomeEntrancePath.Count) + 5)))
                        {
                            var removeItem = false;
                            var removeAt   = 0;

                            foreach (var item in Configuration.PersonalHomeEntrancePath.Select((Value, Index) => (Value, Index)))
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    removeItem = true;
                                    removeAt   = item.Index;
                                }
                            }

                            if (removeItem)
                            {
                                Configuration.PersonalHomeEntrancePath.RemoveAt(removeAt);
                                Configuration.Save();
                            }
                        }
                    }

                    if (Configuration is { RetireMode: true, RetireLocationEnum: RetireLocation.FC_Estate })
                    {
                        if (ImGui.Button("添加当前位置"))
                        {
                            Configuration.FCEstateEntrancePath.Add(Player.Position);
                            Configuration.Save();
                        }

                        ImGuiComponents
                           .HelpMarker("对于大多数房屋，传送位置直接面对大门时无需设置路径；在少数情况下，如果大门需要路径才能到达，您可以在此创建路径。或者，如果您的大门似乎比邻居的传送位置更远，只需前往大门并点击‘添加当前位置’即可。");

                        using (ImRaii.ListBox("##FCEstateVector3List", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X,
                                                                                                   (ImGui.GetTextLineHeightWithSpacing() * Configuration.FCEstateEntrancePath.Count) + 5)))
                        {
                            var removeItem = false;
                            var removeAt   = 0;

                            foreach (var item in Configuration.FCEstateEntrancePath.Select((Value, Index) => (Value, Index)))
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    removeItem = true;
                                    removeAt   = item.Index;
                                }
                            }

                            if (removeItem)
                            {
                                Configuration.FCEstateEntrancePath.RemoveAt(removeAt);
                                Configuration.Save();
                            }
                        }
                    }
                }

                if (ImGui.Checkbox("自动装备最强装备", ref Configuration.AutoEquipRecommendedGear))
                    Configuration.Save();

                ImGuiComponents.HelpMarker("仅使用兵装库中的装备");


                if (Configuration.AutoEquipRecommendedGear)
                {
                    ImGui.Indent();
                    using (ImRaii.Disabled(!Gearsetter_IPCSubscriber.IsEnabled))
                    {
                        if (ImGui.Checkbox("考虑兵装库以外的物品", ref Configuration.AutoEquipRecommendedGearGearsetter))
                            Configuration.Save();

                        if (Configuration.AutoEquipRecommendedGearGearsetter)
                        {
                            ImGui.Indent();
                            if (ImGui.Checkbox("将旧物品移至库存", ref Configuration.AutoEquipRecommendedGearGearsetterOldToInventory))
                                Configuration.Save();
                            ImGuiComponents.HelpMarker("除武器外，这将把要更换的装备转移到库存中。");
                            ImGui.Unindent();
                        }
                    }

                    if (!Gearsetter_IPCSubscriber.IsEnabled)
                    {
                        if (Configuration.AutoEquipRecommendedGearGearsetter)
                        {
                            Configuration.AutoEquipRecommendedGearGearsetter = false;
                            Configuration.Save();
                        }

                        ImGui.Text("* 兵装库以外的物品需要 Gearsetter 插件。");
                        ImGui.Text("获取 @ ");
                        ImGui.SameLine(0, 0);
                        ImGuiEx.TextCopy(ImGuiHelper.LinkColor, @"https://plugins.carvel.li");
                    }

                    ImGui.Unindent();
                }

                if (ImGui.Checkbox("自动修理", ref Configuration.AutoRepair))
                    Configuration.Save();

                if (Configuration.AutoRepair)
                {
                    ImGui.SameLine();

                    if (ImGui.RadioButton("自己", Configuration.AutoRepairSelf))
                    {
                        Configuration.AutoRepairSelf = true;
                        Configuration.Save();
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker("使用暗物质进行修理（需要生产职业拥有足够的等级！）");
                    ImGui.SameLine();

                    if (ImGui.RadioButton("主城NPC", !Configuration.AutoRepairSelf))
                    {
                        Configuration.AutoRepairSelf = false;
                        Configuration.Save();
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker("使用首选修理NPC进行修理");
                    ImGui.Indent();
                    ImGui.Text("修理阈值 @");
                    ImGui.SameLine();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    int autoRepairPct = (int)Configuration.AutoRepairPct;
                    if (ImGui.SliderInt("##Repair@", ref autoRepairPct, 0, 99, "%d%%"))
                    {
                        Configuration.AutoRepairPct = Math.Clamp((uint)autoRepairPct, 0, 99);
                        Configuration.Save();
                    }

                    ImGui.PopItemWidth();
                    if (!Configuration.AutoRepairSelf)
                    {
                        ImGui.Text("首选修理NPC: ");
                        ImGuiComponents.HelpMarker("建议将修理NPC，雇员铃，以及（如果可能）返回位置匹配起来。");
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.BeginCombo("##PreferredRepair",
                                             Configuration.PreferredRepairNPC != null ?
                                                 $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Configuration.PreferredRepairNPC.Name.ToLowerInvariant())} ({Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(Configuration.PreferredRepairNPC.TerritoryType)?.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(Configuration.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(Configuration.PreferredRepairNPC.TerritoryType).Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(Configuration.PreferredRepairNPC.Position.ToVector2(), Svc.Data.GetExcelSheet<TerritoryType>().GetRow(Configuration.PreferredRepairNPC.TerritoryType).Map.Value).Y.ToString("0.0", CultureInfo.InvariantCulture)})" :
                                                 "初始主城旅馆"))
                        {
                            if (ImGui.Selectable("初始主城旅馆"))
                            {
                                Configuration.PreferredRepairNPC = null;
                                Configuration.Save();
                            }

                            foreach (RepairNpcData repairNPC in RepairNPCs)
                            {
                                if (repairNPC.TerritoryType <= 0)
                                {
                                    ImGui.Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant()));
                                    continue;
                                }

                                var territoryType = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(repairNPC.TerritoryType);

                                if (territoryType == null) continue;

                                if
                                    (ImGui.Selectable($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(repairNPC.Name.ToLowerInvariant())} ({territoryType.Value.PlaceName.ValueNullable?.Name.ToString()})  ({MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).X.ToString("0.0", CultureInfo.InvariantCulture)}, {MapHelper.ConvertWorldXZToMap(repairNPC.Position.ToVector2(), territoryType.Value.Map.Value!).Y.ToString("0.0", CultureInfo.InvariantCulture)})"))
                                {
                                    Configuration.PreferredRepairNPC = repairNPC;
                                    Configuration.Save();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.PopItemWidth();
                    }

                    ImGui.Unindent();
                }

                if (ImGui.Checkbox("自动使用物品", ref Configuration.AutoConsume))
                    Configuration.Save();

                ImGuiComponents.HelpMarker("AutoDuty 会在运行和每次循环之间消耗这些物品（如果状态不存在）");
                if (Configuration.AutoConsume)
                {
                    ImGui.SameLine();
                    ImGui.Columns(3, "##AutoConsumeColumns");
                    //ImGui.SameLine(0, 5);
                    ImGui.NextColumn();
                    if (ImGui.Checkbox("忽略状态", ref Configuration.AutoConsumeIgnoreStatus))
                        Configuration.Save();

                    ImGuiComponents.HelpMarker("AutoDuty 会在运行和每次循环之间每次都消耗这些物品（即使状态已存在）");
                    ImGui.NextColumn();
                    //ImGui.SameLine(0, 5);

                    ImGui.PushItemWidth(80 * ImGuiHelpers.GlobalScale);

                    using (ImRaii.Disabled(Configuration.AutoConsumeIgnoreStatus))
                    {
                        if (ImGui.InputInt("最少剩余时间", ref Configuration.AutoConsumeTime))
                        {
                            Configuration.AutoConsumeTime = Math.Clamp(Configuration.AutoConsumeTime, 0, 59);
                            Configuration.Save();
                        }

                        ImGuiComponents.HelpMarker("如果状态剩余时间少于这个时间（以分钟为单位），将消耗这些物品");
                    }

                    ImGui.PopItemWidth();
                    ImGui.Columns(1);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 115 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo("##SelectAutoConsumeItem", consumableItemsSelectedItem.Name))
                    {
                        ImGui.InputTextWithHint("物品名称", "输入物品名称以进行搜索", ref consumableItemsItemNameInput, 1000);
                        foreach (var item in ConsumableItems.Where(x => x.Name.Contains(consumableItemsItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                        {
                            if (ImGui.Selectable($"{item.Name}"))
                            {
                                consumableItemsSelectedItem = item;
                            }
                        }

                        ImGui.EndCombo();
                    }

                    ImGui.PopItemWidth();

                    ImGui.SameLine(0, 5);
                    using (ImRaii.Disabled(consumableItemsSelectedItem == null))
                    {
                        if (ImGui.Button("添加物品"))
                        {
                            if (Configuration.AutoConsumeItemsList.Any(x => x.Key == consumableItemsSelectedItem!.StatusId))
                                Configuration.AutoConsumeItemsList.RemoveAll(x => x.Key == consumableItemsSelectedItem!.StatusId);

                            Configuration.AutoConsumeItemsList.Add(new(consumableItemsSelectedItem!.StatusId, consumableItemsSelectedItem));
                            Configuration.Save();
                        }
                    }

                    using (ImRaii.ListBox("##ConsumableItemList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X,
                                                                                              (ImGui.GetTextLineHeightWithSpacing() * Configuration.AutoConsumeItemsList.Count) + 5)))
                    {
                        var                                  boolRemoveItem = false;
                        KeyValuePair<ushort, ConsumableItem> removeItem     = new();
                        foreach (var item in Configuration.AutoConsumeItemsList)
                        {
                            ImGui.Selectable($"{item.Value.Name}");
                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                boolRemoveItem = true;
                                removeItem     = item;
                            }
                        }

                        if (boolRemoveItem)
                        {
                            Configuration.AutoConsumeItemsList.Remove(removeItem);
                            Configuration.Save();
                        }
                    }
                }
            }
        }

        //Between Loop Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var betweenLoopHeader = ImGui.Selectable("循环间设置", betweenLoopHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (betweenLoopHeader)
            betweenLoopHeaderSelected = !betweenLoopHeaderSelected;

        if (betweenLoopHeaderSelected == true)
        {
            ImGui.Columns(2, "##BetweenLoopHeaderColumns");

            if (ImGui.Checkbox("开启###BetweenLoopEnable", ref Configuration.EnableBetweenLoopActions))
                Configuration.Save();

            using (ImRaii.Disabled(!Configuration.EnableBetweenLoopActions))
            {
                ImGui.NextColumn();

                if (ImGui.Checkbox("在最终循环运行###BetweenLoopEnableLastLoop", ref Configuration.ExecuteBetweenLoopActionLastLoop))
                    Configuration.Save();

                ImGui.Columns(1);

                ImGui.Separator();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcItemWidth());
                if (ImGui.InputInt("(秒)循环间等待", ref Configuration.WaitTimeBeforeAfterLoopActions))
                {
                    if (Configuration.WaitTimeBeforeAfterLoopActions < 0) Configuration.WaitTimeBeforeAfterLoopActions = 0;
                    Configuration.Save();
                }
                ImGui.PopItemWidth();
                ImGuiComponents.HelpMarker("将在循环之间的所有 AutoDuty 进程延迟 X 秒");
                ImGui.Separator();

                MakeCommands("在所有循环之间执行命令",
                             ref Configuration.ExecuteCommandsBetweenLoop,    ref Configuration.CustomCommandsBetweenLoop, ref betweenLoopCommand);

                if (ImGui.Checkbox("自动精炼", ref Configuration.AutoExtract))
                    Configuration.Save();

                if (Configuration.AutoExtract)
                {
                    ImGui.SameLine(0, 10);
                    if (ImGui.RadioButton("装备中", !Configuration.autoExtractAll))
                    {
                        Configuration.AutoExtractAll = false;
                        Configuration.Save();
                    }
                    ImGui.SameLine(0, 5);
                    if (ImGui.RadioButton("所有", Configuration.autoExtractAll))
                    {
                        Configuration.AutoExtractAll = true;
                        Configuration.Save();
                    }
                }

                if (ImGui.Checkbox("自动打开装备箱", ref Configuration.AutoOpenCoffers))
                    Configuration.Save();

                ImGuiComponents.HelpMarker("AutoDuty将在每个循环之间开启装备箱（如骑士武器）");
                if (Configuration.AutoOpenCoffers)
                {
                    unsafe
                    {
                        ImGui.Indent();
                        ImGui.Text("使用套装打开装备箱: ");
                        ImGui.AlignTextToFramePadding();
                        ImGui.SameLine();

                        RaptureGearsetModule* module = RaptureGearsetModule.Instance();
                        
                        if (Configuration.AutoOpenCoffersGearset != null && !module->IsValidGearset((int) Configuration.AutoOpenCoffersGearset))
                        {
                            Configuration.AutoOpenCoffersGearset = null;
                            Configuration.Save();
                        }


                        if (ImGui.BeginCombo("##CofferGearsetSelection", Configuration.AutoOpenCoffersGearset != null ? module->GetGearset(Configuration.AutoOpenCoffersGearset.Value)->NameString : "当前套装"))
                        {
                            if (ImGui.Selectable("当前套装"))
                            {
                                Configuration.AutoOpenCoffersGearset = null;
                                Configuration.Save();
                            }

                            for (int i = 0; i < module->NumGearsets; i++)
                            {
                                RaptureGearsetModule.GearsetEntry* gearset = module->GetGearset(i);
                                if(ImGui.Selectable(gearset->NameString))
                                {
                                    Configuration.AutoOpenCoffersGearset = gearset->Id;
                                    Configuration.Save();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        if (ImGui.Checkbox("使用黑名单", ref Configuration.AutoOpenCoffersBlacklistUse))
                            Configuration.Save();

                        ImGuiComponents.HelpMarker("禁止自动打开某些装备箱");
                        if (Configuration.AutoOpenCoffersBlacklistUse)
                        {
                            if (ImGui.BeginCombo("选择装备箱", autoOpenCoffersSelectedItem.Value))
                            {
                                ImGui.InputTextWithHint("装备箱名称", "开始输入装备箱名称以进行搜索", ref autoOpenCoffersNameInput, 1000);
                                foreach (var item in Items.Where(x => CofferHelper.ValidCoffer(x.Value) && x.Value.Name.ToString().Contains(autoOpenCoffersNameInput, StringComparison.InvariantCultureIgnoreCase)))
                                {
                                    if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                                        autoOpenCoffersSelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                                }
                                ImGui.EndCombo();
                            }

                            ImGui.SameLine(0, 5);
                            using (ImRaii.Disabled(autoOpenCoffersSelectedItem.Value.IsNullOrEmpty()))
                            {
                                if (ImGui.Button("添加装备箱"))
                                {
                                    if (!Configuration.AutoOpenCoffersBlacklist.TryAdd(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value))
                                    {
                                        Configuration.AutoOpenCoffersBlacklist.Remove(autoOpenCoffersSelectedItem.Key);
                                        Configuration.AutoOpenCoffersBlacklist.Add(autoOpenCoffersSelectedItem.Key, autoOpenCoffersSelectedItem.Value);
                                    }
                                    autoOpenCoffersSelectedItem = new(0, "");
                                    Configuration.Save();
                                }
                            }
                            
                            if (!ImGui.BeginListBox("##CofferBlackList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.AutoOpenCoffersBlacklist.Count) + 5))) return;

                            foreach (var item in Configuration.AutoOpenCoffersBlacklist)
                            {
                                ImGui.Selectable($"{item.Value}");
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                {
                                    Configuration.AutoOpenCoffersBlacklist.Remove(item);
                                    Configuration.Save();
                                }
                            }
                            ImGui.EndListBox();
                        }
                        
                        ImGui.Unindent();
                    }
                }

                using (ImRaii.Disabled(!DiscardHelper_IPCSubscriber.IsEnabled))
                {
                    if (ImGui.Checkbox("丢弃物品", ref Configuration.DiscardItems))
                    {
                        Configuration.Save();
                    }
                }
                if (!DiscardHelper_IPCSubscriber.IsEnabled)
                {
                    if (Configuration.DiscardItems)
                    {
                        Configuration.DiscardItems = false;
                        Configuration.Save();
                    }
                    ImGui.SameLine();
                    ImGui.Text("* 丢弃物品需要DiscardHelper插件！");
                    ImGui.SameLine();
                    ImGui.Text("Get @ ");
                    ImGui.SameLine(0, 0);
                    ImGuiEx.TextCopy(ImGuiHelper.LinkColor, @"https://puni.sh/api/repository/vera");
                }


                ImGui.Columns(2, "##DesynthColumns");

                if (ImGui.Checkbox("自动分解", ref Configuration.autoDesynth))
                {
                    Configuration.AutoDesynth = Configuration.autoDesynth;
                    Configuration.Save();
                }
                ImGui.NextColumn();
                //ImGui.SameLine(0, 5);
                using (ImRaii.Disabled(!AutoRetainer_IPCSubscriber.IsEnabled))
                {
                    if (ImGui.Checkbox("自动筹备稀有品", ref Configuration.autoGCTurnin))
                    {
                        Configuration.AutoGCTurnin = Configuration.autoGCTurnin;
                        Configuration.Save();
                    }
                    
                    ImGui.NextColumn();

                    //slightly cursed
                    using (ImRaii.Enabled())
                    {
                        if (Configuration.AutoDesynth)
                        {
                            ImGui.Indent();
                            if (ImGui.Checkbox("仅分解可提升分解技能的装备", ref Configuration.autoDesynthSkillUp))
                            {
                                Configuration.AutoDesynthSkillUp = Configuration.autoDesynthSkillUp;
                                Configuration.Save();
                            }
                            if (Configuration.AutoDesynthSkillUp)
                            {
                                ImGui.Indent();
                                ImGui.Text("装等限制");
                                ImGuiComponents.HelpMarker("一旦你的分解技能达到此限制以上，就停止分解");
                                ImGui.SameLine();
                                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                if (ImGui.SliderInt("##AutoDesynthSkillUpLimit", ref Configuration.AutoDesynthSkillUpLimit, 0, 50))
                                {
                                    Configuration.AutoDesynthSkillUpLimit = Math.Clamp(Configuration.AutoDesynthSkillUpLimit, 0, 50);
                                    Configuration.Save();
                                }
                                ImGui.PopItemWidth();
                                ImGui.Unindent();
                            }
                            ImGui.Unindent();
                        }
                    }

                    if (Configuration.AutoGCTurnin)
                    {
                        ImGui.NextColumn();

                        ImGui.Indent();
                        if (ImGui.Checkbox("当背包空间小于 @", ref Configuration.AutoGCTurninSlotsLeftBool))
                            Configuration.Save();
                        ImGui.SameLine(0);
                        using (ImRaii.Disabled(!Configuration.AutoGCTurninSlotsLeftBool))
                        {
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (Configuration.UseSliderInputs)
                            {
                                if (ImGui.SliderInt("##Slots", ref Configuration.AutoGCTurninSlotsLeft, 0, 140))
                                {
                                    Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);
                                    Configuration.Save();
                                }
                            }
                            else
                            {
                                Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);

                                if (ImGui.InputInt("##Slots", ref Configuration.AutoGCTurninSlotsLeft))
                                {
                                    Configuration.AutoGCTurninSlotsLeft = Math.Clamp(Configuration.AutoGCTurninSlotsLeft, 0, 140);
                                    Configuration.Save();
                                }
                            }
                            ImGui.PopItemWidth();
                        }
                        if (ImGui.Checkbox("使用军队传送卷", ref Configuration.AutoGCTurninUseTicket)) 
                            Configuration.Save();
                        ImGui.Unindent();
                    }
                }
                ImGui.Columns(1);

                if (!AutoRetainer_IPCSubscriber.IsEnabled)
                {
                    if (Configuration.AutoGCTurnin)
                    {
                        Configuration.AutoGCTurnin = false;
                        Configuration.Save();
                    }
                    ImGui.Text("* 自动筹备稀有品需要 AutoRetainer 插件");
                    ImGui.Text("获取 @ ");
                    ImGui.SameLine(0, 0);
                    ImGuiEx.TextCopy(ImGuiHelper.LinkColor, @"https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                }

                if(ImGui.Checkbox("九宫幻卡", ref Configuration.TripleTriadEnabled))
                    Configuration.Save();
                ImGui.SameLine();
                ImGui.TextColored(Configuration.TripleTriadEnabled ? GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500) : ImGuiHelper.ExperimentalColor, "实验性功能");
                if (Configuration.TripleTriadEnabled)
                {
                    ImGui.Indent();
                    if (ImGui.Checkbox("使用幻卡", ref Configuration.TripleTriadRegister))
                        Configuration.Save();
                    if (ImGui.Checkbox("出售幻卡", ref Configuration.TripleTriadSell))
                        Configuration.Save();
                    ImGui.Unindent();
                }

                using (ImRaii.Disabled(!AutoRetainer_IPCSubscriber.IsEnabled))
                {
                    if (ImGui.Checkbox("启用 AutoRetainer 集成", ref Configuration.EnableAutoRetainer))
                        Configuration.Save();
                }
                if (Configuration.EnableAutoRetainer)
                {
                    ImGui.Text("首选雇员铃位置: ");
                    ImGuiComponents.HelpMarker("无论选择哪个位置，如果在触发时您所在的位置有雇员铃，它将优先前往该位置。");
                    if (ImGui.BeginCombo("##PreferredBell", Configuration.PreferredSummoningBellEnum.ToCustomString()))
                    {
                        foreach (SummoningBellLocations summoningBells in Enum.GetValues(typeof(SummoningBellLocations)))
                        {
                            if (ImGui.Selectable(summoningBells.ToCustomString()))
                            {
                                Configuration.PreferredSummoningBellEnum = summoningBells;
                                Configuration.Save();
                            }
                        }
                        ImGui.EndCombo();
                    }
                }
                if (!AutoRetainer_IPCSubscriber.IsEnabled)
                {
                    if (Configuration.EnableAutoRetainer)
                    {
                        Configuration.EnableAutoRetainer = false;
                        Configuration.Save();
                    }
                    ImGui.Text("* 此功能需要插件AutoRetainer");
                    ImGui.Text("如果本插件你使用的是正确的仓库，那你应该能够直接在插件管理器里搜索到AR");
                    ImGui.Text("否则，请添加: ");
                    ImGui.SameLine(0, 0);
                    ImGuiEx.TextCopy(ImGuiHelper.LinkColor, @"https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                }
            }
        }

        //Loop Termination Settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
        var terminationHeader = ImGui.Selectable("循环终止设置", terminationHeaderSelected, ImGuiSelectableFlags.DontClosePopups);
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (terminationHeader)
            terminationHeaderSelected = !terminationHeaderSelected;
        if (terminationHeaderSelected == true)
        {
            if (ImGui.Checkbox("开启###TerminationEnable", ref Configuration.EnableTerminationActions))
                Configuration.Save();

            using (ImRaii.Disabled(!Configuration.EnableTerminationActions))
            {
                ImGui.Separator();

                if (ImGui.Checkbox("达到指定等级时停止", ref Configuration.StopLevel))
                    Configuration.Save();

                if (Configuration.StopLevel)
                {
                    ImGui.SameLine(0, 10);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (Configuration.UseSliderInputs)
                    {
                        if (ImGui.SliderInt("##Level", ref Configuration.StopLevelInt, 1, 100))
                        {
                            Configuration.StopLevelInt = Math.Clamp(Configuration.StopLevelInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    else
                    {
                        if (ImGui.InputInt("##Level", ref Configuration.StopLevelInt))
                        {
                            Configuration.StopLevelInt = Math.Clamp(Configuration.StopLevelInt, 1, 100);
                            Configuration.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                }
                ImGuiComponents.HelpMarker("当满足这些条件时，循环将停止");
                if (ImGui.Checkbox("休息奖励经验耗尽时停止", ref Configuration.StopNoRestedXP))
                    Configuration.Save();

                ImGuiComponents.HelpMarker("当满足这些条件时，循环将停止");
                if (ImGui.Checkbox("物品到达指定数量时停止", ref Configuration.StopItemQty))
                    Configuration.Save();

                ImGuiComponents.HelpMarker("当满足这些条件时，循环将停止");
                if (Configuration.StopItemQty)
                {
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 125 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo("选择物品", stopItemQtySelectedItem.Value))
                    {
                        ImGui.InputTextWithHint("物品名称", "输入物品名称以进行搜索", ref stopItemQtyItemNameInput, 1000);
                        foreach (var item in Items.Where(x => x.Value.Name.ToString().Contains(stopItemQtyItemNameInput, StringComparison.InvariantCultureIgnoreCase))!)
                        {
                            if (ImGui.Selectable($"{item.Value.Name.ToString()}"))
                                stopItemQtySelectedItem = new KeyValuePair<uint, string>(item.Key, item.Value.Name.ToString());
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 220 * ImGuiHelpers.GlobalScale);
                    if (ImGui.InputInt("数量", ref Configuration.StopItemQtyInt))
                        Configuration.Save();

                    ImGui.SameLine(0, 5);
                    using (ImRaii.Disabled(stopItemQtySelectedItem.Value.IsNullOrEmpty()))
                    {
                        if (ImGui.Button("添加物品"))
                        {
                            if (!Configuration.StopItemQtyItemDictionary.TryAdd(stopItemQtySelectedItem.Key, new(stopItemQtySelectedItem.Value, Configuration.StopItemQtyInt)))
                            {
                                Configuration.StopItemQtyItemDictionary.Remove(stopItemQtySelectedItem.Key);
                                Configuration.StopItemQtyItemDictionary.Add(stopItemQtySelectedItem.Key, new(stopItemQtySelectedItem.Value, Configuration.StopItemQtyInt));
                            }
                            Configuration.Save();
                        }
                    }
                    ImGui.PopItemWidth();
                    if (!ImGui.BeginListBox("##ItemList", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * Configuration.StopItemQtyItemDictionary.Count) + 5))) return;

                    foreach (var item in Configuration.StopItemQtyItemDictionary)
                    {
                        ImGui.Selectable($"{item.Value.Key} (Qty: {item.Value.Value})");
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            Configuration.StopItemQtyItemDictionary.Remove(item);
                            Configuration.Save();
                        }
                    }
                    ImGui.EndListBox();
                    if (ImGui.Checkbox("仅在获得所有项目后停止循环", ref Configuration.StopItemAll))
                        Configuration.Save();
                }

                MakeCommands("在所有循环终止时执行命令",
                             ref Configuration.ExecuteCommandsTermination,  ref Configuration.CustomCommandsTermination, ref terminationCommand);

                if (ImGui.Checkbox("完成所有循环后播放声音： ", ref Configuration.PlayEndSound)) //Heavily Inspired by ChatAlerts
                        Configuration.Save();
                if (Configuration.PlayEndSound)
                {
                    if (ImGuiEx.IconButton(FontAwesomeIcon.Play, "##ConfigSoundTest", new Vector2(ImGui.GetItemRectSize().Y)))
                        SoundHelper.StartSound(Configuration.PlayEndSound, Configuration.CustomSound, Configuration.SoundEnum);
                    ImGui.SameLine();
                    DrawGameSound();
                }

                ImGui.Text("在完成所有循环后： ");
                ImGui.SameLine(0, 10);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##ConfigTerminationMethod", Configuration.TerminationMethodEnum.GetDescription()))
                {
                    foreach (TerminationMode terminationMode in Enum.GetValues(typeof(TerminationMode)))
                    {
                        if (terminationMode != TerminationMode.Kill_PC || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                            if (ImGui.Selectable(terminationMode.GetDescription()))
                            {
                                Configuration.TerminationMethodEnum = terminationMode;
                                Configuration.Save();
                            }
                    }
                    ImGui.EndCombo();
                }

                if (Configuration.TerminationMethodEnum is TerminationMode.Kill_Client or TerminationMode.Kill_PC or TerminationMode.Logout)
                {
                    ImGui.Indent();
                    if (ImGui.Checkbox("执行后保留终止选项 ", ref Configuration.TerminationKeepActive))
                        Configuration.Save();
                    ImGui.Unindent();
                }
            }
        }

        void MakeCommands(string checkbox, ref bool execute, ref List<string> commands, ref string curCommand)
        {
            if (ImGui.Checkbox($"{checkbox}{(execute ? ":" : string.Empty)} ", ref execute))
                Configuration.Save();

            ImGuiComponents.HelpMarker($"{checkbox}.\n例如, /echo 测试");

            if (execute)
            {
                ImGui.Indent();
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 185 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputTextWithHint($"##Commands{checkbox}", "输入以 / 开头的命令", ref curCommand, 500, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    if (!curCommand.IsNullOrEmpty() && curCommand[0] == '/' && (ImGui.IsKeyDown(ImGuiKey.Enter) || ImGui.IsKeyDown(ImGuiKey.KeypadEnter)))
                    {
                        Configuration.CustomCommandsPreLoop.Add(curCommand);
                        curCommand = string.Empty;
                        Configuration.Save();
                    }
                }
                ImGui.PopItemWidth();
                    
                ImGui.SameLine(0, 5);
                using (ImRaii.Disabled(curCommand.IsNullOrEmpty() || curCommand[0] != '/'))
                {
                    if (ImGui.Button($"添加命令##CommandButton{checkbox}"))
                    {
                        commands.Add(curCommand);
                        Configuration.Save();
                    }
                }
                if (!ImGui.BeginListBox($"##CommandList{checkbox}", new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, (ImGui.GetTextLineHeightWithSpacing() * commands.Count) + 5))) 
                    return;

                var removeItem = false;
                var removeAt   = 0;

                foreach (var item in commands.Select((Value, Index) => (Value, Index)))
                {
                    ImGui.Selectable($"{item.Value}##Selectable{checkbox}");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        removeItem = true;
                        removeAt   = item.Index;
                    }
                }
                if (removeItem)
                {
                    commands.RemoveAt(removeAt);
                    Configuration.Save();
                }
                ImGui.EndListBox();
                ImGui.Unindent();
            }
        }
    }

    private static void DrawGameSound()
    {
        ImGui.SameLine(0, 10);
        ImGui.PushItemWidth(150 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##ConfigEndSoundMethod", Configuration.SoundEnum.ToName()))
        {
            foreach (var sound in _validSounds)
            {
                if (ImGui.Selectable(sound.ToName()))
                {
                    Configuration.SoundEnum = sound;
                    UIGlobals.PlaySoundEffect((uint)sound);
                    Configuration.Save();
                }
            }
            ImGui.EndCombo();
        }
    }
}
