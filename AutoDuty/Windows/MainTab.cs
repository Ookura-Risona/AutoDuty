using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AutoDuty.Windows
{
    using Data;
    using static Data.Classes;
    internal static class MainTab
    {
        internal static ContentPathsManager.ContentPathContainer? DutySelected;
        internal static readonly (string Normal, string GameFont) Digits = ("0123456789", "");

        private static int _currentStepIndex = -1;
        private static readonly string _pathsURL = "https://github.com/ffxivcode/AutoDuty/tree/master/AutoDuty/Paths";

        // New search text field for filtering duties
        private static string _searchText = string.Empty;

        internal static void Draw()
        {
            if (MainWindow.CurrentTabName != "主界面")
                MainWindow.CurrentTabName = "主界面";
            var dutyMode = Plugin.Configuration.DutyModeEnum;
            var levelingMode = Plugin.LevelingModeEnum;

            static void DrawSearchBar()
            {
                // Set the maximum search to 10 characters
                uint inputMaxLength = 10;
                
                // Calculate the X width of the maximum amount of search characters
                Vector2 _characterWidth = ImGui.CalcTextSize("W");
                float inputMaxWidth = ImGui.CalcTextSize("W").X * inputMaxLength;
                
                // Set the width of the search box to the calculated width
                ImGui.SetNextItemWidth(inputMaxWidth);
                
                ImGui.InputTextWithHint("##search", "搜索副本...", ref _searchText, inputMaxLength);

                // Apply filtering based on the search text
                if (_searchText.Length > 0)
                {
                    // Trim and convert to lowercase for case-insensitive search
                    _searchText = _searchText.Trim().ToLower();
                }
            }

            static void DrawPathSelection()
            {
                if (Plugin.CurrentTerritoryContent == null || !PlayerHelper.IsReady)
                    return;

                using var d = ImRaii.Disabled(Plugin is { InDungeon: true, Stage: > 0 });

                if (ContentPathsManager.DictionaryPaths.TryGetValue(Plugin.CurrentTerritoryContent.TerritoryType, out var container))
                {
                    List<ContentPathsManager.DutyPath> curPaths = container.Paths;
                    if (curPaths.Count > 1)
                    {
                        int                              curPath       = Math.Clamp(Plugin.CurrentPath, 0, curPaths.Count - 1);

                        Dictionary<string, JobWithRole>? pathSelection    = null;
                        JobWithRole                      curJob = Svc.ClientState.LocalPlayer.GetJob().JobToJobWithRole();
                        using (ImRaii.Disabled(curPath <= 0 ||
                                               !Plugin.Configuration.PathSelectionsByPath.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) || 
                                               !(pathSelection = Plugin.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]).Any(kvp => kvp.Value.HasJob(Svc.ClientState.LocalPlayer.GetJob()))))
                        {
                            if (ImGui.Button("清除已保存的路径"))
                            {
                                foreach (KeyValuePair<string, JobWithRole> keyValuePair in pathSelection) 
                                    pathSelection[keyValuePair.Key] &= ~curJob;

                                PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);
                                Plugin.Configuration.Save();
                                if (!Plugin.InDungeon)
                                    container.SelectPath(out Plugin.CurrentPath);
                            }
                        }
                        ImGui.SameLine();
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.BeginCombo("##SelectedPath", curPaths[curPath].Name))
                        {
                            foreach ((ContentPathsManager.DutyPath Value, int Index) path in curPaths.Select((value, index) => (Value: value, Index: index)))
                            {
                                if (ImGui.Selectable(path.Value.Name))
                                {
                                    curPath = path.Index;
                                    PathSelectionHelper.AddPathSelectionEntry(Plugin.CurrentTerritoryContent!.TerritoryType);
                                    Dictionary<string, JobWithRole> pathJobs = Plugin.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]!;
                                    pathJobs.TryAdd(path.Value.FileName, JobWithRole.None);
                                    
                                    foreach (string jobsKey in pathJobs.Keys) 
                                        pathJobs[jobsKey] &= ~curJob;

                                    pathJobs[path.Value.FileName] |= curJob;

                                    PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);

                                    Plugin.Configuration.Save();
                                    Plugin.CurrentPath = curPath;
                                    Plugin.LoadPath();
                                }
                                if (ImGui.IsItemHovered() && !path.Value.PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                                    ImGui.SetTooltip(string.Join("\n", path.Value.PathFile.Meta.Notes));
                            }
                            ImGui.EndCombo();
                        }
                        ImGui.PopItemWidth();
                        
                        if (ImGui.IsItemHovered() && !curPaths[curPath].PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                            ImGui.SetTooltip(string.Join("\n", curPaths[curPath].PathFile.Meta.Notes));
                        
                    }
                }
            }

            if (Plugin.InDungeon)
            {
                if (Plugin.CurrentTerritoryContent == null)
                    Plugin.LoadPath();
                else
                {
                    ImGui.AlignTextToFramePadding();
                    var progress = VNavmesh_IPCSubscriber.IsEnabled ? VNavmesh_IPCSubscriber.Nav_BuildProgress() : 0;
                    if (progress >= 0)
                    {
                        ImGui.Text($"{Plugin.CurrentTerritoryContent.Name} Mesh: Loading: ");
                        ImGui.SameLine();
                        ImGui.ProgressBar(progress, new Vector2(200, 0));
                    }
                    else
                        ImGui.Text($"{Plugin.CurrentTerritoryContent.Name} Mesh: Loaded Path: {(ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ? "Loaded" : "None")}");

                    ImGui.Separator();
                    ImGui.Spacing();

                    if (dutyMode == DutyMode.Trust && Plugin.CurrentTerritoryContent != null)
                    {
                        ImGui.Columns(3);
                        using (ImRaii.Disabled()) 
                            DrawTrustMembers(Plugin.CurrentTerritoryContent);
                        ImGui.Columns(1);
                        ImGui.Spacing();
                    }

                    DrawPathSelection();
                    if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                        MainWindow.GotoAndActions();
                    using (ImRaii.Disabled(!VNavmesh_IPCSubscriber.IsEnabled || !Plugin.InDungeon || !VNavmesh_IPCSubscriber.Nav_IsReady() || !BossMod_IPCSubscriber.IsEnabled))
                    {
                        using (ImRaii.Disabled(!Plugin.InDungeon || !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType)))
                        {
                            if (Plugin.Stage == 0)
                            {
                                if (ImGui.Button("启动"))
                                {
                                    Plugin.LoadPath();
                                    _currentStepIndex = -1;
                                    if (Plugin.MainListClicked)
                                        Plugin.Run(Svc.ClientState.TerritoryType, 0, !Plugin.MainListClicked);
                                    else
                                        Plugin.Run(Svc.ClientState.TerritoryType);
                                }
                            }
                            else
                                MainWindow.StopResumePause();
                            ImGui.SameLine(0, 15);
                        }
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        MainWindow.LoopsConfig();
                        ImGui.PopItemWidth();

                        if (!ImGui.BeginListBox("##MainList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                        if ((VNavmesh_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeMovementPlugin) && (BossMod_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeBossPlugin) && (ReflectionHelper.RotationSolver_Reflection.RotationSolverEnabled || BossMod_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeRotationPlugin))
                        {
                            foreach (var item in Plugin.Actions.Select((Value, Index) => (Value, Index)))
                            {
                                item.Value.DrawCustomText(item.Index, () => ItemClicked(item));
                                //var text = item.Value.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? item.Value.Note : $"{item.Value.ToCustomString()}";
                                ////////////////////////////////////////////////////////////////
                            }
                            if (_currentStepIndex != Plugin.Indexer && _currentStepIndex > -1 && Plugin.Stage > 0)
                            {
                                var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                                _currentStepIndex = Plugin.Indexer;
                                if (_currentStepIndex > 1)
                                    ImGui.SetScrollY((_currentStepIndex - 1) * lineHeight);
                            }
                            else if (_currentStepIndex == -1 && Plugin.Stage > 0)
                            {
                                _currentStepIndex = 0;
                                ImGui.SetScrollY(_currentStepIndex);
                            }
                            if (Plugin.InDungeon && Plugin.Actions.Count < 1 && !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                                ImGui.TextColored(new Vector4(0, 255, 0, 1), $"未找到副本配置文件:\n{TerritoryName.GetTerritoryName(Plugin.CurrentTerritoryContent.TerritoryType).Split('|')[1].Trim()}\n({Plugin.CurrentTerritoryContent.TerritoryType}.json)\n于路径文件夹:\n{Plugin.PathsDirectory.FullName.Replace('\\', '/')}\n请从以下链接下载:\n{_pathsURL}\n或在自行创建配置文件");
                        }
                        else
                        {
                            if (!VNavmesh_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeMovementPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安装并加载 VNavmesh 插件\n请添加第三方仓库：\nhttps://raw.githubusercontent.com/AtmoOmen/DalamudPlugins/main/pluginmaster.json");
                            if (!BossMod_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeBossPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安装并加载 BossMod 插件以正确处理机制。请添加第三方仓库：\nhttps://raw.githubusercontent.com/44451516/ffxiv_bossmod/CN/pluginmaster.json");
                            if (!Wrath_IPCSubscriber.IsEnabled && !ReflectionHelper.RotationSolver_Reflection.RotationSolverEnabled && !BossMod_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeRotationPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安装并加载一个循环插件（Wrath Combo 、 Rotation Solver Reborn 或 BossMod AutoRotation 均可)");
                        }
                        ImGui.EndListBox();
                    }
                }
            }
            else
            {
                if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();

                using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null || (Plugin.Configuration.DutyModeEnum == DutyMode.Trust && Plugin.Configuration.SelectedTrustMembers.Any(x => x is null))))
                {
                    if (!Plugin.States.HasFlag(PluginState.Looping))
                    {
                        if (ImGui.Button("运行"))
                        {
                            if (Plugin.Configuration.DutyModeEnum == DutyMode.None)
                                MainWindow.ShowPopup("Error", "你必须选择一个运行模式");
                            else if (Svc.Party.PartyId > 0 && (Plugin.Configuration.DutyModeEnum == DutyMode.Support || Plugin.Configuration.DutyModeEnum == DutyMode.Squadron || Plugin.Configuration.DutyModeEnum == DutyMode.Trust))
                                MainWindow.ShowPopup("Error", "组队状态中无法使用剧情辅助器、冒险者小队与亲信战友模式");
                            else if (Plugin.Configuration.DutyModeEnum == DutyMode.Regular && !Plugin.Configuration.Unsynced && !Plugin.Configuration.OverridePartyValidation && Svc.Party.PartyId == 0)
                                MainWindow.ShowPopup("Error", "你需要组成四人小队");
                            else if (Plugin.Configuration.DutyModeEnum == DutyMode.Regular && !Plugin.Configuration.Unsynced && !Plugin.Configuration.OverridePartyValidation && !ObjectHelper.PartyValidation())
                                MainWindow.ShowPopup("Error", "您必须拥有正确的小队配置");
                            else if (ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent?.TerritoryType ?? 0))
                                Plugin.Run();
                            else
                                MainWindow.ShowPopup("Error", "未找到配置文件");
                        }
                    }
                    else
                        MainWindow.StopResumePause();
                }
                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping)))
                {
                    using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                    {
                        ImGui.SameLine(0, 15);
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        MainWindow.LoopsConfig();
                        ImGui.PopItemWidth();
                    }
                    ImGui.TextColored(Plugin.Configuration.DutyModeEnum == DutyMode.None ? new Vector4(1, 0, 0, 1) : new Vector4(0, 1, 0, 1), "选择运行模式: ");
                    ImGui.SameLine(0);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##DutyModeEnum", Plugin.Configuration.DutyModeEnum.GetDescription()))
                    {
                        foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                        {
                            if (ImGui.Selectable(mode.GetDescription()))
                            {
                                Plugin.Configuration.DutyModeEnum = mode;
                                Plugin.Configuration.Save();
                            }
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                    if (Plugin.Configuration.DutyModeEnum != DutyMode.None)
                    {
                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Support || Plugin.Configuration.DutyModeEnum == DutyMode.Trust)
                        {
                            ImGui.TextColored(Plugin.LevelingModeEnum == LevelingMode.None ? new Vector4(1, 0, 0, 1) : new Vector4(0, 1, 0, 1), "选择练级模式: ");
                            ImGui.SameLine(0);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginCombo("##LevelingModeEnum", Plugin.LevelingModeEnum == LevelingMode.None ? "关闭" : "自动"))
                            {
                                if (ImGui.Selectable("关闭"))
                                {
                                    Plugin.LevelingModeEnum = LevelingMode.None;
                                    Plugin.Configuration.Save();
                                }
                                if (ImGui.Selectable("自动"))
                                {
                                    Plugin.LevelingModeEnum = Plugin.Configuration.DutyModeEnum == DutyMode.Support ? LevelingMode.Support : LevelingMode.Trust;
                                    Plugin.Configuration.Save();
                                    if (Plugin.Configuration.AutoEquipRecommendedGear)
                                        AutoEquipHelper.Invoke();
                                }
                                ImGui.EndCombo();
                            }
                            ImGui.PopItemWidth();

                            if (Plugin.Configuration.DutyModeEnum != DutyMode.Trust) 
                                ImGuiComponents.HelpMarker("升级模式将根据您的等级和装备等级（Ilvl）选择最合适的副本。\n将不会总是排入最高等级的副本，而是根据我们的稳定副本列表进行选择。");
                            else 
                                ImGuiComponents.HelpMarker("亲信升级模式将根据您的等级和装备等级（Ilvl），以及您最低等级的亲信战友选择最合适的副本，以尽可能让所有亲信战友均衡升级。\n将不会总是排入最高等级的副本，而是根据我们的稳定副本列表进行选择。");
                        }

                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Support && levelingMode == LevelingMode.Support)
                        {
                            if(ImGui.Checkbox("亲信战友等级足够时优先使用亲信", ref Plugin.Configuration.PreferTrustOverSupportLeveling))
                                Plugin.Configuration.Save();
                        }

                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Trust && Player.Available)
                        {
                            ImGui.Separator();
                            if (DutySelected != null && DutySelected.Content.TrustMembers.Count > 0)
                            {
                                ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined("选择你的亲信战友"));
                                

                                TrustHelper.ResetTrustIfInvalid();
                                for (int i = 0; i < Plugin.Configuration.SelectedTrustMembers.Length; i++)
                                {
                                    TrustMemberName? member = Plugin.Configuration.SelectedTrustMembers[i];

                                    if (member is null)
                                        continue;

                                    if (DutySelected.Content.TrustMembers.All(x => x.MemberName != member))
                                    {
                                        Svc.Log.Debug($"Killing {member}");
                                        Plugin.Configuration.SelectedTrustMembers[i] = null;
                                    }
                                }
                                ImGui.Columns(3);
                                using (ImRaii.Disabled(Plugin.TrustLevelingEnabled && TrustHelper.Members.Any(tm => tm.Value.Level < tm.Value.LevelCap)))
                                {
                                    DrawTrustMembers(DutySelected.Content);
                                }
                                //ImGui.Columns(3, null, false);
                                if (DutySelected.Content.TrustMembers.Count == 7)
                                    ImGui.NextColumn();

                                if (ImGui.Button("刷新", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                                {
                                    if (InventoryHelper.CurrentItemLevel < 370)
                                        Plugin.LevelingModeEnum = LevelingMode.None;
                                    TrustHelper.ClearCachedLevels();

                                    SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]), () => TrustHelper.State == ActionState.None);
                                    SchedulerHelper.ScheduleAction("Refresh Levels - EW", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]), () => TrustHelper.State == ActionState.None);
                                    SchedulerHelper.ScheduleAction("Refresh Levels - DT", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                                }
                                ImGui.NextColumn();
                                ImGui.Columns(1, null, true);
                            }
                            else if (ImGui.Button("刷新亲信战友等级"))
                            {
                                if (InventoryHelper.CurrentItemLevel < 370)
                                    Plugin.LevelingModeEnum = LevelingMode.None;
                                TrustHelper.ClearCachedLevels();

                                SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]), () => TrustHelper.State == ActionState.None);
                                SchedulerHelper.ScheduleAction("Refresh Levels - EW", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]), () => TrustHelper.State == ActionState.None);
                                SchedulerHelper.ScheduleAction("Refresh Levels - DT", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                            }
                        }

                        DrawPathSelection();
                        ImGui.Separator();

                        DrawSearchBar();
                        ImGui.SameLine();
                        if (ImGui.Checkbox("隐藏不可用副本", ref Plugin.Configuration.HideUnavailableDuties))
                            Plugin.Configuration.Save();
                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Regular || Plugin.Configuration.DutyModeEnum == DutyMode.Trial || Plugin.Configuration.DutyModeEnum == DutyMode.Raid)
                        {
                            if (ImGuiEx.CheckboxWrapped("解除限制", ref Plugin.Configuration.Unsynced))
                                Plugin.Configuration.Save();
                        }
                    }
                    var ilvl = InventoryHelper.CurrentItemLevel;
                    if (!ImGui.BeginListBox("##DutyList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                    if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if (PlayerHelper.IsReady)
                        {
                            if (Plugin.LevelingModeEnum != LevelingMode.None)
                            {
                                if (Player.Job.GetCombatRole() == CombatRole.NonCombat || (Plugin.LevelingModeEnum == LevelingMode.Trust && ilvl < 370) || (Plugin.LevelingModeEnum == LevelingMode.Trust && Plugin.CurrentPlayerItemLevelandClassJob.Value != null && Plugin.CurrentPlayerItemLevelandClassJob.Value != Player.Job))
                                {
                                    Svc.Log.Debug($"您正处于一个不兼容的职业: {Player.Job.GetCombatRole()}, 或者您正处于亲信模式但装等({ilvl}) 小于 370, 或您的装等已变更, 正在禁用升级模式");
                                    Plugin.LevelingModeEnum = LevelingMode.None;
                                }
                                else if (ilvl > 0 && ilvl != Plugin.CurrentPlayerItemLevelandClassJob.Key)
                                {
                                    Svc.Log.Debug($"您的装等已更改，正在选择新任务。");
                                    Plugin.CurrentTerritoryContent = LevelingHelper.SelectHighestLevelingRelevantDuty(Plugin.LevelingModeEnum == LevelingMode.Trust);
                                }
                                else
                                {
                                    ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), $"Leveling Mode: 等级{Player.Level} (装等{ilvl})");
                                    foreach (var item in LevelingHelper.LevelingDuties.Select((Value, Index) => (Value, Index)))
                                    {
                                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Trust && !item.Value.DutyModes.HasFlag(DutyMode.Trust))
                                            continue;
                                        var disabled = !item.Value.CanRun();
                                        if (!Plugin.Configuration.HideUnavailableDuties || !disabled)
                                        {
                                            using (ImRaii.Disabled(disabled))
                                            {
                                                ImGuiEx.TextWrapped(item.Value == Plugin.CurrentTerritoryContent ? new Vector4(0, 1, 1, 1) : new Vector4(1, 1, 1, 1), $"L{item.Value.ClassJobLevelRequired} (i{item.Value.ItemLevelRequired}): {item.Value.EnglishName}");
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (Player.Job.GetCombatRole() == CombatRole.NonCombat)
                                    ImGuiEx.TextWrapped(new Vector4(255, 1, 0, 1), "请切换到战斗职业运行AutoDuty");
                                else if (Player.Job == Job.BLU && Plugin.Configuration.DutyModeEnum is not (DutyMode.Regular or DutyMode.Trial or DutyMode.Raid))
                                    ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), "青魔法师无法使用亲信战友、剧情辅助器、探险者小队或多变迷宫模式。请更换职业或选择其他模式。");
                                else
                                {
                                    Dictionary<uint, Content> dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DutyModes.HasFlag(Plugin.Configuration.DutyModeEnum)).ToDictionary();

                                    if (dictionary.Count > 0 && PlayerHelper.IsReady)
                                    {
                                        short level = PlayerHelper.GetCurrentLevelFromSheet();
                                        foreach ((uint _, Content? content) in dictionary)
                                        {
                                            // Apply search filter
                                            if (!string.IsNullOrWhiteSpace(_searchText) && !content.Name.ToLower().Contains(_searchText))
                                                continue;  // Skip duties that do not match the search text

                                            bool canRun = content.CanRun(level);
                                            using (ImRaii.Disabled(!canRun))
                                            {
                                                if (Plugin.Configuration.HideUnavailableDuties && !canRun)
                                                    continue;
                                                if (ImGui.Selectable($"L{content.ClassJobLevelRequired} ({content.TerritoryType}) {content.Name}", DutySelected?.id == content.TerritoryType))
                                                {
                                                    DutySelected = ContentPathsManager.DictionaryPaths[content.TerritoryType];
                                                    Plugin.CurrentTerritoryContent = content;
                                                    DutySelected.SelectPath(out Plugin.CurrentPath);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (PlayerHelper.IsReady)
                                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), "请选择剧情辅助器、亲信战友、冒险者小队或常规模式中的一项\n以填充任务列表");
                                    }
                                }
                            }
                        }
                        else
                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), "Busy...");
                    }
                    else
                    {
                        if (!VNavmesh_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), "AutoDuty 需要安装并加载 vnavmesh 插件以实现正确的导航和移动。请添加第三方仓库：\nhttps://raw.githubusercontent.com/AtmoOmen/DalamudPlugins/main/pluginmaster.json");
                        if (!BossMod_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), "AutoDuty 需要安装并加载 BossMod 插件以正确处理机制。请添加第三方仓库：\nhttps://raw.githubusercontent.com/44451516/ffxiv_bossmod/CN/pluginmaster.json");
                    }
                    ImGui.EndListBox();
                }
            }
        }

        private static void DrawTrustMembers(Content content)
        {
            foreach (TrustMember member in content.TrustMembers)
            {
                bool       enabled        = Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName);
                CombatRole playerRole     = Player.Job.GetCombatRole();
                int        numberSelected = Plugin.Configuration.SelectedTrustMembers.Count(x => x != null);

                TrustMember?[] members = Plugin.Configuration.SelectedTrustMembers.Select(tmn => tmn != null ? TrustHelper.Members[(TrustMemberName)tmn] : null).ToArray();

                bool canSelect = members.CanSelectMember(member, playerRole) && member.Level >= content.ClassJobLevelRequired;

                using (ImRaii.Disabled(!enabled && (numberSelected == 3 || !canSelect)))
                {
                    if (ImGui.Checkbox($"###{member.Index}{content.Id}", ref enabled))
                    {
                        if (enabled)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                if (Plugin.Configuration.SelectedTrustMembers[i] is null)
                                {
                                    Plugin.Configuration.SelectedTrustMembers[i] = member.MemberName;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName))
                            {
                                int idx = Plugin.Configuration.SelectedTrustMembers.IndexOf(x => x != null && x == member.MemberName);
                                Plugin.Configuration.SelectedTrustMembers[idx] = null;
                            }
                        }

                        Plugin.Configuration.Save();
                    }
                }

                ImGui.SameLine(0, 2);
                ImGui.SetItemAllowOverlap();
                ImGui.TextColored(member.Role switch
                {
                    TrustRole.DPS => ImGuiHelper.RoleDPSColor,
                    TrustRole.Healer => ImGuiHelper.RoleHealerColor,
                    TrustRole.Tank => ImGuiHelper.RoleTankColor,
                    TrustRole.AllRounder => ImGuiHelper.RoleAllRounderColor,
                    _ => Vector4.One
                }, member.Name);
                if (member.Level > 0)
                {
                    ImGui.SameLine(0, 2);
                    ImGuiEx.TextV(member.Level < member.LevelCap ? ImGuiHelper.White : ImGuiHelper.MaxLevelColor, $"{member.Level.ToString().ReplaceByChar(Digits.Normal, Digits.GameFont)}");
                }

                ImGui.NextColumn();
            }
        }

        private static void ItemClicked((PathAction, int) item)
        {
            if (item.Item2 == Plugin.Indexer || item.Item1.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase))
            {
                Plugin.Indexer = -1;
                Plugin.MainListClicked = false;
            }
            else
            {
                Plugin.Indexer = item.Item2;
                Plugin.MainListClicked = true;
            }
        }

        internal static void PathsUpdated()
        {
            DutySelected = null;
        }
    }
}
