using System.Numerics;
using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.EzSharedDataManager;
using ECommons.Funding;
using ECommons.ImGuiMethods;
using ECommons.Schedulers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;

namespace AutoDuty.Windows;

using System;
using ECommons.Reflection;

public sealed class MainWindow : Window, IDisposable
{
    internal static string CurrentTabName = "";

    private static bool _showPopup = false;
    private static bool _nestedPopup = false;
    private static string _popupText = "";
    private static string _popupTitle = "";
    private static string openTabName = "";
    private static bool UseNewUi => AutoDuty.Configuration.UseNewUi;
    private const int MainTabIndex = 0;
    private const int BuildTabIndex = 1;
    private const int SettingsTabIndex = 3;
    private const int InfoTabIndex = 4;
    private static readonly Vector4 UiBackgroundTop = new(1f, 0.95f, 0.97f, 1f);
    private static readonly Vector4 UiBackgroundBottom = new(1f, 0.99f, 1f, 1f);
    private static readonly Vector4 UiCardTop = new(1f, 0.97f, 0.99f, 1f);
    private static readonly Vector4 UiCardBottom = new(1f, 0.93f, 0.96f, 1f);
    private static readonly Vector4 UiCardBorder = new(1f, 0.84f, 0.91f, 1f);
    private static readonly Vector4 UiNavTop = new(1f, 0.92f, 0.96f, 1f);
    private static readonly Vector4 UiNavBottom = new(0.98f, 0.84f, 0.91f, 1f);
    private static readonly Vector4 UiContentTop = new(1f, 0.99f, 1f, 1f);
    private static readonly Vector4 UiContentBottom = new(0.99f, 0.94f, 0.98f, 1f);
    private static readonly Vector4 UiAccent = new(0.95f, 0.43f, 0.62f, 1f);
    private static readonly Vector4 UiAccentSoft = new(0.99f, 0.84f, 0.9f, 1f);
    private static readonly Vector4 UiShadow = new(0f, 0f, 0f, 0.15f);
    private static readonly Vector4 UiShadowSoft = new(0f, 0f, 0f, 0.08f);
    private static readonly Vector4 UiHighlight = new(1f, 1f, 1f, 0.85f);

    public MainWindow() : base(
        $"AutoDuty v0.0.0.{Plugin.Version}###Autoduty")
    {
        this.SizeConstraints = new WindowSizeConstraints
                               {
                                   MinimumSize = new Vector2(10, 10),
                                   MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
                               };

        this.TitleBarButtons.Add(new TitleBarButton { Icon        = FontAwesomeIcon.Cog, IconOffset                         = new Vector2(1, 1), Click          = _ => OpenTab("Config") });
        this.TitleBarButtons.Add(new TitleBarButton { ShowTooltip = () => ImGui.SetTooltip("在Ko-fi上支持 erdelf"), Icon = FontAwesomeIcon.Heart, IconOffset = new Vector2(1, 1), Click = _ => GenericHelpers.ShellStart("https://ko-fi.com/erdelf") });
    }

    internal static void SetCurrentTabName(string tabName)
    {
        if (CurrentTabName != tabName)
            CurrentTabName = tabName;
    }

    internal static void OpenTab(string tabName)
    {
        openTabName = NormalizeTabName(tabName);
        _ = new TickScheduler(delegate
        {
            openTabName = "";
        }, 25);
    }

    public void Dispose()
    {
    }

    internal static void Start() => 
        ImGui.SameLine(0, 5);

    internal static void LoopsConfig()
    {
        using ImRaii.IEndObject _ = ImRaii.Disabled(ConfigurationMain.Instance.MultiBox && !ConfigurationMain.Instance.host);

        if ((AutoDuty.Configuration.UseSliderInputs  && ImGui.SliderInt("次", ref AutoDuty.Configuration.LoopTimes, 0, 100)) || 
            (!AutoDuty.Configuration.UseSliderInputs && ImGui.InputInt("次", ref AutoDuty.Configuration.LoopTimes, 1)))
        {
            if (AutoDuty.Configuration.AutoDutyModeEnum == AutoDutyMode.Playlist)
                Plugin.PlaylistCurrentEntry?.count = AutoDuty.Configuration.LoopTimes;

            Configuration.Save();
        }
    }

    internal static void StopResumePause()
    {
        using (ImRaii.Disabled(!Plugin.states.HasFlag(PluginState.Looping) && !Plugin.states.HasFlag(PluginState.Navigating) && RepairHelper.State != ActionState.Running && GotoHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GCTurninHelper.State != ActionState.Running && ExtractHelper.State != ActionState.Running && DesynthHelper.State != ActionState.Running))
        {
            if (ImGui.Button($"停止###Stop2"))
            {
                StopAndReset();
                return;
            }
            ImGui.SameLine(0, 5);
        }

        using (ImRaii.Disabled((!Plugin.states.HasFlag(PluginState.Looping) && !Plugin.states.HasFlag(PluginState.Navigating) && RepairHelper.State != ActionState.Running && GotoHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GCTurninHelper.State != ActionState.Running && ExtractHelper.State != ActionState.Running && DesynthHelper.State != ActionState.Running) || Plugin.CurrentTerritoryContent == null))
        {
            if (Plugin.Stage == Stage.Paused)
            {
                if (ImGui.Button("继续"))
                {
                    Plugin.taskManager.StepMode = false;
                    Plugin.Stage = Plugin.previousStage;
                    Plugin.states &= ~PluginState.Paused;
                }
            }
            else
            {
                if (ImGui.Button("暂停")) Plugin.Stage = Stage.Paused;
            }
        }
    }

    private static void StopAndReset()
    {
        Plugin.playlistIndex = 0;
        Plugin.Stage = Stage.Stopped;
    }

    internal static void GotoAndActions()
    {
        if(Plugin.states.HasFlag(PluginState.Other))
        {
            if(ImGui.Button("停止###Stop1"))
                StopAndReset();
            ImGui.SameLine(0,5);
        }

        using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Looping) || Plugin.states.HasFlag(PluginState.Navigating)))
        {
            using (ImRaii.Disabled(AutoDuty.Configuration is { OverrideOverlayButtons: true, GotoButton: false }))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("前往"))
                    {
                        ImGui.OpenPopup("GotoPopup");
                    }   
                }
            }

            if (ImGui.BeginPopup("GotoPopup"))
            {
                if (ImGui.Selectable("军营")) GotoBarracksHelper.Invoke();
                if (ImGui.Selectable("旅馆")) GotoInnHelper.Invoke();
                if (ImGui.Selectable("军票提交")) GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), [GCTurninHelper.GCSupplyLocation], 0.25f, 3f);
                if (ImGui.Selectable("旗标")) MapHelper.MoveToMapMarker();
                if (ImGui.Selectable("传唤铃")) SummoningBellHelper.Invoke(AutoDuty.Configuration.PreferredSummoningBellEnum);
                if (ImGui.Selectable("公寓")) GotoHousingHelper.Invoke(Housing.Apartment);
                if (ImGui.Selectable("个人房屋")) GotoHousingHelper.Invoke(Housing.Personal_Home);
                if (ImGui.Selectable("部队房屋")) GotoHousingHelper.Invoke(Housing.FC_Estate);

                if (ImGui.Selectable("幻卡回收")) GotoHelper.Invoke(TripleTriadCardSellHelper.GoldSaucerTerritoryType, TripleTriadCardSellHelper.TripleTriadCardVendorLocation);
                ImGui.EndPopup();
            }



            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoGCTurnin: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.TurninButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("军票"))
                    {
                        if (AutoRetainer_IPCSubscriber.IsEnabled)
                            GCTurninHelper.Invoke();
                        else
                            ShowPopup("缺少插件", "军队筹备需要 AutoRetainer 插件。获取 @ https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                    }
                    if (AutoRetainer_IPCSubscriber.IsEnabled)
                        ToolTip("点击前往调用 AutoRetainer 进行军队筹备");
                    else
                        ToolTip("军队筹备需要 AutoRetainer 插件。获取 @ https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                }
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoDesynth: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.DesynthButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("分解"))
                        DesynthHelper.Invoke();
                    ToolTip("Click to Desynth all Items in Inventory");
                    
                }
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoExtract: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.ExtractButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("精炼"))
                    {
                        if (QuestManager.IsQuestComplete(66174))
                            ExtractHelper.Invoke();
                        else
                            ShowPopup("缺少前置任务", "精炼需要完成任务: 情感培育之力");
                    }
                    if (QuestManager.IsQuestComplete(66174))
                        ToolTip("点击进行精炼");
                    else
                        ToolTip("精炼需要完成任务: 情感培育之力");
                }
            }
            
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoRepair: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.RepairButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("修理"))
                    {
                        if (InventoryHelper.CanRepair(100))
                            RepairHelper.Invoke();
                        //else
                            //ShowPopup("", "");
                    }
                    //if ()
                        ToolTip("点击修理装备");
                    //else
                        //ToolTip("");
                    
                }
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoEquipRecommendedGear: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.EquipButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("装备"))
                    {
                        AutoEquipHelper.Invoke();
                        //else
                        //ShowPopup("", "");
                    }

                    //if ()
                    ToolTip("点击装备装备");
                    //else
                    //ToolTip("");
                }
            }

            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(AutoDuty.Configuration is { AutoOpenCoffers: false, OverrideOverlayButtons: false } || !AutoDuty.Configuration.CofferButton))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("装备箱")) 
                        CofferHelper.Invoke();
                    ToolTip("点击开启装备箱");
                }
            }
            ImGui.SameLine(0, 5);

            using (ImRaii.Disabled(!(AutoDuty.Configuration.TripleTriadRegister || AutoDuty.Configuration.TripleTriadSell) && (!AutoDuty.Configuration.OverrideOverlayButtons || !AutoDuty.Configuration.TTButton)))
            {
                using (ImRaii.Disabled(Plugin.states.HasFlag(PluginState.Other)))
                {
                    if (ImGui.Button("幻卡"))
                        ImGui.OpenPopup("TTPopup");
                    
                }
            }

            if (ImGui.BeginPopup("TTPopup"))
            {
                if (ImGui.Selectable("使用幻卡"))
                    TripleTriadCardUseHelper.Invoke();
                if (ImGui.Selectable("出售幻卡")) 
                    TripleTriadCardSellHelper.Invoke();
                ImGui.EndPopup();
            }
        }
    }

    internal static void ToolTip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGuiEx.Text(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    internal static void ShowPopup(string popupTitle, string popupText, bool nested = false)
    {
        _popupTitle = popupTitle;
        _popupText = popupText;
        _showPopup = true;
        _nestedPopup = nested;
    }

    internal static void DrawPopup(bool nested = false)
    {
        if (!_showPopup || (_nestedPopup && !nested) || (!_nestedPopup && nested)) return;

        if (!ImGui.IsPopupOpen($"{_popupTitle}###Popup"))
            ImGui.OpenPopup($"{_popupTitle}###Popup");

        Vector2 textSize = ImGui.CalcTextSize(_popupText);
        ImGui.SetNextWindowSize(new Vector2(textSize.X + 25, textSize.Y + 100));
        if (ImGui.BeginPopupModal($"{_popupTitle}###Popup", ref _showPopup, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove))
        {
            ImGuiEx.TextCentered(_popupText);
            ImGui.Spacing();
            if (ImGuiHelper.CenteredButton("OK", .5f, 15))
            {
                _showPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private static void KofiLink()
    {
        OpenTab(CurrentTabName);
        if (EzThrottler.Throttle("KofiLink", 15000))
            _ = new TickScheduler(delegate
                                  {
                                      GenericHelpers.ShellStart("https://ko-fi.com/erdelf");
                                  }, 500);
    }

    //ECommons
    static uint ColorNormal
    {
        get
        {
            Vector4 vector1 = ImGuiEx.Vector4FromRGB(0x022594);
            Vector4 vector2 = ImGuiEx.Vector4FromRGB(0x940238);

            uint    gen                                                                       = GradientColor.Get(vector1, vector2).ToUint();
            uint[]? data                                                                      = EzSharedData.GetOrCreate<uint[]>("ECommonsPatreonBannerRandomColor", [gen]);
            if (!GradientColor.IsColorInRange(data[0].ToVector4(), vector1, vector2)) data[0] = gen;
            return data[0];
        }
    }

    public static void EzTabBar(string id, string? KoFiTransparent, string openTabName, ImGuiTabBarFlags flags, params (string name, Action function, Vector4? color, bool child)[] tabs)
    {
        ImGui.BeginTabBar(id, flags);


        bool valid = (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.UsingAlternativeBossPlugin)     &&
                     (VNavmesh_IPCSubscriber.IsEnabled || AutoDuty.Configuration.UsingAlternativeMovementPlugin) &&
                     (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.UsingAlternativeRotationPlugin);

        if (!valid)
            openTabName = "信息";

        foreach ((string name, Action function, Vector4? color, bool child) in tabs)
        {
            if (name.IsNullOrEmpty()) 
                continue;
            if (color != null) 
                ImGui.PushStyleColor(ImGuiCol.Tab, color.Value);
            
            if ((valid || name == "信息") && ImGui.BeginTabItem(name, openTabName == name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                if (color != null) 
                    ImGui.PopStyleColor();
                if (child) 
                    ImGui.BeginChild(name + "child");

                if(!valid)
                {
                    ImGui.NewLine();
                    ImGui.TextColored(EzColor.Red, "您需要下载缺少的前置插件");
                }

                function();

                if (child) 
                    ImGui.EndChild();
                ImGui.EndTabItem();
            }
            else
            {
                if (color != null) 
                    ImGui.PopStyleColor();
            }
        }
        if (KoFiTransparent != null) 
            PatreonBanner.RightTransparentTab();
        
        ImGui.EndTabBar();
    }

    private static readonly (string, Action, Vector4?, bool)[] tabList =
    [
        ("主界面", MainTab.Draw, null, false), 
        ("创建", BuildTab.Draw, null, false), 
        ("配置", PathsTab.Draw, null, false), 
        ("设置", ConfigTab.Draw, null, false), 
        ("信息", InfoTab.Draw, null, false), 
        ("日志", LogTab.Draw, null, false),
        ("支持AutoDuty", KofiLink, ImGui.ColorConvertU32ToFloat4(ColorNormal), false)
    ];

    public override void Draw()
    {
        DrawPopup();

        if(DalamudReflector.IsOnStaging())
        {
            ImGui.TextColored(GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500), "NOT SUPPORTED ON STAGING.");
            ImGui.Text("请输入“/xlbranch”并选择“Release”，然后重新启动游戏。");

            if (!ImGui.CollapsingHeader("Use despite staging. Support will not be given##stagingHeader"))
                return;
        }

        this.Flags = UseNewUi ? ImGuiWindowFlags.NoTitleBar : ImGuiWindowFlags.None;

        if (UseNewUi)
            DrawNewUi();
        else
            EzTabBar("MainTab", null, openTabName, ImGuiTabBarFlags.None, tabList);
    }

    private static string NormalizeTabName(string tabName)
    {
        if (tabName == "MainTab" && tabList.Length > MainTabIndex)
            return tabList[MainTabIndex].Item1;
        if (tabName == "BuildTab" && tabList.Length > BuildTabIndex)
            return tabList[BuildTabIndex].Item1;
        if (tabName == "Config" && tabList.Length > SettingsTabIndex)
            return tabList[SettingsTabIndex].Item1;

        return tabName;
    }

    private static void DrawNewUi()
    {
        int colorCount = 0;
        int varCount = 0;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(1f, 0.96f, 0.98f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 0.84f, 0.91f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 0.92f, 0.96f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 0.88f, 0.94f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(1f, 0.85f, 0.92f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.98f, 0.86f, 0.91f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.98f, 0.8f, 0.88f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.96f, 0.72f, 0.84f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.28f, 0.18f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.5f, 0.4f, 0.44f, 1f));
        colorCount += 12;

        ImGui.PushStyleColor(ImGuiCol.CheckMark, UiAccent);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(1f, 0.9f, 0.95f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 0.84f, 0.92f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.98f, 0.7f, 0.84f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(1f, 0.86f, 0.92f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, new Vector4(0.98f, 0.76f, 0.86f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SeparatorActive, UiAccent);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(1f, 0.95f, 0.97f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.97f, 0.78f, 0.87f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.97f, 0.7f, 0.84f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.95f, 0.58f, 0.77f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, new Vector4(0.97f, 0.74f, 0.86f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, new Vector4(0.95f, 0.58f, 0.77f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(0.99f, 0.85f, 0.92f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(0.97f, 0.74f, 0.86f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, new Vector4(0.95f, 0.58f, 0.77f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(1f, 0.9f, 0.95f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, new Vector4(0.98f, 0.76f, 0.86f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(1f, 0.88f, 0.93f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(1f, 0.98f, 0.99f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(1f, 0.94f, 0.98f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, new Vector4(0.98f, 0.7f, 0.84f, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, new Vector4(0.95f, 0.58f, 0.77f, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(1f, 0.93f, 0.97f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(1f, 0.86f, 0.93f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(1f, 0.82f, 0.9f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0.55f, 0.4f, 0.48f, 0.35f));
        colorCount += 28;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 8f) * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 6f) * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f * ImGuiHelpers.GlobalScale);
        varCount += 3;

        try
        {
            string normalizedOpenTab = NormalizeTabName(openTabName);
            if (!normalizedOpenTab.IsNullOrEmpty())
                CurrentTabName = normalizedOpenTab;

            if (CurrentTabName.IsNullOrEmpty() && tabList.Length > 0)
                CurrentTabName = tabList[0].Item1;

            CurrentTabName = NormalizeTabName(CurrentTabName);
            if (tabList.Length > 0)
            {
                bool hasTab = false;
                foreach ((string name, Action _, Vector4? _, bool _) in tabList)
                {
                    if (name == CurrentTabName)
                    {
                        hasTab = true;
                        break;
                    }
                }

                if (!hasTab)
                    CurrentTabName = tabList[0].Item1;
            }

            bool valid = (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.UsingAlternativeBossPlugin)     &&
                         (VNavmesh_IPCSubscriber.IsEnabled || AutoDuty.Configuration.UsingAlternativeMovementPlugin) &&
                         (BossMod_IPCSubscriber.IsEnabled  || AutoDuty.Configuration.UsingAlternativeRotationPlugin);

            string infoTabName = tabList.Length > InfoTabIndex ? tabList[InfoTabIndex].Item1 : "信息";
            if (!valid && CurrentTabName != infoTabName)
                CurrentTabName = infoTabName;

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 windowPos = ImGui.GetWindowPos();
            Vector2 windowSize = ImGui.GetWindowSize();

            DrawBackground(drawList, windowPos, windowSize);

            float scale = ImGuiHelpers.GlobalScale;
            float padding = 16f * scale;
            float gap = 12f * scale;
            float topBarHeight = 44f * scale;
            float navWidth = MathF.Min(240f * scale, MathF.Max(170f * scale, windowSize.X * 0.28f));
            float cardRounding = 16f * scale;

            Vector2 topBarMin = windowPos + new Vector2(padding, padding);
            Vector2 topBarMax = new Vector2(windowPos.X + windowSize.X - padding, topBarMin.Y + topBarHeight);
            DrawCard(drawList, topBarMin, topBarMax, cardRounding);

            ImGui.SetCursorScreenPos(topBarMin);
            ImGui.InvisibleButton("##NewUiDrag", topBarMax - topBarMin);
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);

            Vector2 titlePos = new(topBarMin.X + 16f * scale, topBarMin.Y + (topBarHeight - ImGui.GetFontSize()) / 2f);
            drawList.AddText(titlePos, ImGui.GetColorU32(ImGuiCol.Text), $"AutoDuty v{Plugin.Version}");

            float closeSize = 26f * scale;
            Vector2 closePos = new(topBarMax.X - closeSize - 12f * scale, topBarMin.Y + (topBarHeight - closeSize) / 2f);
            if (DrawCloseButton(drawList, closePos, closeSize))
                Plugin.MainWindow.IsOpen = false;

            float contentTop = topBarMax.Y + gap;
            Vector2 navMin = new(windowPos.X + padding, contentTop);
            Vector2 navMax = new(navMin.X + navWidth, windowPos.Y + windowSize.Y - padding);
            Vector2 contentMin = new(navMax.X + gap, contentTop);
            Vector2 contentMax = new(windowPos.X + windowSize.X - padding, navMax.Y);

            DrawCard(drawList, navMin, navMax, cardRounding, UiNavTop, UiNavBottom);
            DrawCard(drawList, contentMin, contentMax, cardRounding, UiContentTop, UiContentBottom);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.SetCursorScreenPos(navMin);
            ImGui.BeginChild("##NewUiNav", navMax - navMin, false, ImGuiWindowFlags.NoBackground);
            float navPadding = 12f * scale;
            float tabHeight = 42f * scale;
            ImGui.SetCursorPos(new Vector2(navPadding, navPadding));

            foreach ((string name, Action function, Vector4? _, bool _) in tabList)
            {
                if (name.IsNullOrEmpty())
                    continue;

                bool isSupport = function == KofiLink;
                bool isInfo = name == infoTabName;
                bool isDisabled = !valid && !isInfo && !isSupport;
                bool isActive = name == CurrentTabName && !isSupport;

                ImGui.SetCursorPosX(navPadding);
                bool clicked = DrawNavButton($"##NewUiTab_{name}", name, new Vector2(navWidth - navPadding * 2f, tabHeight), isActive, isDisabled);
                if (clicked && !isDisabled)
                {
                    if (isSupport)
                        KofiLink();
                    else
                        CurrentTabName = name;
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 16f) * scale);
            ImGui.SetCursorScreenPos(contentMin);
            ImGui.BeginChild("##NewUiContent", contentMax - contentMin, false, ImGuiWindowFlags.NoBackground);

            if (!valid)
            {
                ImGui.NewLine();
                ImGui.TextColored(EzColor.Red, "您需要下载缺少的前置插件");
                ImGui.Spacing();
            }

            foreach ((string name, Action function, Vector4? _, bool _) in tabList)
            {
                if (name == CurrentTabName)
                {
                    function();
                    break;
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        finally
        {
            ImGui.PopStyleVar(varCount);
            ImGui.PopStyleColor(colorCount);
        }
    }

    private static void DrawBackground(ImDrawListPtr drawList, Vector2 windowPos, Vector2 windowSize)
    {
        uint topLeft = ImGui.GetColorU32(UiBackgroundTop);
        uint topRight = ImGui.GetColorU32(new Vector4(1f, 0.96f, 0.98f, 1f));
        uint bottomRight = ImGui.GetColorU32(UiBackgroundBottom);
        uint bottomLeft = ImGui.GetColorU32(new Vector4(1f, 0.98f, 0.99f, 1f));
        drawList.AddRectFilledMultiColor(windowPos, windowPos + windowSize, topLeft, topRight, bottomRight, bottomLeft);

        float radius = MathF.Min(windowSize.X, windowSize.Y) * 0.45f;
        Vector2 glowA = windowPos + new Vector2(windowSize.X * 0.2f, windowSize.Y * 0.2f);
        Vector2 glowB = windowPos + new Vector2(windowSize.X * 0.85f, windowSize.Y * 0.7f);
        drawList.AddCircleFilled(glowA, radius, ImGui.GetColorU32(new Vector4(1f, 0.88f, 0.94f, 0.35f)), 64);
        drawList.AddCircleFilled(glowB, radius * 0.7f, ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.99f, 0.4f)), 64);
    }

    private static void DrawCard(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding)
    {
        DrawCard(drawList, min, max, rounding, UiCardTop, UiCardBottom);
    }

    private static void DrawCard(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, Vector4 topColor, Vector4 bottomColor)
    {
        float scale = ImGuiHelpers.GlobalScale;
        float shadowSpread = 12f * scale;
        DrawRingShadow(drawList, min, max, rounding, shadowSpread);

        uint cardTop = ImGui.GetColorU32(topColor);
        uint cardBottom = ImGui.GetColorU32(bottomColor);
        drawList.AddRectFilledMultiColor(min, max, cardTop, cardTop, cardBottom, cardBottom);

        float highlightHeight = MathF.Min(12f * scale, (max.Y - min.Y) * 0.25f);
        drawList.AddRectFilledMultiColor(
            new Vector2(min.X + 1f, min.Y + 1f),
            new Vector2(max.X - 1f, min.Y + highlightHeight),
            ImGui.GetColorU32(UiHighlight),
            ImGui.GetColorU32(UiHighlight),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));

        drawList.AddRect(min, max, ImGui.GetColorU32(UiCardBorder), rounding, ImDrawFlags.RoundCornersAll, 1f);
    }

    private static void DrawRingShadow(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding, float spread)
    {
        uint shadow = ImGui.GetColorU32(UiShadow);
        uint shadowSoft = ImGui.GetColorU32(UiShadowSoft);
        uint transparent = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f));

        Vector2 leftMin = new(min.X - spread, min.Y + rounding);
        Vector2 leftMax = new(min.X, max.Y + spread);
        drawList.AddRectFilledMultiColor(leftMin, leftMax, shadowSoft, transparent, transparent, shadowSoft);

        Vector2 rightMin = new(max.X, min.Y + rounding);
        Vector2 rightMax = new(max.X + spread, max.Y + spread);
        drawList.AddRectFilledMultiColor(rightMin, rightMax, transparent, shadowSoft, shadowSoft, transparent);

        Vector2 bottomMin = new(min.X + rounding, max.Y);
        Vector2 bottomMax = new(max.X - rounding, max.Y + spread);
        drawList.AddRectFilledMultiColor(bottomMin, bottomMax, transparent, transparent, shadow, shadow);
    }

    private static bool DrawNavButton(string id, string label, Vector2 size, bool active, bool disabled)
    {
        ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float rounding = 12f * ImGuiHelpers.GlobalScale;

        Vector4 baseColor = active ? UiAccentSoft : new Vector4(1f, 0.95f, 0.97f, 1f);
        Vector4 borderColor = active ? UiAccent : UiCardBorder;
        if (hovered && !disabled)
            baseColor = new Vector4(1f, 0.91f, 0.95f, 1f);
        if (disabled)
            baseColor = new Vector4(1f, 0.96f, 0.98f, 0.6f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(baseColor), rounding);
        drawList.AddRect(min, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.RoundCornersAll, 1f);

        if (active && !disabled)
        {
            float accentWidth = 4f * ImGuiHelpers.GlobalScale;
            Vector2 accentMin = new(min.X + 6f * ImGuiHelpers.GlobalScale, min.Y + 6f * ImGuiHelpers.GlobalScale);
            Vector2 accentMax = new(accentMin.X + accentWidth, max.Y - 6f * ImGuiHelpers.GlobalScale);
            drawList.AddRectFilled(accentMin, accentMax, ImGui.GetColorU32(UiAccent), accentWidth * 0.5f);
        }

        Vector2 textSize = ImGui.CalcTextSize(label);
        Vector2 textPos = new(min.X + 16f * ImGuiHelpers.GlobalScale, min.Y + (size.Y - textSize.Y) * 0.5f);
        uint textColor = ImGui.GetColorU32(disabled ? ImGuiCol.TextDisabled : ImGuiCol.Text);
        drawList.AddText(textPos, textColor, label);

        if (hovered && !disabled)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked && !disabled;
    }

    private static bool DrawCloseButton(ImDrawListPtr drawList, Vector2 pos, float size)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##NewUiClose", new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked();

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float rounding = size * 0.35f;

        Vector4 baseColor = hovered ? new Vector4(0.98f, 0.76f, 0.85f, 1f) : new Vector4(0.99f, 0.86f, 0.92f, 1f);
        Vector4 borderColor = hovered ? UiAccent : UiCardBorder;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(baseColor), rounding);
        drawList.AddRect(min, max, ImGui.GetColorU32(borderColor), rounding, ImDrawFlags.RoundCornersAll, 1f);

        float pad = size * 0.3f;
        uint xColor = ImGui.GetColorU32(new Vector4(0.5f, 0.2f, 0.3f, 1f));
        drawList.AddLine(new Vector2(min.X + pad, min.Y + pad), new Vector2(max.X - pad, max.Y - pad), xColor, 2f);
        drawList.AddLine(new Vector2(min.X + pad, max.Y - pad), new Vector2(max.X - pad, min.Y + pad), xColor, 2f);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return clicked;
    }
}
