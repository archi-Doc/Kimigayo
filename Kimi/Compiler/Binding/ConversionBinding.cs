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

    // SPEC 13.5.3: @move or an owning-Semantics spelling transfers a Movable Place, even a Copy one.
    Transfer,
    Borrow,
    PayloadBorrow,
    ObjectUpcast,

    // SPEC 5.4-5.5: between raw pointer Types, or a raw pointer and usize.
    Pointer,
}
