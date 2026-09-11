// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Stable compiler-owned Core catalog vocabulary.

public enum CoreDeclarationId : byte
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
    ObjectOwnership,
}

public enum CoreDeclarationState : byte
{
    Missing,
    Invalid,
    Validated,
}

/// <summary>A required identity and its current declaration validation; not runtime availability.</summary>
public readonly record struct CoreDeclaration(CoreDeclarationId Id, string Name, BindingSymbol? Symbol, CoreDeclarationState State);
