// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen and its place writer (R5 package C 3.2, plan C2b; C0c brings Ref forward, DECISIONS R5-M78).
internal sealed partial class FunctionCodegen
{
    // Ref (IMPL 21.1.3: a shared, exclusive, two-phase or raw borrow is one nonnull pointer to its Place's storage): the address the place
    // writer gives, stored into the local.
    private void Ref(int local, MirPlace place, in SourceInfo source)
    {
        if (!this.Address(place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source);
            return;
        }

        this.Store(local, address);
    }

    // The place writer: a Place's address. A local without projection that has storage is its home; projections are C2a's rows, a static's
    // root C7b's, and a zero-sized local's address (the zero-sized address symbol) C2b's.
    private bool Address(MirPlace place, out Value address)
    {
        var known = IsLocal(place) && this.slots[place.Root].Home != Home.None;
        address = known ? new('h', place.Root) : default;
        return known;
    }
}
