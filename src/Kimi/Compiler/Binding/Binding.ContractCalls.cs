// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly RequirementGroupRows requirementGroups;
    private readonly List<RequirementGroup> requirementPool = new();
    private int requirementCursor; // The pool's next free group in this pass (ResetContracts rewinds it).

    // SPEC 8.4.1, 8.6.4: the bound Contract reference whose requirement is the candidate under evaluation or the selected
    // callee; its Type arguments substitute the requirement's signature (Key of Indexable<isize>) in ContractType.
    private BindingSymbol? activeRequirementContract;

    // SPEC 8.6.4 (F's ruling N1-7 (1)): a requirement is reached through its subject Type: the requirement group a Constraint gave the
    // use's member lookup, or a callee its creator pinned with its bound Contract. A Name that selected one without (an opened Contract's
    // direct member at stage 5) names no Type to call it through, as `Empty.empty()` does not: read on the selected candidate (the call
    // binder's winner, BindReference's group of one; never a gathered group's first: N1-13 B3; a function reference to a group holding
    // one is the reference reader's limit), and the Name records the miss that use records (UnresolvedBinding_Kd).
    private bool LacksRequirementSubject(Koto use, BindingSymbol selected)
        => selected.Declaration is FunctionKoto { IsRequirement: true } && use is not RequirementCalleeKoto && this.ReferenceRequirements(use) is null;

    private BindingSymbol? RequirementMember(MemberAccessKoto member, BindingScope scope, BoundType type, bool typeAccess)
    {
        // SPEC 9.6, 13.3.3, 13.4.1, 22.10.2: requirements are gathered from Constraints, so only a Type parameter, a projection or a
        // Contract's Self has requirement members; a built-in conformance adds no member (a built-in Type, a Tuple, a reference layer).
        if (!type.IsProofDecided && type.Symbol?.Declaration is not ContractKoto)
        {
            return null;
        }

        var group = this.requirementGroups.Ref(member) ??= this.RentRequirementGroup(); // One group per member access per pass.
        group.Members.Clear();
        group.Contracts.Clear();
        group.Seen.Clear();
        group.Self = type;
        group.Active = true;
        group.PropertyRequirement = false;
        group.Properties.Clear();
        group.PropertyContracts.Clear();
        if ((type.Symbol?.SelfOf ?? type.Symbol)?.Contract is { } own)
        {
            Add(own);
        }

        // SPEC 8.3.2, 8.5.2, 9.6: the Contracts the premises supply for the Type, one per identity class (each non-Alias row of Enumerate's
        // rows: the use is judged).
        var start = this.premiseRows.Count;
        this.PremiseRows(type, scope, this.premiseRows);
        for (var i = start; i < this.premiseRows.Count; i++)
        {
            if (this.premiseRows[i] is { Alias: false, Symbol.Contract: { } shape })
            {
                Add(shape);
            }
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);

        // SPEC 8.7.4: PrimitiveInteger implies Position, whose requirement is called through an integer Type parameter;
        // concrete integers gain no members. Temporary: this clause names Position apart from PrimitiveIntegerSupplies until
        // PLAN G776 decides the member calls an entailed built-in conformance gives; it then reads that list or goes.
        if (this.Library.Position.Contract is { } position && this.IsGenericInteger(type, scope, member))
        {
            Add(position);
        }

        // Numeric witnesses are requirements under generic evidence, not named numeric members.
        if (this.IsGenericInteger(type, scope, member))
        {
            for (var id = KimiDeclarationId.Addable; id <= KimiDeclarationId.RemainderProvider; id++)
            {
                if (TypeSpelling(member.Right) == ArithmeticContractSyntax.Method(id))
                {
                    Add(this.ArithmeticReference(id, type).Contract!);
                    break;
                }
            }
        }

        // Expand the proved contracts of this referenced receiver only, as for an explicit associated
        // projection. Inherited signatures can depend on their refining contract's associated identities.
        for (var i = 0; i < group.Contracts.Count + group.PropertyContracts.Count; i++)
        {
            var contract = i < group.Contracts.Count ? group.Contracts[i] : group.PropertyContracts[i - group.Contracts.Count];
            if (this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract)), scope, member) == ConstraintProof.Proven)
            {
                this.AddContractPremises(contract.Contract!, type, scope);
            }
        }

        // SPEC 9.6: requirements gathered from constraints must share one receiver shape; a mismatch is an error at the use.
        SemanticsKind? expected = null;
        for (var i = 0; i < group.Members.Count; i++)
        {
            if (ReceiverShape(group.Members[i]) is not { } current)
            {
                continue;
            }

            if (expected is null)
            {
                expected = current;
            }
            else if (expected != current)
            {
                this.Record(member, BindingFailure.ReceiverShapeMismatch, []);
                group.Active = false;
                return null;
            }
        }

        // SPEC 7.4, 9.6: nor may they acquire corresponding parameters of overlapping Types in different modes.
        if (this.CheckGatheredParameterShapes(member, this.GroupOf(member, null)))
        {
            group.Active = false;
            return null;
        }

        if (group.Properties.Count != 0 && group.Members.Count == 0)
        {
            group.Active = false;
            if (group.Properties.Count == 1 && !typeAccess)
            {
                return group.Properties[0];
            }

            // SPEC 11.9: a Property requirement is instance-only, so a Type names none; several identities are not implemented (the
            // located limit). The check's own decision, each in its family's form; the caller's FailMissing keeps this first failure.
            if (typeAccess)
            {
                this.Record(member, BindingFailure.MissingName, [], unresolved: true);
            }
            else
            {
                this.RecordLocated(member, member.Span, unresolved: true);
            }

            return null;
        }

        group.Active = group.Members.Count != 0;
        return group.Active ? group.Members[0] : null;

        void Add(BoundContract shape)
        {
            if (TypeSpelling(member.Right) is not { } name || !shape.MembersByName.TryGetValue(name, out var members))
            {
                return;
            }

            for (var i = 0; i < members.Count; i++)
            {
                // SPEC 8.6.1: one requirement reached through distinct bound references (Indexable<isize>, Indexable<Name>)
                // is one candidate per reference; a refinement reaches the same reference as its ancestor.
                var identity = members[i];
                var requirement = identity.Symbol;
                if (requirement.Declaration is FunctionKoto && group.Seen.Add(identity) && !Identified(group.Members, group.Contracts, identity))
                {
                    group.Members.Add(requirement);
                    group.Contracts.Add(identity.Contract);
                }
                else if (requirement.Kind == BindingSymbolKind.Property)
                {
                    group.PropertyRequirement = true;
                    if (group.Seen.Add(identity) && !Identified(group.Properties, group.PropertyContracts, identity))
                    {
                        group.Properties.Add(requirement);
                        group.PropertyContracts.Add(identity.Contract);
                    }
                }
            }
        }

        // R7-C3 (C-f3a; SPEC 8.2.3, 8.6.1): a requirement reached through two references the judge identifies in the use's view
        // (`P<U.(HasItem).Item>` and `P<i32>` under `U.(HasItem).Item is i32`) is one candidate.
        bool Identified(List<BindingSymbol> requirements, List<BindingSymbol> contracts, BoundRequirement identity)
        {
            for (var j = 0; j < requirements.Count; j++)
            {
                if (ReferenceEquals(requirements[j], identity.Symbol) && contracts[j].Type is { } kept && identity.Contract.Type is { } reached &&
                    this.IdentityProven(kept, reached, scope))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private BoundType? CallType(
        BoundType type,
        Koto binder,
        ReadOnlySpan<BoundType?> arguments,
        BindingScope scope,
        BoundType? self,
        Span<BoundOrigin> origins,
        Span<BoundOrigin> inputs,
        BoundType? declaringType = null,
        ReadOnlySpan<BoundLength?> lengths = default)
    {
        if (self is not null)
        {
            type = this.ContractType(type, scope, self);
        }

        for (var i = 0; i < InputCount(binder) && i < inputs.Length; i++)
        {
            if (InputType(binder, i)?.BoundType?.Origin is { BorrowCondition: { } selector } &&
                ContainerSlot(CallSlotOwner(binder), selector) is var slot && slot >= 0 && slot < arguments.Length &&
                arguments[slot] is { } binding && !this.HasOuterSlot(binding))
            {
                inputs[i] = BoundOrigin.Static;
            }
        }

        // PLAN G457, G707: the call's substitution in one pass over the callee's and its declaring Type's slots, so a Type argument naming
        // either's own slot or Origin (a call in its own body, a call from another member) keeps the caller's; then normalized in the
        // caller's terms.
        var result = this.Substitute(type, SubstitutionOf(binder, declaringType, arguments, lengths, origins, inputs));
        return result is null ? null : this.ContractType(result, scope, self);
    }

    // Rents a group from the pass-local pool: the first lookup of a member access in a pass takes the next one, each later lookup of it
    // in the pass its row's; a warm pass allocates none.
    private RequirementGroup RentRequirementGroup()
    {
        if (this.requirementCursor == this.requirementPool.Count)
        {
            this.requirementPool.Add(new());
        }

        return this.requirementPool[this.requirementCursor++];
    }

    /// <summary>The `requirementGroups` family's one accessor (DECISIONS BU-220): a working-state family, the group a member access's
    /// lookup through requirements fills in its pass (RequirementMember) and the pass's later readers take (FunctionGroups,
    /// MissingFailure, BindPropertyCall), kept in the member access's module's NodeColumns at its SyntaxId (null for none): a row of an
    /// earlier pass reads none, so an edit that leaves a member access unlooked-up leaves it no group (PLAN D-942), and the library's
    /// rows are released at its freeze (NodeColumns.Freeze). The groups are the Binding's pool's (requirementPool), which each pass
    /// rewinds. Its members keep the names its readers use.</summary>
    private readonly struct RequirementGroupRows(Binding binding)
    {
        internal ref RequirementGroup? Ref(MemberAccessKoto member) => ref HirTables.Of(member).Families.RequirementGroups.Ref(member.SyntaxId, binding);

        internal bool TryGetValue(MemberAccessKoto member, [NotNullWhen(true)] out RequirementGroup? group)
            => (group = member.SyntaxOwner is { } table ? table.Hir.Families.RequirementGroups.Get(member.SyntaxId, binding) : null) is not null;
    }

    internal sealed class RequirementGroup
    {
        internal List<BindingSymbol> Members { get; } = new();

        /// <summary>Gets the Contract, a bound reference where it takes Type arguments, that supplied each member.</summary>
        internal List<BindingSymbol> Contracts { get; } = new();

        internal List<BindingSymbol> Properties { get; } = new();

        internal List<BindingSymbol> PropertyContracts { get; } = new();

        internal HashSet<BoundRequirement> Seen { get; } = new();

        internal BoundType Self { get; set; } = null!;

        internal bool Active { get; set; }

        /// <summary>Gets or sets a value indicating whether a Property requirement matched the name.</summary>
        internal bool PropertyRequirement { get; set; }
    }
}
