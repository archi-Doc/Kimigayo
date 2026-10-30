// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A struct's dispatch shape (<see cref="VTableDef"/>, DECISIONS R5-M62): its base's slots, then its own virtuals in member order,
/// each running the struct's own override, else its base's entry, else the original; and <c>TyCtxt.Applies</c>, whether a slot exists for
/// a closed declaring layer (SPEC 6.4.3).</summary>
public sealed class VTableDefTest
{
    // A three-layer generic family whose middle slot is conditional (a Block in its place) and overridden in the middle layer, and a
    // struct split into two fragments whose own order is not its names' order, with a derived struct.
    private const string Source = "contract Marker\nstruct NonCopy\n    public init() => ()\n    drop => ()\nopen struct Base<T>\n    T is Owned\n" +
        "    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n    Self is Marker when T is Copy\n" +
        "        public virtual func pick(self: objref/Self, value: ref/T) -> i32 => 2\n    public virtual func last(self: objref/Self) -> i32 => 3\n" +
        "open struct Mid<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func last(self: objref/Self) -> i32 => 30\n" +
        "    public virtual func extra(self: objref/Self) -> i32 => 4\nstruct Leaf<T> : Mid<T>\n    T is Owned\n    public init() => ()\n" +
        "    override func first(self: objref/Self) -> i32 => 10\nopen struct Split\n    public init() => ()\n" +
        "    public virtual func second(self: objref/Self) -> i32 => 2\nopen struct Split\n    public virtual func again(self: objref/Self) -> i32 => 1\n" +
        "struct Part : Split\n    public init() => ()\n    override func again(self: objref/Self) -> i32 => 11\n" +
        "let a = Leaf<i32>.init()@obj\nlet b = Leaf<NonCopy>.init()@obj\nlet p = Part.init()@obj\n" +
        "require a.pick(7) == 2 and b.last() == 30 and p.again() == 11 else => $abort(\"vtable\")\n";

    [Fact]
    public void ATableIsItsBasesThenItsVirtualsInMemberOrder()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal("first=Base, pick=Base, last=Base; base 0", Show(c, "Base"));
        Assert.Equal("first=Base, pick=Base, last=Mid, extra=Mid; base 3", Show(c, "Mid"));
        Assert.Equal("first=Leaf, pick=Base, last=Mid, extra=Mid; base 4", Show(c, "Leaf"));
        Assert.Equal("second=Split, again=Split; base 0", Show(c, "Split"));
        Assert.Equal("second=Split, again=Part; base 2", Show(c, "Part"));
        Assert.Null(VTableDef.Of(Symbol(c, "NonCopy")));
        Assert.Equal([1, 3, 1], new[] { "pick", "extra", "again" }.Select(x => VTableDef.Slot(Function(c, x))));
    }

    // SlotOf: the slot an original or an override runs, the index Implementations holds it at in its owner's table (and every table that
    // inherits it); a function that runs no slot has none.
    [Fact]
    public void SlotOfIsTheSlotAnOriginalOrAnOverrideRuns()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal([0, 1, 3, 1], new[] { "first", "pick", "extra", "again" }.Select(x => VTableDef.SlotOf(Function(c, x))));
        Assert.Equal([2, 0, 1], new[] { ("Mid", "last"), ("Leaf", "first"), ("Part", "again") }.Select(x => VTableDef.SlotOf(Member(c, x.Item1, x.Item2))));
        Assert.Equal(-1, VTableDef.SlotOf(KotoTree.Walk(Container(c, "Base")).OfType<FunctionKoto>().First(static x => x.IsConstructor).SymbolOf()!));
    }

    // Milestone 44, the virtual kernel: DeskLamp replaces power and inherits Lamp's kind.
    [Fact]
    public void TheVirtualMilestoneHasItsTables()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone44").Compilation;
        Assert.Equal("power=Device, kind=Device; base 0", Show(c, "Device"));
        Assert.Equal("power=Lamp, kind=Lamp; base 2", Show(c, "Lamp"));
        Assert.Equal("power=DeskLamp, kind=Lamp; base 2", Show(c, "DeskLamp"));
        Assert.Equal("power=Device, kind=Fan; base 2", Show(c, "Fan"));
    }

    // R4-M18: a struct with an override that did not resolve, a failed one or the dependent of a failed input Type, has no table; its base
    // keeps its own.
    [Theory]
    [InlineData("func first(self: objref/Self) -> i64 => 10")]
    [InlineData("func pick(self: objref/Self, value: ref/Missing) -> i32 => 5")]
    public void AnUnresolvedOverrideLeavesNoTable(string replacement)
    {
        var c = MinimalEmissionTest.Analyze(Source.Replace("func first(self: objref/Self) -> i32 => 10", replacement, StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(VTableDef.Of(Symbol(c, "Leaf")));
        Assert.Equal("first=Base, pick=Base, last=Mid, extra=Mid; base 3", Show(c, "Mid"));
    }

    // Each slot of a closed instance runs its implementation in the first layer that declares it, and exists where Applies of its original
    // at the original's layer is true: Base<NonCopy> refutes the Block's `T is Copy`, so that entry is null; an open declaring Type has no answer.
    [Fact]
    public void AppliesIsTheSlotsPremiseAtTheDeclaringLayer()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var plain = c.Binding.SelfType(Symbol(c, "NonCopy"));
        Assert.Equal("first=Leaf<i32>, pick=Base<i32>, last=Mid<i32>, extra=Mid<i32>", Entries(c, Instance(c, "Leaf", BoundType.I32)));
        Assert.Equal("first=Leaf<NonCopy>, pick=null, last=Mid<NonCopy>, extra=Mid<NonCopy>", Entries(c, Instance(c, "Leaf", plain)));
        Assert.Null(c.Types.Applies(Function(c, "pick"), c.Binding.SelfType(Symbol(c, "Base"))));
    }

    // An edit refills the tables and discards the applicability memo (TyCtxt.ResetViews): the same original at the same interned layer is
    // asked again.
    [Fact]
    public void AnEditRefillsTheTableAndRecomputesApplicability()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var pick = Function(c, "pick");
        var layer = Instance(c, "Base", c.Binding.SelfType(Symbol(c, "NonCopy")));
        Assert.False(c.Types.Applies(pick, layer));
        var edited = Source.Replace("func first(self: objref/Self) -> i32 => 10", "func last(self: objref/Self) -> i32 => 300", StringComparison.Ordinal);
        var donor = MinimalEmissionTest.Analyze(edited.Replace("when T is Copy", "when T is Owned", StringComparison.Ordinal));
        var leaf = Container(c, "Leaf");
        var replacement = Container(donor, "Leaf").Members.OfType<FunctionKoto>().Single(static x => x.Name == "last");
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(leaf, leaf.Members.OfType<FunctionKoto>().Single(static x => x.Name == "first"), replacement));
        var condition = Condition(c);
        var changed = ParseTestHelper.Detach(Condition(donor), new CodeBlockKoto(condition.CodeContext));
        Assert.True(KotoHelper.Replace(condition.Parent!, condition, changed));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal("first=Base, pick=Base, last=Leaf, extra=Mid; base 4", Show(c, "Leaf"));
        Assert.Same(pick, Function(c, "pick"));
        Assert.True(c.Types.Applies(pick, layer));
    }

    // A warm rebind refills every table into the same arrays and asking Applies again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindKeepsTheTablesAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var structs = new[] { "Base", "Mid", "Leaf", "Split", "Part" }.Select(x => Symbol(c, x)).ToArray();
        var tables = structs.Select(static x => VTableDef.Of(x)!).ToArray();
        var implementations = tables.Select(static x => x.Implementations).ToArray();
        var pick = Function(c, "pick");
        var layers = new[] { BoundType.I32, c.Binding.SelfType(Symbol(c, "NonCopy")) }.Select(x => Instance(c, "Base", x)).ToArray();
        var applies = layers.Select(x => c.Types.Applies(pick, x)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < structs.Length; i++)
            {
                changed += ReferenceEquals(VTableDef.Of(structs[i]), tables[i]) && ReferenceEquals(tables[i].Implementations, implementations[i]) ? 0 : 1;
            }

            for (var i = 0; i < layers.Length; i++)
            {
                changed += c.Types.Applies(pick, layers[i]) == applies[i] ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Equal([true, false], applies);
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    private static BindingSymbol Symbol(Compilation c, string name) => Container(c, name).SymbolOf()!;

    private static BindingSymbol Function(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name && x.IsVirtual).SymbolOf()!;

    // A struct's own function of a name (its override).
    private static BindingSymbol Member(Compilation c, string container, string name)
        => KotoTree.Walk(Container(c, container)).OfType<FunctionKoto>().Single(x => x.Name == name).SymbolOf()!;

    // The condition of Base's conditional-conformance Block.
    private static IsKoto Condition(Compilation c) => KotoTree.Walk(Container(c, "Base")).OfType<IsKoto>()
        .Single(static x => x.Parent is SyntaxFormKoto { Parent: SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } });

    private static BoundType Instance(Compilation c, string name, BoundType argument)
    {
        var self = c.Binding.SelfType(Symbol(c, name));
        return c.Types.Intern(self.Kind, self.Symbol, self.Semantics, [argument]);
    }

    // Each slot as its original's name and the struct whose function it runs, then the base's slot count.
    private static string Show(Compilation c, string name)
    {
        var table = VTableDef.Of(Symbol(c, name))!;
        var slots = table.Originals.Select((x, i) => $"{x.Name}={((StructKoto)table.Implementations[i].Scope.Owner).Name}");
        return string.Join(", ", slots) + $"; base {table.BaseCount}";
    }

    // The consumer rule: slot i runs Implementations[i] in the first layer that declares it, where its original applies at its own layer.
    private static string Entries(Compilation c, BoundType type)
    {
        var table = VTableDef.Of(type.Symbol!)!;
        var entries = table.Originals.Select((x, i) => x.Name + "=" + Entry(c.Types.Applies(x, Layer(type, x)), Layer(type, table.Implementations[i])));
        return string.Join(", ", entries);

        static string Entry(bool? applies, BoundType layer) => applies == true ? TyCtxt.Name(layer) : "null";

        static BoundType Layer(BoundType type, BindingSymbol function)
        {
            var layer = type;
            while (!ReferenceEquals(layer.Symbol!.Declaration, function.Scope.Owner))
            {
                layer = AdtDef.Base(layer)!;
            }

            return layer;
        }
    }
}
