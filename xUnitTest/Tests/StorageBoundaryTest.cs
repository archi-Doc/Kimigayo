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

    // The Kimigayo iterators execute natively over the boundary: shared items are read in index order, exclusive items
    // are retained together and written through, and both iterators stay exhausted after None.
    [Fact]
    public void ExplicitIterationExecutes()
    {
        const string Source = "public func main()\n    var values: Array<i32> = [1, 2, 3]\n    var total: i32 = 0\n    var shared = values.iterate()\n" +
            "    loop\n        match shared.next()\n            .Some(let v) => total += v\n            .None => exit\n    require total == 6 else => $abort(\"shared\")\n" +
            "    match shared.next()\n        .Some(_) => $abort(\"shared exhausted\")\n        .None => ()\n" +
            "    var exclusive = values.iterateUniq()\n    let first = exclusive.next()\n    let second = exclusive.next()\n" +
            "    match first@move\n        .Some(let r) => r@follow += 10\n        .None => $abort(\"first\")\n" +
            "    match second@move\n        .Some(let r) => r@follow += 20\n        .None => $abort(\"second\")\n" +
            "    match exclusive.next()\n        .Some(let r) => r@follow += 30\n        .None => $abort(\"third\")\n" +
            "    match exclusive.next()\n        .Some(_) => $abort(\"exhausted\")\n        .None => ()\n" +
            "    require values[0] == 11 and values[1] == 22 and values[2] == 33 else => $abort(\"values\")\n" +
            "    var empty: Array<i32> = []\n    var none = empty.iterateUniq()\n    match none.next()\n        .Some(_) => $abort(\"empty\")\n        .None => Console.writeLine(\"boundary\")";
        ScalarEmissionTest.EmitFixture("StorageBoundaryExplicit", Source, "boundary\n");
    }

    // The owning entry binds and verifies through the owning remainder; its lowering is a separate unit, so generation
    // still rejects it with a diagnosis instead of leaking the unreturned elements.
    [Fact]
    public void TheOwningEntryBindsButIsNotLoweredYet()
    {
        var c = MinimalEmissionTest.Analyze(Values + "var it = (values@move).intoIterator()\nlet p = it.next()\nlet q = it.next()\nmatch p\n    .Some(_) => ()\n    .None => ()\nmatch q\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out var error));
        Assert.Contains("owning storage boundary", error);
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
