// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Owns a node's direct failure (Fail), consultation and declaration-part prerequisites, their derived failures and published diagnostic keys,
// and RestingCauses, the one cause query of a node a later phase derives a value or a check from (DECISIONS Z16).
// Checks record the parts they read; publication follows those records without binding or proving them again.
// A node's consultation range and part (PrerequisiteRows, PartPrerequisiteRows: Work pass families, DECISIONS BU-220) hold the running
// pass's rows by the pass stamp, and a walk's visit marks (PrerequisiteVisitRows) its walk's number too; the other storage is reused
// between passes and cleared by ResetPrerequisites with the other Binding facts.
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
    private readonly PrerequisiteRows prerequisites;
    private readonly PartPrerequisiteRows partPrerequisites;
    private readonly List<DiagnosticKey> prerequisiteKeys = [];
    private readonly PrerequisiteVisitRows prerequisiteVisits;
    private readonly Stack<Koto> prerequisitePending = new();
    private readonly Stack<Koto> derivationInputs = new();

    // The running walk's number (PrerequisiteVisitRows): a row carrying it is a node this walk visited, any other row one it did not.
    private uint prerequisiteWalk = FirstQuery;
    private int consultationStart = -1;
    private Koto? consultationNode;

    // The pass's change signal (ValidateConstraintEnvironments): advanced by every write of what an environment's judgment reads, in the
    // step that writes it: each Fail call, whether or not the node held a failure already (a conformance's, an identity's or an Access
    // path's Invalid is written with one); each premise failed (FailPremise, FailFacts); and an inference consumer made Invalid under
    // its source's use (InvalidateAssociatedEvidence). PublishSourceStates' state, written with no Fail at each ValidateConformances,
    // only withdraws a premise (AvailableContractPremise); the failure an Error reads (InvalidDeclarationContext) is written through Fail
    // by ValidateClosedTypeConstraints, which the rounds' condition runs before the walk (InvalidateProjectionCertificates).
    private int invalidations;

    // R7-C3 (F's C2 ruling 317 (1)): the census of the Failed state (ConstraintEnvironment's contract): every premise newly failed
    // (FailPremise) and every unproven input named (FailInput), zeroed with the pass's environments (ResetPass). While it is 0 no
    // environment a judgment reads holds either, so the per-proof readers answer "nothing failed" at once.
    private int premiseFailures;

    // The running pass's part (PassPart.DerivedIssues), which its Check writes (EnterPass).
    private List<BindingIssue> derivedIssues;

    /// <summary>Gets final problems that are explained by failed prerequisites, each by its own requirement; they are reported as derived
    /// problems.</summary>
    internal IReadOnlyList<BindingIssue> DerivedIssues { get; }

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

    /// <summary>Gets the causes a check resting on a node rests on (docs/dev/DIAGNOSTICS.md §4.3), the one cause query of the nodes the
    /// HIR publisher's contract names (<see cref="FailureFact"/>; DECISIONS Z16), by the one rule RestingSource states.</summary>
    /// <param name="node">The node, such as one of GuessedCompletionNodes or a failure fact.</param>
    /// <returns>The causes, or null at a node nothing rejected and Binding recorded no cause for.</returns>
    internal DiagnosticKey[]? RestingCauses(Koto node)
        => !this.RestingSource(node, out var failed, out var own) ? null : failed is not null ? this.CauseKeys(failed) : [own];

    /// <summary>Gets whether a check resting on a node rests on causes (<see cref="RestingCauses"/> is not null), without forming them:
    /// the HIR publisher's contract check, which a warm publish runs without allocating.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether it rests on causes.</returns>
    internal bool HasRestingCauses(Koto node) => this.RestingSource(node, out _, out _);

    /// <summary>Gets whether Binding recorded a cause for a node it did not resolve: the node is a recovery, failed, or rests on an
    /// unresolved prerequisite (a dependent, SPEC 23.1.2).</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether a check that reads the node rests on a Binding cause.</returns>
    internal bool HasFailureCauses(Koto node)
        => this.RecordedState(node) != BindingState.Resolved &&
            (IsRecovery(node, out _) || this.RecordedFailure(node) != BindingFailure.None || this.HasUnresolvedPrerequisite(node));

    // SPEC 23.1.2 (PLAN G1073): the declaration part that gives a slot its Type, where Binding records that Type's failure: a
    // parameter's written Type, an accessor parameter's annotation or Property Type; a definition's result: an accessor's executing
    // function's written result, else its Property's Type or the Property, a function's or Closure's written result Type, else the
    // function; a Property's (a static's or Field's), a local's or a Pattern's declaration; null for any other binding. A use or a
    // written capture entry of a binding without a Type rests on it (ConsultTypeSource: not for a variable, a Property included).
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

        if (symbol is { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto definition })
        {
            return definition.Accessor?.Declaration is PropertyAccessorKoto executed
                ? executed.ReturnType ?? Accessor(executed).Property.Declaration.TypeKoto ?? Accessor(executed).Property.Declaration
                : definition.ReturnType ?? definition;
        }

        return symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.PatternCandidate or BindingSymbolKind.Property ? symbol.Declaration : null;
    }

    // Whether a match (a `try` included) or `for` takes `node` as its Subject (SPEC 15.1.6), as its consumer takes it (Grouped,
    // R4-M145): CompleteDependent's walk stops short of it.
    private static bool IsSubject(Koto node)
    {
        var taken = Grouped(node);
        return ReferenceEquals(taken, taken.Parent switch { MatchKoto match => match.Expression, ForKoto loop => loop.Iterable, _ => null });
    }

    // DIAGNOSTICS 4.3 (a problem is derived only from what its check read): a requirement whose check compares only what declarations
    // declare (their names, normalized signatures, conformance targets or specialization arguments), never what the node's own binding
    // consulted, so neither the node's own recovery nor a recovery or limit it rests on derives it. Census (E2-3b), Duplicate alone, at every
    // site: FailDuplicate's six pairwise sites (Scopes, Aliases, Contracts.Registration twice, Signatures, Specializations;
    // RecordDuplicateGroups records them), a Semantics parameter's name (Indexing), a nested container's name and a value member's name
    // over an inherited member (MemberLookup), a repeated special receiver (Structs' IndexSpecialReceiver), `Kimi` and a module's
    // conflicting names (Modules), a capture list's repeat (CaptureDiagnostics), an Origin set's or annotation's repeat (OriginDeclarations,
    // Origins.Annotations, AssociatedOrigins) and Origins.Schemas' three (a container fragment's incompatible header, an Origin name
    // repeating an enclosing abstract Origin, a repeated Origin name).
    [Transitional("R7", Reason = "The row stands for per-problem inputs (d4: a repeated let beside its initializer's syntax error; k2: a repeated Signature whose later header is a recovery), until R7-E5 states what each problem's check read.")]
    private static bool JudgedByDeclaredNames(BindingFailure failure) => failure == BindingFailure.Duplicate;

    // A record of a reached implementation limit: its code's category is Resource (SPEC 23.1.1).
    private static bool IsResource(DiagnosticCode code) => DiagnosticEntries.TryGet(code, out var entry) && entry.Category == DiagnosticCategory.Resource;

    // PLAN G1073, the failed-slot fact: a slot is stated when it has its Type or Binding recorded a cause at its TypeSource (its record
    // reported or resting on a reported one), so a published slot of no Type is the failed slot. The HIR publisher's slot rows read it
    // (HasFacts, SlotsStated); a node with a slot neither typed nor failed is Broken. One direction only: a Closure whose written result
    // failed keeps an inferred Type until PLAN D-848.
    private bool Stated(BindingSymbol slot) => slot.Type is not null || (TypeSource(slot) is { } source && this.HasFailureCauses(source));

    // The one rule of what a check resting on a node rests on (RestingCauses; docs/dev/DIAGNOSTICS.md §4.3): a supplied body's syntax
    // Error, else the node's own Binding causes (failed: the node, whose CauseKeys they are), else the unresolved mark for an expression
    // the guess rule counts as unresolved. A node rejected without such a cause (Binding.Rejected) rests on its own record: a failure
    // Binding recorded at it, a recovery's syntax Error, a malformed char literal's lexical Error (CharLiteralKoto.Cause), an admitted
    // misfit's value on its write's fit failure (failed: the write, MisfitWrite). A Place Binding resolved whose acquisition failed, its
    // Move resting on an Error Copy proof (HirAcquire.Failed, DECISIONS BU-103), rests on the unresolved mark: Binding records that
    // proof's failure at the Type's declaration, not at the use (DECISIONS Z16). The row reads a resolved node's Failed, which of
    // BU-103's three kinds only the Error Copy proof gives (no Type leaves a node unresolved; a failed Pattern Type check rests on its
    // Pattern's record); a fourth kind revisits it. False at a node nothing rejected and Binding recorded no cause for.
    private bool RestingSource(Koto node, out Koto? failed, out DiagnosticKey own)
    {
        (failed, own) = (null, DiagnosticKey.Unresolved);
        if (SuppliedCause(node) is { } supplied)
        {
            own = supplied;
            return true;
        }

        if (this.HasFailureCauses(node) || this.RecordedFailure(node) != BindingFailure.None)
        {
            failed = node;
            return true;
        }

        if (IsRecovery(node, out var recovered))
        {
            own = recovered;
            return true;
        }

        if (node is CharLiteralKoto { Value: null } literal)
        {
            own = literal.Cause ?? DiagnosticKey.Unresolved;
            return true;
        }

        failed = MisfitWrite(node) is { } write && this.HasFailureCauses(write) ? write : null;
        return failed is not null || IsUnresolvedGuess(node) ||
            (this.RecordedState(node) == BindingState.Resolved && (HirTables.AdjustOf(node).Acquire == HirAcquire.Failed || HirTables.PlanOf(node).Adjust.Acquire == HirAcquire.Failed));
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

    // A node's pass fact, stored by the recorder's one decision (DECISIONS G949 S1 as R7-N5 B4 and E amend it): the fact a failure
    // carries (a failure-fact family's row, such as `qualificationFailures`' stop), a node's dependence rows (PrerequisiteRows' range
    // and PartPrerequisiteRows' part, which a rest with no failure carries too) and a walk's visit mark (PrerequisiteVisitRows). A node
    // of a module the running pass binds keeps it in its module's column at its SyntaxId, any other node in the part's foreign columns at
    // its entry's index (PassPart.ForeignFacts), the row its entry names; its readers read it as RecordedFailure reads the failure, so
    // no caller chooses a store. A node of another module without the pass's entry reads its own module's column: a frozen library's
    // Work column is released (NodeColumns.Freeze), so it reads none, which is the answer the program pass gave before the columns. No
    // EnsurePassNode refuses it, since these readers are asked of library nodes (ContractMatching's HasFailureCauses of a library
    // declaration, Stated's TypeSource of a library symbol, a library cause a walk reaches).
    private T RecordedFact<T>(Koto node, FactFamily<T> family)
        => this.IndexOfForeign(node) is var i and >= 0 ? family(ref this.pass.Part.ForeignFacts).Get(i, this)
            : node.SyntaxOwner is { } table ? family(ref table.Hir.Families).Get(node.SyntaxId, this) : default!;

    private void SetRecordedFact<T>(Koto node, FactFamily<T> family, T fact) => this.RecordedSlot(node, family) = fact;

    // The writers' one store decision, SetRecordedFact's: a node's row for a write, in its module's column when the running pass binds
    // the module, else in the part's foreign columns at its entry, which the pass's first write to the node makes. A step that reads a
    // row and then writes it (a walk's visit mark, PrerequisiteVisitRows.Add) takes the slot once; the slot is used before the family's
    // next write (PagedColumn's Ref).
    private ref T RecordedSlot<T>(Koto node, FactFamily<T> family)
    {
        if (this.pass.Binds(node.SyntaxOwner))
        {
            return ref family(ref HirTables.Of(node).Families).Ref(node.SyntaxId, this);
        }

        this.ForeignEntry(node);
        var row = this.IndexOfForeign(node);
        ref var facts = ref this.pass.Part.ForeignFacts;
        facts.Resize(this.foreignFailures.Count);
        return ref family(ref facts).Ref(row, this);
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

    // The running pass's entry of a node, or -1: linear in the list, which no program fills today, and at once while the pass has no
    // entry (every read and write of the recorder asks it first, the walks' per node).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int IndexOfForeign(Koto node)
    {
        if (this.foreignFailures.Count == 0)
        {
            return -1;
        }

        for (var i = 0; i < this.foreignFailures.Count; i++)
        {
            if (ReferenceEquals(this.foreignFailures[i].Node, node))
            {
                return i;
            }
        }

        return -1;
    }

    // R7-C3 (F's C2 rulings 272-273): the premises of a declaration whose own judgment failed it (an invalid conditional or block) fail
    // with it, each through the one writer.
    private void FailFacts(BindingScope scope, Koto owner)
    {
        if (scope.Constraints is not { } environment)
        {
            return;
        }

        foreach (var fact in environment.Facts)
        {
            this.FailPremise(environment, fact, owner);
        }
    }

    // R7-C3 (B1'): a premise failed alone (ConstraintEnvironment.FailPremise), counted in the change signal and in the Failed state's
    // census when it newly failed: the environment's judgments read the remainder. True when it newly failed.
    private bool FailPremise(ConstraintEnvironment environment, BoundConstraint fact, Koto owner)
    {
        if (!environment.FailPremise(fact, owner))
        {
            return false;
        }

        this.premiseFailures++;
        this.Invalidated();
        return true;
    }

    // The change signal's one writer: Fail, FailPremise, FailFacts and an evidence record made Invalid with no failure of its own call it.
    private void Invalidated() => this.invalidations++;

    // The recorder: records the failure it is given and decides none (a miss's qualification is the resolver's, FailUnselected).
    private BoundType? Fail(Koto node, BindingFailure failure, bool unresolved = false)
    {
        this.Invalidated();

        // A node failed while another was being checked, such as a qualifier resolved without BindNode: the checked node consulted it.
        if (this.consultationStart >= 0 && !this.ChecksOwnSite(node))
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
    // columns, its Type's included. SPEC 23.1.2 (D-823): a node that rests after the Places built on it were bound (a late rest, such as
    // Check's on a called declaration validation failed) takes them with it: each enclosing group, and each Place whose path's next step
    // (NextPathStep) is the node, rests on it, so no Resolved Place stands over an untyped node (the HIR's compound Place has a Place
    // root). The Places are found before any Type is cleared, since a step's form reads its operand's Type. A walk that ends at a match
    // (a `try` included) or `for` Subject (SPEC 15.1.6) rests nothing above the node: that Subject Place, and each part of it, is the
    // construct's, whose own binding owns a resting Subject (BindMatch; open: PLAN G1076, its arms and body published with failed-slot
    // bindings). While binding, a node's consumers are not bound yet, so the walk stops at once.
    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        this.prerequisites[node] = (this.prerequisiteStore.Count, 1);
        this.prerequisiteStore.Add(cause);
        if (this.pass.Binds(node.SyntaxOwner))
        {
            var top = node;
            while (top.Parent is { BindingState: BindingState.Resolved } parent && this.BuiltOn(parent, top))
            {
                top = parent;
            }

            if (IsSubject(top))
            {
                top = node;
            }

            Complete(node, null);
            for (var place = node; !ReferenceEquals(place, top); place = place.Parent!)
            {
                this.prerequisites[place.Parent!] = (this.prerequisiteStore.Count, 1);
                this.prerequisiteStore.Add(place);
                Complete(place.Parent!, null);
            }

            return null;
        }

        ref var entry = ref this.ForeignEntry(node);
        entry.State = CompletedState(entry.State, null);
        return null;
    }

    // Whether `parent` is a group around `place` or a Place whose path's next step is `place` (NextPathStep, the one enumeration of a
    // path's forms).
    private bool BuiltOn(Koto parent, Koto place)
        => IsGroup(parent) || (this.NextPathStep(parent, out var next) is not (PathStep.Root or PathStep.Candidate or PathStep.Receiver) && ReferenceEquals(next, place));

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
        if (count != 0 && node.BindingState != BindingState.Resolved)
        {
            this.prerequisites[node] = (this.prerequisiteStore.Count, count);
            for (var i = start; i < start + count; i++)
            {
                this.prerequisiteStore.Add(this.consulted[i]);
            }
        }

        this.DropConsultation(parent);
    }

    // Closes a frame whose reads explain no outcome of its node (RequireConstraintInFrame, a site frame): nothing becomes its prerequisite.
    // A frame's one end (EndConsultation's too): its reads leave the collector, and the enclosing frame is open again.
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

    // R7-C3 C3-1b (F's C2 rulings 289-290; SPEC 8.2.3, 23.1.2), R7-E5 c1: whether the open consultation is `site`'s own check. A
    // failure's own checks are its sites, never its readers: the one rule for a named unproven input (UnprovenInputRead) and an identity
    // premise error (IdentityPremiseErrorRead); a failure (Fail) or a read (Consulted) of the frame's own node is no prerequisite of it.
    private bool ChecksOwnSite(Koto site) => ReferenceEquals(site, this.consultationNode);

    /// <summary>Records that the node being bound read the result of another node; one that did not resolve becomes a prerequisite, but for
    /// a part that waits for the call reading it (SPEC 10.5.1), which depends on that call instead.</summary>
    /// <param name="dependency">The node whose result was read.</param>
    private void Consulted(Koto dependency)
    {
        if (this.consultationStart >= 0 && this.RecordedState(dependency) != BindingState.Resolved && !this.ChecksOwnSite(dependency) &&
            !this.IsCheckedCall(dependency))
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
    // directly or through unresolved operands: the parser's guess explains what the check combined (DIAGNOSTICS.md §4.3), except a
    // requirement judged by declared names alone (JudgedByDeclaredNames), whose check reads no guess. A failure that reports missing
    // information is derived when the check consulted a prerequisite that stayed unresolved and whose own record, or the record it rests
    // on, is reported (RestsOnReported; AGENTS Diagnostics, G812 rulings 163-164): a pending prerequisite with no record (a part that
    // rests on what is not decided yet) derives nothing. A failure explained by the Origin rule, or any other definite failure, is direct.
    // A constraint proof that a declaration-level check found failing because a part it read failed, such as the invalid
    // declaration context of a Property (AddPrerequisite), is a consequence of that part. A Type mismatch over a Type in an erroneous
    // identity class rests on the premise error recorded at the declaration (SPEC 8.2.3: never again at uses). `failure` is the
    // requirement judged and `code` its record's code: the caller passes the problem's (CollectProblem: the slot's or a further one's);
    // each row reads the node's recorded inputs.
    private bool IsDerived(Koto node, BindingFailure failure, DiagnosticCode code)
        => (!JudgedByDeclaredNames(failure) && (IsRecovery(node, out _) || this.RestsOnRecoveryOrLimit(node, code))) ||
            (failure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.RestsOnReported(node)) ||
            (failure is BindingFailure.InvalidConstraint or BindingFailure.Unsupported && this.partPrerequisites.TryGetValue(node, out var part) &&
            (this.RecordedState(part) == BindingState.Invalid || this.HasFailureCauses(part))) ||
            (failure == BindingFailure.InvalidOrigin && node is IsKoto { IsAssociatedConstraint: true } && this.partPrerequisites.TryGetValue(node, out var occurrence) &&
            this.formationCauses.ContainsKey(occurrence)) ||
            (failure == BindingFailure.InvalidAssociatedType && this.partPrerequisites.TryGetValue(node, out var specification) &&
            (this.RecordedFailure(specification) != BindingFailure.None || this.RecordedState(specification) == BindingState.Invalid)) ||
            (failure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var read) &&
            this.formationCauses.ContainsKey(read)) ||
            (failure == BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var premise) &&
            this.KeepsPremiseErrorContext(premise));

    // SPEC 23.1.2, 8.3.3 (Limits): a check whose evidence read, directly or through failures resting on it, a recovery node or a node
    // holding a reached implementation limit (HoldsLimit) derives every outcome, Unproven, Invalid or any other, from that record and
    // adds none; a refusal at its own site stays its own record: a problem whose own code is Resource is never derived from a limit,
    // and a Language problem beside a limit at one node is judged by its own inputs (the problem's code, not its node's first failure;
    // for the slot's issue the two are one). The one place of the rule; the walk reuses the prerequisite storage of PrerequisiteKeys;
    // both run only at publication.
    private bool RestsOnRecoveryOrLimit(Koto node, DiagnosticCode code)
    {
        var pending = this.prerequisitePending;
        if (!this.PushPrerequisites(node, pending))
        {
            return false;
        }

        var visits = this.prerequisiteVisits;
        visits.Add(node);
        var found = false;
        var limit = !IsResource(code);
        while (pending.TryPop(out var cause))
        {
            if (!visits.Add(cause))
            {
                continue;
            }

            if (IsRecovery(cause, out _) || (limit && this.HoldsLimit(cause)))
            {
                found = true;
                pending.Clear();
                break;
            }

            this.PushPrerequisites(cause, pending);
        }

        visits.Clear();
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

    // SPEC 23.1.2 (rule W): an owner whose check read a failed part and fails only because of it is Invalid resting on that part, with no
    // failure of its own, so it is derived and records nothing. That holds only where the part's own record publishes: an attribute's
    // target on its rejected attribute (InvalidateAttributeTarget), whose record is direct, since an attribute carries no frame or part
    // another check writes, and a layer of a written Type that forms its operand's Type (a wrapper, parentheses, `owner/`) on the Type
    // node it is (RestLayersOnType, R7-V1c), whose record ValidateConstraintUses writes at that node. The state change is a change signal (Invalidated; before the pass's first environment walk it moves none).
    private void RestOnPart(Koto owner, Koto part)
    {
        this.Invalidated();
        this.SetRecordedState(owner, BindingState.Invalid);
        this.AddPrerequisite(owner, part);
    }

    /// <summary>Records a part that a later check of the pass judges, such as a projection use, whose conformance is checked after the
    /// bodies (DECISIONS BU-61): the first part is kept whatever its state now, and UnresolvedPart reads its state at publication.</summary>
    /// <param name="node">The failed node.</param>
    /// <param name="part">The part its failure rests on when that part fails.</param>
    private void AddPendingPrerequisite(Koto node, Koto part) => this.partPrerequisites.TryAdd(node, part);

    // A node whose failure is a reached implementation limit: its record's category is Resource (SPEC 23.1.1).
    private bool ResourceFailed(Koto node)
        => this.RecordedFailure(node) is var failure && failure != BindingFailure.None && IsResource(this.CodeOf(node, failure));

    // SPEC 8.3.3 (Limits), 23.1.1: an input holds a reached implementation limit when its slot's failure is Resource (ResourceFailed: a
    // Binding limit is recorded through Fail alone, CaseLimit, TypeHeightLimit, PremiseClosureLimit and ResolutionWorkLimit) or one of its
    // problems is, whatever its first record (F's ruling 339, the review's B1). The limit half's one input test (RestsOnRecoveryOrLimit),
    // read only at publication; a valid program has no problem and pays the slot's test alone.
    private bool HoldsLimit(Koto node)
    {
        if (this.ResourceFailed(node))
        {
            return true;
        }

        for (var i = this.report.Count == 0 ? -1 : this.report.First(SourceRef.Written(node)); i >= 0; i = this.report.Next(i))
        {
            if (IsResource(this.report.Problem(i).Code))
            {
                return true;
            }
        }

        return false;
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
        var keys = this.AddPrerequisiteKeys(node);
        DiagnosticKey[] result = keys.Count == 0 ? [DiagnosticKey.Unresolved] : [.. keys];
        keys.Clear();
        return result;
    }

    // SPEC 23.1.2, 10.5.1 (AGENTS Diagnostics; PLAN G924, ruling 115 B2; G812 rulings 163-164): whether an input's own record, or the
    // record it rests on, is reported: a recovery, its own record, or a record its prerequisites reach, directly or through failures
    // resting on them (the records PrerequisiteKeys names); one that reaches only parts without a record rests on what is not decided
    // yet. The one test of dependence on an input: EndAwait and a split call's completion (CompleteOnSplit) read it of a part, and a
    // record's derivation (RestsOnReported) of each of its inputs; nothing is allocated.
    private bool FailureReported(Koto node)
    {
        if (IsRecovery(node, out _) || node.BindingFailure != BindingFailure.None)
        {
            return true;
        }

        var keys = this.AddPrerequisiteKeys(node);
        var reported = false;
        foreach (var key in keys)
        {
            reported |= !key.IsUnresolved;
        }

        keys.Clear();
        return reported;
    }

    // Whether a record at `node` rests on an input whose own record, or the record it rests on, is reported (FailureReported, read for
    // each unresolved input PushPrerequisites names): IsDerived's test of a missing-information record.
    private bool RestsOnReported(Koto node)
    {
        var inputs = this.derivationInputs;
        this.PushPrerequisites(node, inputs);
        var reported = false;
        while (inputs.TryPop(out var input))
        {
            this.prerequisiteVisits.Add(node); // A walk back to the record itself reaches no input of it.
            reported = reported || this.FailureReported(input);
            this.prerequisiteVisits.Clear(); // The walk ends, also where FailureReported answered without one (its mark of `node` goes).
        }

        return reported;
    }

    // The walk of PrerequisiteKeys into its reused list, which the caller clears.
    private List<DiagnosticKey> AddPrerequisiteKeys(Koto node)
    {
        var keys = this.prerequisiteKeys;
        var visits = this.prerequisiteVisits;
        var pending = this.prerequisitePending;
        visits.Add(node);
        this.PushPrerequisites(node, pending);
        while (pending.TryPop(out var cause))
        {
            if (!visits.Add(cause))
            {
                continue;
            }

            var key = IsRecovery(cause, out var syntax) ? syntax
                : this.RecordedFailure(cause) is var failure && failure != BindingFailure.None ? cause.KeyIn(this.compilation.StoreOf(cause), DiagnosticRequirement.Binding(failure))
                : this.PushPrerequisites(cause, pending) ? (DiagnosticKey?)null
                : DiagnosticKey.Unresolved;
            if (key is { } found && !keys.Contains(found))
            {
                keys.Add(found);
            }
        }

        visits.Clear();
        return keys;
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

    /// <summary>PLAN G651 a1 (R7-C6 a, F's C8 rulings 9 and 13): where a proof query is asked from, an input of every proof entry and
    /// every proof opener (BeginProof's callers at nesting 0), so a query that names no site does not compile: the node of the judgment
    /// that needs the answer (a Koto converts to it: the node whose validity, acquisition or published plan reads the verdict); Nested,
    /// no frame of its own: a scope inside a running proof or capability work (a connective's operand, a clause of a clause list, a
    /// work's read), a query SubstituteType's family asks while it forms a Type, or a publication whose checks open their own site
    /// frames, whose site is the open frame's; AfterPass, a query after Binding's pass (MIR, R7-M38's after-pass rule, PLAN G651 U2).
    /// Nested and AfterPass are one value, a query that opens no frame of its own; their two names are the census's reading.
    /// SubstituteType's family takes no site: its refusal reads the open consultation frame, the site's one carrier (BU-134, BU-161);
    /// the frame at a judgment's node where none is open is R7-C6 a's second commit (the outermost BeginProof).</summary>
    internal readonly struct QuerySite
    {
        private QuerySite(Koto node) => this.Node = node;

        /// <summary>Gets a scope nested in a running proof or capability work.</summary>
        internal static QuerySite Nested => default;

        /// <summary>Gets a query after Binding's pass.</summary>
        internal static QuerySite AfterPass => default;

        /// <summary>Gets the judgment's node; null for Nested and AfterPass.</summary>
        internal Koto? Node { get; }

        /// <summary>The judgment at a node.</summary>
        /// <param name="node">The node whose judgment needs the answer.</param>
        /// <returns>The site at the node.</returns>
        public static implicit operator QuerySite(Koto node) => new(node);
    }

    // R7-N5 E (DECISIONS BU-220 as R7-N5 B2a-2 and B4 amend it): the Prerequisites family's one accessor, a node's consultation range in
    // prerequisiteStore (Count 0: none). Work: the running pass's dependence store, read by its checks and its publication only, released
    // at the freeze (on dev the program pass's ResetPass emptied the library pass's rows before any reader ran). The recorder decides the
    // store (RecordedFact, SetRecordedFact), so a node of a module the running pass does not bind, which CompleteDependent rests, keeps its
    // range in the pass's foreign columns at its entry, never in a frozen table.
    private readonly struct PrerequisiteRows(Binding binding)
    {
        internal static ref StampedColumn<(int Start, int Count), PassCounter> Family(ref NodeColumns columns) => ref columns.Prerequisites;

        internal (int Start, int Count) this[Koto node]
        {
            set => binding.SetRecordedFact(node, Family, value);
        }

        internal bool TryGetValue(Koto node, out (int Start, int Count) range) => (range = binding.RecordedFact(node, Family)).Count != 0;

        internal bool ContainsKey(Koto node) => binding.RecordedFact(node, Family).Count != 0;

        // A step that fails a use again (VerifyEarlyReads, VerifyEarlyProjections) takes its earlier range off: the row reads absent.
        // Writes only over a present row, so a node without a range numbers no node, carves no page and makes no foreign entry.
        internal void Remove(Koto node)
        {
            if (this.ContainsKey(node))
            {
                this[node] = default;
            }
        }
    }

    // R7-N5 E: the PartPrerequisites family's one accessor, the part a declaration-level check read outside a frame (AddPrerequisite,
    // AddPendingPrerequisite, and the checks that name a part: ContractMatching, AssociatedInference, AssociatedOrigins, OwnedConversions,
    // Specializations, and Check's Final-mode unformed Field read); null: none. Work, stored by the recorder's decision, as
    // PrerequisiteRows.
    private readonly struct PartPrerequisiteRows(Binding binding)
    {
        internal static ref StampedColumn<Koto?, PassCounter> Family(ref NodeColumns columns) => ref columns.PartPrerequisites;

        internal Koto this[Koto node]
        {
            set => binding.SetRecordedFact(node, Family, value);
        }

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out Koto? part) => (part = binding.RecordedFact(node, Family)) is not null;

        internal bool ContainsKey(Koto node) => binding.RecordedFact(node, Family) is not null;

        // The first part stays (AddPendingPrerequisite); a caller that writes only an absent row tests it first (OwnedConversions).
        internal bool TryAdd(Koto node, Koto part)
        {
            if (this.ContainsKey(node))
            {
                return false;
            }

            this[node] = part;
            return true;
        }

        // Dictionary.Add's contract: a second part at one node is a caller's broken contract (both callers test ContainsKey first).
        internal void Add(Koto node, Koto part)
        {
            if (!this.TryAdd(node, part))
            {
                throw new InvalidOperationException($"`{node}` already has a part prerequisite in this pass (InternalInvariant; R7-N5 E).");
            }
        }
    }

    // R7-N5 E: the PrerequisiteVisits family's one accessor, the two publication walks' visited set (RestsOnRecoveryOrLimit,
    // AddPrerequisiteKeys), a walk's scratch: written and read only inside one walk, empty again at its end. Work (DECISIONS BU-220 as
    // R7-N5 B6b and D-986 amend it): a row carries the walk's number (prerequisiteWalk), a row of another walk absent, so Clear is the
    // number's advance (NextQuery), never a walk over the rows; released at the freeze. Stored by the recorder's decision, as
    // PrerequisiteRows. Premise: a walk meets a node of a module the pass does not bind only where the pass made its entry (a cause it
    // failed or rested), so a mark is that entry's row; the whole suite counts no mark that made an entry (R7-N5 E's probe, C2). A mark
    // at such a node without an entry would make its entry with the node's own state and failure, which CountStates counts with
    // Before equal to State and CollectIssue skips (no failure): count-neutral, and every read through it the node's own.
    private readonly struct PrerequisiteVisitRows(Binding binding)
    {
        internal static ref StampedColumn<uint, PassCounter> Family(ref NodeColumns columns) => ref columns.PrerequisiteVisits;

        // HashSet.Add's answer: false when this walk visited the node already. One slot of the recorder's (RecordedSlot) is read and
        // written, so a visit makes one store decision, as HashSet.Add made one hash lookup.
        internal bool Add(Koto node)
        {
            ref var mark = ref binding.RecordedSlot(node, Family);
            if (mark == binding.prerequisiteWalk)
            {
                return false;
            }

            mark = binding.prerequisiteWalk;
            return true;
        }

        // Ends the walk: the next walk's number makes every row this one wrote absent.
        internal void Clear() => binding.prerequisiteWalk = NextQuery(binding.prerequisiteWalk);
    }
}
