// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>
/// Identifies the supported primitive value kinds used by the compiler.
/// </summary>
public enum PrimitiveKind : byte
{
    /// <summary>
    /// A boolean value.
    /// </summary>
    Bool,

    /// <summary>
    /// A signed integer whose size matches the native pointer size of the target platform.
    /// </summary>
    Isize,

    /// <summary>
    /// An unsigned integer whose size matches the native pointer size of the target platform.
    /// </summary>
    Usize,

    /// <summary>
    /// An 8-bit signed integer.
    /// </summary>
    I8,

    /// <summary>
    /// A 16-bit signed integer.
    /// </summary>
    I16,

    /// <summary>
    /// A 32-bit signed integer.
    /// </summary>
    I32,

    /// <summary>
    /// A 64-bit signed integer.
    /// </summary>
    I64,

    /// <summary>
    /// A 128-bit signed integer.
    /// </summary>
    I128,

    /// <summary>
    /// An 8-bit unsigned integer.
    /// </summary>
    U8,

    /// <summary>
    /// A 16-bit unsigned integer.
    /// </summary>
    U16,

    /// <summary>
    /// A 32-bit unsigned integer.
    /// </summary>
    U32,

    /// <summary>
    /// A 64-bit unsigned integer.
    /// </summary>
    U64,

    /// <summary>
    /// A 128-bit unsigned integer.
    /// </summary>
    U128,

    /// <summary>
    /// A 32-bit floating-point number.
    /// </summary>
    F32,

    /// <summary>
    /// A 64-bit floating-point number.
    /// </summary>
    F64,
}
