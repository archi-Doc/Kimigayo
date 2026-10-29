// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns consultation and declaration-part prerequisites, their derived failures and published diagnostic keys.
// Checks record the parts they read; publication follows those records without binding or proving them again.
// Storage is reused between passes and cleared by ResetPrerequisites with the other Binding facts.
// This concern preserves the existing cause order and performs no diagnostic rendering.
public sealed partial class Binding
{
    // SPEC 23.1.2: the operands a node consulted that did not resolve are its explicit prerequisites. They are recorded
    // when consulted (BindNode, failures of other nodes, symbol uses), never searched in the tree afterwards. The storage is
    // reused across passes, so neither resolving nodes nor a warm rebind of invalid code allocates.
    private readonly List<Koto> consulted = [];
    private readonly List<Koto> derivedIssues = [];
    private readonly List<Koto> prerequisiteStore = [];
    private readonly Dictionary<Koto, (int Start, int Count)> prerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Koto> partPrerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly List<DiagnosticKey> prerequisiteKeys = [];
    private readonly HashSet<Koto> prerequisiteVisited = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<Koto> prerequisitePending = new();
    private int consultationStart = -1;
    private Koto? consultationNode;

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues => this.derivedIssues;

    // A recovery node, an ErrorKoto or synthesized syntax kept in place of a rejected form, stands for its syntax error.
    internal static bool IsRecovery(Koto node, out DiagnosticKey cause)
    {
        if (node is ErrorKoto error)
        {
            cause = error.Cause ?? DiagnosticKey.Unresolved;
            return true;
        }

        if (node.CodeContext.RecoveryCause(node) is { } recorded)
        {
            cause = recorded;
            return true;
        }

        cause = default;
        return false;
    }

    /// <summary>Gets the Binding causes of a node that a later phase checks: none when Binding resolved it or recorded no cause.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The check keys, or <see langword="null"/>.</returns>
    internal DiagnosticKey[]? FailureCauses(Koto node) => this.HasFailureCauses(node) ? this.CauseKeys(node) : null;

    /// <summary>Gets whether Binding recorded a cause for a node it did not resolve: the node is a recovery, failed, or rests on an
    /// unresolved prerequisite (a dependent, SPEC 23.1.2).</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether a check that reads the node rests on a Binding cause.</returns>
    internal bool HasFailureCauses(Koto node)
        => node.BindingState != BindingState.Resolved &&
            (IsRecovery(node, out _) || node.BindingFailure != BindingFailure.None || this.HasUnresolvedPrerequisite(node));

    private static bool IsUnresolvedPrerequisite(Koto node)
        => node.BindingState != BindingState.Resolved || IsRecovery(node, out _);

    // SPEC 23.1.2: the declaration part that gives a binding its Type: a parameter's written Type, an accessor parameter's annotation or
    // Property Type, or a local's or Pattern's declaration; null for any other binding. A check that fails on a binding's Type rests on
    // this part when it did not resolve, also where the binding kept a Type (a projection that names no Type, R5-M155).
    private static Koto? TypeSource(BindingSymbol symbol)
    {
        if (symbol is { Kind: BindingSymbolKind.Parameter, Declaration: FunctionKoto function, Slot: var slot } && (uint)slot < (uint)function.Parameters.Count)
        {
            return function.Parameters[slot].Type;
        }

        if (symbol is { Kind: BindingSymbolKind.Parameter, Declaration: PropertyAccessorKoto accessorSyntax })
        {
            var accessor = Accessor(accessorSyntax);
            var annotation = ReferenceEquals(symbol, accessor.SelfSymbol) ? accessorSyntax.ReceiverType : accessorSyntax.ValueType;
            return annotation ?? accessor.Property.Declaration.TypeKoto ?? accessor.Property.Declaration;
        }

        return symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate ? symbol.Declaration : null;
    }

    // An invalid node with a failed part remains rejected while another check is pending; ordinary retries remain pending.
    private void MarkUnresolved(Koto node)
    {
        if (node.BindingState != BindingState.Invalid || this.UnresolvedPart(node) is null)
        {
            node.BindingState = BindingState.Unresolved;
        }
    }

    private Koto? UnresolvedPart(Koto node)
        => this.partPrerequisites.TryGetValue(node, out var part) && IsUnresolvedPrerequisite(part) ? part : null;

    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        this.prerequisites[node] = (this.prerequisiteStore.Count, 1);
        this.prerequisiteStore.Add(cause);
        return Complete(node, null);
    }

    private (int Start, Koto? Node) BeginConsultation(Koto node)
    {
        var parent = (this.consultationStart, this.consultationNode);
        this.consultationStart = this.consulted.Count;
        this.consultationNode = node;
        return parent;
    }

    private void EndConsultation(Koto node, (int Start, Koto? Node) parent)
    {
        var start = this.consultationStart;
        var count = this.consulted.Count - start;
        if (count != 0)
        {
            if (node.BindingState != BindingState.Resolved)
            {
                this.prerequisites[node] = (this.prerequisiteStore.Count, count);
                for (var i = start; i < start + count; i++)
                {
                    this.prerequisiteStore.Add(this.consulted[i]);
                }
            }

            this.consulted.RemoveRange(start, count);
        }

        (this.consultationStart, this.consultationNode) = parent;
    }

    /// <summary>Records that the node being bound read the result of another node; one that did not resolve becomes a prerequisite, but for
    /// a part that waits for the call reading it (SPEC 10.5.1), which depends on that call instead.</summary>
    /// <param name="dependency">The node whose result was read.</param>
    private void Consulted(Koto dependency)
    {
        if (this.consultationStart >= 0 && dependency.BindingState != BindingState.Resolved && !ReferenceEquals(dependency, this.consultationNode) &&
            !this.IsWaitingPart(dependency))
        {
            this.consulted.Add(dependency);
        }
    }

    // SPEC 23.1.2: a use of a symbol without a Type consults its TypeSource; a Pattern's or loop's binding rests on the failure that
    // left it Type-less (RestOnFailure, BindIteration). A local's declaration is consulted where the use binds it (BindVariable).
    private void ConsultTypeSource(BindingSymbol symbol)
    {
        if (symbol.Declaration is not VariableKoto && TypeSource(symbol) is { } part)
        {
            this.Consulted(part);
        }
    }

    // A recovery node derives from its syntax error, and so does every failure of a check that consulted a recovery node,
    // directly or through unresolved operands: the parser's guess explains what the check combined (DIAGNOSTICS.md §4.3).
    // A failure that reports missing information is derived when the check consulted prerequisites that stayed unresolved;
    // a failure explained by the Origin rule, or any other definite failure, is direct.
    // A constraint proof that a declaration-level check found failing because a part it read failed, such as the invalid
    // declaration context of a Property (AddPrerequisite), is a consequence of that part.
    private bool IsDerived(Koto node)
        => IsRecovery(node, out _) || this.RestsOnRecovery(node) ||
            (node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.HasUnresolvedPrerequisite(node)) ||
            (node.BindingFailure == BindingFailure.InvalidOrigin && this.originDeclarations.TryGetValue(node, out var declaration) &&
            declaration.Failure is { } clause && this.partPrerequisites.TryGetValue(node, out var cause) && ReferenceEquals(cause, clause)) ||
            (node.BindingFailure is BindingFailure.InvalidConstraint or BindingFailure.Unsupported && this.partPrerequisites.TryGetValue(node, out var part) && part.BindingState == BindingState.Invalid) ||
            (node.BindingFailure == BindingFailure.InvalidOrigin && node is IsKoto { IsAssociatedConstraint: true } && this.partPrerequisites.TryGetValue(node, out var occurrence) &&
            this.formationCauses?.ContainsKey(occurrence) == true) ||
            (node.BindingFailure == BindingFailure.InvalidAssociatedType && this.partPrerequisites.TryGetValue(node, out var specification) &&
            (specification.BindingFailure != BindingFailure.None || specification.BindingState == BindingState.Invalid)) ||
            (node.BindingFailure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var read) &&
            this.formationCauses?.ContainsKey(read) == true);

    // The walk reuses the prerequisite storage of PrerequisiteKeys; both run only at publication.
    private bool RestsOnRecovery(Koto node)
    {
        if (!this.prerequisites.ContainsKey(node) && !this.partPrerequisites.ContainsKey(node))
        {
            return false;
        }

        var visited = this.prerequisiteVisited;
        var pending = this.prerequisitePending;
        visited.Add(node);
        this.PushPrerequisites(node, pending);
        var found = false;
        while (pending.TryPop(out var cause))
        {
            if (!visited.Add(cause))
            {
                continue;
            }

            if (IsRecovery(cause, out _))
            {
                found = true;
                pending.Clear();
                break;
            }

            this.PushPrerequisites(cause, pending);
        }

        visited.Clear();
        return found;
    }

    /// <summary>Records that a declaration-level check read a part outside a consultation frame; the part explains the node's failure when it did not resolve.
    /// The record belongs to the pass, like every other prerequisite.</summary>
    /// <param name="node">The checked node.</param>
    /// <param name="part">The part it read.</param>
    private void AddPrerequisite(Koto node, Koto part)
    {
        // A recovery part explains the failure even when a Type was formed for it (a length slot binds as isize).
        if (IsRecovery(part, out _) || (part.BindingState != BindingState.Resolved && !this.partPrerequisites.ContainsKey(node)))
        {
            this.partPrerequisites[node] = part;
        }
    }

    // A consulted operand can resolve after it was consulted, such as a callee completed by overload selection; only
    // operands still unresolved when Binding ends are prerequisites.
    private bool HasUnresolvedPrerequisite(Koto node)
    {
        if (this.UnresolvedPart(node) is not null)
        {
            return true;
        }

        if (this.prerequisites.TryGetValue(node, out var range))
        {
            for (var i = range.Start; i < range.Start + range.Count; i++)
            {
                if (IsUnresolvedPrerequisite(this.prerequisiteStore[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets the check keys that explain why a node did not resolve: its own failure, or what it rests on.</summary>
    /// <param name="node">The unresolved node.</param>
    /// <returns>The keys; the unresolved mark when no cause was recorded.</returns>
    private DiagnosticKey[] CauseKeys(Koto node)
        => !IsRecovery(node, out _) && node.BindingFailure != BindingFailure.None ? [node.KeyOf(DiagnosticRequirement.Binding(node.BindingFailure))] : this.PrerequisiteKeys(node);

    /// <summary>Gets the check keys a derived failure rests on; a skipped prerequisite contributes its own prerequisites.</summary>
    /// <param name="node">The derived node.</param>
    /// <returns>The keys; the unresolved mark when no cause was recorded.</returns>
    private DiagnosticKey[] PrerequisiteKeys(Koto node)
    {
        if (IsRecovery(node, out var own))
        {
            return [own];
        }

        // The walk reuses its storage; only the published array is allocated.
        var keys = this.prerequisiteKeys;
        var visited = this.prerequisiteVisited;
        var pending = this.prerequisitePending;
        visited.Add(node);
        this.PushPrerequisites(node, pending);
        while (pending.TryPop(out var cause))
        {
            if (!visited.Add(cause))
            {
                continue;
            }

            var key = IsRecovery(cause, out var syntax) ? syntax
                : cause.BindingFailure != BindingFailure.None ? cause.KeyOf(DiagnosticRequirement.Binding(cause.BindingFailure))
                : this.PushPrerequisites(cause, pending) ? (DiagnosticKey?)null
                : DiagnosticKey.Unresolved;
            if (key is { } found && !keys.Contains(found))
            {
                keys.Add(found);
            }
        }

        DiagnosticKey[] result = keys.Count == 0 ? [DiagnosticKey.Unresolved] : [.. keys];
        keys.Clear();
        visited.Clear();
        return result;
    }

    private bool PushPrerequisites(Koto node, Stack<Koto> pending)
    {
        var count = pending.Count;
        if (this.UnresolvedPart(node) is { } part)
        {
            pending.Push(part);
        }

        if (this.prerequisites.TryGetValue(node, out var range))
        {
            for (var i = range.Start + range.Count - 1; i >= range.Start; i--)
            {
                var cause = this.prerequisiteStore[i];
                if (IsUnresolvedPrerequisite(cause))
                {
                    pending.Push(cause);
                }
            }
        }

        return pending.Count != count;
    }
}
