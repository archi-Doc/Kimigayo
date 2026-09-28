// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines the value kinds supported during compile-time evaluation.
/// </summary>
public enum BasicValueKind
{
    /// <summary>An invalid or unavailable value.</summary>
    Invalid,

    /// <summary>A Boolean value.</summary>
    Bool,

    /// <summary>A signed 64-bit integer value.</summary>
    I64,

    /// <summary>A 64-bit floating-point value.</summary>
    F64,

    /// <summary>A string value.</summary>
    String,
}
