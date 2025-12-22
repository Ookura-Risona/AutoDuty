using AutoDuty.Helpers;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using System.Diagnostics;

namespace AutoDuty.Windows
{
    using IPC;

    internal static class InfoTab
    {
        private const string InfoUrl          = "https://docs.google.com/spreadsheets/d/151RlpqRcCpiD_VbQn6Duf-u-S71EP7d0mx3j1PDNoNA";
        private const string GitIssueUrl      = "https://github.com/erdelf/AutoDuty/issues";
        private const string PunishDiscordUrl = "https://discord.com/channels/1001823907193552978/1236757595738476725";
        private const string CnGitIssueUrl    = "https://github.com/Ookura-Risona/DalamudPlugins/issues";
        
        public static void Draw()
        {
            MainWindow.CurrentTabName = "信息";

            ImGui.NewLine();
            ImGuiEx.TextWrapped("有关 AutoDuty 及其依赖项的一般设置帮助，请查看以下的设置指南以获取更多信息：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Information and Setup").X) / 2);
            if (ImGui.Button("设置指南"))
                Process.Start("explorer.exe", InfoUrl);
            ImGui.NewLine();
            ImGuiEx.TextWrapped("上述指南还包含每个路径的状态信息，例如路径成熟度、模块成熟度和各路径的一般一致性。您还可以查看附加说明或需要注意的事项，以确保循环成功。对于对 AD 的请求、问题或贡献，请使用 AutoDuty 的 GitHub 提交问题：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("GitHub Issues").X) / 2);
            if (ImGui.Button("GitHub 提交问题"))
                Process.Start("explorer.exe", GitIssueUrl);
            ImGui.NewLine();
            ImGuiEx.TextCentered("其他所有问题，请加入 Discord!");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Punish Discord").X) / 2);
            if (ImGui.Button("Punish Discord"))
                Process.Start("explorer.exe", PunishDiscordUrl);

            ImGui.NewLine();

            ImGuiEx.TextWrapped("对于本汉化版特有的问题，请前往汉化者的仓库提交问题：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("提交问题").X) / 2);
            if (ImGui.Button("提交问题"))
                Process.Start("explorer.exe", CnGitIssueUrl);

            ImGui.NewLine();

            int id = 0;

            void PluginInstallLine(ExternalPlugin plugin, string message)
            {
                bool isReady = plugin == ExternalPlugin.BossMod ? 
                                   BossMod_IPCSubscriber.IsEnabled : 
                                   IPCSubscriber_Common.IsReady(plugin.GetExternalPluginData().name);
                
                if(!isReady)
                    if (ImGui.Button($"Install##InstallExternalPlugin_{plugin}_{id++}"))
                        PluginInstaller.InstallPlugin(plugin);

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(isReady ? EzColor.Green : EzColor.Red, plugin.GetExternalPluginName());

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(message);
                ImGui.NextColumn();
            }

            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("必须插件").X) / 2);
            ImGui.Text("必须插件");

            ImGui.Columns(3, "PluginInstallerRequired", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod, "handles boss fights for you");
            PluginInstallLine(ExternalPlugin.vnav, "can move you around");

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("战斗插件").X) / 2);
            ImGui.Text("战斗插件");

            ImGui.Indent(65f);
            ImGui.TextColored(EzColor.Cyan, "Hotly debated, pick your favorite. You can configure it in the config");
            ImGui.Unindent(65f);

            ImGui.Columns(3, "PluginInstallerCombat", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod,              "has integrated rotations");
            PluginInstallLine(ExternalPlugin.WrathCombo,           "Puni.sh's dedicated rotation plugin");
            PluginInstallLine(ExternalPlugin.RotationSolverReborn, "Reborn's rotation plugin");

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("推荐插件").X) / 2);
            ImGui.Text("推荐插件");
            ImGui.NewLine();
            ImGui.Columns(3, "PluginInstallerRecommended", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.AntiAFK,      "keeps you from being marked as afk");
            PluginInstallLine(ExternalPlugin.AutoRetainer, "can be triggered, does GC delivery and discarding");
            PluginInstallLine(ExternalPlugin.Avarice,      "is read for positionals");
            PluginInstallLine(ExternalPlugin.Lifestream,   "incredibly extensive teleporting");
            PluginInstallLine(ExternalPlugin.Pandora,      "chest looting + tankstance");
            PluginInstallLine(ExternalPlugin.Gearsetter,   "recommend items to equip");
            PluginInstallLine(ExternalPlugin.Stylist,      "recommend items to equip");


            ImGui.Columns(1);
        }
    }
}
