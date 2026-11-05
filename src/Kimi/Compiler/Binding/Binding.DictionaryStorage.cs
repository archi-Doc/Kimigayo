// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundType Key, BoundType Value), CallPlan> dictionaryStorageCalls = new();

    // SPEC 12.4.3: a dictionary literal's findEntry call (CallPlan.Of), which reuses the same source definition, substitution and
    // equality witnesses as an ordinary library call over the literal's complete entry Types.
    internal CallPlan DictionaryStorageCall(BoundType key, BoundType value)
    {
        var symbol = this.Library.DictionaryStorage(DictionaryCallee.FindEntry);
        var parameters = ((FunctionKoto)symbol.Declaration).Parameters;
        if (!this.dictionaryStorageCalls.TryGetValue((key, value), out var call))
        {
            this.dictionaryStorageCalls.Add((key, value), call = new());
        }

        call.Set(symbol, BoundType.ISize, null, [], [key, value], inputOrigins: [parameters[0].Type.BoundType!.Origin!, parameters[1].Type.BoundType!.Origin!]);
        return call;
    }
}
