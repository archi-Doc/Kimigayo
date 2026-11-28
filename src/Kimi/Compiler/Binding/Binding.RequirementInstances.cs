// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.6.1: the Contract reference a requirement call selected, as Self's Type conforms to it, for TyCtxt.Witness and a selected
    // call's receiver (ProjectReceiver): a Contract without Type arguments is its own reference; a bound reference, given in the instance
    // (`reference`: `Indexable<Name>`, or `C<E>` read as `C<i32>`; a call's substitution holds it as its declaring Type), is the
    // conformance of Self's Type that names it (NamesReference, in the scope of Self's declaration: Self is closed or has an abstract
    // part). A requirement's function item records its reference as a Type (TyCtxt.Witness(item)), so its Contract declaration with a
    // reference of that declaration in the instance is that reference (DECISIONS R5-M70). Null when the call recorded none or no
    // instantiated reference names a conformance of Self's Type.
    internal BindingSymbol? InstanceContract(BindingSymbol? recorded, BoundType self, BoundType? reference, QuerySite site)
    {
        if (recorded is null || (!IsBoundContractReference(recorded) && !ReferenceEquals(reference?.Symbol, recorded)))
        {
            return recorded;
        }

        // A conformance of a generic Type names its reference in the Type's own parameters (`Self is Indexable<K>`): as written.
        return reference is null || self.Symbol is not { } owner ? null : this.SelectConformance(self, recorded, reference, this.ConstraintScope(owner.Declaration), site, out _)?.Contract;
    }

    // SPEC 21.3.1: a witness's Origin in its requirement call's terms, the call's Origins and inputs over the requirement's binder; an
    // owning input has no outer Origin slot, so its absence (null) stays.
    internal BoundOrigin WitnessOrigin(BoundOrigin? origin, Koto requirement, ReadOnlySpan<BoundOrigin> origins, ReadOnlySpan<BoundOrigin> inputs)
        => origin is null ? null! : this.SubstituteStoredOrigin(origin, requirement, origins, inputs);

    // SPEC 8.6.2, 9.6.1: a selected requirement call on a Self with no abstract part makes its witness's call, so the receiver of a
    // witness inherited through a base path borrows that base (the core's path; TyCtxt.Signature reads Witness's alike). The call keeps
    // its requirement and its acquisition's Origin and source; only the physical receiver Type becomes the base's `ref`, which the
    // core's Refused already guarantees the requirement receiver is.
    private void ProjectReceiver(CallPlan call, BoundType self, QuerySite site)
    {
        if (call.Target.ReceiverIndex < 0)
        {
            return;
        }

        var contract = this.InstanceContract(call.RequirementContract, self, this.types.SubstOf(call)!.Declaring, site);
        if (this.ResolveRequirementWitness(call.Target, self, contract, this.ConstraintScope(call.Target.Declaration), site) is not
            { Kind: RequirementWitnessKind.Conformance, Declaring: { } declaring, Function.BasePath: { } path })
        {
            return;
        }

        var operation = call.ReceiverOperation;
        if (call.Receiver is null)
        {
            foreach (var argument in call.ArgumentOperations)
            {
                if (argument.ParameterIndex == call.Target.ReceiverIndex)
                {
                    operation = argument;
                    break;
                }
            }
        }

        var receiver = operation.ParameterType ?? throw new InvalidOperationException("A base-path witness call has no acquired receiver.");
        var projected = this.types.Reference(SemanticsKind.Ref, declaring, receiver.Origin);
        call.SetReceiverProjection(operation with { ParameterType = projected, AdaptedType = projected, Kind = ArgumentOperationKind.BaseBorrow, BasePath = path, ObjectCompatibility = ConstraintProof.Proven }, path);
    }
}
