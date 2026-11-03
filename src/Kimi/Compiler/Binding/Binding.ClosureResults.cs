// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's inferred result as its signature publishes it (SPEC 15.8.2, 15.6.1, 15.6.5, 23.1; DECISIONS R4-M126 P8, R4-M152).
// - Responsibility: PublishedResult, the inferred result once the Closure is classified: each Origin position the meet of its admissible
//   sources, its receiver resolved (`env_i -> ρ` under a borrowed receiver, `ρ -> static` under `owner/Self`, ResolveReceiver), a
//   body-local source left out (BodyLocalIn: a local, a parameter's own storage, a temporary, an environment binding the call consumes),
//   the Closure's call result atom where a source was left out and none remains or one rests on a failure (an Unbound one), and static
//   where the position has no source; FitResultSources, each returned value fitted to it in the Closure's body, so a left-out source is
//   Refuted at the returned value; CallResultOrigin, that call result atom, which each call substitutes by its Unbound atom (R4-M118).
// - Inputs: the result Type with its local regions resolved (ResolveLocalResultOrigins), the classified receiver, the Closure's body and
//   its result context's sources.
// - Outputs: the published Type; the fits' records, through the binding-time fit and its recording (RecordOriginFitFailure).
// - Invariants: an admissible source stays, so its fit holds by the meet law; a region of the Closure's body with no source is no source;
//   a Function Type component keeps its own per-call Origins; a source with no Origin is never fitted.
// - Prohibitions: no record but a fit's; no judgment of a relation outside a fit; no second resolution of local regions.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.8.2, 15.6.5: the inferred result a classified Closure publishes, each Origin by PublishedOrigin; a Function Type component
    // keeps its own per-call Origins. A part's result depends on the part alone, so each distinct part is rewritten once per call
    // (rewrittenParts; DECISIONS BU-19, PLAN G309).
    private BoundType PublishedResult(FunctionKoto function, SemanticsKind receiver, ExecutableBody body, BoundType type)
        => this.PublishedParts(function, receiver, body, type, this.rewrittenParts.Begin(this.storageVersion));

    // A component's result (PublishedResult); a leaf is cheaper to rewrite again than to record.
    private BoundType PublishedPart(FunctionKoto function, SemanticsKind receiver, ExecutableBody body, BoundType part, uint query)
    {
        if (part.Components.Count == 0)
        {
            return this.PublishedParts(function, receiver, body, part, query);
        }

        if (!this.rewrittenParts.TryGet(part, query, out var result))
        {
            result = this.PublishedParts(function, receiver, body, part, query);
            this.rewrittenParts.Set(part, query, result);
        }

        return result!;
    }

    private BoundType PublishedParts(FunctionKoto function, SemanticsKind receiver, ExecutableBody body, BoundType type, uint query)
    {
        if (!type.CarriesAnyOrigin || type.Kind == BoundTypeKind.Function)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        var arguments = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = false;
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.PublishedPart(function, receiver, body, type.Components[i], query);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                arguments[i] = this.PublishedOrigin(function, receiver, body, type.OriginArguments[i]);
                changed |= !ReferenceEquals(arguments[i], type.OriginArguments[i]);
            }

            var origin = type.Origin is { } outer ? this.PublishedOrigin(function, receiver, body, outer) : null;
            changed |= !ReferenceEquals(origin, type.Origin);
            return changed
                ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, arguments.AsSpan(0, type.OriginArguments.Count))
                : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }
    }

    // One published position (R4-M126 P8): the meet of the admissible operands; the call result atom when one was left out and none
    // remains, or when one rests on a failure; static when there is no source.
    private BoundOrigin PublishedOrigin(FunctionKoto function, SemanticsKind receiver, ExecutableBody body, BoundOrigin origin)
    {
        origin = this.ResolveReceiver(origin, function, receiver);
        if (origin.Kind == OriginKind.Static)
        {
            return origin;
        }

        var kept = BoundOrigin.Static;
        var any = false;
        var omitted = false;
        var count = origin.Kind == OriginKind.Intersection ? origin.Operands.Count : 1;
        for (var i = 0; i < count; i++)
        {
            var operand = origin.Kind == OriginKind.Intersection ? origin.Operands[i] : origin;
            if (operand.Kind == OriginKind.Unbound)
            {
                return this.CallResultOrigin(function); // SPEC 23.1.2: a dependent of the failure it rests on.
            }

            if (RegionIn(operand, body))
            {
                continue; // A region of the Closure's body that kept no source.
            }

            if (BodyLocalIn(operand, body))
            {
                omitted = true;
                continue;
            }

            kept = any ? this.Meet(kept, operand) : operand;
            any = true;
        }

        if (any)
        {
            return kept;
        }

        return omitted ? this.CallResultOrigin(function) : BoundOrigin.Static;
    }

    // SPEC 15.8.2, 15.6.1, 23.1 (DECISIONS R4-M126 P8, OQ-F7-2 (a)): each returned value of the Closure's result context, its local
    // regions resolved as the published result's are, fitted to the published result in the Closure's body, as an annotated result's
    // return is fitted; a failure is recorded at the returned value: the `return` statement, or an expression body.
    private void FitResultSources(FunctionKoto function, ExecutableBody body, BoundType published)
    {
        if (!this.resultContexts.TryGetValue(function, out var context))
        {
            return;
        }

        var saved = this.EnterBody(body);
        try
        {
            for (var i = 0; i < context.Sources.Count; i++)
            {
                if (context.Sources[i] is not { CarriesAnyOrigin: true } source || context.SourceNodes[i] is not { } node)
                {
                    continue;
                }

                var resolved = this.ResolveLocalResultOrigins(source);
                if (!this.FitsTypeAt(resolved, published, node, out var failure) && !this.CheckLocalTypeUse(resolved, published, node))
                {
                    this.FailMismatch(node.Parent is JumpKoto jump ? jump : node, node, resolved, published, failure);
                }
            }
        }
        finally
        {
            this.formingBody = saved;
        }
    }

    // SPEC 15.8.2, 23.1: a Closure's call result atom: a published position no admissible source supplies, which each call substitutes by
    // its Unbound atom (Binding.ValueCalls), and the `closure` display `call result`.
    private BoundOrigin CallResultOrigin(FunctionKoto function) => this.OriginAtom(function, OriginKind.Projection, CallResultSlot);
}
