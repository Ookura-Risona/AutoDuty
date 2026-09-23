using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoDuty.Helpers
{
    public static class SoundHelper
    {
        public static unsafe bool StartSound(Sounds soundEnum = Sounds.None)
        {
            UIGlobals.PlaySoundEffect((uint) soundEnum);
            return true;
        }
    }
}
