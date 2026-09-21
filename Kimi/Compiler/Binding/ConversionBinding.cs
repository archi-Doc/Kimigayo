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
    Borrow,
    PayloadBorrow,

    // SPEC 5.4-5.5: between raw pointer Types, or a raw pointer and usize.
    Pointer,
}
