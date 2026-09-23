namespace AutoDuty.Helpers
{
    using Configurations;
    using Dalamud.Plugin.Services;
    using ECommons.Automation;
    using IPC;

    public class DiscardHelper : ActiveHelperBase<DiscardHelper, DiscardItemsLoopActionConfig>
    {
        public override string Name        { get; } = nameof(DiscardHelper);
        public override string DisplayName { get; } = "Discarding Items";

        private bool started = false;

        internal override void Start()
        {
            base.Start();
            this.started = false;
        }

        protected override unsafe void HelperUpdate(IFramework framework)
        {
            if (!this.UpdateBase() || !PlayerHelper.IsReadyFull)
                return;
            if (!this.started)
            {
                Chat.ExecuteCommand("/ays discard");
                this.started = true;
                return;
            }

            if (!AutoRetainer_IPCSubscriber.IsBusy())
                this.Stop();
        }
    }
}
