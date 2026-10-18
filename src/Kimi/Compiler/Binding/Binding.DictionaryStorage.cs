// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(DictionaryCallee Callee, BoundType Key, BoundType Value), CallPlan> dictionaryStorageCalls = new();

    // Closed compiler calls reuse the same source definitions, substitutions and equality witnesses as ordinary library calls. Only
    // complete Dictionary element Types reach these generation bridges, and only the two DictionaryStorage functions generic over
    // the entry Types; any other callee is a broken contract.
    internal CallPlan DictionaryStorageCall(DictionaryCallee callee, BoundType key, BoundType value)
    {
        var find = callee switch
        {
            DictionaryCallee.FindEntry => true,
            DictionaryCallee.ClearEntries => false,
            _ => throw new InvalidOperationException("A DictionaryStorage callee is not generic over the entry Types."),
        };
        var symbol = this.Library.DictionaryStorage(callee);
        var function = (FunctionKoto)symbol.Declaration;
        var identity = (callee, key, value);
        if (!this.dictionaryStorageCalls.TryGetValue(identity, out var call))
        {
            this.dictionaryStorageCalls.Add(identity, call = new());
        }

        var parameters = function.Parameters;
        var first = parameters[0].Type.BoundType!.Origin!;
        var result = find ? BoundType.ISize : BoundType.Unit;
        call.Set(symbol, result, null, [], [key, value], inputOrigins: find ? [first, parameters[1].Type.BoundType!.Origin!] : [first]);
        return call;
    }
}
