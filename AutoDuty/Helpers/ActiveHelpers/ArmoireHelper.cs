namespace AutoDuty.Helpers;

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using IPC;
using System.Collections.Generic;
using System.Linq;
using Configurations;
using Cabinet = Lumina.Excel.Sheets.Cabinet;
using EventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

public class ArmoireHelper : ActiveHelperBase<ArmoireHelper, ArmoireLoopActionConfig>
{
    public override    string    Name               { get; }       = nameof(ArmoireHelper);
    public override    string    DisplayName        { get; }       = "Armoire";
    public override    string[]? Commands           { get; init; } = ["armoire"];
    public override    string?   CommandDescription { get; init; } = "Stores items in your inventory to the Armoire.";
    protected override int       UpdateBaseThrottle { get; set; }  = 125;

    protected override string[] AddonsToClose { get; } = ["SelectYesno", "Cabinet", "SelectString"];

    private bool glogStarted;

    internal override void Start()
    {
        if (!GlamourLog_IPCSubscriber.IsEnabled)
            return;

        IEnumerable<InventoryItem> items = InventoryHelper.GetInventorySelection(InventoryHelper.Bag).Where(item => this.ItemToCabinetIds.ContainsKey(item.ItemId)).ToList();

        if (!items.Any() || items.All(item => GlamourLog_IPCSubscriber.IsStored(item.ItemId)))
            return;

        this.glogStarted = false;
        base.Start();
    }

    protected override unsafe void HelperUpdate(IFramework framework)
    {
        if (!this.UpdateBase() || !PlayerHelper.IsValid)
            return;

        if (GotoInnHelper.State == ActionState.Running)
            return;

        if(this.glogStarted)
        {
            if (!GlamourLog_IPCSubscriber.Busy)
                this.Stop();
            return;
        }

        Plugin.action = "Armoire";

        if(Svc.Targets.Target == null || Svc.Targets.Target.Struct()->EventHandler->Info.EventId != 720978)
        {
            this.DebugLog("Target is not the armoire.");
            IGameObject? armoire = Svc.Objects.OrderBy(ObjectHelper.GetDistanceToPlayer).FirstOrDefault(o =>
                                                                                                        {
                                                                                                            EventHandler* eventHandler = o.Struct()->EventHandler;
                                                                                                            return eventHandler != null && eventHandler->Info.EventId == 720978;
                                                                                                        });

            if (armoire != null)
            {
                Svc.Targets.Target = armoire;
            }
            else if (!GotoInnHelper.InGCInn())
            {
                this.DebugLog("Not in the correct territory for the inn.");
                GotoInnHelper.Invoke();
            }
            return;
        }

        if (!ObjectHelper.BelowDistanceToPlayer(Svc.Targets.Target.Position, 3f, 2f))
        {
            this.DebugLog("Armoire is too far away.");
            if (!VNavmesh_IPCSubscriber.Path_IsRunning)
                VNavmesh_IPCSubscriber.SimpleMove_PathfindAndMoveTo(Svc.Targets.Target.Position, false);
            return;
        }
        
        if (VNavmesh_IPCSubscriber.Path_IsRunning)
            VNavmesh_IPCSubscriber.Path_Stop();

        if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) && GenericHelpers.IsAddonReady(addonSelectYesno))
        {
            this.DebugLog("Selecting SelectYesno");
            AddonHelper.ClickSelectYesno();
            return;
        }

        AgentCabinet* agentCabinet = AgentCabinet.Instance();
        if (agentCabinet->IsAddonReady() && GenericHelpers.TryGetAddonByName("Cabinet", out AtkUnitBase* _) &&
            UIState.Instance()->Cabinet.IsCabinetLoaded())
        {
            this.DebugLog("Cabinet addon is ready.");

            this.DebugLog("Activating Glamour Log");

            GlamourLog_IPCSubscriber.Entrust();
            this.glogStarted = true;
            return;
        }

        if (GenericHelpers.TryGetAddonByName("SelectString", out AtkUnitBase* addonSelectString) && GenericHelpers.IsAddonReady(addonSelectString))
        {
            this.DebugLog("Selecting SelectString");
            AddonHelper.ClickSelectString(0);
            return;
        }

        if (!agentCabinet->IsAddonShown())
        {
            this.DebugLog("Interact with Cabinet");
            ObjectHelper.InteractWithObject(Svc.Targets.Target);
            return;
        }
    }

    public Dictionary<uint, uint> ItemToCabinetIds
    {
        get
        {
            if (field == null)
            {
                field = [];
                foreach (Cabinet cabinet in Svc.Data.GetExcelSheet<Cabinet>().ToList())
                    field[cabinet.Item.RowId] = cabinet.RowId;
            }
            return field;
        }
    }
}