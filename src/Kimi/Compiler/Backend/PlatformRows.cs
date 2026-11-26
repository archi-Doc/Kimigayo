// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// PlatformRows (IMPL 21.6; the Tatara plan's decision 30, B9d2): Kimi.Platform's requirements (SPEC 22.4.5), one row per requirement in
// its declaration order, which is the order of Binding's published witnesses (Binding.PlatformWitnesses) and of an Application's
// output roots after its startup body (Compilation.OutputRoots): the requirement's name, whether it takes the platform as its receiver
// (a requirement with `self`; one without is a Type function, SPEC 8) and its adapter's record (RuntimeAbi.PlatformMember*), which the
// none layer's routines call and ModuleCodegen defines over the witness, or declares where the module has no platform.
// Input: none. Output: the rows; row i reads the i-th witness and the i-th platform root.
// Invariants: the names and receivers are Platform.kimi's requirements' in declaration order, and each adapter's name is
// RuntimeAbi.PlatformMember followed by its requirement's name (KimiLibraryShapeTest pins both). The one receiverless row, `start`,
// returns the platform, so ModuleCodegen writes the platform storage at it and passes the storage to every result slot; the shape pins
// keep it true. No result mapping: the layer's template maps each result.
// Must not read Binding, a Type or syntax, or hold a per-module state.

namespace Kimi.Compiler.Backend;

/// <summary>A requirement of Kimi.Platform: its name, whether it takes the platform as its receiver, and its adapter's record.</summary>
/// <param name="Name">The requirement's name.</param>
/// <param name="Receiver">Whether the requirement takes the platform as its receiver (<c>self</c>).</param>
/// <param name="Adapter">The adapter the none layer's routines call.</param>
internal readonly record struct PlatformRow(string Name, bool Receiver, FunctionAbi Adapter);

/// <summary>Kimi.Platform's requirements, in declaration order (IMPL 21.6).</summary>
internal static class PlatformRows
{
    /// <summary>Gets the rows, in the requirements' declaration order.</summary>
    internal static PlatformRow[] Rows { get; } =
    [
        new("start", false, RuntimeAbi.PlatformMemberStart),
        new("mapPages", true, RuntimeAbi.PlatformMemberMapPages),
        new("releasePages", true, RuntimeAbi.PlatformMemberReleasePages),
        new("write", true, RuntimeAbi.PlatformMemberWrite),
        new("clock", true, RuntimeAbi.PlatformMemberClock),
        new("terminate", true, RuntimeAbi.PlatformMemberTerminate),
    ];
}
