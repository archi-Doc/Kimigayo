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
        "    abstract: (T, raw/u8 during a), slot: Slot<raw/u8 during a>, derived: Derived, object: obj/Wrap<raw/u8 during a>,\n" +
        "    loanPair: (Loan<uniq/u8 during a>, raw/u8 during a), loanRaw: Loan<uniq/(raw/u8) during a>,\n" +
        "    loanBorrow: (Loan<uniq/u8 during a>, uniq/u8 during a), borrowLoan: (uniq/u8 during a, Loan<uniq/u8 during a>)) -> ()\n    return ()\n" +
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
        Assert.True(c.Types.OwnLayers(PlaceKey.Root(p[parameter]), into));
        Assert.Equal(expected, string.Join("; ", into.Select(static x => $"{(x.Layer is null ? "-" : Binding.DiagnosticTypeName(x.Layer))} {x.Step}")));
    }

    // SPEC 15.3.5 (a Type argument has no address; the Middle's C-370, its landing hunk's R2 (b2')): a part the key walk lists past a
    // Loan's Type argument, which it walks by value until G416 U3a, is marked Loan (L), so a reader takes no write Type and no key past
    // it: the Loan's exclusive borrow, alone, beside a raw layer the Tuple states (not marked) and over a raw pointee; an exclusive borrow
    // that is no Loan's argument, and the layers beside it, are not marked. A part reached both through a Loan's Type argument and
    // directly is listed both ways, in either order (loanBorrow, borrowLoan: the direct borrow keeps its key; G935 review 2c's C1, these
    // two fail with the walked key's Loan bit removed). Fail-before: no Loan mark on 4d510bf64.
    [Theory]
    [InlineData("loan", "- Exclusive L")]
    [InlineData("loanPair", "- Exclusive L; raw/u8 Exclusive")]
    [InlineData("loanRaw", "- Exclusive L")]
    [InlineData("x", "- Exclusive")]
    [InlineData("borrow", "- Exclusive; raw/u8 Static")]
    [InlineData("loanBorrow", "- Exclusive L; - Exclusive")]
    [InlineData("borrowLoan", "- Exclusive; - Exclusive L")]
    public void APartPastALoansTypeArgumentIsMarked(string parameter, string expected)
    {
        var (c, p) = OwnLayersForms();
        var into = new List<OwnLayer>();
        Assert.True(c.Types.OwnLayers(PlaceKey.Root(p[parameter]), into));
        Assert.Equal(expected, string.Join("; ", into.Select(static x => $"{(x.Layer is null ? "-" : Binding.DiagnosticTypeName(x.Layer))} {x.Step}{(x.Loan ? " L" : string.Empty)}")));
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
            var complete = c.Types.OwnLayers(PlaceKey.Root(closure!), into);
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
        Assert.False(c.Types.OwnLayers(PlaceKey.Root(n), new List<OwnLayer>()));
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
            _ = types.OwnLayers(PlaceKey.Root(form), into);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            foreach (var form in forms)
            {
                into.Clear();
                _ = types.OwnLayers(PlaceKey.Root(form), into);
            }
        }));
    }

    private const string ParameterKeysSource =
        "struct Wrap<T>\n    internal let value: T\n" +
        "struct Wrap2<T>\n    internal let value: T\n    internal let q: raw/u8\n" +
        "struct Box<T>\n    internal let b: T\n    public func put(self: uniq/Self, p: raw/T) => ()\n" +
        "    public func put2<U>(self: uniq/Self, p: raw/T, u: raw/U) => ()\n" +
        "struct Box3<T>\n    internal let b: T\n    public func put3<U>(self: uniq/Self, p: raw/U) -> ()\n        U is T\n        return ()\n" +
        "contract HasItem\n    associate Item\n" +
        "contract RawItem: HasItem\n    Self.(HasItem).Item is raw/u8\n" +
        "struct HA<S>\n    S is HasItem\n    internal let x: S.Item\n" +
        "struct Src\n    Self is HasItem\n    associate HasItem.Item is raw/u8\n" +
        "struct SrcR\n    Self is RawItem\n    associate HasItem.Item is raw/u8\n" +
        "struct Outer<S>\n    S is HasItem\n    internal let inner: HA<S>\n" +
        "open struct RB<T>\n    internal let r: T\n    internal let q: raw/u8\n" +
        "struct DB<T>: RB<T>\n    internal let d: T\n" +
        "struct Pair3<T, V>\n    internal let a: T\n    internal let q: raw/u8\n    internal let b: V\n" +
        "enum E<U>\n    A(U)\n    B(raw/u8)\n" +
        "func r1<S>(p: raw/S.Item) -> ()\n    S is HasItem\n    return ()\n" +
        "func r2<S>(p: uniq/HA<S>) -> ()\n    S is HasItem\n    return ()\n" +
        "func r3<S>(p: uniq/HA<S>) -> ()\n    S is HasItem\n    S.Item is raw/u8\n    return ()\n" +
        "func r4<s/U>(p: s/U) -> ()\n    s is ref or uniq\n    return ()\n" +
        "func r5<s/U>(p: s/U) -> ()\n    s is owner or uniq\n    return ()\n" +
        "func r6<T, V>(p: uniq/Pair3<T, V>) => ()\nfunc r7<U>(p: uniq/Option<U>) => ()\nfunc r8<U>(p: uniq/[2 of U]) => ()\n" +
        "func r9<U>(p: uniq/(U, raw/u8)) => ()\nfunc r10<U>(p: uniq/Array<U>) => ()\nfunc r13<U>(p: uniq/Wrap<U>) -> ()\n    U is raw/u8\n    return ()\n" +
        "func r15<U>(p: uniq/E<U>) => ()\nfunc r18<U>(p: raw/U) => ()\n" +
        "func r20<S>(p: uniq/HA<S>) -> ()\n    S is RawItem\n    return ()\n" +
        "func r20b<S>(p: raw/S.Item) -> ()\n    S is RawItem\n    return ()\n" +
        "func r21<S, V>(p: uniq/HA<S>) -> ()\n    S is HasItem\n    S.Item is V\n    V is raw/u8\n    return ()\n" +
        "func r21c<S, V>(p: uniq/HA<S>) -> ()\n    S is HasItem\n    S.Item is V\n    return ()\n" +
        "func r22<s/U>(p: s/U) -> ()\n    s is ref or uniq\n    U is raw/u8\n    return ()\n" +
        "func r24<S>(p: uniq/Outer<S>) -> ()\n    S is HasItem\n    S.Item is raw/u8\n    return ()\n" +
        "func r25<S>(p: uniq/Outer<S>) -> ()\n    S is HasItem\n    return ()\n" +
        "func r26<s/U>(p: s/Wrap<U>) -> ()\n    s is ref or uniq\n    U is raw/u8\n    return ()\n" +
        "func r27<s/U>(p: s/Wrap<U>) -> ()\n    s is ref or uniq\n    return ()\n" +
        "func r28<U, V>(p: uniq/Wrap<U>) -> ()\n    U is Wrap<V>\n    V is raw/u8\n    return ()\n" +
        "func r29<U, V>(p: uniq/Wrap<U>) -> ()\n    U is Wrap<V>\n    return ()\n" +
        "func r30<U, V>(p: uniq/(U, V)) -> ()\n    U is V\n    return ()\n" +
        "func rb<U>(p: uniq/DB<U>) => ()\nfunc rbp<U>(p: uniq/DB<U>) -> ()\n    U is raw/u8\n    return ()\n" +
        "func gargs<V>(gv: raw/V) -> ()\n    return ()\n" +
        "func wa<U>(p: raw/U) => ()\nfunc wa2<U>(p: raw/(raw/U)) => ()\nfunc wu<U>(p: uniq/Wrap<U>) => ()\nfunc wu2<U>(p: uniq/Wrap2<U>) => ()\n" +
        "func wv<U>(p: U) => ()\nfunc wp<U>(p: raw/U) -> ()\n    U is raw/u8\n    return ()\nfunc viaRawWrap(slot: raw/Wrap<raw/u8>, value: raw/u8) => ()\n" +
        "func args(k: ref/u8 during a, t: raw/((raw/(uniq/u8 during a), raw/(raw/(raw/u8)))), n: raw/(raw/(raw/u8)), w2: raw/(raw/(raw/u8)),\n" +
        "    wu: uniq/Wrap<raw/u8>,\n" +
        "    wu2: uniq/Wrap2<raw/u8>, wv: raw/u8, wm: raw/(raw/u8), wp: raw/(raw/u8), view: raw/Wrap<raw/u8>, a2: uniq/HA<Src>, a4: uniq/(raw/u8),\n" +
        "    a6: uniq/Pair3<raw/u8, raw/u8>, a7: uniq/Option<raw/u8>, a8: uniq/[2 of raw/u8], a9: uniq/(raw/u8, raw/u8), a10: uniq/Array<raw/u8>,\n" +
        "    a13: uniq/Wrap<raw/u8>, a15: uniq/E<raw/u8>, a20: uniq/HA<SrcR>, a24: uniq/Outer<Src>, a28: uniq/Wrap<Wrap<raw/u8>>,\n" +
        "    ab: uniq/DB<raw/u8>) -> ()\n    return ()\n" +
        "public func main() => ()\n";

    // SPEC 15.6.4, 5.7 Calls, 8.2.3 (PLAN G823): a called function's parameter key marks each part in the binding of the function's own
    // Type parameter (H; S shown), at the root and at every key past it (Past, nested in braces, four keys deep) as far as the Place is
    // storage of that Type: past a raw layer the binding holds, the Place is a holder the value addresses, read as the input writes it
    // (shown: wa's t and n below their first hidden layer; the owner's O-312 (a), PLAN G935 c3-5). The rows: G790's rAliasClean
    // three and hPunWriteOk (wa's U, t and n), a nested layer the Type expression writes (wa2's outer raw/U), a Field of U beside a Field
    // the declaration states (wu, wu2), a bare U (wv), a projection (r1, r2), a pair target (r4), Fields of T and V beside a stated one
    // (r6), an Option or enum payload (r7, r15), a fixed-array and an Array element (r8, r10), a method's own U (put2's u); shown: an
    // enclosing Type's parameter (Box.put, put2's p), a premise naming U's or S.Item's Type in the function's view, inside a Type too
    // (wp, r13, r3: SPEC 8.2.3's closure under replacement), a premise naming an enclosing Type's parameter (put3), and a Type argument
    // the parameter's own Type expression writes (viaWrap's pWrapViewOk and pWrapViewBad). Named limit: a pair layer's owner case ends
    // the pairing, shown (r5; SPEC labels it hidden). The function's view (SPEC 8.2.3, 8.5.3; the review's View twins i1-i7): a Contract's
    // associated-Type requirement (r20, r20b), a chain of premises (r21; r21c without its last), a pair target under a premise (r22), a
    // Field of a Field (r24; r25 without the premise), a case view then the premise (r26; r27), two premises at depth 2 (r28; r29 with
    // one), a class of two own abstract Types (r30: no form); a generic caller's own V as the input (r18, r17). Labels: F's G823
    // labels.md and its reviews' twins.
    [Theory]
    [InlineData("wa", "p", "t", "raw/(raw/uniq/u8, raw/raw/raw/u8) S {raw/uniq/u8 H {- S}, raw/raw/raw/u8 H {raw/raw/u8 S {raw/u8 S}}}")]
    [InlineData("wa", "p", "n", "raw/raw/raw/u8 S {raw/raw/u8 H {raw/u8 S}}")]
    [InlineData("wa2", "p", "w2", "raw/raw/raw/u8 S {raw/raw/u8 S {raw/u8 H}}")]
    [InlineData("wu", "p", "wu", "- S {raw/u8 H}")]
    [InlineData("wu2", "p", "wu2", "- S {raw/u8 H, raw/u8 S}")]
    [InlineData("wv", "p", "wv", "raw/u8 H")]
    [InlineData("put", "p", "wm", "raw/raw/u8 S {raw/u8 S}")]
    [InlineData("wp", "p", "wp", "raw/raw/u8 S {raw/u8 S}")]
    [InlineData("viaRawWrap", "slot", "view", "raw/Wrap<raw/u8> S {raw/u8 S}")]
    [InlineData("r1", "p", "wm", "raw/raw/u8 S {raw/u8 H}")]
    [InlineData("r2", "p", "a2", "- S {raw/u8 H}")]
    [InlineData("r3", "p", "a2", "- S {raw/u8 S}")]
    [InlineData("r4", "p", "a4", "- S {raw/u8 H}")]
    [InlineData("r5", "p", "wv", "raw/u8 S")]
    [InlineData("r6", "p", "a6", "- S {raw/u8 H, raw/u8 S, raw/u8 H}")]
    [InlineData("r7", "p", "a7", "- S {raw/u8 H}")]
    [InlineData("r8", "p", "a8", "- S {raw/u8 H}")]
    [InlineData("r9", "p", "a9", "- S {raw/u8 H, raw/u8 S}")]
    [InlineData("r10", "p", "a10", "- S {raw/u8 H}")]
    [InlineData("put2", "p", "wm", "raw/raw/u8 S {raw/u8 S}")]
    [InlineData("put2", "u", "wm", "raw/raw/u8 S {raw/u8 H}")]
    [InlineData("r13", "p", "a13", "- S {raw/u8 S}")]
    [InlineData("r15", "p", "a15", "- S {raw/u8 H, raw/u8 S}")]
    [InlineData("put3", "p", "wm", "raw/raw/u8 S {raw/u8 S}")]
    [InlineData("r20", "p", "a20", "- S {raw/u8 S}")]
    [InlineData("r20b", "p", "wm", "raw/raw/u8 S {raw/u8 S}")]
    [InlineData("r21", "p", "a2", "- S {raw/u8 S}")]
    [InlineData("r21c", "p", "a2", "- S {raw/u8 H}")]
    [InlineData("r22", "p", "a4", "- S {raw/u8 S}")]
    [InlineData("r24", "p", "a24", "- S {raw/u8 S}")]
    [InlineData("r25", "p", "a24", "- S {raw/u8 H}")]
    [InlineData("r26", "p", "a13", "- S {raw/u8 S}")]
    [InlineData("r27", "p", "a13", "- S {raw/u8 H}")]
    [InlineData("r28", "p", "a28", "- S {raw/u8 S}")]
    [InlineData("r29", "p", "a28", "- S {raw/u8 H}")]
    [InlineData("r30", "p", "a9", "- S {raw/u8 H, raw/u8 H}")]
    [InlineData("r18", "p", "gv", "raw/V S {- H {- H {- H}}}")]
    public void AParameterKeyHidesTheBindingOfTheCalledFunctionsOwnTypeParameter(string callee, string parameter, string argument, string expected)
    {
        var c = MinimalEmissionTest.Analyze(ParameterKeysSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        var function = functions.Single(x => x.Name == callee);
        var inputs = functions.Where(static x => x.Name is "args" or "gargs").SelectMany(static x => x.Parameters);
        var input = inputs.Single(x => x.InternalName == argument).Type.TypeOf()!;
        var slot = function.Parameters.Select(static x => x.InternalName).ToList().IndexOf(parameter);
        var key = TyCtxt.ParameterKey(HirTables.Of(function).Symbols[function.SyntaxId]!, slot, input);
        Assert.Equal(expected, Hiding(c.Types, key, 0));
    }

    // SPEC 15.6.4's abstract-storage sentence (PLAN G935; the Middle's C-370, its N5): TyCtxt.EntryType names a storage's entry and
    // says whether the name is the callee's kept Type, one answer of one rule. Kept: past slot's two layers, storage of f's T (the
    // output read in f's terms). Not kept: the same storage of a callee that writes the Type itself; past a raw layer T's binding
    // holds (T = `raw/(raw/u8)`: the holder it addresses, read as the input writes it, c3-5) or a safe reference it holds (T =
    // `uniq/(raw/u8)`: the holder its Loan names, B2); and a wholly hidden key of an output not read in the callee's terms (the root
    // of a by-value input that is the kept Type itself), whose answer stays the instantiated Type. Fail-before: no Kept answer on
    // 53dee7570.
    [Theory]
    [InlineData("func f<T>(slot: uniq/(uniq/T during a), value: T) => ()\n", "uniq/(uniq/(raw/u8) during a)", 2, "T kept")]
    [InlineData("func f(slot: uniq/(uniq/(raw/u8) during a), value: raw/u8) => ()\n", "uniq/(uniq/(raw/u8) during a)", 2, "raw/u8 instantiated")]
    [InlineData("func f<T>(slot: uniq/(uniq/T during a), value: raw/u8) => ()\n", "uniq/(uniq/(raw/(raw/u8)) during a)", 2, "T kept")]
    [InlineData("func f<T>(slot: uniq/(uniq/T during a), value: raw/u8) => ()\n", "uniq/(uniq/(raw/(raw/u8)) during a)", 3, "raw/u8 instantiated")]
    [InlineData("func f<T>(slot: T, value: raw/u8) => ()\n", "uniq/(raw/u8)", 1, "raw/u8 instantiated")]
    [InlineData("func f<T>(slot: T, value: raw/u8) => ()\n", "uniq/(raw/u8)", 0, "uniq/raw/u8 instantiated")]
    public void AnEntryTypeSaysWhetherItIsTheKeptType(string callee, string input, int steps, string expected)
    {
        var c = MinimalEmissionTest.Analyze(callee + $"func g(x: {input}, v: raw/u8)\n    f(x, v)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var call = KotoTree.Walk(g).OfType<InvocationKoto>().Select(static x => x.CallOf()).OfType<CallPlan>().Single();
        var written = c.Types.Retains(call.Target, c.Types.SubstOf(call), g.SymbolOf()).Writes.ToArray().Single(static x => x.Output == 0);
        var (key, into) = (TyCtxt.ParameterKey(call.Target, 0, g.Parameters[0].Type.TypeOf()!), new List<OwnLayer>());
        for (var i = 0; i < steps; i++)
        {
            into.Clear();
            Assert.True(c.Types.OwnLayers(key, into));
            key = into.Single().Past;
        }

        var (type, kept) = c.Types.EntryType(written, key);
        Assert.Equal(expected, $"{Binding.DiagnosticTypeName(type!)} {(kept ? "kept" : "instantiated")}");
    }

    // SPEC 15.6.4 "or where a callable the instantiated contract hands the callee's body ... through its result or its writes" (PLAN G935;
    // the Middle's C-370, F's rulings 40 and 45, the owner's O-398 (a)): OutputRetention.Handed lists the kept Types the output's walk
    // meets that a callable the callee's declaration hands its body writes, so a kept binding's "no reach of its own" is not the call's
    // whole reach in that Type's binding, and stays so in another kept Type's. Handed: T for a Function value writing through `uniq/T`
    // (writerFn's), a requirement with a `uniq/Self` receiver (writerReq's), a writer taking only the T. None: a requirement returning
    // Self (mkConc's), a callable reaching T only through a shared borrow, a writer through another kept Type (output 0 beside output 1,
    // which lists U), and a by-value input that is the kept Type itself, read instantiated (Kept null). The root rule's pair (G935's
    // review C1, twins I and J): a by-value input holding the kept Type (`x: Bx<T>`) meets it at a position the call does not write,
    // listed with a writer and not without one; a writer a Field of an input holds (`Hd<T, E>`, twin L) lists T. One output meeting T and
    // U (the Middle's handedOtherOk, valid, and handedSameBad): a writer of T lists T only, a writer of U lists U only, a writer of the
    // pair lists both. Fail-before: the per-Type rows do not compile on 52efd20ca (Handed was one bool per output). Under the callee's
    // identity premises (SPEC 8.2.3: `T is U` makes them one Type in f, so a writer of U writes T's binding; G935's review H1, twin K9,
    // idWriterBad's contract): T listed (fail-before on 3a8f2e251: `0 kept: -`); the same contract without the premise lists nothing.
    [Theory]
    [InlineData("func f<T, E>(slot: uniq/T, y: E, set: (uniq/T, E) -> ()) => set(slot, y@move)\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 kept: T")]
    [InlineData("contract Absorb<E>\n    func absorb(self: uniq/Self, r: E) -> ()\nstruct HoldR\n    Self is Absorb<raw/u8>\n    public var held: raw/(raw/u8)\n    public init() => self.held = null\n    public func absorb(self: uniq/Self, r: raw/u8) -> () => ()\nfunc f<T, E>(slot: uniq/T, y: E)\n    T is Absorb<E>\n    slot.absorb(y@move)\n", "x: uniq/HoldR, v: raw/u8", "x, v", "0 kept: T")]
    [InlineData("func f<T, E>(slot: uniq/T, y: E, set: (uniq/T) -> ()) => set(slot)\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8))) -> ()", "x, v, set@move", "0 kept: T")]
    [InlineData("contract MakeW\n    func make(p: raw/(raw/u8), q: raw/u8) -> Self\nstruct Wk\n    Self is MakeW\n    public var k: raw/(raw/(raw/u8))\n    public init() => self.k = null\n    public func make(p: raw/(raw/u8), q: raw/u8) -> Self => Wk.init()\nfunc f<T>(dst: uniq/T, p: raw/u8)\n    T is MakeW\n    return\n", "x: uniq/Wk, v: raw/u8", "x, v", "0 kept: -")]
    [InlineData("func f<T, E>(slot: uniq/T, y: E, get: (ref/T, E) -> ()) => ()\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, get: (ref/(raw/(raw/u8)), raw/u8) -> ()", "x, v, get@move", "0 kept: -")]
    [InlineData("func f<T, U, E>(x: uniq/T, u: uniq/U, y: E, set: (uniq/U, E) -> ()) => set(u, y@move)\n", "x: uniq/(raw/(raw/u8)), u: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, u, v, set@move", "0 kept: - | 1 kept: U")]
    [InlineData("func f<T, E>(slot: T, y: E, set: (uniq/T, E) -> ()) => ()\n", "x: raw/(raw/u8), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 instantiated")]
    [InlineData("struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E, set: (uniq/T, E) -> ()) -> ()\n    var b = x@move\n    set(b.item@uniq, y@move)\n", "p: raw/(raw/u8), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "Bx.init(item: p), v, set@move", "0 kept: T")]
    [InlineData("struct Bx<T>\n    public var item: T\n    public init(item: T) => self.item = item@move\nfunc f<T, E>(x: Bx<T>, y: E) -> ()\n    return\n", "p: raw/(raw/u8), v: raw/u8", "Bx.init(item: p), v", "0 kept: -")]
    [InlineData("struct Hd<T, E>\n    public var set: (uniq/T, E) -> ()\n    public init(set: (uniq/T, E) -> ()) => self.set = set@move\nfunc f<T, E>(slot: uniq/T, y: E, h: Hd<T, E>) => ()\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, Hd.init(set: set@move)", "0 kept: T")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/T, E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/u64, raw/u8) -> ()", "x, v, set@move", "0 kept: T")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/U, E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 kept: U")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/(T, U), E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/(u64, raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 kept: T, U")]
    [InlineData("func f<T, U, E>(slot: uniq/T, y: E, set: (uniq/U, E) -> ())\n    T is U\n    return\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 kept: T")]
    [InlineData("func f<T, U, E>(slot: uniq/T, y: E, set: (uniq/U, E) -> ()) => ()\n", "x: uniq/(raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "x, v, set@move", "0 kept: -")]
    public void AnOutputSaysWhetherAHandedCallableWritesItsKeptType(string callee, string parameters, string arguments, string expected)
    {
        var c = MinimalEmissionTest.Analyze(callee + $"func g({parameters})\n    f({arguments})\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var call = KotoTree.Walk(g).OfType<InvocationKoto>().Select(static x => x.CallOf()).OfType<CallPlan>().Single(static x => x.Target.Name == "f");
        var writes = c.Types.Retains(call.Target, c.Types.SubstOf(call), g.SymbolOf()).Writes.ToArray();
        var outputs = expected.Count(static x => x == '|') + 1; // The outputs holding a kept part: the first one, or the first two.
        Assert.Equal(expected, string.Join(" | ", writes.Where(x => x.Output < outputs).Select(static x => $"{x.Output} {(x.Kept is null ? "instantiated" : $"kept: {Listed(x.Handed.ToArray())}")}")));

        static string Listed(BoundType[] types) => types.Length == 0 ? "-" : string.Join(", ", types.Select(static x => Binding.DiagnosticTypeName(x)).Order(StringComparer.Ordinal));
    }

    // SPEC 15.6.4 (PLAN G935; the Middle's C-370, F's ruling 47): a hidden layer names the kept Type whose binding it lies in
    // (OwnLayer.Kept), the same interned Type OutputRetention.Handed lists, so a reader of the layer asks Handed for it by identity. One
    // output meeting T and U at the key `(T, U)` past slot's layer: the Middle's handedOtherOk (V: the writer writes T, U's raw layer stays
    // hidden), handedSameBad and handedBothBad (I: the writer writes U, or the pair); hidPunOtherOk (V: the writer writes U, T's raw layer
    // stays hidden) and hidPunSameBad (I: the writer writes T). A layer the declaration shows has no kept Type. Fail-before: no Kept member
    // on 110b7ff24. Under `T is U` (SPEC 8.2.3; G935's review H1, twin K1) a writer of U writes both layers' kept Types: T listed (fail-
    // before on 3a8f2e251: `T not listed`).
    [Theory]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/T, E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/u64, raw/u8) -> ()", "raw/raw/u8 U not listed")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/U, E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "raw/raw/u8 U listed")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/(T, U), E) -> ()) => ()\n", "x: uniq/(u64, raw/(raw/u8)), v: raw/u8, set: (uniq/(u64, raw/(raw/u8)), raw/u8) -> ()", "raw/raw/u8 U listed")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/U, E) -> ()) => ()\n", "x: uniq/(raw/usize, u64), v: usize, set: (uniq/u64, usize) -> ()", "raw/usize T not listed")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/T, E) -> ()) => ()\n", "x: uniq/(raw/usize, u64), v: usize, set: (uniq/(raw/usize), usize) -> ()", "raw/usize T listed")]
    [InlineData("func f<U, E>(slot: uniq/(raw/(raw/u8), U), y: E, set: (uniq/U, E) -> ()) => ()\n", "x: uniq/(raw/(raw/u8), u64), v: raw/u8, set: (uniq/u64, raw/u8) -> ()", "raw/raw/u8 shown")]
    [InlineData("func f<T, U, E>(slot: uniq/(T, U), y: E, set: (uniq/U, E) -> ())\n    T is U\n    return\n", "x: uniq/(raw/(raw/u8), raw/(raw/u8)), v: raw/u8, set: (uniq/(raw/(raw/u8)), raw/u8) -> ()", "raw/raw/u8 T listed | raw/raw/u8 U listed")]
    public void AHiddenLayerNamesTheKeptTypeHandedLists(string callee, string parameters, string expected)
    {
        var c = MinimalEmissionTest.Analyze(callee + $"func g({parameters})\n    f(x, v, set@move)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var g = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        var call = KotoTree.Walk(g).OfType<InvocationKoto>().Select(static x => x.CallOf()).OfType<CallPlan>().Single(static x => x.Target.Name == "f");
        var handed = c.Types.Retains(call.Target, c.Types.SubstOf(call), g.SymbolOf()).Writes.ToArray().Single(static x => x.Output == 0).Handed.ToArray();
        var (key, into) = (TyCtxt.ParameterKey(call.Target, 0, g.Parameters[0].Type.TypeOf()!), new List<OwnLayer>());
        Assert.True(c.Types.OwnLayers(key, into));
        key = into.Single().Past; // Past slot's own layer: the Tuple holding both parts.
        into.Clear();
        Assert.True(c.Types.OwnLayers(key, into));
        Assert.Equal(expected, string.Join(" | ", into.Where(static x => x.Layer is not null).Select(x => x.Kept is not { } kept
            ? $"{Binding.DiagnosticTypeName(x.Layer!)} shown"
            : $"{Binding.DiagnosticTypeName(x.Layer!)} {Binding.DiagnosticTypeName(kept)} {(handed.Any(y => ReferenceEquals(y, kept)) ? "listed" : "not listed")}")));
    }

    // Base steps a parameter key's Signature by Select's rule (SPEC 8.2.3, 15.6.4; review N3, N5): a generic derived struct's base, its
    // Field of U hidden beside the Field RB states, shown under the premise `U is raw/u8` and at a body's key (labels.md b1-b3).
    [Theory]
    [InlineData("rb", "raw/u8 H, raw/u8 S")]
    [InlineData("rbp", "raw/u8 S, raw/u8 S")]
    [InlineData("", "raw/u8 S, raw/u8 S")]
    public void BaseKeepsAParameterKeysPath(string callee, string expected)
    {
        var c = MinimalEmissionTest.Analyze(ParameterKeysSource);
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        var input = functions.Single(static x => x.Name == "args").Parameters.Single(static x => x.InternalName == "ab").Type.TypeOf()!;
        var function = functions.SingleOrDefault(x => x.Name == callee);
        var key = function is null ? PlaceKey.Root(input) : TyCtxt.ParameterKey(HirTables.Of(function).Symbols[function.SyntaxId]!, 0, input);
        var into = new List<OwnLayer>();
        Assert.True(c.Types.OwnLayers(key, into));
        Assert.Equal(expected, Hiding(c.Types, c.Types.Base(into.Single().Past), 1));
    }

    // Select steps a parameter key's Signature by OwnParts' rule (SPEC 8.2.3, 15.6.4): each part of the borrow's referent, then OwnLayers
    // there (r2 against r3's premise, Pair3's T, stated q and V, a Tuple's U beside a stated raw/u8).
    [Theory]
    [InlineData("r2", "a2", "raw/u8 H")]
    [InlineData("r3", "a2", "raw/u8 S")]
    [InlineData("r6", "a6", "raw/u8 H | raw/u8 S | raw/u8 H")]
    [InlineData("r9", "a9", "raw/u8 H | raw/u8 S")]
    public void SelectKeepsAParameterKeysPath(string callee, string argument, string expected)
    {
        var c = MinimalEmissionTest.Analyze(ParameterKeysSource);
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        var function = functions.Single(x => x.Name == callee);
        var input = functions.Single(static x => x.Name == "args").Parameters.Single(x => x.InternalName == argument).Type.TypeOf()!;
        var into = new List<OwnLayer>();
        Assert.True(c.Types.OwnLayers(TyCtxt.ParameterKey(HirTables.Of(function).Symbols[function.SyntaxId]!, 0, input), into));
        var referent = into.Single().Past;
        var parts = expected.Split(" | ").Select((_, i) => Hiding(c.Types, c.Types.Select(referent, i), 1));
        Assert.Equal(expected, string.Join(" | ", parts));
    }

    // Any key a body forms for its own Places hides nothing (no walked signature): the rows above with PlaceKey.Root.
    [Fact]
    public void ARootKeyHidesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ParameterKeysSource);
        var args = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "args");
        Assert.DoesNotContain(args.Parameters, x => Hiding(c.Types, PlaceKey.Root(x.Type.TypeOf()!), 0).Contains(" H", StringComparison.Ordinal));
    }

    // The contract: a warm parameter key and its OwnLayers reads, the Past keys included, allocate nothing, an identity premise's view
    // (r3's `S.Item is raw/u8`, Binding.TypeIdentity's classes) included.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmParameterKeyReadAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ParameterKeysSource);
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        var args = functions.Single(static x => x.Name == "args").Parameters;
        var calls = new[] { ("wa", "t"), ("wa", "n"), ("r3", "a2"), ("r2", "a2") }.Select(x =>
        {
            var function = functions.Single(f => f.Name == x.Item1);
            return (HirTables.Of(function).Symbols[function.SyntaxId]!, args.Single(a => a.InternalName == x.Item2).Type.TypeOf()!);
        }).ToArray();
        var (types, into, past) = (c.Types, new List<OwnLayer>(16), new List<OwnLayer>(16));
        void Read()
        {
            foreach (var (symbol, input) in calls)
            {
                into.Clear();
                _ = types.OwnLayers(TyCtxt.ParameterKey(symbol, 0, input), into);
                foreach (var layer in into)
                {
                    past.Clear();
                    _ = types.OwnLayers(layer.Past, past);
                }
            }
        }

        Read();
        Assert.Equal(0, AllocationMeasurement.Measure(Read));
    }

    // The keys' layers with their Hidden bit, and each Past key's nested (four keys deep).
    private static string Hiding(TyCtxt types, PlaceKey key, int depth)
    {
        var into = new List<OwnLayer>();
        Assert.True(types.OwnLayers(key, into));
        return string.Join(", ", into.Select(x =>
        {
            var row = $"{(x.Layer is null ? "-" : Binding.DiagnosticTypeName(x.Layer))} {(x.Hidden ? "H" : "S")}";
            var below = depth < 3 && x.Past.Type is not null ? Hiding(types, x.Past, depth + 1) : string.Empty;
            return below.Length == 0 ? row : $"{row} {{{below}}}";
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
