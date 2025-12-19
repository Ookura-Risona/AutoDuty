using System.Numerics;
using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface;
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

public sealed class MainWindow : Window, IDisposable
{
    internal static string CurrentTabName = "";

    private static bool _showPopup = false;
    private static bool _nestedPopup = false;
    private static string _popupText = "";
    private static string _popupTitle = "";
    private static string openTabName = "";

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
        openTabName = tabName;
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
            openTabName = "Info";

        foreach ((string name, Action function, Vector4? color, bool child) in tabs)
        {
            if (name.IsNullOrEmpty()) 
                continue;
            if (color != null) 
                ImGui.PushStyleColor(ImGuiCol.Tab, color.Value);
            
            if ((valid || name == "Info") && ImGui.BeginTabItem(name, openTabName == name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                if (color != null) 
                    ImGui.PopStyleColor();
                if (child) 
                    ImGui.BeginChild(name + "child");

                if(!valid)
                {
                    ImGui.NewLine();
                    ImGui.TextColored(EzColor.Red, "You need to do the basic setup below. Enjoy");
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

        if(DalamudInfoHelper.IsOnStaging())
        {
            ImGui.TextColored(GradientColor.Get(ImGuiHelper.ExperimentalColor, ImGuiHelper.ExperimentalColor2, 500), "NOT SUPPORTED ON STAGING.");
            ImGui.Text("请输入“/xlbranch”并选择“Release”，然后重新启动游戏。");

            if (!ImGui.CollapsingHeader("Use despite staging. Support will not be given##stagingHeader"))
                return;
        }

        EzTabBar("MainTab", null, openTabName, ImGuiTabBarFlags.None, tabList);
    }
}
