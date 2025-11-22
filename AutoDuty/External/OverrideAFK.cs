//All from ﻿https://github.com/awgil/ffxiv_visland/blob/master/OverrideAFK.cs.

using FFXIVClientStructs.FFXIV.Client.UI;
using System.Runtime.InteropServices;

namespace AutoDuty.External;

[StructLayout(LayoutKind.Explicit, Size = 0x4F8)]
internal unsafe struct AfkModule
{
    [FieldOffset(0x10)] public float ElapsedForAfkMessage;
    [FieldOffset(0x14)] public float ElapsedForKick;
    [FieldOffset(0x18)] public float ElapsedUnk1;
    [FieldOffset(0x1C)] public float ElapsedUnk2;
}

internal unsafe class OverrideAFK
{
    private AfkModule* _module;

    public OverrideAFK()
    {
        UIModule*                                           uiModule     = UIModule.Instance();
        void**                                              uiModuleVtbl = (void**)uiModule->VirtualTable;
        delegate* unmanaged[Stdcall]<UIModule*, AfkModule*> getAfkModule = (delegate* unmanaged[Stdcall]<UIModule*, AfkModule*>)uiModuleVtbl[55];
        this._module = getAfkModule(uiModule);
    }

    public void ResetTimers()
    {
        this._module->ElapsedForAfkMessage = 0;
        this._module->ElapsedForKick       = 0;
        this._module->ElapsedUnk1          = 0;
        this._module->ElapsedUnk2             = 0;
    }
}