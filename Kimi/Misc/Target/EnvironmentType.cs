// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

/// <summary>Mirror of llvm::Triple::EnvironmentType (LLVM main, Aug 2026).</summary>
public enum EnvironmentType
{
    Unknown = 0,

    Gnu,           // "gnu"
    GnuT64,        // "gnut64"
    GnuAbiN32,     // "gnuabin32"
    GnuAbi64,      // "gnuabi64"
    GnuEabi,       // "gnueabi"
    GnuEabiT64,    // "gnueabit64"
    GnuEabiHF,     // "gnueabihf"
    GnuEabiHFT64,  // "gnueabihft64"
    GnuF32,        // "gnuf32"
    GnuF64,        // "gnuf64"
    GnuSF,         // "gnusf"
    GnuX32,        // "gnux32"
    GnuIlp32,      // "gnu_ilp32"
    Code16,        // "code16"
    Eabi,          // "eabi"
    EabiHF,        // "eabihf"
    Android,       // "android"
    Musl,          // "musl"
    MuslAbiN32,    // "muslabin32"
    MuslAbi64,     // "muslabi64"
    MuslEabi,      // "musleabi"
    MuslEabiHF,    // "musleabihf"
    MuslF32,       // "muslf32"
    MuslSF,        // "muslsf"
    MuslX32,       // "muslx32"
    MuslWali,      // "muslwali"
    Llvm,          // "llvm"
    Msvc,          // "msvc"
    Itanium,       // "itanium"
    Cygnus,        // "cygnus"
    CoreCLR,       // "coreclr"
    Simulator,     // "simulator" (Apple simulator variants)
    MacABI,        // "macabi" (Mac Catalyst)

    // DirectX shader stages (order matters in LLVM; kept for parity).
    Pixel,         // "pixel"
    Vertex,        // "vertex"
    Geometry,      // "geometry"
    Hull,          // "hull"
    Domain,        // "domain"
    Compute,       // "compute"
    Library,       // "library"
    RayGeneration, // "raygeneration"
    Intersection,  // "intersection"
    AnyHit,        // "anyhit"
    ClosestHit,    // "closesthit"
    Miss,          // "miss"
    Callable,      // "callable"
    Mesh,          // "mesh"
    Amplification, // "amplification"
    RootSignature, // "rootsignature"

    OpenHos,       // "ohos"
    Mlibc,         // "mlibc"
    PAuthTest,     // "pauthtest"
    Mtia,          // "mtia"
}
