// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Stable compiler-owned Kimi catalog vocabulary.

public enum KimiDeclarationId : byte
{
    Copy,
    Owned,
    Callable,
    WriteLine,
    Option,
    Result,
    Array,
    Index,
    Range,
    ResolvedRange,
    Slice,
    Dictionary,
    Stringify,
    Equatable,
    Comparable,
    Iterator,
    Iterable,
    ObjectOwnership, // Reserved legacy aggregate ID; no longer a declaration entry.
    Sealed,
    Replace,
    Exchange,
    Swap,
    MakeObj,
    MakeRc,
    MakeArc,
    Clone,
    Downgrade,
    Upgrade,
    MakeRcCyclic,
    MakeArcCyclic,
    Weak,
    TestTempDirectory,
    ArrayReserve,
    ArrayAppend,
    ArrayInsert,
    ArrayPop,
    ArrayRemove,
    ArrayClear,
    ArrayShrinkToFit,
}

public enum KimiDeclarationState : byte
{
    Missing,
    Invalid,
    Validated,
}

/// <summary>A required identity and its current declaration validation; not runtime availability.</summary>
public readonly record struct KimiDeclaration(KimiDeclarationId Id, string Name, BindingSymbol? Symbol, KimiDeclarationState State);
