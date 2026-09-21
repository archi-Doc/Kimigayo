// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class NamedArgumentBoundaryTest
{
    [Theory]
    [InlineData("()", -1, 0)]
    [InlineData("(a: i32, b: i32,)", -1, 2)]
    [InlineData("(! a: i32, b: i32,)", 0, 2)]
    [InlineData("(a: i32 ! b: i32,)", 1, 2)]
    [InlineData("(a:i32!b:i32)", 1, 2)]
    [InlineData("(\n    a: i32\n    // boundary\n    !\n    b: i32,\n)", 1, 2)]
    [InlineData("(a: bool = 1 != 2 ! b: i32)", 1, 2)]
    [InlineData("(a: string = \"!\" ! b: i32)", 1, 2)]
    [InlineData("(a: i32 = if true => 1 else => 2 ! b: i32)", 1, 2)]
    [InlineData("(a: i32 = do => 1 ! b: i32)", 1, 2)]
    [InlineData("(a: (i32) -> i32 = func (x: i32) => x ! b: i32)", 1, 2)]
    [InlineData("(\n    a: () -> () = func ()\n        func inner(! x: i32) => ()\n        inner(x: 1)\n    ! b: i32\n)", 1, 2)]
    [InlineData("(\n    a: i32 = if true\n        1\n    else\n        2\n    ! b: i32\n)", 1, 2)]
    [InlineData("(\n    a: i32 = do\n        1\n    ! b: i32\n)", 1, 2)]
    [InlineData("(\n    a: () -> i32 = func ()\n        return 1\n    ! b: i32\n)", 1, 2)]
    public void ParsesAndReprintsBoundaries(string parameters, int boundary, int count)
    {
        var function = ParseTestHelper.ParseSingleFunction("func f" + parameters + " => ()");
        Assert.Equal(boundary, function.NameBoundaryIndex);
        Assert.Equal(count, function.Parameters.Count);
        var restored = ParseTestHelper.ParseSingleFunction(function.ToString());
        Assert.Equal(boundary, restored.NameBoundaryIndex);
        Assert.Equal(count, restored.Parameters.Count);
    }

    [Theory]
    [InlineData("func f(!) => ()")]
    [InlineData("func f(a: i32 !) => ()")]
    [InlineData("func f(! a: i32 ! b: i32) => ()")]
    [InlineData("func f(!! a: i32) => ()")]
    [InlineData("func f(a: i32, ! b: i32) => ()")]
    [InlineData("func f(a: i32 !, b: i32) => ()")]
    [InlineData("func f(a?: i32) => ()")]
    [InlineData("func f(a!: i32) => ()")]
    [InlineData("func f(a => b?: i32) => ()")]
    [InlineData("func f(a => b!: i32) => ()")]
    [InlineData("func f(a => x: i32 ! a => y: i32) => ()")]
    [InlineData("func f(a => x: i32, a => y: i32) => ()")]
    [InlineData("struct S\n    func f(! self) => ()")]
    [InlineData("struct S\n    func f(self !) => ()")]
    [InlineData("struct S\n    func f(a: i32 ! self) => ()")]
    [InlineData("struct S\n    func f(self, self => x: i32) => ()")]
    [InlineData("let f = func (! x: i32) => x")]
    [InlineData("let f: (! i32) -> i32")]
    [InlineData("struct S\n    property x: i32\n        get(! self: ref/Self) -> i32 => 0")]
    [InlineData("specialize func f<i32>(! x: i32) => ()")]
    [InlineData("func f<! T>() => ()")]
    [InlineData("enum E\n    Item(! i32)")]
    [InlineData("f(1 ! x: 2)")]
    [InlineData("#Inline(! true)\nfunc f() => ()")]
    [InlineData("func f(a: i32 = do\n    1 ! b: i32) => ()")]
    [InlineData("func f(a: () -> i32 = func ()\n    return 1 ! b: i32) => ()")]
    public void RejectsInvalidBoundarySyntax(string source)
        => Assert.NotEmpty(ParseTestHelper.Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(1, b: 2, c: 3)", true)]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(c: 3, a: 1, b: 2)", true)]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(1, 2, c: 3)", false)]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(1, b: 2, 3)", false)]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(1, a: 2, c: 3)", false)]
    [InlineData("(a: i32 ! b: i32, c: i32)", "f(1, unknown: 2, c: 3)", false)]
    [InlineData("(a: i32 ! b: i32 = 2, c: i32)", "f(1, c: 3)", true)]
    [InlineData("(a: i32 = 1 ! b: i32, c: i32)", "f(b: 2, c: 3)", true)]
    [InlineData("(a: i32 = 1 ! b: i32, c: i32)", "f(2, c: 3)", false)]
    [InlineData("(a: i32, b: i32, c: i32)", "f(1, 2, 3)", true)]
    public void MatchesWithoutSkippingOrReordering(string parameters, string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("func f" + parameters + " -> i32 => a + b + c\n" + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
        Assert.Equal(accepted, c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("self ! x: i32", 0, "s.f(x: 3)", "S.f(s, x: 3)")]
    [InlineData("! self, x: i32", 0, "s.f(x: 3)", "S.f(s, x: 3)")]
    [InlineData("x: i32, self", 1, "s.f(3)", "S.f(3, s)")]
    [InlineData("! x: i32, self", 0, "s.f(x: 3)", "S.f(x: 3, self: s)")]
    [InlineData("x: i32 ! self, y: i32", 1, "s.f(3, y: 4)", "S.f(3, s, y: 4)")]
    [InlineData("x: i32, self ! y: i32", 1, "s.f(3, y: 4)", "S.f(3, s, y: 4)")]
    [InlineData("x: i32 ! y: i32, self", 1, "s.f(3, y: 4)", "S.f(3, y: 4, self: s)")]
    public void NormalizesReceiverPositions(string parameters, int k, string bound, string unbound)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public func f(" + parameters + ") -> i32 => x\nfunc use(s: ref/S) -> i32 => " + bound + " + " + unbound);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var f = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Equal(k, f.PositionalParameterCount);
    }

    [Theory]
    [InlineData("! x: i32, self", "S.f(s, x: 3)")]
    [InlineData("! x: i32, self", "s.f(3)")]
    [InlineData("x: i32 ! y: i32, self", "S.f(3, s, y: 4)")]
    [InlineData("self ! x: i32", "s.f(self: s, x: 3)")]
    public void ReceiversDoNotPermitPositionalSkipping(string parameters, string call)
        => Assert.False(MinimalEmissionTest.Analyze("struct S\n    public func f(" + parameters + ") => ()\nfunc use(s: ref/S) => " + call).Binding.Result.IsComplete);

    [Fact]
    public void OrdinarySelfAndExternalInternalNamespacesStayIndependent()
    {
        var c = MinimalEmissionTest.Analyze("func f(self: i32 ! other: i32) -> i32 => self + other\nfunc g(a => b: i32 ! b => a: i32) -> i32 => a + b\nf(1, other: 2)\ng(1, b: 2)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Emission.Validate(out var error), error);
        Assert.False(MinimalEmissionTest.Analyze("func f(! self: i32) => ()\nf(1)").Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("s.f(3)", true)]
    [InlineData("s.f(x: 3)", false)]
    public void DistinctRequirementContractsAreNotMerged(string call, bool accepted)
    {
        var c = MinimalEmissionTest.Analyze("contract A\n    func f(self, x: i32) -> i32\ncontract B\n    func f(self ! x: i32) -> i32\nstruct S\n    Self is A\n    Self is B\n    public func f(self, x: i32) -> i32 => x\nfunc use<T>(s: ref/T) -> i32\n    T is A and B\n    return " + call);
        Assert.Equal(accepted, c.Binding.Result.IsComplete);
        if (accepted)
        {
            var selected = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single().BoundCall!.Target;
            Assert.Equal("A", selected.Scope.Owner.BoundSymbol!.Name);
        }
    }

    [Fact]
    public void SpecializationMetadataUsesInheritedK()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(x: i32 ! y: i32 = 2) -> i32 => x + y\nspecialize func f<i32>(x: i32, y: i32) -> i32 => x + y\nf<i32>(1)");
        Assert.True(c.Binding.Result.IsComplete);
        var specialized = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsSpecialization);
        Assert.Equal(-1, specialized.NameBoundaryIndex);
        Assert.Equal(1, specialized.PositionalParameterCount);
    }

    [Fact]
    public void InheritedLookupKeepsTheStaticDeclarationsNamesAndDefaults()
    {
        var c = MinimalEmissionTest.Analyze("open struct B\n    public func f(! external => x: i32 = 1) -> i32 => x\nstruct D: B\nfunc fromBase() -> i32 => B.f()\nfunc fromDerived() -> i32 => D.f()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal(2, calls.Length);
        for (var i = 0; i < calls.Length; i++)
        {
            var plan = calls[i].BoundCall!;
            var target = Assert.IsType<FunctionKoto>(plan.Target.Declaration);
            Assert.Equal("B", plan.Target.Scope.Owner.BoundSymbol!.Name);
            Assert.Equal(0, target.PositionalParameterCount);
            Assert.Equal("external", target.Parameters[0].ExternalName);
            Assert.Single(plan.DefaultArguments.ToArray());
            Assert.Same(target.Parameters[0].DefaultValue, plan.DefaultArguments[0].Expression);
        }
    }

    [Fact]
    public void NamedMappingPreservesSourceOrderBeforeDefaults()
        => ScalarEmissionTest.EmitFixture("NameBoundaryOrder", "func mark(label: string) -> i32\n    Console.writeLine(label)\n    return 3\nfunc f(a: i32 = 10, b: i32 = a + 1 ! c: i32, d: i32 = c + 1) -> i32 => a + b + c + d\nif f(c: mark(\"c\"), b: mark(\"b\")) == 20 => Console.writeLine(\"ok\")", "c\nb\nok\n");

    [Fact]
    public void ExecutesBoundAndUnboundReceiversAcrossTheBoundary()
        => ScalarEmissionTest.EmitFixture("NameBoundaryReceiver", "struct S\n    public func f(x: i32 ! self, y: i32 = 2) -> i32 => x + y\n    public func g(! x: i32, self) -> i32 => x\nlet s = S.init()\nif s.f(1) == 3 and S.f(1, s, y: 4) == 5 and s.g(x: 6) == 6 and S.g(self: s, x: 7) == 7 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void AcquiresInSourceOrderAndCleansUpInParameterOrder()
        => ScalarEmissionTest.EmitFixture("NameBoundaryCleanup", "struct Item\n    let id: i32\n    public init(id: i32)\n        self.id = id\n    deinit\n        if id == 1 => Console.writeLine(\"first\") else => Console.writeLine(\"second\")\nfunc make(id: i32) -> Item\n    if id == 1 => Console.writeLine(\"first\") else => Console.writeLine(\"second\")\n    return Item.init(id)\nfunc f(first: Item ! second: Item) => Console.writeLine(\"body\")\nf(second: make(2), first: make(1))", "second\nfirst\nbody\nsecond\nfirst\n");

    [Fact]
    public void ConstructorBaseArgumentsUseCallSyntaxWithoutABoundary()
    {
        var tree = ParseTestHelper.ParseSuccess("open struct Base\n    public init(! x: i32) => ()\nstruct Derived: Base\n    public init(x: i32 ! y: i32 = 2): base(x: x) => ()");
        var derived = Walk(tree.RootKoto).OfType<FunctionKoto>().Single(x => x.BaseInitializer is not null);
        Assert.Equal(1, derived.NameBoundaryIndex);
        Assert.Equal("x", derived.BaseInitializer!.GetArgumentLabel(0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    public void ReusesNamedArgumentMapping(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(i => "p" + i + ": i32"));
        var arguments = string.Join(", ", Enumerable.Range(0, count).Reverse().Select(i => "p" + i + ": " + i));
        var c = MinimalEmissionTest.Analyze("func f(! " + parameters + ") -> i32 => p0\nf(" + arguments + ")\nf(" + arguments + ")");
        Assert.True(c.Binding.Result.IsComplete);
        foreach (var call in Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>())
        {
            Assert.Equal(Enumerable.Range(0, count).Reverse(), call.BoundCall!.ArgumentToParameter.ToArray());
        }

        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }

    [Fact]
    public void RejectsSnapshotsWithoutCompatibleVersionMetadata()
    {
        var c = MinimalEmissionTest.Analyze("func f(! x: i32) => ()\nf(x: 1)");
        foreach (var (field, value) in new (string, object?)[] { ("sourceFormat", 0), ("sourceLanguageVersion", "0.0.1"), ("sourceCompilerVersion", "other build") })
        {
            var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
            var tree = TinyhandSerializer.Deserialize<Kotonoha>(bytes)!;
            typeof(Kotonoha).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(tree, value);
            var destination = Compilation.CreateForTest();
            tree.OnDeserialized(destination);
            Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
            Assert.Empty(tree.RootKoto.ChildNodes);
            Assert.Null(tree.GeneratedFunction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsLegacySnapshotsBeforeReinterpretingUnmarkedParameters(bool existing)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32) => ()\nf(1)");
        var bytes = TinyhandSerializer.Serialize(new LegacySourceSnapshot { Documents = c.Kotonoha.SourceDocuments.ToList() });
        Kotonoha? tree = existing ? c.Kotonoha : null;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(c);
        Assert.NotEmpty(tree.DiagnosticCollection.GetArray());
        Assert.Empty(tree.RootKoto.ChildNodes);
        Assert.Null(tree.GeneratedFunction);
    }

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}

[TinyhandObject]
public partial class LegacySourceSnapshot
{
    [Key(0)]
    public uint Id { get; set; }

    [Key(1)]
    public string Name { get; set; } = "legacy";

    [Key(2)]
    public string Url { get; set; } = "legacy";

    [Key(3)]
    public List<SourceDocument> Documents { get; set; } = new();

    [Key(4)]
    public Dictionary<int, (string ModId, int AdditionOrder)>? GeneratedLocations { get; set; }
}
