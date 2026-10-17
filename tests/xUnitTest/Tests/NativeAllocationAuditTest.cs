// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The native allocation audit fails closed: a reference its interposer cannot redirect, or a bound without the
/// transfer anchor, fails instead of escaping the counters; the unchanged control is a written fixture.</summary>
public class NativeAllocationAuditTest
{
    [Fact]
    public void ControlIsAudited()
        => Audit("Control");

    [Theory]
    [InlineData("HeapAlloc")]
    [InlineData("HeapFree")]
    [InlineData("__kimi_exit")]
    public void UnredirectedReferenceFails(string symbol)
        => Assert.ThrowsAny<XunitException>(() => Audit("Stray", transformIr: ir => ir + "@audit_stray = private constant ptr @" + symbol + "\n"));

    [Fact]
    public void BoundWithoutTransferAnchorFails()
        => Assert.ThrowsAny<XunitException>(() => Audit("Unanchored", bound: 16));

    // One Array growth: one 16-byte allocation and its release.
    private static void Audit(string name, long bound = -1, Func<string, string>? transformIr = null)
    {
        const string Source = "var values: Array<i32> = []\nvalues@uniq.append(1)";
        NativeAllocationAudit.WriteFixture("NativeAllocationAudit" + name, Source, 1, 1, 16, maxTransferredBytes: bound, transformIr: transformIr);
    }
}
