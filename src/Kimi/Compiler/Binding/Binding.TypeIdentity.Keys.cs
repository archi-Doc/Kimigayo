// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kimi.Compiler;

// Binding.TypeIdentity's class keys and forms (the entry file states the component's contract; R7-C2c2 c2-1, c2-4b): ClassKey, a
// Type's key in a view's identity table, which only ContractSolver's premise supply reads; Order, the total structural order whose
// least parameter or projection is a class's key (IdentityClasses.Representative); MemberOf, an atom judge's subject; and ClassForm,
// a Type's one form in a view (its contract below). The table keeps each key and form until its next Open.
public sealed partial class Binding
{
    // R7-C2c2: a total structural order on Types that never reads premise order (ClassKey's least member): projection depth, kind,
    // the symbol (its declaration's span, then its place among every source, CompareNodes, then name and kind), Semantics,
    // components, Origins (CompareOrigins), length, and last the canonical name, which tells apart symbol-less Types (primitives).
    // Allocation-free: enums compare as integers (Enum.CompareTo(object) boxes).
    private static int Order(BoundType x, BoundType y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        var c = Depth(x).CompareTo(Depth(y));
        c = c != 0 ? c : ((int)x.Kind).CompareTo((int)y.Kind);
        c = c != 0 ? c : SymbolOrder(x.Symbol, y.Symbol);
        c = c != 0 ? c : ((int)x.Semantics).CompareTo((int)y.Semantics);
        c = c != 0 ? c : x.Components.Count.CompareTo(y.Components.Count);
        for (var i = 0; c == 0 && i < x.Components.Count; i++)
        {
            c = Order(x.Components[i], y.Components[i]);
        }

        c = c != 0 ? c : x.OriginArguments.Count.CompareTo(y.OriginArguments.Count);
        for (var i = 0; c == 0 && i < x.OriginArguments.Count; i++)
        {
            c = CompareOrigins(x.OriginArguments[i], y.OriginArguments[i]);
        }

        c = c != 0 ? c : OriginOrder(x.Origin, y.Origin); // a raw Origin is no class member's: members are parameters and projections
        c = c != 0 ? c : x.Length.CompareTo(y.Length);
        return c != 0 ? c : string.CompareOrdinal(x.Name, y.Name);

        static int Depth(BoundType type) => type.Kind == BoundTypeKind.AssociatedProjection ? 1 + Depth(type.Components[0]) : 0;
        static int OriginOrder(BoundOrigin? a, BoundOrigin? b) => a is null || b is null ? (b is null).CompareTo(a is null) : CompareOrigins(a, b);
    }

    private static int SymbolOrder(BindingSymbol? a, BindingSymbol? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null || b is null)
        {
            return a is null ? -1 : 1;
        }

        var c = a.Declaration.Span.Start.CompareTo(b.Declaration.Span.Start);
        c = c != 0 ? c : a.Declaration.Span.Length.CompareTo(b.Declaration.Span.Length);
        c = c != 0 || ReferenceEquals(a.Declaration, b.Declaration) ? c : CompareNodes(a.Declaration, b.Declaration); // another source
        c = c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        return c != 0 ? c : ((int)a.Kind).CompareTo((int)b.Kind);
    }

    // R7-C2c2: the key of `type` in a view's classes: its class's Representative (c2-4: a constructed member's too, so every member's
    // premise rows are the class's, SPEC 8.2.3 "a Contract fact about a Type is evidence for an identical one, also where that Type is
    // constructed"), else the Type over its components' keys (congruence), whose class is asked again. A lent row meeting the member's
    // closed world is contradictory evidence (ProveConformance's polarity join, SPEC 8.3.2), never a Proven over a Refuted. A
    // key names the class to the premise supply only; no reader sees it as a Type. Each
    // Type is keyed once per table (the table keeps it), so a request's keys cost the distinct Types it compares; a warm pass finds
    // the rebuilt Types interned and the table's capacity kept, and allocates nothing.
    private BoundType ClassKey(BoundType type, IdentityClasses classes)
    {
        if (classes.KeyOf(type) is not { } key)
        {
            this.IdentityKeyWork++;
            key = classes.Representative(type) ?? this.CongruentKey(classes.Applied(type), classes); // R7-C3: a family application keys as its instance
            classes.SetKey(type, key);
        }

        return key;
    }

    // R7-C2c2 c2-4 (design R1.1): the subject an atom judge takes: the Type's identity class's Member in a view with an identity
    // premise, else the Type, with the table's Completion (Complete without an identity premise; a re-entered build, which answers no
    // table, Provisional). A Member whose operand contexts are unresolved (a concrete prerequisite still pending) stands for nothing
    // yet: the Type itself is judged, Unknown as before (UnresolvedCapabilityProofBindingTest's pending `Target`). ContractTypeCore's
    // (E) arm meets the Completion (W4); the judges (ProveConformance, RequestCapability, JudgeConstraintAtom's other arms) read it
    // unthreaded, as PremiseRefines' (ContractSolver's header), until R7-C2e2 carries Completion through the judgments' answers.
    private BoundType MemberOf(BoundType type, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        if (!HasTypeIdentity(scope))
        {
            return type;
        }

        if (this.ClassesOf(scope, out completion) is not { } classes)
        {
            return type;
        }

        if (classes.Member(type) is { } member && !this.OperandContexts(member).Unresolved)
        {
            return member;
        }

        this.ReadIdentityPremiseError(scope, type); // R7-C3 (F's C2 ruling 276 (4)): a Type of an erroneous class is a failed input
        return type;
    }

    // ClassForm (R7-C2c2 c2-4b; design R4.12, R4.12b, R4.12c with F's ruling 211; SPEC 8.2.3: "all Type checking in the scope treats
    // identical Types as one Type"; an abstract Type "takes the form its binding ... fixes").
    // - Responsibility: the one form every Type of an identity class takes in the view `scope` (design R3.3; R4.1's termination; R4.10's
    //   Representative): a Type of a valid class forms as its class, its Member rebuilt over its components' forms, else its
    //   Representative as written; a Type of an erroneous or incomplete class stays as written (its premise error is the declaration's);
    //   a Type of no class, and a projection of a constructed subject (OfConstructed), is rebuilt over its parts' forms (FormParts).
    // - Inputs: a Type and the scope of the judgment that reads the form, never a producer's.
    // - Outputs: the form and its Completion (ContractSolver's header; W7 replaced W2 with SubstituteIdentity): Complete without an
    //   identity premise in the view, else the table's (W7); a re-entered build (no table) answers the Type itself, Provisional, and
    //   keeps nothing.
    // - Readers (closed; ArchitectureRulesTest "Readers of a class's form"): the one intake of each judgment SPEC states over identical
    //   Types. J1, a proposition's identity (8.3.1): NormalizeProofConstraint (subject, required Type, Contract reference) and the
    //   Contract judge's entry ProveConformance (subject after MemberOf, Contract reference). J3, the Type relation at a use: the relation
    //   walk's root with a use (RelationWalk.Fits), CallableContractType, and a failed fit's record (RecordMismatch: its structure
    //   gate and Origin search read the pair the walk compared, F's C2 ruling 307 (2)). J4, a slot's bindings agree (10.8 Solving): Infer's
    //   bound-slot branch, in SlotFrame.View. (ii), a use's binding input: BindAndAdaptNode (the expected and the bound Type, BU-72),
    //   BindVariableCore and a conversion's written target (BindConversionTarget: a Type target, a typed borrow's, @wrap's and @bits' U;
    //   PLAN G1240). The inline walk (VisitInlineProjection, SPEC 6.2) reads a by-value projection's form in the walking node's scope, a
    //   judgment of that scope's storage (F's C2 ruling 304). (iii), a member step's receiver (SPEC 9.6; R7-N1 c-b): QualifierSelection's
    //   receiver Type, in the use's view, and EvaluateProjection's normal form, in the left evaluation's view (its kept layer); each form
    //   is read by its step alone. J2, the identity proposition, reads the classes (IdentityProven, among
    //   others ResolveAssociated's candidate agreement), no form. Every comparison of identical Types whose inputs are formed is one of
    //   J1-J4; one input is not formed: an anonymous function's written header Types, which the unifier's decomposition, Infer's
    //   TargetProjection and array-length-hole branches and 10.7's fixed parts compare as written (open issue D-779, PLAN G999:
    //   rv.hd-dec). Any other reader compares Types as written.
    // - Readings: each judgment reads its pair formed in its one view (J1's request and facts, J3's two sides, J4's binding and
    //   evidence); a first binding reads Types as given; a pattern's decomposition reads them as given, which is sound only where its
    //   evidence is formed (an unformed header is the open issue); a reader of a published Type forms what it reads in its own view.
    // - Exposure: a form is visible only inside its judging view: J1's normalized request and kept readings (per view serial), J3's
    //   compared pair (transient), J4's pair within the branch and what the branch stores (the solve's solution, read only in the use's
    //   own view: the selected call's instantiation), (ii)'s Types in the use's body (its HIR, and MIR through TyCtxt.Premised with the
    //   body's scope), among them a call's published result and a value call's signature (CallableContractType in the call's view, dev's
    //   one published form: F's C2 ruling 268). Once J4 has formed both bindings, a twice-bound slot stores the form and a once-bound
    //   slot keeps the written Type; 8.2.3 allows it (every reader forms, J3), but a record naming the instantiated signature can show
    //   either. Published columns (associated-Type bindings, declared signatures, requirement instances, conformance tables) hold Types
    //   as ContractType gives them; records name the Types as written (8.2.3: "which Type is shown has no meaning").
    // - Invariants: (1) a form is taken in the view of the judgment that reads it and kept only where that view alone reads it; the table
    //   keeps its forms and clears them at its Open, so none outlives it; (2) in one view the form is idempotent and equal for identical
    //   Types, so two sides meet exactly when the view identifies them; (3) a form identifies nothing the view's classes do not: forming
    //   a Contract reference's arguments relates no Types (8.2.3: Contract arguments never decompose), and an erroneous or incomplete
    //   class leaves its Types as written; (4) termination (R4.1, below) and W7. NormalizeProofConstraint meets the Completion (W1);
    //   ProveConformance, the walk's root, J4, CallableContractType and (ii) discard it, as MemberOf's readers do, until R7-C2e2
    //   carries Completion through the judgments' answers.
    // - Prohibitions: no form in a producer whose result another view reads (ContractTypeCore and ContractType*, StoredType and
    //   UnconditionalBinding, ResolveAssociated's stored binding, BindFunctionParts' declared signature, requirement instances); no form
    //   at FixCallSlots, at a first binding or in a pattern; no reader outside the list (a missing comparison is found by its judgment,
    //   never added as a site); no second comparison after a failed one, no mode or bool parameter, no per-form clause, no first-premise
    //   or premise-order choice. ClassForm does not read MemberOf's unresolved-operand gate: a fit compares Types, not closed worlds (N1).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private BoundType ClassForm(BoundType type, BindingScope scope, out Completion completion)
    {
        completion = Completion.Complete;
        return type.HasAbstractPart && HasTypeIdentity(scope) ? this.AbstractClassForm(type, scope, out completion) : type;
    }

    // ClassForm past its inlined domain test (F's C2 ruling 317 (3)): a Type with an abstract part in a view with an identity premise,
    // in the view's classes.
    private BoundType AbstractClassForm(BoundType type, BindingScope scope, out Completion completion)
    {
        if (this.ClassesOf(scope, out completion) is not { } classes)
        {
            return type;
        }

        this.ReadIdentityPremiseError(scope, type); // R7-C3 (F's C2 ruling 276 (4)): a Type of an erroneous class is a failed input
        return this.ClassForm(type, classes);
    }

    // Terminates on every table (design R4.1): from a Type of a class ClassForm descends only into its Member's components, each a class
    // graph edge the cycle marking marks, so a descent that met its class again would be a marked class, which has no Member; a class
    // met again within one formation is the table's broken contract (InternalInvariant_Kd).
    private BoundType ClassForm(BoundType type, IdentityClasses classes)
    {
        if (!type.HasAbstractPart)
        {
            return type;
        }

        if (classes.FormOf(type) is { } known)
        {
            return known;
        }

        BoundType formed;
        var (least, member) = ((BoundType?)null, (BoundType?)null);
        var root = classes.OfConstructed(type) ? -1 : classes.FormClass(type, out least, out member);
        if (member is not null)
        {
            classes.EnterForm(root);
            try
            {
                formed = this.FormParts(member, classes);
            }
            finally
            {
                classes.LeaveForm(root);
            }
        }
        else
        {
            formed = root >= 0 ? least ?? type : this.FormParts(classes.Applied(type), classes); // R7-C3: a family application of no class forms as its instance
        }

        classes.SetForm(type, formed);
        return formed;
    }

    // A Type rebuilt over its parts' forms: a projection's subject by its class key (SPEC 8.3.3: the expansion never enters a
    // projection's subject), every other component and BU-60's held context by ClassForm; a rebuilt struct or enum gets its storage prepared,
    // as a substituted declared Type does (SPEC 8.2).
    private BoundType FormParts(BoundType type, IdentityClasses classes)
    {
        var count = type.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                var part = type.Components[i];
                components[i] = i == 0 && type.Kind == BoundTypeKind.AssociatedProjection ? this.ClassKey(part, classes) : this.ClassForm(part, classes);
                changed |= !ReferenceEquals(components[i], part);
            }

            var context = type.InstanceContext;
            if (TyCtxt.HeldType(type) is { } held && this.ClassForm(held, classes) is var image && !ReferenceEquals(image, held))
            {
                context = this.types.HeldContext(type.Symbol!, image);
            }

            if (!changed && ReferenceEquals(context, type.InstanceContext))
            {
                return type;
            }

            var formed = this.WithContext(this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments), context);
            this.PrepareTypeStorage(formed);
            return formed;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // The Type over its components' keys, whose class is asked again; the Type itself when no component's key differs.
    private BoundType CongruentKey(BoundType type, IdentityClasses classes)
    {
        var count = type.Components.Count;
        if (count == 0 || !type.ContainsParameter)
        {
            return type;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.ClassKey(type.Components[i], classes);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            if (!changed)
            {
                return type;
            }

            var rebuilt = this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments);
            return classes.Representative(rebuilt) ?? rebuilt;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // The table's part of the key: its least member per class (Representative) and the keys it keeps (cleared at Open with the rest).
    private sealed partial class IdentityClasses
    {
        private readonly Dictionary<BoundType, BoundType> keys = new(ReferenceEqualityComparer.Instance); // ClassKey's answers (R7-C2c2)
        private readonly Dictionary<int, (BoundType? Least, BoundType? Member)> scanned = new(); // per class root: Representative's and Member's answers
        private readonly Dictionary<BoundType, BoundType> forms = new(ReferenceEqualityComparer.Instance); // ClassForm's answers (R7-C2c2 c2-4b)
        private readonly HashSet<int> forming = new(); // the class roots one formation is inside (ClassForm's termination guard)
        private readonly Dictionary<int, (int Start, int Count)> projections = new(); // ProjectionMembers' range per class root (G881)
        private readonly List<BoundType> projectionMembers = new();

        // PLAN G881: the projection members of a Type's class in Order, appended to `into`, where Members lists the class (ContractSolver's
        // row (5) walks them). Each class is scanned and sorted once per table, so a link of a parameter-only class costs a lookup.
        internal void ProjectionMembers(BoundType type, List<BoundType> into)
        {
            var root = this.ListedRoot(type);
            if (root < 0)
            {
                return;
            }

            if (!this.projections.TryGetValue(root, out var range))
            {
                var start = this.projectionMembers.Count;
                for (var m = this.head[root]; m >= 0; m = this.next[m])
                {
                    binding.IdentityMateWork++;
                    if (this.types[m].Kind == BoundTypeKind.AssociatedProjection)
                    {
                        this.projectionMembers.Add(this.types[m]);
                    }
                }

                range = (start, this.projectionMembers.Count - start);
                CollectionsMarshal.AsSpan(this.projectionMembers).Slice(range.Start, range.Count).Sort(static (x, y) => Order(x, y));
                this.projections[root] = range;
            }

            for (var i = range.Start; i < range.Start + range.Count; i++)
            {
                into.Add(this.projectionMembers[i]);
            }
        }

        // R7-C2c2: the table's key of a Type ClassKey formed from it, or null. Keys are a function of the closed table (no class joins
        // after Close), so each Type is keyed once per table; they go with the table at its next Open.
        internal BoundType? KeyOf(BoundType type) => this.keys.GetValueOrDefault(type);

        internal void SetKey(BoundType type, BoundType key) => this.keys[type] = key;

        // R7-C2c2: the key of a class in state One whose meets are complete, its least parameter or unresolved projection by Order (a
        // projection of an abstract subject: a projection of a constructed Type is its binding, SPEC 8.2.3 Structure; c2-4b) (shallowest
        // first), for any Type of it (ClassOf: a node, or a Type congruent with one, c2-4b), constructed ones included (c2-4); null for a
        // Type of no class, a class with no such member, or
        // an erroneous one (an erroneous class keys nothing). The table's finality is not read: it decides only reuse (ContractSolver's
        // kept rows).
        internal BoundType? Representative(BoundType type) => this.Scanned(type).Least;

        // R7-C2c2 c2-4 (SPEC 8.2.3, 8.3.2; design R1.1, R1.2): the class's constructed member whose closed world decides an atom about
        // any member, a closed one (no parameter) first, else the least by Order (the more determined spelling; never premise order);
        // null as Representative. The members of a class in state One are identical, so any one decides. Its readers: MemberOf (the
        // atom judges' subjects) and Refuted.
        internal BoundType? Member(BoundType type) => this.Scanned(type).Member;

        // R7-C2c2 c2-4b: the class ClassForm reads: its root (-1 for a Type of no class) with Scanned's Representative and Member (both
        // null for an erroneous or incomplete class, whose Types stay as written).
        internal int FormClass(BoundType type, out BoundType? least, out BoundType? member)
        {
            var root = this.ClassOf(type);
            (least, member) = root < 0 ? default : this.Scanned(type);
            return root;
        }

        internal BoundType? FormOf(BoundType type) => this.forms.GetValueOrDefault(type);

        // R7-C2c2 c2-4b (design R4.10): a projection of a constructed subject (its target a constructor) is that Type's declared
        // conformance's binding (SPEC 8.2.3 Projections), an obligation while the conformance is being declared (8.2.3 Identity
        // premises): no abstract Type, so it never represents a class and is never formed by its class (ContractTypeCore's (E) and
        // ProjectionSpecification complete it).
        internal bool OfConstructed(BoundType type) => type.Kind == BoundTypeKind.AssociatedProjection && !this.Target(type.Components[0]).IsAbstractType;

        internal void SetForm(BoundType type, BoundType formed) => this.forms[type] = formed;

        internal void EnterForm(int root)
        {
            if (!this.forming.Add(root))
            {
                throw new InvalidOperationException("A class was formed again inside its own formation (InternalInvariant; R7-C2c2 c2-4b design R4.1).");
            }
        }

        internal void LeaveForm(int root) => this.forming.Remove(root);

        // The one class-validity rule and member scan behind both answers: a Type's class (ClassOf: any node, constructed ones included,
        // or a Type congruent with one) in state One whose meets are complete, scanned once per table (its members are fixed after
        // Close), whatever the number asked. The Member is a constructor as the cycle marking counts it (Constructs), so every step from
        // a class into its Member's components is a class graph edge MarkCycleEdges marks (design R4.1). Its Kind list names the
        // members that may key a class (R7-M25: a Type parameter, the original `s/T` among them, or an associated projection), not SPEC
        // 8.2.3's abstract Type, which the meets test by IsAbstractType.
        private (BoundType? Least, BoundType? Member) Scanned(BoundType type)
        {
            var root = this.ClassOf(type);
            if (root < 0 || this.state[root] != One || this.incompleteClass[root])
            {
                return default;
            }

            if (!this.scanned.TryGetValue(root, out var answer))
            {
                BoundType? best = null;
                BoundType? complete = null;
                BoundType? open = null;
                for (var m = this.head[root]; m >= 0; m = this.next[m])
                {
                    var member = this.types[m];
                    binding.IdentityKeyWork++;
                    if (member.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection ||
                        (member.Kind == BoundTypeKind.AssociatedProjection && !this.OfConstructed(member)))
                    {
                        best = best is null || Order(member, best) < 0 ? member : best;
                    }
                    else if (!this.Constructs(m))
                    {
                        continue; // a pair target or an owner-only application is never the Member (design R4.1: Form's descents are cycle edges)
                    }
                    else if (!member.ContainsParameter)
                    {
                        complete = complete is null || Order(member, complete) < 0 ? member : complete;
                    }
                    else
                    {
                        open = open is null || Order(member, open) < 0 ? member : open;
                    }
                }

                this.scanned[root] = answer = (best, complete ?? open);
            }

            return answer;
        }
    }
}
