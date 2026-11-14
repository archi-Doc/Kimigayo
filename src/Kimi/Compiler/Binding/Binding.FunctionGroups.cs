// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// FunctionGroup: the function group a use's Name denotes (SPEC 9.5, 9.5.1, 9.6), as lookup published it.
// Responsibility: the one way a reader reaches the candidates of a use: a call, a function reference, a function item or an
//   argument-kind query.
// Inputs: the use and the head symbol its lookup returned; lookup's publications for the use (the alias stage's gathered list,
//   Binding.Imports.cs; the Constraint-gathered requirement group, Binding.ContractCalls.cs).
// Output: FunctionGroup, enumerated as the requirement members, or every source's declarations, or the head's chain.
// Invariants: GroupOf is the one constructor for a use, and the only reader of the gathered table; FunctionGroup.Of is the one for a
//   declaration's own chain, which has no use (a scope's init chain). The head is lookup's published head, never a selection: a
//   selected declaration would enumerate a suffix of its chain. The order of enumeration never decides acceptance or selection
//   (SPEC 10.4).
// Prohibitions: no lookup, no gathering, no access or applicability judgment, no report.
public sealed partial class Binding
{
    private FunctionGroup GroupOf(Koto use, BindingSymbol? head)
    {
        var name = ReferenceName(use);
        var requirements = name is MemberAccessKoto member && this.requirementGroups.TryGetValue(member, out var found) && found.Active ? found : null;
        return new(head, requirements, this.gatheredGroups?.GetValueOrDefault(name));
    }

    private readonly struct FunctionGroup(BindingSymbol? first, RequirementGroup? requirements, List<BindingSymbol>? gathered)
    {
        // The Constraint-gathered group, whose Self a requirement call substitutes.
        internal RequirementGroup? Requirements => requirements;

        // The alias stage's list when lookup gathered the Name there.
        internal List<BindingSymbol>? Gathered => gathered;

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
                if (members is not null)
                {
                    if (this.index == members.Count)
                    {
                        return false;
                    }

                    this.Current = members[this.index++];
                    return true;
                }

                if (this.next is null)
                {
                    return false;
                }

                this.Current = this.next;
                this.next = this.next.Next;
                return true;
            }
        }
    }
}
