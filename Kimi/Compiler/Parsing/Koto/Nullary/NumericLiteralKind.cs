// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents the recognized kind of numeric literal in the parser.
/// </summary>
public enum NumericLiteralKind
{
    /// <summary>
    /// The numeric literal is invalid or could not be classified.
    /// </summary>
    Invalid,

    /// <summary>
    /// A signed 8-bit integer literal.
    /// </summary>
    I8,

    /// <summary>
    /// A signed 16-bit integer literal.
    /// </summary>
    I16,

    /// <summary>
    /// A signed 32-bit integer literal.
    /// </summary>
    I32,

    /// <summary>
    /// A signed 64-bit integer literal.
    /// </summary>
    I64,

    /// <summary>
    /// A signed 128-bit integer literal.
    /// </summary>
    I128,

    /// <summary>
    /// A signed pointer-sized integer literal.
    /// </summary>
    ISize,

    /// <summary>
    /// An unsigned 8-bit integer literal.
    /// </summary>
    U8,

    /// <summary>
    /// An unsigned 16-bit integer literal.
    /// </summary>
    U16,

    /// <summary>
    /// An unsigned 32-bit integer literal.
    /// </summary>
    U32,

    /// <summary>
    /// An unsigned 64-bit integer literal.
    /// </summary>
    U64,

    /// <summary>
    /// An unsigned 128-bit integer literal.
    /// </summary>
    U128,

    /// <summary>
    /// An unsigned pointer-sized integer literal.
    /// </summary>
    USize,

    /// <summary>
    /// A 32-bit floating-point literal.
    /// </summary>
    F32,

    /// <summary>
    /// A 64-bit floating-point literal.
    /// </summary>
    F64,
}
