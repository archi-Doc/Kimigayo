// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Compiler-owned identity vocabulary.

public enum IntrinsicKind : byte
{
    None,
    Copy,
    Owned,
    Callable,
    Sealed,
}

/// <summary>Identifies a compiler-provided language function implementation.</summary>
public enum CompilerFunctionKind : byte
{
    None,
    WriteLine,
    Abort,
    Replace,
    Exchange,
    Swap,
    MakeObj,
}
