namespace AutoDuty.Helpers
{
    using Dalamud.Plugin.Services;
    using ECommons.Automation;
    using IPC;

    internal class DiscardHelper : ActiveHelperBase<DiscardHelper>
    {
        protected override string Name        { get; } = nameof(DiscardHelper);
        protected override string DisplayName { get; } = "Discarding Items";

        private bool started = false;

        internal override void Start()
        {
            base.Start();
            this.started = false;
        }

        protected override unsafe void   HelperUpdate(IFramework framework)
        {
            if (!this.UpdateBase() || !PlayerHelper.IsReadyFull)
                return;
            if (!this.started)
            {
                Chat.ExecuteCommand("/ays discard");
                this.started = true;
                return;
            }
            if(!AutoRetainer_IPCSubscriber.IsBusy())
                this.Stop();
        }
    }
}
