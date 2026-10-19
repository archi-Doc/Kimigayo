// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Pattern layer selection (SPEC 14.8.1, 3.4.3, 13.5.5, 15.1.6): the one step by which a Pattern position reaches the Place its
// Pattern applies to, from the Place its parent selected.
// - Responsibility: the layers a position selects (its ImplicitFollows) and what its path has reached after them
//   (PatternReach: the access past the layers and the dependency of the Place reached).
// - Inputs: the position's matched Type, whether its Pattern is structural, the parent's reach and the admitted sets of the pair
//   binders in scope.
// - Outputs: the selected Type, the number of layers and the reach after them.
// - Invariants: a structural position selects the referent of every safe value-reference layer that lacks its structure, a
//   qualifying pair layer included; a shared layer bounds every later access to Shared; a ref layer restarts the dependency at
//   its own Origin and a uniq layer meets it with the dependency reached so far.
// - Prohibitions: nothing here reports a diagnostic or records a position; BindPattern records what the step selected.
public sealed partial class Binding
{
    private BoundType SelectLayers(BoundType matched, bool structural, BindingScope scope, ref PatternReach reach, out int layers)
    {
        var type = matched;
        layers = 0;
        if (!structural)
        {
            return type;
        }

        // SPEC 14.8.1, 3.4.3: a structural Pattern selects the referent of every safe value-reference layer
        // that lacks its structure; a shared layer anywhere on the path bounds every descendant to shared access.
        while (true)
        {
            if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                // SPEC 10.2: a ref layer is a Copy that restarts the dependency at its own Origin; a uniq layer below it
                // is Reborrowed, so its Origin is met with the dependency reached so far.
                var origin = reach.Origin;
                var restarted = type.Semantics == SemanticsKind.Ref || origin is null ? type.Origin ?? origin : type.Origin is { } layer ? this.Meet(origin, layer) : origin;
                reach = new(type.Semantics == SemanticsKind.Ref || reach.Access == PatternAccessMode.Shared ? PatternAccessMode.Shared : PatternAccessMode.Exclusive, restarted);
                type = type.Components[0];
                layers++;
                continue;
            }

            var admitted = this.FollowablePair(type, scope, out var target);
            if (admitted == SemanticsMask.None)
            {
                return type;
            }

            // SPEC 14.8.1, 13.5.5: a qualifying pair layer is selected like the other safe value-reference layers. The access
            // past it is the weakest over its admitted cases; an admitted owner keeps the dependency reached so far.
            var reached = reach.Origin;
            var dependency = (admitted & SemanticsMask.Owner) != 0 ? reached : (admitted & SemanticsMask.Ref) != 0 || reached is null ? type.Origin ?? reached
                : type.Origin is { } stored ? this.Meet(reached, stored) : reached;
            reach = new((admitted & SemanticsMask.Ref) != 0 || reach.Access == PatternAccessMode.Shared ? PatternAccessMode.Shared : PatternAccessMode.Exclusive, dependency);
            type = target;
            layers++;
        }
    }

    /// <summary>What a Pattern path has reached (SPEC 13.5.5, 15.1.6): the access the Place reached so far grants, Owned before any
    /// layer, and the dependency of that Place.</summary>
    /// <param name="Access">The mode reached so far.</param>
    /// <param name="Origin">The dependency reached so far, or null when no layer gave one.</param>
    private readonly record struct PatternReach(PatternAccessMode Access, BoundOrigin? Origin);
}
