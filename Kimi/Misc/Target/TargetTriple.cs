// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

#pragma warning disable SA1623 // Property summary documentation should match accessors
#pragma warning disable SA1615 // Element return value should be documented
#pragma warning disable SA1611 // Element parameters should be documented

/// <summary>
/// Parsed LLVM target triple: ARCHITECTURE-VENDOR-OS[-ENVIRONMENT].
/// Faithful port of the llvm::Triple constructor (no normalization).
/// </summary>
public sealed record class TargetTriple(
    string Value,
    Architecture Arch,
    VendorType Vendor,
    OsType Os,
    EnvironmentType Environment,
    ObjectFormatType ObjectFormat,
    string ArchName,
    string VendorName,
    string OsName,
    string EnvironmentName)
{
    public static readonly TargetTriple Invalid = new(string.Empty, Architecture.Unknown, VendorType.Unknown, OsType.Unknown, EnvironmentType.Unknown, ObjectFormatType.Unknown, string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// Parse a target triple string. Mirrors Triple::Triple(std::string):
    /// split into at most 4 dash-separated components, parse each positionally.
    /// </summary>
    public static TargetTriple Parse(string triple)
    {
        ArgumentNullException.ThrowIfNull(triple);
        var s = triple.AsSpan();

        // Split on '-' with MaxSplit = 3 (the 4th component keeps any dashes,
        // e.g. object-format suffixes like "gnuelf").
        Span<Range> ranges = stackalloc Range[4];
        var count = 0;
        var start = 0;
        for (int i = 0; i < s.Length && count < 3; i++)
        {
            if (s[i] == '-')
            {
                ranges[count++] = new Range(start, i);
                start = i + 1;
            }
        }

        ranges[count++] = new Range(start, s.Length);

        var archSpan = s[ranges[0]];
        var arch = ParseArch(archSpan);
        var vendor = VendorType.Unknown;
        var os = OsType.Unknown;
        var env = EnvironmentType.Unknown;
        var format = ObjectFormatType.Unknown;
        string vendorName = string.Empty, osName = string.Empty, envName = string.Empty;

        if (count > 1)
        {
            ReadOnlySpan<char> vendorSpan = s[ranges[1]];
            vendor = ParseVendor(vendorSpan);
            vendorName = vendorSpan.ToString();
            if (count > 2)
            {
                ReadOnlySpan<char> osSpan = s[ranges[2]];
                os = ParseOs(osSpan);
                osName = osSpan.ToString();
                if (count > 3)
                {
                    ReadOnlySpan<char> envSpan = s[ranges[3]];
                    env = ParseEnvironment(envSpan);
                    format = ParseFormat(envSpan);
                    envName = envSpan.ToString();
                }
            }
        }
        else
        {
            // Single-component triple: certain MIPS arch names imply an
            // environment (mirrors the special case in the Triple constructor).
            env = ImpliedMipsEnvironment(archSpan);
        }

        var result = new TargetTriple(triple, arch, vendor, os, env, format, archSpan.ToString(), vendorName, osName, envName);

        if (format == ObjectFormatType.Unknown)
        {
            result = result with { ObjectFormat = GetDefaultFormat(result) };
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Architecture (port of Triple::parseArch + parseARMArch + parseBPFArch)
    // ------------------------------------------------------------------

    private static Architecture ParseArch(ReadOnlySpan<char> name)
    {
        // Exact matches first (span switch: length + char compare, no alloc).
        Architecture at = name switch
        {
            "i386" or "i486" or "i586" or "i686" or
            "i786" or "i886" or "i986" => Architecture.X86,
            "amd64" or "x86_64" or "x86_64h" or "x86_64_lfi" => Architecture.X86_64,
            "powerpc" or "powerpcspe" or "ppc" or "ppc32" => Architecture.Ppc,
            "powerpcle" or "ppcle" or "ppc32le" => Architecture.PpcLE,
            "powerpc64" or "ppu" or "ppc64" => Architecture.Ppc64,
            "powerpc64le" or "ppc64le" => Architecture.Ppc64LE,
            "xscale" => Architecture.Arm,
            "xscaleeb" => Architecture.ArmEB,
            "aarch64" or "arm64" or "arm64e" or "arm64ec" or "aarch64_lfi" => Architecture.AArch64,
            "aarch64_be" => Architecture.AArch64BE,
            "aarch64_32" or "arm64_32" => Architecture.AArch64_32,
            "arc" => Architecture.Arc,
            "arm" => Architecture.Arm,
            "armeb" => Architecture.ArmEB,
            "thumb" => Architecture.Thumb,
            "thumbeb" => Architecture.ThumbEB,
            "avr" => Architecture.Avr,
            "m68k" => Architecture.M68k,
            "msp430" => Architecture.Msp430,
            "mips" or "mipseb" or "mipsallegrex" or "mipsisa32r6" or "mipsr6" => Architecture.Mips,
            "mipsel" or "mipsallegrexel" or "mipsisa32r6el" or "mipsr6el" => Architecture.MipsEL,
            "mips64" or "mips64eb" or "mipsn32" or "mipsisa64r6" or
            "mips64r6" or "mipsn32r6" => Architecture.Mips64,
            "mips64el" or "mipsn32el" or "mipsisa64r6el" or "mips64r6el" or
            "mipsn32r6el" => Architecture.Mips64EL,
            "amdgcn" => Architecture.AmdGpu,
            "r600" => Architecture.R600,
            "riscv32" => Architecture.RiscV32,
            "riscv64" => Architecture.RiscV64,
            "riscv32be" => Architecture.RiscV32BE,
            "riscv64be" => Architecture.RiscV64BE,
            "hexagon" => Architecture.Hexagon,
            "s390x" or "systemz" => Architecture.SystemZ,
            "sparc" => Architecture.Sparc,
            "sparcel" => Architecture.SparcEL,
            "sparcv9" or "sparc64" => Architecture.SparcV9,
            "tce" => Architecture.Tce,
            "tcele" => Architecture.TceLE,
            "tcele64" => Architecture.TceLE64,
            "xcore" => Architecture.XCore,
            "nvptx" => Architecture.Nvptx,
            "nvptx64" => Architecture.Nvptx64,
            "amdil" => Architecture.AmdIL,
            "amdil64" => Architecture.AmdIL64,
            "hsail" => Architecture.Hsail,
            "hsail64" => Architecture.Hsail64,
            "spir" => Architecture.Spir,
            "spir64" => Architecture.Spir64,
            "spirv" or "spirv1.5" or "spirv1.6" => Architecture.SpirV,
            "spirv32" or "spirv32v1.0" or "spirv32v1.1" or "spirv32v1.2" or
            "spirv32v1.3" or "spirv32v1.4" or "spirv32v1.5" or "spirv32v1.6" => Architecture.SpirV32,
            "spirv64" or "spirv64v1.0" or "spirv64v1.1" or "spirv64v1.2" or
            "spirv64v1.3" or "spirv64v1.4" or "spirv64v1.5" or "spirv64v1.6" => Architecture.SpirV64,
            "lanai" => Architecture.Lanai,
            "renderscript32" => Architecture.RenderScript32,
            "renderscript64" => Architecture.RenderScript64,
            "shave" => Architecture.Shave,
            "ve" => Architecture.Ve,
            "wasm32" => Architecture.Wasm32,
            "wasm64" => Architecture.Wasm64,
            "csky" => Architecture.Csky,
            "loongarch32" => Architecture.LoongArch32,
            "loongarch64" => Architecture.LoongArch64,
            "dxil" or "dxilv1.0" or "dxilv1.1" or "dxilv1.2" or "dxilv1.3" or
            "dxilv1.4" or "dxilv1.5" or "dxilv1.6" or "dxilv1.7" or
            "dxilv1.8" or "dxilv1.9" => Architecture.Dxil,
            "xtensa" => Architecture.Xtensa,
            _ => Architecture.Unknown,
        };
        if (at != Architecture.Unknown)
        {
            return at;
        }

        // Prefix-matched families (mirrors StringSwitch::StartsWith cases).
        if (name.StartsWith("amdgpu"))
        {
            return Architecture.AmdGpu;
        }

        if (name.StartsWith("kalimba"))
        {
            return Architecture.Kalimba;
        }

        if (name.StartsWith("arm") || name.StartsWith("thumb") || name.StartsWith("aarch64"))
        {
            return ParseArmArch(name);
        }

        if (name.StartsWith("bpf"))
        {
            return ParseBpfArch(name);
        }

        return Architecture.Unknown;
    }

    /// <summary>
    /// Port of parseARMArch for versioned names ("armv7a", "thumbebv8", ...).
    /// The ISA/endianness split follows ARM::parseArchISA/parseArchEndian.
    /// </summary>
    private static Architecture ParseArmArch(ReadOnlySpan<char> name)
    {
        bool bigEndian;
        bool isThumb;
        ReadOnlySpan<char> rest;

        if (name.StartsWith("aarch64"))
        {
            // aarch64 has no versioned little/big-endian variants beyond _be,
            // which was matched exactly above; treat the rest as aarch64.
            return name.StartsWith("aarch64_be") ? Architecture.AArch64BE : Architecture.AArch64;
        }

        if (name.StartsWith("armeb"))
        {
            bigEndian = true;
            isThumb = false;
            rest = name[5..];
        }
        else if (name.StartsWith("thumbeb"))
        {
            bigEndian = true;
            isThumb = true;
            rest = name[7..];
        }
        else if (name.StartsWith("thumb"))
        {
            bigEndian = false;
            isThumb = true;
            rest = name[5..];
        }
        else if (name.StartsWith("arm"))
        {
            bigEndian = false;
            isThumb = false;
            rest = name[3..];
        }
        else
        {
            return Architecture.Unknown;
        }

        // Thumb only exists in v4+ (ARM::parseArchISA rejects thumbv2/v3).
        if (isThumb && (rest.StartsWith("v2") || rest.StartsWith("v3")))
        {
            return Architecture.Unknown;
        }

        // v6-M is Thumb-only: "armv6m" parses as thumb (LLVM parseARMArch).
        if (rest.StartsWith("v6m") || rest.StartsWith("v6sm") || rest.StartsWith("v6-m"))
        {
            isThumb = true;
        }

        if (isThumb)
        {
            return bigEndian ? Architecture.ThumbEB : Architecture.Thumb;
        }

        return bigEndian ? Architecture.ArmEB : Architecture.Arm;
    }

    private static Architecture ParseBpfArch(ReadOnlySpan<char> name)
    {
        // LLVM resolves plain "bpf" to the host endianness; on .NET platforms
        // we assume little-endian hosts (matches BitConverter.IsLittleEndian).
        if (name.SequenceEqual("bpf"))
        {
            return BitConverter.IsLittleEndian ? Architecture.BpfEL : Architecture.BpfEB;
        }

        if (name.SequenceEqual("bpf_be") || name.SequenceEqual("bpfeb"))
        {
            return Architecture.BpfEB;
        }

        if (name.SequenceEqual("bpf_le") || name.SequenceEqual("bpfel"))
        {
            return Architecture.BpfEL;
        }

        return Architecture.Unknown;
    }

    // ------------------------------------------------------------------
    // Vendor (exact match, port of parseVendor / TripleName.def)
    // ------------------------------------------------------------------

    private static VendorType ParseVendor(ReadOnlySpan<char> name) => name switch
    {
        "amd" => VendorType.AMD,
        "apple" => VendorType.Apple,
        "csr" => VendorType.CSR,
        "fsl" => VendorType.Freescale,
        "ibm" => VendorType.IBM,
        "img" => VendorType.ImaginationTechnologies,
        "intel" => VendorType.Intel,
        "mesa" => VendorType.Mesa,
        "mti" => VendorType.MipsTechnologies,
        "nvidia" => VendorType.Nvidia,
        "oe" => VendorType.OpenEmbedded,
        "pc" => VendorType.PC,
        "scei" or "sie" => VendorType.SCEI,
        "suse" => VendorType.SUSE,
        "meta" => VendorType.Meta,
        _ => VendorType.Unknown,
    };

    // ------------------------------------------------------------------
    // OS (prefix match; declaration order from TripleName.def is significant:
    // "wasip1/2/3" before "wasi", "macosx" before "macos", etc.)
    // ------------------------------------------------------------------

    private static OsType ParseOs(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            return OsType.Unknown;
        }

        // Bucket by first character to minimize StartsWith calls; the order
        // inside each bucket preserves the TripleName.def declaration order.
        switch (name[0])
        {
            case 'a':
                if (name.StartsWith("aix"))
                {
                    return OsType.AIX;
                }

                if (name.StartsWith("amdhsa"))
                {
                    return OsType.AmdHsa;
                }

                if (name.StartsWith("amdpal"))
                {
                    return OsType.AmdPal;
                }

                break;
            case 'b':
                if (name.StartsWith("bridgeos"))
                {
                    return OsType.BridgeOS;
                }

                break;
            case 'c':
                if (name.StartsWith("cuda"))
                {
                    return OsType.Cuda;
                }

                if (name.StartsWith("cheriotrtos"))
                {
                    return OsType.CheriotRtos;
                }

                if (name.StartsWith("chipstar"))
                {
                    return OsType.ChipStar;
                }

                break;
            case 'd':
                if (name.StartsWith("darwin"))
                {
                    return OsType.Darwin;
                }

                if (name.StartsWith("dragonfly"))
                {
                    return OsType.DragonFly;
                }

                if (name.StartsWith("driverkit"))
                {
                    return OsType.DriverKit;
                }

                break;
            case 'e':
                if (name.StartsWith("elfiamcu"))
                {
                    return OsType.ElfIamcu;
                }

                if (name.StartsWith("emscripten"))
                {
                    return OsType.Emscripten;
                }

                break;
            case 'f':
                if (name.StartsWith("freebsd"))
                {
                    return OsType.FreeBSD;
                }

                if (name.StartsWith("fuchsia"))
                {
                    return OsType.Fuchsia;
                }

                if (name.StartsWith("firmware"))
                {
                    return OsType.Firmware;
                }

                break;
            case 'h':
                if (name.StartsWith("haiku"))
                {
                    return OsType.Haiku;
                }

                if (name.StartsWith("hermit"))
                {
                    return OsType.HermitCore;
                }

                if (name.StartsWith("hurd"))
                {
                    return OsType.Hurd;
                }

                if (name.StartsWith("h2"))
                {
                    return OsType.H2;
                }

                break;
            case 'i':
                if (name.StartsWith("ios"))
                {
                    return OsType.IOS;
                }

                break;
            case 'k':
                if (name.StartsWith("kfreebsd"))
                {
                    return OsType.KFreeBSD;
                }

                break;
            case 'l':
                if (name.StartsWith("linux"))
                {
                    return OsType.Linux;
                }

                if (name.StartsWith("lv2"))
                {
                    return OsType.Lv2;
                }

                if (name.StartsWith("liteos"))
                {
                    return OsType.LiteOS;
                }

                break;
            case 'm':
                if (name.StartsWith("macosx"))
                {
                    return OsType.MacOSX;
                }

                if (name.StartsWith("macos"))
                {
                    return OsType.MacOSX;   // alias
                }

                if (name.StartsWith("managarm"))
                {
                    return OsType.Managarm;
                }

                if (name.StartsWith("mesa3d"))
                {
                    return OsType.Mesa3D;
                }

                break;
            case 'n':
                if (name.StartsWith("netbsd"))
                {
                    return OsType.NetBSD;
                }

                if (name.StartsWith("nvcl"))
                {
                    return OsType.NVCL;
                }

                break;
            case 'o':
                if (name.StartsWith("openbsd"))
                {
                    return OsType.OpenBSD;
                }

                if (name.StartsWith("opencl"))
                {
                    return OsType.OpenCL;
                }

                break;
            case 'p':
                if (name.StartsWith("ps4"))
                {
                    return OsType.PS4;
                }

                if (name.StartsWith("ps5"))
                {
                    return OsType.PS5;
                }

                break;
            case 'q':
                if (name.StartsWith("qurt"))
                {
                    return OsType.Qurt;
                }

                break;
            case 'r':
                if (name.StartsWith("rtems"))
                {
                    return OsType.RTEMS;
                }

                break;
            case 's':
                if (name.StartsWith("solaris"))
                {
                    return OsType.Solaris;
                }

                if (name.StartsWith("shadermodel"))
                {
                    return OsType.ShaderModel;
                }

                if (name.StartsWith("serenity"))
                {
                    return OsType.Serenity;
                }

                break;
            case 't':
                if (name.StartsWith("tvos"))
                {
                    return OsType.TvOS;
                }

                break;
            case 'u':
                if (name.StartsWith("uefi"))
                {
                    return OsType.UEFI;
                }

                break;
            case 'v':
                if (name.StartsWith("visionos"))
                {
                    return OsType.XROS; // alias
                }

                if (name.StartsWith("vulkan"))
                {
                    return OsType.Vulkan;
                }

                break;
            case 'w':
                if (name.StartsWith("windows"))
                {
                    return OsType.Win32;
                }

                if (name.StartsWith("win32"))
                {
                    return OsType.Win32;   // alias
                }

                if (name.StartsWith("watchos"))
                {
                    return OsType.WatchOS;
                }

                if (name.StartsWith("wasip1"))
                {
                    return OsType.WasiP1;
                }

                if (name.StartsWith("wasip2"))
                {
                    return OsType.WasiP2;
                }

                if (name.StartsWith("wasip3"))
                {
                    return OsType.WasiP3;
                }

                if (name.StartsWith("wasi"))
                {
                    return OsType.Wasi;
                }

                break;
            case 'x':
                if (name.StartsWith("xros"))
                {
                    return OsType.XROS;
                }

                break;
            case 'z':
                if (name.StartsWith("zos"))
                {
                    return OsType.ZOS;
                }

                break;
        }

        return OsType.Unknown;
    }

    // ------------------------------------------------------------------
    // Environment (prefix match; TripleName.def order is significant:
    // "eabihf" before "eabi", gnu*/musl* variants before "gnu"/"musl")
    // ------------------------------------------------------------------

    private static EnvironmentType ParseEnvironment(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            return EnvironmentType.Unknown;
        }

        switch (name[0])
        {
            case 'a':
                if (name.StartsWith("android"))
                {
                    return EnvironmentType.Android;
                }

                if (name.StartsWith("anyhit"))
                {
                    return EnvironmentType.AnyHit;
                }

                if (name.StartsWith("amplification"))
                {
                    return EnvironmentType.Amplification;
                }

                break;
            case 'c':
                if (name.StartsWith("code16"))
                {
                    return EnvironmentType.Code16;
                }

                if (name.StartsWith("cygnus"))
                {
                    return EnvironmentType.Cygnus;
                }

                if (name.StartsWith("coreclr"))
                {
                    return EnvironmentType.CoreCLR;
                }

                if (name.StartsWith("compute"))
                {
                    return EnvironmentType.Compute;
                }

                if (name.StartsWith("closesthit"))
                {
                    return EnvironmentType.ClosestHit;
                }

                if (name.StartsWith("callable"))
                {
                    return EnvironmentType.Callable;
                }

                break;
            case 'd':
                if (name.StartsWith("domain"))
                {
                    return EnvironmentType.Domain;
                }

                break;
            case 'e':
                if (name.StartsWith("eabihf"))
                {
                    return EnvironmentType.EabiHF;
                }

                if (name.StartsWith("eabi"))
                {
                    return EnvironmentType.Eabi;
                }

                break;
            case 'g':
                if (name.StartsWith("gnuabin32"))
                {
                    return EnvironmentType.GnuAbiN32;
                }

                if (name.StartsWith("gnuabi64"))
                {
                    return EnvironmentType.GnuAbi64;
                }

                if (name.StartsWith("gnueabihft64"))
                {
                    return EnvironmentType.GnuEabiHFT64;
                }

                if (name.StartsWith("gnueabihf"))
                {
                    return EnvironmentType.GnuEabiHF;
                }

                if (name.StartsWith("gnueabit64"))
                {
                    return EnvironmentType.GnuEabiT64;
                }

                if (name.StartsWith("gnueabi"))
                {
                    return EnvironmentType.GnuEabi;
                }

                if (name.StartsWith("gnuf32"))
                {
                    return EnvironmentType.GnuF32;
                }

                if (name.StartsWith("gnuf64"))
                {
                    return EnvironmentType.GnuF64;
                }

                if (name.StartsWith("gnusf"))
                {
                    return EnvironmentType.GnuSF;
                }

                if (name.StartsWith("gnux32"))
                {
                    return EnvironmentType.GnuX32;
                }

                if (name.StartsWith("gnu_ilp32"))
                {
                    return EnvironmentType.GnuIlp32;
                }

                if (name.StartsWith("gnut64"))
                {
                    return EnvironmentType.GnuT64;
                }

                if (name.StartsWith("gnu"))
                {
                    return EnvironmentType.Gnu;
                }

                if (name.StartsWith("geometry"))
                {
                    return EnvironmentType.Geometry;
                }

                break;
            case 'h':
                if (name.StartsWith("hull"))
                {
                    return EnvironmentType.Hull;
                }

                break;
            case 'i':
                if (name.StartsWith("itanium"))
                {
                    return EnvironmentType.Itanium;
                }

                if (name.StartsWith("intersection"))
                {
                    return EnvironmentType.Intersection;
                }

                break;
            case 'l':
                if (name.StartsWith("library"))
                {
                    return EnvironmentType.Library;
                }

                if (name.StartsWith("llvm"))
                {
                    return EnvironmentType.Llvm;
                }

                break;
            case 'm':
                if (name.StartsWith("msvc"))
                {
                    return EnvironmentType.Msvc;
                }

                if (name.StartsWith("muslabin32"))
                {
                    return EnvironmentType.MuslAbiN32;
                }

                if (name.StartsWith("muslabi64"))
                {
                    return EnvironmentType.MuslAbi64;
                }

                if (name.StartsWith("musleabihf"))
                {
                    return EnvironmentType.MuslEabiHF;
                }

                if (name.StartsWith("musleabi"))
                {
                    return EnvironmentType.MuslEabi;
                }

                if (name.StartsWith("muslf32"))
                {
                    return EnvironmentType.MuslF32;
                }

                if (name.StartsWith("muslsf"))
                {
                    return EnvironmentType.MuslSF;
                }

                if (name.StartsWith("muslx32"))
                {
                    return EnvironmentType.MuslX32;
                }

                if (name.StartsWith("muslwali"))
                {
                    return EnvironmentType.MuslWali;
                }

                if (name.StartsWith("musl"))
                {
                    return EnvironmentType.Musl;
                }

                if (name.StartsWith("macabi"))
                {
                    return EnvironmentType.MacABI;
                }

                if (name.StartsWith("miss"))
                {
                    return EnvironmentType.Miss;
                }

                if (name.StartsWith("mesh"))
                {
                    return EnvironmentType.Mesh;
                }

                if (name.StartsWith("mlibc"))
                {
                    return EnvironmentType.Mlibc;
                }

                if (name.StartsWith("mtia"))
                {
                    return EnvironmentType.Mtia;
                }

                break;
            case 'o':
                if (name.StartsWith("ohos"))
                {
                    return EnvironmentType.OpenHos;
                }

                break;
            case 'p':
                if (name.StartsWith("pixel"))
                {
                    return EnvironmentType.Pixel;
                }

                if (name.StartsWith("pauthtest"))
                {
                    return EnvironmentType.PAuthTest;
                }

                break;
            case 'r':
                if (name.StartsWith("raygeneration"))
                {
                    return EnvironmentType.RayGeneration;
                }

                if (name.StartsWith("rootsignature"))
                {
                    return EnvironmentType.RootSignature;
                }

                break;
            case 's':
                if (name.StartsWith("simulator"))
                {
                    return EnvironmentType.Simulator;
                }

                break;
            case 'v':
                if (name.StartsWith("vertex"))
                {
                    return EnvironmentType.Vertex;
                }

                break;
        }

        return EnvironmentType.Unknown;
    }

    // ------------------------------------------------------------------
    // Object format (port of parseFormat; suffix match on the 4th component)
    // ------------------------------------------------------------------

    private static ObjectFormatType ParseFormat(ReadOnlySpan<char> name)
    {
        // "xcoff" must be tested before "coff" (order-dependent suffix match).
        if (name.EndsWith("xcoff"))
        {
            return ObjectFormatType.Xcoff;
        }

        if (name.EndsWith("coff"))
        {
            return ObjectFormatType.Coff;
        }

        if (name.EndsWith("elf"))
        {
            return ObjectFormatType.Elf;
        }

        if (name.EndsWith("goff"))
        {
            return ObjectFormatType.Goff;
        }

        if (name.EndsWith("macho"))
        {
            return ObjectFormatType.MachO;
        }

        if (name.EndsWith("wasm"))
        {
            return ObjectFormatType.Wasm;
        }

        if (name.EndsWith("spirv"))
        {
            return ObjectFormatType.SpirV;
        }

        return ObjectFormatType.Unknown;
    }

    private static EnvironmentType ImpliedMipsEnvironment(ReadOnlySpan<char> arch)
    {
        if (arch.StartsWith("mipsn32"))
        {
            return EnvironmentType.GnuAbiN32;
        }

        if (arch.StartsWith("mips64"))
        {
            return EnvironmentType.GnuAbi64;
        }

        if (arch.StartsWith("mipsisa64"))
        {
            return EnvironmentType.GnuAbi64;
        }

        if (arch.StartsWith("mipsisa32"))
        {
            return EnvironmentType.Gnu;
        }

        return arch switch
        {
            "mips" or "mipsel" or "mipsr6" or "mipsr6el" => EnvironmentType.Gnu,
            _ => EnvironmentType.Unknown,
        };
    }

    // ------------------------------------------------------------------
    // Default object format (port of getDefaultFormat)
    // ------------------------------------------------------------------

    private static ObjectFormatType GetDefaultFormat(TargetTriple t)
    {
        switch (t.Arch)
        {
            case Architecture.Unknown:
            case Architecture.AArch64:
            case Architecture.AArch64_32:
            case Architecture.Arm:
            case Architecture.Thumb:
            case Architecture.X86:
            case Architecture.X86_64:
                if (t.Os is OsType.Win32 or OsType.UEFI)
                {
                    return ObjectFormatType.Coff;
                }

                return t.IsOsDarwin ? ObjectFormatType.MachO : ObjectFormatType.Elf;

            case Architecture.MipsEL:
                return t.Os == OsType.Win32 ? ObjectFormatType.Coff : ObjectFormatType.Elf;

            case Architecture.Ppc:
            case Architecture.Ppc64:
                if (t.Os == OsType.AIX)
                {
                    return ObjectFormatType.Xcoff;
                }

                if (t.IsOsDarwin)
                {
                    return ObjectFormatType.MachO;
                }

                return ObjectFormatType.Elf;

            case Architecture.SystemZ:
                return t.Os == OsType.ZOS ? ObjectFormatType.Goff : ObjectFormatType.Elf;

            case Architecture.Wasm32:
            case Architecture.Wasm64:
                return ObjectFormatType.Wasm;

            case Architecture.SpirV:
            case Architecture.SpirV32:
            case Architecture.SpirV64:
                return ObjectFormatType.SpirV;

            case Architecture.Dxil:
                return ObjectFormatType.DXContainer;

            default:
                return ObjectFormatType.Elf;
        }
    }

    // ------------------------------------------------------------------
    // Convenience predicates (subset of Triple.h used by data layout code)
    // ------------------------------------------------------------------

    /// <summary>macOS, iOS, tvOS, watchOS, DriverKit, XROS, bridgeOS or Apple firmware.</summary>
    public bool IsOsDarwin =>
        this.Os is OsType.Darwin or OsType.MacOSX or OsType.IOS or OsType.TvOS
           or OsType.WatchOS or OsType.DriverKit or OsType.XROS or OsType.BridgeOS
        || (this.Vendor == VendorType.Apple && this.Os == OsType.Firmware);

    public bool IsOsWindows => this.Os == OsType.Win32;

    public bool IsWindowsMsvcEnvironment =>
        this.IsOsWindows && this.Environment is EnvironmentType.Msvc or EnvironmentType.Unknown;

    public bool IsMusl =>
        this.Environment is EnvironmentType.Musl or EnvironmentType.MuslAbiN32
            or EnvironmentType.MuslAbi64 or EnvironmentType.MuslEabi
            or EnvironmentType.MuslEabiHF or EnvironmentType.MuslF32
            or EnvironmentType.MuslSF or EnvironmentType.MuslX32
            or EnvironmentType.MuslWali or EnvironmentType.OpenHos
        || this.Os == OsType.LiteOS;

    /// <summary>x32 ABI: 32-bit pointers on x86-64.</summary>
    public bool IsX32 => this.Environment is EnvironmentType.GnuX32 or EnvironmentType.MuslX32;

    /// <summary>MIPS N32 ABI.</summary>
    public bool IsAbiN32 => this.Environment is EnvironmentType.GnuAbiN32 or EnvironmentType.MuslAbiN32;

    public bool IsOsBinFormatMachO => this.ObjectFormat == ObjectFormatType.MachO;

    public bool IsOsBinFormatCoff => this.ObjectFormat == ObjectFormatType.Coff;

    public bool IsOsBinFormatGoff => this.ObjectFormat == ObjectFormatType.Goff;

    public bool IsOsBinFormatXcoff => this.ObjectFormat == ObjectFormatType.Xcoff;

    /// <summary>Port of Triple::isLittleEndian (enumerated by big-endian set).</summary>
    public bool IsLittleEndian => this.Arch switch
    {
        Architecture.ArmEB or Architecture.ThumbEB or Architecture.AArch64BE or
        Architecture.BpfEB or Architecture.M68k or Architecture.Mips or
        Architecture.Mips64 or Architecture.Ppc or Architecture.Ppc64 or
        Architecture.RiscV32BE or Architecture.RiscV64BE or Architecture.Sparc or
        Architecture.SparcV9 or Architecture.SystemZ or Architecture.Tce or
        Architecture.Lanai => false,
        _ => true,
    };

    /// <summary>
    /// Parse the major version number embedded in the OS component
    /// (e.g. "freebsd13.2" -> 13). Returns 0 when absent.
    /// </summary>
    public int OsMajorVersion
    {
        get
        {
            ReadOnlySpan<char> s = this.OsName.AsSpan();
            int i = 0;
            while (i < s.Length && !char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            int v = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                v = (v * 10) + (s[i++] - '0');
            }

            return v;
        }
    }

    public override string ToString() => this.Value;
}
