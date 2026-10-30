// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Backend;

// Requirement edges (K package 4.5 rows 2, 3, 5, 6, 12 and 13 and their witness results; F package 4.5 and 4.6; S2-S4 X14, X16, X28;
// DECISIONS R5-M61, R5-M67, R5-M70) and the targets of the shim items.
internal sealed partial class InstanceCollector
{
    /// <summary>Gets a Type of the signature a property-witness item's key fixes (<see cref="StorageSignature(BoundType, BindingSymbol?,
    /// PropertyWitnessKind, int, out int)"/>), over its representative Self: the one its shim's function takes (C3c).</summary>
    /// <param name="item">The PropertyWitness item.</param>
    /// <param name="slot">The parameter slot, or -1 for the result.</param>
    /// <param name="count">The number of parameter slots.</param>
    /// <returns>The erased Type; null where the representative Self stores no such Field.</returns>
    internal BoundType? StorageSignature(int item, int slot, out int count)
    {
        var entry = this.Set.Items[item];
        return this.StorageSignature(entry.RepresentativeType!, entry.Key.Symbol, (PropertyWitnessKind)entry.Key.Variant, slot, out count);
    }

    // K 4.5 rows 2, 3, 5, 6, 12 and 13: what a requirement runs in the item's instance, TyCtxt.Witness of it with the Contract reference its
    // call recorded and the call's closed substitution (X16): a requirement call's composed one, a compiler function's call on a Self or a
    // requirement Item's call. A Body's function item, the implementation its instance selects (SPEC 8.13.3; -1 without a body); a Builtin's
    // shim item, keyed by its compiler function and the erased Self; a Storage witness's property-witness item, keyed by its Field, the
    // erased Self and its operation (X14, X28), whose key's signature agrees with the requirement's in the instance; a shim item represented
    // by the call's substitution, a Builtin's and a Storage witness's, whose bound reference instantiates the requirement's signature (one
    // Abi for every call of a key: its Self erases to one Type, references are pointers, SPEC 13.3.3 fixes a built-in numeric Type's
    // counterpart and Output; DECISIONS R5-M126). OCC-X's refusal, a Self whose storage does not complete and an arithmetic shim whose
    // operation the capability table refuses (ArithmeticGap; IMPL 21.3.3) are capability gaps at the call, each requesting site's, and
    // refuse the edge; None, no answer for the closed Self of an accepted program, and a Storage witness whose signature disagrees are
    // broken contracts; a kind without a row is a fault.
    private int Requirement(MirProgram program, int item, SourceRef subject, BindingSymbol requirement, in RequirementTarget target)
    {
        DiagnosticCode? refused = target.Kind switch
        {
            RequirementTargetKind.Builtin when ArithmeticGap(target) is { } gap => Capability.Code(gap),
            RequirementTargetKind.Body or RequirementTargetKind.Builtin => null,
            RequirementTargetKind.Storage when this.Agrees(requirement, target) => null,
            RequirementTargetKind.Storage or RequirementTargetKind.None => DiagnosticCode.InternalInvariant_Kd,
            RequirementTargetKind.Refused => Capability.Code(CapabilityGap.ExclusiveInheritedWitness),
            RequirementTargetKind.Limit => Capability.Code(CapabilityGap.StorageNesting),
            _ => throw new InvalidOperationException($"The witness kind {target.Kind} has no edge row."),
        };

        if (refused is { } code)
        {
            this.Report(code, subject);
            return -1;
        }

        if (target.Kind == RequirementTargetKind.Body)
        {
            return this.Implementation(program, item, subject, target.Target!, target.Subst);
        }

        var key = target.Kind == RequirementTargetKind.Builtin ? new MonoKey(MonoKind.Builtin, default, null, target.Target, 0, null)
            : new MonoKey(MonoKind.PropertyWitness, default, null, target.Target, (byte)target.Access, null);
        return this.AdmitType(item, subject, key, target.Subst!.Self!, target.Subst);
    }

    // K 4.5 rows 3, 5 and 13 (DECISIONS R5-M146): the target of a requirement call or of a requirement Item, where it is formed, called or
    // erased, which code generation runs as a body or shim item (FunctionCodegen.Calls' Witness). A Body witness without a MIR body is
    // decided by TyCtxt.Bodyless, the one classification of such a function: a compiler function (FixedBuffer's or HeapBuffer's reserve) or
    // a #LibraryImport function is the BodylessWitness gap at each such site, whatever else Binding rejected (SPEC 23.1.2); one Binding
    // rejected is that error's dependent (no record); any other is a broken contract (Report). The other edges a requirement runs on (a
    // compiler function's published row, Text.writer's reserve witness) keep Requirement's -1 for such a witness: their writers read the
    // function itself.
    private int Called(MirProgram program, int item, SourceRef subject, BindingSymbol requirement, in RequirementTarget target)
    {
        if (target.Kind != RequirementTargetKind.Body)
        {
            return this.Requirement(program, item, subject, requirement, target);
        }

        if (this.Selected(program, target.Target!, target.Subst, out var implementation) is { } body)
        {
            return this.Admit(item, subject, body, target.Subst);
        }

        DiagnosticCode? record = TyCtxt.Bodyless(implementation) switch
        {
            CompilerFunctionEffect.Dependent => null,
            CompilerFunctionEffect.Unclassified when implementation.CompilerFunction == CompilerFunctionKind.None => DiagnosticCode.InternalInvariant_Kd,
            _ => Capability.Code(CapabilityGap.BodylessWitness),
        };

        if (record is { } code)
        {
            this.Report(code, subject);
        }

        return -1;
    }

    // K 4.5 rows 2 and 6, F 4.5: a compiler function's call of a requirement on a Type of the item's instance, through the requirement's own
    // Contract under the Self-bearing factory (TyCtxt.Synthesized), a Type whose verdict its caller read (Below; an intrinsic's argument In
    // or Edge's Compose).
    private int Runs(MirProgram program, int item, SourceRef subject, BindingSymbol requirement, BoundType self)
    {
        var contract = TyCtxt.Contract(requirement);
        var call = this.types.Synthesized(requirement, self, contract.Type!);
        return this.Requirement(program, item, subject, requirement, this.types.Witness(requirement, contract, call));
    }

    // F 4.5, X14: the signature a property-witness key fixes, the one statement of it, which K6's agreement (Agrees) and the shim's function
    // (ModuleCodegen.Shim) read: Binding admits a standard witness only with a ref/Self getter or a uniq/Self setter, a result F or ref/F and
    // a setter's input F (Binding.PropertyMatching.cs:102-117), F the Field's Type in Self (AdtDef.FindField), and SPEC 11.3.1 puts the
    // receiver first: slot 0 the receiver, a Set's slot 1 its input, the result (-1) F (Copy), ref/F (Borrow) or Unit (Set); each erased.
    // Null where Self stores no such Field.
    private BoundType? StorageSignature(BoundType self, BindingSymbol? field, PropertyWitnessKind access, int slot, out int count)
    {
        var set = access == PropertyWitnessKind.StorageSet;
        count = set ? 2 : 1;
        if (!AdtDef.FindField(self, field, out var stored, out _))
        {
            return null;
        }

        var type = slot switch
        {
            0 => this.types.Reference(set ? SemanticsKind.Uniq : SemanticsKind.Ref, self),
            1 when set => stored!,
            -1 => access switch
            {
                PropertyWitnessKind.StorageCopy => stored!,
                PropertyWitnessKind.StorageBorrow => this.types.Reference(SemanticsKind.Ref, stored!),
                PropertyWitnessKind.StorageSet => BoundType.Unit,
                _ => throw new InvalidOperationException($"A Storage witness has the operation {access}."),
            },
            _ => throw new InvalidOperationException($"A {access} witness has no slot {slot}."),
        };
        return this.types.Erase(type);
    }

    // F 4.5: C3c writes a property-witness shim from its key alone (StorageSignature), so the requirement's signature in the call's instance
    // (TyCtxt.Signature) must be the key's, Type for Type up to Origins (Erase).
    private bool Agrees(BindingSymbol requirement, in RequirementTarget target)
    {
        var subst = target.Subst!;
        var field = target.Target;
        var access = target.Access;
        var agrees = Same(TyCtxt.Signature(requirement, subst, -1, out var count), this.StorageSignature(subst.Self!, field, access, -1, out var slots)) &&
            count == slots;
        for (var slot = 0; agrees && slot < count; slot++)
        {
            agrees = Same(TyCtxt.Signature(requirement, subst, slot, out _), this.StorageSignature(subst.Self!, field, access, slot, out _));
        }

        return agrees;

        bool Same(BoundType? declared, BoundType? key) => declared is not null && key is not null && ReferenceEquals(this.types.Erase(declared), key);
    }

    // K 4.3: a shim's targets, at its default site: a comparison shim's are its parts' witnesses and shims (SPEC 13.4; Leaves) by
    // TyCtxt.Comparison of its Contract, the scope owner of its compiler function (F 4.6), on the representative Self under the Contract's
    // mapping; a formatting or arithmetic shim's scalar Self calls no witness (FormattingTypes.IsBuiltin, ArithmeticContracts.Supports); a
    // setter's property witness drops the Field's old value (its glue), the Field Agrees found in the representative Self at admission.
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

        switch (key.Symbol!.CompilerFunction)
        {
            case CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare:
                this.Leaves(program, item, site, this.types.Comparison(TyCtxt.Contract(key.Symbol), self, operators: false));
                return;
            case CompilerFunctionKind.BuiltinFormat or CompilerFunctionKind.BuiltinArithmetic:
                return;
            default:
                throw new InvalidOperationException($"The shim {key.Symbol.CompilerFunction} has no target row.");
        }
    }

    // F 4.6: a comparison shim's targets in order, one per part of its Self's node (a Tuple's parts, a reference layer's referent, followed)
    // that is not Builtin: a Witness leaf's Body under its substitution, a Tuple part's own shim, the Contract's requirement on its Type
    // (Runs: the Builtin item of that Type), so each distinct Tuple of the comparison DAG is one item and its parts are read once (V16;
    // DECISIONS R5-M164). A Requirement or None part, which a closed Self's comparison has not, is a broken contract, and no tree, a Self
    // whose storage does not complete, the storage-nesting gap, at the shim's site. K 4.8: each shim lays out its Self at its admission
    // (AdmitType), so a compared Tuple past a reference layer is a use at the site that admits its shim.
    private void Leaves(MirProgram program, int item, SourceRef site, ComparisonNode? node)
    {
        if (node is null)
        {
            this.Report(Capability.Code(CapabilityGap.StorageNesting), site);
            return;
        }

        foreach (var part in node.Parts)
        {
            switch (part.Kind)
            {
                case ComparisonKind.Witness:
                    this.Target(item, this.Implementation(program, item, site, part.Target!, part.Subst));
                    break;
                case ComparisonKind.Composite when part.Type.Kind == BoundTypeKind.Tuple:
                    this.Target(item, this.Runs(program, item, site, part.Target!, part.Type));
                    break;
                case ComparisonKind.Composite:
                    this.Leaves(program, item, site, part); // A reference layer's referent.
                    break;
                case ComparisonKind.Requirement or ComparisonKind.None:
                    this.Report(DiagnosticCode.InternalInvariant_Kd, site);
                    break;
            }
        }
    }
}
