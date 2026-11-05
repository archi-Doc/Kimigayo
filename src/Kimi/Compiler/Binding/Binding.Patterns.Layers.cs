// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Pattern layer selection (SPEC 14.8.1, 3.4.3, 13.5.5, 14.6.2, 15.1.6): the one step by which a Pattern position of a match arm
// or a for decomposition reaches the Place its Pattern applies to, from the Place its parent selected, and the Type a Binding
// position binds there.
// - Responsibility: the layers a position selects (its ImplicitFollows) and what its path has reached after them (PatternReach:
//   the access past the layers, the dependency of the Place reached and whether it may lie in the owned Subject or item).
// - Inputs: the position's matched Type, whether its Pattern is structural, the parent's reach, the plan (its Subject Place
//   and whether it extracts a value, SPEC 17.3) and the admitted sets of the pair binders in scope.
// - Outputs: the selected Type, the number of layers and the reach after them; BindingTypeOf.
// - Invariants: a structural position selects the referent of every safe value-reference layer that lacks its structure, a
//   qualifying pair layer included; a Binding (structural: false) selects only a qualifying pair layer it reaches with owned
//   access, which offers no Take (13.5.5), and then binds a reference to the layer's selected Place in the mode past it; a
//   Wildcard, a for slot `_` included, and a grouping select nothing and take no step (14.8.1); a shared layer bounds every
//   later access to Shared; a ref layer restarts the dependency at its own Origin and a uniq layer meets it; a pair layer's
//   dependency is PairSlot's, the Place reached being the owned Subject Place at the root of an owned Subject or item; a Place
//   stays an owned part only through pair layers admitting owner.
// - Prohibitions: nothing here reports a diagnostic or records a position; BindPattern and BindIteration record what the step
//   selected and judge it.
public sealed partial class Binding
{
    private BoundType SelectLayers(BoundMatch plan, BoundType matched, bool structural, BindingScope scope, ref PatternReach reach, out int layers)
    {
        var type = matched;
        layers = 0;
        while (true)
        {
            if (structural && type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                // SPEC 10.2: a ref layer is a Copy that restarts the dependency at its own Origin; a uniq layer below it
                // is Reborrowed, so its Origin is met with the dependency reached so far.
                var origin = reach.Origin;
                var readOnly = type.Semantics == SemanticsKind.Ref || reach.Access == PatternAccessMode.Shared;
                var restarted = this.MeetLayer(origin, type.Origin, shared: type.Semantics == SemanticsKind.Ref);
                reach = new(readOnly ? PatternAccessMode.Shared : PatternAccessMode.Exclusive, restarted, OwnedPart: false);
                type = type.Components[0];
                layers++;
                continue;
            }

            // SPEC 13.5.5, 14.8.1: a Binding selects nothing, except the qualifying pair layer of a Place it would take by value,
            // since the layer offers no Take; a try's binding extracts a value (SPEC 17.3).
            if (!structural && (reach.Access != PatternAccessMode.Owned || plan.ExtractsValue))
            {
                return type;
            }

            var admitted = this.FollowablePair(type, scope, out var target);
            if (admitted == SemanticsMask.None)
            {
                return type;
            }

            // SPEC 14.8.1, 13.5.5: a qualifying pair layer is selected like the other safe value-reference layers. The access past it
            // is the weakest over its admitted cases; its dependency is PairSlot's: the Place reached, which a part of an owned Subject
            // or item reached by no layer has in its Subject Place, or the slot met with the borrows reached so far.
            var slot = this.PairSlot(type, admitted, out var met);
            var dependency = (slot, reach.Origin) switch
            {
                (null, null) when reach.OwnedPart => this.OriginAtom(plan.Syntax, OriginKind.Projection, 0),
                (null, var reached) => reached,
                ({ } stored, { } reached) when met => this.Meet(reached, stored),
                _ => slot,
            };
            var shared = (admitted & SemanticsMask.Ref) != 0 || reach.Access == PatternAccessMode.Shared;
            reach = new(shared ? PatternAccessMode.Shared : PatternAccessMode.Exclusive, dependency, reach.OwnedPart && (admitted & SemanticsMask.Owner) != 0);
            type = target;
            layers++;
        }
    }

    // SPEC 5.7: a Case payload's `during self` layer read at a position: through the layer the position is reached by, in its mode,
    // else through the Subject's path (ReadHolder); null after a located Unsupported_Kd at `node`.
    private BoundType? PayloadRead(BoundType stored, BoundMatch plan, in PatternReach reach, Koto node)
    {
        if (reach.Access == PatternAccessMode.Owned || !this.HoldsHolder(stored))
        {
            return this.ReadHolder(stored, plan.Syntax.Expression, node);
        }

        return this.ReplaceHolder(stored, reach.Origin);
    }

    // SPEC 15.1.6 (Bindings): a part of an owned Subject or item reached with owned access is bound as its stored Type; a Place reached
    // with shared or exclusive access binds ref/T or uniq/T of the Place the step selected, also for a Copy T.
    private BoundType BindingTypeOf(BoundType selected, in PatternReach reach)
        => reach.Access == PatternAccessMode.Owned ? selected : this.types.Intern(
            BoundTypeKind.Semantics, null, reach.Access == PatternAccessMode.Shared ? SemanticsKind.Ref : SemanticsKind.Uniq, [selected], origin: reach.Origin);

    /// <summary>What a Pattern path has reached (SPEC 13.5.5's mode M and dependencies p, 15.1.6): the access the Place reached so far
    /// grants, Owned before any layer; the dependency of that Place; and whether it may lie in the owned Subject or iteration item.</summary>
    /// <param name="Access">The mode reached so far.</param>
    /// <param name="Origin">The dependency reached so far, or null when no layer gave one.</param>
    /// <param name="OwnedPart">Whether the Place may lie in the owned Subject or item (<see cref="BoundPattern.OwnedPart"/>).</param>
    private readonly record struct PatternReach(PatternAccessMode Access, BoundOrigin? Origin, bool OwnedPart);
}
