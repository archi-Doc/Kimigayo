// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// Requirement edges (K package 4.5 rows 2, 3 and 6 and their witness results; F package 4.5 and 4.6; S2-S4 X14, X16, X28; DECISIONS R5-M61,
// R5-M67, R5-M70) and the targets of the shim items.
internal sealed partial class InstanceCollector
{
    // K 4.5 rows 2, 3 and 6: what a requirement runs in the item's instance, TyCtxt.Witness of it with the Contract reference its call
    // recorded and the call's closed substitution (X16), a requirement call's composed one or a compiler function's call on a Self: a Body's
    // function item, the implementation its instance selects (SPEC 8.13.3; -1 without a body); a Builtin's shim item, keyed by its compiler
    // function and the erased Self; a Storage witness's property-witness item, keyed by its Field, the erased Self and its operation (X14,
    // X28), whose key's signature agrees with the requirement's in the instance. OCC-X's refusal and a Self whose storage does not complete
    // are capability gaps at the call; None, no answer for the closed Self of an accepted program, and a Storage witness whose signature
    // disagrees are broken contracts; a kind without a row is a fault.
    private int Requirement(MirProgram program, int item, SourceRef subject, BindingSymbol requirement, BindingSymbol? contract, Subst subst)
    {
        var target = this.types.Witness(requirement, contract, subst);
        DiagnosticCode? refused = target.Kind switch
        {
            RequirementTargetKind.Body or RequirementTargetKind.Builtin => null,
            RequirementTargetKind.Storage when this.Agrees(requirement, subst, target) => null,
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
            return this.Implementation(program, item, subject, target.Target!, target.Subst);
        }

        var key = target.Kind == RequirementTargetKind.Builtin
            ? new MonoKey(MonoKind.Builtin, default, Symbol: target.Target, Variant: (byte)target.Target!.CompilerFunction)
            : new MonoKey(MonoKind.PropertyWitness, default, Symbol: target.Target, Variant: (byte)target.Access);
        return this.AdmitType(item, subject, key, subst.Self!);
    }

    // K 4.5 rows 2 and 6, F 4.5: a compiler function's call of a requirement on a Type of the item's instance, through the requirement's own
    // Contract under the Self-bearing factory (TyCtxt.Synthesized); a Type TyCtxt cannot make is the storage-nesting gap at the call.
    private int Runs(MirProgram program, int item, SourceRef subject, BindingSymbol requirement, BoundType? self)
    {
        if (self is null)
        {
            this.Set.Report(Capability.Code(CapabilityGap.StorageNesting), subject);
            return -1;
        }

        return this.Requirement(program, item, subject, requirement, requirement.Scope.Owner.BoundSymbol, this.types.Synthesized(requirement, self));
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

    // K 4.3: a shim's targets, at its default site: a comparison shim's are its leaf witnesses (SPEC 13.4) by TyCtxt.Comparison of its
    // Contract, the scope owner of its compiler function (F 4.6), on the representative Self under the Contract's mapping; a formatting or
    // arithmetic shim's scalar Self calls no witness (FormattingTypes.IsBuiltin, ArithmeticContracts.Supports); a setter's property witness
    // drops the Field's old value (its glue), the Field Agrees found in the representative Self at admission.
    private void Shim(MirProgram program, int item, SourceRef site, in MonoKey key, BoundType self)
    {
        if (key.Kind == MonoKind.PropertyWitness)
        {
            if (key.Variant != (byte)PropertyWitnessKind.StorageSet)
            {
                return;
            }

            if (!AdtDef.FindField(self, key.Symbol, out var field, out _))
            {
                throw new InvalidOperationException($"The property witness of {key.Symbol} is not a Field of {TyCtxt.Name(self)}.");
            }

            this.Target(item, this.Glue(item, site, field!));
            return;
        }

        switch ((CompilerFunctionKind)key.Variant)
        {
            case CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare:
                this.Leaves(program, item, site, this.types.Comparison(key.Symbol!.Scope.Owner.BoundSymbol!, self, operators: false));
                return;
            case CompilerFunctionKind.BuiltinFormat or CompilerFunctionKind.BuiltinArithmetic:
                return;
            default:
                throw new InvalidOperationException($"The shim {(CompilerFunctionKind)key.Variant} has no target row.");
        }
    }

    // F 4.6: a comparison tree's targets in order: a Witness leaf's Body under its substitution, a Composite's parts', none for a Builtin. A
    // Requirement or None leaf, which a closed Self's comparison has not, is a broken contract, and no tree, a Self whose storage does not
    // complete, the storage-nesting gap, at the shim's site.
    private void Leaves(MirProgram program, int item, SourceRef site, ComparisonNode? node)
    {
        DiagnosticCode? refused = node?.Kind switch
        {
            null => Capability.Code(CapabilityGap.StorageNesting),
            ComparisonKind.Composite or ComparisonKind.Builtin or ComparisonKind.Witness => null,
            ComparisonKind.Requirement or ComparisonKind.None => DiagnosticCode.InternalInvariant_Kd,
            _ => throw new InvalidOperationException($"The comparison kind {node!.Kind} has no target row."),
        };

        if (refused is { } code)
        {
            this.Set.Report(code, site);
            return;
        }

        if (node!.Kind == ComparisonKind.Witness)
        {
            this.Target(item, this.Implementation(program, item, site, node.Target!, node.Subst));
        }

        foreach (var part in node.Parts)
        {
            this.Leaves(program, item, site, part);
        }
    }
}
