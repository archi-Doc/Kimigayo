// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericStorageEmissionTest
{
    internal const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func take(self: Self) -> T => self.value\n";
    private const string Choose = "func choose<T>(a: T, b: T, first: bool) -> T => if first => a else => b\n";
    private const string Token = "struct Token\n    let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        if self.id == 1 => writeLine(\"one\")\n        if self.id == 2 => writeLine(\"two\")\n        if self.id == 3 => writeLine(\"three\")\n        if self.id == 4 => writeLine(\"four\")\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "String", Box + "let b = Box<string>.init(\"text\")\nwriteLine(b.take())", "text\n" },
        { "Array", Box + "let b = Box<[3 of i32]>.init([2, 4, 6])\nvar total = 0\nfor x in b.take() => total += x\nrequire total == 12 else => $abort(\"array\")\nwriteLine(\"ok\")", "ok\n" },
        { "Scalars", Box + "let a = Box<i32>.init(42)\nlet b = Box<bool>.init(true)\nrequire a.take() == 42 and b.take() else => $abort(\"scalar\")", string.Empty },
        { "Unit", Box + "let b = Box<()>.init(())\nb.take()\nwriteLine(\"ok\")", "ok\n" },
        { "EmptyArray", Box + "let b = Box<[0 of i32]>.init([])\nfor x in b.take() => $abort(\"empty\")\nwriteLine(\"ok\")", "ok\n" },
        { "ChooseBoth", Choose + "writeLine(choose(\"first\", \"other\", true))\nwriteLine(choose(\"other\", \"second\", false))", "first\nsecond\n" },
        { "ChooseCopy", Choose + "let a: i32 = 12\nlet b: i32 = 30\nrequire choose(a, b, true) + choose(a, b, false) == a + b else => $abort(\"copy\")", string.Empty },
        { "ChoiceDestruction", Token + Choose + "do\n    let selected = choose(Token.init(1), Token.init(2), true)\n    writeLine(\"returned\")\ndo\n    let selected = choose(Token.init(3), Token.init(4), false)\n    writeLine(\"returned second\")", "two\nreturned\none\nthree\nreturned second\nfour\n" },
        { "BoxDestruction", Token + Box + "do\n    let box = Box<Token>.init(Token.init(1))\n    let value = box.take()\n    writeLine(\"taken\")\nwriteLine(\"done\")", "taken\none\ndone\n" },
        { "UnusedBox", Token + Box + Choose + "do\n    let box = choose(Box<Token>.init(Token.init(1)), Box<Token>.init(Token.init(2)), true)\n    let value = box.take()\n    writeLine(\"taken\")", "two\ntaken\none\n" },
        { "CopyPremise", "func again<T>(value: T) -> T\n    T is Copy\n    let first = value\n    return value\nrequire again<i32>(42) == 42 else => $abort(\"copy\")", string.Empty },
        { "CopyBox", Box.Replace("    let value: T", "    Self is Copy when T is Copy\n    let value: T") + "let b = Box<i32>.init(42)\nrequire b.take() + b.take() == 84 else => $abort(\"copy box\")", string.Empty },
        { "MultiField", Token + "struct Pair<T>\n    let first: T\n    let second: T\n    let third: T\n    public init(a: T, b: T, c: T)\n        self.first = a\n        self.second = b\n        self.third = c\n    public func take(self: Self) -> T => self.first\ndo\n    let pair = Pair<Token>.init(Token.init(1), Token.init(2), Token.init(3))\n    let first = pair.take()\n    writeLine(\"returned\")", "three\ntwo\nreturned\none\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericStorage" + name, source, stdout);

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nwriteLine(value.take())")]
    [InlineData("group Outer\n    public group Inner\n        public func choose<T>(a: T, b: T, first: bool) -> T => if first => a else => b\nwriteLine(Outer.Inner.choose(\"a\", \"b\", true))")]
    public void GenericCallsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nwriteLine(value.take())", true)]
    [InlineData(Box + "let value = Box<string>.init(\"value\")\nwriteLine(value.take())\nwriteLine(value.take())", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func take(self: Self) -> T => self.value\n    deinit => ()\nwriteLine(\"unused\")", false)]
    [InlineData("struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n    public func twice(self: Self) -> T\n        let first = self.value\n        return self.value\nwriteLine(\"unused\")", false)]
    public void ChecksGenericOwnershipAtDefinition(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
        if (valid)
        {
            Assert.Contains(c.Ownership.Bodies.SelectMany(x => x.Operations), x => x.Acquisition == AcquisitionKind.CopyOrMove);
        }
    }

    [Theory]
    [InlineData(Box + "let value = Box<i32>.init(\"wrong\")")]
    [InlineData(Box + "let value = Box<i32>.init()")]
    [InlineData(Box + "let value = Box<i32>.init(1)\nvalue.value")]
    [InlineData(Choose + "choose(\"text\", true, true)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value\n    return value\nlet actual = twice<i32>(42)")]
    [InlineData("func twice<T>(value: T) -> T\n    let first = value\n    return value\nwriteLine(\"unused\")")]
    [InlineData("func copied<T>(value: T) -> T\n    T is Copy\n    return value\nwriteLine(copied<string>(\"text\"))")]
    public void RejectsInvalidInputsBeforeEmission(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void SharesOneDefinitionAcrossConcreteEntriesAndDeduplicatesPolicies()
    {
        var c = MinimalEmissionTest.Analyze(Choose + "writeLine(choose(\"a\", \"b\", true))\nlet n = choose<i32>(1, 2, false)\nlet m = choose<i32>(3, 4, true)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        var body = Assert.Single(module.SharedBodies);
        Assert.Equal(2, body.PolicyCount); // T and bool, independent of parameter/temp/result occurrences.
        Assert.Equal(2, body.LiveFlags.Count(x => x)); // Only the conditionally consumed parameters need flags.
        Assert.Equal(2, module.SharedEntries.Count);
        Assert.Equal(new[] { 12, 72 }, module.SharedEntries.Select(x => x.ScratchSize).Order().ToArray());
        Assert.All(module.SharedEntries, entry => Assert.Same(body, entry.Body));
    }

    [Theory]
    [InlineData("acquisition")]
    [InlineData("projection")]
    [InlineData("cleanup")]
    [InlineData("parameter")]
    public void MalformedSharedPlansFailAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Box + "let b = Box<string>.init(\"text\")\nwriteLine(b.take())");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "take");
        if (defect == "projection")
        {
            body.Projections[0] = body.Projections[0] with { Selector = 42 };
        }
        else if (defect == "cleanup")
        {
            body.CleanupStepStorage.Clear();
        }
        else if (defect == "parameter")
        {
            var place = body.Places.Single(x => x.Kind == OwnershipPlaceKind.Parameter);
            body.PlaceStorage[place.Id] = place with { Type = BoundType.String };
        }
        else
        {
            var id = body.OperationStorage.FindIndex(x => x.Projection >= 0 && x.Kind == OwnershipOperationKind.Produce);
            body.OperationStorage[id] = body.Operations[id] with { Acquisition = AcquisitionKind.Copy };
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void RebindingInvalidatesEntriesAndRestoresOnlyCurrentTypes()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let b = Box<i32>.init(42)\nrequire b.take() == 42 else => $abort(\"value\")");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Emission.Validate(out var error), error);
            c.Bind();
            Assert.False(c.Emission.Validate(out _));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        Assert.True(c.Emission.Validate(out var finalError), finalError);
    }
}
