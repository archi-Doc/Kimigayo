// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Dictionary row of FunctionCodegen written in place (SPEC 4.3, 4.9.5; R5 plan C8e; DECISIONS R5-M124): a Dictionary from none, a
// call of its target, the body of DictionaryStorage.initialize, at the aggregate's Place. A Dictionary's glue level is a collection's
// (FunctionCodegen.Glue's CollectionGlue; DECISIONS R8-M5).
internal sealed partial class FunctionCodegen
{
    // SPEC 4.3, 4.9.5: `[:]`, an aggregate of no operand whose Type is a Dictionary, allocates nothing: its target, initialize's body, writes
    // the handle's words at the Place, its one parameter (no location: it reports nothing).
    private void EmptyDictionary(in MirStatement statement)
    {
        var target = this.Target(new(this.at, this.position));
        if (target < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // The collector targets a Dictionary from none (K 4.5 row 11).
            return;
        }

        if (this.module.Signature(target) is { } initialize && this.Destination(statement, out var address))
        {
            _ = this.WriteCall(initialize, [address]);
        }
    }
}
