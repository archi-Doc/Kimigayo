// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedMemberCallEmissionTest
{
    private const string Counter = "struct Counter\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func add(self: uniq/Self, amount: i32) => self.value += amount\n    public func read(self: ref/Self) -> i32 => self.value\n";

    [Theory]
    [InlineData("ConstructBorrow", Counter + "func run<T>(value: ref/T) -> i32\n    var c = Counter.init(1)\n    c.add(2)\n    return c.read()\nlet v = true\nrequire run(v) == 3 else => $abort(\"result\")", "")]
    [InlineData("TemporaryReceiver", Counter + "func make() -> Counter\n    Console.writeLine(\"made\")\n    return Counter.init(42)\nfunc run<T>(value: ref/T) -> i32 => make().read()\nlet v = true\nrequire run(v) == 42 else => $abort(\"result\")", "made\n")]
    [InlineData("ExternalReceiver", Counter + "func run<T>(value: ref/T, c: uniq/Counter) -> i32\n    c.add(2)\n    return c.read()\nlet v = true\nvar c = Counter.init(40)\nrequire run(v, c) == 42 and c.read() == 42 else => $abort(\"result\")", "")]
    [InlineData("Empty", "struct Empty\n    public init() => ()\n    public func answer(self: Self) -> i32 => 42\nfunc run<T>(value: ref/T) -> i32 => Empty.init().answer()\nlet v = true\nrequire run(v) == 42 else => $abort(\"result\")", "")]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("SharedMemberCall" + name, source, stdout);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedReceiverTransfersAndCleansStringFieldsOnce(bool named)
    {
        const string Source = "struct Pair\n    let first: string\n    let second: string\n    public init(first: string, second: string)\n        self.first = first\n        self.second = second\n    public func take(self: Self) -> string => self.first\nfunc run<T>(value: ref/T) -> string\n    let pair = Pair.init(\"first\", \"second\")\n    return pair.take()\nlet v = true\nConsole.writeLine(run(v))";
        var source = named ? Source.Replace("func run<T>", "func word(text: string) -> string\n    Console.writeLine(\"arg\")\n    return text\nfunc run<T>").Replace("Pair.init(\"first\", \"second\")", "Pair.init(second: word(\"second\"), first: word(\"first\"))") : Source;
        var name = named ? "SharedMemberCallOwnedNamed" : "SharedMemberCallOwned";
        var stdout = named ? "arg\narg\nfirst\n" : "first\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, named ? "first=1;second=1;arg=2" : "first=1;second=1", order: named ? [2, 2, 1, 0] : [1, 0]);
    }

    [Fact]
    public void ExclusiveReceiverAllowsPreparedSharedInspection()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "func run<T>(value: ref/T, c: uniq/Counter)\n    c.add(c.read())\nlet v = true\nvar c = Counter.init(1)\nrun(v, c)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void CorruptReborrowAuthorityCannotProduceIr()
    {
        var c = MinimalEmissionTest.Analyze(Counter + "func run<T>(value: ref/T, c: uniq/Counter) => c.add(1)\nlet v = true\nvar c = Counter.init(1)\nrun(v, c)");
        Assert.True(c.Emission.TryPrepare(out _, out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.GenericArguments.Count != 0);
        var borrow = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Borrow);
        Assert.True(borrow >= 0);
        body.OperationStorage[borrow] = body.Operations[borrow] with { LoanMode = LoanRequirement.Ref };
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Equal(string.Empty, output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }
}
