// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.5: the standard storage boundary splits Array Storage into non-overlapping regions, so the
/// standard exclusive iterator's items are independent children (SPEC 15.6.3, 22.1.2.4).</summary>
public class StorageBoundaryTest
{
    private const string Values = "var values: Array<i32> = [1, 2, 3]\n";

    [Theory]
    [InlineData("iterateUniq")]
    [InlineData("iterate")]
    public void ItemsOfEarlierStepsStayValid(string entry)
    {
        // Two items from the same iterator are live at once; the second step conflicts with neither the first item
        // nor the source Loan the iterator keeps.
        var c = MinimalEmissionTest.Analyze(Values + "var it = values." + entry + "()\nlet p = it.next()\nlet q = it.next()\nmatch p\n    .Some(_) => ()\n    .None => ()\nmatch q\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheSourceStaysProtectedWhileAnItemIsNeeded()
    {
        var c = MinimalEmissionTest.Analyze(Values + "var it = values.iterateUniq()\nlet p = it.next()\nvalues = [4]\nmatch p\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    // SPEC 9.3, 22.1.2.5: the boundary is internal to the Kimi Kotonoha; no user source reaches it.
    [Theory]
    [InlineData("let r = Kimi.Storage.borrowStorage(values@ref)")]
    [InlineData("let r = Storage.borrowStorage(values@ref)")]
    [InlineData("func f(r: Kimi.Storage.RefRemainder<Array<i32>>) => ()")]
    public void UserSourceCannotReachTheBoundary(string use)
    {
        var c = MinimalEmissionTest.Analyze(Values + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // The library's own conformances pass the Iterator effect bound: splitting off a child conflicts with no earlier
    // child, while a user iterator that reborrows its stored reference does not (IteratorOriginEffectsTest).
    [Fact]
    public void TheLibraryBindsWithItsStandardIterators()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"a\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Library.ValidateDeclarations(), "declarations: " + c.Library.InvalidDeclaration?.ToString().Split((char)10)[0]);
        Assert.True(c.Library.ValidateBoundDeclarations(), "bound: " + c.Library.InvalidDeclaration?.ToString().Split((char)10)[0]);
    }
}
