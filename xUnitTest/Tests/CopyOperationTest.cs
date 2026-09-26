// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Linq;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

/// <summary>SPEC 13.5.3: E@copy Copies a proven-Copy value and never transfers or borrows. The bare owning shorthands
/// @owner, @obj, @rc and @arc are not operations; complete targets such as @owner/T keep their acquisitions.</summary>
public class CopyOperationTest
{
    private const string Point =
        "struct Point\n    Self is Copy\n    public var x: i32\n    public var y: i32\n    public init(x: i32, y: i32)\n        self.x = x\n        self.y = y\n";

    // Scalars, aggregates, references and referents are copied, and every source stays usable.
    private const string ValuesSource =
        Point + "var n: i32 = 5\nlet a = n@copy\nn += 1\nrequire a == 5 and n == 6 else => $abort(\"scalar\")\n" +
        "let pair = (1, true)\nlet b = pair@copy\nrequire b.0 == 1 and pair.1 else => $abort(\"tuple\")\n" +
        "var fixed: [2 of i32] = [1, 2]\nlet c = fixed@copy\nfixed[0] = 9\nrequire c[0] == 1 and fixed[0] == 9 else => $abort(\"array\")\n" +
        "var p = Point.init(1, 2)\nlet q = p@copy\np.x = 5\nlet y = p@copy.y\nrequire q.x == 1 and p.x == 5 and y == 2 else => $abort(\"struct\")\n" +
        "let o: Option<i32> = .Some(3)\nlet u = o@copy\nmatch u\n    .Some(3) => ()\n    _ => $abort(\"option\")\n" +
        "let unit = ()@copy\nrequire unit == () else => $abort(\"unit\")\nrequire p.x@copy + 1 == 6 and (n@copy) / 2 == 3 else => $abort(\"chain\")\n" +
        "let r = n@ref\nlet s = r@copy\nlet t = r@follow@copy\nrequire s == 6 and t == 6 else => $abort(\"reference\")\nConsole.writeLine(\"ok\")";

    // A copied Subject is ByValue: match bindings and for items are owned, and the original stays unchanged.
    private const string SubjectsSource =
        "var count: i32 = 5\nmatch count@copy\n    var m\n        m += 1\n        require m == 6 else => $abort(\"match\")\nrequire count == 5 else => $abort(\"count\")\n" +
        "var fixed: [3 of i32] = [1, 2, 3]\nvar total: i32 = 0\nfor var v in fixed@copy\n    v += 10\n    total += v\n" +
        "require total == 36 and fixed[0] == 1 else => $abort(\"for\")\nConsole.writeLine(\"ok\")";

    // A generic Type is copied only with Copy evidence.
    private const string GenericSource =
        "func twice<T>(value: ref/T) -> (T, T)\n    T is Copy\n    return (value@follow@copy, value@follow@copy)\n" +
        "let pair = twice(7)\nrequire pair.0 == 7 and pair.1 == 7 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")";

    private const string MovedStructMemberSource =
        "struct Tracked\n    public var n: i32\n    public init(n: i32) => self.n = n\n    deinit\n        Console.writeLine(\"drop\")\n" +
        "var t = Tracked.init(7)\nlet a = t@move.n\nrequire a == 7 else => $abort(\"move\")\nConsole.writeLine(\"after\")";

    [Theory]
    [InlineData("Values", ValuesSource)]
    [InlineData("Subjects", SubjectsSource)]
    [InlineData("Generic", GenericSource)]
    public void CopiesExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("CopyOperation" + name, source, "ok\n");

    // A struct acquired by @move is a temporary receiver: its member is read, and the temporary is destroyed once at the
    // end of the full expression while the moved local is not destroyed again.
    [Fact]
    public void AMemberOfAMovedStructTemporaryIsReadOnce()
        => ScalarEmissionTest.EmitFixture("CopyOperationMovedStructMember", MovedStructMemberSource, "drop\nafter\n");

    [Fact]
    public void ACopyIsAnIdentityAcquisitionThatKeepsItsPlace()
    {
        var c = MinimalEmissionTest.Analyze("var n: i32 = 1\nlet a = n@copy\nn = 2\nlet b = n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.Equal(ConversionBinding.Identity, conversion.ConversionBinding);
        Assert.Equal(BoundType.I32, conversion.BoundType);
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Ownership.Issues));
    }

    [Fact]
    public void ACopiedSubjectIsByValue()
    {
        var c = MinimalEmissionTest.Analyze("var count: i32 = 1\nmatch count@copy\n    var n => n += 5");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var match = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MatchKoto>().Single();
        Assert.True(c.Binding.TryGetMatch(match, out var plan));
        Assert.Equal(SubjectMode.ByValue, plan!.Mode);
    }

    [Theory]
    [InlineData("let text = \"a\"\nlet b = text@copy", DiagnosticCode.NonCopyOperand_Kd)]
    [InlineData("let b = \"a\"@copy", DiagnosticCode.NonCopyOperand_Kd)]
    [InlineData("func same<T>(value: T) -> T => value@copy", DiagnosticCode.NonCopyOperand_Kd)]
    [InlineData("var n: i32 = 1\nlet r = n@uniq\nlet s = r@copy", DiagnosticCode.NonCopyOperand_Kd)]
    [InlineData("let n: i32 = 1\nlet m = n@owner", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("let text = \"a\"\nlet m = text@move@owner", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("let n: i32 = 1\nlet m = n@((owner))", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("struct Node\n    public var v: i32 = 0\nlet h = Kimi.Intrinsics.makeObj(Node.init())\nlet g = h@obj", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("struct Node\n    public var v: i32 = 0\nfunc f(h: rc/Node)\n    let g = h@rc", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("struct Node\n    public var v: i32 = 0\nfunc f(h: arc/Node)\n    let g = h@arc", DiagnosticCode.BareOwningShorthand_Kd)]
    public void InvalidCopiesAndBareOwningShorthandsAreDiagnosed(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("let n: i32 = 1\nlet m = n@owner/i32")]
    [InlineData("let n: i32 = 1\nlet m = n@(owner/i32)")]
    [InlineData("let text = \"a\"\nlet m = text@move@owner/string")]
    public void CompleteOwningTargetsRemainIdentityAcquisitions(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), x => x.ConversionBinding == ConversionBinding.Identity);
    }

    [Fact]
    public void CopyEndsTheTargetAndContinuesThePostfixChain()
    {
        var tree = ParseSuccess("let value = point@copy.x");
        var access = Assert.IsType<MemberAccessKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        var conversion = Assert.IsType<ConversionKoto>(access.Left);
        Assert.Equal("copy", conversion.Right.ToString());
    }

    [Fact]
    public void CopyIsNotASemanticsPrefix()
    {
        var tree = Parse("let value = source@copy/i32\nlet after = 1");
        Assert.Contains(tree.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.UnexpectedToken_Kd));
        Assert.Equal("after", Assert.IsType<FieldKoto>(tree.GeneratedFunction!.Body!.Items.Last()).NameKoto.IdentifierName);
    }
}
