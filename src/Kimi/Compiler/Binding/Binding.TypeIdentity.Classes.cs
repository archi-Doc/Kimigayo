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
        internal const byte Unsupported = 1;
        internal const byte Unproven = 2;
        internal const byte Cyclic = 3;
        internal const byte Conflict = 4;

        private readonly Dictionary<BoundType, int> node = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, int> signatures = new();
        private readonly List<BoundType> types = new();
        private readonly List<int> parent = new();
        private readonly List<(int A, int B)> pending = new();
        private readonly List<bool> incompleteClass = new(); // a meet resting on an Origin "no" while a declaration it read is not complete
        private readonly List<bool> transparent = new(); // a pair application admitting exactly `owner`: its target (SPEC 8.2.3)
        private readonly List<(BoundOrigin A, BoundOrigin B)> correspondence = new(); // one Function pair's quantified Origins, by position
        private readonly List<(BoundType A, BoundType B)> parts = new(); // a meet's decomposition, read by its consumer and removed
        private readonly List<byte> state = new();
        private readonly List<int> head = new();
        private readonly List<int> next = new();
        private readonly List<int> signatureNext = new();
        private readonly List<int> order = new(); // the class graph's strongly connected components (MarkCycles)
        private readonly List<int> low = new();
        private readonly List<int> component = new();
        private readonly List<int> open = new();
        private readonly List<byte> cycle = new();
        private readonly List<(int Root, int Member, int Component)> stack = new();
        private BindingScope? scope;
        private bool provisional;
        private bool meetProvisional;
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
            this.state.Clear();
            this.head.Clear();
            this.next.Clear();
            this.signatureNext.Clear();
            this.order.Clear();
            this.low.Clear();
            this.component.Clear();
            this.open.Clear();
            this.cycle.Clear();
            this.stack.Clear();
            this.keys.Clear();
            this.scanned.Clear();
        }

        internal void Premise(BoundType a, BoundType b, Completion availability)
        {
            this.Read(availability);
            this.pending.Add((this.Add(a), this.Add(b)));
        }

        // The one finality rule (W7): a class is final unless one of its meets rests on an Origin "no" from incomplete declarations or an
        // unrentable premise environment.
        internal bool Failed(BoundType type)
            => this.node.TryGetValue(type, out var n) && this.state[this.Find(n)] != One && !this.incompleteClass[this.Find(n)];

        // The members of a Type's class in index order, appended to `into`, where the class is final and One (Same's condition: an
        // erroneous or provisional class fixes no form); nothing for a Type outside the classes (PLAN G823's key walk reads them).
        internal void Members(BoundType type, List<BoundType> into)
        {
            if (!this.node.TryGetValue(binding.PairTarget(type, this.scope!), out var n) || this.state[this.Find(n)] != One ||
                this.incompleteClass[this.Find(n)])
            {
                return;
            }

            for (var i = this.head[this.Find(n)]; i >= 0; i = this.next[i])
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

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.Holds(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // The worst final error class this table holds that the parent's table does not hold with the same state for the same member (the
        // premises that first form it are this scope's), and whether an own error class is still waiting for its Origin declarations.
        internal (byte Worst, bool Waiting) OwnError(IdentityClasses? parent)
        {
            var (worst, waiting) = (One, false);
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
            }

            return (worst, waiting);
        }

        // Verdict's Proven: one final class in state One, or one head and agreeing attributes over Proven parts.
        internal bool Same(BoundType a, BoundType b)
        {
            (a, b) = (binding.PairTarget(a, this.scope!), binding.PairTarget(b, this.scope!));
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (this.node.TryGetValue(a, out var x) && this.node.TryGetValue(b, out var y))
            {
                var (rx, ry) = (this.Find(x), this.Find(y));
                if (rx == ry)
                {
                    return this.state[rx] == One && !this.incompleteClass[rx];
                }

                if (this.state[rx] != One || this.state[ry] != One)
                {
                    return false;
                }
            }

            if (!IsConstructor(a) || !IsConstructor(b))
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

        // Applies every premise, joins congruent nodes, and meets each class's constructor pairs, whose agreeing parts join, to a fixpoint:
        // the round that joins nothing judged the final classes, so its meets are the states. Each round is linear in the nodes plus the
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

            this.MarkCycles();
            for (var root = 0; root < this.types.Count; root++)
            {
                if (this.head[root] >= 0 && this.state[root] != One)
                {
                    this.provisional |= this.incompleteClass[root];
                    this.Worst = Math.Max(this.Worst, this.state[root]);
                }
            }
        }

        private static bool IsConstructor(BoundType type)
            => type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection);

        private static bool Quantifies(BoundOrigin? origin) => origin is { Kind: OriginKind.Input or OriginKind.Parameter, Binder: FunctionTypeKoto };

        // Whether a Type holds an Origin quantified in a Function Type, in any Origin slot.
        private static bool Carries(BoundType type)
        {
            if (Quantifies(type.Origin))
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

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Carries(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameHead(BoundType x, BoundType y)
            => x.Kind == y.Kind && ReferenceEquals(x.Symbol, y.Symbol) && x.Semantics == y.Semantics && x.Components.Count == y.Components.Count &&
            ReferenceEquals(x.ClosureContext, y.ClosureContext) && (x.Kind != BoundTypeKind.Primitive || ReferenceEquals(x, y)) &&
            x.OriginArguments.Count == y.OriginArguments.Count &&
            (x.LengthExpression is not null || y.LengthExpression is not null || x.Length == y.Length);

        // The class's constructor pairs (an owner-only pair application is no constructor): an agreeing pair's parts join, and the state
        // is the worst meet. A "no" that more premises may turn marks the class incomplete.
        private void MeetMembers(int root)
        {
            for (var i = this.head[root]; i >= 0; i = this.next[i])
            {
                for (var j = this.next[i]; j >= 0; j = this.next[j])
                {
                    if (!IsConstructor(this.types[i]) || !IsConstructor(this.types[j]) || this.transparent[i] || this.transparent[j])
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
        private int Signature(int n)
        {
            var type = this.types[n];
            var hash = HashCode.Combine(type.Kind, type.Symbol, type.Semantics, type.Length, type.LengthExpression, type.Origin, type.ClosureContext);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                hash = HashCode.Combine(hash, type.OriginArguments[i]);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                hash = HashCode.Combine(hash, this.Find(this.node[type.Components[i]]));
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

                    var key = this.Signature(n);
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

        // SPEC 8.2.3 Cycles: a class that is a proper part of itself is Cyclic; one that is so only through pair applications admitting
        // `owner` is the Proof row. The class graph (a class's edges are its constructor members' components) is cut into strongly
        // connected components by an iterative Tarjan walk, linear in nodes plus edges, with no depth bound.
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

                var type = this.types[member];
                if (!IsConstructor(type) || this.transparent[member] || at >= type.Components.Count)
                {
                    this.stack[^1] = (root, this.next[member], 0);
                    continue;
                }

                this.stack[^1] = (root, member, at + 1);
                var target = this.Find(this.node[type.Components[at]]);
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

        // A component's internal edge lies on a cycle; one through no owner-admitting pair application makes the component Cyclic.
        private void MarkCycleEdges()
        {
            for (var member = 0; member < this.types.Count; member++)
            {
                var type = this.types[member];
                if (!IsConstructor(type) || this.transparent[member])
                {
                    continue;
                }

                var from = this.component[this.Find(member)];
                var mark = type.Kind == BoundTypeKind.SemanticsApplication && (this.Admitted(type) & SemanticsMask.Owner) != 0 ? (byte)1 : (byte)2;
                for (var i = 0; i < type.Components.Count; i++)
                {
                    this.cycle[from] |= this.component[this.Find(this.node[type.Components[i]])] == from ? mark : (byte)0;
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

        private bool Congruent(BoundType x, BoundType y)
        {
            if (x.Kind != y.Kind || !ReferenceEquals(x.Symbol, y.Symbol) || x.Components.Count != y.Components.Count || !ReferenceEquals(x.Origin, y.Origin) ||
                !ReferenceEquals(x.LengthExpression, y.LengthExpression) || x.Length != y.Length || x.Semantics != y.Semantics ||
                !ReferenceEquals(x.ClosureContext, y.ClosureContext) || x.OriginArguments.Count != y.OriginArguments.Count)
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

            for (var i = 0; i < x.Components.Count; i++)
            {
                if (this.Find(this.node[x.Components[i]]) != this.Find(this.node[y.Components[i]]))
                {
                    return false;
                }
            }

            return true;
        }

        // Two constructors' meet, each compared attribute by its domain's rule; an agreeing pair leaves its parts (Same, MeetMembers).
        private byte Meet(BoundType x, BoundType y)
        {
            if (x.Kind == BoundTypeKind.SemanticsApplication || y.Kind == BoundTypeKind.SemanticsApplication)
            {
                return this.PairMeet(x, y);
            }

            if (x.Kind is BoundTypeKind.SemanticsAdaptation || y.Kind is BoundTypeKind.SemanticsAdaptation)
            {
                return Unsupported;
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

        // SPEC 8.2.3's pair application: its head is the admitted set's one Semantics (8.3.4); disjoint from the other's outer Semantics
        // it differs, and a set whose truth varies is the Proof row. An application admitting exactly `owner` never meets (Add).
        private byte PairMeet(BoundType x, BoundType y)
        {
            if (x.Kind == y.Kind && ReferenceEquals(x.Symbol, y.Symbol))
            {
                return this.Decompose(x, y); // Applications of one `s` share their head.
            }

            var (pair, other) = x.Kind == BoundTypeKind.SemanticsApplication ? (x, y) : (y, x);
            if (other.Kind == BoundTypeKind.SemanticsApplication || !TryPairLayer(pair, out _, out _))
            {
                return Unsupported;
            }

            var admitted = this.Admitted(pair);
            var outer = other.Kind == BoundTypeKind.Semantics ? other.Semantics.ToMask() : SemanticsMask.Owner;
            if ((admitted & outer) == 0)
            {
                return Conflict;
            }

            if (admitted != outer)
            {
                return Unproven; // The truth varies by Semantics case: SPEC 8.2.3's premise-error row 4.
            }

            return other.Kind == BoundTypeKind.Semantics && other.Components.Count == pair.Components.Count ? this.Decompose(pair, other) : Unsupported;
        }

        // A constructor pair outside a Function pair: its components are its parts, its Origins and length compared by their domains.
        private byte Decompose(BoundType x, BoundType y)
        {
            for (var i = 0; i < x.Components.Count; i++)
            {
                this.parts.Add((x.Components[i], y.Components[i]));
            }

            if (!ReferenceEquals(x.LengthExpression, y.LengthExpression) ||
                !binding.OriginsAgree(x.Origin, y.Origin, this.scope!.Owner, ref this.meetProvisional))
            {
                return Unproven;
            }

            for (var i = 0; i < x.OriginArguments.Count; i++)
            {
                if (!binding.OriginsAgree(x.OriginArguments[i], y.OriginArguments[i], this.scope!.Owner, ref this.meetProvisional))
                {
                    return Unproven;
                }
            }

            return One;
        }

        // A Function pair walked position by position under its own correspondence: every Origin slot pairs a quantified Origin with one
        // other only (else the structures differ); fixed Origins agree by their domain; abstract positions are its parts, where a
        // quantified Origin would be carried out of its Function Type (Refuted; a pair application there is no form this rule covers).
        private byte Correspond(BoundType x, BoundType y)
        {
            var paired = x.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation ||
                y.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.SemanticsAdaptation;
            var structural = IsConstructor(x) && IsConstructor(y) && !paired;
            if (!structural && !(x.Kind == y.Kind && ReferenceEquals(x.Symbol, y.Symbol) && x.Kind != BoundTypeKind.Parameter))
            {
                if (!Carries(x) && !Carries(y))
                {
                    this.parts.Add((x, y));
                    return One;
                }

                return paired ? Unsupported : Conflict;
            }

            if (!SameHead(x, y))
            {
                return Conflict;
            }

            var meet = !ReferenceEquals(x.LengthExpression, y.LengthExpression) ? Unproven : this.Pair(x.Origin, y.Origin);
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

        private byte Pair(BoundOrigin? a, BoundOrigin? b)
        {
            if (Quantifies(a) != Quantifies(b))
            {
                return Conflict;
            }

            if (!Quantifies(a))
            {
                return binding.OriginsAgree(a, b, this.scope!.Owner, ref this.meetProvisional) ? One : Unproven;
            }

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

        // The admitted set of a pair application's binder in this view (SPEC 8.3.4).
        private SemanticsMask Admitted(BoundType application)
            => TryPairLayer(application, out var whole, out _) ? binding.AdmittedSemantics(whole, this.scope!) : SemanticsMask.All;

        // A node is added with its subterms; an application admitting exactly `owner` is its target from the start: joined with it,
        // and no constructor (no head, no proper part, no cycle edge).
        private int Add(BoundType type)
        {
            if (this.node.TryGetValue(type, out var n))
            {
                return n;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                this.Add(type.Components[i]);
            }

            n = this.types.Count;
            this.node.Add(type, n);
            this.types.Add(type);
            this.parent.Add(n);
            this.incompleteClass.Add(false);
            var target = binding.PairTarget(type, this.scope!);
            this.transparent.Add(!ReferenceEquals(target, type));
            if (!ReferenceEquals(target, type))
            {
                this.pending.Add((n, this.node[type.Components[0]]));
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
