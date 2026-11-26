// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns compiler/library identities, the vocabulary of how a call is served (IntrinsicDisposition, which a function's symbol publishes as
// BindingSymbol.Definition) and the intrinsic Contract table.
// Input: IntrinsicId; IntrinsicKind. Output: an intrinsic's written inputs; an intrinsic Contract's row; the categories of a function
// without a MIR body (BodylessKind, which TyCtxt.Bodyless gives).
// Each function has one disposition, set where its symbol is made (the library's registration from `#Intrinsic` or its catalog entry's
// Form row, $abort's, Binding.PrepareBuiltinTargets' shims), and each intrinsic Contract one row; temporary intrinsics keep their
// recorded removal stage.
// Must not create a second intrinsic allowlist or hand-written library path.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Compiler-owned identity vocabulary.

public enum IntrinsicKind : byte
{
    None,
    Copy,
    Owned,
    Callable,
    Sealed,
    ObjectPayload,
    PrimitiveInteger,
}

// The intrinsic Contract table (SPEC 8.7.1; DECISIONS R4-M89, package 2b-D §3.4): how Binding decides each compiler-intrinsic Contract.
// Input: an IntrinsicKind. Output: its row, which ProveConstraint, a Contract's Self clause, a struct's or enum's Self clause (plain
// or conditional), conformance verification and inheritance (VerifyConformance, InheritedConformances, VerifyInheritedConformance) and
// KimiLibrary's registration (the judged intrinsics' shapes without members, with their Parents as ancestors, which make them
// refinement parents) read.
// Invariants: one row per IntrinsicKind, None (a declared Contract) included; a new intrinsic adds a row, never a case at a reader.
// Must not: judge a capability (the capability engine's) or decide a conformance's members (contract matching's).

/// <summary>A row of the intrinsic Contract table.</summary>
internal readonly record struct IntrinsicContract
{
    /// <summary>Gets a value indicating whether the capability engine judges <c>T is K</c> (SPEC 8.7.2–8.7.4, 15.2.3): K is then a
    /// refinement parent without members (SPEC 8.4.3), and a conformance to K, an ancestor's included, is that judgment, as a Contract's
    /// <c>Self is K</c> is (SPEC 8.7.1): neither form opts the conforming Type in.</summary>
    internal bool Judged { get; init; }

    /// <summary>Gets the failure of <c>Self is K</c> in a struct or enum declaration; None when it declares a conformance (SPEC 8.7.1).</summary>
    internal BindingFailure Declaration { get; init; }

    /// <summary>Gets a value indicating whether a Contract's <c>Self is K</c> is a requirement and a premise (SPEC 8.7.1, 8.7.3).</summary>
    internal bool SelfRequirement { get; init; }

    /// <summary>Gets the judged intrinsics a premise <c>T is K</c> supplies by refinement (SPEC 8.3.2, 8.7.4: PrimitiveInteger's
    /// Copy and Owned), the registered shape's ancestors; null when none.</summary>
    internal IntrinsicKind[]? Parents { get; init; }

    private static readonly IntrinsicKind[] CopyAndOwned = [IntrinsicKind.Copy, IntrinsicKind.Owned];

    internal static IntrinsicContract Of(IntrinsicKind kind) => kind switch
    {
        IntrinsicKind.None => default,
        IntrinsicKind.Copy => new() { Judged = true, SelfRequirement = true }, // Derived Copy (SPEC 3.5.2).
        IntrinsicKind.Owned => new() { Judged = true, SelfRequirement = true }, // A checked assertion.
        IntrinsicKind.Callable => new() { Declaration = BindingFailure.InvalidConstraint }, // Bound as a Callable Constraint, never a Contract one.
        IntrinsicKind.Sealed => new() { Judged = true, Declaration = BindingFailure.InvalidConstraint },
        IntrinsicKind.ObjectPayload => new() { Judged = true, Declaration = BindingFailure.InvalidSelfClause, SelfRequirement = true }, // Never granted.
        IntrinsicKind.PrimitiveInteger => new() { Judged = true, Declaration = BindingFailure.ClosedContractConformance, Parents = CopyAndOwned }, // Twelve integer Types (8.7.4).
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>A compiler intrinsic (plan 5.8): an operation Kimigayo cannot express, which MIR calls as an <c>Intrinsic</c> callee and the
/// backend generates. <see cref="IntrinsicKind"/> is another concept, the compiler-known Contracts.</summary>
internal enum IntrinsicId : byte
{
    None,
    RawAllocate, // Memory (SPEC 5.6, 22.2).
    RawRelease,
    RawInitialize,
    RawSlice,
    TryAllocateBytes,
    MoveBytes, // A byte copy whose ranges do not overlap (transferBytes' contract; DECISIONS R5-M18).
    Stride, // Type facts (SPEC 22.2, IMPL 21.1.3); destroying the values of a cleanup-free T generates nothing.
    DestroyValues,
    Abort, // $abort (SPEC 17.4).
    Fail, // A fixed SPEC 22.5.2 code.
    WriteStdout, // SPEC 22.1, 22.5.
    FormatFloat, // Formatting (SPEC 22.9.1): Ryu, the code of a Writer's reserve, the erased reserve through it, i128 until a Kimigayo body replaces it (RQ16: a post-reduction unit, by measurement).
    ReserveDispatch,
    ErasedReserve,
    FormatInteger128,
    MakeObj, // Handles (IMPL 21.1.3, SPEC 13.6); the Weak operations come with P41.
    MakeRc,
    MakeArc,
    Clone,
    TypeTest,
    TestObserve, // Tests (SPEC 20.6.1, testing profile).
    TestMessage,
    TestRequireAbort,
    TestRecordValue,
    TestTempDirectory,
    StaticEnsure, // SPEC 22.4.3.
    VolatileRead, // Volatile access (SPEC 5.6).
    VolatileWrite,
    MonotonicClock, // SPEC 22.8.2, IMPL 21.5.5 (the Tatara plan's decision 25): the platform layer's monotonic clock.
}

/// <summary>What a function without a MIR body is (SPEC 8.11.2, 22.1, 22.6; TyCtxt.Bodyless), Binding's one category of it, which A4
/// turns into its effect class and the instance collector into what its call runs.</summary>
internal enum BodylessKind : byte
{
    /// <summary>None of the others: a broken contract for both readers in a complete compilation (the instance collector since PLAN G890,
    /// A4 since Z2: the located InternalInvariant_Kd at a call, a throw in its solve); in an incomplete one, a dependent of Binding's
    /// records for both (TyCtxt.BindingComplete).</summary>
    Unclassified,

    /// <summary>A library declaration's `#Intrinsic` (its Definition's IntrinsicId), whose effects are MIR's row of it.</summary>
    Intrinsic,

    /// <summary>A built-in target (Binding.PrepareBuiltinTargets), which acts only on its inputs.</summary>
    Shim,

    /// <summary>A `#LibraryImport` foreign function, an environment effect.</summary>
    Import,

    /// <summary>A library declaration the MIR builder writes as a MIR form (<see cref="DispositionKind.Form"/>): validated MIR has no
    /// Call of it (MirValidator), so A4 and the instance collector, which trust validated MIR, read none. No value of it reaches MIR
    /// either: every Form function is `unsafe` (its KimiLibraryShapeTest row), and Binding acquires such a function only as a direct
    /// callee (SPEC 7.6, Binding.DirectCallOnly). A Form function that is not `unsafe` would add the validator's MirConstKind.FnItem
    /// row, in its own unit.</summary>
    Form,

    /// <summary>A definition Binding rejected, whose parts are not published (DECISIONS R4-M18), or whose Body the parser recorded missing
    /// (SPEC 14.2.1): a dependent of that error.</summary>
    Dependent,
}

/// <summary>How a call of a compiler function is served (plan 5.8).</summary>
internal enum DispositionKind : byte
{
    Body, // A Kimigayo body, the declaration's own.
    Intrinsic, // An IntrinsicId; a family's id (IntrinsicDisposition.TakesMember) carries its member, Fail its SPEC 22.5.2 code.
    Shim, // A built-in witness (equality, comparison, arithmetic), whose body the backend generates for its instance.
    Form, // A call the builder writes as a MIR form, never a Call: its catalog entry's Form row (KimiLibraryCatalog.Entry).
}

/// <summary>How a call of a function is served (plan 5.8), which its symbol publishes (<see cref="BindingSymbol.Definition"/>): a library
/// declaration's `#Intrinsic` (IMPL 21.3.4), a built-in target's Shim, a cataloged lang item's Form, otherwise its Body.</summary>
/// <param name="Kind">How the call is served.</param>
/// <param name="Id">The intrinsic of an Intrinsic disposition.</param>
/// <param name="Member">A family's member, the case its second `#Intrinsic` argument names: <see cref="IntrinsicId.Fail"/>'s SPEC 22.5.2
/// code.</param>
internal readonly record struct IntrinsicDisposition(DispositionKind Kind, IntrinsicId Id = IntrinsicId.None, string? Member = null)
{
    // IMPL 21.3.4's family form `#Intrinsic("Family", "Member")`: whether an id is a family, whose declarations each name its member.
    internal static bool TakesMember(IntrinsicId id) => id is IntrinsicId.Fail;

    // SPEC 5.2, 7.7.3 (the owner's O-21 (ii)): the written inputs, whether an intrinsic's contract performs an access that needs
    // write capability through the raw pointer it takes as a parameter: a construction at `storage`, a relocation's destination and source
    // (the source as a Take), an in-place destruction and a volatile write at `storage`; a Dictionary entry's placement is the
    // DictionaryStorage.placeEntry body. Raw.release, Raw.slice and Raw.readVolatile write nothing.
    internal static bool WritesThrough(IntrinsicId id, int parameter) => id switch
    {
        IntrinsicId.RawInitialize or IntrinsicId.DestroyValues or IntrinsicId.VolatileWrite => parameter == 0,
        IntrinsicId.MoveBytes => parameter is 0 or 1,
        _ => false,
    };
}
