// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 21.3.5: an implementation invariant that decides an analysis outcome is checked in every
// configuration and reported as an internal issue, never left to a Debug-only assertion.
public class InternalInvariantTest
{
    [Fact]
    public void CompletionDisagreementIsAnInternalIssue()
    {
        var c = MinimalEmissionTest.Analyze("func f() -> i32 => 1\nlet n = f()");
        Assert.True(c.Ownership.Result.IsVerified);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "f");
        Assert.Empty(body.Issues);

        // A construct recorded as completing whose exit the ownership graph never reaches.
        body.RecordCompletion(0, -1, true);
        body.CheckUnreachable();
        var issue = Assert.Single(body.Issues);
        Assert.Equal(OwnershipFailure.Internal, issue.Failure);
        Assert.Same(body.Operations[0].Source, issue.Source);
        body.ResetCompletion();
    }
}
