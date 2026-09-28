// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3: an omitted default is evaluated once per omission, after the explicit arguments and in
// parameter order, in the declaration's scope; a default may call an ordinary function, effects included.
public class DefaultCallTest
{
    private const string DefaultIndex = "func defaultIndex() -> isize\n    Console.writeLine(\"Default index evaluated.\")\n    return 2\n";

    private const string Order = DefaultIndex + "func note(v: isize) -> isize\n    Console.writeLine(\"Explicit evaluated.\")\n    return v\n" +
        "func get(a: isize, index: isize = defaultIndex()) -> isize => index + a\n" +
        "require get(note(1)) == 3 and get(note(1), index: 5) == 6 and get(note(2)) == 4 else => $abort(\"defaults\")\nConsole.writeLine(\"done\")";

    private const string Generic = DefaultIndex + "func pick<length N, T>(values: ref/[N of T] during source, index: isize = defaultIndex()) -> ref/T during source => values[index]@ref/T\n" +
        "func forward<length N, T>(values: ref/[N of T] during source) -> ref/T during source => pick<N, T>(values)\n" +
        "let numbers: [3 of i32] = [10, 20, 30]\nrequire forward(numbers@ref) == 30 and pick(numbers@ref, 0) == 10 else => $abort(\"generic\")";

    [Fact]
    public void EvaluatesCallDefaultsOncePerOmissionAfterExplicitArguments()
        => ScalarEmissionTest.EmitFixture("DefaultCallOrder", Order, "Explicit evaluated.\nDefault index evaluated.\nExplicit evaluated.\nExplicit evaluated.\nDefault index evaluated.\ndone\n");

    [Fact]
    public void CallDefaultsReadPrecedingScalarSlots()
        => ScalarEmissionTest.EmitFixture(
            "DefaultCallPrecedingSlot",
            "func twice(n: isize) -> isize => n * 2\nfunc get(a: isize, index: isize = twice(a)) -> isize => index\nrequire get(3) == 6 and get(3, index: 1) == 1 else => $abort(\"slots\")",
            string.Empty);

    [Fact]
    public void GenericBodiesEvaluateOmittedCallDefaults()
        => ScalarEmissionTest.EmitFixture("DefaultCallGeneric", Generic, "Default index evaluated.\n");

    [Fact]
    public void BorrowingCallDefaultsStayGuardedWithoutInternalIssues()
    {
        // A default call with a borrowed argument is still recorded as unsupported; the partial graph it
        // leaves must not cascade into an internal invariant issue (SPEC 21.3.5).
        var c = MinimalEmissionTest.Analyze("func g(r: ref/i32) -> isize => 0\nfunc f(x: i32, i: isize = g(x@ref)) -> isize => i\ndo\n    let n = f(1)\n    require n == 0 else => $abort(\"n\")\n    Console.writeLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Internal);
    }
}
