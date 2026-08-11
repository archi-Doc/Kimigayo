// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Kimi.Misc.Target;

public enum Architecture
{
    Unknown,
    X86,        // i386 系 32bit
    X86_64,
    Arm,        // 32bit ARM (LE)
    ArmBe,      // 32bit ARM (BE)
    AArch64,
    AArch64Be,
    RiscV32,
    RiscV64,
    Wasm32,
    Wasm64,
    Mips,       // 32bit BE
    MipsEl,     // 32bit LE
    Mips64,     // 64bit BE
    Mips64El,   // 64bit LE
    PowerPc64,
    PowerPc64Le,
    Sparcv9,
    SystemZ,    // s390x
}
