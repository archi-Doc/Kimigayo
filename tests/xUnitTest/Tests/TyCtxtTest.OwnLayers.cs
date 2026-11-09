// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The parts a value holds at its own address (TyCtxt.OwnLayers, contract in TyCtxt.Raw.cs; PLAN G583, G598): one row per form.
public sealed partial class TyCtxtTest
{
    private const string OwnLayersSource =
        "struct Wrap<T>\n    internal let value: T\n" +
        "struct Outer<T>\n    internal let inner: Wrap<T>\n" +
        "struct SelfHeld\n    internal let data: raw/i64 during self\n" +
        "struct Empty<T>\n" +
        "enum Slot<T>\n    Full(T, i32)\n    Vacant\n" +
        "open struct Base0\n    internal let at: raw/u8 during static\n" +
        "struct Derived: Base0\n    internal let n: i32\n" +
        "func probe<T>(x: uniq/u8 during a, tuple: (raw/u8 during a, i32), option: Option<raw/u8 during a>, field: Wrap<raw/u8 during a>,\n" +
        "    nested: Option<Outer<raw/u8 during a>>, array: Array<raw/u8 during a>, selfHeld: SelfHeld, borrow: (uniq/u8 during a, raw/u8 during static),\n" +
        "    loan: Loan<uniq/u8 during a>, empty: Empty<raw/u8 during a>, scalar: i32, bare: raw/u8, rc: rc/Wrap<raw/u8 during a>,\n" +
        "    abstract: (T, raw/u8 during a), slot: Slot<raw/u8 during a>, derived: Derived, object: obj/Wrap<raw/u8 during a>) -> ()\n    return ()\n" +
        "func pairs<b/B>(x: uniq/u8 during a, pair: b/(raw/u8 during a)) -> ()\n    b is owner or uniq\n    return ()\n" +
        "public func main() => ()\n";

    // SPEC 5.2.1, 5.7 Calls, 15.6.4: a stated layer at any holder by the storage walk's by-value relation (a Tuple, an Option payload, a Field
    // of T, a nested holder, an Array's elements, an enum's payload, a struct's base, an `obj/T` payload, a pair layer's owner case), a
    // `during self` layer by its own step, a borrow element, a pair layer's borrow case, an rc view, an abstract part and a Loan<T>'s
    // argument as a part with no layer, a Type argument and a scalar as nothing (a layer shown without its Origin, by its step).
    [Theory]
    [InlineData("x", "- Exclusive")]
    [InlineData("tuple", "raw/u8 Exclusive")]
    [InlineData("option", "raw/u8 Exclusive")]
    [InlineData("field", "raw/u8 Exclusive")]
    [InlineData("nested", "raw/u8 Exclusive")]
    [InlineData("array", "raw/u8 Exclusive")]
    [InlineData("selfHeld", "raw/i64 Own")]
    [InlineData("borrow", "- Exclusive; raw/u8 Static")]
    [InlineData("loan", "- Exclusive")]
    [InlineData("empty", "")]
    [InlineData("scalar", "")]
    [InlineData("bare", "raw/u8 Address")]
    [InlineData("rc", "- Own")]
    [InlineData("abstract", "- Abstract; raw/u8 Exclusive")]
    [InlineData("slot", "raw/u8 Exclusive")]
    [InlineData("derived", "raw/u8 Static")]
    [InlineData("object", "raw/u8 Exclusive")]
    [InlineData("pair", "raw/u8 Exclusive; - Exclusive")]
    public void EachFormListsTheLayersItHoldsAtItsOwnAddress(string parameter, string expected)
    {
        var (c, p) = OwnLayersForms();
        var into = new List<OwnLayer>();
        Assert.True(c.Types.OwnLayers(p[parameter], into));
        Assert.Equal(expected, string.Join("; ", into.Select(static x => $"{(x.Layer is null ? "-" : Binding.DiagnosticTypeName(x.Layer))} {x.Step}")));
    }

    // A Closure part as the contract's body sees it (DECISIONS R4-M148, the storage walk's BodyRun): its environment's bindings, a raw layer
    // captured by value with the Origin its definition names (`p`'s), not a local region its inferred Type names; a binding past Binding's
    // Limit makes the answer false.
    [Fact]
    public void AClosureListsItsEnvironmentAsItsBodySeesIt()
    {
        var c = MinimalEmissionTest.Analyze(
            "struct N<X>\n    internal var next: raw/N<(X, X)>\n" +
            "func probe(x: uniq/u8 during a, p: raw/u8 during a, n: N<i32>) -> ()\n    let k = func [p] () => p\n    _ = k()\n" +
            "    let m = func [n@move] () => ()\n    m()\n    return ()\npublic func main() => ()\n");
        var closures = LocalTypes(c).Where(static x => x is { Kind: BoundTypeKind.Closure }).Distinct().ToArray();
        var p = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[1].Type.TypeOf()!;
        var (into, answers) = (new List<OwnLayer>(), new List<string>());
        foreach (var closure in closures)
        {
            into.Clear();
            var complete = c.Types.OwnLayers(closure!, into);
            answers.Add($"{complete}: {string.Join("; ", into.Select(x => $"{x.Step} {(x.Layer is null ? "-" : ReferenceEquals(x.Layer.RawOrigin, p.RawOrigin) ? "p's Origin" : x.Layer.RawOrigin?.Kind.ToString())}"))}");
        }

        Assert.Equal(["True: Exclusive p's Origin", "False: "], answers.Order(StringComparer.Ordinal).Reverse());
    }

    // Binding's Limit: a growing Type answers false.
    [Fact]
    public void AStorageLimitListsNoOwnLayers()
    {
        var c = MinimalEmissionTest.Analyze("struct N<X>\n    internal var next: raw/N<(X, X)>\nfunc probe(n: N<i32>) -> ()\n    return ()\npublic func main() => ()\n");
        var n = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        Assert.False(c.Types.OwnLayers(n, new List<OwnLayer>()));
    }

    // The contract: a warm read allocates nothing.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmOwnLayersReadAllocatesNothing()
    {
        var (c, p) = OwnLayersForms();
        var (types, into, forms) = (c.Types, new List<OwnLayer>(16), p.Values.ToArray());
        foreach (var form in forms)
        {
            _ = types.OwnLayers(form, into);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            foreach (var form in forms)
            {
                into.Clear();
                _ = types.OwnLayers(form, into);
            }
        }));
    }

    private static (Compilation C, Dictionary<string, BoundType> Parameters) OwnLayersForms()
    {
        var c = MinimalEmissionTest.Analyze(OwnLayersSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var probes = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.Name is "probe" or "pairs");
        return (c, probes.SelectMany(static x => x.Parameters).DistinctBy(static x => x.InternalName).ToDictionary(static x => x.InternalName, static x => x.Type.TypeOf()!));
    }
}
