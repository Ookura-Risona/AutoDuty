using System;
using System.Collections.Generic;
using System.Text;

namespace AutoDuty.Helpers
{
    using Dalamud.Plugin.VersionInfo;
    using ECommons;
    using ECommons.DalamudServices;

    internal static class DalamudHelper
    {
        private static  bool stagingChecked = false;
        private static bool isStaging      = false;
        public static bool IsOnStaging()
        {
            if (stagingChecked)
                return isStaging;

            try
            {
                IDalamudVersionInfo v = Svc.PluginInterface.GetDalamudVersion();
                if (v.BetaTrack.Equals("release", StringComparison.CurrentCultureIgnoreCase))
                {
                    stagingChecked = true;
                    isStaging      = false;
                    return false;
                }
                else
                {
                    stagingChecked = false;
                    isStaging      = true;
                    return true;
                }
            }
            catch (Exception)
            {
                stagingChecked = true;
                isStaging      = false;
                return false;
            }
        }
    }
}
