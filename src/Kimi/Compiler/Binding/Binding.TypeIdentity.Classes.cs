// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding.TypeIdentity's identity classes of one view (the entry file states the contract): a union-find over the premises' subterms,
// closed under decomposition and congruence, each class with its state and its finality.
public sealed partial class Binding
{
    private sealed partial class IdentityClasses(Binding binding)
    {
        internal const byte One = 0;
        internal const byte Limit = 1; // R7-C2c2 c2-4: the binding grounds' closure limit (PremiseClosureLimit), below every error state
        internal const byte Unsupported = 2;
        internal const byte Unproven = 3;
        internal const byte Cyclic = 4;
        internal const byte Conflict = 5;

        private readonly Dictionary<BoundType, int> node = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, int> signatures = new();
        private readonly List<BoundType> types = new();
        private readonly List<int> parent = new();
        private readonly List<(int A, int B)> pending = new();
        private readonly List<bool> incompleteClass = new(); // a meet resting on an Origin "no" while a declaration it read is not complete
        private readonly List<bool> transparent = new(); // a pair application admitting exactly `owner`: its target (SPEC 8.2.3)
        private readonly List<(BoundOrigin A, BoundOrigin B)> correspondence = new(); // one Function pair's quantified Origins, by position
        private readonly List<(BoundType A, BoundType B)> parts = new(); // a meet's decomposition, read by its consumer and removed
        private readonly HashSet<int> grounded = new(); // projection nodes joined with a binding ground (BindingGrounds, R7-C2c2 c2-4)
        private readonly HashSet<int> familyGrounded = new(); // R7-C3 B4: family applications with a node joined with their instance (FamilyGrounds)
        private readonly Dictionary<int, int> roundFamilies = new(); // FamilyGrounds' round index: FamilyKey -> first family node (chained by roundNext)
        private readonly List<int> roundNext = new();
        private readonly Dictionary<int, (BindingSymbol Owner, BindingSymbol Associated, int From)> groundOrigin = new(); // a ground's new nodes: the binding and the node it grounded
        private readonly List<int> unbounded = new(); // projection nodes whose ground passes the closure limit (SPEC 8.3.3 Limits)
        private readonly List<byte> state = new();
        private readonly List<int> head = new();
        private readonly List<int> next = new();
        private readonly List<int> signatureNext = new();
        private readonly Dictionary<BoundType, int> congruent = new(ReferenceEqualityComparer.Instance); // ClassOf's answers for Types that are no node
        private readonly Dictionary<int, int> families = new(); // R7-C3 R1: Close's family entries, FamilyKey -> first family node (chained by familyNext)
        private readonly List<int> familyNext = new();
        private readonly Dictionary<int, BoundType?> familyMembers = new(); // per class root: FamilyMember's answer
        private readonly Dictionary<BoundType, BoundType> applied = new(ReferenceEqualityComparer.Instance); // Applied's answers
        private readonly List<int> order = new(); // the class graph's strongly connected components (MarkCycles)
        private readonly List<int> low = new();
        private readonly List<int> component = new();
        private readonly List<int> open = new();
        private readonly List<byte> cycle = new();
        private readonly List<(int Root, int Member, int Component)> stack = new();
        private readonly HashSet<BoundType> groupSeen = new(ReferenceEqualityComparer.Instance); // InGroup's walk (R7-C3 C3d)
        private readonly List<BoundType> groupWalk = new();
        private BindingScope? scope;
        private bool provisional;
        private bool meetProvisional;
        private bool closed; // Close ended: the congruence index holds the final partition (ClassOf reads it)
        private int reading; // Same's meets in progress: they ask the attribute domains, which read no identity, so nothing reopens the table

        internal byte Worst { get; private set; }

        // DECISIONS R7-M20's W7: Provisional when a class is not final or an availability the table read is Provisional.
        internal Completion Completion => this.Final ? Completion.Complete : Completion.Provisional;

        // Whether the table is Complete (the memo's condition).
        internal bool Final => !this.provisional;

        internal int Work { get; private set; }

        internal void Read(Completion availability) => this.provisional |= availability == Completion.Provisional;

        internal void Open(BindingScope view)
        {
            if (this.reading != 0)
            {
                throw new InvalidOperationException("An identity table was reopened while Same read it (InternalInvariant; R7-C3a).");
            }

            this.scope = view;
            this.provisional = false;
            this.Work = 0;
            this.Worst = One;
            this.node.Clear();
            this.signatures.Clear();
            this.types.Clear();
            this.parent.Clear();
            this.pending.Clear();
            this.incompleteClass.Clear();
            this.transparent.Clear();
            this.correspondence.Clear();
            this.parts.Clear();
            this.grounded.Clear();
            this.familyGrounded.Clear();
            this.groundOrigin.Clear();
            this.unbounded.Clear();
            this.state.Clear();
            this.head.Clear();
            this.next.Clear();
            this.signatureNext.Clear();
            this.congruent.Clear();
            this.families.Clear();
            this.familyNext.Clear();
            this.familyMembers.Clear();
            this.applied.Clear();
            this.closed = false;
            this.order.Clear();
            this.low.Clear();
            this.component.Clear();
            this.open.Clear();
            this.cycle.Clear();
            this.stack.Clear();
            this.keys.Clear();
            this.scanned.Clear();
            this.projections.Clear();
            this.projectionMembers.Clear();
            this.forms.Clear();
            this.forming.Clear();
        }

        internal void Premise(BoundType a, BoundType b, Completion availability)
        {
            this.Read(availability);
            this.pending.Add((this.Add(a), this.Add(b)));
        }

        // The one finality rule (W7): a class is final unless one of its meets rests on an Origin "no" from incomplete declarations or an
        // unrentable premise environment.
        internal bool Failed(BoundType type)
            => this.ClassOf(type) is var root && root >= 0 && this.state[root] != One && !this.incompleteClass[root];

        // The members of a Type's class in index order, appended to `into`, where the class is final and One (Same's condition: an
        // erroneous or provisional class fixes no form); nothing for a Type outside the classes (PLAN G823's key walk reads them).
        internal void Members(BoundType type, List<BoundType> into)
        {
            var root = this.ListedRoot(type);
            for (var i = root < 0 ? -1 : this.head[root]; i >= 0; i = this.next[i])
            {
                into.Add(this.types[i]);
            }
        }

        // Whether a Type has a part in a final erroneous class (the producer channel's question).
        internal bool Holds(BoundType? type)
        {
            if (type is null)
            {
                return false;
            }

            if (this.Failed(type))
            {
                return true;
            }

            for (var i = 0; i < Arity(type); i++)
            {
                if (this.Holds(Component(type, i)))
                {
                    return true;
                }
            }

            return false;
        }

        // R7-C3 C3d (PLAN G830): whether a Type or a part of it (Arity and Component, as Holds walks) is in the final error group `group`
        // (OwnError's): one walk over the Type's parts, each once, linear in its distinct parts (no recursion; a Type shares its parts).
        internal bool InGroup(BoundType? type, int group)
        {
            if (type is null)
            {
                return false;
            }

            this.groupSeen.Clear();
            this.groupWalk.Clear();
            this.groupWalk.Add(type);
            while (this.groupWalk.Count != 0)
            {
                var part = this.groupWalk[^1];
                this.groupWalk.RemoveAt(this.groupWalk.Count - 1);
                if (!this.groupSeen.Add(part))
                {
                    continue;
                }

                if (this.ClassOf(part) is var root && root >= 0 && this.Group(root) == group)
                {
                    return true;
                }

                for (var i = 0; i < Arity(part); i++)
                {
                    this.groupWalk.Add(Component(part, i));
                }
            }

            return false;
        }

        // R7-C3 C3d (F's C2/C3 rulings 332, 333; SPEC 8.3.4, 23.1.2): the whole of a pair layer in the final error group `group` whose binder
        // admits no case in this view (PairHead's empty set, 8.3.4's Error: the failed input the group's meets read), else null. One scan
        // of the nodes, on an error group only.
        internal BoundType? EmptiedPair(int group)
        {
            for (var n = 0; n < this.types.Count; n++)
            {
                if (TryPairLayer(this.types[n], out var whole, out _) && this.Group(this.Find(n)) == group &&
                    binding.AdmittedSemantics(whole, this.scope!) == SemanticsMask.None)
                {
                    return whole;
                }
            }

            return null;
        }

        // The worst final error class this table holds that the parent's table does not hold with the same state for the same member (the
        // premises that first form it are this scope's), whether an own error class is still waiting for its Origin declarations, and
        // whether an own final class is at the closure limit, which a worse class hides from Worst; each own final error group with its
        // worst state, appended to `groups` (R7-C3 C3d: the recorder's one record per group, PLAN G830).
        internal (byte Worst, bool Waiting, bool Limited) OwnError(IdentityClasses? parent, List<(int Group, byte State)> groups)
        {
            var (worst, waiting, limited) = (One, false, false);
            for (var root = 0; root < this.types.Count; root++)
            {
                if (this.head[root] < 0 || this.state[root] == One)
                {
                    continue;
                }

                if (parent is not null && parent.node.TryGetValue(this.types[this.head[root]], out var p) && parent.state.Count > p &&
                    parent.state[parent.Find(p)] == this.state[root] && !parent.incompleteClass[parent.Find(p)])
                {
                    continue; // The parent's premises formed it first.
                }

                waiting |= this.incompleteClass[root];
                worst = this.incompleteClass[root] ? worst : Math.Max(worst, this.state[root]);
                limited |= !this.incompleteClass[root] && this.state[root] == Limit;
                if (!this.incompleteClass[root])
                {
                    AddGroup(groups, this.Group(root), this.state[root]);
                }
            }

            return (worst, waiting, limited);

            static void AddGroup(List<(int Group, byte State)> groups, int group, byte state)
            {
                for (var i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Group == group)
                    {
                        groups[i] = (group, Math.Max(groups[i].State, state));
                        return;
                    }
                }

                groups.Add((group, state));
            }
        }

        // Verdict's Proven: one final class in state One, or one head and agreeing attributes over Proven parts.
        internal bool Same(BoundType a, BoundType b)
        {
            (a, b) = (this.Applied(this.Target(a)), this.Applied(this.Target(b))); // R7-C3: a family application reads as its instance
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (this.ClassOf(a) is var rx && rx >= 0 && this.ClassOf(b) is var ry && ry >= 0)
            {
                if (rx == ry)
                {
                    return this.state[rx] == One && !this.incompleteClass[rx];
                }

                if (this.state[rx] != One || this.state[ry] != One)
                {
                    return false;
                }
            }

            if (a.IsAbstractType || b.IsAbstractType)
            {
                return false;
            }

            var start = this.parts.Count;
            bool same;
            this.reading++;
            try
            {
                same = this.Meet(a, b) == One;
            }
            finally
            {
                this.reading--;
            }

            for (var i = start; same && i < this.parts.Count; i++)
            {
                same = this.Same(this.parts[i].A, this.parts[i].B);
            }

            this.parts.RemoveRange(start, this.parts.Count - start);
            return same;
        }

        // SPEC 8.2.3's Refuted (PLAN G866), read by the one table the premise meets read (Meet): a node stands for its class's Member
        // (R7-C2c2 c2-4); one class is no difference; an abstract Type (IsAbstractType) differs only as
        // a proper part; two constructors differ where their meet conflicts or a pair of their parts differs, or as a proper part.
        // The meet is the premises' own: a Function pair's quantification structure is a compared attribute (Correspond), while an
        // attribute's Unproven (Origins whose agreement is not proven) decides nothing. Limit: SameHead reads no length arguments.
        internal bool Refuted(BoundType a, BoundType b)
        {
            (a, b) = (this.Applied(this.Target(a)), this.Applied(this.Target(b))); // R7-C3: a family application reads as its instance
            (a, b) = (this.Member(a) ?? a, this.Member(b) ?? b); // R7-C2c2 c2-4 (design R1.2): Member, with parameters too
            if (ReferenceEquals(a, b) || (this.ClassOf(a) is var x && x >= 0 && x == this.ClassOf(b)))
            {
                return false;
            }

            if (a.IsAbstractType || b.IsAbstractType)
            {
                return this.ProperPart(a, b) || this.ProperPart(b, a);
            }

            var start = this.parts.Count;
            byte meet;
            this.reading++;
            try
            {
                meet = this.Meet(a, b);
            }
            finally
            {
                this.reading--;
            }

            var refuted = meet == Conflict;
            for (var i = start; !refuted && i < this.parts.Count; i++)
            {
                refuted = this.Refuted(this.parts[i].A, this.parts[i].B);
            }

            this.parts.RemoveRange(start, this.parts.Count - start);
            return refuted || this.ProperPart(a, b) || this.ProperPart(b, a);
        }

        // Applies every premise, joins congruent nodes, and meets each class's constructor pairs, whose agreeing parts join, to a fixpoint:
        // the round that joins nothing judged the final classes, so its meets are the states (binding grounds add nodes within their bound,
        // BindingGrounds). Each round is linear in the nodes plus the
        // class sizes' squares (constructors of one class only).
        internal void Close()
        {
            do
            {
                for (var i = 0; i < this.pending.Count; i++)
                {
                    this.Union(this.pending[i].A, this.pending[i].B);
                }

                this.pending.Clear();
                this.Congruence();
                this.Members();
                this.BindingGrounds();
                this.FamilyGrounds();
                this.state.Clear();
                for (var n = 0; n < this.types.Count; n++)
                {
                    this.state.Add(One);
                }

                for (var root = 0; root < this.types.Count; root++)
                {
                    this.MeetMembers(root);
                }
            }
            while (this.pending.Count != 0);

            for (var i = 0; i < this.unbounded.Count; i++)
            {
                var root = this.Find(this.unbounded[i]);
                this.state[root] = Math.Max(this.state[root], Limit); // the closure limit's class (SPEC 8.3.3 Limits)
            }

            this.MarkCycles();
            for (var root = 0; root < this.types.Count; root++)
            {
                if (this.head[root] >= 0 && this.state[root] != One)
                {
                    this.provisional |= this.incompleteClass[root];
                    this.Worst = Math.Max(this.Worst, this.state[root]);
                }
            }

            for (var n = 0; n < this.types.Count; n++)
            {
                this.familyNext.Add(-1);
                if (IsFamily(this.types[n]))
                {
                    var key = this.FamilyKey(this.types[n]);
                    if (this.families.TryGetValue(key, out var first))
                    {
                        this.familyNext[n] = this.familyNext[first];
                        this.familyNext[first] = n;
                    }
                    else
                    {
                        this.families[key] = n;
                    }
                }
            }

            this.closed = true;
        }

        // R7-C3 (design §3 R1; SPEC 8.2.3 Projections, 8.5.4): a Contract binds a family "for every a" (Iterator's `LentItem(step) is
        // Item`). Close publishes each family node at its own binder parameters (the clause a `T is C` premise supplies; IsFamily) by its
        // FamilyKey (the head and its components' classes, no Origin arguments). Once closed, an application with Origin arguments of no
        // node of its own whose FamilyKey has an entry in a final One class reads as that class's least non-family member by Order
        // (else, a class of families only, its least family member: H) instanced at the application's Origin arguments; every other
        // Type reads as itself. Every answer reads it (ClassOf, Same, Refuted; Binding's ClassForm and ClassKey); no class joins
        // after Close; kept per table.
        internal BoundType Applied(BoundType type)
        {
            if (!this.closed || type.Kind != BoundTypeKind.AssociatedProjection || type.OriginArguments.Count == 0 || this.node.ContainsKey(type))
            {
                return type;
            }

            if (this.applied.TryGetValue(type, out var known))
            {
                return known;
            }

            var answer = type;
            if (this.families.TryGetValue(this.FamilyKey(type), out var first))
            {
                for (var m = first; m >= 0; m = this.familyNext[m])
                {
                    if (this.SameFamily(this.types[m], type))
                    {
                        var root = this.Find(m);
                        if (this.state[root] == One && !this.incompleteClass[root] && this.FamilyMember(root) is { } member)
                        {
                            answer = binding.SubstituteStoredOrigins(member, type.Symbol!.Declaration, (BoundOrigin[])type.OriginArguments);
                        }

                        break;
                    }
                }
            }

            this.applied[type] = answer;
            return answer;
        }

        // A family node: an associated projection applied at its own declaration's Origin parameters, in order (a clause's subject).
        private static bool IsFamily(BoundType type)
            => type is { Kind: BoundTypeKind.AssociatedProjection, OriginArguments.Count: > 0 } && UniversalAssociatedArguments(type);

        // A node's components (SPEC 8.2.3's Structure): a pair layer's one component is its target (TryPairLayer), so the original `s/T`
        // is the application of `s` to its target; any other Type's are its Components. Every component walk of a node reads these.
        private static int Arity(BoundType type) => TryPairLayer(type, out _, out _) ? 1 : type.Components.Count;

        private static BoundType Component(BoundType type, int i) => TryPairLayer(type, out _, out var target) ? target : type.Components[i];

        // SPEC 8.2.3's pair row: a pair layer whose admitted set includes `owner` may be its target itself (PairHead, ProperPart,
        // MarkCycleEdges). An empty set (8.3.4's Error, 8.3.1's contradictory evidence) admits no `owner`; PairHead decides it first.
        private static bool MayBeItsTarget(SemanticsMask admitted) => (admitted & SemanticsMask.Owner) != 0;

        // An Origin quantified in a Function Type, or a meet with one among its operands (a result's omitted Origin is the meet of the
        // per-call inputs, SPEC 8.10, 15.4.3): quantification structure (8.2.3's Function row).
        private static bool Quantifies(BoundOrigin? origin)
            => origin is { Kind: OriginKind.Input or OriginKind.Parameter, Binder: FunctionTypeKoto } ||
                (origin is { Kind: OriginKind.Intersection } && HasQuantifiedOperand(origin));

        private static bool HasQuantifiedOperand(BoundOrigin meet)
        {
            for (var i = 0; i < meet.Operands.Count; i++)
            {
                if (Quantifies(meet.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int OperandCount(BoundOrigin origin) => origin.Kind == OriginKind.Intersection ? origin.Operands.Count : 1;

        private static BoundOrigin Operand(BoundOrigin origin, int i) => origin.Kind == OriginKind.Intersection ? origin.Operands[i] : origin;

        private static bool HasOperand(BoundOrigin meet, BoundOrigin operand)
        {
            for (var i = 0; i < OperandCount(meet); i++)
            {
                if (ReferenceEquals(Operand(meet, i), operand))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameHead(BoundType x, BoundType y)
            => x.Kind == y.Kind && ReferenceEquals(x.Symbol, y.Symbol) && x.Semantics == y.Semantics && x.Components.Count == y.Components.Count &&
            ReferenceEquals(x.InstanceContext, y.InstanceContext) && (x.Kind != BoundTypeKind.Primitive || ReferenceEquals(x, y)) &&
            x.OriginArguments.Count == y.OriginArguments.Count &&
            (x.LengthExpression is not null || y.LengthExpression is not null || x.Length == y.Length);

        // Whether `part` is `type` up to structure or is reached from it by steps into components, a pair layer's target included; an
        // abstract Type and a family end a route (ProperPart's walk).
        private static bool Reaches(BoundType part, BoundType type)
        {
            if (SameStructure(part, type))
            {
                return true;
            }

            if (TryPairLayer(type, out _, out var target))
            {
                return Reaches(part, target);
            }

            if (type.IsAbstractType || type.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                return false;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Reaches(part, type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // SPEC 8.2.3, 3.8: two Types of one structure, their Origins aside (SameExceptOrigins at every layer).
        private static bool SameStructure(BoundType a, BoundType b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (!a.SameExceptOrigins(b))
            {
                return false;
            }

            for (var i = 0; i < a.Components.Count; i++)
            {
                if (!SameStructure(a.Components[i], b.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // The class's least non-family member by Order, else its least family member (H: a class of families bound to each other answers
        // the family of least Order; that family's own entry is this class again, by congruence, so no second hop reads anything new).
        private BoundType? FamilyMember(int root)
        {
            // Inside Close's rounds the classes still join, so the answer is kept only once closed.
            if (!this.closed || !this.familyMembers.TryGetValue(root, out var answer))
            {
                var own = this.OwnMember(root);
                BoundType? family = null;
                for (var m = this.head[root]; own is null && m >= 0; m = this.next[m])
                {
                    family = family is null || Order(this.types[m], family) < 0 ? this.types[m] : family; // a class of families only
                }

                answer = own ?? family;
                if (this.closed)
                {
                    this.familyMembers[root] = answer;
                }
            }

            return answer;
        }

        // A node that is a constructor of its class (R7-C2c2 c2-4b, design R4.1): no abstract Type (IsAbstractType; the original `s/T`
        // is a pair application) and no pair layer admitting exactly `owner` (that one is its target). The one classification the class
        // graph's edges and cycles (Walk, MarkCycleEdges), the meets (MeetMembers), the binding grounds and the Member (Scanned) read.
        private bool Constructs(int n) => !this.types[n].IsAbstractType && !this.transparent[n];

        // A node's edges in the class graph (SPEC 8.3.3's expansion steps), EdgeCount of them: a constructor's components (the
        // proper-part step; Arity and Component, a pair layer's one component its target) and a projection's Contract arguments (its
        // Contract reference's components; never its subject). Only a constructor's edges mark a cycle (MarkCycleEdges): a cycle of
        // projection arguments alone never grows a Type (R7-C2c2 c2-4b, design R2.1, RB2).
        private int EdgeCount(int n)
        {
            var type = this.types[n];
            if (this.Constructs(n))
            {
                return Arity(type);
            }

            return type.Kind == BoundTypeKind.AssociatedProjection && type.Components.Count > 1 ? type.Components[1].Components.Count : 0;
        }

        private BoundType Edge(int n, int i) => this.Constructs(n) ? Component(this.types[n], i) : this.types[n].Components[1].Components[i];

        // The one class lookup (R7-C2c2 c2-4b, design R4.2): the class of a Type's target (SPEC 8.2.3's pair row), a node's root, else, once
        // the table is closed, the root of the node it is congruent with (the table's own congruence: Signature and Congruent over its
        // components' classes, the index the last Congruence round left), else -1. Answers for Types that are no node are kept per table.
        // R7-C3: a family application reads as its entry's instance (Applied).
        private int ClassOf(BoundType type)
        {
            type = this.Applied(this.Target(type));
            if (this.node.TryGetValue(type, out var n))
            {
                return this.Find(n);
            }

            if (!this.closed || type.Components.Count == 0 || type.Kind == BoundTypeKind.Primitive)
            {
                return -1;
            }

            if (this.congruent.TryGetValue(type, out var known))
            {
                return known < 0 ? -1 : this.Find(known);
            }

            var found = -1;
            if (this.signatures.TryGetValue(this.Signature(type), out var first))
            {
                for (var m = first; m >= 0 && found < 0; m = this.signatureNext[m])
                {
                    found = this.Congruent(this.types[m], type) ? this.Find(m) : -1;
                }
            }

            this.congruent[type] = found;
            return found;
        }

        // A component's class, as Signature and Congruent compare it: a node's root (no target read: an owner-only application node is
        // joined with its target), else ClassOf.
        private int ComponentClass(BoundType component)
            => this.node.TryGetValue(component, out var n) ? this.Find(n) : this.ClassOf(component);

        // Whether a Type holds an Origin quantified in a Function Type, in any Origin slot.
        private bool Carries(BoundType type)
        {
            if (Quantifies(this.Slot(type).Slot))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (Quantifies(type.OriginArguments[i]))
                {
                    return true;
                }
            }

            for (var i = 0; i < Arity(type); i++)
            {
                if (this.Carries(Component(type, i)))
                {
                    return true;
                }
            }

            return false;
        }

        // The class's constructor pairs (an owner-only pair application is no constructor): an agreeing pair's parts join, and the state
        // is the worst meet. A "no" that more premises may turn marks the class incomplete.
        private void MeetMembers(int root)
        {
            for (var i = this.head[root]; i >= 0; i = this.next[i])
            {
                for (var j = this.next[i]; j >= 0; j = this.next[j])
                {
                    if (!this.Constructs(i) || !this.Constructs(j))
                    {
                        continue;
                    }

                    this.meetProvisional = false;
                    var start = this.parts.Count;
                    var meet = this.Meet(this.types[i], this.types[j]);
                    this.incompleteClass[root] |= meet != One && this.meetProvisional;
                    for (var k = start; meet == One && k < this.parts.Count; k++)
                    {
                        var (p, q) = (this.node[this.parts[k].A], this.node[this.parts[k].B]);
                        if (this.Find(p) != this.Find(q))
                        {
                            this.pending.Add((p, q));
                        }
                    }

                    this.parts.RemoveRange(start, this.parts.Count - start);
                    this.state[root] = Math.Max(this.state[root], meet);
                }
            }
        }

        // The head with its components' class ids and its compared attributes by reference: equal keys are one Type (SPEC 8.2.3 replacement).
        private int Signature(BoundType type)
        {
            var hash = this.FamilyKey(type);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                hash = HashCode.Combine(hash, type.OriginArguments[i]);
            }

            return hash;
        }

        // Signature without the Origin arguments (R7-C3 R1: a family's key, SameFamily's hash).
        private int FamilyKey(BoundType type)
        {
            var hash = HashCode.Combine(type.Kind, type.Symbol, type.Semantics, type.Length, type.LengthExpression, type.Origin, type.InstanceContext);
            for (var i = 0; i < type.Components.Count; i++)
            {
                hash = HashCode.Combine(hash, this.ComponentClass(type.Components[i]));
            }

            return hash;
        }

        private void Congruence()
        {
            var joined = true;
            while (joined)
            {
                joined = false;
                this.signatures.Clear();
                this.signatureNext.Clear();
                for (var n = 0; n < this.types.Count; n++)
                {
                    this.signatureNext.Add(-1);
                    if (this.types[n].Components.Count == 0 || this.types[n].Kind == BoundTypeKind.Primitive)
                    {
                        continue;
                    }

                    var key = this.Signature(this.types[n]);
                    if (!this.signatures.TryGetValue(key, out var first))
                    {
                        this.signatures[key] = n;
                        continue;
                    }

                    var match = -1;
                    for (var m = first; m >= 0 && match < 0; m = this.signatureNext[m])
                    {
                        match = this.Congruent(this.types[m], this.types[n]) ? m : -1;
                    }

                    if (match >= 0)
                    {
                        joined |= this.Union(match, n);
                    }
                    else
                    {
                        this.signatureNext[n] = this.signatureNext[first];
                        this.signatureNext[first] = n;
                    }
                }
            }
        }

        // R7-C2c2 c2-4 (design RB3): a projection node whose subject's class has a constructor member with an unconditional binding
        // for it (UnconditionalBinding: declarations only, no identity or proof, within this table's prohibition) is joined with that
        // binding's instance, as a premise is; once per node, so its pending union starts the next round as decomposition's do, and the
        // rounds end when one joins nothing. A binding not yet complete makes the table Provisional (W7). The bound (SPEC 8.3.3
        // Limits): the nodes a ground adds remember the binding (conforming declaration, associated Type) and the node it grounded;
        // a chain uses one binding at most PremiseClosureLimit times (each use is one round of the premises' closure, the limit
        // G881b's request rounds count), whatever the spelling of its Types (ground-dec2 and its directly spelled twin alike). A
        // ground past it is not taken and its class is in state Limit, recorded once at the declaration as PremiseClosureLimit_Kd
        // (a Resource record); a growing expansion (`associate C.X is Option<G<Option<A>>.(C).X>`) and a descent deeper than the limit
        // (ground-deep) reach it alike. Finite: each binding is used a bounded number of times along any chain.
        // R7-C3 (B4; SPEC 8.2.3 Projections, 8.5.4): a family application that is a node of its own (a premise's side, `U.(L).Lent(b)`)
        // is its family's instance at its Origin arguments, as Applied reads one of no node: once per node it joins that instance (the
        // family class's least non-family member instanced at its Origin arguments) inside the rounds, as a binding ground does; no class
        // joins after Close. It reads FamilyMember, the one instance rule Applied reads (a class of families only answers its least
        // family member), and skips only the node itself; each node is grounded once, so the rounds end.
        private void FamilyGrounds()
        {
            var count = this.types.Count;
            this.roundFamilies.Clear();
            this.roundNext.Clear();
            for (var m = 0; m < count; m++)
            {
                this.roundNext.Add(-1);
                if (IsFamily(this.types[m]))
                {
                    var key = this.FamilyKey(this.types[m]);
                    this.roundNext[m] = this.roundFamilies.TryGetValue(key, out var first) ? first : -1;
                    this.roundFamilies[key] = m;
                }
            }

            for (var n = 0; n < count && this.roundFamilies.Count != 0; n++)
            {
                var type = this.types[n];
                if (type.Kind != BoundTypeKind.AssociatedProjection || type.OriginArguments.Count == 0 || IsFamily(type) || this.familyGrounded.Contains(n) ||
                    !this.roundFamilies.TryGetValue(this.FamilyKey(type), out var first))
                {
                    continue;
                }

                for (var m = first; m >= 0; m = this.roundNext[m])
                {
                    if (!this.SameFamily(this.types[m], type))
                    {
                        continue;
                    }

                    if (this.Find(m) != this.Find(n) && this.FamilyMember(this.Find(m)) is { } member &&
                        binding.SubstituteStoredOrigins(member, type.Symbol!.Declaration, (BoundOrigin[])type.OriginArguments) is var instance && !ReferenceEquals(instance, type))
                    {
                        this.familyGrounded.Add(n);
                        this.pending.Add((n, this.Add(instance)));
                    }

                    break;
                }
            }

            if (this.types.Count != count)
            {
                this.Members(); // the instances' new nodes are classes of their own until the next round joins them
            }
        }

        // A class's least non-family member by Order (FamilyGrounds; FamilyMember answers this once closed).
        private BoundType? OwnMember(int root)
        {
            BoundType? own = null;
            for (var m = this.head[root]; m >= 0; m = this.next[m])
            {
                if (!IsFamily(this.types[m]) && (own is null || Order(this.types[m], own) < 0))
                {
                    own = this.types[m];
                }
            }

            return own;
        }

        private void BindingGrounds()
        {
            var count = this.types.Count;
            for (var n = 0; n < count; n++)
            {
                var type = this.types[n];
                if (type.Kind != BoundTypeKind.AssociatedProjection || this.grounded.Contains(n) || !this.node.TryGetValue(type.Components[0], out var subject))
                {
                    continue;
                }

                for (var m = this.head[this.Find(subject)]; m >= 0; m = this.next[m])
                {
                    if (!this.Constructs(m))
                    {
                        continue;
                    }

                    var instance = binding.UnconditionalBinding(this.types[m], type, out var unfinished);
                    this.provisional |= unfinished;
                    if (instance is null)
                    {
                        continue;
                    }

                    this.grounded.Add(n);
                    if (this.PastClosureLimit(n, this.types[m].Symbol!, type.Symbol!))
                    {
                        this.unbounded.Add(n);
                        break;
                    }

                    var before = this.types.Count;
                    this.pending.Add((n, this.Add(instance)));
                    for (var k = before; k < this.types.Count; k++)
                    {
                        this.groundOrigin[k] = (this.types[m].Symbol!, type.Symbol!, n);
                    }

                    break;
                }
            }

            if (this.types.Count != count)
            {
                this.Members(); // the instances' new nodes are classes of their own until the next round joins them
            }
        }

        // Whether grounding node n through `owner`'s binding for `associated` passes the closure limit: n's chain (the grounds that
        // added it, back to a premise's node) already used that binding PremiseClosureLimit times.
        private bool PastClosureLimit(int n, BindingSymbol owner, BindingSymbol associated)
        {
            var uses = 0;
            for (var at = n; this.groundOrigin.TryGetValue(at, out var origin); at = origin.From)
            {
                uses += ReferenceEquals(origin.Owner, owner) && ReferenceEquals(origin.Associated, associated) ? 1 : 0;
            }

            return uses >= PremiseClosureLimit;
        }

        private void Members()
        {
            this.head.Clear();
            this.next.Clear();
            for (var n = 0; n < this.types.Count; n++)
            {
                this.head.Add(-1);
                this.next.Add(-1);
            }

            for (var n = this.types.Count - 1; n >= 0; n--)
            {
                var root = this.Find(n);
                this.next[n] = this.head[root];
                this.head[root] = n;
            }
        }

        // SPEC 8.2.3 Cycles and 8.3.3 Unbounded Types: a class on a cycle through a constructor edge is Cyclic; one that is so only through
        // pair layers that may be their target (MayBeItsTarget) is the Proof row. The class graph (Edge: a constructor's components,
        // Arity, and a projection's Contract arguments) is cut into strongly connected components by an iterative Tarjan walk, linear in
        // nodes plus edges, with no depth bound.
        private void MarkCycles()
        {
            for (var n = 0; n < this.types.Count; n++)
            {
                this.order.Add(-1);
                this.low.Add(0);
                this.component.Add(-1);
                this.cycle.Add(0);
            }

            var visits = 0;
            for (var start = 0; start < this.types.Count; start++)
            {
                if (this.head[start] >= 0 && this.order[start] < 0)
                {
                    this.Visit(start, ref visits);
                    this.Walk(ref visits);
                }
            }

            this.MarkCycleEdges();
        }

        private void Visit(int root, ref int visits)
        {
            this.order[root] = this.low[root] = visits++;
            this.open.Add(root);
            this.stack.Add((root, this.head[root], 0));
        }

        private void Walk(ref int visits)
        {
            while (this.stack.Count != 0)
            {
                var (root, member, at) = this.stack[^1];
                if (member < 0)
                {
                    this.stack.RemoveAt(this.stack.Count - 1);
                    for (var closed = -1; this.low[root] == this.order[root] && closed != root; this.open.RemoveAt(this.open.Count - 1))
                    {
                        closed = this.open[^1];
                        this.component[closed] = root;
                    }

                    if (this.stack.Count != 0)
                    {
                        var up = this.stack[^1].Root;
                        this.low[up] = Math.Min(this.low[up], this.low[root]);
                    }

                    continue;
                }

                if (at >= this.EdgeCount(member))
                {
                    this.stack[^1] = (root, this.next[member], 0);
                    continue;
                }

                this.stack[^1] = (root, member, at + 1);
                var target = this.Find(this.node[this.Edge(member, at)]);
                if (this.order[target] < 0)
                {
                    this.Visit(target, ref visits);
                }
                else if (this.component[target] < 0)
                {
                    this.low[root] = Math.Min(this.low[root], this.order[target]);
                }
            }
        }

        // A component's internal edge lies on a cycle; one from no pair layer that may be its target (MayBeItsTarget) makes the
        // component Cyclic.
        private void MarkCycleEdges()
        {
            for (var member = 0; member < this.types.Count; member++)
            {
                var type = this.types[member];
                if (!this.Constructs(member))
                {
                    continue;
                }

                var from = this.component[this.Find(member)];
                var mark = TryPairLayer(type, out _, out _) && MayBeItsTarget(this.Admitted(type)) ? (byte)1 : (byte)2;
                for (var i = 0; i < Arity(type); i++)
                {
                    this.cycle[from] |= this.component[this.Find(this.node[Component(type, i)])] == from ? mark : (byte)0;
                }
            }

            for (var root = 0; root < this.types.Count; root++)
            {
                if (this.head[root] >= 0 && this.cycle[this.component[root]] != 0)
                {
                    this.state[root] = Math.Max(this.state[root], this.cycle[this.component[root]] >= 2 ? Cyclic : Unproven);
                }
            }
        }

        // An error class's group, one premise error (SPEC 8.2.3): the class itself, or for a class on a cycle its strongly connected
        // component (MarkCycles), so `T is Option<U>` with `U is Option<T>` is one `cyclic` error over its two classes.
        private int Group(int root) => this.cycle[this.component[root]] != 0 ? this.component[root] : root;

        private bool Congruent(BoundType x, BoundType y)
        {
            if (!this.SameFamily(x, y))
            {
                return false;
            }

            for (var i = 0; i < x.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(x.OriginArguments[i], y.OriginArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Congruent but for the Origin arguments' references (their number agrees): one family's applications (R7-C3 R1, FamilyKey).
        private bool SameFamily(BoundType x, BoundType y)
        {
            if (x.Kind != y.Kind || !ReferenceEquals(x.Symbol, y.Symbol) || x.Components.Count != y.Components.Count || !ReferenceEquals(x.Origin, y.Origin) ||
                !ReferenceEquals(x.LengthExpression, y.LengthExpression) || x.Length != y.Length || x.Semantics != y.Semantics ||
                !ReferenceEquals(x.InstanceContext, y.InstanceContext) || x.OriginArguments.Count != y.OriginArguments.Count)
            {
                return false;
            }

            for (var i = 0; i < x.Components.Count; i++)
            {
                if (this.ComponentClass(x.Components[i]) != this.ComponentClass(y.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Two constructors' meet, each compared attribute by its domain's rule; an agreeing pair leaves its parts (Same, MeetMembers).
        private byte Meet(BoundType x, BoundType y)
        {
            if (x.IsPairLayer || y.IsPairLayer)
            {
                return this.PairMeet(x, y);
            }

            if (!SameHead(x, y))
            {
                return Conflict;
            }

            if (x.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Closure)
            {
                // SPEC 8.2.3's Function row: quantified Origins correspond by position within the pair (renaming changes nothing).
                this.correspondence.Clear();
                var meet = this.Correspond(x, y);
                this.correspondence.Clear();
                return meet;
            }

            return this.Decompose(x, y);
        }

        // SPEC 8.2.3's pair row (8.3.4's admitted sets). Pair layers of one `s` share their head. A pair that may be its target itself
        // (MayBeItsTarget) meets every form: the truth varies (Unproven). Otherwise
        // disjoint outer Semantics (`owner` for a Type that is no Semantics layer, another pair's admitted set; an empty set is disjoint
        // from every one) differ; where the heads
        // may agree the components decide in that case: one head on both sides decomposes, and a head that varies compares them too
        // while the identity stays Unproven (the premise-error row 4). A pair layer admitting exactly `owner` never meets (Add).
        // Every pair layer reaches it, the original `s/T` included (its one component is its target, Arity).
        private byte PairMeet(BoundType x, BoundType y)
        {
            var (head, pair, other, target, component) = this.PairHead(x, y);
            return component is null ? head : this.PairParts(pair, target, other, component, head);
        }

        // The pair row's head (PairMeet's comment): the heads' meet and, where the heads may agree so that the components decide, the
        // pair layer, the other side, the pair's target and the other side's component (null when the head alone decides). A family is
        // no form of 8.2.3's table (Unsupported). An empty admitted set on either side is 8.3.4's Error (8.3.1's contradictory
        // evidence) and disjoint from every set, and a pair differs from another form where its admitted set is disjoint from the other's
        // outer Semantics (8.2.3): Conflict, decided before the other tests read the sets (one binder's shared head, MayBeItsTarget).
        private (byte Head, BoundType Pair, BoundType Other, BoundType Target, BoundType? Component) PairHead(BoundType x, BoundType y)
        {
            var (pair, other) = x.IsPairLayer ? (x, y) : (y, x);
            if (other.Kind == BoundTypeKind.SemanticsAdaptation || !TryPairLayer(pair, out var whole, out var target))
            {
                return (Unsupported, pair, other, pair, null);
            }

            var paired = TryPairLayer(other, out var otherWhole, out var otherTarget);
            var admitted = binding.AdmittedSemantics(whole, this.scope!);
            var outer = !paired ? (other.Kind == BoundTypeKind.Semantics ? other.Semantics.ToMask() : SemanticsMask.Owner)
                : ReferenceEquals(whole, otherWhole) ? admitted : binding.AdmittedSemantics(otherWhole, this.scope!);
            if (admitted == SemanticsMask.None || outer == SemanticsMask.None)
            {
                return (Conflict, pair, other, target, null); // An empty set: 8.3.4's Error, disjoint from every set (8.2.3).
            }

            if (paired && ReferenceEquals(whole, otherWhole))
            {
                return (One, pair, other, target, otherTarget);
            }

            if (MayBeItsTarget(admitted) || (paired && MayBeItsTarget(outer)))
            {
                return (Unproven, pair, other, target, null);
            }

            if ((admitted & outer) == 0)
            {
                return (Conflict, pair, other, target, null);
            }

            var component = paired ? otherTarget : other is { Kind: BoundTypeKind.Semantics, Components: [var inner] } ? inner : null;
            return (component is null ? Unsupported : admitted == outer && admitted.IsSingle() ? One : Unproven, pair, other, target, component);
        }

        // SPEC 8.2.3 (Refuted, Cycles): whether `part` is a proper part of `whole` up to structure (Origins are never a known
        // difference): a route of steps into constructors' components, a pair layer's target included, with one step that is no pair
        // that may be its target (MayBeItsTarget), as the class graph's cycle edges count it (MarkCycleEdges); an abstract
        // Type and a family end a route (Reaches).
        private bool ProperPart(BoundType part, BoundType whole)
        {
            if (whole.IsAbstractType || whole.Kind == BoundTypeKind.SemanticsAdaptation)
            {
                return false;
            }

            if (TryPairLayer(whole, out _, out var target))
            {
                return MayBeItsTarget(this.Admitted(whole)) ? this.ProperPart(part, target) : Reaches(part, target);
            }

            for (var i = 0; i < whole.Components.Count; i++)
            {
                if (Reaches(part, whole.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // A pair layer's target and the other side's component as the meet's part, the two layers' slots compared as Decompose compares
        // an Origin (Slot: a pair layer's outer Origin only in its admitted borrow cases). `head` is the heads' meet.
        private byte PairParts(BoundType pair, BoundType target, BoundType other, BoundType component, byte head)
        {
            this.parts.Add((target, component));
            return Math.Max(head, this.OriginMeet(this.Slot(pair).Slot, this.Slot(other).Slot));
        }

        // A constructor pair outside a Function pair: its components are its parts, its Origins and length compared by their domains.
        private byte Decompose(BoundType x, BoundType y)
        {
            for (var i = 0; i < x.Components.Count; i++)
            {
                this.parts.Add((x.Components[i], y.Components[i]));
            }

            var meet = ReferenceEquals(x.LengthExpression, y.LengthExpression) ? this.OriginMeet(x.Origin, y.Origin) : Unproven;
            for (var i = 0; i < x.OriginArguments.Count && meet != Unproven; i++)
            {
                meet = Math.Max(meet, this.OriginMeet(x.OriginArguments[i], y.OriginArguments[i]));
            }

            return meet;
        }

        // Two Origin slots by the Origin domain's rule (SPEC 8.2.3's compared attributes, 15.6.5): One where proven to agree, else
        // Unproven. An Origin quantified in a Function Type is quantification structure, which only the Function row's correspondence
        // compares (Pair); outside it (a pair application's layer at a Function position) it is no form of that domain: Unsupported.
        private byte OriginMeet(BoundOrigin? a, BoundOrigin? b)
            => Quantifies(a) || Quantifies(b) ? Unsupported
                : binding.OriginsAgree(a, b, this.scope!.Owner, ref this.meetProvisional) ? One : Unproven;

        // A Function pair walked position by position under its own correspondence: every Origin slot pairs a quantified Origin with one
        // other only (else the structures differ), a meet of them operand by operand; fixed Origins agree by their domain; abstract
        // positions are its parts, where a quantified Origin would be carried out of its Function Type (Refuted); a pair application
        // against a constructor carrying one is walked as a layer (CorrespondPair).
        private byte Correspond(BoundType x, BoundType y)
        {
            var paired = x.IsPairLayer || y.IsPairLayer;
            var structural = !x.IsAbstractType && !y.IsAbstractType && !paired;
            if (!structural && !(x.Kind == y.Kind && ReferenceEquals(x.Symbol, y.Symbol) && x.Kind != BoundTypeKind.Parameter))
            {
                if (!this.Carries(x) && !this.Carries(y))
                {
                    this.parts.Add((x, y));
                    return One;
                }

                return !paired ? Conflict : x.IsAbstractType || y.IsAbstractType ? Unsupported : this.CorrespondPair(x, y);
            }

            if (!SameHead(x, y))
            {
                return Conflict;
            }

            var meet = !ReferenceEquals(x.LengthExpression, y.LengthExpression) ? Unproven : this.SlotMeet(x, y);
            for (var i = 0; i < x.OriginArguments.Count && meet != Conflict; i++)
            {
                meet = Math.Max(meet, this.Pair(x.OriginArguments[i], y.OriginArguments[i]));
            }

            for (var i = 0; i < x.Components.Count && meet != Conflict; i++)
            {
                meet = Math.Max(meet, this.Correspond(x.Components[i], y.Components[i]));
            }

            return meet;
        }

        // SPEC 8.2.3's pair row at a Function position, walked as the Function row walks every layer: an application admitting exactly
        // `owner` is its target (Target); otherwise the head is the pair row's (PairHead), where it is one Semantics the outer-Origin
        // slots compare as every slot does (SlotMeet), and the target is walked as every component is. Never below Unsupported: such an
        // identity is never joined (the stated limit).
        private byte CorrespondPair(BoundType x, BoundType y)
        {
            (x, y) = (this.Target(x), this.Target(y));
            if (!x.IsPairLayer && !y.IsPairLayer)
            {
                return Math.Max(this.Correspond(x, y), Unsupported);
            }

            var (meet, pair, other, target, component) = this.PairHead(x, y);
            meet = component is not null && meet == One ? this.SlotMeet(pair, other) : meet;
            return Math.Max(component is null || meet == Conflict ? meet : Math.Max(meet, this.Correspond(target, component)), Unsupported);
        }

        // Two layers' outer-Origin slots at corresponding positions of a Function pair, compared as the Function row compares every slot
        // (Pair). A pair layer's slot is its OuterOrigin, present only in its admitted borrow cases (SPEC 8.1.2), so a difference in
        // only some cases varies (Unproven); any other layer's is its Origin.
        private byte SlotMeet(BoundType x, BoundType y)
        {
            var (a, someA) = this.Slot(x);
            var (b, someB) = this.Slot(y);
            var meet = this.Pair(a, b);
            return meet == Conflict && (someA || someB) ? Unproven : meet;
        }

        private (BoundOrigin? Slot, bool SomeCases) Slot(BoundType layer)
        {
            if (!TryPairLayer(layer, out var whole, out _))
            {
                return (layer.Origin, false);
            }

            var admitted = binding.AdmittedSemantics(whole, this.scope!);
            return (admitted & SemanticsMask.Borrow) == 0 ? (null, false) : (binding.OuterOrigin(layer), (admitted & ~SemanticsMask.Borrow) != 0);
        }

        private byte Pair(BoundOrigin? a, BoundOrigin? b)
        {
            if (Quantifies(a) != Quantifies(b))
            {
                return Conflict;
            }

            if (!Quantifies(a))
            {
                return this.OriginMeet(a, b);
            }

            return a!.Kind == OriginKind.Intersection || b!.Kind == OriginKind.Intersection ? this.PairMeets(a, b!) : this.PairOne(a, b!);
        }

        // SPEC 8.2.3's Function row over two meets: a different number of quantified operands is a different structure; each quantified
        // operand of `a` meets its partner in the correspondence, or the first unpaired quantified operand of `b`; fixed operands agree by
        // their domain, position by position (OriginMeet).
        private byte PairMeets(BoundOrigin a, BoundOrigin b)
        {
            var (count, quantified) = (OperandCount(a), 0);
            for (var i = 0; i < count; i++)
            {
                quantified += Quantifies(Operand(a, i)) ? 1 : 0;
            }

            for (var i = 0; i < OperandCount(b); i++)
            {
                quantified -= Quantifies(Operand(b, i)) ? 1 : 0;
            }

            if (quantified != 0)
            {
                return Conflict;
            }

            var meet = count == OperandCount(b) ? One : Unproven;
            for (var i = 0; i < count && meet != Conflict; i++)
            {
                var x = Operand(a, i);
                if (!Quantifies(x))
                {
                    meet = Math.Max(meet, i < OperandCount(b) ? this.OriginMeet(x, Operand(b, i)) : Unproven);
                    continue;
                }

                var y = this.Partner(x) ?? this.Unpaired(b);
                meet = y is null || !HasOperand(b, y) ? Conflict : Math.Max(meet, this.PairOne(x, y));
            }

            return meet;
        }

        private BoundOrigin? Partner(BoundOrigin x)
        {
            foreach (var (p, q) in this.correspondence)
            {
                if (ReferenceEquals(p, x))
                {
                    return q;
                }
            }

            return null;
        }

        private BoundOrigin? Unpaired(BoundOrigin meet)
        {
            for (var i = 0; i < OperandCount(meet); i++)
            {
                var y = Operand(meet, i);
                if (Quantifies(y) && !this.PairedRight(y))
                {
                    return y;
                }
            }

            return null;
        }

        private bool PairedRight(BoundOrigin y)
        {
            foreach (var (_, q) in this.correspondence)
            {
                if (ReferenceEquals(q, y))
                {
                    return true;
                }
            }

            return false;
        }

        // One quantified Origin paired with one other only (else the structures differ).
        private byte PairOne(BoundOrigin a, BoundOrigin b)
        {
            foreach (var (p, q) in this.correspondence)
            {
                if (ReferenceEquals(p, a) || ReferenceEquals(q, b))
                {
                    return ReferenceEquals(p, a) && ReferenceEquals(q, b) ? One : Conflict; // One Origin paired with two: the structures differ.
                }
            }

            this.correspondence.Add((a!, b!));
            return One;
        }

        // The admitted set of a pair layer's binder in this view (SPEC 8.3.4); All for any other Type.
        private SemanticsMask Admitted(BoundType application)
            => TryPairLayer(application, out var whole, out _) ? binding.AdmittedSemantics(whole, this.scope!) : SemanticsMask.All;

        // The root of a Type's class where Members lists it (a final One class), else -1 (Members' and ProjectionMembers' condition).
        private int ListedRoot(BoundType type)
            => this.ClassOf(type) is var root && root >= 0 && this.state[root] == One && !this.incompleteClass[root] ? root : -1;

        // SPEC 8.2.3's pair row: with exactly `owner` admitted, `s/V` is `V` itself.
        private BoundType Target(BoundType type)
        {
            while (this.Admitted(type) == SemanticsMask.Owner)
            {
                type = Component(type, 0);
            }

            return type;
        }

        // A node is added with its subterms; a pair layer admitting exactly `owner` is its target from the start: joined with it,
        // and no constructor (no head, no proper part, no cycle edge).
        private int Add(BoundType type)
        {
            if (this.node.TryGetValue(type, out var n))
            {
                return n;
            }

            for (var i = 0; i < Arity(type); i++)
            {
                this.Add(Component(type, i));
            }

            n = this.types.Count;
            this.node.Add(type, n);
            this.types.Add(type);
            this.parent.Add(n);
            this.incompleteClass.Add(false);
            var target = this.Target(type);
            this.transparent.Add(!ReferenceEquals(target, type));
            if (!ReferenceEquals(target, type))
            {
                this.pending.Add((n, this.node[Component(type, 0)]));
            }

            this.Work++;
            return n;
        }

        private int Find(int n)
        {
            while (this.parent[n] != n)
            {
                this.parent[n] = this.parent[this.parent[n]];
                n = this.parent[n];
            }

            return n;
        }

        // The smaller index is the root: the partition and every state are functions of the premise set.
        private bool Union(int a, int b)
        {
            var (x, y) = (this.Find(a), this.Find(b));
            if (x == y)
            {
                return false;
            }

            if (y < x)
            {
                (x, y) = (y, x);
            }

            this.parent[y] = x;
            this.incompleteClass[x] |= this.incompleteClass[y];
            return true;
        }
    }
}
