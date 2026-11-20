// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// ResolvePath: a written name resolved to its declaration by SPEC 9.5's stages, 9.6's member steps and `::` paths (DECISIONS BU-122).
// - Responsibility: the one evaluation of a Type name or an unqualified value name and its one publication. c, d, e and f bring the rest
//   of name resolution here.
// - Inputs: the name's syntax, the use's scope, the role (its namespace with it) and the written Type-argument count.
// - Outputs: a ResolvedPath (how the path ended, the selected symbol, a stage stop's typed facts); its steps stand on nameSteps.
// - Invariants: evaluation writes nothing on the use's nodes or in a table keyed by them, and reads no state a publication writes. It may
//   force a declaration query (a base clause, an alias target), whose facts and records are that declaration's own. An evaluation forms
//   no input whose form depends on the undecided reading; an input every reading forms alike (a written Contract reference) is formed by
//   its own former first. PublishPath alone writes, once. One path stands above a mark, but for an adaptation target, whose roles are
//   compared (ResolveAdaptationTarget). A
//   Container's members are read through MemberTypes (SPEC 9.6, 18.2.1): no path, `::` member step or import stage reaches a formal
//   parameter, which only the scope walk finds.
// - A component returns through the use verdict (Supported, SPEC 22.1) as the walk completes it, before it is handed on as a qualifier
//   or ends the path; at a refused one the walk ends Unresolved and the refused Name carries the record, an Unsupported step (an
//   ordinary path ends at that Name, a `::` path at its outer member node). No publication arm judges.
// - Prohibited: reading an outcome from anywhere but the result; a second stage walk; a publish on the way.
// - Published per use (R7-N1e2-1R): a Type-name use's lookup is published on the use (HirTables.Lookups: its end; its selection in the
//   Symbols column), once; NamePath, the only entry outside this file, reads the published lookup on every later call. The row is the
//   use's position's (UseRow), which no caller passes and no column stores.
// - A member access a former selects (SelectMember, BaseMember, ConstructorMember, a length argument's group member, the qualified Case
//   pattern) is published by PublishPath too, as a MemberAccess step: its member, lookup, side and Type-path qualifier; a second
//   publication keeps the side (R7-N1c). A projection path's selection (an associated Type, a member Type of its normal form) is its
//   ProjectionMember step, an inaccessible member a lookup met where it selected none its HiddenMember step (R7-N1 c-b). A call's
//   overload selection rewrites the member's Name later (BindCall).
// - A member access's left reading (ReadLeft, SPEC 9.6) is decided here before either reading publishes. A projection path in the left
//   is evaluated, not formed (EvaluateProjection: the associated search alone, ProjectionEvidence, ProjectionIdentity and
//   NormalizedProjection, the former's own steps), its written Contract reference formed first by its former (ProjectionQualifier), and
//   the premises a proved projection's Contract supplies (AddContractPremises, the former's one supply) added to the use scope's kept
//   layer (BindingScope.EvaluatedPremises), never to that scope; BindAssociatedProjection forms the projection after the reading is
//   decided, so every question (ReadLeft, ResultSurvey's MayBeValueQualifier) gives one answer whatever was published before it.

// SPEC 9.2's Lookup Roles: a use's syntactic position chooses one (UseRow), the row alone decides eligibility (HasLookupRole), and
// Qualifier alone reaches stage 4 (SPEC 9.5). Type legality rejects the rest after selection, never resuming lookup.
internal enum LookupRole : byte
{
    Qualifier,
    Core,
    ObjectViewTarget,
    TypeSemantics,
    Requirement,
    ContractReference,
    Value,
    EnumCase,
}

// How a lookup ended; None is a use no lookup has published. Projection: the path names an associated Type of its left's Type (SPEC
// 8.5.2): the suffix `.(C)`, a Name its Core left may have an associated Type of and declares no member Type of, or any Name after a
// projection; the reader forms it through BindAssociatedProjection.
internal enum PathEnd : byte
{
    None,
    Selected,
    Unresolved,
    Ambiguous,
    QualificationStop,
    Projection,
}

public sealed partial class Binding
{
    private readonly List<NameStep> nameSteps = new();
    private int nameWalks;

    // The member steps the Type walks evaluated (a scaling check's measure, TypeDagScalingTest).
    internal int NameWalkWork => this.nameWalks;

    private enum NameStepKind : byte
    {
        Head,
        ImportGroup,
        Qualifier,
        Member,
        InheritedMember,
        RootMember,
        RootOperand,
        AdaptationTarget,
        AmbiguousRole,
        Unsupported,
        MemberAccess,
        ProjectionMember,
        HiddenMember,
    }

    // ReadLeft's verdict: the reading of a member access's left that selects its member (SPEC 9.6).
    private enum LeftReading : byte
    {
        Type,
        Value,
        Ambiguous,
        Unsupported,
        ValueFailure, // No path succeeds; the value path failed at its own stage (a stop or a conflict), whose record the access rests on.
    }

    // LeftValue's verdict: a member access's left's value reading (SPEC 9.6).
    private enum LeftValueKind : byte
    {
        None,
        Possible,
        StageFailure,
        StepFailure,
    }

    // One step of a path, with what its publication writes (PublishPath's table). A Head's role names the namespace whose stop it clears.
    // A Qualifier is a path's left use, published with its end (End). An Unsupported step is a component the use verdict refused
    // (Supported), its Use the component's Name. A MemberAccess is a selected member of a member access (SPEC 9.6): its role names the
    // namespace of the path that selected it (Value: the value path, a receiver's member; Qualifier: the Type path), its Qualifier the
    // Type path's left to publish, its Selection the member lookup (none for a constructor). Every site writes every field (no optional
    // parameter, CR-R2); a field its kind does not read is null or default.
    private readonly record struct NameStep(
        NameStepKind Kind, Koto Use, BindingSymbol? Symbol, BindingSymbol? Qualifier, AliasImport Import, LookupRole Role, PathEnd End, MemberSelection? Selection);

    private readonly record struct ResolvedPath(PathEnd End, BindingSymbol? Symbol, Koto Use, QualificationFact Stop = default);

    // SPEC 9.2's eligible declarations of each row. Qualifier: groups, Kotonoha references and the Types that qualify members (no pair
    // binder); Requirement: what requirement syntax admits (SPEC 8.2.1: subjects, Contracts and complete Types); Contract Reference: Contracts.
    private static bool HasLookupRole(BindingSymbol candidate, LookupRole role) => role switch
    {
        LookupRole.Qualifier => candidate.Kind != BindingSymbolKind.SemanticsParameter,
        LookupRole.Core => IsCore(candidate),
        LookupRole.ObjectViewTarget => IsCore(candidate) || candidate.IsContract,
        LookupRole.TypeSemantics => candidate.Kind == BindingSymbolKind.SemanticsParameter,
        LookupRole.Requirement => candidate.Kind != BindingSymbolKind.Container,
        LookupRole.ContractReference => candidate.IsContract,
        LookupRole.Value => true,
        LookupRole.EnumCase => candidate.EnumCase is not null,
        _ => throw new UnreachableException(),
    };

    // SPEC 9.2: a role's namespace: Value and Enum Case are the Value namespace's rows, every other row the Type namespace's.
    private static bool InValueNamespace(LookupRole role) => role is LookupRole.Value or LookupRole.EnumCase;

    private static bool IsCore(BindingSymbol candidate) => !candidate.IsContract &&
        candidate.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType;

    private static bool IsAdaptationTarget(Koto syntax)
    {
        var target = syntax;
        while (target.Parent is TypeSemanticsKoto { IsTransparentWrapper: true } wrapper)
        {
            target = wrapper;
        }

        return target.Parent is ConversionKoto conversion && ReferenceEquals(conversion.Right, target);
    }

    // SPEC 9.2: the one table of a Type-name use's row, by its syntactic position (the position seen through parentheses and transparent
    // wrappers). A new form adds a row here; NamePath asks no caller.
    private static LookupRole UseRow(Koto use)
    {
        if (IsAdaptationTarget(use))
        {
            return LookupRole.Core; // SPEC 13.5.1: a bare target compares Core with Type Semantics (ResolveAdaptationTarget).
        }

        var node = use;
        var grouped = false; // `(C)`: a ContractReference in parentheses (F.6), or with an Origin binding set a bound qualifier `(S{v})`.
        var bound = use is TypeSemanticsKoto { HasOrigin: true };
        while (node.Parent is ParenthesizedKoto or ParenthesizedTypeKoto or TypeSemanticsKoto { IsTransparentWrapper: true })
        {
            grouped |= node.Parent is ParenthesizedKoto or ParenthesizedTypeKoto;
            bound |= node.Parent is TypeSemanticsKoto { HasOrigin: true };
            node = node.Parent;
        }

        var requirement = node;
        while (requirement.Parent is UnaryKoto { Akind: KotoKind.Not } or BinaryKoto { Akind: KotoKind.And or KotoKind.Or } or ParenthesizedKoto)
        {
            requirement = requirement.Parent;
        }

        return node.Parent switch
        {
            ContractKoto => LookupRole.ContractReference, // A parent (F.6 ContractParentList, SPEC 8.4.3).
            MemberAccessKoto member when grouped && (ReferenceEquals(member.Right, node) || !bound)
                => LookupRole.ContractReference, // `.(C).Name` (8.5.2) and the selector `(C).Name` (8.5.1).
            MemberAccessKoto => LookupRole.Qualifier, // SPEC 9.6: a path's intermediate component, the selector `C.Name` too.
            EffectBoundKoto => grouped ? LookupRole.ContractReference : LookupRole.Qualifier, // `effect (A).r` (8.11.1), `effect A.r` (9.6).
            AliasKoto => LookupRole.Qualifier, // SPEC 18.2.1, 9.5.1.
            TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq }
                => LookupRole.ObjectViewTarget,
            _ => requirement.Parent switch
            {
                IsKoto { Parent: SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Operands: [IsKoto clause, SyntaxFormKoto, ..] } } conformance
                    when ReferenceEquals(clause, conformance) && ReferenceEquals(conformance.Right, requirement)
                    => LookupRole.ContractReference, // `Self is C when P` (8.8): the form whose second operand is P; P's clauses are requirements.
                IsKoto { IsRuntimeTest: false } clause when ReferenceEquals(clause.Left, requirement) || ReferenceEquals(clause.Right, requirement)
                    => LookupRole.Requirement, // SPEC 8.2.1: a subject and a requirement; a formation Type (`for T`) is a Type position.
                _ => LookupRole.Core,
            },
        };
    }

    // Publishes a use's lookup once: a later publication (a path's walk evaluating a published qualifier again) must be equal, and a
    // different end is a broken contract, as a different selection is (the Symbols entry's write, HirTables.WriteSymbol). Every
    // publication of a use is in UseRow's row (NamePath's and the walk's left), so the column stores no row.
    private static void PublishUse(Koto use, PathEnd end, BindingSymbol? symbol)
    {
        var published = HirTables.LookupOf(use);
        if (published != PathEnd.None && published != end)
        {
            throw new InvalidOperationException($"`{use}` was published as {published}, then as {end} (InternalInvariant; R7-N1e2-1R).");
        }

        if (end == PathEnd.Selected)
        {
            use.BoundSymbol = symbol;
        }

        HirTables.Of(use).Lookups[use.SyntaxId] = end;
    }

    // The node a use's lookup is published on: below the transparent wrappers and the parentheses that ResolvePath sees through.
    private static Koto UseNode(Koto syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner }:
                    syntax = inner;
                    break;
                case ParenthesizedTypeKoto parentheses:
                    syntax = parentheses.Type;
                    break;
                default:
                    return syntax;
            }
        }
    }

    // The symbol a Type name selects, published. Its qualifiers' Types are bound once, in the role of the form the name selects, by
    // BindContainerReference for a nested Type and by BindAssociatedProjection for a projection base (SPEC 15.4.2).
    private BindingSymbol? TypeName(Koto syntax, BindingScope scope, int arity = 0) => this.NamePath(syntax, scope, arity).Symbol;

    // A use's one lookup in its position's row (UseRow), with how it ended: resolved and published on the use (UseNode) at the first call,
    // read from the use at every later one, so BindNamedType forms the use's Type from it and no reader looks the use up again.
    private ResolvedPath NamePath(Koto syntax, BindingScope scope, int arity = 0)
    {
        var mark = this.nameSteps.Count;
        var path = this.EvaluateUse(syntax, scope, arity, out var early);
        return this.PublishLookup(syntax, path, mark, early, scope, arity);
    }

    // NamePath's evaluation: the use's lookup in its row, its steps above the caller's mark, unpublished; or the published one, read. A
    // member access's left takes it before the access selects the left's reading (SPEC 9.6, Member).
    private ResolvedPath EvaluateUse(Koto syntax, BindingScope scope, int arity, out bool early)
    {
        var use = UseNode(syntax);
        early = false;
        if (HirTables.LookupOf(use) is var published and not PathEnd.None)
        {
            return new(published, published == PathEnd.Selected ? use.BoundSymbol : null, use);
        }

        var reads = this.notReadyReads;
        var path = this.ResolvePath(use, scope, UseRow(use), arity);
        early = reads != this.notReadyReads;
        return path;
    }

    // NamePath's publication of an evaluated lookup (its steps above `mark`) on the use, once; a read one is returned as it is.
    private ResolvedPath PublishLookup(Koto syntax, in ResolvedPath path, int mark, bool early, BindingScope scope, int arity)
    {
        var use = UseNode(syntax);
        if (HirTables.LookupOf(use) != PathEnd.None)
        {
            return path;
        }

        if (early)
        {
            (this.earlyReads ??= new(ReferenceEqualityComparer.Instance))[use] = new(scope, UseRow(use), arity, path.Symbol, null);
        }

        var symbol = this.PublishPath(path, mark);
        PublishUse(use, path.End, symbol);
        return path with { Symbol = symbol };
    }

    // An unqualified value name (SPEC 9.5 in the Value namespace), for a form that selects now. A lookup that saw a base clause not
    // ready is an early read, noted as NamePath's (BaseLayers) with its name: a value use need not spell it (a capture's source is
    // looked up at its function).
    private BindingSymbol? Lookup(string name, BindingScope scope, Koto use)
    {
        var mark = this.nameSteps.Count;
        var reads = this.notReadyReads;
        var path = this.ResolveValue(name, scope, use);
        if (reads != this.notReadyReads)
        {
            (this.earlyReads ??= new(ReferenceEqualityComparer.Instance))[use] = new(scope, LookupRole.Value, 0, path.Symbol, name);
        }

        return this.PublishPath(path, mark);
    }

    // SPEC 13.5.1: each role fixes its own first eligible lookup stage before the two roles are compared.
    // Operand or target fitting never resolves an ambiguity or reopens either search.
    // The target's one lookup in its Type row, published on it, so the target's BindType reads it and never compares the roles again.
    private BindingSymbol? LookupAdaptationTarget(Koto syntax, BindingScope scope, out bool semantics)
    {
        var symbol = this.NamePath(syntax, scope).Symbol;
        semantics = symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        return symbol;
    }

    private BindingSymbol? PublishPath(in ResolvedPath path, int mark)
    {
        var targetFailed = false;
        for (var i = mark; i < this.nameSteps.Count; i++)
        {
            var step = this.nameSteps[i];
            switch (step.Kind)
            {
                case NameStepKind.Head:
                    this.gatheredGroups?.GetValueOrDefault(step.Use)?.Clear();
                    this.qualificationStops?.Remove((step.Use, !InValueNamespace(step.Role)));
                    if (step.Import.Environment is not null)
                    {
                        this.importedContainers[step.Use] = step.Import;
                    }

                    break;
                case NameStepKind.ImportGroup:
                    this.AddImportCandidates(step.Use, step.Qualifier!, step.Symbol!);
                    break;
                case NameStepKind.Qualifier:
                    PublishUse(step.Use, step.End, step.Symbol); // The state stays the binder's.
                    break;
                case NameStepKind.Member:
                    var member = (MemberAccessKoto)step.Use;
                    PublishQualifier(member.Left, step.Qualifier!);
                    member.Right.BoundSymbol = step.Symbol;
                    member.Right.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.InheritedMember:
                    var inherited = (MemberAccessKoto)step.Use;
                    inherited.Left.BoundSymbol = step.Qualifier;
                    inherited.Right.BoundSymbol = step.Symbol;
                    inherited.Right.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.RootMember:
                    var rooted = (MemberAccessKoto)step.Use;
                    PublishQualifier(rooted.Left, step.Qualifier!);
                    rooted.Right.BoundSymbol = step.Symbol;
                    rooted.Right.BindingState = step.Symbol is null ? BindingState.Unresolved : BindingState.Resolved;
                    if (step.Symbol is null)
                    {
                        rooted.BoundSymbol = null;
                        rooted.BindingState = BindingState.Unresolved;
                    }
                    else
                    {
                        PublishQualifier(rooted, step.Symbol); // The outer level's qualifier in a longer `::` path (SPEC 9.7.1 step 1).
                    }

                    break;
                case NameStepKind.RootOperand:
                    step.Use.BoundSymbol = step.Symbol;
                    step.Use.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.AmbiguousRole:
                    this.Fail(step.Use, BindingFailure.Ambiguous, unresolved: true);
                    break;
                case NameStepKind.Unsupported:
                    this.Fail(step.Use, BindingFailure.Unsupported, unresolved: true); // As the base's miss path fails the Name.
                    break;
                case NameStepKind.HiddenMember:
                    ((MemberAccessKoto)step.Use).BoundSymbol = step.Symbol;
                    break;
                case NameStepKind.ProjectionMember:
                    var projected = (MemberAccessKoto)step.Use;
                    projected.BoundSymbol = step.Symbol;
                    projected.Right.BoundSymbol = step.Symbol;
                    break;
                case NameStepKind.MemberAccess:
                    var access = (MemberAccessKoto)step.Use;
                    var side = step.Role == LookupRole.Value ? MemberSide.Value : MemberSide.Type;
                    if (this.memberSides.TryGetValue(access, out var publishedSide) && publishedSide != side)
                    {
                        throw new InvalidOperationException($"`{access}` was published as a {publishedSide} member, then as a {side} member (InternalInvariant; R7-N1c).");
                    }

                    if (step.Selection is { } selection)
                    {
                        this.memberSelections[access] = selection;
                    }

                    this.memberSides[access] = side;
                    if (step.Qualifier is not null)
                    {
                        PublishQualifier(access.Left, step.Qualifier);
                    }

                    access.BoundSymbol = step.Symbol;
                    access.Right.BoundSymbol = step.Symbol;
                    access.Right.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.AdaptationTarget:
                    // A target that already failed selects nothing (SPEC 13.5.1: fitting never reopens the search).
                    targetFailed = step.Use.BindingState == BindingState.Invalid;
                    if (!targetFailed)
                    {
                        step.Use.BoundSymbol = step.Symbol;
                    }

                    break;
                default:
                    throw new UnreachableException();
            }
        }

        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        switch (path.End)
        {
            case PathEnd.Selected:
                return targetFailed ? null : path.Symbol;
            case PathEnd.Ambiguous:
                this.Fail(path.Use, BindingFailure.Ambiguous, unresolved: true);
                return null;
            case PathEnd.QualificationStop:
                this.RecordQualificationStop(path.Use, path.Stop);
                return null;
            case PathEnd.Unresolved or PathEnd.Projection:
                return null;
            default:
                throw new UnreachableException();
        }
    }

    // A member access's selected member (SPEC 9.6), published by its MemberAccess step: `path` names the namespace of the path that selected
    // it (Value: the value path; Qualifier: the Type path, whose left `qualifier` is), `selection` its member lookup (none for a constructor).
    private BindingSymbol? PublishMemberAccess(MemberAccessKoto member, BindingSymbol selected, BindingSymbol? qualifier, LookupRole path, MemberSelection? selection)
    {
        var mark = this.nameSteps.Count;
        this.nameSteps.Add(new(NameStepKind.MemberAccess, member, selected, qualifier, default, path, default, selection));
        return this.PublishPath(new(PathEnd.Selected, selected, member), mark);
    }

    // A projection path's selection (SPEC 8.5.2, 9.6), published by its ProjectionMember step: the associated Type a projection names,
    // or the member Type of its normal form a Name after it names (x08). A Type position, so no side and no member lookup.
    private void PublishProjectionMember(MemberAccessKoto syntax, BindingSymbol selected)
    {
        var mark = this.nameSteps.Count;
        this.nameSteps.Add(new(NameStepKind.ProjectionMember, syntax, selected, null, default, default, default, null));
        this.PublishPath(new(PathEnd.Projection, null, syntax), mark);
    }

    // SPEC 9.4: a member access whose lookup selected nothing but met an inaccessible member: that member is its failure's evidence,
    // published by the HiddenMember step, and the access records InaccessibleBinding_Kd.
    private void PublishHiddenMember(MemberAccessKoto member, BindingSymbol hidden)
    {
        var mark = this.nameSteps.Count;
        this.nameSteps.Add(new(NameStepKind.HiddenMember, member, hidden, null, default, default, default, null));
        this.PublishPath(new(PathEnd.Unresolved, null, member), mark);
        this.Fail(member, BindingFailure.Access);
    }

    // An unqualified value Name completed (SPEC 9.5 in the Value namespace): judged (Supported) as Lookup reads it and ReadAgain reads it again.
    private ResolvedPath ResolveValue(string name, BindingScope scope, Koto use) => this.Supported(this.ResolveName(name, scope, use, LookupRole.Value, 0));

    // The evaluation alone of a spelling the program does not contain (a repair's check that a qualifier names its Container alone): what
    // lookup selects, unjudged, since no use is written (SPEC 22.1 judges uses).
    private BindingSymbol? ProbePath(string name, BindingScope scope, Koto use, LookupRole role) => this.EvaluateName(name, scope, use, role).Symbol;

    // A Name's lookup evaluated alone (SPEC 9.5), publishing nothing: a repair's probe, and a member access's left before the access
    // selects its reading (Member: the value reading's stage, stop or ambiguity).
    private ResolvedPath EvaluateName(string name, BindingScope scope, Koto use, LookupRole role)
    {
        var mark = this.nameSteps.Count;
        var path = this.ResolveName(name, scope, use, role, 0);
        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        return path;
    }

    private BindingSymbol? ProbePath(Koto syntax, BindingScope scope, LookupRole role, int arity)
    {
        var mark = this.nameSteps.Count;
        var path = this.ResolvePath(syntax, scope, role, arity);
        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        return path.Symbol;
    }

    // SPEC 9.6: the reading of a member access's left that selects its member, decided before either reading publishes. The left's
    // Type walk (its use's lookup in its row) and its value reading (LeftValue) are evaluated unpublished. A Name left, with Type
    // arguments or not, of both readings takes its sole successful path (PickReading). A member-access left takes the Type reading
    // unless its value reading may succeed: when its Type walk also selected, both readings succeed at the left and the form is
    // Unsupported (its value is its own access's selection, which publishes as it selects; ruling 13's Q1); when the Type walk failed,
    // the value reading. The Type reading's lookup is published here; a failed Type walk beside the value reading publishes its
    // outcome too (true facts the inner accesses read, so the chain is walked once), but for the head's own steps when the head is a
    // value; the value reading is published by its former (BindNode), its own stage's failure by Lookup (ValueFailure).
    private LeftReading ReadLeft(MemberAccessKoto member, string name, BindingScope scope, out ResolvedPath qualifierPath)
    {
        var mark = this.nameSteps.Count;
        qualifierPath = this.EvaluateUse(member.Left, scope, 0, out var early);
        var step = this.nameSteps.Count;
        var typed = qualifierPath.End == PathEnd.Selected ||
            (qualifierPath.End == PathEnd.Projection && UseNode(member.Left) is MemberAccessKoto projection && this.EvaluateProjection(projection, scope, mark, ref step) is not null);
        var value = this.LeftValue(member.Left, scope, mark, out var valuePath, out var walkFrom, out var walkReceiver);
        if (value == LeftValueKind.Possible && typed && walkFrom is not null)
        {
            // Q1 asks whether the value reading succeeds at the left only where the Type reading may (a walk per access, not per level).
            value = this.ValueWalk(walkFrom, walkReceiver, member.Left, scope);
        }

        var reading = value == LeftValueKind.None ? LeftReading.Type
            : member.Left is IdentifierNameKoto or GenericsKoto { Identifier: IdentifierNameKoto }
                ? typed || qualifierPath.End is PathEnd.Ambiguous or PathEnd.QualificationStop ? this.PickReading(member, qualifierPath, valuePath, name, scope)
                    : value == LeftValueKind.StageFailure ? LeftReading.ValueFailure : LeftReading.Value
            : typed ? (value == LeftValueKind.Possible ? LeftReading.Unsupported : this.PickReading(member, qualifierPath, valuePath, name, scope))
            : LeftReading.Value;
        if (reading == LeftReading.ValueFailure)
        {
            this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
            if (valuePath.Use is IdentifierNameKoto head)
            {
                this.Lookup(head.IdentifierName, scope, head); // Publishes the value path's stop or conflict, the access's record.
                if (member.Left is GenericsKoto applied)
                {
                    this.CompleteDependent(applied, head); // A left with Type arguments rests on its Name's failure (SPEC 23.1.2).
                }
            }

            return reading;
        }

        if (reading == LeftReading.Value && !this.PublishesFailedWalk(qualifierPath, mark, valuePath))
        {
            this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
            return reading;
        }

        qualifierPath = this.PublishLookup(member.Left, qualifierPath, mark, early, scope, 0);
        return reading;
    }

    // Whether a Type walk the value reading replaces publishes its outcome (SPEC 9.6: a failed path's failure is no error): only an
    // Unresolved walk with no step that records, and then without the head's own steps when the head is a value (its reading is the
    // value one); the rest are facts no reading contradicts.
    private bool PublishesFailedWalk(in ResolvedPath qualifierPath, int mark, in ResolvedPath valuePath)
    {
        if (qualifierPath.End != PathEnd.Unresolved)
        {
            return false;
        }

        for (var i = this.nameSteps.Count - 1; i >= mark; i--)
        {
            if (this.nameSteps[i].Kind is NameStepKind.Unsupported or NameStepKind.AmbiguousRole)
            {
                return false;
            }

            if (valuePath.End != PathEnd.Unresolved && ReferenceEquals(this.nameSteps[i].Use, valuePath.Use))
            {
                this.nameSteps.RemoveAt(i);
            }
        }

        return true;
    }

    // SPEC 9.6: a Name left of both readings: each namespace's path, evaluated without publishing; the sole successful one is selected,
    // and paths to one Symbol that the value access uses with no receiver count once (the Type reading). A path fails at its own stage
    // (a 9.5 stop or a conflict), at a missing, inaccessible or ambiguous member, and the Type path at a member that needs a receiver and
    // is no function (its static use; a Type-qualified instance function takes its receiver as an argument, 9.6.1). The Type path's
    // member is looked up over the qualifier declaration's own Type (arguments never change the selection), the value path's over the
    // value's declared Type (QueryValueType); a value named with Type arguments is a function, whose value has no members, but its
    // lookup's own stage failure is its path's (B3). A member-access left comes here when its value reading did not succeed. When no
    // path succeeds, a path that failed at its own stage keeps its record (the value reading's when only it did), else the Type path's.
    private LeftReading PickReading(MemberAccessKoto member, in ResolvedPath qualifierPath, in ResolvedPath valuePath, string name, BindingScope scope)
    {
        var valueMember = this.ValuePathMember(member, valuePath, name, scope);
        var typeMember = qualifierPath.End is PathEnd.Selected or PathEnd.Projection && qualifierPath.Symbol is { } qualifier
            ? this.TypePathMember(member, qualifier, this.OwnType(qualifier), name, scope) : null;

        if (typeMember is not null && valueMember is not null)
        {
            return ReferenceEquals(typeMember, valueMember) && !IsInstanceMember(valueMember) ? LeftReading.Type : LeftReading.Ambiguous;
        }

        return typeMember is not null ? LeftReading.Type : valueMember is not null ? LeftReading.Value
            : valuePath.End is PathEnd.Ambiguous or PathEnd.QualificationStop && qualifierPath.End is not (PathEnd.Ambiguous or PathEnd.QualificationStop)
            ? LeftReading.ValueFailure : LeftReading.Type;
    }

    // SPEC 9.6: the value path's verdict at the first member step `step` of a Name left, whose value lookup is `headPath`: none when the
    // head gives no receiver (a value named with Type arguments is a function, whose value has no members; a function; a value whose
    // Type is not known), else ValueStepMember over the value's declared Type (QueryValueType). Read by PickReading for a Name left's
    // member and by LeftValue for a member-access left's first step.
    private BindingSymbol? ValuePathMember(MemberAccessKoto step, in ResolvedPath headPath, string name, BindingScope scope)
        => this.HeadReceiver(step, headPath) is { } receiver ? this.ValueStepMember(step, receiver, name, scope, out _) : null;

    private BoundType? HeadReceiver(MemberAccessKoto step, in ResolvedPath headPath)
        => step.Left is IdentifierNameKoto && headPath is { End: PathEnd.Selected, Symbol: { Kind: not BindingSymbolKind.Function } value } ? this.DeclaredValueType(value) : null;

    // SPEC 9.6, 7.3.4: the one verdict of the value path at a member step over a receiver Type: the member (ReceiverMember), or none where
    // the path fails: an ambiguous, missing or inaccessible member, or a function member at a step that is not called (no bound method,
    // 7.3.4). `valueType` is the member's value Type, its declared Type in its declaring layer, when known before publication.
    private BindingSymbol? ValueStepMember(MemberAccessKoto step, BoundType receiver, string name, BindingScope scope, out BoundType? valueType)
    {
        valueType = null;
        var side = this.ReceiverMember(step, name, scope, receiver, out _);
        if (side.Selection.Ambiguous || side.Member is not { } found || (found.Kind == BindingSymbolKind.Function && !IsCallee(step)))
        {
            return null;
        }

        if (found.Kind != BindingSymbolKind.Function && this.DeclaredValueType(found) is { } declared)
        {
            valueType = this.PartType(declared, side.Selection.DeclaringType ?? receiver);
        }

        return found;
    }

    // The value path from `step` outward to the left `left`, `receiver` the value Type of `step`'s left: each step's verdict over the
    // previous member's value Type (ValueStepMember). StepFailure where a step fails; Possible when every step succeeds, or from a step
    // whose member's value Type is not known before publication (its access's value Type, the expression binder's: Q1's remainder).
    private LeftValueKind ValueWalk(MemberAccessKoto step, BoundType? receiver, Koto left, BindingScope scope)
    {
        while (receiver is not null && step.Right is IdentifierNameKoto right)
        {
            if (this.ValueStepMember(step, receiver, right.IdentifierName, scope, out var next) is null)
            {
                return LeftValueKind.StepFailure;
            }

            if (ReferenceEquals(step, left) || step.Parent is not MemberAccessKoto outer || !ReferenceEquals(outer.Left, step))
            {
                break;
            }

            step = outer;
            receiver = next;
        }

        return LeftValueKind.Possible;
    }

    // SPEC 9.6, 9.6.1: the Type path's member at a member step over `type` (a qualifier declaration's own Type, or a formed projection's,
    // `qualifier` then none): QualifierSelection's member where TypePathFailure finds none, else none (the path fails). Read by
    // PickReading and by LeftValue (a step naming a static value of its prefix's Type).
    private BindingSymbol? TypePathMember(MemberAccessKoto step, BindingSymbol? qualifier, BoundType? type, string name, BindingScope scope)
    {
        var side = this.QualifierSelection(step, qualifier, name, scope, type);
        return this.TypePathFailure(side, scope) == BindingFailure.None ? side.Member : null;
    }

    // A qualifier declaration's own Type, whose members lookup reads (arguments and Origins never change a selection): a Type's
    // SelfType, a group's nominal Type; none for another qualifier.
    private BoundType? OwnType(BindingSymbol qualifier) => qualifier.Type is not null ? this.SelfType(qualifier)
        : qualifier.Declaration is GroupKoto { IsRoot: false } ? this.types.Intern(BoundTypeKind.Nominal, qualifier, SemanticsKind.Owner, []) : null;

    // A member access's left's value reading (SPEC 9.6), evaluated unpublished: None when it has none (a Type head; a head with no value;
    // a projection step with no static value outward of it); StageFailure when the head's value lookup stopped or conflicted at its
    // stage; StepFailure when the head gives no receiver (Type arguments, a function, an unknown Type); Possible otherwise, with where
    // its value path starts (`walkFrom`, `walkReceiver`: a value head's first step, or the step after a static value of the Type a prefix
    // selected or a prefix projection's normal form), which ReadLeft walks (ValueWalk) only where the Type reading may succeed. A
    // prefix's selected Type is read from the left's Type walk above `mark` (its Qualifier steps) or its published lookup: one pass.
    private LeftValueKind LeftValue(Koto left, BindingScope scope, int mark, out ResolvedPath headPath, out MemberAccessKoto? walkFrom, out BoundType? walkReceiver)
    {
        headPath = default;
        walkFrom = null;
        walkReceiver = null;
        var step = this.nameSteps.Count;
        MemberAccessKoto? first = null;
        var node = left;
        while (true)
        {
            if (node is GenericsKoto generic)
            {
                node = generic.Identifier!;
            }

            if (node is not MemberAccessKoto member)
            {
                break;
            }

            if (member.Right is ParenthesizedTypeKoto)
            {
                return LeftValueKind.None;
            }

            // A step naming a static value (no function: a function value has no members) of the Type its prefix selected, or of a
            // prefix projection's normal form, starts a value path there (6.5.2, 8.5.2: a Case through a projection qualifier).
            var prefix = UseNode(member.Left);
            BindingSymbol? qualifier;
            var end = HirTables.LookupOf(prefix);
            if (end == PathEnd.None)
            {
                end = this.WalkEnd(prefix, mark, ref step, out qualifier);
            }
            else
            {
                qualifier = end == PathEnd.Selected ? prefix.BoundSymbol : null;
            }

            var type = qualifier is not null ? this.OwnType(qualifier)
                : end == PathEnd.Projection && prefix is MemberAccessKoto { Right: not ParenthesizedTypeKoto } projection ? this.EvaluateProjection(projection, scope, mark, ref step) : null;
            if ((qualifier is not null || type is not null) && member.Right is IdentifierNameKoto right &&
                this.TypePathMember(member, qualifier, type, right.IdentifierName, scope) is { Kind: not BindingSymbolKind.Function } start)
            {
                if (ReferenceEquals(member, left) || member.Parent is not MemberAccessKoto outer || !ReferenceEquals(outer.Left, member))
                {
                    return LeftValueKind.Possible;
                }

                walkFrom = outer;
                walkReceiver = this.DeclaredValueType(start) is { } declared && (type ?? this.OwnType(qualifier!)) is { } owner ? this.PartType(declared, owner) : null;
                return LeftValueKind.Possible;
            }

            first = member;
            node = member.Left;
        }

        if (node is not IdentifierNameKoto name)
        {
            return node is TypeKoto or SyntaxFormKoto { Akind: KotoKind.RootName } ? LeftValueKind.None : LeftValueKind.Possible;
        }

        headPath = this.EvaluateName(name.IdentifierName, scope, node, LookupRole.Value);
        if (headPath.End != PathEnd.Selected)
        {
            return headPath.End == PathEnd.Unresolved ? LeftValueKind.None : LeftValueKind.StageFailure;
        }

        if (first is null)
        {
            return LeftValueKind.Possible;
        }

        walkFrom = first;
        walkReceiver = this.HeadReceiver(first, headPath);
        return walkReceiver is null ? LeftValueKind.StepFailure : LeftValueKind.Possible;
    }

    // SPEC 8.5.2, 9.6: the Type a projection path names, evaluated without forming it (BindAssociatedProjection, its former, forms it
    // once the reading is decided): over the receiver's Type (its walk's selection's own Type, or a projection receiver's, evaluated), the
    // written Contract's declaration, the associated search alone (SearchAssociated, recording nothing) normalized; else a member Type of
    // a projection receiver's normal form (x08). None where that does not decide it: no or several matches, an associated family with
    // Origin parameters, a receiver no walk selected. The search, the proof and the normalization read what the former's scope holds there
    // (SPEC 8.3.2): the use's scope with the premises each proved projection's Contract supplies (ProjectionEvidence, AddContractPremises),
    // added to the use scope's kept layer (BindingScope.EvaluatedPremises), an inner projection's first, so the use's scope is not written
    // before the reading is decided and a warm pass forms no layer. The normal form is read in its class's form in that view (ClassForm,
    // the one identity judge: SPEC 8.2.3), never by a first identity premise.
    private BoundType? EvaluateProjection(MemberAccessKoto projection, BindingScope scope, int mark, ref int step)
    {
        if (TypeSpelling(projection.Right) is not { } name)
        {
            return null;
        }

        var receiver = projection.Left;
        BindingSymbol? contract = null;
        if (receiver is MemberAccessKoto { Right: ParenthesizedTypeKoto } qualified)
        {
            contract = this.ProjectionQualifier(qualified, scope); // Every reading forms the written reference alike (ruling 24).
            if (contract is not { IsContract: true })
            {
                return null;
            }

            receiver = qualified.Left;
        }

        var prefix = UseNode(receiver);
        BindingSymbol? selected;
        var end = HirTables.LookupOf(prefix);
        if (end == PathEnd.None)
        {
            end = this.WalkEnd(prefix, mark, ref step, out selected);
        }
        else
        {
            selected = end == PathEnd.Selected ? prefix.BoundSymbol : null;
        }

        var type = selected is not null ? (selected.Kind == BindingSymbolKind.TypeParameter ? selected.Type : this.OwnType(selected))
            : end == PathEnd.Projection && prefix is MemberAccessKoto { Right: not ParenthesizedTypeKoto } inner ? this.EvaluateProjection(inner, scope, mark, ref step) : null;
        if (type is null)
        {
            return null;
        }

        var environment = scope.EvaluatedPremises;
        var search = this.SearchAssociated(type, environment, name, contract);
        if (search is { Found: { } associated, Alternative: null })
        {
            if (this.UnappliedFamily(associated.Declaration, false))
            {
                return null;
            }

            var evidence = ProjectionEvidence(associated, search.Reference, contract);
            if (this.SuppliesPremises(type, evidence, environment))
            {
                this.AddContractPremises(evidence.Contract!, type, environment);
            }

            return this.ClassForm(this.NormalizedProjection(this.ProjectionIdentity(type, associated, search.Reference, contract), environment, false), environment, out _);
        }

        return end == PathEnd.Projection && contract is null && this.LookupMember(type, name, scope, LookupRole.Qualifier).Member is { } nested ? this.OwnType(nested) : null;
    }

    // How the Type walk above `mark` ended for a path's left `prefix` and what it selected (its Qualifier step), searched below `step`,
    // which moves to it: a chain's prefixes are met outermost first and their steps stand innermost first, so the search is one pass.
    private PathEnd WalkEnd(Koto prefix, int mark, ref int step, out BindingSymbol? selected)
    {
        selected = null;
        for (var i = step - 1; i >= mark; i--)
        {
            if (this.nameSteps[i] is { Kind: NameStepKind.Qualifier } found && ReferenceEquals(found.Use, prefix))
            {
                step = i;
                selected = found.End == PathEnd.Selected ? found.Symbol : null;
                return found.End;
            }
        }

        return PathEnd.None;
    }

    // ResultSurvey's question before a call binds: whether a member access's left reads as a value, publishing nothing.
    private bool MayBeValueQualifier(Koto left, BindingScope scope)
    {
        var mark = this.nameSteps.Count;
        this.EvaluateUse(left, scope, 0, out _);
        var value = this.LeftValue(left, scope, mark, out _, out _, out _) != LeftValueKind.None;
        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        return value;
    }

    // An early read (BaseLayers) evaluated again as it was read, judged as it was and publishing nothing: a value name by its name (Lookup's
    // ResolveValue), a Type name by its syntax (NamePath's ResolvePath).
    private BindingSymbol? ReadAgain(Koto use, in EarlyRead read)
    {
        var mark = this.nameSteps.Count;
        var path = read.Name is { } name ? this.ResolveValue(name, read.Scope, use) : this.ResolvePath(use, read.Scope, read.Role, read.Arity);
        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        return path.Symbol;
    }

    // SPEC 6.1.5, 9.1, 9.3: what a written Self selects, decided here for every reader. The first enclosing Container that is no group
    // is its context: a Contract's Self is the Contract's Self Type parameter (the conforming-Type binder), never the Contract; a
    // struct's or enum's is the declaration, which Self names with all its bindings.
    private BindingSymbol? EnclosingSelf(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is DeclarationContainerKoto and not GroupKoto)
            {
                return current.Owner.BoundSymbol is { Declaration: ContractKoto } contract ? this.ContractSelfType(contract).Symbol : current.Owner.BoundSymbol;
            }
        }

        return null;
    }

    // A Type path's component, completed: judged (Supported) before ResolveMember hands it on as a qualifier or the path ends with it.
    private ResolvedPath ResolvePath(Koto syntax, BindingScope scope, LookupRole role, int arity = 0)
        => this.Supported(this.EvaluatePath(syntax, scope, role, arity));

    private ResolvedPath EvaluatePath(Koto syntax, BindingScope scope, LookupRole role, int arity)
    {
        while (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            syntax = inner;
        }

        switch (syntax)
        {
            case GenericsKoto generic:
                return this.ResolvePath(generic.Identifier!, scope, role, generic.TypeArguments.Count);
            case ParenthesizedTypeKoto parentheses:
                return this.ResolvePath(parentheses.Type, scope, role, arity);
            case MemberAccessKoto member:
                var left = UseNode(member.Left);
                var qualifier = this.ResolvePath(member.Left, scope, UseRow(left)); // The left's row is its position's, as NamePath asks it.
                this.nameSteps.Add(new(NameStepKind.Qualifier, left, qualifier.Symbol, null, default, default, qualifier.End, null));
                return this.ResolveMember(member, qualifier, scope, role, arity);
            case SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root:
                var rooted = this.ResolveRootPath(root.Operands[0], this.ModuleScope(root.Operands[0]), role, arity);
                if (rooted.End == PathEnd.Selected)
                {
                    this.nameSteps.Add(new(NameStepKind.RootOperand, root.Operands[0], rooted.Symbol, null, default, default, default, null));
                }

                return rooted;
        }

        if (TypeSpelling(syntax) is not { } name)
        {
            return new(PathEnd.Unresolved, null, syntax);
        }

        if (IsAdaptationTarget(syntax))
        {
            return this.ResolveAdaptationTarget(syntax, scope);
        }

        if (name == "Self" && this.EnclosingSelf(scope) is { } self)
        {
            // SPEC 9.7: Self has no Type parameters, so no candidate remains for a Self written with Type arguments.
            return arity > 0 ? new(PathEnd.Unresolved, null, syntax) : new(PathEnd.Selected, self, syntax);
        }

        return this.ResolveName(name, scope, syntax, role, arity);
    }

    // The one form with more than one lookup above a mark: both roles' steps replay, and a role's ambiguity stands beside the other's selection.
    private ResolvedPath ResolveAdaptationTarget(Koto syntax, BindingScope scope)
    {
        if (TypeSpelling(syntax) is not { } name)
        {
            return new(PathEnd.Unresolved, null, syntax);
        }

        if (name == "Self")
        {
            return this.EnclosingSelf(scope) is { } self ? new(PathEnd.Selected, self, syntax) : new(PathEnd.Unresolved, null, syntax);
        }

        var target = this.ResolveName(name, scope, syntax, LookupRole.Core, 0);
        var parameter = this.ResolveName(name, scope, syntax, LookupRole.TypeSemantics, 0);
        if (target.End == PathEnd.Ambiguous || parameter.End == PathEnd.Ambiguous)
        {
            this.nameSteps.Add(new(NameStepKind.AmbiguousRole, syntax, null, null, default, default, default, null));
        }

        if (target.Symbol is not null && parameter.Symbol is not null)
        {
            return new(PathEnd.Ambiguous, null, syntax);
        }

        var selected = parameter.Symbol is not null ? parameter : target;
        this.nameSteps.Add(new(NameStepKind.AdaptationTarget, syntax, selected.Symbol, null, default, default, default, null));
        return selected;
    }

    // SPEC 9.5: stages 1-3 the scope chain with the inherited-layer stop, 4 a Type qualifier's reference names, 5-6 the imports (not
    // while an alias target resolves). Lookup stops at the first stage with an eligible declaration.
    private ResolvedPath ResolveName(string name, BindingScope scope, Koto use, LookupRole role, int arity)
    {
        var step = this.nameSteps.Count;
        this.nameSteps.Add(new(NameStepKind.Head, use, null, null, default, role, default, null));
        for (var current = scope; current is not null; current = current.Parent)
        {
            var stage = InValueNamespace(role) ? this.ValueStage(current, scope, use, name) : this.TypeStage(current, scope, use, name, role, arity, step);
            if (stage.End != PathEnd.Unresolved)
            {
                return stage;
            }

            if (this.InheritedStop(current, scope, use, name, role, arity) is { } stop)
            {
                return new(PathEnd.QualificationStop, null, use, stop);
            }
        }

        // Stage 4, for Type qualifiers only (SPEC 9.5).
        if (role == LookupRole.Qualifier && this.ModuleReference(use, name) is { } reference)
        {
            return new(PathEnd.Selected, reference, use);
        }

        if (this.aliasResolutionDepth != 0)
        {
            return new(PathEnd.Unresolved, null, use);
        }

        return InValueNamespace(role) ? this.ResolveImportedValue(name, scope, use, step) : this.ResolveImportedType(name, scope, use, role, arity, step);
    }

    private ResolvedPath TypeStage(BindingScope current, BindingScope scope, Koto use, string name, LookupRole role, int arity, int step)
    {
        var candidates = default(TypeCandidates);
        this.AddTypeCandidates(ref candidates, current.Types.GetValueOrDefault(name), scope, role, arity);
        return candidates.First is not null ? this.SelectedPath(candidates, use, step) : new(PathEnd.Unresolved, null, use);
    }

    // SPEC 9.3, 9.5: a stage's first visible, accessible value; a Container stage whose candidates are all Cases or members needing a
    // receiver (none HasUnqualifiedCandidate) stops the lookup (qualification required).
    private ResolvedPath ValueStage(BindingScope current, BindingScope scope, Koto use, string name)
    {
        var head = current.Values.GetValueOrDefault(name);
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if ((candidate.Kind == BindingSymbolKind.Local && candidate.Declaration.Span.End > use.Span.Start) ||
                !this.ParameterVisible(candidate) || !this.Accessible(candidate, scope))
            {
                continue;
            }

            if (current.Owner is DeclarationContainerKoto && !this.HasUnqualifiedCandidate(head!, scope))
            {
                return new(PathEnd.QualificationStop, null, use, new(current, scope, candidate, name, LookupRole.Value, 0));
            }

            return new(PathEnd.Selected, candidate, use);
        }

        return new(PathEnd.Unresolved, null, use);
    }

    // An alias target's segment and a default alias's segment: one unfiltered selection with its ambiguity reported (part e folds both walks).
    private BindingSymbol? SelectQualifier(BindingSymbol? head, BindingScope scope, Koto use)
    {
        var candidates = default(TypeCandidates);
        this.AddTypeCandidates(ref candidates, head, scope, LookupRole.Qualifier, 0);
        if (candidates.Ambiguous)
        {
            this.Fail(use, BindingFailure.Ambiguous, unresolved: true);
            return null;
        }

        return this.Supported(use, candidates.Match ?? candidates.First);
    }

    private ResolvedPath SelectedPath(in TypeCandidates candidates, Koto use, int step)
    {
        if (candidates.Ambiguous)
        {
            return new(PathEnd.Ambiguous, null, use);
        }

        // Retain a mismatching declaration for the existing formation/arity diagnostic.
        // A populated nearer stage must never fall through to an outer or default stage.
        var symbol = candidates.Match ?? candidates.First;
        if (candidates.Import.Environment is not null)
        {
            this.nameSteps[step] = this.nameSteps[step] with { Import = candidates.Import };
        }

        return new(PathEnd.Selected, symbol, use);
    }

    // SPEC 9.1, 9.6: the later components search the selected target's members; a qualified Type name never selects a generic parameter;
    // the declaration's own Type selects an inherited member, so a qualifier's arguments and Origins never change the selection.
    private ResolvedPath ResolveMember(MemberAccessKoto member, in ResolvedPath qualifier, BindingScope scope, LookupRole role, int arity)
    {
        this.nameWalks++;
        // SPEC 8.5.2, F.5: `L.(C)` names a projection whatever Type its left is, by the left walk's end: a projection, a selected Core (a
        // parameter, a pair target and `Self` included) or a Type the walk names no symbol of (a built-in, a tuple, an array, or a missing
        // name: Unresolved), which the former binds by BindType with its own records. A left that ended with its own record (Ambiguous,
        // QualificationStop) keeps that end; a selected non-Core (a group, a Contract) is no projection base.
        if (member.Right is ParenthesizedTypeKoto)
        {
            return qualifier.End switch
            {
                PathEnd.Projection or PathEnd.Unresolved => new(PathEnd.Projection, null, member),
                PathEnd.Selected => new(IsCore(qualifier.Symbol!) ? PathEnd.Projection : PathEnd.Unresolved, null, member),
                _ => qualifier,
            };
        }

        if (qualifier.End == PathEnd.Projection)
        {
            return new(PathEnd.Projection, null, member); // SPEC 8.5.2: a projection's own associated Type, `T.(C).X`.
        }

        if (qualifier.End != PathEnd.Selected)
        {
            return qualifier;
        }

        var left = qualifier.Symbol!;
        if (TypeSpelling(member.Right) is not { } name)
        {
            return new(PathEnd.Unresolved, null, member);
        }

        var candidates = default(TypeCandidates);
        if (this.scopes.TryGetValue(left.Declaration, out var members))
        {
            this.AddTypeCandidates(ref candidates, MemberTypes(members, name), scope, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            if ((candidates.Match ?? candidates.First) is { } target)
            {
                this.nameSteps.Add(new(NameStepKind.Member, member, target, left, default, default, default, null));
                return new(PathEnd.Selected, target, member);
            }
        }

        if (left.Declaration is StructKoto && this.LookupMember(this.SelfType(left), name, scope, role) is { Member: { } inherited })
        {
            this.nameSteps.Add(new(NameStepKind.InheritedMember, member, inherited, left, default, default, default, null));
            candidates = default;
            this.AddTypeCandidates(ref candidates, inherited, scope, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            var selected = candidates.Match ?? candidates.First;
            return new(selected is null ? PathEnd.Unresolved : PathEnd.Selected, selected, member);
        }

        // SPEC 8.5.2, 9.6: a Name the Core declares no member Type of is a projection unless the Core is a declared struct or enum whose
        // conformances (declarations, once registered) name no associated Type of it: then it stays the missing member Type it is. Before
        // they register every one may (G621's early read); a parameter's, a pair target's or `Self`'s candidates are premises, completed
        // later (an override's selection), so its Name stays a projection. The former decides with evidence.
        var associable = IsCore(left) && (left.Declaration is not (StructKoto or EnumKoto) || left.Kind != BindingSymbolKind.Type || !this.impls.Reached(ImplTable.AdmissionPoint.SelfClauses) ||
            (left.Type is { } leftType && this.SearchAssociated(leftType, scope, name, null).Found is not null));
        return new(associable ? PathEnd.Projection : PathEnd.Unresolved, null, member);
    }

    // SPEC 9.6: `::A.B` starts at the Compilation root of the use's Kotonoha, ignoring locals and aliases. A component, completed: judged
    // (Supported) before the next `::` step hands it on as its qualifier or the path ends with it (RootOperand).
    private ResolvedPath ResolveRootPath(Koto syntax, BindingScope root, LookupRole role, int arity = 0)
        => this.Supported(this.EvaluateRootPath(syntax, root, role, arity));

    private ResolvedPath EvaluateRootPath(Koto syntax, BindingScope root, LookupRole role, int arity)
    {
        syntax = UnwrapTypeSyntax(syntax);
        if (syntax is GenericsKoto generic)
        {
            return this.ResolveRootPath(generic.Identifier!, root, role, generic.TypeArguments.Count);
        }

        if (TypeSpelling(syntax) == "Kimi")
        {
            return new(PathEnd.Selected, this.Library.Module, syntax);
        }

        var candidates = default(TypeCandidates);
        if (syntax is MemberAccessKoto member && this.ResolveRootPath(member.Left, root, UseRow(UseNode(member.Left))) is { Symbol: { } qualifier } &&
            this.scopes.TryGetValue(qualifier.Declaration, out var members) && TypeSpelling(member.Right) is { } rightName)
        {
            this.AddTypeCandidates(ref candidates, MemberTypes(members, rightName), root, role, arity);
            var selected = candidates.Ambiguous ? null : candidates.Match ?? candidates.First;
            this.nameSteps.Add(new(NameStepKind.RootMember, member, selected, qualifier, default, default, default, null));
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            return new(selected is null ? PathEnd.Unresolved : PathEnd.Selected, selected, member);
        }

        if (TypeSpelling(syntax) is { } name)
        {
            this.AddTypeCandidates(ref candidates, root.Types.GetValueOrDefault(name), root, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, syntax);
            }

            var target = candidates.Match ?? candidates.First ?? this.ModuleReference(syntax, name);
            var eligible = target is not null && HasLookupRole(target, role) && this.Accessible(target, root);
            return new(eligible ? PathEnd.Selected : PathEnd.Unresolved, eligible ? target : null, syntax);
        }

        return new(PathEnd.Unresolved, null, syntax);
    }
}
