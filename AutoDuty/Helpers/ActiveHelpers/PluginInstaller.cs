namespace AutoDuty.Helpers
{
    using Dalamud.Plugin.Services;
    using ECommons.Reflection;
    using ECommons.Throttlers;
    using global::AutoDuty.Configurations;
    using IPC;
    using System.Linq;
    using System.Threading.Tasks;

    public class PluginInstaller : ActiveHelperBase<PluginInstaller, LoopActionConfigBare>
    {
        public override string Name        { get; } = "Plugin Installer";
        public override string DisplayName { get; } = "Plugin Installer";

        private static ExternalPlugin pluginsToInstall;
        private        Task<bool>?    installTask;
        private        int            retries = 0;


        internal static void InstallPlugin(ExternalPlugin plugins)
        {
            pluginsToInstall = plugins.GetFlags()
                .Where(plugin => plugin.HasInstaller())
                .Aggregate(ExternalPlugin.None, (current, plugin) => current | plugin);
            Invoke();
        }

        internal override void Start()
        {
            if(this.installTask?.Status == TaskStatus.Running)
            {
                this.DebugLog("Plugin installation already in progress");
                return;
            }
            if(pluginsToInstall == ExternalPlugin.None)
            {
                this.DebugLog("No plugin specified for installation");
                return;
            }

            base.Start();
        }

        protected override void HelperUpdate(IFramework framework)
        {
            if(pluginsToInstall == ExternalPlugin.None)
            {
                this.Stop();
                return;
            }

            if (!EzThrottler.Throttle(this.Name, this.UpdateBaseThrottle))
                return;

            if (this.installTask == null)
            {
                this.DebugLog("Getting plugin data");

                ExternalPlugin plugin = pluginsToInstall.GetFlags().Last();
                this.retries = 0;

                (string url, string name) = plugin.GetExternalPluginData();
                this.DebugLog($"{url} | {name}");
                this.installTask = DalamudReflector.AddPlugin(url, name);
                return;
            }
            if(this.installTask.IsCompleted)
            {
                this.DebugLog("task completed");

                ExternalPlugin plugin = pluginsToInstall.GetFlags().Last();
                string pluginName = plugin.GetExternalPluginData().name;
                if (this.installTask.Result || IPCSubscriber_Common.IsReady(pluginName))
                {
                    this.DebugLog("Successfully installed");
                    pluginsToInstall &= ~plugin;
                    return;
                } else
                {
                    if(PluginInterface.InstalledPlugins.Any(iep => iep.InternalName == pluginName))
                    {
                        this.DebugLog("Plugin already installed but not ready, stopping installation");

                        pluginsToInstall &= ~plugin;
                        return;
                    }

                    this.DebugLog("Failed to install plugin");
                    this.retries++;
                    if(this.retries > 5)
                        pluginsToInstall &= ~plugin;
                    else
                        this.installTask = null;
                }
            }
        }

        internal override void Stop()
        {
            pluginsToInstall = ExternalPlugin.None;
            this.installTask = null;
            base.Stop();
        }
    }
}
