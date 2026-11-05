// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns whether a virtual slot exists for a declaring instance (Applies; DECISIONS R5-M62; R5 package F 4.1-4.3, S2-S4 X5), beside its
// struct's VTableDef.
// Input: an original and the layer of a representative that declares it.
// Output: true or false as Binding.VirtualApplicability proves it under the public premises, else null, which for a closed declaring Type
// is a broken contract its reader records as InternalInvariant_Kd in a complete compilation (DECISIONS R5-M151); false makes the slot's
// entry null (VTableDef's consumer rule). One answer per (original, declaring), discarded at an edit (ResetDispatch).
// Must not: write a diagnostic, HIR or pass state, or prove on an erased value.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    private readonly Dictionary<(BindingSymbol Original, BoundType Declaring), bool?> applicability = new();

    // SPEC 6.4.3: whether an original's slot exists for the declaring instance, the layer of the representative that declares the original
    // (Binding.VirtualApplicability under the public premises): Proven is true and Refuted false.
    internal bool? Applies(BindingSymbol original, BoundType declaring)
    {
        if (!this.applicability.TryGetValue((original, declaring), out var applies))
        {
            applies = binding.VirtualApplicability(new((FunctionKoto)original.Declaration, declaring)) switch
            {
                ConstraintProof.Proven => true,
                ConstraintProof.Refuted => false,
                _ => null,
            };
            this.applicability.Add((original, declaring), applies);
        }

        return applies;
    }
}
