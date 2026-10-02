// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 15.2.1, 15.3.1: a projection such as value.source names the Origin bound to a slot of the value's Type; it borrows nothing
// from the value's own storage. A result bound to the slot of an owned parameter keeps the caller's Loan of that slot, so the
// parameter itself may be moved into the result, while the caller still keeps the Loan the slot carries.
public class OriginProjectionOwnershipTest
{
    private const string View = "public struct View<T> {source}\n    public let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n";

    [Theory]
    [InlineData("func forward<T>(value: View<T>) -> View<T>{result}\n    origin result.source == value.source\n    return value@move\n")]
    [InlineData("func single<T>(value: View<T>) -> View<T> during value.source\n    return value@move\n")]
    [InlineData("func pair<T>(value: View<T>, other: View<T>) -> (View<T>{a}, View<T>{b})\n    origin a.source == value.source\n    origin b.source == other.source\n    return (value@move, other@move)\n")]
    public void AResultBoundToAnOwnedInputSlotMayMoveTheInput(string function)
    {
        var c = MinimalEmissionTest.Analyze(View + function + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // The caller still keeps the Loan the slot carries through the moved value.
    [Fact]
    public void TheCallerKeepsTheSlotLoan()
    {
        const string Main = "func single<T>(value: View<T>) -> View<T> during value.source\n    return value@move\n" +
            "public func main()\n    var number = 1\n    let view = View<i32>.init(number@ref)\n    let moved = single(view@move)\n    number = 2\n    let n = moved.data@follow\n";
        var c = MinimalEmissionTest.Analyze(View + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        c.Ownership.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && x.Text == "number = 2");
    }

    [Fact]
    public void AMovedInputKeepsItsReferentAtRunTime()
    {
        const string Source = "func single<T>(value: View<T>) -> View<T> during value.source\n    return value@move\n" +
            "var number = 41\nlet view = View<i32>.init(number@ref)\nlet moved = single(view@move)\nrequire moved.data@follow == 41 else => $abort(\"projection\")\nConsole.writeLine(\"projected\")";
        ScalarEmissionTest.EmitFixture("OriginProjectionMovedInput", View + Source, "projected\n");
    }

    [Fact]
    public void TheCallerMayWriteAfterTheResultEnds()
    {
        const string Main = "func single<T>(value: View<T>) -> View<T> during value.source\n    return value@move\n" +
            "public func main()\n    var number = 1\n    let view = View<i32>.init(number@ref)\n    let moved = single(view@move)\n    let n = moved.data@follow\n    number = 2\n";
        var c = MinimalEmissionTest.Analyze(View + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }
}
