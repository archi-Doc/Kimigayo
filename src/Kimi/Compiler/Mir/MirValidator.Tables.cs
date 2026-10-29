// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A body's tables and the Places that index them (plan 5.4).
// - Responsibility: the locals (local 0 the Return local, the Arg locals directly after it, each of a known kind and typed, a Result
//   local a Temp of local 0's Type), the MustNotComplete rows, the scopes, the Opaque sites and the projection table, each in range and
//   each projection step admitted by the Type of the step it extends (Tables, Admits); and each Place MirUses reports (Place): in range
//   (IsValid), its first step admitted by its root's Type, and a Result local read only by the whole Move that delivers it into _0
//   (DECISIONS R4-M132).
// - Inputs: the body's tables; each location's Places with their access. Outputs: Local, TargetRange, Scope, Opaque, Projection,
//   UnknownKind and Place findings; a written root's write bits for G131 and G136 (MirValidator.Diverged, MirValidator.Inputs); a valid
//   Place for G191's rule of its static root (MirValidator.Statics) and for the shared layers it passes (MirValidator.SharedPaths).
// - Invariants: Tables returns false exactly when a projection chain or an Opaque site cannot be walked, and Validate then stops; a
//   scope chain that cannot be walked marks the body Unscoped, so Validate runs every check but Entries and SharedPath.
// - Prohibitions: no rule of the operation a location performs (MirValidator.Blocks, .Rvalues, .Calls) and no Type agreement.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // The table checked each step against the step it extends; the first step is checked against the root's Type.
        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (!this.IsValid(place))
            {
                this.Report(MirViolation.Place);
                return;
            }

            this.Accessed(place);
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

            this.SharedPath(place, access, kind);
        }

        // Locals, MustNotComplete rows, scopes, Opaque sites and the projection table; false when a chain or site cannot be walked.
        internal bool Tables()
        {
            var locals = body.Locals;
            var arguments = true;
            var constructor = body.Key.Kind == MirBodyKind.Fn && AdtDef.IsConstructor(body.Key.Decl); // Its _0 is the receiver (BU-48).
            for (var i = 0; i < locals.Length; i++)
            {
                ref readonly var local = ref locals[i];
                this.At(local.Source);
                arguments &= i == 0 || local.Kind == MirLocalKind.Arg;
                var typed = local.Type is { } type && !TyCtxt.StatesUnstated(type, parts); // Unstated marks kept parts only (DECISIONS BU-46).
                var placed = (local.Kind == MirLocalKind.Return) == (i == 0) && (local.Kind != MirLocalKind.Arg || arguments);
                if (!typed || !placed || local.Kind > MirLocalKind.DropFlag ||
                    (local.Role == MirLocalRole.Result && (local.Kind != MirLocalKind.Temp || !ReferenceEquals(local.Type, locals[0].Type))) ||
                    (local.Role == MirLocalRole.Receiver) != (i == 0 && constructor))
                {
                    this.Report(MirViolation.Local);
                }
            }

            var blocks = body.Blocks;
            foreach (var row in body.MustNotComplete)
            {
                this.at = new(row.Subject, 0, StatementStart: false);
                if ((uint)row.Entry >= (uint)blocks.Length || (uint)row.End >= (uint)blocks.Length ||
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
                var valid = InRange(site.Start, site.Count, body.OpaqueRoots.Length);
                foreach (var root in valid ? body.OpaqueRoots.Slice(site.Start, site.Count) : default)
                {
                    valid &= this.IsValid(new(root));
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

        private readonly bool IsValid(MirPlace place)
            => (place.Root >= 0 ? place.Root < body.Locals.Length : ~place.Root < body.Statics.Length) &&
                (uint)place.Projection < (uint)body.Projections.Length;

        private readonly bool Admits(BoundType? type, in MirProjection row) => row.Type is not null && ReferenceEquals(
            row.Type,
            body.ProjectedType(type, row.Kind, row.Arg));
    }
}
