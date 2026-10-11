// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(bool Find, BoundType Key, BoundType Value), CallPlan> dictionaryStorageCalls = new();

    // Closed compiler calls reuse the same source definitions, substitutions and equality witnesses as
    // ordinary library calls. Only complete Dictionary element Types reach these generation bridges.
    internal CallPlan DictionaryStorageCall(bool find, BoundType key, BoundType value)
    {
        var function = find ? this.compilation.Library.DictionaryFind : this.compilation.Library.DictionaryClear;
        var identity = (find, key, value);
        if (!this.dictionaryStorageCalls.TryGetValue(identity, out var call))
        {
            this.dictionaryStorageCalls.Add(identity, call = new());
        }

        var first = function.Parameters[0].Type.BoundType!.Origin!;
        call.Set(function.BoundSymbol!, find ? BoundType.ISize : BoundType.Unit, null, [], [key, value], inputOrigins: find ? [first, function.Parameters[1].Type.BoundType!.Origin!] : [first]);
        return call;
    }
}
