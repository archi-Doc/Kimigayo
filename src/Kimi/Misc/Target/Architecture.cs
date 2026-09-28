// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Target;

/// <summary>Mirror of llvm::Triple::ArchType (LLVM main, Aug 2026).</summary>
public enum Architecture
{
    Unknown = 0,

    Arm,            // ARM (little endian): arm, armv.*, xscale
    ArmEB,          // ARM (big endian): armeb
    AArch64,        // AArch64 (little endian): aarch64
    AArch64BE,      // AArch64 (big endian): aarch64_be
    AArch64_32,     // AArch64 (little endian) ILP32: aarch64_32
    Arc,            // ARC: Synopsys ARC
    Avr,            // AVR: Atmel AVR microcontroller
    BpfEL,          // eBPF (little endian)
    BpfEB,          // eBPF (big endian)
    Csky,           // CSKY
    Dxil,           // DXIL 32-bit DirectX bytecode
    Hexagon,        // Hexagon
    LoongArch32,    // LoongArch (32-bit)
    LoongArch64,    // LoongArch (64-bit)
    M68k,           // Motorola 680x0 family
    Mips,           // MIPS: mips, mipsallegrex, mipsr6
    MipsEL,         // MIPSEL: mipsel, mipsallegrexe, mipsr6el
    Mips64,         // MIPS64: mips64, mips64r6, mipsn32, mipsn32r6
    Mips64EL,       // MIPS64EL: mips64el, mips64r6el, mipsn32el, mipsn32r6el
    Msp430,         // MSP430
    Ppc,            // PPC: powerpc
    PpcLE,          // PPCLE: powerpc (little endian)
    Ppc64,          // PPC64: powerpc64, ppu
    Ppc64LE,        // PPC64LE: powerpc64le
    R600,           // R600: AMD GPUs HD2XXX - HD6XXX
    AmdGpu,         // AMDGPU: AMD GCN+ GPUs (formerly amdgcn)
    RiscV32,        // RISC-V (32-bit, little endian)
    RiscV64,        // RISC-V (64-bit, little endian)
    RiscV32BE,      // RISC-V (32-bit, big endian)
    RiscV64BE,      // RISC-V (64-bit, big endian)
    Sparc,          // Sparc
    SparcV9,        // Sparcv9
    SparcEL,        // Sparc (little endian)
    SystemZ,        // SystemZ: s390x
    Tce,            // OpenASIP big endian 32b
    TceLE,          // OpenASIP little endian 32b
    TceLE64,        // OpenASIP little endian 64b
    Thumb,          // Thumb (little endian): thumb, thumbv.*
    ThumbEB,        // Thumb (big endian)
    X86,            // X86: i[3-9]86
    X86_64,         // X86-64: amd64, x86_64
    XCore,          // XCore
    Xtensa,         // Tensilica Xtensa
    Nvptx,          // NVPTX: 32-bit
    Nvptx64,        // NVPTX: 64-bit
    AmdIL,          // AMDIL
    AmdIL64,        // AMDIL with 64-bit pointers
    Hsail,          // AMD HSAIL
    Hsail64,        // AMD HSAIL with 64-bit pointers
    Spir,           // SPIR: standard portable IR for OpenCL, 32-bit
    Spir64,         // SPIR: 64-bit
    SpirV,          // SPIR-V with logical memory layout
    SpirV32,        // SPIR-V with 32-bit pointers
    SpirV64,        // SPIR-V with 64-bit pointers
    Kalimba,        // Kalimba
    Shave,          // Movidius vector VLIW processors
    Lanai,          // Lanai 32-bit
    Wasm32,         // WebAssembly with 32-bit pointers
    Wasm64,         // WebAssembly with 64-bit pointers
    RenderScript32, // 32-bit RenderScript
    RenderScript64, // 64-bit RenderScript
    Ve,             // NEC SX-Aurora Vector Engine
}
