// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>Closed physical storage and initialization ABI; contains no syntax or binding objects.</summary>
internal sealed class StaticScalarEntry(FunctionAbi initializer)
{
    internal FunctionAbi Initializer { get; } = initializer;

    internal FunctionAbi Getter { get; } = new(initializer.Name + ".get", initializer.Result, []);

    internal string StateName { get; } = initializer.Name + ".state";

    internal string ValueName { get; } = initializer.Name + ".value";

    internal int Location { get; set; }

    internal int Message { get; set; }
}
