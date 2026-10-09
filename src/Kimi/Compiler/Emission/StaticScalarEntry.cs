// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>Closed physical storage and initialization ABI; contains no syntax or binding objects.</summary>
internal sealed class StaticScalarEntry(FunctionAbi initializer)
{
    internal FunctionAbi Initializer { get; } = initializer;

    // SPEC 22.5.4: each read passes the start of its Place expression, which a cycle Abort reports.
    internal FunctionAbi Getter { get; } = new(initializer.Name + ".get", initializer.Result, [new("ptr", "location", AbiParameterKind.Location), new("i64", "location_length", AbiParameterKind.LocationLength)]);

    internal string StateName { get; } = initializer.Name + ".state";

    internal string ValueName { get; } = initializer.Name + ".value";
}
