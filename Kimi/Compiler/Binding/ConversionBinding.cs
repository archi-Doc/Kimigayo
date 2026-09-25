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
    Identity,

    // SPEC 13.5.3: @move transfers a Movable Place, even a Copy one.
    Transfer,
    Borrow,
    ObjectUpcast,

    // SPEC 13.5.5.1: E@deref selects the Place a ref/uniq value points to, or a proven complete Sealed payload.
    Deref,
    PayloadDeref,

    // SPEC 5.4-5.5: between raw pointer Types, or a raw pointer and usize.
    Pointer,
}
