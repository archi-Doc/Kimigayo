// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A5-F4's verdicts on the OriginProof families (2a0-D §10's 2a-4): the twin of OriginProofScalingTest and OriginObligationVerdictTest,
// which read the default pipeline's records and Binding's judge.
public sealed partial class MirOriginsTest
{
    // SPEC 15.3.4-15.3.7, 15.6.1, 15.6.5: on the MIR side a relation the visible premises prove is accepted and one they do not is one
    // UnprovenOriginRelation_Kd, Binding's record where it judges the relation and A5's where the body judges it (`atomic`).
    [Theory]
    [MemberData(nameof(OriginProofScalingTest.Families), MemberType = typeof(OriginProofScalingTest))]
    public void OriginProofFamiliesHaveTheirOutcomeOnTheMir(string family, string? code)
    {
        foreach (var size in new[] { 1, 2, 8 })
        {
            var c = MinimalEmissionTest.Analyze(Verification.OriginProofWorkloads.Create(family, size));
            var (common, _, _, mir) = MirDifferenceLedger.Sides(c, c.BuildMir());
            Assert.Equal(code is null ? [] : [code], common.Concat(mir).Select(static x => x.Code.ToString()));
        }
    }
}
