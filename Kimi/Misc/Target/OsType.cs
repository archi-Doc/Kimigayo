// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

/// <summary>Mirror of llvm::Triple::OSType (LLVM main, Aug 2026).</summary>
public enum OsType
{
    Unknown = 0,

    Darwin,      // "darwin"
    DragonFly,   // "dragonfly"
    FreeBSD,     // "freebsd"
    Fuchsia,     // "fuchsia"
    IOS,         // "ios"
    KFreeBSD,    // "kfreebsd"
    Linux,       // "linux"
    Lv2,         // "lv2" (PS3)
    MacOSX,      // "macosx" (alias: "macos")
    Managarm,    // "managarm"
    NetBSD,      // "netbsd"
    OpenBSD,     // "openbsd"
    Solaris,     // "solaris"
    UEFI,        // "uefi"
    Win32,       // "windows" (alias: "win32")
    ZOS,         // "zos"
    Haiku,       // "haiku"
    RTEMS,       // "rtems"
    AIX,         // "aix"
    Cuda,        // "cuda" (NVIDIA CUDA)
    NVCL,        // "nvcl" (NVIDIA OpenCL)
    AmdHsa,      // "amdhsa" (AMD HSA Runtime)
    PS4,         // "ps4"
    PS5,         // "ps5"
    ElfIamcu,    // "elfiamcu"
    TvOS,        // "tvos"
    WatchOS,     // "watchos"
    BridgeOS,    // "bridgeos"
    DriverKit,   // "driverkit"
    XROS,        // "xros" (alias: "visionos")
    Mesa3D,      // "mesa3d"
    AmdPal,      // "amdpal" (AMD PAL Runtime)
    HermitCore,  // "hermit"
    Hurd,        // "hurd" (GNU/Hurd)
    Wasi,        // "wasi" (deprecated alias of WASI 0.1)
    WasiP1,      // "wasip1" (WASI 0.1)
    WasiP2,      // "wasip2" (WASI 0.2)
    WasiP3,      // "wasip3" (WASI 0.3)
    Emscripten,  // "emscripten"
    ShaderModel, // "shadermodel" (DirectX)
    LiteOS,      // "liteos"
    Serenity,    // "serenity"
    Vulkan,      // "vulkan" (Vulkan SPIR-V)
    CheriotRtos, // "cheriotrtos"
    OpenCL,      // "opencl"
    ChipStar,    // "chipstar"
    Firmware,    // "firmware"
    Qurt,        // "qurt"
    H2,          // "h2"
}
