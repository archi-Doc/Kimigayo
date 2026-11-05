// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Dictionary rows of FunctionCodegen written in place (SPEC 4.3, 4.9.5, 4.9.7, 16.3.2; R5 plan C8e; DECISIONS R5-M124), beside the storage
// kernels (FunctionCodegen.Storage) and placeEntry, whose call runs its LegacyRow item's helper (FunctionCodegen.Intrinsics' Routine): a
// Dictionary from none, a call of its target, the body of the DictionaryStorage function the Dictionary runtime declares for construction
// (initialize, LegacyRuntimeHelpers.Construction), at the aggregate's Place; and a Dictionary's glue level, its glue item's Drop helper
// called with the glue's own parameters (the Array glue's form), which calls the glue's target, the clearEntries instance, when the entries
// may need a drop, then releases the buffer.
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

    // SPEC 4.9.7, 16.3.2: a Dictionary's glue level at its address, its glue item's Drop helper (P1c's row, which calls the glue's target,
    // the clearEntries instance, when it has one, then releases the buffer) with the glue's own parameters.
    private void DictionaryGlue(int glue, Value address, Site site)
    {
        if (this.module.DictionaryDrop(glue) is { } drop)
        {
            this.Destroy(drop, address, site);
        }
    }
}
