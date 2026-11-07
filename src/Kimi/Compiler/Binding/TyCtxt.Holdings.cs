// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The storage a value of a Type holds, published to the MIR analyses and every other reader (G416; contract in Binding.Holdings.cs).
internal sealed partial class TyCtxt
{
    // SPEC 3.3.4: Binding's one reading, one level at a time: a Type's entries appended to `into`, each read on with its Part, Written and
    // Holder; false at Binding's Limit. A function of the three inputs, total; a warm call allocates nothing.
    internal bool Holdings(BoundType? type, BoundType? written, BoundType? holder, List<Hold> into) => binding.Holdings(type, written, holder, into);
}
