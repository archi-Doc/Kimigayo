// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// IMPL 21.1.3's rows and the sequence handles (SPEC 4.8.6, 4.9.5) in Layout.Of, which the old pool and gate read (R5 P7a), and
// struct, Tuple, fixed-array, enum, Closure and Function Item placement with its OK/Limit/CLayout result (R5 P7b, S2-S4 X1, X18, X31);
// one Layout per compilation, its memo for the program version, the count and depth bounds, the outcomes and their precedence (R5 P7c,
// DECISIONS R5-M37).
public partial class LayoutTest
{
    private const string CLayoutGeneric = "#Layout(\"C\")\nstruct P<T>\n    var n: i32\n    var value: T\n";

    // Exactly 2^63 - 1 bytes (649657 * 92737 * 153092023), every count within 2^31 - 1.
    private const string M = "[649657 of [92737 of [153092023 of u8]]]";

    [Theory]
    [InlineData("i8", "i8", "i8", 1)]
    [InlineData("u8", "i8", "i8", 1)]
    [InlineData("bool", "i8", "i1", 1)]
    [InlineData("i16", "i16", "i16", 2)]
    [InlineData("u16", "i16", "i16", 2)]
    [InlineData("i32", "i32", "i32", 4)]
    [InlineData("u32", "i32", "i32", 4)]
    [InlineData("char", "i32", "i32", 4)]
    [InlineData("f32", "float", "float", 4)]
    [InlineData("i64", "i64", "i64", 8)]
    [InlineData("u64", "i64", "i64", 8)]
    [InlineData("isize", "i64", "i64", 8)]
    [InlineData("usize", "i64", "i64", 8)]
    [InlineData("f64", "double", "double", 8)]
    [InlineData("i128", "i128", "i128", 16)]
    [InlineData("u128", "i128", "i128", 16)]
    public void ScalarsFollowTheProfileRows(string type, string storage, string computation, int size)
    {
        // IMPL 21.1.3: size, alignment and stride are equal; arguments pass the computation Type; the old gate reads the record.
        var value = Layout.Row(BoundType.Primitives[type]);
        Assert.Equal((storage, size, size, size, computation, computation), Row(value));
        Assert.Same(value, WindowsLowering.GetValue(BoundType.Primitives[type]));
    }

    // DECISIONS R5-M92: no Layout alignment exceeds 16, the heap's (SPEC 5.6, 21.4.3), which Raw.allocate's row relies on in place of the
    // old generator's refusal: every row is at most 16 (the primitives', a borrow's, a raw pointer's, a handle's, a function value's and
    // the sequence handles') and a placement takes its parts' maximum (IMPL 21.1.2), in C mode too.
    [Fact]
    public void NoLayoutAlignmentExceedsTheHeapAlignment()
    {
        var c = CompilationTestHelper.BindSuccess(
            "struct S\n    var a: i128\n    var b: u8\n#Layout(\"C\")\nstruct C\n    var a: u128\n    var b: i8\n" +
            "func probe(s: S, c: C, t: (i128, (u128, i8)), a: [3 of i128], o: Option<i128>) => ()");
        Assert.All(BoundType.Primitives.Values, x => Assert.InRange(c.Layout.Of(x).Value!.Layout.Alignment, 1, 16));
        Assert.All(Parameters(c), x => Assert.Equal(16, c.Layout.Of(x).Value!.Layout.Alignment));
        var rows = CompilationTestHelper.BindSuccess(
            "func probe(r: ref/i32, p: raw/i32, h: obj/i32, f: (i32) -> i32, s: Slice<i32>, a: Array<i32>, d: Dictionary<i32, i32>) => ()");
        Assert.All(Parameters(rows), x => Assert.InRange(rows.Layout.Of(x).Value!.Layout.Alignment, 1, 16));
    }

    [Fact]
    public void UnitStringAndNeverUseTheRuntimeRecords()
    {
        Assert.Same(RuntimeAbi.Unit, Layout.Row(BoundType.Unit));
        Assert.Same(RuntimeAbi.String, Layout.Row(BoundType.String));

        // IMPL 21.1.3: Never has size 0, alignment 1 and stride 0, laid out as Unit; the old gate keeps it without a record (X31).
        var never = Layout.Row(BoundType.Never);
        Assert.Same(RuntimeAbi.Unit, never);
        Assert.Equal(("void", 0, 1, 0, "void", null), Row(never));
        Assert.Null(WindowsLowering.GetValue(BoundType.Never));
    }

    [Fact]
    public void ANeverPayloadIsZeroSizedWhileTheOldPoolKeepsItsNull()
    {
        // SPEC 3.1.3: Option<Never> is valid. Layout lays the stored Never out as zero-sized (X31): the tag, then an empty payload
        // union; the old pool keeps its null (STATUS).
        var c = CompilationTestHelper.BindSuccess("let a: Option<Never> = .None");
        var option = Variable(c, "a");
        Assert.Same(BoundType.Never, option.Components[0]);
        var value = c.Layout.Of(option).Value!;
        Assert.Equal(("{ i32, { [0 x i8], [0 x i8] } }", 4, 4, 4, "{ i32, { [0 x i8], [0 x i8] } }", "ptr"), Row(value));
        Assert.Equal(new[] { 0, 4 }, value.Layout.FieldOffsets.ToArray());
        Assert.Null(new AggregateLayoutPool(c.Layout).Get(option));
    }

    [Fact]
    public void EveryLocalOfTheNeverExampleIsLaidOut()
    {
        // SPEC 3.1.3's example through MIR construction: each local of its body, Option<Never> and Result<i32, Never> included, is laid
        // out (X31).
        var c = MirBuildTest.Compile("func use(v: i32) => ()\nfunc f(r: Result<i32, Never>)\n    let a: Option<Never> = .None\n    let b = a\n" +
            "    match r\n        .Ok(let v) => use(v)\n        .Err(_) => ()\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.Contains(body.Locals.ToArray(), static x => x.Type.Kind == BoundTypeKind.Constructed && ReferenceEquals(x.Type.Components[0], BoundType.Never));
        Assert.All(body.Locals.ToArray(), x => Assert.Equal(Layout.Outcome.Ok, c.Layout.Of(x.Type).Outcome));
    }

    [Theory]
    [InlineData("struct S\n    var a: u8\n    var b: i64\n    var c: u16", "S", "{ [0 x i64], [16 x i8] }", 16, 8, "10,0,8")]
    [InlineData("#Layout(\"C\")\nstruct S\n    var a: u8\n    var b: u64\n    var c: u8", "S", "{ i8, i64, i8 }", 24, 8, "0,8,16")]
    [InlineData("", "(u8, i32, bool)", "{ [0 x i32], [8 x i8] }", 8, 4, "4,0,5")]
    [InlineData("", "(i32, ())", "{ [0 x i32], [4 x i8] }", 4, 4, "0,4")]
    [InlineData("", "[3 of i16]", "{ [0 x i16], [6 x i8] }", 6, 2, "")]
    [InlineData("", "[0 of i64]", "{ [0 x i64], [0 x i8] }", 0, 8, "")]
    [InlineData("", "Option<i32>", "{ i32, { [0 x i32], [4 x i8] } }", 8, 4, "0,4")]
    [InlineData("", "Option<i128>", "{ i32, { [0 x i128], [16 x i8] } }", 32, 16, "0,16")]
    [InlineData("", "Option<()>", "{ i32, { [0 x i8], [0 x i8] } }", 4, 4, "0,4")]
    public void PlacementGroupsByAlignmentOrKeepsTheWrittenCOrder(string declarations, string type, string storage, int size, int alignment, string offsets)
    {
        // IMPL 21.1.2: Kimigayo mode groups the parts by descending alignment, each group in written order (a zero-sized part last);
        // C mode keeps the written order; an enum is an i32 tag, then its payload union. The old pool's record is Layout's.
        var c = CompilationTestHelper.BindSuccess(declarations + "\nfunc probe(value: " + type + ") => ()");
        var probed = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        var value = c.Layout.Of(probed).Value!;
        Assert.Equal((storage, size, alignment, size, storage, "ptr"), Row(value));
        Assert.Equal(offsets, string.Join(',', value.Layout.FieldOffsets.ToArray()));
        Assert.Same(value, new AggregateLayoutPool(c.Layout).Get(probed)!.Value);
    }

    [Fact]
    public void ClosuresAndFunctionItemsPlaceTheirStoredParts()
    {
        // A Closure stores its captures like a Tuple; a Function Item's Components are generic arguments, so it stores nothing.
        var c = CompilationTestHelper.BindSuccess("func g<T>() -> i32 => 1\nlet item = g<i64>\nlet x: u8 = 4\nlet y: i64 = 1\nlet f = func [x, y] () -> i64 => y");
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var environment = c.Layout.Of(closure).Value!;
        Assert.Equal(("{ [0 x i64], [16 x i8] }", 16, 8), (environment.Layout.StorageType, environment.Layout.Size, environment.Layout.Alignment));
        Assert.Equal(new[] { 8, 0 }, environment.Layout.FieldOffsets.ToArray());
        Assert.Same(environment, new AggregateLayoutPool(c.Layout).Get(closure)!.Value);

        var item = Variable(c, "item");
        Assert.Equal(BoundTypeKind.FunctionItem, item.Kind);
        Assert.Same(c.Layout.Of(c.Binding.SelfType(c.Library.GetSymbol(KimiDeclarationId.Loan)!)).Value, c.Layout.Of(item).Value);
    }

    [Theory]
    [InlineData("E", "{ [0 x i32], [16 x i8] }", 16, 4, "12")]
    [InlineData("D", "{ [0 x i64], [32 x i8] }", 32, 8, "24,16")]
    public void TheCompleteBaseIsAPrefixOfTheDerivedLayout(string derivedName, string storage, int size, int alignment, string offsets)
    {
        // S2-S4 X1: B places its C-layout Field (8 bytes, alignment 4) at 0 and t at 8, so its data ends at 9 and its size 12
        // includes 3 bytes of tail padding. E's u follows the complete base at 12 (in the padding it would be at 9, size 12); D's
        // Fields follow it in their groups (w at 16, u at 24). A derived struct has no C layout (SPEC 6.7.1), so the base precedes
        // only Kimigayo-mode Fields. The old pool's records are Layout's.
        var c = CompilationTestHelper.BindSuccess("#Layout(\"C\")\nstruct C\n    var x: u8\n    var y: i32\nopen struct B\n    var c: C\n    var t: u8\nstruct E: B\n    var u: u8\nstruct D: B\n    var u: u8\n    var w: i64\nlet p: raw/" + derivedName + " = null");
        var derived = Variable(c, "p").Components[0];
        var whole = c.Layout.Of(derived).Value!;
        var prefix = c.Layout.Of(AdtDef.Base(derived)!).Value!;
        Assert.Equal(("{ [0 x i32], [12 x i8] }", 12, 4), (prefix.Layout.StorageType, prefix.Layout.Size, prefix.Layout.Alignment));
        Assert.Equal(new[] { 0, 8 }, prefix.Layout.FieldOffsets.ToArray());
        Assert.Equal((storage, size, alignment), (whole.Layout.StorageType, whole.Layout.Size, whole.Layout.Alignment));
        Assert.Equal(offsets, string.Join(',', whole.Layout.FieldOffsets.ToArray()));

        var old = new AggregateLayoutPool(c.Layout).Get(derived)!;
        Assert.Same(whole, old.Value);
        Assert.Same(prefix, old.Base!.Value);
    }

    [Theory]
    [InlineData("let x: Option<P<()>> = .None")]
    [InlineData("let x: Option<P<Never>> = .None")]
    [InlineData("let x: Option<(i32, Q<P<[0 of u8]>>)> = .None")]
    public void ACLayoutInstanceWithAZeroSizedFieldHasNoLayoutAtAnyDepth(string use)
    {
        // S2-S4 X18, X31: the result names the C-layout instance that stores a zero-sized Field (a stored Never counts), however deep;
        // the old pool refuses the Type without a resource limit.
        var c = CompilationTestHelper.Parse(CLayoutGeneric + "struct Q<T>\n    var value: T\n" + use);
        c.Bind();
        var option = Variable(c, "x");
        var result = c.Layout.Of(option);
        Assert.Equal((Layout.Outcome.CLayout, null), (result.Outcome, result.Value));
        Assert.Equal("P", result.Instance!.Name);
        Assert.Equal(0, c.Layout.Of(result.Instance.Components[0]).Value!.Layout.Size);
        var pool = new AggregateLayoutPool(c.Layout);
        Assert.Null(pool.Get(option));
        Assert.Null(pool.ResourceLimitFailure);
    }

    [Fact]
    public void AnInferredCLayoutInstanceHasNoLayout()
    {
        // S2-S4 X18: f<T> writes Option<P<T>>, valid for every T at the declaration; the instance f<()> stores P<()>.
        var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "func f<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = f(())\nlet s = f(1)");
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf()).Where(static x => x is { Target.Name: "f" }).ToArray();
        var declared = Variable(c, "x");
        var instances = calls.Select(x => c.Types.Apply(declared, c.Types.SubstOf(x!)!)!).ToArray();
        Assert.Equal([BoundType.Unit, BoundType.I32], instances.Select(static x => x.Components[0].Components[0]));
        Assert.Same(instances[0].Components[0], c.Layout.Of(instances[0]).Instance);
        Assert.Equal(("{ i32, { [0 x i32], [8 x i8] } }", 12), (c.Layout.Of(instances[1]).Value!.Layout.StorageType, c.Layout.Of(instances[1]).Value!.Layout.Size));
    }

    [Theory]
    [InlineData("", "[1073741824 of i32]")]
    [InlineData("", "(bool, [2147483647 of u8])")]
    [InlineData("enum Big\n    A([2147483647 of u8])\n    B\n", "Big")]
    [InlineData("", M)]
    [InlineData("", "[2 of [1073741824 of u8]]")]
    public void LimitsNameTheirBound(string declarations, string type)
    {
        // IMPL 21.3.2: sizes and offsets are bounded by 2^31 - 1, a SizeLimit with neither a record nor an instance (R5-M37); M, exactly
        // 2^63 - 1 bytes, is a limit, not Oversized (R5-M52).
        var c = CompilationTestHelper.BindSuccess(declarations + "func probe(value: " + type + ") => ()");
        Assert.Equal(new Layout.Result(Layout.Outcome.SizeLimit, null, null, false), c.Layout.Of(Parameters(c)[0]));
    }

    [Theory]
    [InlineData("[2147483648 of ()]", "CountLimit", 0)]
    [InlineData("[2147483647 of ()]", "Ok", 0)]
    [InlineData("[2147483648 of u8]", "CountLimit", 0)]
    public void TheCountBoundCountsZeroSizedElements(string type, string outcome, int size)
    {
        // IMPL 21.3.2: a fixed array holds at most 2^31 - 1 elements, whatever its stride; the count is checked before the size.
        var c = CompilationTestHelper.BindSuccess("func probe(value: " + type + ") => ()");
        var result = c.Layout.Of(Parameters(c)[0]);
        Assert.Equal((Enum.Parse<Layout.Outcome>(outcome), size), (result.Outcome, result.Value?.Layout.Size ?? 0));
    }

    [Fact]
    public void CLayoutOutranksLimits()
    {
        // SPEC 23.1, IMPL 21.3.2: a limit never hides a C-layout failure; Rejected storage yields to an independent limit or CLayout.
        var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "func probe(value: [1073741824 of i32]) => ()");
        var p = P(c, BoundType.Unit);
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, false), c.Layout.Of(c.Types.Tuple([Parameters(c)[0], p])));

        var failed = CompilationTestHelper.Parse(
            CLayoutGeneric + "struct Node\n    let value: i32\n    let next: Node\nfunc probe(value: [2147483648 of u8]) => ()");
        Assert.False(failed.Bind().IsComplete);
        var node = Declared(failed, "Node");
        p = P(failed, BoundType.Unit);
        Assert.Equal(Layout.Outcome.CountLimit, failed.Layout.Of(failed.Types.Tuple([node, Parameters(failed)[0]])).Outcome);
        Assert.Equal(new Layout.Result(Layout.Outcome.Rejected, null, node, false), failed.Layout.Of(failed.Types.Tuple([node, BoundType.I32])));
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, false), failed.Layout.Of(failed.Types.Tuple([node, p])));
    }

    [Theory]
    [InlineData("struct Node\n    let value: i32\n    let next: Node\n", "Node", "")]
    [InlineData("struct Box<T>\n    let value: T\nstruct E\n    let b: Box<E>\n", "E", "")]
    [InlineData("struct Box<T>\n    let value: T\nstruct E\n    let b: Box<E>\n", "E.b", "E")]
    [InlineData("enum E<T>\n    Empty\n    Again(E<E<T>>)\nlet x = E<i32>.Empty\n", "E<i32>", "")]
    [InlineData("struct S\n    var a: Missing\n", "S", "")]
    [InlineData("", "[-1 of i32]", "")]
    public void RejectedStorageIsDerived(string source, string probe, string instance)
    {
        // R5-M37: after a failed Binding, a declaration on an inline storage cycle (AdtDef.InlineCycle), a stored Type Binding did not
        // resolve and an unresolved fixed-array length give Rejected (the probed Type itself unless `instance` names the declaration),
        // which a Type storing them by value takes on; nothing throws.
        var c = CompilationTestHelper.Parse(source + "let y = 1");
        Assert.Equal(source.Length == 0, c.Bind().IsComplete);
        var type = probe switch
        {
            "E.b" => AdtDef.FieldType(Declared(c, "E"), 0)!,
            "E<i32>" => c.Types.Intern(BoundTypeKind.Constructed, Declared(c, "E").Symbol, SemanticsKind.Owner, [BoundType.I32]),
            "[-1 of i32]" => c.Types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [BoundType.I32], length: -1),
            _ => Declared(c, probe),
        };
        var expected = instance.Length == 0 ? type : Declared(c, instance);
        Assert.Equal(new Layout.Result(Layout.Outcome.Rejected, null, expected, false), c.Layout.Of(type));
    }

    [Theory]
    [InlineData("enum E\n    A(Missing)\n    B(i32)\n", "Rejected")]
    [InlineData("enum E\n    A(Missing)\n    B([9223372036854775807 of i64])\n", "Oversized")]
    [InlineData("enum E\n    A(Missing)\n    B([2147483648 of u8])\n", "CountLimit")]
    [InlineData("struct E\n    var a: Missing\n    var b: [9223372036854775807 of i64]\n", "Oversized")]
    public void ARejectedCaseYieldsToAnotherCasesOutcome(string source, string outcome)
    {
        // R5-M52, DECISIONS BU-37 (G242): an enum's Case whose payload Binding did not substitute is a part Layout does not place, as a
        // struct's Field is, so another Case's Oversized or limit takes precedence over Rejected (fail-before: the enum Rejected at once).
        var c = CompilationTestHelper.Parse(source + "let y = 1");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(outcome, c.Layout.Of(Declared(c, "E")).Outcome.ToString());
    }

    [Fact]
    public void AnAbstractTypeIsRejected()
    {
        // DECISIONS R5-M155 (PLAN G293, G187): an abstract Type, which no closed Type holds, is Rejected naming itself, and a Type storing it
        // takes that on: after a failed Binding a qualified projection that names no Type, in a complete compilation an open Type (a broken
        // contract the collector records at its use). Before, Layout.Row threw.
        var failed = CompilationTestHelper.Parse("contract Iter<T>\n    associate Tag\nstruct B\n    Self is Iter<i32>\n    associate Tag is bool\n" +
            "let t: B.(Iter<u8>).Tag = true");
        Assert.False(failed.Bind().IsComplete);
        var projection = Variable(failed, "t");
        Assert.Equal(BoundTypeKind.AssociatedProjection, projection.Kind);
        Assert.Equal(new Layout.Result(Layout.Outcome.Rejected, null, projection, false), failed.Layout.Of(failed.Types.Tuple([BoundType.I32, projection])));

        var complete = CompilationTestHelper.BindSuccess("func probe<T>(value: T) => ()");
        var slot = Parameters(complete)[0];
        Assert.Equal(new Layout.Result(Layout.Outcome.Rejected, null, slot, false), complete.Layout.Of(slot));
    }

    [Fact]
    public void AnUnresolvedBaseIsLaidOutWithoutIt()
    {
        // R5-M37: Binding publishes an unresolved base as no base, so the struct is laid out without it; no record follows the failure.
        var c = CompilationTestHelper.Parse("struct D: Missing\n    var a: u8\nlet y = 1");
        Assert.False(c.Bind().IsComplete);
        var result = c.Layout.Of(Declared(c, "D"));
        Assert.Equal((Layout.Outcome.Ok, 1), (result.Outcome, result.Value!.Layout.Size));
    }

    [Theory]
    [InlineData("Tuple", 64, "Ok")]
    [InlineData("Tuple", 65, "DepthLimit")]
    [InlineData("Option", 64, "Ok")]
    [InlineData("Option", 66, "DepthLimit")]
    [InlineData("Struct", 64, "Ok")]
    [InlineData("Struct", 65, "DepthLimit")]
    public void DepthIsThePlacementHeight(string shape, int height, string outcome)
    {
        // IMPL 21.3.2, R5-M37: a row is 0 high and a placed Type 1 more than its highest part, an enum's Case payload Tuple counting as
        // a part (Option<ref/i32> is 2 high); over 64 is DepthLimit, whichever Type is laid out first.
        var chain = new StringBuilder("struct S1\n    var v: i32\n");
        for (var i = 2; i <= 65; i++)
        {
            chain.Append("struct S").Append(i).Append("\n    var v: S").Append(i - 1).Append('\n');
        }

        for (var order = 0; order < 2; order++)
        {
            var c = CompilationTestHelper.BindSuccess(chain.ToString());
            var whole = Chain(c, shape, height);
            var part = Chain(c, shape, height - 2);
            if (order == 0)
            {
                Assert.Equal(Layout.Outcome.Ok, c.Layout.Of(part).Outcome);
            }

            Assert.Equal(Enum.Parse<Layout.Outcome>(outcome), c.Layout.Of(whole).Outcome);
            Assert.Equal(Layout.Outcome.Ok, c.Layout.Of(part).Outcome);
        }
    }

    [Theory]
    [InlineData("()", 1, "CLayout")]
    [InlineData("()", 63, "CLayout")]
    [InlineData("()", 64, "DepthLimit")]
    [InlineData("0-Tuple", 62, "CLayout")]
    [InlineData("0-Tuple", 63, "DepthLimit")]
    public void DepthLimitHidesNoVisibleCLayout(string field, int level, string outcome)
    {
        // R5-M37: P<X> placed at `level` beside a Type 65 high, in either part order and either query order (the parts first, then
        // the whole, or the whole first in a fresh compilation). A CLayout whose zero-sized Field is laid out within the 64-level window
        // outranks DepthLimit; one placed at level 64 or below, or whose Field reaches past the window (a placed zero-sized Field, the
        // 0-Tuple, is 1 high, so its P is visible down to level 62), stays behind DepthLimit, as in the old pool.
        var expected = Enum.Parse<Layout.Outcome>(outcome);
        for (var first = 0; first < 2; first++)
        {
            var verdicts = new Layout.Result[2];
            for (var order = 0; order < 2; order++)
            {
                var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "()");
                var p = P(c, field == "()" ? BoundType.Unit : c.Types.Tuple([]));
                var placed = Wrap(c, p, level - 1);
                var deep = Wrap(c, BoundType.I32, 65);
                var whole = first == 0 ? c.Types.Tuple([placed, deep]) : c.Types.Tuple([deep, placed]);
                if (order == 0)
                {
                    c.Layout.Of(deep);
                    c.Layout.Of(placed);
                }

                verdicts[order] = c.Layout.Of(whole);
                Assert.Equal(Layout.Outcome.DepthLimit, c.Layout.Of(deep).Outcome);
                Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, false), c.Layout.Of(placed));
                Assert.Equal((expected, expected == Layout.Outcome.CLayout ? p : null), (verdicts[order].Outcome, verdicts[order].Instance));
            }

            Assert.Equal(verdicts[0].Outcome, verdicts[1].Outcome);
        }
    }

    // IMPL 21.1.1, DECISIONS R5-M52: a size, stride or Field offset over 2^63 - 1 is Oversized, measured exactly: one byte over M, an
    // array product, padding that rounds 2^63 - 1 up (Kimigayo grouping; C written order), a limited part's size, an enum's tag, a
    // zero-length array of an oversized element; at or below the bound a count or size limit stays.
    [Theory]
    [InlineData("", "(" + M + ", u8)", "Oversized")]
    [InlineData("", "[9223372036854775807 of u8]", "CountLimit")]
    [InlineData("", "[9223372036854775807 of u16]", "Oversized")]
    [InlineData("", "[4611686018427387903 of u16]", "CountLimit")]
    [InlineData("", "(u16, [9223372036854775805 of u8])", "Oversized")]
    [InlineData("#Layout(\"C\")\nstruct W\n    var a: u8\n    var b: [1152921504606846974 of u64]\n    var c: u8\n", "W", "Oversized")]
    [InlineData("struct W\n    var a: u8\n    var b: [1152921504606846974 of u64]\n    var c: u8\n", "W", "CountLimit")]
    [InlineData("", "[4 of [4611686018427387904 of u8]]", "Oversized")]
    [InlineData("enum Big\n    A(" + M + ")\n    B\n", "Big", "Oversized")]
    [InlineData("", "[0 of (" + M + ", u8)]", "Oversized")]
    public void TheLayoutBoundIsTwoToTheSixtyThreeMinusOne(string declarations, string type, string outcome)
    {
        var c = CompilationTestHelper.BindSuccess(declarations + "func probe(value: " + type + ") => ()");
        var result = c.Layout.Of(Parameters(c)[0]);
        var expected = Enum.Parse<Layout.Outcome>(outcome);
        Assert.Equal(new Layout.Result(expected, null, null, expected == Layout.Outcome.Oversized), result);
    }

    // DECISIONS R5-M52: a CLayout outranks Oversized and both hold at once (Result.Oversized), in either part order, while beside a part
    // that brings the Tuple to 2^63 - 4 bytes it is not oversized; Oversized outranks limits and Rejected parts, a lower bound sufficing; a
    // C-layout instance storing an exactly zero-sized, count-limited Field is CLayout (KC7), one storing an exactly zero-sized CLayout
    // instance names that inner instance, and one whose Field's size is not exact (a Rejected part) is a limit.
    [Fact]
    public void CLayoutAndOversizedOutrankTheRest()
    {
        var c = CompilationTestHelper.BindSuccess(CLayoutGeneric + "#Layout(\"C\")\nstruct Z<T>\n    var value: T\n" +
            "func probe(over: (" + M + ", u8), wide: [9223372036854775800 of u8], zeros: [2147483648 of ()]) => ()");
        var (over, wide, zeros) = (Parameters(c)[0], Parameters(c)[1], Parameters(c)[2]);
        var p = P(c, BoundType.Unit);
        var inner = c.Types.Intern(BoundTypeKind.Constructed, Declared(c, "Z").Symbol, SemanticsKind.Owner, [BoundType.Unit]);
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, true), c.Layout.Of(c.Types.Tuple([p, over])));
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, true), c.Layout.Of(c.Types.Tuple([over, p])));
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, p, false), c.Layout.Of(c.Types.Tuple([p, wide])));
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, P(c, zeros), false), c.Layout.Of(P(c, zeros)));
        Assert.Equal(new Layout.Result(Layout.Outcome.CLayout, null, inner, false), c.Layout.Of(P(c, inner)));

        var failed = CompilationTestHelper.Parse(CLayoutGeneric + "struct Node\n    let value: i32\n    let next: Node\n" +
            "func probe(a: [9223372036854775807 of u16], b: [4611686018427387904 of u8], z: [2147483648 of ()]) => ()");
        Assert.False(failed.Bind().IsComplete);
        var node = Declared(failed, "Node");
        var (a, b, z) = (Parameters(failed)[0], Parameters(failed)[1], Parameters(failed)[2]);
        Assert.Equal(Layout.Outcome.Oversized, failed.Layout.Of(failed.Types.Tuple([node, a])).Outcome);
        Assert.Equal(Layout.Outcome.Oversized, failed.Layout.Of(failed.Types.Tuple([node, b, b])).Outcome);
        Assert.Equal(Layout.Outcome.CountLimit, failed.Layout.Of(failed.Types.Tuple([node, b])).Outcome);
        Assert.Equal(Layout.Outcome.CountLimit, failed.Layout.Of(P(failed, failed.Types.Tuple([node, z]))).Outcome);
    }

    // DECISIONS R5-M52 (R5-M37's window, extended): an oversize the walk sees within the 64-level window outranks DepthLimit, in either
    // part order and either query order (the parts first, or the whole first in a fresh compilation); one that needs bytes placed past
    // the window stays behind DepthLimit. An oversized array placed at level 63 beside a Type 65 high is seen, at 64 not; two arrays of
    // 2^62 bytes beside it are over by a lower bound, one is not.
    [Theory]
    [InlineData("array", 63, "Oversized")]
    [InlineData("array", 64, "DepthLimit")]
    [InlineData("pair", 0, "Oversized")]
    [InlineData("single", 0, "DepthLimit")]
    public void DepthLimitHidesNoVisibleOversize(string shape, int level, string outcome)
    {
        var expected = Enum.Parse<Layout.Outcome>(outcome);
        for (var first = 0; first < 2; first++)
        {
            for (var order = 0; order < 2; order++)
            {
                var c = CompilationTestHelper.BindSuccess("func probe(a: [9223372036854775807 of u16], b: [4611686018427387904 of u8]) => ()");
                var (array, half) = (Parameters(c)[0], Parameters(c)[1]);
                var deep = Wrap(c, BoundType.I32, 65);
                BoundType[] parts = shape switch
                {
                    "array" => [Wrap(c, array, level - 1), deep],
                    "pair" => [half, half, deep],
                    _ => [half, deep],
                };
                if (first == 1)
                {
                    Array.Reverse(parts);
                }

                if (order == 0)
                {
                    foreach (var part in parts)
                    {
                        c.Layout.Of(part);
                    }
                }

                var whole = c.Layout.Of(c.Types.Tuple(parts));
                Assert.Equal((expected, expected == Layout.Outcome.Oversized), (whole.Outcome, whole.Oversized));
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task SharedPartsArePlacedOnce()
    {
        // R5-M37: placement is linear over shared parts. Tk = (Tk-1, Tk-1) over the 0-Tuple stores 2^k leaves, so placing T40 again
        // for every use of a part would not end in time, nor would re-placing unmemoized parts past the bound for T100.
        var c = CompilationTestHelper.BindSuccess("()");
        var tuples = new BoundType[101];
        tuples[0] = c.Types.Tuple([]);
        for (var k = 1; k < tuples.Length; k++)
        {
            tuples[k] = c.Types.Tuple([tuples[k - 1], tuples[k - 1]]);
        }

        await Task.Run(
            () =>
            {
                var shared = c.Layout.Of(tuples[40]);
                Assert.Equal((Layout.Outcome.Ok, 0), (shared.Outcome, shared.Value!.Layout.Size));
                Assert.Same(shared.Value, new AggregateLayoutPool(c.Layout).Get(tuples[40])!.Value);
                Assert.Equal(Layout.Outcome.DepthLimit, c.Layout.Of(tuples[100]).Outcome);
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(SemanticsKind.Ref)]
    [InlineData(SemanticsKind.Uniq)]
    [InlineData(SemanticsKind.Raw)]
    [InlineData(SemanticsKind.ObjRef)]
    [InlineData(SemanticsKind.ObjUniq)]
    public void BorrowsAndRawPointersAreOnePointer(SemanticsKind semantics)
    {
        // IMPL 21.1.3: 8 / 8 / 8, whatever the referent.
        var value = Layout.Row(new BoundType("p", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String]));
        Assert.Same(RuntimeAbi.StringReference, value);
        Assert.Equal(("ptr", 8, 8, 8, "ptr", "ptr"), Row(value));
    }

    [Theory]
    [InlineData(SemanticsKind.Obj)]
    [InlineData(SemanticsKind.Rc)]
    [InlineData(SemanticsKind.Arc)]
    public void ObjectHandlesAreOneWordForEveryPayload(SemanticsKind semantics)
    {
        var handle = new BoundType("h", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.I32]);
        var value = Layout.Row(handle);
        Assert.Equal(("[8 x i8]", 8, 8, 8, "[8 x i8]", "ptr"), Row(value));
        Assert.Same(value, Layout.Row(new BoundType("h", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.String])));
        Assert.Same(value, new AggregateLayoutPool(new Layout()).Get(handle)!.Value);
    }

    [Fact]
    public void FunctionValuesAreTwoWords()
    {
        // IMPL 21.1.3: a common function value is 16 bytes with alignment 8: code and environment words.
        var function = new BoundType("fn", BoundTypeKind.Function, components: [BoundType.I32, BoundType.I32]);
        var value = Layout.Row(function);
        Assert.Equal(("[16 x i8]", 16, 8, 16, "[16 x i8]", "ptr"), Row(value));
        Assert.Equal(new[] { 0, 8 }, value.Layout.FieldOffsets.ToArray());
        Assert.Same(value, new AggregateLayoutPool(new Layout()).Get(function)!.Value);
    }

    [Theory]
    [InlineData(BoundTypeKind.Slice, 2)]
    [InlineData(BoundTypeKind.Array, 3)]
    [InlineData(BoundTypeKind.Dictionary, 7)]
    public void SequenceHandlesArePointerAlignedWordsForEveryElement(BoundTypeKind kind, int words)
    {
        // SPEC 4.8.6, 4.9.5: the buffer pointer, then the length and further words; the old pool's handle record is Layout's.
        BoundType[] components = kind == BoundTypeKind.Dictionary ? [BoundType.String, BoundType.I32] : [BoundType.I32];
        var sequence = new BoundType("s", kind, components: components);
        var value = Layout.Row(sequence);
        var storage = "{ [0 x i64], [" + (words * 8) + " x i8] }";
        Assert.Equal((storage, words * 8, 8, words * 8, storage, "ptr"), Row(value));
        Assert.Equal(Enumerable.Range(0, words).Select(static x => x * 8), value.Layout.FieldOffsets.ToArray());
        Assert.Same(value, Layout.Row(new BoundType("s", kind, components: [.. components.Select(static _ => BoundType.Boolean)])));

        var layout = new AggregateLayoutPool(new Layout()).Get(sequence)!;
        Assert.Same(value, layout.Value);
        Assert.Equal(words, layout.Fields.Length);
    }

    // SPEC 4.7.3, 22.2 (DECISIONS R5-M124): each handle row names its length word, which a Len reads: a Slice's and an Array's Field 1, a
    // Dictionary's DictionaryStorage.Handle Field `length`, its live count; dictionaryStorage's {address, stride} Tuple lies at 0 and 8, the
    // words P2a's DictionaryLayout kernel stores.
    [Fact]
    public void TheHandlesLengthWordsAndTheDictionaryLayoutTupleAreTheKernelsWords()
    {
        var c = MirBuildTest.Compile("func f(entries: uniq/Dictionary<i32, i64>) => entries.reserve(1)\n");
        var dictionary = c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type)
            .Select(static x => TyCtxt.Layer(x, out var below, out _) == HirPlaceKind.Deref ? below! : x)
            .First(static x => x.Kind == BoundTypeKind.Dictionary && !x.ContainsParameter);
        var handle = c.Library.DictionaryUnlink.Parameters[0].Type.TypeOf()!.Components[0];
        Assert.Equal(AdtDef.IndexOf(handle, "length"), Layout.LengthWord(dictionary));
        Assert.Equal(3, Layout.LengthWord(dictionary));
        Assert.Equal(1, Layout.LengthWord(new BoundType("a", BoundTypeKind.Array, components: [BoundType.I32])));
        Assert.Equal(1, Layout.LengthWord(new BoundType("s", BoundTypeKind.Slice, components: [BoundType.I32])));
        var tuple = c.Types.Tuple([c.Types.Pointer(dictionary), BoundType.ISize]);
        Assert.Equal(new[] { 0, 8 }, c.Layout.Of(tuple).Value!.Layout.FieldOffsets.ToArray());
    }

    [Fact]
    public void LoanIsPlacedAsAStructWithoutStoredFields()
    {
        // IMPL 21.1.3: Loan<T> has size 0, alignment 1 and stride 0, the one record of a struct without stored Fields.
        var c = CompilationTestHelper.BindSuccess("()");
        var loan = c.Binding.SelfType(c.Library.GetSymbol(KimiDeclarationId.Loan)!);
        var value = c.Layout.Of(loan).Value!;
        Assert.Equal(("{ [0 x i8], [0 x i8] }", 0, 1, 0, "{ [0 x i8], [0 x i8] }", "ptr"), Row(value));
        Assert.Equal(0, value.Layout.FieldOffsets.Length);
        Assert.Same(value, new AggregateLayoutPool(c.Layout).Get(loan)!.Value);
    }

    // SPEC 22.2 (DECISIONS R5-M121): borrowStorageUniq over a UniqSlice runs the Array's BorrowStorage helper, which reads the buffer and the
    // length at an Array handle's words 0 and 8: a UniqSlice's storage and length lie there, its Loan zero-sized.
    [Fact]
    public void AUniqSlicesStorageAndLengthLieAtTheArrayHandlesWords()
    {
        var c = MirBuildTest.Compile("func f(values: uniq/Array<i64>) -> i64\n    var view = values.sliceUniq()\n    return view[0]\n");
        var slice = c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type)
            .First(x => x.Symbol?.LibraryDeclaration == KimiDeclarationId.UniqSlice && !x.ContainsParameter);
        var value = c.Layout.Of(slice).Value!;
        Assert.Equal([0, 8], value.Layout.FieldOffsets.ToArray()[..2]);
        Assert.Equal(16, value.Layout.Size);
    }

    // DECISIONS R5-M155: Of gives an abstract Type Rejected (AnAbstractTypeIsRejected, where the parameter this test probed before moved);
    // a kind outside the rows, placement and abstract Types, here the direct range marker, breaks Layout's input contract in any
    // compilation, an internal error.
    [Fact]
    public void ATypeWithoutARowIsAnInternalError()
        => Assert.Throws<InvalidOperationException>(() => new Layout().Of(BoundType.Range));

    // SPEC 5.7, R5-M37 (DECISIONS BU-24): a raw Origin is no part of a layout; the memo is keyed by the interned Type, so a Type that
    // differs in a raw Origin alone is its own entry with the same layout.
    [Fact]
    public void ARawOriginChangesNoLayout()
    {
        var c = CompilationTestHelper.BindSuccess("func probe(r: ref/i32, p: (raw/i32 during r, isize), q: (raw/i32, isize)) => ()");
        var parameters = Parameters(c);
        Assert.NotSame(parameters[1], parameters[2]);
        Assert.Equal(Row(c.Layout.Of(parameters[2]).Value!), Row(c.Layout.Of(parameters[1]).Value!));
    }

    private static (string Storage, int Size, int Alignment, int Stride, string Computation, string? Argument) Row(ValueLowering value)
        => (value.Layout.StorageType, value.Layout.Size, value.Layout.Alignment, value.Layout.Stride, value.ComputationType, value.ArgumentType);

    private static (Layout.Outcome Outcome, string? Storage, int Size, string Offsets, BoundType? Instance) Verdict(Layout.Result result)
        => (result.Outcome, result.Value?.Layout.StorageType, result.Value?.Layout.Size ?? -1,
            string.Join(',', result.Value?.Layout.FieldOffsets.ToArray() ?? []), result.Instance);

    private static BoundType Variable(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == name).NameKoto.TypeOf()!;

    private static BoundType[] Parameters(Compilation c)
        => [.. KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters.Select(static x => x.Type.TypeOf()!)];

    // A declared struct's or enum's own Type.
    private static BoundType Declared(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name).SymbolOf()!.Type!;

    // CLayoutGeneric's instance P<argument>.
    private static BoundType P(Compilation c, BoundType argument)
        => c.Types.Intern(BoundTypeKind.Constructed, Declared(c, "P").Symbol, SemanticsKind.Owner, [argument]);

    // `count` one-component Tuples around `type`, one placement level each.
    private static BoundType Wrap(Compilation c, BoundType type, int count)
    {
        for (var i = 0; i < count; i++)
        {
            type = c.Types.Tuple([type]);
        }

        return type;
    }

    // A Type `height` levels high: one-component Tuples over i32, Options over ref/i32 (2 levels each) or the struct chain S1..S65.
    private static BoundType Chain(Compilation c, string shape, int height)
    {
        if (shape == "Struct")
        {
            return Declared(c, "S" + height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (shape == "Tuple")
        {
            return Wrap(c, BoundType.I32, height);
        }

        var type = c.Types.Reference(SemanticsKind.Ref, BoundType.I32);
        for (var i = 0; i < height / 2; i++)
        {
            type = c.Types.Intern(BoundTypeKind.Constructed, c.Library.Option, SemanticsKind.Owner, [type]);
        }

        return type;
    }

    private static BoundType Closure(Compilation c)
        => Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
}
