// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal enum ConversionBinding : byte
{
    None,
    Literal,
    Integer,
    Floating,
    Abrupt,
    Numeric,

    // SPEC 13.5.4.3: E@wrap<U> wraps an integer value to U; it never fails, and a direct literal is folded.
    Wrap,

    // SPEC 13.5.4.4: E@bits<U> reinterprets the bits between a floating-point Type and a same-width integer Type.
    Bits,
    Identity,

    // SPEC 13.5.3: @move transfers a Movable Place, even a Copy one.
    Transfer,
    Borrow,
    ObjectUpcast,

    // SPEC 13.5.5.1: E@follow selects the Place a ref/uniq value points to, or a proven complete Sealed payload.
    Follow,
    PayloadFollow,

    // SPEC 13.5.5.1 pair layers: E@follow on a pair s/U whose admitted set lies in value or valueborrow selects the
    // Place storing U: the operand itself for owner and the referent for ref and uniq.
    PairFollow,

    // SPEC 5.4-5.5: between raw pointer Types, or a raw pointer and usize.
    Pointer,

    // SPEC 5.4: P@raw takes the address of the written slot through an immediately ending shared borrow.
    Address,
}
