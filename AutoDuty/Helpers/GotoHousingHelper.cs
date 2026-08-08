using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;

namespace AutoDuty.Helpers
{
    using System;
    using System.Collections.Generic;
    using FFXIVClientStructs.FFXIV.Client.Game;

    internal class GotoHousingHelper : ActiveHelperBase<GotoHousingHelper>
    {
        protected override string Name        { get; } = nameof(GotoHousingHelper);
        protected override string DisplayName { get; } = string.Empty;

        protected override string[] AddonsToClose { get; } = ["SelectYesno", "SelectString", "HousingWardSelection", "HousingWardSelectionDialog"];

        protected override int TimeOut { get; set; } = 600_000;

        internal static void Invoke(Housing whichHousing)
        {
            if (!InPrivateHouse(whichHousing))
            {
                GotoHousingHelper.whichHousing = whichHousing;
                Instance.Start();
            }
        }

        internal override void Stop() 
        {
            GotoHelper.ForceStop();
            base.Stop();
            whichHousing = Housing.Apartment;
            this.index      = 0;
        }

        private static HouseId GetOwnedHouseId(Housing whichHousing)
        {
            return HousingManager.GetOwnedHouseId(whichHousing switch
            {
                Housing.Apartment => EstateType.ApartmentRoom,
                Housing.Personal_Home => EstateType.PersonalEstate,
                Housing.FC_Estate => EstateType.FreeCompanyEstate,
                _ => throw new ArgumentOutOfRangeException(nameof(whichHousing), whichHousing, null)
            });
        }

        internal static unsafe bool InPrivateHouse(Housing whichHousing) => 
            HousingManager.Instance()->GetCurrentIndoorHouseId() == GetOwnedHouseId(whichHousing);

        internal static bool InHousingArea(Housing whichHousing) =>
            GetOwnedHouseId(whichHousing).TerritoryTypeId == Svc.ClientState.TerritoryType;

        private static IGameObject? EntranceGameObject => whichHousing switch
        {
            Housing.FC_Estate => TeleportHelper.FCEstateEntranceGameObject,
            Housing.Personal_Home => TeleportHelper.PersonalHomeEntranceGameObject,
            _ => TeleportHelper.ApartmentEntranceGameObject
        };
        private static Housing whichHousing = Housing.Apartment;
        private static List<Vector3> EntrancePath => whichHousing == Housing.Personal_Home ? 
                                                          Configuration.PersonalHomeEntrancePath : 
                                                          Configuration.FCEstateEntrancePath;
        private int index = 0;

        protected override void HelperUpdate(IFramework framework)
        {
            if (Plugin.States.HasFlag(PluginState.Navigating))
            {
                Svc.Log.Debug($"AutoDuty has Started, Stopping GotoHousing");
                this.Stop();
            }

            if (!EzThrottler.Check("GotoHousing"))
                return;

            EzThrottler.Throttle("GotoHousing", 50);

            if (!Player.Available)
            {
                Svc.Log.Debug($"Our player is null");
                return;
            }

            if (GotoHelper.State == ActionState.Running)
                return;

            Plugin.action = $"Retiring to {whichHousing}";

            if (InPrivateHouse(whichHousing))
            {
                Svc.Log.Debug($"We are in a private house, Stopping GotoHousing");
                this.Stop();
                return;
            }

            if (!InHousingArea(whichHousing))
            {
                if (!PlayerHelper.IsCasting)
                {
                    Svc.Log.Debug($"We are not in the correct housing area, teleporting there");
                    if (whichHousing == Housing.Apartment && !TeleportHelper.TeleportApartment() && TeleportHelper.ApartmentTeleportId == 0)
                    {
                        this.Stop();
                        return;
                    }
                    else if (whichHousing == Housing.Personal_Home && !TeleportHelper.TeleportPersonalHome() && TeleportHelper.PersonalHomeTeleportId == 0)
                    {
                        this.Stop();
                        return;
                    }
                    else if (whichHousing == Housing.FC_Estate && !TeleportHelper.TeleportFCEstate() && TeleportHelper.FCEstateTeleportId == 0)
                    {
                        this.Stop();
                        return;
                    }
                    EzThrottler.Throttle("GotoHousing", 7500, true);
                }
                return;
            }
            else if (PlayerHelper.IsValid)
            {
                if (this.index < EntrancePath.Count)
                {
                    Svc.Log.Debug($"Our entrancePath has entries, moving to index {this.index} which is {EntrancePath[this.index]}");
                    if (((this.index + 1) != EntrancePath.Count && MovementHelper.Move(EntrancePath[this.index], 0.25f, 0.25f, false, false)) || MovementHelper.Move(EntrancePath[this.index], 0.25f, 3f, false, false))
                    {
                        Svc.Log.Debug($"We are at index {this.index} increasing our index");
                        this.index++;
                    }
                }
                else if (EntranceGameObject == null)
                {
                    Svc.Log.Debug($"unable to find entrance door {TeleportHelper.FCEstateWardCenterVector3} {TeleportHelper.FCEstateEntranceGameObject}");
                }
                else if (MovementHelper.Move(EntranceGameObject, 0.25f, 3f, false, false))
                {
                    Svc.Log.Debug($"We are in range of the entrance door, entering");
                    ObjectHelper.InteractWithObject(EntranceGameObject);
                    AddonHelper.ClickSelectString(0);
                    AddonHelper.ClickSelectYesno();
                    AddonHelper.ClickTalk();
                }
                else
                {
                    Svc.Log.Debug($"Moving closer to {EntranceGameObject?.Name} at location {EntranceGameObject?.Position}, we are {Vector3.Distance(EntranceGameObject?.Position ?? Vector3.Zero, Player.Position)} away");
                }
            }
        }
    }
}
