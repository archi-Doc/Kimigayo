// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns a node's direct failure (Fail), consultation and declaration-part prerequisites, their derived failures and published diagnostic keys.
// Checks record the parts they read; publication follows those records without binding or proving them again.
// Storage is reused between passes and cleared by ResetPrerequisites with the other Binding facts.
// A consultation frame is the site of what a check reads and forms (PLAN G651 U1), in one of three forms: the walk's (BindNode,
// BindType: BeginConsultation, EndConsultation); a judgment frame (BeginConsultation(use) before a validation check's proof,
// RequireConstraintInFrame after it; its reads become the outcome's prerequisites only when the outcome is the use's record); a site
// frame (BeginConsultation(element) around a pass element that publishes facts and records no proof, DropConsultation after it).
// This concern preserves the existing cause order and performs no diagnostic rendering.
// A node of a module the running pass does not bind keeps its columns, the state its own pass left: the recorder writes such a node's
// state and failure in the pass's entry for it (PassPart.ForeignFailures), and every read of a node's state or failure here and in the
// publication goes through RecordedState and RecordedFailure (PLAN G949 S1-1, s11-design 3.3).
public sealed partial class Binding
{
    // SPEC 23.1.2: the operands a node consulted that did not resolve are its explicit prerequisites. They are recorded
    // when consulted (BindNode, failures of other nodes, symbol uses), never searched in the tree afterwards. The storage is
    // reused across passes, so neither resolving nodes nor a warm rebind of invalid code allocates.
    private readonly List<Koto> consulted = [];
    private readonly List<Koto> prerequisiteStore = [];
    private readonly Dictionary<Koto, (int Start, int Count)> prerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Koto> partPrerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly List<DiagnosticKey> prerequisiteKeys = [];
    private readonly HashSet<Koto> prerequisiteVisited = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<Koto> prerequisitePending = new();
    private int consultationStart = -1;
    private Koto? consultationNode;

    // The pass's change signal (ValidateConstraintEnvironments): advanced by every write of what an environment's judgment reads, in the
    // step that writes it: each Fail call, whether or not the node held a failure already (a conformance's, an identity's or an Access
    // path's Invalid is written with one); each environment made Invalid (FailEnvironment); and an inference consumer made Invalid under
    // its source's use (InvalidateAssociatedEvidence). PublishSourceStates' state, written with no Fail at each ValidateConformances,
    // only withdraws a premise (AvailableContractPremise); the failure an Error reads (InvalidDeclarationContext) is written through Fail
    // by ValidateClosedTypeConstraints, which the rounds' condition runs before the walk (InvalidateProjectionCertificates).
    private int invalidations;

    // The running pass's part (PassPart.DerivedIssues), which its Check writes (EnterPass).
    private List<Koto> derivedIssues;

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues { get; }

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
        => this.RecordedState(node) != BindingState.Resolved &&
            (IsRecovery(node, out _) || this.RecordedFailure(node) != BindingFailure.None || this.HasUnresolvedPrerequisite(node));

    // SPEC 23.1.2: the declaration part that gives a binding its Type: a parameter's written Type, an accessor parameter's annotation or
    // Property Type, or a local's or Pattern's declaration; null for any other binding. A use or a written capture entry of a binding
    // without a Type rests on it.
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

    private bool IsUnresolvedPrerequisite(Koto node)
        => this.RecordedState(node) != BindingState.Resolved || IsRecovery(node, out _);

    // The recorder's two readers: a node the running pass has an entry for (ForeignEntry) reads the entry, any other node its columns, so
    // a node of another module that the pass never wrote reads the state its own pass left (for a library node, the library's final state).
    private BindingState RecordedState(Koto node)
        => this.IndexOfForeign(node) is var i and >= 0 ? this.foreignFailures[i].State : node.BindingState;

    private BindingFailure RecordedFailure(Koto node)
        => this.IndexOfForeign(node) is var i and >= 0 ? this.foreignFailures[i].Failure : node.BindingFailure;

    // The recorder's two writers, the readers' counterparts: a node of a module the running pass does not bind takes the value in its
    // entry, never in its columns; any other node in its column.
    private void SetRecordedState(Koto node, BindingState state)
    {
        if (this.pass.Binds(node.SyntaxOwner))
        {
            node.BindingState = state;
        }
        else
        {
            this.ForeignEntry(node).State = state;
        }
    }

    private void SetRecordedFailure(Koto node, BindingFailure failure)
    {
        if (this.pass.Binds(node.SyntaxOwner))
        {
            node.BindingFailure = failure;
        }
        else
        {
            this.ForeignEntry(node).Failure = failure;
        }
    }

    // A foreign node's entry, listed at the pass's first write to it with its columns as they stood (State and Before the state its own
    // pass counted): a node a check cleared (ClearFailure) and fails again keeps its one entry with that first state (review-s1r1b N2),
    // so a node enters the list once per pass.
    private ref (Koto Node, BindingState Before, BindingState State, BindingFailure Failure) ForeignEntry(Koto node)
    {
        var i = this.IndexOfForeign(node);
        if (i < 0)
        {
            i = this.foreignFailures.Count;
            this.foreignFailures.Add((node, node.BindingState, node.BindingState, node.BindingFailure));
        }

        return ref CollectionsMarshal.AsSpan(this.foreignFailures)[i];
    }

    // The running pass's entry of a node, or -1: linear in the list, which no program fills today.
    private int IndexOfForeign(Koto node)
    {
        for (var i = 0; i < this.foreignFailures.Count; i++)
        {
            if (ReferenceEquals(this.foreignFailures[i].Node, node))
            {
                return i;
            }
        }

        return -1;
    }

    // An environment made Invalid by the failure of `node` (ConstraintEnvironment.Fail), counted in the change signal whether or not the
    // writer also fails a node: under an owner that held a failure already it calls no Fail.
    private void FailEnvironment(ConstraintEnvironment environment, Koto node)
    {
        environment.Fail(node);
        this.Invalidated();
    }

    // The change signal's one writer: Fail, FailEnvironment and an evidence record made Invalid with no failure of its own call it.
    private void Invalidated() => this.invalidations++;

    private BoundType? Fail(Koto node, BindingFailure failure, bool unresolved = false)
    {
        this.Invalidated();
        if (failure is BindingFailure.MissingName or BindingFailure.MissingType && this.QualificationStop(node) is { } qualification)
        {
            failure = BindingFailure.QualificationRequired;
            (this.qualificationFailures ??= new(ReferenceEqualityComparer.Instance))[node] = qualification;
            unresolved = false;
        }

        // A node failed while another was being checked, such as a qualifier resolved without BindNode: the checked node consulted it.
        if (this.consultationStart >= 0 && !ReferenceEquals(node, this.consultationNode))
        {
            this.consulted.Add(node);
        }

        if (this.RecordedFailure(node) != BindingFailure.None)
        {
            return null;
        }

        // A failure at a node of a module the running pass does not bind (PLAN G949, DECISIONS BU-220's pass driver) is the pass's entry
        // for it (the recorder's writers), which CountStates gives its record and count; its columns keep its own pass's state.
        if (unresolved)
        {
            this.MarkUnresolved(node);
        }
        else
        {
            this.SetRecordedState(node, BindingState.Invalid);
        }

        this.SetRecordedFailure(node, failure);
        return null;
    }

    // An invalid node with a failed part remains rejected while another check is pending; ordinary retries remain pending.
    private void MarkUnresolved(Koto node)
    {
        if (this.RecordedState(node) != BindingState.Invalid || this.UnresolvedPart(node) is null)
        {
            this.SetRecordedState(node, BindingState.Unresolved);
        }
    }

    private Koto? UnresolvedPart(Koto node)
        => this.partPrerequisites.TryGetValue(node, out var part) && this.IsUnresolvedPrerequisite(part) ? part : null;

    // A dependent takes Complete's state for no Type; a node of a module the pass does not bind takes it in its entry and keeps its
    // columns, its Type's included.
    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        this.prerequisites[node] = (this.prerequisiteStore.Count, 1);
        this.prerequisiteStore.Add(cause);
        if (this.pass.Binds(node.SyntaxOwner))
        {
            return Complete(node, null);
        }

        ref var entry = ref this.ForeignEntry(node);
        entry.State = CompletedState(entry.State, null);
        return null;
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

    // Closes a frame whose reads explain no outcome of its node (RequireConstraintInFrame, a site frame): nothing becomes its prerequisite.
    private void DropConsultation((int Start, Koto? Node) parent)
    {
        this.consulted.RemoveRange(this.consultationStart, this.consulted.Count - this.consultationStart);
        (this.consultationStart, this.consultationNode) = parent;
    }

    // SPEC 23.1.2: a check whose result the node being checked does not read runs with no frame open, so nothing it fails becomes that
    // node's prerequisite: a header that a set Type demands is its declaration's (BindType), a part an unselected call binds on its
    // own is read by no check of that call (CompleteUnselected; DECISIONS U4b), and the binding result of a declaration that a container
    // or a Property contains is read by no verdict of it (BindContainedDeclarations, BindVariableCore; DECISIONS G52-1). ResumeConsultation
    // reopens the frame.
    private (int Start, Koto? Node) SuspendConsultation()
    {
        var parent = (this.consultationStart, this.consultationNode);
        (this.consultationStart, this.consultationNode) = (-1, null);
        return parent;
    }

    private void ResumeConsultation((int Start, Koto? Node) parent) => (this.consultationStart, this.consultationNode) = parent;

    /// <summary>Records that the node being bound read the result of another node; one that did not resolve becomes a prerequisite, but for
    /// a part that waits for the call reading it (SPEC 10.5.1), which depends on that call instead.</summary>
    /// <param name="dependency">The node whose result was read.</param>
    private void Consulted(Koto dependency)
    {
        if (this.consultationStart >= 0 && this.RecordedState(dependency) != BindingState.Resolved && !ReferenceEquals(dependency, this.consultationNode) &&
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
    // declaration context of a Property (AddPrerequisite), is a consequence of that part. A Type mismatch over a Type in an erroneous
    // identity class rests on the premise error recorded at the declaration (SPEC 8.2.3: never again at uses).
    private bool IsDerived(Koto node)
    {
        var failure = this.RecordedFailure(node);
        return IsRecovery(node, out _) || this.RestsOnRecoveryOrLimit(node) ||
            (failure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.HasUnresolvedPrerequisite(node)) ||
            (failure is BindingFailure.InvalidConstraint or BindingFailure.Unsupported && this.partPrerequisites.TryGetValue(node, out var part) &&
            (this.RecordedState(part) == BindingState.Invalid || this.HasFailureCauses(part))) ||
            (failure == BindingFailure.InvalidOrigin && node is IsKoto { IsAssociatedConstraint: true } && this.partPrerequisites.TryGetValue(node, out var occurrence) &&
            this.formationCauses?.ContainsKey(occurrence) == true) ||
            (failure == BindingFailure.InvalidAssociatedType && this.partPrerequisites.TryGetValue(node, out var specification) &&
            (this.RecordedFailure(specification) != BindingFailure.None || this.RecordedState(specification) == BindingState.Invalid)) ||
            (failure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var read) &&
            this.formationCauses?.ContainsKey(read) == true) ||
            (failure == BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var premise) &&
            this.KeepsPremiseErrorContext(premise));
    }

    // SPEC 23.1.2, 8.3.3 (Limits): a check whose evidence read, directly or through failures resting on it, a recovery node or a node
    // a reached implementation limit refused (its record's category is Resource) derives every outcome, Unproven, Invalid or any
    // other, from that record and adds none; a refusal at its own site stays its own record. The one place of the rule; the walk
    // reuses the prerequisite storage of PrerequisiteKeys; both run only at publication.
    private bool RestsOnRecoveryOrLimit(Koto node)
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
        var limit = !this.ResourceFailed(node);
        while (pending.TryPop(out var cause))
        {
            if (!visited.Add(cause))
            {
                continue;
            }

            if (IsRecovery(cause, out _) || (limit && this.ResourceFailed(cause)))
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
        if (IsRecovery(part, out _) || (this.RecordedState(part) != BindingState.Resolved && !this.partPrerequisites.ContainsKey(node)))
        {
            this.partPrerequisites[node] = part;
        }
    }

    /// <summary>Records a part that a later check of the pass judges, such as a projection use, whose conformance is checked after the
    /// bodies (DECISIONS BU-61): the first part is kept whatever its state now, and UnresolvedPart reads its state at publication.</summary>
    /// <param name="node">The failed node.</param>
    /// <param name="part">The part its failure rests on when that part fails.</param>
    private void AddPendingPrerequisite(Koto node, Koto part) => this.partPrerequisites.TryAdd(node, part);

    // A node whose failure is a reached implementation limit: its record's category is Resource (SPEC 23.1.1).
    private bool ResourceFailed(Koto node)
        => this.RecordedFailure(node) is var failure && failure != BindingFailure.None && DiagnosticEntries.TryGet(this.CodeOf(node, failure), out var entry) &&
            entry.Category == DiagnosticCategory.Resource;

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
                if (this.IsUnresolvedPrerequisite(this.prerequisiteStore[i]))
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
        => !IsRecovery(node, out _) && this.RecordedFailure(node) is var failure && failure != BindingFailure.None ? [node.KeyOf(DiagnosticRequirement.Binding(failure))] : this.PrerequisiteKeys(node);

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
                : this.RecordedFailure(cause) is var failure && failure != BindingFailure.None ? cause.KeyOf(DiagnosticRequirement.Binding(failure))
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
                if (this.IsUnresolvedPrerequisite(cause))
                {
                    pending.Push(cause);
                }
            }
        }

        return pending.Count != count;
    }
}
