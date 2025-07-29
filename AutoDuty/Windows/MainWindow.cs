﻿using System;
using System.Collections.Generic;
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
using ImGuiNET;

namespace AutoDuty.Windows;

public class MainWindow : Window, IDisposable
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
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(10, 10),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        
        TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.Cog, IconOffset = new(1, 1), Click = _ => OpenTab("Config") });
        TitleBarButtons.Add(new() { ShowTooltip = () => ImGui.SetTooltip("在Ko-fi上支持Herculezz"), Icon = FontAwesomeIcon.Heart, IconOffset = new(1, 1), Click = _ => GenericHelpers.ShellStart("https://ko-fi.com/Herculezz") });
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

    internal static void Start()
    {
        ImGui.SameLine(0, 5);
    }

    internal static void LoopsConfig()
    {
        if ((Plugin.Configuration.UseSliderInputs && ImGui.SliderInt("次", ref Plugin.Configuration.LoopTimes, 0, 100)) || (!Plugin.Configuration.UseSliderInputs && ImGui.InputInt("次", ref Plugin.Configuration.LoopTimes)))
            Plugin.Configuration.Save();
    }

    internal static void StopResumePause()
    {
        using (ImRaii.Disabled(!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.States.HasFlag(PluginState.Navigating) && RepairHelper.State != ActionState.Running && GotoHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GCTurninHelper.State != ActionState.Running && ExtractHelper.State != ActionState.Running && DesynthHelper.State != ActionState.Running))
        {
            if (ImGui.Button("停止"))
            {
                Plugin.Stage = Stage.Stopped;
                return;
            }
            ImGui.SameLine(0, 5);
        }

        using (ImRaii.Disabled((!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.States.HasFlag(PluginState.Navigating) && RepairHelper.State != ActionState.Running && GotoHelper.State != ActionState.Running && GotoInnHelper.State != ActionState.Running && GotoBarracksHelper.State != ActionState.Running && GCTurninHelper.State != ActionState.Running && ExtractHelper.State != ActionState.Running && DesynthHelper.State != ActionState.Running) || Plugin.CurrentTerritoryContent == null))
        {
            if (Plugin.Stage == Stage.Paused)
            {
                if (ImGui.Button("继续"))
                {
                    Plugin.TaskManager.SetStepMode(false);
                    Plugin.Stage = Plugin.PreviousStage;
                    Plugin.States &= ~PluginState.Paused;
                }
            }
            else
            {
                if (ImGui.Button("暂停"))
                {
                    Plugin.Stage = Stage.Paused;
                }
            }
        }
    }

    internal static void GotoAndActions()
    {
        if(Plugin.States.HasFlag(PluginState.Other))
        {
            if(ImGui.Button("停止"))
                Plugin.Stage = Stage.Stopped;
            ImGui.SameLine(0,5);
        }

        using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping) || Plugin.States.HasFlag(PluginState.Navigating)))
        {
            using (ImRaii.Disabled(Plugin.Configuration is { OverrideOverlayButtons: true, GotoButton: false }))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && GotoHelper.State != ActionState.Running))
                {
                    if ((GotoHelper.State == ActionState.Running && GCTurninHelper.State != ActionState.Running && RepairHelper.State != ActionState.Running) || MapHelper.State == ActionState.Running || GotoHousingHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
                    {
                        if (ImGui.Button("前往"))
                        {
                            ImGui.OpenPopup("GotoPopup");
                        }
                    }
                }
            }

            if (ImGui.BeginPopup("GotoPopup"))
            {
                if (ImGui.Selectable("军营"))
                {
                    GotoBarracksHelper.Invoke();
                }
                if (ImGui.Selectable("旅馆"))
                {
                    GotoInnHelper.Invoke();
                }
                if (ImGui.Selectable("军票提交"))
                {
                    GotoHelper.Invoke(PlayerHelper.GetGrandCompanyTerritoryType(PlayerHelper.GetGrandCompany()), [GCTurninHelper.GCSupplyLocation], 0.25f, 3f);
                }
                if (ImGui.Selectable("旗帜"))
                {
                    MapHelper.MoveToMapMarker();
                }
                if (ImGui.Selectable("传唤铃"))
                {
                    SummoningBellHelper.Invoke(Plugin.Configuration.PreferredSummoningBellEnum);
                }
                if (ImGui.Selectable("公寓"))
                {
                    GotoHousingHelper.Invoke(Housing.Apartment);
                }
                if (ImGui.Selectable("个人房屋"))
                {
                    GotoHousingHelper.Invoke(Housing.Personal_Home);
                }
                if (ImGui.Selectable("部队房屋"))
                {
                    GotoHousingHelper.Invoke(Housing.FC_Estate);
                }

                if (ImGui.Selectable("Triple Triad Trader"))
                {
                    GotoHelper.Invoke(TripleTriadCardSellHelper.GoldSaucerTerritoryType, TripleTriadCardSellHelper.TripleTriadCardVendorLocation);
                }
                ImGui.EndPopup();
            }



            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(!Plugin.Configuration.AutoGCTurnin && !Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.TurninButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && GCTurninHelper.State != ActionState.Running))
                {
                    if (GCTurninHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
                    {
                        if (ImGui.Button("军票"))
                        {
                            if (AutoRetainer_IPCSubscriber.IsEnabled)
                                GCTurninHelper.Invoke();
                            else
                                ShowPopup("缺少插件", "军队筹备需要 AutoRetainer 插件。获取 @ https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                        }
                        if (AutoRetainer_IPCSubscriber.IsEnabled)
                            ToolTip("点击前往调用AutoRetainer进行军队筹备");
                        else
                            ToolTip("军队筹备需要 AutoRetainer 插件。获取 @ https://raw.githubusercontent.com/Ookura-Risona/DalamudPlugins/main/pluginmaster.json");
                    }
                }
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(!Plugin.Configuration.AutoDesynth && !Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.DesynthButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && DesynthHelper.State != ActionState.Running))
                {
                    if (DesynthHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
                    {
                        if (ImGui.Button("分解"))
                            DesynthHelper.Invoke();
                        ToolTip("Click to Desynth all Items in Inventory");
                    }
                }
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(!Plugin.Configuration.AutoExtract && !Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.ExtractButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && ExtractHelper.State != ActionState.Running))
                {
                    if (ExtractHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
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
            }
            
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(!Plugin.Configuration.AutoRepair && !Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.RepairButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && RepairHelper.State != ActionState.Running))
                {
                    if (RepairHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
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
            }
            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(!Plugin.Configuration.AutoEquipRecommendedGear && !Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.EquipButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && AutoEquipHelper.State != ActionState.Running))
                {
                    if (AutoEquipHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
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
            }

            ImGui.SameLine(0, 5);
            using (ImRaii.Disabled(Plugin.Configuration is { AutoOpenCoffers: false, OverrideOverlayButtons: false } || !Plugin.Configuration.CofferButton))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other) && CofferHelper.State != ActionState.Running))
                {
                    if (CofferHelper.State == ActionState.Running)
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
                    {
                        if (ImGui.Button("装备箱")) 
                            CofferHelper.Invoke();
                        ToolTip("点击开启装备箱");
                    }
                }
            }
            ImGui.SameLine(0, 5);

            using (ImRaii.Disabled(!Plugin.Configuration.TripleTriadEnabled && (!Plugin.Configuration.OverrideOverlayButtons || !Plugin.Configuration.TTButton)))
            {
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Other)))
                {
                    if ((GotoHelper.State == ActionState.Running && TripleTriadCardUseHelper.State != ActionState.Running && TripleTriadCardSellHelper.State != ActionState.Running))
                    {
                        if (ImGui.Button("停止"))
                            Plugin.Stage = Stage.Stopped;
                    }
                    else
                    {
                        if (ImGui.Button("幻卡"))
                            ImGui.OpenPopup("TTPopup");
                    }
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
        ImGui.SetNextWindowSize(new(textSize.X + 25, textSize.Y + 100));
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
        {
            _ = new TickScheduler(delegate
            {
                GenericHelpers.ShellStart("https://ko-fi.com/Herculezz");
            }, 500);
        }
    }

    //ECommons
    static uint ColorNormal
    {
        get
        {
            var vector1 = ImGuiEx.Vector4FromRGB(0x022594);
            var vector2 = ImGuiEx.Vector4FromRGB(0x940238);

            var gen = GradientColor.Get(vector1, vector2).ToUint();
            var data = EzSharedData.GetOrCreate<uint[]>("ECommonsPatreonBannerRandomColor", [gen]);
            if (!GradientColor.IsColorInRange(data[0].ToVector4(), vector1, vector2))
            {
                data[0] = gen;
            }
            return data[0];
        }
    }
    public static void EzTabBar(string id, string? KoFiTransparent, string openTabName, ImGuiTabBarFlags flags, params (string name, Action function, Vector4? color, bool child)[] tabs)
    {
        ImGui.BeginTabBar(id, flags);
        foreach (var x in tabs)
        {
            if (x.name == null) continue;
            if (x.color != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Tab, x.color.Value);
            }
            if (ImGuiEx.BeginTabItem(x.name, openTabName == x.name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                if (x.color != null) 
                    ImGui.PopStyleColor();
                if (x.child) 
                    ImGui.BeginChild(x.name + "child");
                x.function();
                if (x.child) 
                    ImGui.EndChild();
                ImGui.EndTabItem();
            }
            else
            {
                if (x.color != null)
                {
                    ImGui.PopStyleColor();
                }
            }
        }
        if (KoFiTransparent != null) PatreonBanner.RightTransparentTab();
        ImGui.EndTabBar();
    }

    private static readonly List<(string, Action, Vector4?, bool)> tabList =
        [("主界面", MainTab.Draw, null, false), ("创建", BuildTab.Draw, null, false), ("配置", PathsTab.Draw, null, false), ("设置", ConfigTab.Draw, null, false), ("信息", InfoTab.Draw, null, false), ("日志", LogTab.Draw, null, false),("支持AutoDuty", KofiLink, ImGui.ColorConvertU32ToFloat4(ColorNormal), false)
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

        EzTabBar("MainTab", null, openTabName, ImGuiTabBarFlags.None, tabList.ToArray());
    }
}
