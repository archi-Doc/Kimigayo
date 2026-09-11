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

    private static int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundType?[] parameters, int argumentCount)
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
                var equal = true;
                for (var i = 0; i < argumentCount; i++)
                {
                    equal &= ReferenceEquals(parameters[(a * argumentCount) + i], parameters[(b * argumentCount) + i]);
                }

                var aGeneric = fa.GenericArguments.Count != 0;
                var bGeneric = fb.GenericArguments.Count != 0;
                if (!(equal && (aGeneric != bGeneric ? !aGeneric : fa.Parameters.Count < fb.Parameters.Count)))
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

    private readonly record struct EvaluatedCandidate(BindingSymbol Symbol, CandidateApplicability State, BoundType? DeclaringType);
}
