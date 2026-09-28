// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines the ownership, borrowing, object, and pointer semantics of a type.
/// </summary>
/// <remarks>
/// Kinds in the same Type Semantics category occupy contiguous ranges for fast classification.
/// </remarks>
public enum SemanticsKind : byte
{
    /// <summary>
    /// An owned value. This is the default form used by <c>T</c>.
    /// </summary>
    Owner,

    /// <summary>
    /// A shared borrowed reference to a value expressed as <c>ref/T</c>.
    /// </summary>
    Ref,

    /// <summary>
    /// An exclusive borrowed reference to a value expressed as <c>uniq/T</c>.
    /// </summary>
    Uniq,

    /// <summary>
    /// An owned object expressed as <c>obj/T</c>.
    /// </summary>
    Obj,

    /// <summary>
    /// A reference-counted object expressed as <c>rc/T</c>.
    /// </summary>
    Rc,

    /// <summary>
    /// An atomically reference-counted object expressed as <c>arc/T</c>.
    /// </summary>
    Arc,

    /// <summary>
    /// A shared borrowed reference to an object expressed as <c>objref/T</c>.
    /// </summary>
    ObjRef,

    /// <summary>
    /// An exclusive borrowed reference to an object expressed as <c>objuniq/T</c>.
    /// </summary>
    ObjUniq,

    /// <summary>
    /// An unsafe pointer expressed as <c>unsafe/T</c>.
    /// </summary>
    Unsafe,

    /// <summary>
    /// A generic semantics parameter expressed as <c>s/T</c>.
    /// </summary>
    Parameter = byte.MaxValue,
}
