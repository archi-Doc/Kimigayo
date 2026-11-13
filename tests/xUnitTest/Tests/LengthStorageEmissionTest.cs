// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class LengthStorageEmissionTest
{
    private const string Keep = "func keep<length N, T>(value: [N of T]) -> [N of T] => value@move\n";
    private const string Choose = "func choose<length N, T>(a: [N of T], b: [N of T], first: bool) -> [N of T] => if first => a@move else => b@move\n";
    private const string Size = "func size<length N>(values: [N of i32]) -> isize => N\n";
    private const string Token = "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    drop\n        if self.id == 1 => Console.writeLine(\"one\")\n        if self.id == 2 => Console.writeLine(\"two\")\n        if self.id == 3 => Console.writeLine(\"three\")\n        if self.id == 4 => Console.writeLine(\"four\")\n";

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Keep + "let a = keep<2, i32>([4, 9])")]
    [InlineData(Keep + "let a: [2 of i32] = [4, 9]\nlet b = keep(a)")]
    [InlineData("func keep(value: [2 of i32]) -> [2 of i32] => value\nlet a = keep([4, 9])")]
    public void WarmArrayCallBindingAllocatesNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Bind().IsComplete);
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

        // SPEC 4.4.1, 4.4.2, 7.2.3, 7.7.2 (R8-W2 c2a): a length slot read as an isize value, each instance's (fail-before: Unsupported_Kd at
        // each read): in a body, two instances of one generic, a Constant-readable Binding (SPEC 4.4.2's example), a Closure, a default
        // omitted by a closed and by a generic caller, and an instance whose slot is its caller's.
        { "SlotValue", Size + "require size([1, 2, 3]) == 3 else => $abort(\"slot\")", string.Empty },
        { "SlotValueInstances", Size + "require size([1, 2, 3]) == 3 and size([1, 2]) == 2 else => $abort(\"instances\")", string.Empty },
        { "SlotValueBinding", "func f<length N>(a: [(N + 1) of u8]) -> isize\n    let k: isize = N + 1\n    let b: [k of u8] = a\n    return k * 10 + b.length\nrequire f<1>([2 of 0]) == 22 else => $abort(\"binding\")", string.Empty },
        { "SlotValueClosure", "func size<length N>(values: [N of i32]) -> isize\n    let read = func () -> isize => N\n    return read()\nrequire size([1, 2, 3]) == 3 else => $abort(\"closure\")", string.Empty },
        { "SlotValueDefault", "func count<length N>(values: [N of i32], n: isize = N) -> isize => n\nfunc outer<length M>(values: [M of i32]) -> isize => count(values)\nrequire count([1, 2]) == 2 and outer([1, 2, 3]) == 3 else => $abort(\"default\")", string.Empty },
        { "SlotValueNested", Size + "func twice<length M>(values: [M of i32]) -> isize => size(values) + M\nrequire twice([1, 2, 3]) == 6 else => $abort(\"nested\")", string.Empty },
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
        Assert.True(c.Analysis.ErrorCount > 0);
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    // SPEC 4.4.1, 23.1.2 (R8-W2 c2a's invalid twin): a length slot's Name outside its function resolves nothing, Binding's one record,
    // while the read inside is its value, no record (fail-before: Unsupported_Kd at the inside read too).
    [Fact]
    public void ALengthSlotReadOutsideItsFunctionIsUnresolved()
    {
        var source = Size + "let m: isize = N";
        var c = MinimalEmissionTest.Check(source);
        Assert.Equal([(nameof(DiagnosticCode.UnresolvedBinding_Kd), source.Length - 1)], TestDiagnostics.Of(c).Select(static x => (x.Code, x.Span.Start)));
    }

    [Fact]
    public void LengthMetadataStillRequiresInitializedStorage()
    {
        var c = MinimalEmissionTest.Analyze("func count<length N, T>(a: ref/[N of T]) -> isize => a.length\nlet a: [2 of i32]\ncount(a@ref)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.UninitializedPlace_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReloadAndWarmVerificationPreserveSharedLengthPlans()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>([4, 9])\nrequire a[1] == 9 else => $abort(\"reload\")");
        using var original = new StringWriter();
        Assert.True(c.Generate(original), MinimalEmissionTest.Describe(c));
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Analyze().IsVerified);
        using var writer = new StringWriter();
        Assert.True(restored.Generate(writer), MinimalEmissionTest.Describe(restored));
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("LengthStorageReload", writer.ToString(), string.Empty);
        // Measure the new length-call path independently of existing require-expression
        // analysis and shared-module construction (which allocate their own plans).
        c = MinimalEmissionTest.Analyze(Keep + "let a = keep<2, i32>([4, 9])");
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null));
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
            if (!c.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Length storage ownership failed.");
            }
        }));
    }
}
