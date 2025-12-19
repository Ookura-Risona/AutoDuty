using AutoDuty.Helpers;
using ECommons;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoDuty.Managers
{
    using System;
    using static Data.Classes;
    internal class VariantManager(TaskManager _taskManager)
    {
        internal unsafe void RegisterVariantDuty(Content content)
        {
            if (content.VVDIndex < 0)
                return;
            _taskManager.Enqueue(() => Svc.Log.Info($"Queueing Duty: {content.Name}"), "RegisterVariantDuty");
            _taskManager.Enqueue(() => Svc.Log.Info($"Index#: {content.VVDIndex}"), "RegisterVariantDuty");
            _taskManager.Enqueue(() => Plugin.action = $"Queueing Duty: {content.Name}", "RegisterVariantDuty");
            AtkUnitBase* addon = null;
            AtkUnitBase* yesno = null;

            if (!PlayerHelper.IsValid)
            {
                _taskManager.Enqueue(() => PlayerHelper.IsValid, "RegisterVariantDuty", new TaskManagerConfiguration(int.MaxValue));
                _taskManager.EnqueueDelay(2000);
            }
            _taskManager.Enqueue((Action) (() => GenericHelpers.TryGetAddonByName("VVDFinder", out addon)),                            "RegisterVariantDuty");
            _taskManager.Enqueue(() => { if (addon == null) OpenVVD(); },                                                            "RegisterVariantDuty");
            _taskManager.Enqueue(() => GenericHelpers.TryGetAddonByName("VVDFinder", out addon) && GenericHelpers.IsAddonReady(addon), "RegisterVariantDuty");
            _taskManager.Enqueue(() => AddonHelper.FireCallBack(addon, true, 12, content.VVDIndex+1),                                  "RegisterVariantDuty");
            _taskManager.EnqueueDelay(500);
            _taskManager.Enqueue(() => AddonHelper.FireCallBack(addon, true, 11, 1), "RegisterVariantDuty");
            _taskManager.EnqueueDelay(500);
            _taskManager.Enqueue(() => GenericHelpers.TryGetAddonByName("SelectYesno", out yesno) && GenericHelpers.IsAddonReady(yesno), "RegisterVariantDuty");
            _taskManager.Enqueue(() => AddonHelper.FireCallBack(yesno, true, 0, 1), "RegisterVariantDuty");
            _taskManager.Enqueue(() => GenericHelpers.TryGetAddonByName("ContentsFinderConfirm", out addon) && GenericHelpers.IsAddonReady(addon), "RegisterVariantDuty");
            _taskManager.Enqueue(() => AddonHelper.FireCallBack(addon, true, 8), "RegisterVariantDuty");
        }

        private static unsafe void OpenVVD() => 
            AgentModule.Instance()->GetAgentByInternalId(AgentId.VVDFinder)->Show();
    }
}
