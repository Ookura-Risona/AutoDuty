using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ECommons.Throttlers;

namespace AutoDuty.Helpers
{
    using System.Collections.Generic;
    using System.Linq;
    using Configurations;
    using FFXIVClientStructs.FFXIV.Client.UI.Misc;
    using Lumina.Excel.Sheets;

    public class CofferHelper : ActiveHelperBase<CofferHelper, CofferOpenLoopActionConfig>
    {
        public override string[]? Commands { get; init; } = ["coffer"];
        public override string? CommandDescription { get; init; } = "Opens coffers in your inventory";

        private readonly Dictionary<uint, int> doneItems = [];
        private          int           initialGearset;

        internal override unsafe void Start()
        {
            base.Start();
            this.initialGearset = RaptureGearsetModule.Instance()->CurrentGearsetIndex;
            this.doneItems.Clear();
        }

        public override string Name        { get; } = nameof(CofferHelper);
        public override string DisplayName { get; } = "Opening Coffers";

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (!this.UpdateBase())
                return;

            if (Conditions.Instance()->Mounted)
            {
                this.DebugLog("Dismount");
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
                return;
            }

            if (InventoryManager.Instance()->GetEmptySlotsInBag() < 1)
            {
                this.DebugLog("No empty slots");
                this.Stop();
                return;
            }

            if (PlayerHelper.IsCasting || !PlayerHelper.IsReadyFull || Player.IsBusy)
                return;

            this.DebugLog("Checking items");

            IEnumerable <InventoryItem> items = InventoryHelper.GetInventorySelection(InventoryHelper.Bag)
                                                               .Where(iv =>
                                                                      {
                                                                          Item? excelItem = InventoryHelper.GetExcelItem(iv.ItemId);
                                                                          this.DebugLog($"checking item: {iv.ItemId} in {iv.Container} {iv.Slot}");
                                                                          return iv.ItemId > 0 && (!this.doneItems.ContainsKey(iv.ItemId) || this.doneItems[iv.ItemId] != iv.Quantity) && excelItem.HasValue && ValidCoffer(excelItem.Value, this.ActionConfig);
                                                                      });


            RaptureGearsetModule* module = RaptureGearsetModule.Instance();
            
            if (items.Any())
            {
                this.DebugLog("item found");
                if (this.ActionConfig.Gearset != null && module->CurrentGearsetIndex != this.ActionConfig.Gearset)
                {
                    this.DebugLog("change gearset");
                    if (!module->IsValidGearset((int)this.ActionConfig.Gearset))
                    {
                        this.DebugLog("invalid gearset");
                        this.ActionConfig.Gearset = null;
                        ConfigurationProfileV2.Save();
                    } else
                    {
                        module->EquipGearset(this.ActionConfig.Gearset.Value);
                        return;
                    }
                }

                InventoryItem item = items.First();

                InventoryHelper.UseItem(item.ItemId);

                if (!PlayerHelper.IsCasting)
                {
                    this.DebugLog("failed to use item");
                    return;
                }

                this.DebugLog("item used");
                this.doneItems[item.ItemId] = item.Quantity;

            } else if (this.initialGearset != module->CurrentGearsetIndex)
            {
                if (!EzThrottler.Throttle("CofferChangeBack", 1000))
                    return;

                this.DebugLog("change back to original gearset");
                module->EquipGearset(this.initialGearset);
            }
            else
            {
                this.DebugLog("no items found");
                this.Stop();
            }
        }

        internal static bool ValidCoffer(Item item, CofferOpenLoopActionConfig config) => // Miscellany
            item.ItemAction.RowId is 1085 or 388 or 367 && item.ItemUICategory.RowId is 61 && (!config.UseBlacklist || !config.Blacklist.ContainsKey(item.RowId));
        /*
         *  367 Triple Triad Card Pack
         */
    }
}