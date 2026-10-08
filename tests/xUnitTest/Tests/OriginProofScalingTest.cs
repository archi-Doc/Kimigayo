// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Verification;
using Xunit;

namespace XunitTest;

public class OriginProofScalingTest
{
    public static TheoryData<string, string?> Families
    {
        get
        {
            var data = new TheoryData<string, string?>();
            foreach (var (family, code) in OriginProofWorkloads.Families)
            {
                data.Add(family, code);
            }

            return data;
        }
    }

    // SPEC 15.3.4-15.3.7, 15.6.1: a relation the visible premises prove is accepted and one they do not prove is one
    // UnprovenOriginRelation_Kd, at every size of the family.
    [Theory]
    [MemberData(nameof(Families))]
    public void FamiliesHaveTheirSpecifiedOutcome(string family, string? code)
    {
        foreach (var size in new[] { 1, 2 })
        {
            var output = DiagnosticCorpus.Check(OriginProofWorkloads.Create(family, size));
            Assert.Equal(code is null ? [] : [code], output.Diagnostics.Select(static x => x.Code));
        }
    }
}
