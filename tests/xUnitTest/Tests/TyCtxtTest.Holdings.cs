// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The storage a value of a Type holds (TyCtxt.Holdings, contract in Binding.Holdings.cs; G416): one row per form, dev's answer in each entry.
public sealed partial class TyCtxtTest
{
    private const string HoldingsSource =
        "open struct Base\n    internal let b: i32\n" +
        "struct Plain: Base\n    internal let pair: (i32, bool)\n    internal let bare: raw/u8\n    internal let deep: Array<raw/u8>\n" +
        "enum Cell\n    Some(i32, raw/u8 during static)\n    None\n" +
        "struct Empty<T>\n" +
        "struct Generic<T>\n    internal let item: T\n" +
        "struct SelfOnly<T>\n    internal let data: raw/i64 during self\n    internal let item: T\n" +
        "struct SelfOrigin {s}\n    internal let data: raw/i64 during self\n    internal let loan: Loan<uniq/i32 during s>\n" +
        "struct RefSlot {s}\n    internal let loan: Loan<ref/i32 during s>\n    internal let at: raw/i32 during s\n" +
        "struct UniqSlot {s}\n    internal let loan: Loan<uniq/i64 during s>\n    internal let at: raw/i64 during s\n" +
        "func probe<T, P>(i: i32, unit: (), tuple: (i32, bool), two: [2 of i32], one: [1 of i32], plain: Plain, cell: Cell, empty: Empty<bool>,\n" +
        "    generic: Generic<bool>, ref: ref/Plain, uniq: uniq/Plain, slice: Slice<i32>, uniqSlice: UniqSlice<i32>, obj: obj/Plain, rc: rc/Plain,\n" +
        "    arc: arc/Plain, array: Array<i32>, dictionary: Dictionary<i32, i32>, text: string, bare: raw/u8, stated: raw/u8 during static,\n" +
        "    parameter: T, projection: P.(Iterator).Item, function: (i32) -> i32, heap: Text.HeapBuffer, start: Start, selfOnly: SelfOnly<bool>, x: uniq/i32,\n" +
        "    selfOrigin: SelfOrigin during x, refSlot: RefSlot during x, uniqSlot: UniqSlot during x) -> ()\n    P is Iterator\n    return ()\n" +
        "func pairs<a/A, b/B, c/C>(all: a/A, some: b/B, rawOnly: c/C) -> ()\n    b is owner or uniq\n    c is raw\n    return ()\n" +
        "public func main() => ()\n";

    // SPEC 3.3.4 (G416, fact.md rows 1-21): each form's entries, by a parameter of `probe` or a stored part: rows 1 (none), 2, 3 with 3b,
    // 5-8, 9, 11, 14, 17, 18 and 20; `Written` and `Holder` read on through a stored part.
    [Theory]
    [InlineData("i", "")]
    [InlineData("unit", "")]
    [InlineData("tuple", "i32 Inline Its; bool Inline Its")]
    [InlineData("two", "i32 Inline Its Repeated")]
    [InlineData("one", "i32 Inline Its")]
    [InlineData("plain", "Base Inline Its; (i32, bool) Inline Its written holder; raw/u8 Inline Its written holder; Array<raw/u8> Inline Its written holder")]
    [InlineData("cell", "i32 Inline Its written holder; raw/u8 Inline Its written holder")]
    [InlineData("empty", "bool Loan Its")]
    [InlineData("generic", "bool Loan Its; bool Inline Its written holder")]
    [InlineData("ref", "Plain Shared Its")]
    [InlineData("uniq", "Plain Exclusive Its")]
    [InlineData("slice", "i32 Shared Its Repeated")]
    [InlineData("uniqSlice", "i32 Exclusive Its Repeated")]
    [InlineData("obj", "Plain Own Its")]
    [InlineData("rc", "Plain Own Its ReadOnly")]
    [InlineData("arc", "Plain Own Its ReadOnly")]
    [InlineData("array", "i32 Own Its Repeated")]
    [InlineData("dictionary", "(i32, i32) Own Its Repeated")]
    [InlineData("text", "u8 Own Its Repeated")]
    [InlineData("bare", "u8 Address Its Raw")]
    [InlineData("stated", "u8 Static Its Raw")]
    [InlineData("parameter", "T Abstract Premised")]
    [InlineData("projection", "P.Item Abstract Premised")]
    [InlineData("function", "- Erased Nothing")]
    [InlineData("heap", "u8 Own Its Repeated")]
    [InlineData("start", "")]
    public void EachFormHoldsItsRowsEntries(string parameter, string entries)
    {
        var (c, p) = HoldingsLayers();
        Assert.Equal(entries, Shown(Entries(c, p[parameter], null, null)));
    }

    // SPEC 5.7, 5.2.1 condition 3, 8.1.1: a raw layer's entries through its stored part, by the step StoredStep's path query reads (rows 12,
    // 13 with and without an Origin argument, 14 and 15); a declaration's mark at any depth (N7), where a parameter's layer is row 11.
    [Fact]
    public void ARawLayerHoldsByItsStoredStep()
    {
        var (c, p) = HoldingsLayers();
        Assert.Equal("u8 Address Any Raw", ShownAt(c, p["plain"], 1, []));
        var deep = c.Types.StoredParts(p["plain"], out _)[2]!;
        Assert.Equal("u8 Address Any Raw", Shown(Entries(c, deep.Components[0], null, null)));
        Assert.Equal("u8 Static Its Raw", ShownAt(c, p["cell"], 0, [1]));
        Assert.Equal("i64 Own Its Raw; bool Own Its Raw", ShownAt(c, p["selfOnly"], 0, []));
        Assert.Equal("i64 Own Its Raw; - Own AnyOf Raw, ExclusiveOrigin holder", ShownAt(c, p["selfOrigin"], 0, []));
        Assert.Equal("i32 Shared Members Raw written holder", ShownAt(c, p["refSlot"], 1, []));
        Assert.Equal("i64 Exclusive Members Raw written holder", ShownAt(c, p["uniqSlot"], 1, []));
    }

    // SPEC 8.1.1: a pair layer holds one entry per admitted case, by the view table (rows 5-8), its raw case unstepped until U3b (row 16); an
    // unconstrained binder admits all of them.
    [Fact]
    public void APairLayerHoldsOneEntryPerAdmittedCase()
    {
        var c = MinimalEmissionTest.Analyze(HoldingsSource);
        var pairs = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "pairs");
        var all = pairs.Parameters.Select(static x => x.Type.TypeOf()!).ToArray();
        Assert.Equal(
            "A Inline Its; A Shared Its; A Exclusive Its; A Own Its; A Own Its ReadOnly; A Own Its ReadOnly; A Shared Its; A Exclusive Its; A Exclusive Its Raw",
            Shown(Entries(c, all[0], null, null)));
        Assert.Equal("B Inline Its; B Exclusive Its", Shown(Entries(c, all[1], null, null)));
        Assert.Equal("C Exclusive Its Raw", Shown(Entries(c, all[2], null, null)));
    }

    // SPEC 23.1.2 (DECISIONS BU-36): a Field Binding did not bind holds any value, a dependent of the reported error; a null Type too.
    [Fact]
    public void APartBindingDidNotBindHoldsAny()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    internal let bad: Missing\n    internal let good: i32\nfunc probe(s: S) -> ()\npublic func main() => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
        var s = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        Assert.Equal("- Inline Any holder; i32 Inline Its written holder", Shown(Entries(c, s, null, null)));
        Assert.Equal("- Inline Any", Shown(Entries(c, null, null, null)));
    }

    // A Closure's environment bindings as its body sees them; a captured raw layer no declaration wrote is row 11 (`RawStated` false), dev's
    // answer, which a Closure's Type keeps, since Unstated marks only kept parts.
    [Fact]
    public void AClosuresBindingsAreItsParts()
    {
        var c = MinimalEmissionTest.Analyze(
            "func probe() -> ()\n    let n: raw/u8 = null\n    let k = func [n] () => n\n    _ = k()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var closure = LocalTypes(c).First(static x => x is { Kind: BoundTypeKind.Closure })!;
        Assert.Equal("raw/u8 Inline Its", Shown(Entries(c, closure, null, null)));
        Assert.Equal("u8 Address Its Raw", Shown(Entries(c, closure.Components[0], null, null)));
    }

    // Q2's condition 2 over the corpus: at every raw layer an instance of the library and the milestones stores, the accessor's step is the
    // path query's (a Tuple's and a fixed array's inline positions included), `Any` exactly where a declaration wrote it, every struct's or
    // enum's stored parts are its entries in order, and no reachable form lacks a row.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void EveryStoredPartsEntryIsThePathQuerysAnswer(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var (types, into, seen, layers, pending) = (c.Types, new List<Hold>(), new HashSet<BoundType>(), 0, new Stack<BoundType>());
        foreach (var local in LocalTypes(c))
        {
            pending.Push(local!);
        }

        while (pending.TryPop(out var type))
        {
            if (type is null || !seen.Add(type))
            {
                continue;
            }

            into.Clear();
            Assert.True(types.Holdings(type, null, null, into), type.Name);
            Assert.DoesNotContain(into, x => x.Step == HoldStep.Unsupported);
            foreach (var entry in into)
            {
                pending.Push(entry.Part!);
            }

            var parts = types.StoredParts(type, out _);
            for (var i = 0; type.Symbol?.LibraryDeclaration is not (KimiDeclarationId.UniqSlice or KimiDeclarationId.HeapBuffer) && i < parts.Length; i++)
            {
                var enumCase = AdtDef.IsEnum(type);
                for (var j = 0; parts[i] is { } part && j < (enumCase ? part.Components.Count : 1); j++)
                {
                    layers += StoredLayers(types, type, i, enumCase ? part.Components[j] : part, enumCase ? [j] : [], into);
                }
            }
        }

        Assert.True(layers > 0, name);
    }

    // SPEC 3.3.4's contract: a warm call allocates nothing, a Dictionary's interned entry included.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmHoldingsReadAllocatesNothing()
    {
        var (c, p) = HoldingsLayers();
        var (types, into, forms) = (c.Types, new List<Hold>(16), p.Values.ToArray());
        var dictionary = p["dictionary"];
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            foreach (var form in forms)
            {
                into.Clear();
                _ = types.Holdings(form, null, null, into);
            }

            into.Clear();
            _ = types.Holdings(dictionary, null, null, into);
        }));
    }

    private static List<BoundType?> LocalTypes(Compilation c)
    {
        var locals = new List<BoundType?>();
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals)
            {
                locals.Add(local.Type);
            }
        }

        return locals;
    }

    // A stored part's raw layers at inline positions: each one's entries against StoredStep and RawStated; the number of layers.
    private static int StoredLayers(TyCtxt types, BoundType instance, int part, BoundType type, int[] positions, List<Hold> into)
    {
        var written = TyCtxt.WrittenAt(instance, part, positions);
        if (type.Semantics == SemanticsKind.Raw)
        {
            var step = types.StoredStep(instance, part, positions);
            into.Clear();
            Assert.True(types.Holdings(type, written, instance, into));
            Assert.Equal(step, into[0].Step);
            Assert.Equal(step == HoldStep.Address && TyCtxt.RawStated(type), into[0].Holds == HoldContent.Any);
            return 1;
        }

        var layers = 0;
        for (var i = 0; type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray && i < type.Components.Count; i++)
        {
            layers += StoredLayers(types, instance, part, type.Components[i], [.. positions, i], into);
        }

        return layers;
    }

    // The compilation of HoldingsSource and its `probe`'s parameter Types by name.
    private static (Compilation C, Dictionary<string, BoundType> Parameters) HoldingsLayers()
    {
        var c = MinimalEmissionTest.Analyze(HoldingsSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        return (c, probe.Parameters.ToDictionary(static x => x.InternalName, static x => x.Type.TypeOf()!));
    }

    private static List<Hold> Entries(Compilation c, BoundType? type, BoundType? written, BoundType? holder)
    {
        var into = new List<Hold>();
        Assert.True(c.Types.Holdings(type, written, holder, into));
        return into;
    }

    // The entries of the raw layer an instance stores at a part and inline positions, read with its declaration's key.
    private static string ShownAt(Compilation c, BoundType instance, int part, int[] positions)
    {
        var entries = Entries(c, KeptLayer(c.Types, instance, part, positions), TyCtxt.WrittenAt(instance, part, positions), instance);
        Assert.Equal(c.Types.StoredStep(instance, part, positions), entries[0].Step);
        return Shown(entries);
    }

    private static string Shown(List<Hold> entries) => string.Join("; ", entries.Select(static x =>
        $"{(x.Part is null ? "-" : Binding.DiagnosticTypeName(x.Part))} {x.Step} {x.Holds}" + (x.Flags == HoldFlags.None ? string.Empty : $" {x.Flags}") +
        (x.Written is null ? string.Empty : " written") + (x.Holder is null ? string.Empty : " holder")));
}
