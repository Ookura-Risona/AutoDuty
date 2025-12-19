using ECommons.DalamudServices;
using ECommons.Reflection;
using System.Net;
using Dalamud.Networking.Http;

namespace AutoDuty.Helpers
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using Dalamud.Common;
    using Newtonsoft.Json;

    internal static class DalamudInfoHelper
    {
        private const string DalDeclarative = "https://raw.githubusercontent.com/goatcorp/dalamud-declarative/refs/heads/main/config.yaml";

        private static bool stagingChecked = false;
        private static bool isStaging      = false;

        public static bool IsOnStaging()
        {
            if(Plugin.isDev)
                return false;

            if (stagingChecked) 
                return isStaging;

            if (DalamudReflector.TryGetDalamudStartInfo(out DalamudStartInfo? startInfo, Svc.PluginInterface))
            {
                try
                {
                    if (startInfo.GameVersion == null)
                        return false;


                    SocketsHttpHandler httpHandler    = new() { AutomaticDecompression = DecompressionMethods.All, ConnectCallback = new HappyEyeballsCallback().ConnectCallback };
                    HttpClient         client         = new(httpHandler) { Timeout = TimeSpan.FromSeconds(10) };
                    using Stream       stream         = client.GetStreamAsync(DalDeclarative).Result;
                    using StreamReader reader         = new(stream);

                    for (int i = 0; i <= 4; i++)
                    {
                        string line = reader.ReadLine()!.Trim();
                        if (i != 4) 
                            continue;
                        string version = line.Split(":").Last().Trim().Replace("'", "");
                        if (version != startInfo.GameVersion.ToString())
                        {
                            stagingChecked = true;
                            isStaging      = false;
                            return false;
                        }
                    }
                }
                catch
                {
                    // Something has gone wrong with checking the Dalamud github file, just allow plugin load anyway
                    stagingChecked = true;
                    isStaging = false;
                    return false;
                }

                if (File.Exists(startInfo.ConfigurationPath))
                {
                    try
                    {
                        string   file = File.ReadAllText(startInfo.ConfigurationPath);
                        dynamic ob   = JsonConvert.DeserializeObject<dynamic>(file) ?? throw new Exception("configuration can't be deserialized");
                        string type = ob.DalamudBetaKind;
                        if (type is not null && !string.IsNullOrEmpty(type) && type != "release")
                        {
                            stagingChecked = true;
                            isStaging      = true;
                            return true;
                        }
                        else
                        {
                            stagingChecked = true;
                            isStaging      = false;
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Svc.Chat.PrintError($"Unable to determine Dalamud staging due to file being config being unreadable.");
                        Svc.Log.Error(ex.ToString());
                        stagingChecked = true;
                        isStaging = false;
                        return false;
                    }
                }
                else
                {
                    stagingChecked = true;
                    isStaging = false;
                    return false;
                }
            }
            return false;
        }
    }
}
