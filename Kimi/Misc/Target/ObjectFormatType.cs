// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

/// <summary>Mirror of llvm::Triple::ObjectFormatType.</summary>
public enum ObjectFormatType
{
    Unknown = 0,
    Coff,
    DXContainer,
    Elf,
    Goff,
    MachO,
    SpirV,
    Wasm,
    Xcoff,
}
