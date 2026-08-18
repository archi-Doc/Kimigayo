// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines the ownership and reference semantics of a type.
/// </summary>
/// <remarks>
/// Value and reference kinds occupy contiguous ranges for fast classification.
/// </remarks>
public enum SemanticsKind : byte
{
    /// <summary>
    /// An owned value. This is the default form used by <c>T</c>.
    /// </summary>
    Owner,

    /// <summary>
    /// A borrowed value expressed as <c>borrow/T</c>.
    /// </summary>
    Borrow,

    /// <summary>
    /// A stack-bound value expressed as <c>stack/T</c>.
    /// </summary>
    Stack,

    /// <summary>
    /// An owning reference expressed as <c>ownerref/T</c>.
    /// </summary>
    OwnerRef,

    /// <summary>
    /// A borrowed reference expressed as <c>/T</c> or <c>borrowref/T</c>.
    /// </summary>
    BorrowRef,

    /// <summary>
    /// A reference-counted reference expressed as <c>rc/T</c>.
    /// </summary>
    Rc,

    /// <summary>
    /// An atomically reference-counted reference expressed as <c>arc/T</c>.
    /// </summary>
    Arc,

    /// <summary>
    /// An unsafe reference expressed as <c>unsafe/T</c>.
    /// </summary>
    Unsafe,

    /// <summary>
    /// A generic semantics parameter expressed as <c>s/T</c>.
    /// </summary>
    Parameter = byte.MaxValue,
}
