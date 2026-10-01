// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<EvaluatedCandidate> candidateScratch = new();
    private readonly ScratchBuffers<ArgumentAcquisition> acquisitionScratch = new();

    // SPEC 10.2.2: the two plans for a bare Place argument that the call site does not distinguish.
    private enum ArgumentAcquisition : byte
    {
        Other,

        /// <summary>The bare by-value acquisition of SPEC 3.5: a Copy that needs Copy proof.</summary>
        Copy,

        /// <summary>A new shared borrow of the same Place, the first row of SPEC 10.2.</summary>
        SharedBorrow,
    }

    private enum CandidateApplicability : byte
    {
        Inapplicable,
        Applicable,
        Pending,
        Error,

        /// <summary>Applicable except for a held bare Copy whose proof failed: it takes part in the conflict check only (SPEC 10.2.2).</summary>
        CopyUnproven,
    }

    private static int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        // SPEC 10.4: the receiver acquisition is common to the group (one receiver shape per Name, SPEC 7.3),
        // so Best Candidate compares the explicit arguments only; the receiver occupies the last slot.
        var compared = stride - 1;
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
                for (var i = 0; i < compared; i++)
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

                for (var i = 0; i < compared; i++)
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

    // SPEC 10.2.2 step 2: an argument at which one remaining candidate plans the bare by-value acquisition and another the new
    // shared borrow of the same Place is a conflict that ranking never resolves. One pass per argument over the plans; the
    // explanation is recorded in reused stores only when the call fails.
    private bool CheckAcquisitionConflicts(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, ArgumentAcquisition[] plans, int argumentCount, BoundArgumentOperation[] operations, int stride, BoundType?[] proofTypes, bool[] proven)
    {
        var start = this.acquisitionConflictStore.Count;
        for (var i = 0; i < argumentCount; i++)
        {
            var copy = false;
            var borrow = false;
            for (var c = 0; c < candidates.Length; c++)
            {
                if (candidates[c].State is CandidateApplicability.Applicable or CandidateApplicability.CopyUnproven)
                {
                    var plan = plans[(c * argumentCount) + i];
                    copy |= plan == ArgumentAcquisition.Copy;
                    borrow |= plan == ArgumentAcquisition.SharedBorrow;
                }
            }

            if (!(copy && borrow))
            {
                continue;
            }

            var partyStart = this.acquisitionPartyStore.Count;
            for (var c = 0; c < candidates.Length; c++)
            {
                var plan = plans[(c * argumentCount) + i];
                if (candidates[c].State is CandidateApplicability.Applicable or CandidateApplicability.CopyUnproven && plan != ArgumentAcquisition.Other)
                {
                    var operation = operations[(c * stride) + i];
                    this.acquisitionPartyStore.Add(new((FunctionKoto)candidates[c].Symbol.Declaration, operation.ParameterIndex, operation.ParameterType, plan == ArgumentAcquisition.Copy));
                }
            }

            var argument = call.ArgumentNodes[i];
            this.acquisitionConflictStore.Add(new(argument, argument.BoundType!, proofTypes[i] is not null && proven[i], OffersTake(argument), partyStart, this.acquisitionPartyStore.Count - partyStart));
        }

        var found = this.acquisitionConflictStore.Count - start;
        if (found == 0)
        {
            return false;
        }

        this.acquisitionConflicts[call] = (start, found);
        return true;
    }

    // SPEC 10.2.2 step 3: one Copy proof per argument and Type, shared by every candidate that holds it.
    private bool HeldCopyProven(int argument, BoundType type, Koto source, BoundType?[] proofTypes, bool[] proven)
    {
        if (!ReferenceEquals(proofTypes[argument], type))
        {
            proofTypes[argument] = type;
            proven[argument] = this.ProveCopy(type, source) == ConstraintProof.Proven;
        }

        return proven[argument];
    }

    private readonly record struct EvaluatedCandidate(BindingSymbol Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed);
}
