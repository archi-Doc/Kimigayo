// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 5.4: P@raw is the address of the written Place P, a raw/V where V is the stored Type of P. It needs no unsafe context,
// performs the checks of an immediately ending @ref (initialization, read capability, Loan conflicts), reads no value and leaves
// no Loan; @raw/U only converts.
public class RawAddressTest
{
    private const string Point = "struct Point\n    public var x: i32\n    public var y: i32\n    public init(x: i32, y: i32)\n        self.x = x\n        self.y = y\n";

    [Theory]
    [InlineData("var number: i32 = 1\n    let address = number@raw\n", "raw/i32")]
    [InlineData("var number: i32 = 1\n    let pointer = number@raw\n    let address = pointer@raw\n", "raw/raw/i32")]
    [InlineData("var number: i32 = 1\n    let r = number@uniq\n    let address = r@follow@raw\n", "raw/i32")]
    [InlineData("var number: i32 = 1\n    let r = number@ref\n    let address = r@raw\n", "raw/ref/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let address = point.y@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let address = point@raw\n", "raw/Point")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = (*p).y@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = p[0].x@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = (*p)@raw\n", "raw/Point")]
    [InlineData("var values: [3 of i32] = [1, 2, 3]\n    let p = values@raw\n    unsafe\n        let address = (*p)[1]@raw\n", "raw/i32")]
    public void TheAddressHasTheStoredTypeOfTheWrittenSlot(string body, string type)
    {
        var source = Point + "public func main()\n    " + body;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var address = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "address");
        Assert.Equal(type, Text(address.TypeOf()!));
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // The pointer keeps no Loan, so later writes to the Place and accesses through the pointer coexist.
    [Fact]
    public void TheAddressKeepsNoLoan()
    {
        var source = "public func main()\n    var number: i32 = 1\n    let pointer = number@raw\n    number += 1\n    unsafe => *pointer = 3\n    let other = number@uniq\n    other@follow += 1\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // The address is checked as an immediately ending shared borrow.
    [Theory]
    [InlineData("var number: i32 = 1\n    let r = number@uniq\n    let pointer = number@raw\n    r@follow += 1\n")]
    [InlineData("var number: i32\n    let pointer = number@raw\n")]
    [InlineData("var text = \"a\"\n    let moved = text@move\n    let pointer = text@raw\n")]
    public void TheAddressChecksItsPlaceLikeAnImmediatelyEndingBorrow(string body)
    {
        var c = MinimalEmissionTest.Analyze("public func main()\n    " + body);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    // SPEC 5.4, 8.11.2: in an effect summary the address is an access to its Place, an environment effect only on a mutable static.
    [Theory]
    [InlineData("    let slot = self.count@raw\n", true)]
    [InlineData("    let p = self@raw\n        var slot = p@raw/i32\n        unsafe => slot = (*p).count@raw\n", true)]
    [InlineData("    let slot = Metrics.puts@raw\n", false)]
    public void TheAddressIsAnAccessToItsPlace(string body, bool valid)
    {
        const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\ngroup Metrics\n    public var puts: i32 = 0\n" +
            "struct AddressSink\n    Self is Sink\n    var count: i32 = 0\n    public func put(self: uniq/Self, value: i32)\n    ";
        var c = MinimalEmissionTest.Analyze(Sink + body + "        unsafe => *slot = value\npublic func main() => ()\n");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(valid || !c.Accepts, MinimalEmissionTest.Describe(c));

        // R6-check H3b T40: the bound is A4's record at `Self is Sink` (EffectBoundImplementationTest.AssertBound), beside Binding's located
        // Unsupported_Kd at the static's address (no static raw authority, DECISIONS BU-24), which keeps Binding incomplete. Since Binding
        // is incomplete, its completeness cannot show a returning Binding effect record, so Binding's issues are counted: only the
        // address's. Every row also has P42's StaticStorage record at the settable Metrics.puts (DECISIONS R5-M82; STATUS Static members),
        // so a valid row is refused by it until P42 (group review tests-b N1).
        Assert.Equal(valid ? 0 : 1, c.Binding.Issues.Count);
        (DiagnosticCode, SyntaxLocation) storage = (DiagnosticCode.Unsupported_Kd, MirTargetSide.At(Sink, "var puts: i32 = 0"));
        (DiagnosticCode, SyntaxLocation)[] records =
            [(DiagnosticCode.Unsupported_Kd, new("Metrics.puts@raw")), (DiagnosticCode.IncompatibleContractImplementation_Kd, new("Self is Sink")), storage];
        MirTargetSide.Check(c, valid ? [storage] : records);
    }

    // SPEC 13.5.6: a slot storing a raw pointer is borrowed like any other slot.
    [Fact]
    public void APointerSlotIsBorrowable()
    {
        var source = "public func main()\n    var number: i32 = 1\n    var pointer = number@raw\n    let slot = pointer@ref\n    let copied: raw/i32 = slot@follow\n    let edit = pointer@uniq\n    edit@follow = null\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ABareConversionTakesNoAddress()
    {
        var source = "public func main()\n    var number: i32 = 1\n    let pointer = number@raw\n    let address = pointer@raw/u8\n    let integer = pointer@usize\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var address = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "address");
        Assert.Equal("raw/u8", Text(address.TypeOf()!));
    }

    [Fact]
    public void WritesThroughTheAddressReachThePlace()
    {
        var source = Point + """
            func bump(pointer: raw/i32)
                unsafe => *pointer += 1
            var number: i32 = 1
            let pointer = number@raw
            bump(pointer)
            bump(number@raw)
            require number == 3 else => $abort("local")
            var point = Point.init(1, 2)
            bump(point.y@raw)
            require point.x == 1 and point.y == 3 else => $abort("field")
            var r = number@uniq
            bump(r@follow@raw)
            r@follow += 1
            require number == 5 else => $abort("referent")
            var slot = pointer
            let indirect = slot@raw
            unsafe => *indirect = null
            require slot == null else => $abort("pointer slot")
            Console.writeLine("addressed")
            """;
        ScalarEmissionTest.EmitFixture("RawAddressWrite", source, "addressed\n");
    }

    // SPEC 5.4, 5.2.2: the address of a raw Place part is displaced from the pointer; nothing is read.
    [Fact]
    public void WritesThroughARawPlacePartAddressReachThePart()
    {
        var source = Point + """
            func bump(pointer: raw/i32)
                unsafe => *pointer += 1
            var point = Point.init(1, 2)
            let p = point@raw
            unsafe
                bump((*p).y@raw)
                bump(p[0].x@raw)
            require point.x == 2 and point.y == 3 else => $abort("field")
            var values: [3 of i32] = [1, 2, 3]
            let q = values@raw
            var i: isize = 2
            unsafe
                bump((*q)[i]@raw)
                bump((*q)[0]@raw)
            require values[0] == 2 and values[1] == 2 and values[2] == 4 else => $abort("element")
            Console.writeLine("parts")
            """;
        ScalarEmissionTest.EmitFixture("RawAddressPart", source, "parts\n");
    }

    // SPEC 5.7 (Typed paths): P@raw states the Origin of the last borrow or raw pointer P is reached through, whatever the borrow's mode;
    // through no dereference, or through a pointer that states none, it states none.
    [Theory]
    [InlineData("var number: i32 = 1\n    let r = number@uniq\n    let address = r@follow@raw\n", "r")]
    [InlineData("var number: i32 = 1\n    let r = number@ref\n    let address = r@follow@raw\n", "r")]
    [InlineData("var number: i32 = 1\n    let address = number@raw\n", null)]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = (*p).y@raw\n", null)]
    public void ATypedPathStatesTheOriginItIsReachedThrough(string body, string? borrow)
    {
        var c = MinimalEmissionTest.Analyze(Point + "public func main()\n    " + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var fields = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().ToArray();
        var raw = fields.Single(static x => x.NameKoto.IdentifierName == "address").TypeOf()!.RawOrigin;
        if (borrow is null)
        {
            Assert.Null(raw);
            return;
        }

        Assert.Same(fields.Single(x => x.NameKoto.IdentifierName == borrow).TypeOf()!.Origin, raw);
    }

    // PLAN G821 (the owner's O-237 (a), until U8): a view between pointee Types that are not the same, either holding a nonnull Option
    // (IMPL 21.1.3) in its own storage, is one located Unsupported_Kd at its written pointee Type, whether or not an access uses it: a payload
    // write, a pun, a C struct's first Field, an Option nested in a Tuple, the target side and an Option<Option<R>>'s inner Option, and
    // the miscompiled probes (a payload read, a whole write at the first Field, two C first Fields deep, a later Field read as a word,
    // raw storage placed and read, an rc payload; G808's N2 is the first Field row's view). A same-Type conversion (SPEC 5.4), Typed Null
    // Formation and a view of storage holding none (tagged Options) are not gated, nor is a pointee that reaches a Type parameter in a
    // generic body (`view`, `viewOpt`: the named limit, a known defect until U8).
    [Theory]
    [InlineData("var h: Option<ref/i32> = .Some(x@ref)\n    let q = h@raw@raw/(ref/i32)\n    unsafe => *q = x@ref\n", "h@raw@raw/(ref/i32)")]
    [InlineData("let h: Option<ref/i32> = .Some(x@ref)\n    let q = h@raw@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "h@raw@raw/u64")]
    [InlineData("var n = Node.init()\n    let q = n@raw@raw/(obj/Box)\n    unsafe => (*q).v = 7\n", "n@raw@raw/(obj/Box)")]
    [InlineData("var t: (u8, Option<ref/i32>) = (1, .Some(x@ref))\n    let q = t@raw@raw/u8\n", "t@raw@raw/u8")]
    [InlineData("let m = Raw.allocate<u64>(2)\n    let q = m@raw/(Option<obj/Box>)\n", "m@raw/(Option<obj/Box>)")]
    [InlineData("var h: Option<Option<ref/i32>> = .None\n    let q = h@raw@raw/u8\n", "h@raw@raw/u8")]
    [InlineData("var z: i32 = 0\n    let h: Option<ref/i32> = .Some(x@ref)\n    let q = h@raw@raw/(ref/i32)\n    var r: ref/i32 = z@ref\n    unsafe => r = *q\n", "h@raw@raw/(ref/i32)")]
    [InlineData("var n = Node.init()\n    let q = n@raw@raw/(obj/Box)\n    unsafe => *q = Box.init(v: 9)@obj\n", "n@raw@raw/(obj/Box)")]
    [InlineData("let o = Outer.init()\n    let q = o@raw@raw/(obj/Box)\n    var w: u64 = 0\n    unsafe => w = (*q).v\n", "o@raw@raw/(obj/Box)")]
    [InlineData("let t = Later.init()\n    let q = t@raw@raw/u64\n    var w: u64 = 0\n    unsafe => w = q[1]\n", "t@raw@raw/u64")]
    [InlineData("let m = Raw.allocate<u64>(2)\n    let po = m@raw/(Option<obj/Box>)\n    unsafe => Raw.initialize(po, .Some(Box.init(v: 4)@obj))\n    let pr = m@raw/(obj/Box)\n    var w: u64 = 0\n    unsafe => w = (*pr).v\n    unsafe => _ = (*po)@move\n    unsafe => Raw.release(m)\n", "m@raw/(Option<obj/Box>)")]
    [InlineData("let o: Option<rc/Box> = .Some(Box.init(v: 4)@rc)\n    let q = o@raw@raw/(rc/Box)\n    var w: u64 = 0\n    unsafe => w = (*q).v\n", "o@raw@raw/(rc/Box)")]
    [InlineData("var h: Option<i32> = .Some(5)\n    let q = h@raw@raw/u8\n", null)]
    [InlineData("var h: Option<(ref/i32, i32)> = .Some((x@ref, 5))\n    let q = h@raw@raw/u8\n", null)]
    [InlineData("var h: Option<raw/u8> = .None\n    let q = h@raw@raw/u8\n", null)]
    [InlineData("var h: Option<ref/i32> = .Some(x@ref)\n    let q = h@raw@raw/(Option<ref/i32>)\n", null)]
    [InlineData("let q = null@raw/(Option<ref/i32>)\n", null)]
    [InlineData("var a: [2 of u32] = [3, 4]\n    let q = a@raw@raw/u8\n", null)]
    [InlineData("var h: Option<ref/i32> = .Some(x@ref)\n    let q = view(h@raw)\n", null)]
    [InlineData("let h: Option<obj/Box> = .Some(Box.init(v: 4)@obj)\n    let q = viewOpt(h@raw)\n", null)]
    public void AViewOfANonnullOptionIsUnsupportedUntilU8(string body, string? view)
    {
        const string Node = "struct Box\n    public var v: u64\n    public init(v: u64) => self.v = v\n#Layout(\"C\")\nstruct Node\n    public var link: Option<obj/Box>\n" +
            "    public var tail: u64\n    public init()\n        self.link = .Some(Box.init(v: 0)@obj)\n        self.tail = 5\n#Layout(\"C\")\nstruct Outer\n    public var inner: Node\n" +
            "    public var k: u64\n    public init()\n        self.inner = Node.init()\n        self.k = 2\n#Layout(\"C\")\nstruct Later\n    public var a: u64\n" +
            "    public var link: Option<obj/Box>\n    public init()\n        self.a = 0\n        self.link = .Some(Box.init(v: 4)@obj)\n" +
            "func view<T>(p: raw/T) -> raw/u8 => p@raw/u8\nfunc viewOpt<T>(p: raw/(Option<T>)) -> raw/u64 => p@raw/u64\n";
        var c = MinimalEmissionTest.Analyze(Node + "public func main()\n    var x: i32 = 1\n    " + body);
        MirTargetSide.Check(c, view is null ? [] : [(DiagnosticCode.Unsupported_Kd, MirTargetSide.At(view, view[(view.LastIndexOf('@') + 1)..]))]);
    }

    // PLAN G821 (F's rulings 2, 8): the over-gate. The view is judged whole, so a view the tagged layout compiles correctly today is the
    // same located Unsupported_Kd until U8: a view no access uses, an array's element view, layout-equal C structs and a byte arena
    // placing and reading one Type that holds a nonnull Option. A pointee at Binding's storage limits is TypeDagScalingTest's raw depth
    // 70 row; MirBorrowsTest's nNestOk keeps A5's record beside the gate's.
    [Theory]
    [InlineData("", "var x: i32 = 1\n    var h: Option<ref/i32> = .Some(x@ref)\n    let q = h@raw@raw/u8\n", "h@raw@raw/u8")]
    [InlineData("", "var x: i32 = 1\n    var a: [2 of Option<ref/i32>] = [.Some(x@ref), .None]\n    let q = a@raw@raw/(Option<ref/i32>)\n", "a@raw@raw/(Option<ref/i32>)")]
    [InlineData("#Layout(\"C\")\nstruct A\n    public var link: Option<obj/Box>\n    public var n: u64\n    public init()\n        self.link = .Some(Box.init(v: 0)@obj)\n        self.n = 7\n#Layout(\"C\")\nstruct B\n    public var link: Option<obj/Box>\n    public var n: u64\n", "let a = A.init()\n    let q = a@raw@raw/B\n    var w: u64 = 0\n    unsafe => w = (*q).n\n", "a@raw@raw/B")]
    [InlineData("struct Node\n    public var link: Option<obj/Box>\n    public var v: u64\n    public init()\n        self.link = .Some(Box.init(v: 1)@obj)\n        self.v = 3\n", "let m = Raw.allocate<u64>(8)\n    let p = m@raw/Node\n    unsafe => Raw.initialize(p, Node.init())\n    var w: u64 = 0\n    unsafe => w = (*p).v\n    unsafe => _ = (*p)@move\n    unsafe => Raw.release(m)\n", "m@raw/Node")]
    public void TheGateAlsoRejectsViewsTheTaggedLayoutCompiles(string declarations, string body, string view)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "struct Box\n    public var v: u64\n    public init(v: u64) => self.v = v\npublic func main()\n    " + body);
        MirTargetSide.Check(c, (DiagnosticCode.Unsupported_Kd, MirTargetSide.At(view, view[(view.LastIndexOf('@') + 1)..])));
    }

    // PLAN G821 (the owner's O-252 (a); the Middle's G821-5): an Option whose Some payload is a pair layer is undecided at Binding
    // (TyCtxt.NonnullOption) where the binder's admitted set meets IMPL 21.1.3's R list, so the gate rejects its view as it does a
    // nonnull Option (the Middle's four natives: `obj`, `object` at an obj and an rc instance, `owner or obj`; `ref` with an Origin), also
    // in a Tuple, as an Option's Case payload and past a raw layer; a set outside the list (`owner`, `raw`) is decided and not gated.
    [Theory]
    [InlineData("s is obj", "Option<s/Box>", "let q = p@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "var h: Option<obj/Box> = .Some(Box.init(v: 1)@obj)\n    rd<obj/Box>(h@raw)\n", "p@raw/u64")]
    [InlineData("s is object", "Option<s/Box>", "let q = p@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "var h: Option<obj/Box> = .Some(Box.init(v: 1)@obj)\n    rd<obj/Box>(h@raw)\n", "p@raw/u64")]
    [InlineData("s is object", "Option<s/Box>", "let q = p@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "var h: Option<rc/Box> = .Some(Box.init(v: 1)@rc)\n    rd<rc/Box>(h@raw)\n", "p@raw/u64")]
    [InlineData("s is owner or obj", "Option<s/Box>", "let q = p@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "var h: Option<obj/Box> = .Some(Box.init(v: 1)@obj)\n    rd<obj/Box>(h@raw)\n", "p@raw/u64")]
    [InlineData("s is ref", "Option<s/u64 during a>", "let q = p@raw/u64\n    var w: u64 = 0\n    unsafe => w = *q\n", "", "p@raw/u64")]
    [InlineData("s is owner", "Option<s/u64>", "usep(p@raw/u8)\n", "", null)]
    [InlineData("s is obj", "(u8, Option<s/Box>)", "usep(p@raw/u8)\n", "", "p@raw/u8")]
    [InlineData("s is owner", "(u8, Option<s/Box>)", "usep(p@raw/u8)\n", "", null)]
    [InlineData("s is obj", "Option<Option<s/Box>>", "usep(p@raw/u8)\n", "", "p@raw/u8")]
    [InlineData("s is obj", "raw/(Option<s/Box>)", "usep(p@raw/(raw/u64))\n", "", "p@raw/(raw/u64)")]
    [InlineData("s is raw", "Option<s/Box>", "usep(p@raw/u8)\n", "", null)]
    public void AnOptionOfAPairPayloadIsGatedWhereItsSetMeetsTheList(string premise, string pointee, string view, string main, string? at)
    {
        var c = MinimalEmissionTest.Analyze($"struct Box\n    public var v: u64\n    public init(v: u64) => self.v = v\nfunc usep<T>(p: raw/T) => ()\n" +
            $"func rd<s/T>(p: raw/({pointee}))\n    {premise}\n    {view}public func main()\n    {main}");
        MirTargetSide.Check(c, at is null ? [] : [(DiagnosticCode.Unsupported_Kd, MirTargetSide.At(at, at[(at.LastIndexOf('@') + 1)..]))]);
    }

    // PLAN G821 revision 1 (F's rulings 5-7): the gate reads through the raw layers a pointee holds (SPEC 5.2: a punned raw pointer has
    // the provenance of the raw pointer whose bytes it reads), one level, two levels, in a Tuple and at a C struct's first Field, but an
    // Option-free raw layer and a raw-layer cycle with no Option are not gated; a generic body's `raw/(Option<ref/T>)` is a nonnull
    // Option for every T.
    [Theory]
    [InlineData("", "let q = slot@raw@raw/(raw/(obj/Box))\n", "slot@raw@raw/(raw/(obj/Box))")]
    [InlineData("#Layout(\"C\")\nstruct HP\n    public var p: raw/(Option<obj/Box>)\n    public var k: u64\n    public init(p: raw/(Option<obj/Box>))\n        self.p = p\n        self.k = 0\n", "let s = HP.init(p: slot)\n    let q = s@raw@raw/(raw/(obj/Box))\n", "s@raw@raw/(raw/(obj/Box))")]
    [InlineData("", "let s2 = slot@raw\n    let q = s2@raw@raw/(raw/(raw/(obj/Box)))\n", "s2@raw@raw/(raw/(raw/(obj/Box)))")]
    [InlineData("", "var t: (raw/(Option<obj/Box>), u64) = (slot, 0)\n    let q = t@raw@raw/u8\n", "t@raw@raw/u8")]
    [InlineData("", "var y: u64 = 1\n    let s3: raw/u64 = y@raw\n    let q = s3@raw@raw/(raw/u8)\n", null)]
    [InlineData("struct L\n    public var next: raw/L\n    public var v: u64\n", "let m = Raw.allocate<u64>(2)\n    let q = m@raw/L\n", null)]
    [InlineData("func viewRef<T>(p: raw/(Option<ref/T during a>)) -> u64\n    let q = p@raw/u64\n    unsafe => return *q\n", "", "p@raw/u64")]
    public void TheGateReadsThroughRawLayers(string declarations, string body, string? view)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "struct Box\n    public var v: u64\n    public init(v: u64) => self.v = v\npublic func main()\n" +
            "    let h: Option<obj/Box> = .Some(Box.init(v: 5)@obj)\n    let slot: raw/(Option<obj/Box>) = h@raw\n    " + body);
        MirTargetSide.Check(c, view is null ? [] : [(DiagnosticCode.Unsupported_Kd, MirTargetSide.At(view, view[(view.LastIndexOf('@') + 1)..]))]);
    }

    // PLAN G821 revision 1 (ruling 5): a view of a Type on an inline storage cycle, plain or expanding (SPEC 6.2), keeps the cycle's own
    // record alone: the walk does not enter the declaration, so it ends, and adds no record that would read the failed declaration.
    [Theory]
    [InlineData("struct S\n    public var next: Option<S>\n", "S")]
    [InlineData("struct G<T>\n    public var v: T\n    public var next: Option<G<(T, T)>>\n", "G<u8>")]
    public void AViewOfAnInlineCycleKeepsTheCyclesRecordAlone(string declaration, string type)
    {
        var c = MinimalEmissionTest.Analyze(declaration + $"public func main()\n    let m = Raw.allocate<u8>(8)\n    let q = m@raw/({type})\n    unsafe => Raw.release(m)\n");
        Assert.Equal("InvalidInlineLayout_Kd", string.Join(" ", c.Binding.Issues.Select(static x => x.Code.ToString()).Distinct()));
    }

    // PLAN G821 revision 2 (ruling 11): the gate is the adaptation table's Pointer row (CheckAdaptationCase), so a pair binder's raw case
    // is gated as a written conversion is (G821-2); its record is at the written pointee Type and the conversion stays bound, so the
    // conversion's own record (a claim outside an unsafe context), its operand's (`h@raw` under a live `uniq`) and a use's (`*q` outside
    // an unsafe context) stay beside it (G821-1, SPEC 23.1.2); the pair form over an Option-free pointee is not gated. A view of an
    // Option of a pair payload (O-252 (a)) keeps a use's record beside the gate's.
    [Theory]
    [InlineData("func f<s/T>(y: s/(Option<obj/Box>)) -> u64\n    s is raw\n    let q = y@raw/u64\n    unsafe => return *q\n", "", "Unsupported_Kd raw/u64")]
    [InlineData("#Layout(\"C\")\nstruct W\n    public var link: Option<obj/Box>\n    public var k: u64\nfunc f<s/T>(y: s/W) -> u64\n    s is raw\n    let q = y@raw/u64\n    unsafe => return *q\n", "", "Unsupported_Kd raw/u64")]
    [InlineData("func f<s/T>(y: s/(u64)) -> u32\n    s is raw\n    let q = y@raw/u32\n    unsafe => return *q\n", "", "")]
    [InlineData("", "let q = h@raw@(raw/(obj/Box) during static)\n    usep(q)\n", "UnsatisfiedOriginRelation_Kd h@raw | Unsupported_Kd (raw/(obj/Box) during static)")]
    [InlineData("", "let m = Raw.allocate<u64>(2)\n    let q = m@(raw/(Option<obj/Box>) during static)\n    usep(q)\n    unsafe => Raw.release(m)\n", "UnsatisfiedOriginRelation_Kd m | Unsupported_Kd (raw/(Option<obj/Box>) during static)")]
    [InlineData("", "let u = h@uniq\n    let q = h@raw@raw/u64\n    useu(u)\n    usep(q)\n", "ComparisonLoanConflict_Kd h@raw | Unsupported_Kd raw/u64")]
    [InlineData("", "let q = h@raw@raw/u64\n    let w = *q\n", "UnsafeBlockRequired_Kd *q | Unsupported_Kd raw/u64")]
    [InlineData("func f<s/T>(p: raw/(Option<s/Box>)) -> u64\n    s is obj\n    let q = p@raw/u64\n    return *q\n", "", "UnsafeBlockRequired_Kd *q | Unsupported_Kd raw/u64")]
    public void TheGateIsThePointerRowAndKeepsTheConversionBound(string declarations, string body, string records)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "struct Box\n    public var v: u64\n    public init(v: u64) => self.v = v\nfunc usep<T>(p: raw/T) => ()\n" +
            "func useu<T>(x: uniq/T) => ()\npublic func main()\n    var h: Option<obj/Box> = .Some(Box.init(v: 1)@obj)\n    " + body);
        Assert.Equal(records, string.Join(" | ", MirTargetSide.MirSide(c).Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    private static string Text(BoundType type)
        => type.Kind == BoundTypeKind.Semantics ? type.Semantics.ToString().ToLowerInvariant() + "/" + Text(type.Components[0]) : type.Name;
}
