// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// C-189: the Binding facts A5's pun rule reads (the Middle's G667b), for the Middle's named programs and the design reviews' forms
// (Q1-Q7), and C-211's leading step (the Middle's G667c). Fail-before: PlaceKey, Select, SameStructure, NoStore and Leading do not exist.
public sealed partial class TyCtxtTest
{
    // pStatedOk (G338's qPunOver, V) and pOver (I): the pun target's own layers with their stated step and pointee.
    private const string StatedSource =
        "func s() -> raw/u8 during static\n    unsafe => return Raw.allocate<u8>(8)@(raw/u8 during static)\n" +
        "func put(target: uniq/(raw/u8) during a, q: raw/u8 during a) => ()\n" +
        "func keep(slot: raw/(raw/u8), value: raw/u8) => ()\n" +
        "func t(c: bool)\n    var kk: u8 = 1\n    let r = kk@uniq\n    var spare: raw/u8 = r@follow@raw\n    var other: raw/u8 = null\n" +
        "    let o = other@uniq\n    var q = o@follow@raw@raw/u8\n    let bytes = q@raw@raw/usize\n    unsafe => *bytes = s()@usize\n" +
        "    put(spare@uniq, q)\n    kk = 2\n" +
        "func u()\n    var kk: u8 = 1\n    let r = kk@uniq\n    var spare: raw/u8 = r@follow@raw\n    var other: raw/u8 = null\n" +
        "    var p: raw/(raw/u8) = other@raw\n    let bytes = p@raw@raw/usize\n    unsafe => *bytes = s()@usize\n    keep(p, spare)\n    kk = 2\n" +
        "struct Packet\n    internal var pair: (raw/u8 during self, i32)\n    public init() => self.pair = (null, 0)\n" +
        "struct Lens<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "struct Wrap<T>\n    public var value: T\n" +
        "struct Outer<T> {s}\n    internal let loan: Loan<ref/T during s>\n    internal var w: Wrap<raw/T during s>\n" +
        "struct Nest\n    internal var inner: Packet\n" +
        "struct Plain\n    public var p: raw/u8\n" +
        "struct Fixed\n    public var p: raw/u8 during static\n" +
        "struct Via\n    internal var p: raw/Packet during self\n" +
        "enum Cell\n    Some(raw/i32 during self)\n    None\n" +
        "enum Ce {s}\n    A(raw/i32 during s, Loan<ref/i32 during s>)\n    B\n" +
        "enum Ev\n    A(Packet)\n    B\n" +
        "contract HasItem\n    associate Item\n" +
        "struct Src\n    Self is HasItem\n    associate HasItem.Item is raw/u8\n" +
        "struct SrcS\n    Self is HasItem\n    associate HasItem.Item is raw/u8 during static\n" +
        "struct HA<S>\n    S is HasItem\n    var x: S.Item\n" +
        "func pp<b/B, c/C, d/D>(pr: b/Packet, po: c/Packet, pd: d/Packet) -> ()\n    b is ref or uniq\n    c is owner or uniq\n    d is ref\n    return ()\n" +
        "func cl(y: uniq/u8 during e, p: raw/u8 during e)\n    var b = Packet.init()\n    let k = func [p] () => p\n    _ = k()\n" +
        "    let m = func [b@move] () => ()\n    m()\n" +
        "func probe(b: Packet, lens: Lens<i32>, x: uniq/u8 during a, y: uniq/u8 during e, tuple: (raw/u8 during a, i32), bare: (raw/u8, i32),\n" +
        "    two: [2 of u8], three: [3 of u8], z: (u8, raw/(raw/u8 during a)), w: (u8, raw/(raw/u8)), o: Outer<i32>, n: Nest,\n" +
        "    h: raw/Packet, hs: raw/Packet during static, hh: raw/(raw/Packet during static), gg: raw/(raw/Packet) during static,\n" +
        "    rb: uniq/Packet during a, sb: ref/Packet during a, pl: Plain, fx: Fixed, via: Via, cell: Cell, arr: Array<Packet>,\n" +
        "    ha: raw/Array<Packet>, fa: (raw/u8 during static) -> (), fb: (raw/u8) -> (), wa: Wrap<raw/u8 during static>, wb: Wrap<raw/u8>,\n" +
        "    ta: [2 of raw/u8 during static], tb: [2 of raw/u8], ce: Ce, rr: ref/(raw/Packet), hr: raw/(ref/Packet during a), he: raw/Ev,\n" +
        "    hes: raw/Ev during static, hc: raw/Cell, hx: HA<SrcS>, hy: HA<Src>, hp: raw/HA<SrcS>) -> ()\n    return ()\n" +
        "#Layout(\"C\")\nstruct CNode\n    public var tag: u32\n    public var next: raw/u8\n" +
        "#Layout(\"C\")\nstruct CArr\n    public var items: [2 of CNode]\n    public var n: u32\n" +
        "struct KNode\n    public var tag: u32\n    public var next: raw/u8\n" +
        "func lead<length N>(la: [2 of u16], lz: [0 of u16], ln: [N of u16], lnn: [2 of [N of raw/u8]], lc: CNode, lca: CArr,\n" +
        "    lk: KNode, lr: raw/CNode) -> ()\n    return ()\n" +
        "public func main() => ()\n";

    // SPEC 5.7 Origins, Reading a Field, Writing a Field (C-189 B1, B2): a raw layer's step at a Place, by the Place's path from its root.
    // Labels from SPEC before output: Address = no Origin, Static, Own = `self` through no dereference, Shared/Exclusive = another Origin
    // (a dereferenced borrow's outer Origin by StepOf's kind), Unsupported = U2. A path step: MIR's Arg (`*`: a step past the value, Arg 0).
    [Theory]
    [InlineData("t.q", "", "raw/u8 Exclusive u8")] // pStatedOk: q states o's Origin.
    [InlineData("u.p", "", "raw/raw/u8 Address raw/u8")] // pOver: p states none.
    [InlineData("u.other", "", "raw/u8 Address u8")]
    [InlineData("x", "", "- Exclusive u8")] // A uniq layer: its referent is Past.
    [InlineData("b", "0", "raw/u8 Own u8")] // b.pair: the `self` layer through no dereference (5.7 Writing a Field).
    [InlineData("b", "0 0", "raw/u8 Own u8")] // b.pair.0.
    [InlineData("n", "0 0 0", "raw/u8 Own u8")] // n.inner.pair.0 (review K3): one instance deeper, still Own.
    [InlineData("lens", "1", "raw/i32 Shared i32")] // lens.at: the slot `source`, a ref keeper (DeclaredStep's cap).
    [InlineData("o", "1 0", "raw/i32 Exclusive i32")] // o.w.value (review K1): the slot `s` the holding value binds, not Address.
    [InlineData("pl", "0", "raw/u8 Address u8")] // An unstated layer.
    [InlineData("fx", "0", "raw/u8 Static u8")] // `static`.
    [InlineData("h", "* 0 0", "raw/u8 Address u8")] // (*h).pair.0: h states none (5.7 Reading a Field), not Own.
    [InlineData("hs", "* 0 0", "raw/u8 Static u8")] // (*hs).pair.0: hs states `static`.
    [InlineData("hh", "* * 0 0", "raw/u8 Static u8")] // (**hh).pair.0: the last dereferenced pointer *hh states `static`.
    [InlineData("gg", "* * 0 0", "raw/u8 Address u8")] // (**gg).pair.0: the last one, *gg, states none (gg's `static` is not last).
    [InlineData("rb", "* 0 0", "raw/u8 Exclusive u8")] // Through a uniq borrow: its outer Origin.
    [InlineData("sb", "* 0 0", "raw/u8 Exclusive u8")] // Through a ref borrow: its outer Origin, StepOf's kind (Revision 2: one rule).
    [InlineData("via", "0 * 0 0", "raw/u8 Address u8")] // (*via.p).pair.0: via.p read through no dereference states none.
    [InlineData("cell", "0 0", "raw/i32 Own i32")] // A Case payload's `self` layer (Downcast, then the component).
    [InlineData("arr", "* 0 0", "raw/u8 Own u8")] // An Array element: an own step keeps no dereference.
    [InlineData("ha", "* * 0 0", "raw/u8 Address u8")] // (*ha)[i].pair.0: ha states none.
    [InlineData("pr", "* 0 0", "raw/u8 Exclusive u8")] // Review Q1: a pair admitting ref or uniq: its outer Origin, stated.
    [InlineData("pd", "* 0 0", "raw/u8 Exclusive u8")] // Review Q1: a pair admitting ref: its outer Origin, StepOf's kind.
    [InlineData("po", "* 0 0", "raw/u8 Unsupported u8")] // Review Q2: a pair admitting owner: U2 (PathOrigin's), no stated Origin.
    [InlineData("rr", "* * 0 0", "raw/u8 Address u8")] // Review Q5: the last dereferenced pointer is the unstated raw one.
    [InlineData("hr", "* * 0 0", "raw/u8 Exclusive u8")] // Review Q5: the last is the ref (its outer Origin a).
    [InlineData("he", "* 0 0 0 0", "raw/u8 Address u8")] // Review Q6: an enum payload behind an unstated raw layer.
    [InlineData("hes", "* 0 0 0 0", "raw/u8 Static u8")]
    [InlineData("hc", "* 0 0", "raw/i32 Address i32")]
    [InlineData("hx", "0", "raw/u8 Static u8")] // Review Q7: a projection-typed Field is its binding.
    [InlineData("hy", "0", "raw/u8 Address u8")]
    [InlineData("hp", "* 0", "raw/u8 Static u8")]
    [InlineData("k", "0", "raw/u8 Exclusive u8")] // Review Q4: a Closure's captured raw/u8 during e.
    [InlineData("m", "0 0 0", "raw/u8 Own u8")] // Review Q4: a captured Packet's `self` layer, by value.
    public void C189TheStatedLayerFollowsThePlacesPath(string root, string path, string expected)
    {
        var (c, locals, p) = PunForms();
        var key = PlaceKey.Root(root.Contains('.') ? locals[(root.Split('.')[0], root.Split('.')[1])] : p[root]);
        foreach (var step in path.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            key = c.Types.Select(key, step == "*" ? 0 : int.Parse(step));
        }

        var into = new List<OwnLayer>();
        Assert.True(c.Types.OwnLayers(key, into));
        Assert.Equal(expected, Describe(into));
    }

    // C-211 (PLAN G667c): the one step of a Place's leading chain, the part the specification places at the Place's own address: a fixed
    // array's element at any length (SPEC 4.1: element i at offset i * stride; the step reads no length), a `#Layout("C")` struct's first
    // Field (IMPL 21.1.2: offset 0); no other Type promises one. Labels from SPEC before output: each step's kind (E: an element, F: a
    // first Field) and Type, to the chain's end. The first written Field is MIR's Field 0, and they cannot differ: C layout has no base
    // and one storage fragment (SPEC 6.7.1), a struct has no static Field, and stored Fields keep declaration order (AdtDef.Fields;
    // a computed property is no stored Field): no row can tell them apart.
    [Theory]
    [InlineData("la", "", "E u16")] // A constant length.
    [InlineData("lz", "", "E u16")] // Length 0.
    [InlineData("ln", "", "E u16")] // A generic length.
    [InlineData("lnn", "", "E [N of raw/u8]; E raw/u8")] // A nest of arrays.
    [InlineData("lc", "", "F u32")]
    [InlineData("lca", "", "F [2 of CNode]; E CNode; F u32")] // A C struct whose first Field is a fixed array of C structs.
    [InlineData("lr", "*", "F u32")] // Past a dereference, which the step keeps.
    [InlineData("lk", "", "")] // A Kimigayo-layout struct.
    [InlineData("tuple", "", "")] // A Tuple.
    [InlineData("cell", "", "")] // An enum,
    [InlineData("cell", "0", "")] // and its Case key.
    [InlineData("h", "", "")] // A raw pointer.
    public void C211TheLeadingStepIsThePartAtAPlacesOwnAddress(string root, string path, string expected)
    {
        var (c, _, p) = PunForms();
        var key = PlaceKey.Root(p[root]);
        foreach (var step in path.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            key = c.Types.Select(key, step == "*" ? 0 : int.Parse(step));
        }

        var rows = new List<string>();
        for (var part = c.Types.Leading(key, out var element); part.Type is not null; part = c.Types.Leading(part, out element))
        {
            Assert.Same(key.Through, part.Through);
            rows.Add($"{(element ? "E" : "F")} {Binding.DiagnosticTypeName(part.Type)}");
        }

        Assert.Equal(expected, string.Join("; ", rows));
    }

    // C-211: total: an unbound key has no part.
    [Fact]
    public void C211AnUnboundKeyLeadsNowhere()
    {
        var (c, _, _) = PunForms();
        Assert.Null(c.Types.Leading(new PlaceKey(null, null, null, -1, null), out var element).Type);
        Assert.False(element);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void C211AWarmLeadingStepAllocatesNothing()
    {
        var (c, _, p) = PunForms();
        var (t, forms) = (c.Types, p.Values.ToArray());
        void Read()
        {
            foreach (var form in forms)
            {
                for (var part = t.Leading(PlaceKey.Root(form), out _); part.Type is not null; part = t.Leading(part, out _))
                {
                }
            }
        }

        Read();
        Assert.Equal(0, AllocationMeasurement.Measure(Read));
    }

    // A call input's Type-level walk past a layer (the Middle's first step and past it): OwnLayers' Past key carries the dereference.
    [Fact]
    public void C189PastALayerTheKeyCarriesTheDereference()
    {
        var (c, _, p) = PunForms();
        var into = new List<OwnLayer>();
        string Past(string root)
        {
            into.Clear();
            Assert.True(c.Types.OwnLayers(PlaceKey.Root(p[root]), into));
            var past = into[0].Past;
            into.Clear();
            Assert.True(c.Types.OwnLayers(past, into));
            return Describe(into);
        }

        Assert.Equal("raw/u8 Address u8", Past("h")); // Review form 2: not Own.
        Assert.Equal("raw/u8 Static u8", Past("hs"));
        Assert.Equal("raw/u8 Exclusive u8", Past("rb"));
    }

    // Review Q3 (B3): one layer has one step at every key: the enum by value, its Case payload (Downcast) and the component (BU-44: the
    // declaration states slot `s`, a `ref` keeper: Shared).
    [Fact]
    public void C189ACaseKeyReadsTheCasesWrittenTypes()
    {
        var (c, _, p) = PunForms();
        var into = new List<OwnLayer>();
        string Steps(PlaceKey key)
        {
            into.Clear();
            Assert.True(c.Types.OwnLayers(key, into));
            return string.Join("; ", into.Where(static x => x.Layer is not null).Select(static x => $"{Binding.DiagnosticTypeName(x.Layer!)} {x.Step}"));
        }

        var root = PlaceKey.Root(p["ce"]);
        var payload = c.Types.Select(root, 0);
        Assert.Equal(["raw/i32 Shared", "raw/i32 Shared", "raw/i32 Shared"], [Steps(root), Steps(payload), Steps(c.Types.Select(payload, 0))]);
    }

    [Fact]
    public void C189SameStructureTreatsOriginsAsEqual()
    {
        var (c, locals, p) = PunForms();
        var t = c.Types;
        void Same(bool expected, BoundType a, BoundType b)
        {
            Assert.Equal(expected, t.SameStructure(a, b));
            Assert.Equal(expected, t.SameStructure(b, a)); // Symmetric (review N1).
        }

        Same(true, locals[("t", "q")], locals[("u", "other")]); // raw/u8 during o, raw/u8.
        Assert.False(ReferenceEquals(locals[("t", "q")], locals[("u", "other")])); // Interned identity compares Origins.
        Same(false, locals[("u", "other")], locals[("u", "bytes")]); // raw/u8, raw/usize.
        Same(true, p["x"], p["y"]); // uniq/u8 during a, during e.
        Same(true, p["tuple"], p["bare"]); // (raw/u8 during a, i32), (raw/u8, i32).
        Same(true, p["z"], p["w"]); // A stated layer below a pointee.
        Same(false, p["two"], p["three"]); // Lengths.
        Same(false, p["tuple"], p["z"]);
        Same(true, p["fa"], p["fb"]); // Function Types (review S2).
        Same(true, p["wa"], p["wb"]); // A Type argument's layer (S3).
        Same(true, p["ta"], p["tb"]); // Fixed-array elements (S4).
        Same(true, p["h"], p["hs"]);
        foreach (var form in p.Values)
        {
            Assert.True(t.SameStructure(form, form)); // Reflexive without a reference test (review N2: the walk discharges it).
        }

        Same(true, AdtDef.FieldType(p["b"], 0)!, c.Types.StoredParts(p["b"], out _)[0]!); // AdtDef's view, kept part.
    }

    [Fact]
    public void C189AnOperationStoresNoValueThroughTheseInputs()
    {
        var c = MinimalEmissionTest.Analyze(StatedSource);
        var slice = c.Library.GetSymbol(KimiDeclarationId.RawSlice);
        var initialize = c.Library.GetSymbol(KimiDeclarationId.RawInitialize);
        var keep = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "keep").SymbolOf();
        Assert.Equal([true, false], [TyCtxt.NoStore(slice, 0), TyCtxt.NoStore(slice, 1)]);
        Assert.Equal([false, true], [TyCtxt.NoStore(initialize, 0), TyCtxt.NoStore(initialize, 1)]);
        Assert.Equal([false, false], [TyCtxt.NoStore(keep, 0), TyCtxt.NoStore(keep, 1)]);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void C189AWarmPunFactsReadAllocatesNothing()
    {
        var (c, locals, p) = PunForms();
        var (t, into) = (c.Types, new List<OwnLayer>(16));
        var forms = locals.Values.Concat(p.Values).ToArray();
        var (h, o, ha, via, pr, ce) = (p["h"], p["o"], p["ha"], p["via"], p["pr"], p["ce"]);
        void Read()
        {
            foreach (var a in forms)
            {
                into.Clear();
                _ = t.OwnLayers(PlaceKey.Root(a), into);
                foreach (var b in forms)
                {
                    _ = t.SameStructure(a, b);
                }
            }

            into.Clear();
            _ = t.OwnLayers(t.Select(t.Select(t.Select(PlaceKey.Root(h), 0), 0), 0), into);
            into.Clear();
            _ = t.OwnLayers(t.Select(t.Select(PlaceKey.Root(o), 1), 0), into);
            into.Clear();
            _ = t.OwnLayers(t.Select(t.Select(t.Select(PlaceKey.Root(ha), 0), 0), 0), into);
            into.Clear();
            _ = t.OwnLayers(t.Select(t.Select(t.Select(t.Select(PlaceKey.Root(via), 0), 0), 0), 0), into);
            into.Clear();
            _ = t.OwnLayers(t.Select(t.Select(t.Select(PlaceKey.Root(pr), 0), 0), 0), into);
            into.Clear();
            _ = t.OwnLayers(t.Select(PlaceKey.Root(ce), 0), into);
        }

        Read();
        Assert.Equal(0, AllocationMeasurement.Measure(Read));
    }

    private static string Describe(List<OwnLayer> into)
        => string.Join("; ", into.Select(static x => $"{(x.Layer is null ? "-" : Binding.DiagnosticTypeName(x.Layer))} {x.Step} "
            + $"{(x.Past.Type is null ? "-" : Binding.DiagnosticTypeName(x.Past.Type))}"));

    private static (Compilation C, Dictionary<(string Body, string Local), BoundType> Locals, Dictionary<string, BoundType> Parameters) PunForms()
    {
        var c = MinimalEmissionTest.Analyze(StatedSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var locals = new Dictionary<(string, string), BoundType>();
        var parameters = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.Name is "probe" or "pp" or "lead")
            .SelectMany(static x => x.Parameters).ToDictionary(static x => x.InternalName, static x => x.Type.TypeOf()!);
        foreach (var body in c.BuildMir().Bodies)
        {
            if (body.Key.Decl?.Name is "t" or "u" or "cl" && body.Key.Decl.Name is var name)
            {
                foreach (var local in body.Locals)
                {
                    if (local.Name is { } named)
                    {
                        locals.TryAdd((name, named), local.Type);
                    }

                    if (name == "cl" && local.Name is "k" or "m")
                    {
                        parameters.TryAdd(local.Name, local.Type);
                    }
                }
            }
        }

        return (c, locals, parameters);
    }
}
