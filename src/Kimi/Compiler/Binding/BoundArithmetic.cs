// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed class BoundArithmetic
{
    internal List<BindingSymbol> Candidates { get; } = new();

    internal List<Koto> Sources { get; } = new();

    internal InvocationKoto? Call { get; set; }

    internal bool Active { get; set; }
}
