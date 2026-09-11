// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<EvaluatedCandidate> candidateScratch = new();

    private enum CandidateApplicability : byte
    {
        Inapplicable,
        Applicable,
        Pending,
        Error,
    }

    private static int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        for (var a = 0; a < candidates.Length; a++)
        {
            if (candidates[a].State != CandidateApplicability.Applicable)
            {
                continue;
            }

            var fa = (FunctionKoto)candidates[a].Symbol.Declaration;
            var dominates = true;
            for (var b = 0; b < candidates.Length; b++)
            {
                if (a == b || candidates[b].State != CandidateApplicability.Applicable)
                {
                    continue;
                }

                var fb = (FunctionKoto)candidates[b].Symbol.Declaration;
                var better = false;
                var worse = false;
                for (var i = 0; i < stride; i++)
                {
                    var x = operations[(a * stride) + i];
                    var y = operations[(b * stride) + i];
                    better |= x.Adaptation < y.Adaptation;
                    worse |= x.Adaptation > y.Adaptation;
                }

                if (worse)
                {
                    dominates = false;
                    break;
                }

                if (better)
                {
                    continue;
                }

                for (var i = 0; i < stride; i++)
                {
                    var x = operations[(a * stride) + i].ParameterType;
                    var y = operations[(b * stride) + i].ParameterType;
                    if (ReferenceEquals(x, y))
                    {
                        continue;
                    }

                    // Only existing operation-free Type relations participate here.
                    var xy = x is not null && y is not null && FitsType(x, y);
                    var yx = x is not null && y is not null && FitsType(y, x);
                    better |= xy && !yx;
                    worse |= !xy;
                }

                if (worse)
                {
                    dominates = false;
                    break;
                }

                if (better)
                {
                    continue;
                }

                var aGeneric = fa.GenericArguments.Count != 0;
                var bGeneric = fb.GenericArguments.Count != 0;
                if (!(aGeneric != bGeneric ? !aGeneric : candidates[a].DefaultsUsed < candidates[b].DefaultsUsed))
                {
                    dominates = false;
                    break;
                }
            }

            if (dominates)
            {
                return a;
            }
        }

        return -1;
    }

    private readonly record struct EvaluatedCandidate(BindingSymbol Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed);
}
