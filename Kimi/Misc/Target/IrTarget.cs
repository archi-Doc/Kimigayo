// Copyright(c) All contributors. All rights reserved.Licensed under the MIT license.

namespace Kimi.Compiler.Target;

#pragma warning disable SA1611 // Element parameters should be documented
#pragma warning disable SA1615


/// <summary>
/// The IR-level target description derived from a parsed triple:
/// pointer width in bits and the LLVM data layout string.
/// </summary>
public sealed record class IrTarget(int PointerWidth, string DataLayout)
{
    /// <summary>
    /// Build an IrTarget from a parsed triple. <paramref name="abiName"/>
    /// mirrors the ABIName parameter of Triple::computeDataLayout (e.g.
    /// "elfv2" for ppc64, "n32"/"n64" for MIPS, "ilp32e"/"lp64e" for RISC-V,
    /// "aapcs"/"apcs-gnu" for ARM, "shortptr" for NVPTX).
    /// </summary>
    public static IrTarget Create(TargetTriple triple, string abiName = "")
        => new(ComputePointerWidth(triple), ComputeDataLayout(triple, abiName));

    // ==================================================================
    // Pointer width
    // ==================================================================

    /// <summary>
    /// Port of Triple::getArchPointerBitWidth: pointer width implied by the
    /// architecture alone.
    /// </summary>
    public static int GetArchPointerBitWidth(Architecture arch) => arch switch
    {
        Architecture.Unknown => 0,

        Architecture.Avr or Architecture.Msp430 => 16,

        Architecture.AArch64_32 or Architecture.AmdIL or Architecture.Arc or
        Architecture.Arm or Architecture.ArmEB or Architecture.Csky or
        Architecture.Dxil or Architecture.Hexagon or Architecture.Hsail or
        Architecture.Kalimba or Architecture.Lanai or Architecture.LoongArch32 or
        Architecture.M68k or Architecture.Mips or Architecture.MipsEL or
        Architecture.Nvptx or Architecture.Ppc or Architecture.PpcLE or
        Architecture.R600 or Architecture.RenderScript32 or Architecture.RiscV32 or
        Architecture.RiscV32BE or Architecture.Shave or Architecture.Sparc or
        Architecture.SparcEL or Architecture.Spir or Architecture.SpirV32 or
        Architecture.Tce or Architecture.TceLE or Architecture.Thumb or
        Architecture.ThumbEB or Architecture.Wasm32 or Architecture.X86 or
        Architecture.XCore or Architecture.Xtensa => 32,

        // Everything else (aarch64, x86_64, riscv64, wasm64, systemz, ...).
        _ => 64,
    };

    /// <summary>
    /// Arch pointer width refined by ABI-affecting environments so it agrees
    /// with the "p:" entry of the computed data layout (x32, AArch64 ILP32,
    /// MIPS N32 use 32-bit pointers on 64-bit architectures).
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

    // ==================================================================
    // Data layout (port of Triple::computeDataLayout and helpers from
    // llvm/lib/TargetParser/TargetDataLayout.cpp)
    // ==================================================================

    /// <summary>Port of Triple::computeDataLayout(StringRef ABIName).</summary>
    public static string ComputeDataLayout(TargetTriple t, string abiName = "")
    {
        switch (t.Arch)
        {
            case Architecture.Arm:
            case Architecture.ArmEB:
            case Architecture.Thumb:
            case Architecture.ThumbEB:
                return ComputeArmDataLayout(t, abiName);

            case Architecture.AArch64:
            case Architecture.AArch64BE:
            case Architecture.AArch64_32:
                return ComputeAArch64DataLayout(t);

            case Architecture.Arc:
                return "e-m:e-p:32:32-i1:8:32-i8:8:32-i16:16:32-i32:32:32-" +
                       "f32:32:32-i64:32-f64:32-a:0:32-n32";

            case Architecture.Avr:
                return "e-P1-p:16:8-i8:8-i16:8-i32:8-i64:8-f32:8-f64:8-n8:16-a:8";

            case Architecture.BpfEL:
                return "e-m:e-p:64:64-i64:64-i128:128-n32:64-S128";
            case Architecture.BpfEB:
                return "E-m:e-p:64:64-i64:64-i128:128-n32:64-S128";

            case Architecture.Csky:
                // CSKY is always a 32-bit little-endian target (CSKYv2 ABI).
                return "e-m:e-S32-p:32:32-i32:32:32-i64:32:32-f32:32:32-f64:32:32-" +
                       "v64:32:32-v128:32:32-a:0:32-Fi32-n32";

            case Architecture.Dxil:
                return "e-m:e-ve-p:32:32-i1:32-i8:8-i16:16-i32:32-i64:64-f16:16-" +
                       "f32:32-f64:64-n8:16:32:64";

            case Architecture.Hexagon:
                return "e-m:e-p:32:32:32-a:0-n16:32-" +
                       "i64:64:64-i32:32:32-i16:16:16-i1:8:8-f32:32:32-f64:64:64-" +
                       "v32:32:32-v64:64:64-v512:512:512-v1024:1024:1024-v2048:2048:2048";

            case Architecture.LoongArch32:
                return "e-m:e-p:32:32-i64:64-n32-S128";
            case Architecture.LoongArch64:
                return "e-m:e-p:64:64-i64:64-i128:128-n32:64-S128";

            case Architecture.M68k:
                // Big endian, ELF mangling, 32-bit pointers with 16-bit ABI
                // alignment, GCC-compatible 16-bit aggregate/stack alignment.
                return "E-m:e-p:32:16:32-i8:8:8-i16:16:16-i32:16:32-n8:16:32-a:0:16-S16";

            case Architecture.Mips:
            case Architecture.MipsEL:
            case Architecture.Mips64:
            case Architecture.Mips64EL:
                return ComputeMipsDataLayout(t, abiName);

            case Architecture.Msp430:
                return "e-m:e-p:16:16-i32:16-i64:16-f32:16-f64:16-a:8-n8:16-S16";

            case Architecture.Ppc:
            case Architecture.PpcLE:
            case Architecture.Ppc64:
            case Architecture.Ppc64LE:
                return ComputePowerDataLayout(t, abiName);

            case Architecture.AmdGpu:
            case Architecture.R600:
                return ComputeAmdDataLayout(t);

            case Architecture.RiscV32:
            case Architecture.RiscV64:
            case Architecture.RiscV32BE:
            case Architecture.RiscV64BE:
                return ComputeRiscVDataLayout(t, abiName);

            case Architecture.Sparc:
            case Architecture.SparcV9:
            case Architecture.SparcEL:
                return ComputeSparcDataLayout(t);

            case Architecture.SystemZ:
                return ComputeSystemZDataLayout(t);

            case Architecture.Tce:
                return "E-p:32:32:32-i1:8:8-i8:8:32-i16:16:32-i32:32:32-i64:32:32-" +
                       "f16:16:16-f32:32:32-f64:32:32-v64:64:64-i128:128-v128:128:128-" +
                       "v256:256:256-v512:512:512-v1024:1024:1024-v2048:2048:2048-" +
                       "v4096:4096:4096-a0:0:32-n32";
            case Architecture.TceLE:
                return "e-p:32:32:32-i1:8:8-i8:8:32-i16:16:32-i32:32:32-i64:32:32-" +
                       "f16:16:16-f32:32:32-f64:32:32-v64:64:64-i128:128-v128:128:128-" +
                       "v256:256:256-v512:512:512-v1024:1024:1024-v2048:2048:2048-" +
                       "v4096:4096:4096-a0:0:32-n32";
            case Architecture.TceLE64:
                return "e-p:64:64:64-i1:8:64-i8:8:64-i16:16:64-i32:32:64-i64:64:64-" +
                       "f16:16:64-f32:32:64-f64:64:64-v64:64:64-i128:128-v128:128:128-" +
                       "v256:256:256-v512:512:512-v1024:1024:1024-v2048:2048:2048-" +
                       "v4096:4096:4096-a0:0:64-n64";

            case Architecture.X86:
            case Architecture.X86_64:
                return ComputeX86DataLayout(t);

            case Architecture.XCore:
                return "e-m:e-p:32:32-i1:8:32-i8:8:32-i16:16:32-i64:32-f64:32-a:0:32-n32";

            case Architecture.Xtensa:
                return "e-m:e-p:32:32-i8:8:32-i16:16:32-i64:64-n32";

            case Architecture.Nvptx:
            case Architecture.Nvptx64:
                return ComputeNvptxDataLayout(t, abiName);

            case Architecture.Spir:
            case Architecture.Spir64:
            case Architecture.SpirV:
            case Architecture.SpirV32:
            case Architecture.SpirV64:
                return ComputeSpirVDataLayout(t);

            case Architecture.Lanai:
                // Big endian, ELF mangling, 32-bit pointers, 32-bit aggregate
                // alignment, 32-bit native integers, 64-bit stack alignment.
                return "E-m:e-p:32:32-i64:64-a:0:32-n32-S64";

            case Architecture.Wasm32:
            case Architecture.Wasm64:
                return ComputeWebAssemblyDataLayout(t);

            case Architecture.Ve:
                return ComputeVeDataLayout();

            // Virtual ISAs with no LLVM backend => no fixed data layout.
            case Architecture.AmdIL:
            case Architecture.AmdIL64:
            case Architecture.Hsail:
            case Architecture.Hsail64:
            case Architecture.Kalimba:
            case Architecture.Shave:
            case Architecture.RenderScript32:
            case Architecture.RenderScript64:
            case Architecture.Unknown:
            default:
                return string.Empty;
        }
    }

    /// <summary>Port of getManglingComponent.</summary>
    private static string GetManglingComponent(TargetTriple t)
    {
        if (t.IsOsBinFormatGoff)
        {
            return "-m:l";
        }

        if (t.IsOsBinFormatMachO)
        {
            return "-m:o";
        }

        if ((t.IsOsWindows || t.Os == OsType.UEFI) && t.IsOsBinFormatCoff)
        {
            return t.Arch == Architecture.X86 ? "-m:x" : "-m:w";
        }

        if (t.IsOsBinFormatXcoff)
        {
            return "-m:a";
        }

        return "-m:e";
    }

    // ------------------------------------------------------------------
    // ARM
    // ------------------------------------------------------------------

    private enum ArmAbi { Apcs, Aapcs, Aapcs16 }

    /// <summary>
    /// Port of ARM::computeTargetABI + computeDefaultTargetABI
    /// (llvm/lib/TargetParser/ARMTargetParserCommon.cpp), reduced to the three
    /// ABI kinds that influence the data layout. The M-profile check is
    /// approximated from the arch name ("...v6m"/"...v7m"/"...v7em"/"...v8m"/
    /// "...v8.1m") instead of a full sub-arch parse.
    /// </summary>
    private static ArmAbi ComputeArmAbi(TargetTriple t, string abiName)
    {
        if (abiName.Length != 0)
        {
            if (abiName == "aapcs16")
            {
                return ArmAbi.Aapcs16;
            }

            if (abiName.StartsWith("aapcs", StringComparison.Ordinal))
            {
                return ArmAbi.Aapcs;
            }

            if (abiName.StartsWith("apcs", StringComparison.Ordinal))
            {
                return ArmAbi.Apcs;
            }
            // Unknown ABI names fall back to the triple-derived default.
        }

        if (t.IsOsBinFormatMachO)
        {
            if (t.Environment == EnvironmentType.Eabi ||
                t.Os == OsType.Unknown ||
                IsArmMProfileName(t.ArchName))
            {
                return ArmAbi.Aapcs;
            }

            if (t.Os == OsType.WatchOS)
            {
                return ArmAbi.Aapcs16;
            }

            return ArmAbi.Apcs;
        }

        if (t.IsOsWindows)
        {
            return ArmAbi.Aapcs;
        }

        switch (t.Environment)
        {
            case EnvironmentType.Android:
            case EnvironmentType.GnuEabi:
            case EnvironmentType.GnuEabiT64:
            case EnvironmentType.GnuEabiHF:
            case EnvironmentType.GnuEabiHFT64:
            case EnvironmentType.MuslEabi:
            case EnvironmentType.MuslEabiHF:
            case EnvironmentType.OpenHos:
            case EnvironmentType.Eabi:
            case EnvironmentType.EabiHF:
                return ArmAbi.Aapcs;
            default:
                if (t.Os == OsType.NetBSD)
                {
                    return ArmAbi.Apcs;
                }

                return ArmAbi.Aapcs;
        }
    }

    private static bool IsArmMProfileName(string archName)
    {
        // Matches canonical M-profile arch suffixes: v6m, v6sm, v7m, v7em,
        // v8m.base, v8m.main, v8.1m.main.
        ReadOnlySpan<char> s = archName.AsSpan();
        return s.EndsWith("v6m") || s.EndsWith("v6sm") || s.EndsWith("v7m") ||
               s.EndsWith("v7em") || s.Contains("v8m", StringComparison.Ordinal) ||
               s.Contains("v8.1m", StringComparison.Ordinal);
    }

    /// <summary>Port of computeARMDataLayout.</summary>
    private static string ComputeArmDataLayout(TargetTriple t, string abiName)
    {
        var abi = ComputeArmAbi(t, abiName);
        var sb = new System.Text.StringBuilder(96);

        sb.Append(t.IsLittleEndian ? 'e' : 'E');
        sb.Append(GetManglingComponent(t));

        // Pointers are 32 bits and aligned to 32 bits.
        sb.Append("-p:32:32");

        // Function pointers are aligned to 8 bits (LSB stores ARM/Thumb state).
        sb.Append("-Fi8");

        // ABIs other than APCS have 64-bit integers with natural alignment.
        if (abi != ArmAbi.Apcs)
        {
            sb.Append("-i64:64");
        }

        // APCS requires 64-bit floats to be aligned to 32 bits.
        if (abi == ArmAbi.Apcs)
        {
            sb.Append("-f64:32:64");
        }

        // Vector alignments: APCS aligns to 32 bits; AAPCS16 omits them.
        if (abi == ArmAbi.Apcs)
        {
            sb.Append("-v64:32:64-v128:32:128");
        }
        else if (abi != ArmAbi.Aapcs16)
        {
            sb.Append("-v128:64:128");
        }

        // Align aggregates to 32 bits; 32-bit native integer width.
        sb.Append("-a:0:32-n32");

        // Stack alignment: 128 bits on AAPCS16, 64 bits on AAPCS, else 32.
        sb.Append(abi == ArmAbi.Aapcs16 ? "-S128" : abi == ArmAbi.Aapcs ? "-S64" : "-S32");

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // AArch64
    // ------------------------------------------------------------------

    /// <summary>Port of computeAArch64DataLayout.</summary>
    private static string ComputeAArch64DataLayout(TargetTriple t)
    {
        if (t.IsOsBinFormatMachO)
        {
            if (t.Arch == Architecture.AArch64_32)
                return "e-m:o-p:32:32-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-" +
                       "n32:64-S128-Fn32";
            return "e-m:o-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-n32:64-S128-" +
                   "Fn32";
        }
        if (t.IsOsBinFormatCoff)
            return "e-m:w-p270:32:32-p271:32:32-p272:64:64-p:64:64-i32:32-i64:64-i128:" +
                   "128-n32:64-S128-Fn32";

        var endian = t.IsLittleEndian ? "e" : "E";
        var ptr32 = t.Environment == EnvironmentType.GnuIlp32 ? "-p:32:32" : string.Empty;
        return endian + "-m:e" + ptr32 +
               "-p270:32:32-p271:32:32-p272:64:64-i8:8:32-i16:16:32-i64:64-i128:128-" +
               "n32:64-S128-Fn32";
    }

    // ------------------------------------------------------------------
    // MIPS
    // ------------------------------------------------------------------

    private enum MipsAbi { O32, N32, N64 }

    /// <summary>Port of getMipsABI.</summary>
    private static MipsAbi GetMipsAbi(TargetTriple t, string abiName)
    {
        if (abiName.StartsWith("o32", StringComparison.Ordinal))
        {
            return MipsAbi.O32;
        }

        if (abiName.StartsWith("n32", StringComparison.Ordinal))
        {
            return MipsAbi.N32;
        }

        if (abiName.StartsWith("n64", StringComparison.Ordinal))
        {
            return MipsAbi.N64;
        }

        if (t.IsAbiN32)
        {
            return MipsAbi.N32;
        }

        var isMips64 = t.Arch is Architecture.Mips64 or Architecture.Mips64EL;
        return isMips64 ? MipsAbi.N64 : MipsAbi.O32;
    }

    /// <summary>Port of computeMipsDataLayout.</summary>
    private static string ComputeMipsDataLayout(TargetTriple t, string abiName)
    {
        var abi = GetMipsAbi(t, abiName);
        var sb = new System.Text.StringBuilder(80);

        sb.Append(t.IsLittleEndian ? 'e' : 'E');
        sb.Append(abi == MipsAbi.O32 ? "-m:m" : "-m:e");

        // Pointers are 32-bit on all ABIs except N64.
        if (abi != MipsAbi.N64)
        {
            sb.Append("-p:32:32");
        }

        sb.Append("-i8:8:32-i16:16:32-i64:64");

        if (abi is MipsAbi.N64 or MipsAbi.N32)
        {
            sb.Append("-i128:128-n32:64-S128");
        }
        else
        {
            sb.Append("-n32-S64");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // PowerPC
    // ------------------------------------------------------------------

    /// <summary>Port of Triple::isPPC64ELFv2ABI.</summary>
    private static bool IsPpc64ElfV2Abi(TargetTriple t) =>
        t.Arch == Architecture.Ppc64 &&
        ((t.Os == OsType.FreeBSD && (t.OsMajorVersion >= 13 || t.OsMajorVersion == 0)) ||
         t.Os == OsType.OpenBSD || t.IsMusl);

    /// <summary>Port of computePowerDataLayout.</summary>
    private static string ComputePowerDataLayout(TargetTriple t, string abiName)
    {
        var is64Bit = t.Arch is Architecture.Ppc64 or Architecture.Ppc64LE;
        var isAix = t.Os == OsType.AIX;
        var sb = new System.Text.StringBuilder(80);

        sb.Append(t.IsLittleEndian ? 'e' : 'E');
        sb.Append(GetManglingComponent(t));

        // PPC32 has 32-bit pointers; the PS3 (OS Lv2) is PPC64 with 32-bit pointers.
        if (!is64Bit || t.Os == OsType.Lv2)
        {
            sb.Append("-p:32:32");
        }

        // Function pointer alignment depends on function descriptors.
        if (t.Arch == Architecture.Ppc64 && !IsPpc64ElfV2Abi(t) && abiName != "elfv2")
        {
            sb.Append("-Fi64");
        }
        else if (isAix)
        {
            sb.Append(is64Bit ? "-Fi64" : "-Fi32");
        }
        else
        {
            sb.Append("-Fn32");
        }

        sb.Append("-i64:64");
        sb.Append(is64Bit ? "-i128:128-n32:64" : "-n32");

        // The ABI alignment for doubles on AIX is 4 bytes.
        if (isAix)
        {
            sb.Append("-f64:32:64");
        }

        // Explicit vector alignments (avoid over-aligned v256i1/v512i1).
        if (is64Bit && (isAix || t.Os == OsType.Linux))
        {
            sb.Append("-S128-v256:256:256-v512:512:512");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // AMD GPU
    // ------------------------------------------------------------------

    /// <summary>Port of computeAMDDataLayout.</summary>
    private static string ComputeAmdDataLayout(TargetTriple t)
    {
        if (t.Arch == Architecture.R600)
            return "e-m:e-p:32:32-i64:64-v16:16-v24:32-v32:32-v48:64-v96:128" +
                   "-v192:256-v256:256-v512:512-v1024:1024-v2048:2048-n32:64-S32-A5-G1";

        return "e-m:e-p:64:64-p1:64:64-p2:32:32-p3:32:32-p4:64:64-p5:32:32-p6:32:32" +
               "-p7:160:256:256:32-p8:128:128:128:48-p9:192:256:256:32-i64:64-" +
               "v16:16-v24:32-v32:32-v48:64-v96:128-v192:256-v256:256-v512:512-" +
               "v1024:1024-v2048:2048-n32:64-S32-A5-G1-ni:7:8:9";
    }

    // ------------------------------------------------------------------
    // RISC-V
    // ------------------------------------------------------------------

    /// <summary>Port of computeRISCVDataLayout.</summary>
    private static string ComputeRiscVDataLayout(TargetTriple t, string abiName)
    {
        if (t.IsOsBinFormatMachO)
        {
            return "e-m:o-p:32:32-i64:64-n32-S128";
        }

        var is64 = t.Arch is Architecture.RiscV64 or Architecture.RiscV64BE;
        var pureCap = abiName.StartsWith("il32pc64", StringComparison.Ordinal) ||
                       abiName.StartsWith("l64pc128", StringComparison.Ordinal) ||
                       abiName.StartsWith("cheriot", StringComparison.Ordinal);
        var sb = new System.Text.StringBuilder(64);

        sb.Append(t.IsLittleEndian ? 'e' : 'E');
        sb.Append("-m:e");

        if (is64)
        {
            sb.Append("-p:64:64");
            if (pureCap)
            {
                sb.Append("-pe200:128:128:128:64");
            }

            sb.Append("-i64:64-i128:128-n32:64");
        }
        else
        {
            sb.Append("-p:32:32");
            if (pureCap)
            {
                sb.Append("-pe200:64:64:64:32");
            }

            sb.Append("-i64:64-n32");
        }

        // Stack alignment based on ABI.
        sb.Append(abiName == "ilp32e" ? "-S32" : abiName == "lp64e" ? "-S64" : "-S128");

        if (pureCap)
        {
            sb.Append("-A200-P200-G200");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // SPARC
    // ------------------------------------------------------------------

    /// <summary>Port of computeSparcDataLayout.</summary>
    private static string ComputeSparcDataLayout(TargetTriple t)
    {
        if (t.Arch == Architecture.SparcV9)
        {
            return "E-m:e-i64:64-i128:128-n32:64-S128";
        }

        return (t.Arch == Architecture.SparcEL ? "e" : "E") +
               "-m:e-p:32:32-i64:64-i128:128-f128:64-n32-S64";
    }

    // ------------------------------------------------------------------
    // SystemZ
    // ------------------------------------------------------------------

    /// <summary>Port of computeSystemZDataLayout.</summary>
    private static string ComputeSystemZDataLayout(TargetTriple t)
    {
        // z/OS uses GOFF mangling and a 32-bit ptr32 address space.
        var mangling = GetManglingComponent(t);
        var zosPtr32 = t.Os == OsType.ZOS ? "-p1:32:32" : string.Empty;
        return "E-S64" + mangling + zosPtr32 +
               "-i1:8:16-i8:8:16-i64:64-f128:64-v128:64-a:8:16-n32:64";
    }

    // ------------------------------------------------------------------
    // X86
    // ------------------------------------------------------------------

    /// <summary>Port of computeX86DataLayout.</summary>
    private static string ComputeX86DataLayout(TargetTriple t)
    {
        var is64Bit = t.Arch == Architecture.X86_64;
        var isIamcu = t.Os == OsType.ElfIamcu;
        var sb = new System.Text.StringBuilder(112);

        sb.Append('e'); // X86 is little endian.
        sb.Append(GetManglingComponent(t));

        // X86-32 and x32 have 32-bit pointers.
        if (!is64Bit || t.IsX32)
        {
            sb.Append("-p:32:32");
        }

        // Address spaces for 32-bit signed/unsigned and 64-bit pointers.
        sb.Append("-p270:32:32-p271:32:32-p272:64:64");

        if (is64Bit || t.IsOsWindows)
        {
            sb.Append("-i64:64-i128:128");
        }
        else if (isIamcu)
        {
            sb.Append("-i64:32-f64:32");
        }
        else
        {
            sb.Append("-i128:128-f64:32:64");
        }

        // f80 alignment: 128 bits on 64-bit/Darwin/MSVC, 32 bits otherwise.
        if (isIamcu)
        { /* no f80 */ }
        else if (is64Bit || t.IsOsDarwin || t.IsWindowsMsvcEnvironment)
            sb.Append("-f80:128");
        else
            sb.Append("-f80:32");

        if (isIamcu)
        {
            sb.Append("-f128:32");
        }

        sb.Append(is64Bit ? "-n8:16:32:64" : "-n8:16:32");

        if ((!is64Bit && t.IsOsWindows) || isIamcu)
        {
            sb.Append("-a:0:32-S32");
        }
        else
        {
            sb.Append("-S128");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // NVPTX
    // ------------------------------------------------------------------

    /// <summary>Port of computeNVPTXDataLayout.</summary>
    private static string ComputeNvptxDataLayout(TargetTriple t, string abiName)
    {
        var is32Bit = t.Arch == Architecture.Nvptx;
        var shortPtr = abiName == "shortptr";
        var sb = new System.Text.StringBuilder(96);

        sb.Append('e');
        if (is32Bit)
        {
            sb.Append("-p:32:32");
        }
        else
        {
            // In shortptr mode shared/constant/local/shared-cluster/param
            // address spaces are 32 bits; tensor memory (p6) always is.
            if (shortPtr)
            {
                sb.Append("-p3:32:32-p4:32:32-p5:32:32");
            }

            sb.Append("-p6:32:32");
            if (shortPtr)
            {
                sb.Append("-p7:32:32-p101:32:32");
            }
        }
        sb.Append("-i64:64-i128:128-i256:256-v16:16-v32:32-n16:32:64");
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // SPIR / SPIR-V
    // ------------------------------------------------------------------

    /// <summary>Port of computeSPIRVDataLayout.</summary>
    private static string ComputeSpirVDataLayout(TargetTriple t)
    {
        if (t.Arch is Architecture.SpirV32 or Architecture.Spir)
            return "e-p:32:32-i64:64-v16:16-v24:32-v32:32-v48:64-v96:128-v192:256-" +
                   "v256:256-v512:512-v1024:1024-n8:16:32:64-G1";
        if (t.Arch == Architecture.SpirV)
        {
            return "e-ve-i64:64-n8:16:32:64-G10";
        }

        if (t.Vendor == VendorType.AMD && t.Os == OsType.AmdHsa)
            return "e-i64:64-v16:16-v24:32-v32:32-v48:64-v96:128-v192:256-v256:256-" +
                   "v512:512-v1024:1024-n32:64-S32-G1-P4-A0";
        if (t.Vendor == VendorType.Intel)
            return "e-i64:64-v16:16-v24:32-v32:32-v48:64-v96:128-v192:256-v256:256-" +
                   "v512:512-v1024:1024-n8:16:32:64-G1-P9-A0";
        return "e-i64:64-v16:16-v24:32-v32:32-v48:64-v96:128-v192:256-v256:256-" +
               "v512:512-v1024:1024-n8:16:32:64-G1";
    }

    // ------------------------------------------------------------------
    // WebAssembly
    // ------------------------------------------------------------------

    /// <summary>Port of computeWebAssemblyDataLayout.</summary>
    private static string ComputeWebAssemblyDataLayout(TargetTriple t)
    {
        var is64 = t.Arch == Architecture.Wasm64;
        var emscripten = t.Os == OsType.Emscripten;
        return is64
            ? (emscripten
                ? "e-m:e-p:64:64-p10:8:8-p20:8:8-i64:64-i128:128-f128:64-n32:64-S128-ni:1:10:20"
                : "e-m:e-p:64:64-p10:8:8-p20:8:8-i64:64-i128:128-n32:64-S128-ni:1:10:20")
            : (emscripten
                ? "e-m:e-p:32:32-p10:8:8-p20:8:8-i64:64-i128:128-f128:64-n32:64-S128-ni:1:10:20"
                : "e-m:e-p:32:32-p10:8:8-p20:8:8-i64:64-i128:128-n32:64-S128-ni:1:10:20");
    }

    // ------------------------------------------------------------------
    // NEC SX-Aurora VE
    // ------------------------------------------------------------------

    /// <summary>Port of computeVEDataLayout (constant for all VE triples).</summary>
    private static string ComputeVeDataLayout() =>
        "e-m:e-i64:64-n32:64-S128-v64:64:64-v128:64:64-v256:64:64-v512:64:64-" +
        "v1024:64:64-v2048:64:64-v4096:64:64-v8192:64:64-v16384:64:64";
}
