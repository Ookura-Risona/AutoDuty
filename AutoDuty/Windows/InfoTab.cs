using ECommons.ImGuiMethods;
using ImGuiNET;
using System.Diagnostics;

namespace AutoDuty.Windows
{
    internal static class InfoTab
    {
        static string infoUrl = "https://docs.google.com/spreadsheets/d/151RlpqRcCpiD_VbQn6Duf-u-S71EP7d0mx3j1PDNoNA";
        static string gitIssueUrl = "https://github.com/ffxivcode/AutoDuty/issues";
        static string punishDiscordUrl = "https://discord.com/channels/1001823907193552978/1236757595738476725";
        static string ffxivcodeDiscordUrl = "https://discord.com/channels/1241050921732014090/1273374407653462017";
        private static Configuration Configuration = Plugin.Configuration;

        public static void Draw()
        {
            if (MainWindow.CurrentTabName != "信息")
                MainWindow.CurrentTabName = "信息";
            ImGui.NewLine();
            ImGuiEx.TextWrapped("有关 AutoDuty 及其依赖项的一般设置帮助，请查看以下的设置指南以获取更多信息：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("设置指南").X) / 2);
            if (ImGui.Button("设置指南"))
                Process.Start("explorer.exe", infoUrl);
            ImGui.NewLine();
            ImGuiEx.TextWrapped("上述指南还包含每个路径的状态信息，例如路径成熟度、模块成熟度和各路径的一般一致性。您还可以查看附加说明或需要注意的事项，以确保循环成功。对于对 AD 的请求、问题或贡献，请使用 AutoDuty 的 GitHub 提交问题：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("GitHub 提交问题").X) / 2);
            if (ImGui.Button("GitHub 提交问题"))
                Process.Start("explorer.exe", gitIssueUrl);
            ImGui.NewLine();
            ImGuiEx.TextCentered("其他所有问题，请加入 Discord!");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Punish Discord").X) / 2);
            if (ImGui.Button("Punish Discord"))
                Process.Start("explorer.exe", punishDiscordUrl);
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("FFXIVCode Discord").X) / 2);
            if (ImGui.Button("FFXIVCode Discord"))
                Process.Start("explorer.exe", ffxivcodeDiscordUrl);
        }
    }
}
