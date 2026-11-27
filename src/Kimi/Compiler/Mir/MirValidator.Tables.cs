// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A body's tables and the Places that index them (plan 5.4).
// - Responsibility: the locals (local 0 the Return local, the Arg locals directly after it, each of a known kind and typed, a Result
//   local a Temp of local 0's Type; of no Type only Binding's failed slot: _0 and an Arg exactly where the signature's slot is, a Var or
//   Temp by the builder's contract, each accessed only whole as Bounded allows, PLAN G1073; a Field step of no Type, a stored Field whose
//   slot failed, only written, PLAN G1184), the MustNotComplete rows, the scopes, the Opaque sites and the projection table, each in
//   range and each projection step admitted by the Type of the step it extends (MirBody.Selects), a constant index only on an Index of a
//   fixed array (Tables, Admits); each Place a location names (Names): in range
//   (IsValid) and its first step admitted by its root's Type, each Place MirUses reports (Place) and a diverging Call's destination,
//   which it does not (MirValidator.Calls); and a Result local read only by the whole Move that delivers it into _0 (DECISIONS R4-M132).
// - Inputs: the body's tables; each location's Places with their access, and a diverging Call's destination without one. Outputs:
//   Local, TargetRange, Scope, Opaque, Projection, UnknownKind and Place findings; a written root's write bits for G131 and G136
//   (MirValidator.Diverged, MirValidator.Inputs); a valid Place for G191's rule of its static root (MirValidator.Statics) and for the
//   shared layers it passes (MirValidator.SharedPaths).
// - Invariants: Tables returns false exactly when a projection chain or an Opaque site cannot be walked, and Validate then stops; a
//   scope chain that cannot be walked marks the body Unscoped, so Validate runs every check but Entries and SharedPath.
// - Prohibitions: no rule of the operation a location performs (MirValidator.Blocks, .Rvalues, .Calls) and no Type agreement.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // A Place MirUses reports: a Place the body names (Names), then the rules of its access at the Place they judge, an Opaque site's
        // undecided use's root (MirDescriptors.Judged, DECISIONS Z17).
        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (!this.Names(place))
            {
                return;
            }

            place = MirDescriptors.Judged(place, access);
            this.Accessed(place, access);
            if (body.TypeOf(place) is null && (place.Projection == 0 || this.Admitted(place)) && !this.Bounded(place, access))
            {
                this.Report(MirViolation.Local); // PLAN G1073, G1184: a Place of no Type is accessed only where no reader judges its Type.
            }

            if (place.Root >= 0 && body.Locals[place.Root].Role == MirLocalRole.Result &&
                (access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows | MirAccess.DropUse)) != 0 &&
                !(this.delivering && place.Projection == 0 && (access & MirAccess.Moves) != 0))
            {
                this.Report(MirViolation.Local); // R4-M132: the body's result in transit is read only by its delivery into _0.
            }

            if ((access & MirAccess.Writes) != 0 && place.Root >= 0)
            {
                writes[place.Root] |= OtherWrite;
                this.Write(place);
            }

            this.SharedPath(place, access, kind);
        }

        // Locals, MustNotComplete rows, scopes, Opaque sites and the projection table; false when a chain or site cannot be walked.
        internal bool Tables()
        {
            var locals = body.Locals;
            var arguments = true;
            // The receiver (BU-48, amended by G283): a constructor's or a layer body's _0 (R7-M115), and a destructor's self _1, the body MIR
            // names by its Type's destructor.
            var receiver = body.Key.Kind == MirBodyKind.Layer ? 0 : body.Key.Kind != MirBodyKind.Fn ? -1 : AdtDef.IsConstructor(body.Key.Decl) ? 0
                : body.Key.Decl is { } decl && locals is [_, { Type: { } self }, ..] && ReferenceEquals(AdtDef.DestructorSymbol(self), decl) ? 1 : -1;
            var first = this.FirstParameter();
            for (var i = 0; i < locals.Length; i++)
            {
                ref readonly var local = ref locals[i];
                this.At(local.Source);
                arguments &= i == 0 || local.Kind == MirLocalKind.Arg;
                var failed = local.Kind is MirLocalKind.Return or MirLocalKind.Arg && i != receiver && this.SlotFailed(i, first);
                var typed = local.Type is { } type // Unstated marks kept parts only (DECISIONS BU-46).
                    ? !failed && !TyCtxt.StatesUnstated(type, parts) : failed || local.Kind is MirLocalKind.Var or MirLocalKind.Temp;
                var placed = (local.Kind == MirLocalKind.Return) == (i == 0) && (local.Kind != MirLocalKind.Arg || arguments);
                var roled = (local.Role == MirLocalRole.Receiver) == (i == receiver) && // A Result is a temp of _0's Type (MirLocalRole).
                    (local.Role != MirLocalRole.Result || (local.Kind == MirLocalKind.Temp && ReferenceEquals(local.Type, locals[0].Type))) &&
                    (local.Role != MirLocalRole.Assigned || local.Kind == MirLocalKind.Temp); // An Assigned is a temp.
                if (!typed || !placed || local.Kind > MirLocalKind.DropFlag || !roled)
                {
                    this.Report(MirViolation.Local);
                }
            }

            var blocks = body.Blocks;
            foreach (var row in body.MustNotComplete)
            {
                this.at = new(row.Subject, 0, StatementStart: false);
                if ((uint)row.Entry >= (uint)blocks.Length || (uint)row.End >= (uint)blocks.Length ||
                    !InRange(row.Start, row.Count, body.GuessedSites.Length) ||
                    blocks[row.End].Terminator is not { Kind: MirTerminatorKind.Unreachable, CheckTarget: -1 })
                {
                    this.Report(MirViolation.TargetRange);
                }
            }

            this.at = default;
            if (locals.Length == 0)
            {
                this.Report(MirViolation.Local);
            }

            var scopes = body.Scopes;
            for (var i = 0; i < scopes.Length; i++)
            {
                if (i == 0 ? scopes[i].Outer != -1 : (uint)scopes[i].Outer >= (uint)i)
                {
                    this.Report(MirViolation.Scope);
                    this.Unscoped = true;
                }
            }

            var walkable = true;
            foreach (var site in body.OpaqueSites)
            {
                var valid = InRange(site.Start, site.Count, body.OpaqueUses.Length) && InRange(site.Kept, site.Enclosing, site.Count);
                foreach (var use in valid ? body.OpaqueUses.Slice(site.Start, site.Count) : default)
                {
                    valid &= this.IsValid(use);
                }

                if (!valid)
                {
                    this.Report(MirViolation.Opaque);
                    walkable = false;
                }
            }

            var projections = body.Projections;
            for (var id = 1; id < projections.Length; id++)
            {
                ref readonly var row = ref projections[id];
                if (!MirDescriptors.Of(row.Kind).Defined || (uint)row.Prefix >= (uint)id)
                {
                    this.Report(MirDescriptors.Of(row.Kind).Defined ? MirViolation.Projection : MirViolation.UnknownKind);
                    walkable = false;
                }
                else if (row.Prefix != 0 && !this.Admits(projections[row.Prefix].Type, row))
                {
                    this.Report(MirViolation.Projection);
                }
            }

            return walkable;
        }

        // A Place a location names, whether MirUses reports it (Place) or not (a diverging Call's destination, Call): in range, else a Place
        // finding and false, and its first step admitted by its root's Type, else a Projection finding (Tables checks each later step
        // against the step it extends).
        private readonly bool Names(MirPlace place)
        {
            if (!this.IsValid(place))
            {
                this.Report(MirViolation.Place);
                return false;
            }

            var projections = body.Projections;
            var first = place.Projection;
            while (first != 0 && projections[first].Prefix != 0)
            {
                first = projections[first].Prefix;
            }

            if (first != 0 && !this.Admits(place.Root >= 0 ? body.Locals[place.Root].Type : body.Statics[~place.Root].Type, projections[first]))
            {
                this.Report(MirViolation.Projection);
            }

            return true;
        }

        // PLAN G1073, G1184 (SPEC 23.1.2, rule W): the accesses of an admitted Place of no Type that judge nothing of its Type: a write and
        // its Kills (its storage's beginning or end, an EndBorrow); and of a whole slot of no Type (Binding's failed slot, MirLocal) also the
        // Return's Move of _0, the delivery into _0 (a Result local's included, R4-M132), the placing of a write's value in transit
        // (MirLocalRole.Assigned) into a destination of no Type (placing), an Opaque site's undecided use (MirDescriptors.IsUndecided) and a
        // read of a temp the Unknown mask names by an rvalue that takes its Type from its operands (Propagates, the mask's rule); a read,
        // borrow, drop or case-dependent access other than these, which a case reader would judge (MirCases.TypeOf), is not one, so a Field
        // step of no Type (a stored Field whose slot failed, Admitted) is only written. MirCases.KnownType names the case readers of each
        // access admitted here.
        private readonly bool Bounded(MirPlace place, MirAccess access)
            => (access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows | MirAccess.DropUse | MirAccess.CaseDependent)) == 0 ||
                (place.Projection == 0 && (MirDescriptors.IsUndecided(access) || (access == MirAccess.Moves && ((this.returning && place.Root == 0) ||
                this.delivering || (this.placing && place.Root >= 0 && body.Locals[place.Root].Role == MirLocalRole.Assigned))) ||
                (this.propagating && (access & ~(MirAccess.Reads | MirAccess.Moves)) == 0 && place.Root >= 0 && unknown[place.Root])));

        // PLAN G1184 (the Z28 review's D1): whether a projected Place's last step is admitted by the Type of the Place it extends, its root's
        // for a first step (as Names checks it), so that a last step of no Type is a stored Field whose slot failed (MirBody.Selects); a
        // step that is not admitted is a Projection finding alone.
        private readonly bool Admitted(MirPlace place)
            => this.Admits(body.TypeOf(place with { Projection = body.Projections[place.Projection].Prefix }), body.Projections[place.Projection]);

        // The first Arg local a parameter is: past a receiver no signature slot names (a Closure's environment, a destructor's self), the
        // Arg locals beyond the signature's parameters (types.Signature); a default template's p_i are its parameters from local 1.
        private readonly int FirstParameter()
        {
            if (body.Key is not { Decl.Declaration: Parsing.FunctionKoto, Kind: not (MirBodyKind.DefaultTemplate or MirBodyKind.StaticInit) } key)
            {
                return 1;
            }

            _ = types.Signature(key.Decl, null, -1, out var count);
            var arguments = 0;
            while (arguments + 1 < body.Locals.Length && body.Locals[arguments + 1].Kind == MirLocalKind.Arg)
            {
                arguments++;
            }

            return 1 + Math.Max(arguments - count, 0);
        }

        // PLAN G1073 (SPEC 23.1.2): whether Binding failed the signature slot of _0 or an Arg local, which is then of no Type, and only then:
        // _0's the body's Result (the top-level statements, without a declaration, give Unit), a default template's p_i parameter i's (its
        // ref/P_i), another Arg's its parameter's by its position past the receiver (MirCallee.Slot): the slot Binding publishes
        // (types.Signature) and the builder reads.
        private readonly bool SlotFailed(int local, int first)
        {
            if (body.Key.Decl is not { } decl)
            {
                return false;
            }

            if (local == 0)
            {
                return this.Result(body.Key) is null;
            }

            var template = body.Key.Kind == MirBodyKind.DefaultTemplate;
            var position = local - (template ? 1 : first);
            return position >= 0 && decl.Declaration is Parsing.FunctionKoto &&
                types.Signature(decl, null, template ? position : MirCallee.Slot(decl, position), out _) is null;
        }

        private readonly bool IsValid(MirPlace place)
            => (place.Root >= 0 ? place.Root < body.Locals.Length : ~place.Root < body.Statics.Length) &&
                (uint)place.Projection < (uint)body.Projections.Length;

        // A step admitted by the Type of the Place it extends with the Type it selects (MirBody.Selects, the one projection rule).
        private readonly bool Admits(BoundType? type, in MirProjection row)
            => body.Selects(types, type, row.Kind, row.Arg, out var part) && ReferenceEquals(row.Type, part) &&
                (row.Position == -1 || (row.Position >= 0 && row.Kind == MirProjectionKind.Index && type?.Kind == BoundTypeKind.FixedArray));
    }
}
