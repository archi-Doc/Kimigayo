// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class GenericCaptureStorageTest
{
    private const string Take = "func take<length N, T>(values: [N of T]) -> [N of T]\n    let action = func [values@move] () -> [N of T] => values@move\n    return action@move()\n";
    private const string Replace = "func replace<T>(value: uniq/T, next: T) -> T\n    T is Copy\n    var action = func [value@move] (item: T) -> T\n        let previous = value@follow\n        value@follow = item\n        return previous\n    return action(next)\n";

    [Theory]
    [InlineData("Scalars", "let values = take([2, 4, 6])\nrequire values[2] == 6 else => $abort(\"array\")", "")]
    [InlineData("Strings", "let values = take([\"first\", \"second\"])\nConsole.writeLine(values[1])", "second\n")]
    [InlineData("Empty", "let values: [0 of string] = []\nlet result = take(values@move)\nrequire result.length == 0 else => $abort(\"empty\")", "")]
    public void LengthAndTypeSubstitutionsDetermineCaptureStorage(string name, string body, string output)
        => ScalarEmissionTest.EmitFixture("GenericCaptureStorage" + name, Take + body, output);

    [Fact]
    public void ExclusiveGenericCapturesRetainReferentAuthority()
    {
        const string Source = Replace + "var value = 7\nrequire replace(value@uniq, 9) == 7 and value == 9 else => $abort(\"exclusive\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureStorageExclusive", Source, string.Empty);
    }

    [Fact]
    public void GenericIdentityRetainsConcreteEnvironmentContext()
    {
        const string Source = "func identity<F>(value: F) -> F => value@move\nfunc take<T>(value: T) -> T\n    let original = func [value@move] () -> T => value@move\n    let action = identity(original@move)\n    return action@move()\nrequire take(7) == 7 else => $abort(\"forwarded\")\nConsole.writeLine(take(\"owned\"))";
        ScalarEmissionTest.EmitFixture("GenericCaptureStorageForwarded", Source, "owned\n");
    }

    [Fact]
    public void SharedGenericCapturesCopyTheBorrowAndReadTheReferent()
    {
        const string Source = "func inspect<T>(value: ref/T) -> T\n    T is Copy\n    let action = func [value] () -> T => value@follow\n    return action()\nlet value = 7\nrequire inspect(value@ref) == 7 else => $abort(\"shared\")";
        ScalarEmissionTest.EmitFixture("GenericCaptureStorageShared", Source, string.Empty);
    }

    [Fact]
    public void MovedBorrowCannotBeReadThroughItsOldBinding()
    {
        var source = Replace.Replace("    return action(next)", "    _ = value@follow\n    return action(next)", StringComparison.Ordinal) + "var value = 7\n_ = replace(value@uniq, 9)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        c.Ownership.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "MovedPlace_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLengthAndBorrowPlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Take + Replace + "let values = take([\"first\", \"second\"])\nvar value = 1\n_ = replace(value@uniq, 2)");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
