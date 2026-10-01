// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private const string RangeShapeAdvice = "If the function only resolves a range for slicing, accept R with R is PositionRange; if it enumerates, require the matching Iterable, UniqIterable or IntoIterable entry and its Item constraints; if it accesses boundaries, retain the required concrete range Type. Verify the function body after changing its contract";

    // SPEC 10.6: the acquisitions an acquisition conflict admits, appended to the whole written argument. @copy is offered only
    // when the Type is proven Copy and @move only when the Place offers Take; the selection that follows is not claimed.
    private const string AcquisitionAdvice = "Append @ref to the whole argument to borrow the Place";
    private const string AcquisitionAdviceCopy = "Append @ref to the whole argument to borrow the Place, or @copy to pass a Copy of it";
    private const string AcquisitionAdviceMove = "Append @ref to the whole argument to borrow the Place, or @move to transfer it";
    private const string AcquisitionAdviceCopyMove = "Append @ref to the whole argument to borrow the Place, @copy to pass a Copy of it, or @move to transfer it";

    // SPEC 23.3.6.4: the operands a node consulted that did not resolve are its explicit prerequisites. They are recorded
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

    // SPEC 10.2.2, 10.6: the arguments of a call whose candidates disagree on acquiring a bare Place, recorded only when it fails.
    // The stores are reused across passes, so a warm rebind of the failure allocates nothing.
    private readonly Dictionary<Koto, (int Start, int Count)> acquisitionConflicts = new(ReferenceEqualityComparer.Instance);
    private readonly List<AcquisitionConflict> acquisitionConflictStore = [];
    private readonly List<AcquisitionParty> acquisitionPartyStore = [];

    // Keep proof failures intact. Only diagnostic publication follows these recorded
    // missing-name causes; later validators must still see an invalid declaration.
    private Dictionary<Koto, Koto>? constraintDiagnosticCauses;
    private Dictionary<Koto, BindingSymbol>? objectPayloadCauses;
    private MissingConstraintNameVisitor? missingConstraintNameVisitor;
    private int consultationStart = -1;
    private Koto? consultationNode;

    // The expression being bound without its expected Type because the syntax that supplies it failed.
    private (Koto Expression, Koto Cause)? missingExpectation;

    // SPEC 23.3.6.2: the Types a mismatch compared and the syntax that shows it, recorded only when a check fails.
    private Dictionary<Koto, (Koto At, object Actual, object Expected)>? mismatches;

    // The selected iteration entry and the range whose boundary Types cannot supply it.
    private Dictionary<Koto, (BoundType Subject, BindingSymbol Entry)>? rangeIterationFailures;

    // The target a write could not use, recorded only when the check fails; it is the smallest syntax that shows the failure.
    private Dictionary<Koto, Koto>? writeTargets;

    // The candidates a failed overload selection considered, recorded only when it fails.
    private Dictionary<Koto, RejectedCandidate[]>? rejectedCandidates;

    private readonly record struct RejectedCandidate(FunctionKoto Function, BoundType? Actual, BoundType? Expected);

    // A conflicting argument with its stored Type, whether that Type is proven Copy, whether the Place offers Take, and the
    // range of its candidates in the party store.
    private readonly record struct AcquisitionConflict(Koto Argument, BoundType Type, bool CopyProven, bool Take, int PartyStart, int PartyCount);

    // A candidate that acquires the argument by value or newly borrows it, with the parameter and its substituted Type.
    private readonly record struct AcquisitionParty(FunctionKoto Function, int ParameterIndex, BoundType? ParameterType, bool ByValue);

    private static bool DifferentRangeShapes(BoundType actual, BoundType expected)
    {
        while (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            actual = actual.Components[0];
        }

        while (expected is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            expected = expected.Components[0];
        }

        return actual.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange &&
            expected.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange && actual.Symbol != expected.Symbol;
    }

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues => this.derivedIssues;

    /// <summary>Gets the Binding causes of a node that a later phase checks: none when Binding resolved it or recorded no cause.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The check keys, or <see langword="null"/>.</returns>
    internal DiagnosticKey[]? FailureCauses(Koto node)
        => node.BindingState != BindingState.Resolved && (IsRecovery(node, out _) || node.BindingFailure != BindingFailure.None || this.HasUnresolvedPrerequisite(node))
            ? this.CauseKeys(node) : null;

    // A recovery node, an ErrorKoto or synthesized syntax kept in place of a rejected form, stands for its syntax error.
    private static bool IsRecovery(Koto node, out DiagnosticKey cause)
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

    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        this.prerequisites[node] = (this.prerequisiteStore.Count, 1);
        this.prerequisiteStore.Add(cause);
        return Complete(node, null);
    }

    /// <summary>Binds an expression against the Type that a written syntax supplies; when that syntax failed, a check that
    /// needs the expected Type names it as the prerequisite (SPEC 23.3.6.4).</summary>
    private BoundType? BindExpected(Koto node, BindingScope scope, BoundType? expected, Koto? supplier)
    {
        if (expected is not null || supplier is null || supplier.BindingState == BindingState.Resolved)
        {
            return this.BindNode(node, scope, expected);
        }

        var saved = this.missingExpectation;
        this.missingExpectation = (node, supplier);
        var type = this.BindNode(node, scope);
        this.missingExpectation = saved;
        return type;
    }

    // An inferred case that is the value of an expression whose expected Type failed rests on that failure.
    private Koto? MissingExpectationCause(Koto reference)
    {
        if (this.missingExpectation is not { } missing)
        {
            return null;
        }

        var value = missing.Expression;
        while (true)
        {
            value = KotoHelper.UnwrapParentheses(value);
            if (value is not CodeBlockKoto { IsExpressionBody: true, Items.Count: 1 } body)
            {
                break;
            }

            value = body.Items[0];
        }

        return ReferenceEquals(value, reference) || (value is InvocationKoto { Method: var method } && ReferenceEquals(method, reference)) ? missing.Cause : null;
    }

    /// <summary>Fails a node whose value does not have the Type its use expects, recording both Types as evidence.</summary>
    /// <param name="node">The node whose check failed.</param>
    /// <param name="at">The smallest syntax that shows the mismatch, such as the value.</param>
    /// <param name="actual">The value's Type.</param>
    /// <param name="expected">The expected Type.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailMismatch(Koto node, Koto at, BoundType actual, BoundType expected)
        => this.RecordMismatch(node, at, actual, expected);

    // An untyped literal is described by its category, such as "integer literal".
    private BoundType? FailMismatch(Koto node, Koto at, string actual, string expected)
        => this.RecordMismatch(node, at, actual, expected);

    private BoundType? RecordMismatch(Koto node, Koto at, object actual, object expected)
    {
        // Keep semantic identities even when their short names agree. Format only at publication, never during Binding.
        if (node.BindingFailure == BindingFailure.None)
        {
            (this.mismatches ??= new(ReferenceEqualityComparer.Instance))[node] = (at, actual, expected);
        }

        return this.Fail(node, BindingFailure.TypeMismatch);
    }

    /// <summary>Fails a write whose target's path denies it (SPEC 3.4, 15.1.5), recording the target.</summary>
    /// <param name="node">The write.</param>
    /// <param name="target">The written target.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailWrite(Koto node, Koto target)
    {
        if (node.BindingFailure == BindingFailure.None)
        {
            (this.writeTargets ??= new(ReferenceEqualityComparer.Instance))[node] = target;
        }

        return this.Fail(node, AccessFailure(target));
    }

    // Reports a write failure at its target; an assignment names the target, and a let root gets conditional advice.
    private void ReportWrite(Koto node, Koto target, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (code != DiagnosticCode.InvalidAssignment_Kd)
        {
            node.Report(requirement, code, at: target, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [target.ToString()] : null);
            return;
        }

        var root = KotoHelper.UnwrapParentheses(target);
        while (root is MemberAccessKoto or IndexKoto)
        {
            root = KotoHelper.UnwrapParentheses(root is MemberAccessKoto member ? member.Left : ((IndexKoto)root).Left);
        }

        var immutable = root is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { VariableKind: VariableKind.Let } } };
        node.Report(requirement, code, at: target, evidence: [target.ToString()], advice: immutable ? "Declare the binding with var to assign it again" : null);
    }

    // SPEC 10.6: one record per conflicting argument, in source order, located at the argument and relating each candidate's
    // parameter Type. The call is the subject; the condition number distinguishes its arguments, and the first one carries the
    // check that problems depending on the call name as their prerequisite.
    private void ReportAcquisitionConflicts(Koto call, DiagnosticRequirement requirement, (int Start, int Count) range)
    {
        for (var k = 0; k < range.Count; k++)
        {
            var conflict = this.acquisitionConflictStore[range.Start + k];
            var type = DiagnosticTypeName(conflict.Type);
            var related = new (string Role, Koto At, string? Label)[conflict.PartyCount];
            for (var p = 0; p < related.Length; p++)
            {
                var party = this.acquisitionPartyStore[conflict.PartyStart + p];
                var parameter = party.ParameterType is { } shown ? DiagnosticTypeName(shown) : type;
                related[p] = ("candidate", party.Function.Parameters[party.ParameterIndex].Type, party.ByValue ? $"{party.Function.Name} acquires it by value as {parameter}" : $"{party.Function.Name} borrows it as {parameter}");
            }

            var advice = conflict.CopyProven ? (conflict.Take ? AcquisitionAdviceCopyMove : AcquisitionAdviceCopy) : conflict.Take ? AcquisitionAdviceMove : AcquisitionAdvice;
            var note = conflict.CopyProven ? null : $"{type} is not proven Copy, so a by-value candidate cannot Copy this Place";
            call.Report(requirement, DiagnosticCode.AcquisitionRequired_Kd, note: note, at: conflict.Argument, evidence: [type], advice: advice, related: related, condition: (ushort)k);
        }
    }

    private void ResetPrerequisites()
    {
        this.mismatches?.Clear();
        this.rangeIterationFailures?.Clear();
        this.writeTargets?.Clear();
        this.rejectedCandidates?.Clear();
        this.acquisitionConflicts.Clear();
        this.acquisitionConflictStore.Clear();
        this.acquisitionPartyStore.Clear();
        this.duplicateDeclarations?.Clear();
        this.prerequisites.Clear();
        this.prerequisiteStore.Clear();
        this.derivedIssues.Clear();
        this.consulted.Clear();
        this.consultationStart = -1;
        this.consultationNode = null;
        this.missingExpectation = null;
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

    /// <summary>Records that the node being bound read the result of another node; one that did not resolve becomes a prerequisite.</summary>
    /// <param name="dependency">The node whose result was read.</param>
    private void Consulted(Koto dependency)
    {
        if (this.consultationStart >= 0 && dependency.BindingState != BindingState.Resolved && !ReferenceEquals(dependency, this.consultationNode))
        {
            this.consulted.Add(dependency);
        }
    }

    // A recovery node derives from its syntax error, and so does every failure of a check that consulted a recovery node,
    // directly or through unresolved operands: the parser's guess explains what the check combined (DIAGNOSTICS.md §4.3).
    // A failure that reports missing information is derived when the check consulted prerequisites that stayed unresolved;
    // a failure explained by the Origin rule, or any other definite failure, is direct.
    private bool IsDerived(Koto node)
        => IsRecovery(node, out _) || this.RestsOnRecovery(node) ||
            (node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.HasUnresolvedPrerequisite(node) && this.BorrowOriginHint(node) is null);

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
    /// Declaration checks run once per Bind, so the record outlives the per-pass prerequisite storage.</summary>
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
        if (this.prerequisites.TryGetValue(node, out var range))
        {
            for (var i = range.Start; i < range.Start + range.Count; i++)
            {
                if (this.prerequisiteStore[i].BindingState != BindingState.Resolved)
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
        var pushed = false;
        if (this.partPrerequisites.TryGetValue(node, out var part) && (part.BindingState != BindingState.Resolved || IsRecovery(part, out _)))
        {
            pending.Push(part);
            pushed = true;
        }

        if (!this.prerequisites.TryGetValue(node, out var range))
        {
            return pushed;
        }

        for (var i = range.Start + range.Count - 1; i >= range.Start; i--)
        {
            if (this.prerequisiteStore[i].BindingState != BindingState.Resolved || IsRecovery(this.prerequisiteStore[i], out _))
            {
                pending.Push(this.prerequisiteStore[i]);
                pushed = true;
            }
        }

        return pushed;
    }

    // A lookup that misses a Name the language defines but the implementation does not yet provide meets an implementation
    // limit, not a missing Name (DIAGNOSTICS.md rule 3): a cataloged declaration without source (PLAN G4), or a Property
    // requirement read through a generic receiver (P24).
    private BindingFailure MissingFailure(Koto name, BindingScope scope, BindingFailure missing)
    {
        var limit = name switch
        {
            MemberAccessKoto member when this.requirementGroups.TryGetValue(member, out var group) && group.PropertyRequirement => true,
            MemberAccessKoto member => TypeSpelling(member.Right) is { } spelled && KimiLibraryCatalog.IsUnsourced(KimiLibraryContainer.Intrinsics, spelled) &&
                ReferenceEquals(this.TypeName(member.Left, scope, false)?.Declaration, this.Library.Intrinsics),
            _ => TypeSpelling(name) is { } spelled && KimiLibraryCatalog.IsUnsourced(KimiLibraryContainer.Root, spelled),
        };
        return limit ? BindingFailure.Unsupported : missing;
    }

    // A Type position names a Type: an inaccessible qualifier explains the miss, otherwise the Name is missing.
    private BoundType? FailMissingType(Koto name, BindingScope scope)
        => this.ReportUnavailableQualifier(name, scope) is { } qualifier
            ? this.CompleteDependent(name, qualifier)
            : this.Fail(name, this.MissingFailure(name, scope, BindingFailure.MissingType), true);

    // Called only after both value and Type lookup failed. A tentative Type path must not
    // publish access errors when a valid value path exists in the other namespace.
    // Returns the inaccessible qualifier it failed, which explains the failed lookup.
    private Koto? ReportUnavailableQualifier(Koto syntax, BindingScope scope)
    {
        if (syntax is GenericsKoto generic)
        {
            return this.ReportUnavailableQualifier(generic.Identifier!, scope);
        }

        if (syntax is not MemberAccessKoto member)
        {
            return null;
        }

        if (this.ReportUnavailableQualifier(member.Left, scope) is { } inner)
        {
            return inner;
        }

        if (this.TypeName(member.Left, scope, false) is not { } qualifier || !this.scopes.TryGetValue(qualifier.Declaration, out var members) ||
            TypeSpelling(member.Right) is not { } name || !members.Types.TryGetValue(name, out var first))
        {
            return null;
        }

        for (var candidate = first; candidate is not null; candidate = candidate.Next)
        {
            if (this.Accessible(candidate, scope))
            {
                return null;
            }
        }

        this.Fail(member, BindingFailure.Access);
        return member;
    }

    /// <summary>Rejects an object form, creation, cast or runtime test over a Type that opts out of ObjectPayload, naming the declaring Type (SPEC 8.4.7.2).</summary>
    private BoundType? FailObjectPayload(Koto use, BindingSymbol renounced)
    {
        (this.objectPayloadCauses ??= new(ReferenceEqualityComparer.Instance))[use] = renounced;
        return this.Fail(use, BindingFailure.NotObjectPayload);
    }

    private void FailConstraint(Koto use, Koto? diagnosticCause = null)
    {
        if (use.BindingFailure != BindingFailure.None)
        {
            return;
        }

        this.Fail(use, BindingFailure.InvalidConstraint);
        if (diagnosticCause is null && use is IsKoto clause)
        {
            diagnosticCause = this.FindMissingConformanceName(clause);
        }

        if (diagnosticCause is not null)
        {
            (this.constraintDiagnosticCauses ??= new(ReferenceEqualityComparer.Instance))[use] = diagnosticCause;
        }
    }

    private Koto? ConformanceDiagnosticCause(Koto owner)
        => owner is StructKoto or EnumKoto && owner.BindingFailure == BindingFailure.InvalidConstraint &&
            this.constraintDiagnosticCauses?.TryGetValue(owner, out var cause) == true ? cause : null;

    private Koto? FindConformanceDiagnosticCause(Koto owner, BoundConstraint fact)
    {
        if (fact.Kind == ConstraintKind.Error && owner is DeclarationContainerKoto container)
        {
            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (ReferenceEquals(clause.BoundConstraint, fact) && this.FindMissingConformanceName(clause) is { } cause)
                {
                    return cause;
                }
            }
        }

        return null;
    }

    private Koto? FindMissingConformanceName(IsKoto clause)
    {
        if (clause.Parent is not (StructKoto or EnumKoto) || !IsSelfConstraint(clause) ||
            clause.BoundConstraint is null)
        {
            return null;
        }

        var visitor = this.missingConstraintNameVisitor ??= new();
        visitor.Visit(clause.Right);
        var cause = visitor.Cause;
        visitor.Cause = null;
        return cause;
    }

    private sealed class MissingConstraintNameVisitor : KotoVisitor
    {
        internal Koto? Cause { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Cause is not null)
            {
                return;
            }

            if (node.BoundSymbol is null && node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType)
            {
                this.Cause = node;
                return;
            }

            node.VisitChildren(this);
        }
    }
}
