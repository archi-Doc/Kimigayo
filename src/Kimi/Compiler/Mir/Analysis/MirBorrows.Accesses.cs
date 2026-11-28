// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A5's accesses at a location (A3 5's access, E13 (1)): the accesses of borrowed roots that the location's uses make, each classified by
// what it does (KindOf, MirBorrows.Checks' AccessKind) and with the check set it takes, and a Call's or Drop's effect access after them
// (MirBorrows.Effects); every write also goes to R26-access before the borrowed-root filter (MirBorrows.Defaults). Its classifier KindOf is
// also read by MirBorrows.OwnSteps' MayDepart, Dropping by MirBorrows.Holders (drop slots), Dropping and Destruction by MirBorrows.Liveness
// and MirBorrows.Activations, and Dropping and its Types (Droppings, one walk per Drop and view) by MirBorrows.Destructions' receivers and
// stores.
// Must not compare an access with a Loan (MirBorrows.Checks) or record a problem (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private readonly Dictionary<BoundType, byte> entered = new(ReferenceEqualityComparer.Instance); // Observation's scratch.
    private readonly Dictionary<BoundType, MirOutcome> destructions = new(ReferenceEqualityComparer.Instance); // Destruction's, by case Type.
    private readonly List<MirDropStep> dropSteps = []; // MirDropTable.Destroyed's steps of the Drop being read (Droppings).
    private readonly List<BoundType> droppedTypes = []; // The Types the view's Drops destroy whole, each Drop's range once (droppings).
    private (BindingSymbol? Definition, ulong Version) destructionsOf; // The definition and program version the destructions are for.
    private (AccessKind Kind, int Start, int Count)[] droppings = []; // By block: how its Drop destroys its Place, its droppedTypes.
    private Access[] accesses = [];
    private int accessCount;

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
            return this.Dropping(at.Block);
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

    // How a block's Drop under the view destroys its Place (A3 5's access, R4-M6), as Droppings found it.
    private AccessKind Dropping(int block) => this.droppings[block].Kind;

    // How each of the view's Drops destroys its Place (pre-pass, before the addresses, whose own-step departures read it): by what A2's
    // group destroys (MirDropTable.Destroyed, as A4's Dropped reads it; SPEC 16.3.2: only the remaining parts), shallowly where no part
    // is destroyed whole (an Elide group), else as the strongest destruction among the Types destroyed whole: unclassified, then
    // observing (Destruction). Each Drop lists those Types once (droppedTypes, for its stores: Destroyed); a Drop without a row (a body
    // over the analyses' arena limit, R4-M15a) destroys its Place's case Type. Each Drop is read under its own site, as its readers' sites
    // were, so a refusal the walk forms keeps the Drop's statement.
    private void Droppings()
    {
        MirDataflow.Grow(ref this.droppings, this.body.Blocks.Length);
        this.droppedTypes.Clear();
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            if (this.body.Blocks[block].Terminator is not { Kind: MirTerminatorKind.Drop, Place: var place })
            {
                continue;
            }

            var site = this.OpenAt(this.Location(block, this.body.Blocks[block].Count));
            var (start, type, kind) = (this.droppedTypes.Count, cases.TypeOf(place), AccessKind.Destroy);
            var group = this.program.Drops.Group(this.body.Ordinal, block, this.view);
            if (group < 0)
            {
                this.droppedTypes.Add(type);
            }
            else
            {
                this.program.Drops.Destroyed(types, this.body.Ordinal, group, type, this.dropSteps);
                foreach (var step in this.dropSteps)
                {
                    if (step.Kind is MirDropStepKind.Whole or MirDropStepKind.Run && !this.DropsType(start, step.Type))
                    {
                        this.droppedTypes.Add(step.Type);
                    }
                }

                this.dropSteps.Clear();
            }

            for (var i = start; i < this.droppedTypes.Count && kind != AccessKind.Unclassified; i++)
            {
                var destruction = this.Destruction(this.droppedTypes[i]);
                kind = destruction == AccessKind.Destroy ? kind : destruction;
            }

            this.droppings[block] = (kind, start, this.droppedTypes.Count - start);
            types.CloseSite(site);
        }

        this.Work += this.body.Blocks.Length + this.droppedTypes.Count;
    }

    // Whether the Drop whose Types start at start already lists a Type: a fixed array's several Runs of one element Type read alike.
    private bool DropsType(int start, BoundType type)
    {
        for (var i = start; i < this.droppedTypes.Count; i++)
        {
            if (ReferenceEquals(this.droppedTypes[i], type))
            {
                return true;
            }
        }

        return false;
    }

    // How destroying a value of a case Type accesses it, read off the walks' answer (MirTypes.Observation): observing where decided or Limit,
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
            observation = MirTypes.Observation(type, types, this.body.Key.Decl, this.entered);
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
    // Opaque site's undecided uses (MirDescriptors.IsUndecided) are its problems' (derived, R4-M10).
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
            if (!MirDescriptors.IsUndecided(access) &&
                owner.KindOf(at, place, reborrows ? access | MirAccess.Borrows : access, reborrows ? reborrow : kind) is { } known)
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
}
