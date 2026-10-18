// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// Requirement edges (K package 4.5 row 3 and its witness results; F package 4.5; S2-S4 X14, X16, X28; DECISIONS R5-M61, R5-M67) and the
// targets of the shim items.
internal sealed partial class InstanceCollector
{
    // K 4.5 row 3: what a requirement call runs in the item's instance, TyCtxt.Witness of its requirement, the Contract reference it
    // recorded and its composed substitution (X16): a Body's function item, the implementation its instance selects (SPEC 8.13.3; -1
    // without a body); a Builtin's shim item, keyed by the erased Self and its compiler function; a Storage witness's property-witness
    // item, keyed by its Field, the erased Self and its operation (X14, X28), whose key's signature agrees with the requirement's in the
    // instance. OCC-X's refusal and a Self whose storage does not complete are capability gaps at the call; None, no answer for the closed
    // Self of an accepted program, and a Storage witness whose signature disagrees are broken contracts; a kind without a row is a fault.
    private int Requirement(MirProgram program, int item, SourceRef subject, in MirCallee callee, Subst subst)
    {
        var target = this.types.Witness(callee.Symbol!, callee.Contract, subst);
        DiagnosticCode? refused = target.Kind switch
        {
            RequirementTargetKind.Body or RequirementTargetKind.Builtin => null,
            RequirementTargetKind.Storage when this.Agrees(callee.Symbol!, subst, target) => null,
            RequirementTargetKind.Storage or RequirementTargetKind.None => DiagnosticCode.InternalInvariant_Kd,
            RequirementTargetKind.Refused => Capability.Code(CapabilityGap.ExclusiveInheritedWitness),
            RequirementTargetKind.Limit => Capability.Code(CapabilityGap.StorageNesting),
            _ => throw new InvalidOperationException($"The witness kind {target.Kind} has no edge row."),
        };

        if (refused is { } code)
        {
            this.Set.Report(code, subject);
            return -1;
        }

        if (target.Kind == RequirementTargetKind.Body)
        {
            var body = program.Callee(new(MirCalleeKind.Fn, this.types.Implementation(target.Target!, target.Subst, 0)));
            return body is null ? -1 : this.Admit(item, subject, body, target.Subst);
        }

        var key = target.Kind == RequirementTargetKind.Builtin ? new MonoKey(MonoKind.Builtin, default, Variant: (byte)target.Target!.CompilerFunction)
            : new MonoKey(MonoKind.PropertyWitness, default, Symbol: target.Target, Variant: (byte)target.Access);
        return this.AdmitType(item, subject, key, subst.Self!);
    }

    // F 4.5: C3c writes a property-witness shim from its key alone, since Binding admits a standard witness only with a ref/Self getter or a
    // uniq/Self setter, a result F or ref/F and a setter's input F (Binding.PropertyMatching.cs:102-117), F the Field's Type in Self; the
    // requirement's signature in the call's instance (TyCtxt.Signature) is the key's, Type for Type up to Origins (Erase).
    private bool Agrees(BindingSymbol requirement, Subst subst, in RequirementTarget target)
    {
        var self = subst.Self!;
        if (!AdtDef.FindField(self, target.Target, out var field, out _))
        {
            return false;
        }

        var set = target.Access == PropertyWitnessKind.StorageSet;
        var receiver = this.types.Reference(set ? SemanticsKind.Uniq : SemanticsKind.Ref, self);
        var result = target.Access switch
        {
            PropertyWitnessKind.StorageCopy => field!,
            PropertyWitnessKind.StorageBorrow => this.types.Reference(SemanticsKind.Ref, field!),
            PropertyWitnessKind.StorageSet => BoundType.Unit,
            _ => throw new InvalidOperationException($"A Storage witness has the operation {target.Access}."),
        };
        var agrees = Same(TyCtxt.Signature(requirement, subst, -1, out var count), result) && count == (set ? 2 : 1);
        for (var slot = 0; agrees && slot < count; slot++)
        {
            agrees = Same(TyCtxt.Signature(requirement, subst, slot, out _), slot == requirement.ReceiverIndex ? receiver : field!);
        }

        return agrees;

        bool Same(BoundType? declared, BoundType expected) => declared is not null && ReferenceEquals(this.types.Erase(declared), this.types.Erase(expected));
    }

    // K 4.3: a shim's targets, at its default site: a setter's property witness drops the Field's old value (its glue), the Field Agrees
    // found in the representative Self at admission; a Builtin's leaves are K6b's (DECISIONS R5-M67).
    private int Shim(int item, SourceRef site, in MonoKey key, BoundType self)
    {
        if (key is not { Kind: MonoKind.PropertyWitness, Variant: (byte)PropertyWitnessKind.StorageSet })
        {
            return -1;
        }

        return AdtDef.FindField(self, key.Symbol, out var field, out _) ? this.Glue(item, site, field!)
            : throw new InvalidOperationException($"The property witness of {key.Symbol} is not a Field of {TyCtxt.Name(self)}.");
    }
}
