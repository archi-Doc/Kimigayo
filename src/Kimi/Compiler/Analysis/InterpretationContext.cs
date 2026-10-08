// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>
/// SPEC 7.2.3, 8.10: the context in which an operation of an ownership body interprets declared information. A default
/// evaluated inline at a call is an instantiation of its verified declaration: inside its interval, declared Types and calls
/// take that call's bindings, which already compose every enclosing default's, and then the body's Semantics case or
/// instance. Outside every interval the root context applies only the body's case or instance.
/// </summary>
/// <param name="Index">The default-context index within its owning body, or -1 for the root context.</param>
internal readonly record struct InterpretationContext(int Index)
{
    /// <summary>Gets the root context, outside every default interval.</summary>
    internal static InterpretationContext Root => new(-1);

    /// <summary>Gets a value indicating whether no default substitution applies.</summary>
    internal bool IsRoot => this.Index < 0;
}
