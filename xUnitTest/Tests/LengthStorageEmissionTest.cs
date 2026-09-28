// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class LengthStorageEmissionTest
{
    private const string Keep = "func keep<length N, T>(value: [N of T]) -> [N of T] => value@move\n";
    private const string Choose = "func choose<length N, T>(a: [N of T], b: [N of T], first: bool) -> [N of T] => if first => a@move else => b@move\n";
    private const string Token = "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => Console.writeLine(\"one\")\n        if self.id == 2 => Console.writeLine(\"two\")\n        if self.id == 3 => Console.writeLine(\"three\")\n        if self.id == 4 => Console.writeLine(\"four\")\n";

    [Theory]
    [InlineData(Keep + "let a = keep<2, i32>([4, 9])")]
    [InlineData(Keep + "let a: [2 of i32] = [4, 9]\nlet b = keep(a)")]
    [InlineData("func keep(value: [2 of i32]) -> [2 of i32] => value\nlet a = keep([4, 9])")]
    public void WarmArrayCallBindingAllocatesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Copy", Keep + "let a: [2 of i32] = [2, 7]\nlet b = keep<2, i32>(a)\nrequire a[0] + b[1] == 9 else => $abort(\"copy\")", string.Empty },
        { "Inferred", Keep + "let b = keep([2, 7])\nrequire b[1] == 7 else => $abort(\"inferred\")", string.Empty },
        { "Empty", Keep + "let b = keep<0, string>([])\nrequire b.length == 0 else => $abort(\"empty\")\nConsole.writeLine(\"empty\")", "empty\n" },
        { "Move", Keep + "let b = keep<2, string>([\"one\", \"two\"])\nConsole.writeLine(b[0])\nConsole.writeLine(b[1])", "one\ntwo\n" },
        { "Nested", Keep + "let a: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nlet b = keep(a)\nrequire b[1][0] == 3 and a[0][1] == 2 else => $abort(\"nested\")", string.Empty },
        { "Destruction", Token + Keep + "do\n    let b = keep<2, Token>([Token.init(1), Token.init(2)])\n    Console.writeLine(\"returned\")\nConsole.writeLine(\"done\")", "returned\ntwo\none\ndone\n" },
        { "Choice", Token + Choose + "do\n    let b = choose<2, Token>([Token.init(1), Token.init(2)], [Token.init(3), Token.init(4)], true)\n    Console.writeLine(\"returned\")", "four\nthree\nreturned\ntwo\none\n" },
        { "ChoiceSecond", Choose + "let b = choose([1, 2], [3, 4], false)\nrequire b[0] == 3 and b[1] == 4 else => $abort(\"choice\")", string.Empty },
        { "CheckedExpression", "func keep<length N>(value: [(N - 1) of i32]) -> [(N - 1) of i32] => value\nlet b = keep<3>([4, 9])\nrequire b[1] == 9 else => $abort(\"formation\")", string.Empty },
        { "Metadata", "func count<length N, T>(a: [N of T]) -> isize => a.length\nrequire count<3, i32>([1, 2, 3]) == 3 else => $abort(\"length\")", string.Empty },
        { "BorrowedMetadata", "func count<length N, T>(a: ref/[N of T]) -> isize => a.length\nlet a: [3 of i32] = [1, 2, 3]\nrequire count(a@ref) == 3 and a[2] == 3 else => $abort(\"borrow\")", string.Empty },
        { "EmptyMetadata", "func count<length N, T>(a: ref/[N of T]) -> isize => a.length\nlet a: [0 of string] = []\nrequire count(a@ref) == 0 else => $abort(\"empty\")", string.Empty },
        { "ZeroSizedMetadata", "func count<length N, T>(a: ref/[N of T]) -> isize => a.length\nlet a: [3 of ()] = [(), (), ()]\nrequire count(a@ref) == 3 else => $abort(\"zero stride\")", string.Empty },
        { "BorrowedMoveMetadata", Token + "func count<length N, T>(a: ref/[N of T]) -> isize => a.length\ndo\n    let a: [2 of Token] = [Token.init(1), Token.init(2)]\n    require count(a@ref) == 2 else => $abort(\"borrow\")\n    Console.writeLine(\"inspected\")", "inspected\ntwo\none\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("LengthStorage" + name, source, stdout);

    [Theory]
    [InlineData("func twice<length N, T>(a: [N of T]) -> [N of T]\n    let first = a@move\n    return a@move\nlet b = twice<2, i32>([1, 2])")]
    [InlineData("func twice<length N, T>(a: [N of T]) -> [N of T]\n    let first = a@move\n    return a@move\nConsole.writeLine(\"unused definition\")")]
    [InlineData(Keep + "let a: [1 of string] = [\"one\"]\nlet b = keep(a@move)\nlet c = keep(a@move)")]
    public void RejectsPotentialMovesBeforeEmission(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Ownership.Result.ErrorCount > 0);
        Assert.Equal(0, c.Ownership.Result.UnsupportedCount);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void MonomorphizesOneBodyPerLengthAndElementType()
    {
        // SPEC 21.3.1: one concrete body per closed substitution; the repeated keep<2, i32> reuses its body.
        var c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>([1, 2])\nlet b = keep<0, i32>([])\nlet c = keep<1, string>([\"x\"])\nlet d = keep<2, i32>([3, 4])");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.PendingEntries);
        Assert.Equal(3, GenericStorageEmissionTest.Instances(module).Length);
    }

    [Fact]
    public void LengthMetadataStillRequiresInitializedStorage()
    {
        var c = MinimalEmissionTest.Analyze("func count<length N, T>(a: ref/[N of T]) -> isize => a.length\nlet a: [2 of i32]\ncount(a@ref)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("receiver")]
    [InlineData("kind")]
    [InlineData("index")]
    [InlineData("producer")]
    public void RejectsMalformedMetadataPlansAndRecovers(string defect)
    {
        // BodyLowering validates every lowered body, including each monomorphized instance (SPEC 21.3.1);
        // the corrupt length-metadata plan is rejected on the ordinary body that owns it.
        var c = MinimalEmissionTest.Analyze("func count(a: ref/[2 of i32], b: ref/[2 of i32]) -> isize\n    let ignored = b.length\n    return a.length\nlet a: [2 of i32] = [1, 2]\ncount(a@ref, a@ref)");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "count");
        var index = body.Sequences.Count - 1;
        var sequence = body.Sequences[index];
        if (defect == "producer")
        {
            var value = body.Values[sequence.Operation];
            var other = body.Values[body.Sequences[0].Operation];
            body.ValueOperands[value.Start] = body.ValueOperands[other.Start];
        }
        else
        {
            body.Sequences[index] = defect switch
            {
                "receiver" => sequence with { Receiver = 0 },
                "kind" => sequence with { Kind = SequenceOperation.Read },
                _ => sequence with { Index = 0 },
            };
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void ReloadAndWarmVerificationPreserveSharedLengthPlans()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>([4, 9])\nrequire a[1] == 9 else => $abort(\"reload\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), MinimalEmissionTest.Describe(c, error));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("LengthStorageReload", writer.ToString(), string.Empty);
        // Measure the new length-call path independently of existing require-expression
        // analysis and shared-module construction (which allocate their own plans).
        c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>([4, 9])");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.Validate(out _));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Length storage Binding failed.");
            }
        }));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Length storage ownership failed.");
            }
        }));
    }
}
