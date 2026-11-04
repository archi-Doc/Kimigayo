// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's accesses at a location (A3 5's access, E13 (1)): the accesses of borrowed roots that the location's uses make, each classified by
// what it does (KindOf, MirBorrows.Checks' AccessKind) and with the check set it takes, and a Call's or Drop's effect access after them
// (MirBorrows.Effects); every write also goes to R26-access before the borrowed-root filter (MirBorrows.Defaults). Its classifier KindOf is
// also read by MirBorrows.OwnSteps' MayDepart, Dropping by MirBorrows.Holders (drop slots), Dropping and Destruction by MirBorrows.Liveness
// and MirBorrows.Activations, and Dropping by MirBorrows.Destructions' receivers. It owns the observation (Observation, its Observer over
// MirTypes' destruction walk; SPEC 15.6.6, 16.3.3), whose one product reader is Destruction.
// Must not compare an access with a Loan (MirBorrows.Checks) or record a problem (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private readonly Dictionary<BoundType, byte> entered = new(ReferenceEqualityComparer.Instance); // Observation's scratch.
    private readonly Dictionary<BoundType, MirOutcome> destructions = new(ReferenceEqualityComparer.Instance); // Destruction's, by case Type.
    private (BindingSymbol? Definition, ulong Version) destructionsOf; // The definition and program version the destructions are for.
    private Access[] accesses = [];
    private int accessCount;

    /// <summary>Gets what destroying a value of a Type may observe of the Origins and Loans it holds (SPEC 15.6.6, 16.3.3), as the walks'
    /// answer (<see cref="MirOutcome"/>): its <see cref="MirTypes.Destruction"/> reaches a struct that declares a drop, which observes all it
    /// reaches, a Function value, whose destruction is unknown, or an owning handle whose payload is not proven Sealed, or is and observes,
    /// since any rc or arc release may be final (decided, where the walk stops); an abstract part not proven Copy, whose instance decides
    /// (Limit, DECISIONS R4-M147); and a part past the walk's bound (Deep, DECISIONS R4-M153). The answer is the Join of its parts' (the
    /// walk goes on past Limit and Deep), so it is the same in every order of the parts: decided whatever an unknown part is, and Limit
    /// beside a part past the bound. A payload is walked below its handle: a recursive Type ends, exactly where it repeats and at the walk's
    /// bound where it grows.</summary>
    /// <param name="type">The Type.</param>
    /// <param name="definition">The definition whose premises prove Copy and Sealed.</param>
    /// <returns>What the destruction may observe.</returns>
    internal MirOutcome Observation(BoundType? type, BindingSymbol? definition)
    {
        this.entered.Clear();
        var observer = new Observer(this, types, definition);
        MirTypes.Destruction(type, ref observer);
        return observer.Answer;
    }

    // What a use of a Place at a location does (A3 5's access, E13 (1)), none for a use that accesses nothing: Copy, FakeRead,
    // Discriminant, Len and an index read; Move; a Ref borrows (Borrowing), as a Reborrowing PairAcquire does, which its reader passes as a
    // borrow in its marker's mode; a destination is written; StorageDead destroys, and a Drop as Dropping says.
    private AccessKind? KindOf(MirLocation at, MirPlace place, MirAccess access, MirRefKind kind)
    {
        if ((access & MirAccess.Kills) != 0)
        {
            return at.Index < this.body.Blocks[at.Block].Count && this.body.Statements[this.Location(at.Block, at.Index)].Kind == MirStatementKind.StorageDead
                ? AccessKind.Destroy : null;
        }

        if ((access & MirAccess.DropUse) != 0)
        {
            return this.Dropping(at.Block, place);
        }

        if ((access & MirAccess.Writes) != 0)
        {
            return AccessKind.Write;
        }

        if ((access & MirAccess.Borrows) != 0)
        {
            return this.Borrowing(at, kind);
        }

        if ((access & MirAccess.Moves) != 0)
        {
            return AccessKind.Move;
        }

        return (access & MirAccess.Reads) != 0 ? AccessKind.Read : null;
    }

    // How a Drop under the view destroys its Place (A3 5's access, R4-M6): observing what it holds where its case Type observes and A2 keeps
    // some of it (no group: only the checks reach it), else shallowly.
    private AccessKind Dropping(int block, MirPlace place)
    {
        var group = this.program.Drops.Group(this.body.Ordinal, block, this.view);
        if (group >= 0 && this.program.Drops.RowsOf(this.body.Ordinal)[group].Verdict == MirDropVerdict.Elide)
        {
            return AccessKind.Destroy;
        }

        return this.Destruction(cases.Type(this.body.TypeOf(place)!));
    }

    // How destroying a value of a case Type accesses it, read off the walks' answer (Observation): observing where decided or Limit,
    // unclassified where Deep (past the walk's bound), else shallowly; once per case Type for the body's definition and the program version
    // (Observation reads the definition's Copy and Sealed proofs).
    private AccessKind Destruction(BoundType type)
    {
        if (this.destructionsOf != (this.body.Key.Decl, this.program.Version))
        {
            this.destructionsOf = (this.body.Key.Decl, this.program.Version);
            this.destructions.Clear();
        }

        ref var observation = ref CollectionsMarshal.GetValueRefOrAddDefault(this.destructions, type, out var known);
        if (!known)
        {
            observation = this.Observation(type, this.body.Key.Decl);
        }

        if (observation.IsDeep)
        {
            return AccessKind.Unclassified;
        }

        return observation.IsNone ? AccessKind.Destroy : AccessKind.Observe;
    }

    // A Ref's access: a unique one borrows exclusively, the destroying reservation reads its target (A3 4's destroying(site)), another
    // borrows shared.
    private AccessKind Borrowing(MirLocation at, MirRefKind kind)
    {
        if (kind == MirRefKind.Uniq)
        {
            return AccessKind.UniqueBorrow;
        }

        var loan = this.loanAt[this.Location(at.Block, at.Index)];
        return loan >= 0 && this.loans[loan].Destroying ? AccessKind.Read : AccessKind.SharedBorrow;
    }

    // A location's effect access, listed after its own accesses so that the one gather takes its values (MirBorrows.Effects checks it): at a
    // Call or a Drop of a view with an effect root, or at a Drop of a mutable static (R3), a Call's set its last operand's (TakesStores
    // without one: its result carries no Loan of its own), a Drop's its value's (TakesValue). It is listed as the widest row; the rule
    // gives each root its own.
    private void Effecting(int block, int index)
    {
        ref readonly var data = ref this.body.Blocks[block];
        var terminator = data.Terminator;
        if (index < data.Count || terminator.Kind is not (MirTerminatorKind.Call or MirTerminatorKind.Drop) ||
            (this.effectRoots.Count == 0 && this.DroppedStatic(terminator) < 0))
        {
            return;
        }

        var taking = TakesValue;
        if (terminator.Kind == MirTerminatorKind.Call)
        {
            var operands = terminator.Count + (terminator.Callee.Kind == MirCalleeKind.Value ? 1 : 0);
            taking = operands > 0 ? operands - 1 : TakesStores;
        }

        MirDataflow.Grow(ref this.accesses, this.accessCount + 1);
        this.accesses[this.accessCount++] = new(terminator.Place, AccessKind.EffectWrite, false, taking);
    }

    private void Accessed(in Access access)
    {
        if (this.borrowed[this.Key(access.Place.Root)])
        {
            MirDataflow.Grow(ref this.accesses, this.accessCount + 1);
            this.accesses[this.accessCount++] = access;
        }
    }

    /// <summary>An access of a borrowed root: its Place, a Reborrow's of its Place's referent, its kind and its check set (A3 E13 (1)):
    /// its operand's index from 0, TakesParents for a borrow, TakesValue for an observing destruction, else TakesStores.</summary>
    private readonly record struct Access(MirPlace Place, AccessKind Kind, bool Reborrow, int Taking);

    // The accesses of one location (A3 5's access, E13 (1)), each as KindOf classifies its use: a Reborrowing PairAcquire's operand Place
    // borrows as its Use's marker says (reborrow; TwoPhaseUniq reserves, a shared access); every write is also R26-access's, whether its root
    // is borrowed or not (MirBorrows.Defaults). Each operand's accesses, its index locals' and its own, take its operand's check set. An
    // Opaque site's roots are its problems' (derived, R4-M10).
    private struct Accessing(MirBorrows owner, MirLocation at, MirRefKind reborrow) : IMirUses
    {
        private MirOperand operand;
        private int operands; // The operands reported: the current one's index plus 1.
        private bool inOperand; // Whether the Places reported are the current operand's.

        public void Operand(in MirOperand operand)
            => (this.operand, this.operands, this.inOperand) = (operand, this.operands + 1, operand.Kind != MirOperandKind.Const);

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var (taking, reborrows) = (this.inOperand ? this.operands - 1 : TakesStores, this.inOperand && this.operand.Kind == MirOperandKind.PairAcquire &&
                reborrow != MirRefKind.Raw && this.operand.Place == place);
            this.inOperand &= this.operand.Place != place; // An operand's index locals precede its Place.
            var opaque = (access & (MirAccess.Reads | MirAccess.Writes)) == (MirAccess.Reads | MirAccess.Writes);
            if (!opaque && owner.KindOf(at, place, reborrows ? access | MirAccess.Borrows : access, reborrows ? reborrow : kind) is { } known)
            {
                if (known == AccessKind.Write)
                {
                    owner.DefaultWrite(place, at);
                }

                taking = known == AccessKind.Observe ? TakesValue : reborrows || (access & MirAccess.Borrows) != 0 ? TakesParents : taking;
                owner.Accessed(new(place, known, reborrows, taking));
            }
        }
    }

    // Observation's visitor: the Join of the reported parts' outcomes, a part that may observe decided (the walk stops), an abstract part
    // not proven Copy Limit, after which the walk goes on, since another part may decide, and Deep where a part past the walk's depth
    // bound has a Type the walk never enters within it (MirDepths). A string's or a sequence's own release observes nothing; its elements
    // are parts of their own.
    private struct Observer(MirBorrows owner, TyCtxt types, BindingSymbol? definition) : IMirDestruction
    {
        internal readonly MirOutcome Answer => this.Outcome.Join(owner.entered.ContainsValue(MirDepths.Past) ? MirOutcome.Deep : MirOutcome.None);

        private MirOutcome Outcome { get; set; }

        public readonly bool First(BoundType type, int depth)
            => MirDepths.Enter(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _), depth);

        public bool Visit(BoundType type, int depth)
        {
            if (type.IsAbstract)
            {
                this.Outcome = this.Outcome.Join(types.Copy(definition, type) ? MirOutcome.None : MirOutcome.Limit);
                return false;
            }

            if (type.Kind == BoundTypeKind.Function || AdtDef.Destructor(type) is not null)
            {
                return this.Decide();
            }

            return type.Kind == BoundTypeKind.Semantics &&
                (MirTypes.SealedPayload(type, types, definition) is { } payload ? MirTypes.Below(payload, ref this, depth + 1) : this.Decide());
        }

        public readonly bool Deep(BoundType type)
        {
            MirDepths.Meet(ref CollectionsMarshal.GetValueRefOrAddDefault(owner.entered, type, out _));
            return false;
        }

        // PLAN G349 U2: the Observer keeps no declaration summary yet; it walks the instance (R4-M153's bound below an indirection).
        public bool Declared(BoundType instance, int depth) => MirTypes.Entered(instance, ref this, depth);

        // A part known to observe: the decided answer, the Join's top, where the walk stops.
        private bool Decide()
        {
            this.Outcome = this.Outcome.Join(MirOutcome.Decided);
            return true;
        }
    }
}
