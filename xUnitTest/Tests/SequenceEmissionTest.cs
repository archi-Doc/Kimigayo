// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SequenceEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Indices", "var a: [3 of i32] = [10, 20, 12]\nvar sum = 0\nfor i in a.indices\n    sum += a[i]\nrequire sum == 42 else => $abort(\"sum\")\nwriteLine(\"ok\")", "ok\n" },
        { "Empty", "let a: [0 of i32] = []\nfor i in a.indices => $abort(\"iteration\")\nlet s = a[..]\nrequire s.isEmpty and s.length == 0 else => $abort(\"empty\")\nfor i in s.indices => $abort(\"slice iteration\")\nwriteLine(\"ok\")", "ok\n" },
        { "Metadata", "let a: [2 of string] = [\"a\", \"b\"]\nlet r = a.indices\nlet copy = r\nrequire r.start == 0 and r.end == 2 and r.length == 2 and not r.isEmpty else => $abort(\"range\")\nrequire a.length == 2 else => $abort(\"length\")\nwriteLine(a[0])\nwriteLine(a[1])", "a\nb\n" },
        { "Snapshot", "var a: [2 of i32] = [1, 2]\nlet r = a.indices\na = [20, 22]\nvar total = 0\nfor i in r\n    a[i] += 1\n    total += a[i]\nrequire total == 44 else => $abort(\"snapshot\")\nwriteLine(\"ok\")", "ok\n" },
        { "Once", "func make() -> [2 of i32]\n    writeLine(\"once\")\n    return [1, 2]\nfor i in make().indices => writeLine(\"step\")", "once\nstep\nstep\n" },
        { "Nested", "var a: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nvar n: isize = 0\nouter: for i in a.indices\n    defer => writeLine(\"row\")\n    for j in a[i].indices\n        n += 1\n        if i == 0 => continue to outer\n        exit to outer\nrequire n == 2 else => $abort(\"count\")", "row\nrow\n" },
        { "Return", "func f() -> i32\n    defer => writeLine(\"function\")\n    let a: [2 of i32] = [42, 0]\n    for i in a.indices\n        defer => writeLine(\"iteration\")\n        return a[i]\n    return 0\nrequire f() == 42 else => $abort(\"return\")", "iteration\nfunction\n" },
        { "Yield", "let a: [2 of i32] = [42, 0]\nlet n = result: if true\n    for i in a.indices\n        defer => writeLine(\"iteration\")\n        yield to result: a[i]\n    yield 0\nelse => 0\nrequire n == 42 else => $abort(\"yield\")", "iteration\n" },
        { "Slice", "var a: [3 of i32] = [10, 20, 12]\nlet s = a[..]\nlet copied = s\nlet again = copied[..]\nvar total = 0\nfor i in again.indices\n    total += again[i]\nrequire total == 42 and not s.isEmpty and s.length == 3 else => $abort(\"slice\")\na[0] = 99\nwriteLine(\"ok\")", "ok\n" },
        { "NestedSlice", "let a: [2 of [2 of i32]] = [[1, 2], [20, 22]]\nlet s = a[1][..]\nrequire s[0] + s[1] == 42 else => $abort(\"nested\")\nwriteLine(\"ok\")", "ok\n" },
        { "SliceIndexOnce", "func index() -> isize\n    writeLine(\"index\")\n    return 0\nlet a: [1 of i32] = [42]\nrequire a[..][index()] == 42 else => $abort(\"read\")", "index\n" },
        { "ImmediateTemporary", "func make() -> [1 of i32] => [42]\nrequire make()[..][0] == 42 else => $abort(\"temporary\")\nwriteLine(\"ok\")", "ok\n" },
        { "CopyScalars", "let a: [2 of bool] = [false, true]\nlet s = a[..]\nrequire not s[0] and s[1] else => $abort(\"bool\")\nlet b: [1 of f64] = [2.5]\nrequire b[..][0] == 2.5 else => $abort(\"float\")\nwriteLine(\"ok\")", "ok\n" },
        { "Shadow", "let i = 42\nlet a: [1 of i32] = [0]\nfor i in a.indices => require i == 0 else => $abort(\"inner\")\nrequire i == 42 else => $abort(\"outer\")\nwriteLine(\"ok\")", "ok\n" },
        { "MetadataEffects", "func index() -> isize\n    writeLine(\"index\")\n    return 0\nlet a: [1 of [1 of i32]] = [[42]]\nrequire a[index()].length == 1 else => $abort(\"length\")", "index\n" },
        { "DisjointSlice", "var a: [2 of [1 of i32]] = [[42], [0]]\nlet s = a[0][..]\na[1][0] = 7\nrequire s[0] == 42 and a[1][0] == 7 else => $abort(\"disjoint\")\nwriteLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("Sequence" + name, source, stdout);

    [Theory]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("9223372036854775807")]
    public void SliceBoundsAbort(string index)
        => ScalarEmissionTest.EmitFixture(
            "SequenceBounds" + (index == "-1" ? "Negative" : index == "1" ? "Length" : "Maximum"),
            "let a: [1 of i32] = [42]\nlet s = a[..]\nlet n = s[" + index + "]\nwriteLine(\"bad\")",
            string.Empty,
            1,
            "Hello.kimi:3:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("let a: [1 of i32] = [1]\nfor i in a.indices => i = 2")]
    [InlineData("let a: [1 of i32] = [1]\nfor i in a.indices => ()\ni")]
    [InlineData("for i in 0..3 => ()")]
    [InlineData("let a: [1 of i32] = [1]\nfor (i, j) in a.indices => ()")]
    [InlineData("let a: [1 of i32] = [1]\nfor (i, i) in a.indices => ()")]
    [InlineData("let a: [1 of i32] = [1]\nfor i in a.indices => exit 1")]
    [InlineData("let a: [1 of i32]\na.indices")]
    [InlineData("let a: [2 of string] = [\"a\", \"b\"]\nwriteLine(a[0])\na.indices")]
    [InlineData("let a: [1 of i32] = [1]\nvar s = a[..]\ns[0] = 2")]
    [InlineData("var a: [1 of i32] = [1]\nlet s = a[..]\na[0] = 2\nlet n = s[0]")]
    [InlineData("var a: [1 of i32] = [1]\nlet s = a[..]\na = [2]\nlet n = s[0]")]
    [InlineData("let a: [1 of string] = [\"a\"]\nlet s = a[..]\nwriteLine(a[0])\ns.length")]
    [InlineData("func make() -> [1 of i32] => [42]\nlet s = make()[..]\nlet n = s[0]")]
    [InlineData("let s = scope: do\n    let a: [1 of i32] = [1]\n    exit to scope: a[..]\nlet n = s[0]")]
    [InlineData("var a: [1 of i32] = [1]\nlet s = a[..]\nlet n = s[(work: do\n    a[0] = 2\n    exit to work: 0)]")]
    [InlineData("let a: [1 of i32] = [1]\nlet i: i32 = 0\na[..][i]")]
    public void RejectsInvalidOrUnsupportedInput(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ReanalysisReusesPlans()
    {
        var c = MinimalEmissionTest.Analyze("let a: [2 of i32] = [1, 2]\nlet s = a[..]\nfor i in s.indices => s[i]");
        for (var i = 0; i < 3; i++)
        {
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            c.Ownership.Analyze();
            Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        }
    }

    [Theory]
    [InlineData("receiver")]
    [InlineData("kind")]
    [InlineData("projection")]
    [InlineData("index")]
    public void MalformedSequencePlansFailAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let a: [2 of i32] = [1, 2]\nlet b: [2 of i32] = [3, 4]\nlet s = a[..]\nfor i in s.indices => s[i]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var index = body.Sequences.FindIndex(x => x.Kind == (defect == "index" ? SequenceOperation.Read : SequenceOperation.Slice));
        var plan = body.Sequences[index];
        body.Sequences[index] = defect switch
        {
            "receiver" => plan with { Receiver = body.Places.Single(x => x.Source is Kimi.Compiler.Parsing.FieldKoto { NameKoto.IdentifierName: "b" }).Id },
            "kind" => plan with { Kind = SequenceOperation.Length },
            "projection" => plan with { Projection = 12345 },
            _ => plan with { Index = -1 },
        };
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }
}
