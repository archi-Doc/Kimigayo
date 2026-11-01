// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

#pragma warning disable SA1611 // Element parameters should be documented
#pragma warning disable SA1615

/// <summary>
/// The IR-level target description derived from a parsed triple: the pointer width in bits, 0 when the triple has no IR target
/// (<see cref="Invalid"/>). The one executable profile's data layout is <c>WindowsProfile.DataLayout</c> (DECISIONS R5-M174).
/// </summary>
public sealed record class IrTarget(int PointerWidth)
{
    public static readonly IrTarget Invalid = new(0);

    /// <summary>Build an IrTarget from a parsed triple.</summary>
    public static IrTarget Create(TargetTriple triple)
        => new(ComputePointerWidth(triple));

    /// <summary>
    /// Port of Triple::getArchPointerBitWidth: pointer width implied by the architecture alone, except that an architecture without
    /// an LLVM backend (no data layout in Triple::computeDataLayout) has no IR target.
    /// </summary>
    private static int GetArchPointerBitWidth(Architecture arch) => arch switch
    {
        Architecture.Unknown or
        Architecture.AmdIL or Architecture.AmdIL64 or Architecture.Hsail or Architecture.Hsail64 or Architecture.Kalimba or
        Architecture.Shave or Architecture.RenderScript32 or Architecture.RenderScript64 => 0,

        Architecture.Avr or Architecture.Msp430 => 16,

        Architecture.AArch64_32 or Architecture.Arc or
        Architecture.Arm or Architecture.ArmEB or Architecture.Csky or
        Architecture.Dxil or Architecture.Hexagon or
        Architecture.Lanai or Architecture.LoongArch32 or
        Architecture.M68k or Architecture.Mips or Architecture.MipsEL or
        Architecture.Nvptx or Architecture.Ppc or Architecture.PpcLE or
        Architecture.R600 or Architecture.RiscV32 or
        Architecture.RiscV32BE or Architecture.Sparc or
        Architecture.SparcEL or Architecture.Spir or Architecture.SpirV32 or
        Architecture.Tce or Architecture.TceLE or Architecture.Thumb or
        Architecture.ThumbEB or Architecture.Wasm32 or Architecture.X86 or
        Architecture.XCore or Architecture.Xtensa => 32,

        // Everything else (aarch64, x86_64, riscv64, wasm64, systemz, ...).
        _ => 64,
    };

    /// <summary>
    /// Arch pointer width refined by ABI-affecting environments (x32, AArch64 ILP32 and MIPS N32 use 32-bit pointers on 64-bit
    /// architectures).
    /// </summary>
    private static int ComputePointerWidth(TargetTriple t)
    {
        int width = GetArchPointerBitWidth(t.Arch);
        if (width == 64)
        {
            if (t.Arch == Architecture.X86_64 && t.IsX32)
            {
                return 32;
            }

            if (t.Arch == Architecture.AArch64 && t.Environment == EnvironmentType.GnuIlp32)
            {
                return 32;
            }

            if ((t.Arch == Architecture.Mips64 || t.Arch == Architecture.Mips64EL) && t.IsAbiN32)
            {
                return 32;
            }
        }

        return width;
    }
}
