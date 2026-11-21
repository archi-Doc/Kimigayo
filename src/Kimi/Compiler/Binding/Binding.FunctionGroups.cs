// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// FunctionGroup: the function group a use's Name denotes (SPEC 9.5, 9.5.1, 9.6), as lookup gathered it.
// Responsibility: the one way a reader reaches the candidates of a use: a call, a function reference, a function item, an
//   argument-kind query, or the use verdict while the use is evaluated (Binding.UnsupportedUses.cs).
// Inputs: the use and the head symbol its lookup returned; lookup's gathering for the use (the alias stage's ImportGroup steps and
//   their publication, the gathered list, Binding.Imports.cs; the Constraint-gathered requirement group, Binding.ContractCalls.cs).
// Output: FunctionGroup, enumerated as the requirement members, or every source's declarations, or the head's chain; and the verdict
//   of the check SPEC 7.4.4 and 9.5.1 give a group gathered at the alias stage (GatheredGroupFails).
// Invariants: FunctionGroup.Of builds every chain. A use's group is its head's chain plus each gathered source's chain, read from the
//   walk's own ImportGroup steps while the use is evaluated (the use verdict, which runs before PublishPath) and from their
//   publication after it: GroupOf, the one constructor of a published use's group and the only reader of the gathered table. A
//   declaration's own chain with no use (a scope's init chain) is FunctionGroup.Of alone. ReferenceRequirements is the one decision
//   that a use is a requirement reference (its Name a member access whose lookup gathered an active group from Constraints):
//   GroupOf enumerates by it and the reference and item binders route by it, so no other reader decides it from the requirement
//   table. The head is the walk's own head before publication (the verdict) and lookup's published head after it (GroupOf), never
//   a selection: a selected declaration would enumerate a suffix of its chain. A null head is allowed only where lookup published a
//   group for the use: the Constraint group's own check (RequirementMember) reads GroupOf(member, null) after it stored the group
//   and set Active; with a null head and nothing published the group is empty, and a check over it passes. An explicit
//   specialization is never yielded (SPEC 8.13.3: specializations are never candidates). The order of enumeration never decides
//   acceptance or selection (SPEC 10.4). No reader walks BindingSymbol.Next from a use's symbol.
// Prohibitions: no lookup, no gathering, no access or applicability judgment, no report.
public sealed partial class Binding
{
    private FunctionGroup GroupOf(Koto use, BindingSymbol? head)
        => new(head, this.ReferenceRequirements(use), this.gatheredGroups?.GetValueOrDefault(ReferenceName(use)));

    // SPEC 9.6: the active group lookup gathered from Constraints for the use's member access, or none. A requirement reference
    // retains the requirement declaration, the conforming Self and its bound declaring Contract (Binding.RequirementItems.cs).
    private RequirementGroup? ReferenceRequirements(Koto use)
        => ReferenceName(use) is MemberAccessKoto member && this.requirementGroups.TryGetValue(member, out var group) && group.Active ? group : null;

    // SPEC 7.4.4, 9.5.1: a function group gathered at the alias stage from several declaration sources is checked at every use,
    // whatever its arguments. Every use passes one of two binders, which IsValueUse separates: the callee (BindCallCore) and the value
    // use (BindReference). Lookup cannot run the check where it publishes the group: the check reads the members' bound headers, and
    // lookup runs while headers are bound and under probes that publish nothing.
    private bool GatheredGroupFails(Koto use, in FunctionGroup group)
    {
        if (!group.Gathered)
        {
            return false;
        }

        foreach (var member in group)
        {
            this.BindHeader(member);
        }

        return this.CheckGatheredParameterShapes(use, group);
    }

    private readonly struct FunctionGroup(BindingSymbol? first, RequirementGroup? requirements, List<BindingSymbol>? gathered)
    {
        // The Constraint-gathered group, whose Self a requirement call substitutes.
        internal RequirementGroup? Requirements => requirements;

        // Whether lookup gathered the Name at the alias stage from several declaration sources.
        internal bool Gathered => gathered is { Count: > 1 };

        // The group's one candidate, or none when it holds several or none.
        internal BindingSymbol? Single
        {
            get
            {
                BindingSymbol? single = null;
                foreach (var candidate in this)
                {
                    if (single is not null)
                    {
                        return null;
                    }

                    single = candidate;
                }

                return single;
            }
        }

        public Enumerator GetEnumerator() => new(first, requirements, gathered);

        internal static FunctionGroup Of(BindingSymbol head) => new(head, null, null);

        internal struct Enumerator(BindingSymbol? first, RequirementGroup? requirements, List<BindingSymbol>? gathered)
        {
            private BindingSymbol? next = first;
            private int index;

            public BindingSymbol Current { get; private set; } = null!;

            public bool MoveNext()
            {
                var members = requirements?.Members ?? (gathered is { Count: > 0 } ? gathered : null);
                BindingSymbol? candidate;
                do
                {
                    if (members is not null)
                    {
                        candidate = this.index == members.Count ? null : members[this.index++];
                    }
                    else if ((candidate = this.next) is not null)
                    {
                        this.next = candidate.Next;
                    }

                    if (candidate is null)
                    {
                        return false;
                    }
                }
                while (candidate.Declaration is FunctionKoto { IsSpecialization: true }); // SPEC 8.13.3: never a candidate.

                this.Current = candidate;
                return true;
            }
        }
    }
}
