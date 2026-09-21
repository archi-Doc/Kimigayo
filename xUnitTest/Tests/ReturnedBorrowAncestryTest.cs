// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ReturnedBorrowAncestryTest
{
    private const string Counter = "struct Counter\n    public var value: i32 = 1\nfunc relay(p: uniq/Counter) -> uniq{p}/Counter\n    return p\nfunc second(a: ref/Counter, b: uniq/Counter) -> uniq{b}/Counter\n    return b\n";

    [Theory]
    [InlineData("Update", "func change(p: uniq/Counter)\n    relay(p).value += 1\n    p.value += 1\nvar counter = Counter.init()\nchange(counter@uniq)\nrequire counter.value == 3 else => $abort(\"value\")")]
    [InlineData("Repeated", "func change(p: uniq/Counter)\n    relay(p).value = 5\n    relay(p).value += 2\n    let before = relay(p).value++\n    require before == 7 and p.value == 8 else => $abort(\"result\")\nvar counter = Counter.init()\nchange(counter@uniq)\nrequire counter.value == 8 else => $abort(\"value\")")]
    [InlineData("Nested", "func change(p: uniq/Counter)\n    relay(relay(p)).value += 1\n    p.value += 1\nvar counter = Counter.init()\nchange(counter@uniq)\nrequire counter.value == 3 else => $abort(\"value\")")]
    [InlineData("Slot", "func change(p: uniq/Counter, q: uniq/Counter)\n    second(q, p).value += 1\n    second(a: q, b: p).value += 1\n    p.value += q.value\nvar left = Counter.init()\nvar right = Counter.init()\nchange(left@uniq, right@uniq)\nrequire left.value == 4 and right.value == 1 else => $abort(\"value\")")]
    [InlineData("Local", "func change(p: uniq/Counter)\n    let r = relay(p)\n    r.value += 1\n    p.value += 1\nvar counter = Counter.init()\nchange(counter@uniq)\nrequire counter.value == 3 else => $abort(\"value\")")]
    public void ReturnedExclusiveEndsBeforeParentUse(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(Counter + source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("ReturnedBorrowAncestry" + name, Counter + source, string.Empty);
    }

    [Theory]
    [InlineData("func change(p: uniq/Counter)\n    let r = relay(p)\n    p.value += 1\n    r.value += 1")]
    [InlineData("func change(p: uniq/Counter)\n    let r = relay(p)\n    let s = relay(p)\n    r.value += 1")]
    [InlineData("func change(p: uniq/Counter)\n    relay(p).value += p.value")]
    [InlineData("func change(p: uniq/Counter, q: uniq/Counter)\n    let r = second(q, p)\n    p.value += 1\n    r.value += 1")]
    [InlineData("func change(p: uniq/Counter)\n    let r = relay(relay(p))\n    p.value += 1\n    r.value += 1")]
    public void RejectsParentUseWhileReturnedLoanLives(string source)
    {
        var c = MinimalEmissionTest.Analyze(Counter + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RejectsSharedToExclusiveEscalation()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "func change(p: ref/Counter)\n    relay(p).value += 1");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
