// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G140, DECISIONS BU-19: an interned Type is a DAG, so the parts of a Type may be shared, and every walk over a Type's parts
/// decides each distinct part once per query. Checking a program whose Types share parts is then linear in the distinct parts: each
/// scaling row sizes its shape so that a walk that re-visits shared parts meets 2^26 or more paths (hours) while the DAG walk takes
/// milliseconds; a regression shows as a hang, which the runner's process timeout reports, as <see cref="InferenceSlotScalingTest"/>'s
/// does.</summary>
public partial class TypeDagScalingTest
{
    // PLAN G375: a kept Proven is read, so a signature is walked once however many calls read it.
    [Fact]
    public void EachProvenSignatureIsWalkedOnce()
    {
        Assert.Equal(Walks(2), Walks(40));

        static int Walks(int calls)
        {
            var source = "struct Box<T>\n    T is Copy\n    public init() => ()\nfunc take(b: ref/Box<i32>) -> i32 => 1\n" +
                string.Concat(Enumerable.Range(0, calls).Select(static i => $"func c{i}(b: ref/Box<i32>) -> i32 => take(b)\n")) + "let present = 1";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.SignatureWalkWork;
        }
    }

    // Binding's walks: the instance Types of a Tuple chain (TyCtxt.Apply, from the checked set), the capability proofs of HIR publication
    // over inferred shared Tuples, closed and generic, through the MIR, its analyses and the checked set.
    [Theory]
    [InlineData("chain", 28)]
    [InlineData("chainValues", 28)]
    [InlineData("lets", 28)]
    [InlineData("genericLets", 26)]
    public void BindingWalksAreLinearInDistinctParts(string shape, int size)
    {
        var c = MirBuildTest.Compile(Source(shape, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(program.Records(MirRecordClass.Collection));
        if (shape.StartsWith("chain", StringComparison.Ordinal))
        {
            Assert.Equal(size + 1, Instances(set, "f"));
        }
    }

    // SPEC 9.6 (R7-N2c): member lookup's walk is linear in the layers it reads, on a chain and on a cycle, where its test may go three times round.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberWalksAreLinearInTheLayers(bool cycle)
    {
        var work = new[] { 100, 200, 400 }.Select(Work).ToArray();
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 400, string.Join(' ', work));

        int Work(int layers)
        {
            var chain = string.Concat(Enumerable.Range(1, layers - 1).Select(static i => $"open struct C{i}: C{i - 1}\n"));
            var misses = string.Concat(Enumerable.Repeat("    v.none()\n", 10));
            var head = cycle ? $"open struct C0: C{layers - 1}" : "open struct C0";
            var c = CompilationTestHelper.ParseSuccess($"{head}\n    public func top(self) -> i32 => 1\n{chain}func f(v: ref/C{layers - 1}) -> i32\n{misses}    return v.top()\n");
            c.Bind();
            return c.Binding.MemberWalkWork;
        }
    }

    // SPEC 8.6.2 (PLAN G602): a reversed chain of clauses through inherited conformances (L_i: S_i.HasBase.Parent, S_i's Parent L_(i-1),
    // declared from the far end) binds with no stack failure, and the InheritedConformances query's clause readings beyond those of the
    // same chain written nominally are linear in its length.
    [Fact]
    public void InheritedConformanceDemandsAreLinearInTheChain()
    {
        var work = new[] { 64, 128, 256 }.Select(static levels => Work(levels, true) - Work(levels, false)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 64, string.Join(' ', work));

        static int Work(int levels, bool projected)
        {
            var chain = string.Concat(Enumerable.Range(1, levels).Reverse().Select(i => (projected ? $"open struct L{i}: S{i}.HasBase.Parent\n" : $"open struct L{i}: L{i - 1}\n") + $"    public init(): base() => ()\nstruct S{i}\n    Self is HasBase\n    associate HasBase.Parent is L{i - 1}\n"));
            var c = CompilationTestHelper.ParseSuccess($"contract HasBase\n    associate Parent\nopen struct Root\n    public func r(self) -> i32 => 1\n    public init() => ()\nstruct D: L{levels}.HasBase.Parent\n    public init(): base() => ()\nfunc f(d: ref/D) -> i32 => d.r()\n{chain}open struct L0\n    Self is HasBase\n    associate HasBase.Parent is Root\n    public init() => ()\n");
            Assert.True(c.Bind().IsComplete);
            return c.Binding.InheritedConformanceWork;
        }
    }

    // SPEC 6.2.2 (PLAN G602, design review N2): on a base cycle every member's InheritedConformances query stays NotStarted for the pass,
    // so each conformance read of a member walks the cycle again: the query's clause readings for ten more reads are linear in its length.
    [Fact]
    public void AConformanceReadOfABaseCycleIsLinearInTheCycle()
    {
        var work = new[] { 64, 128, 256 }.Select(static length => Work(length, 20) - Work(length, 10)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 64, string.Join(' ', work));

        static int Work(int length, int reads)
        {
            var ring = string.Concat(Enumerable.Range(0, length).Select(i => $"open struct A{i}: A{(i + 1) % length}\n    public init(): base() => ()\n"));
            var uses = string.Concat(Enumerable.Range(0, reads).Select(static i => $"func g{i}(a: ref/A0.HasBase.Parent) => ()\n"));
            var c = CompilationTestHelper.ParseSuccess($"contract HasBase\n    associate Parent\n{ring}{uses}");
            Assert.False(c.Bind().IsComplete);
            return c.Binding.InheritedConformanceWork;
        }
    }

    // SPEC 9.4, 9.6 (R7-N2d): a protected read tests its derivation over BaseLayers, so the walks' steps per read are linear in the layers.
    [Fact]
    public void ProtectedReadsAreLinearInTheLayers()
    {
        var work = new[] { 10, 20, 40 }.Select(static layers => Work(layers, 20) - Work(layers, 10)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 40, string.Join(' ', work));

        static int Work(int layers, int reads)
        {
            var chain = string.Concat(Enumerable.Range(1, layers - 1).Select(static i => $"open struct C{i}: C{i - 1}\n"));
            var body = string.Concat(Enumerable.Range(0, reads).Select(static i => $"        let a{i} = self.pm()\n"));
            var c = MinimalEmissionTest.Analyze($"open struct C0\n    protected func pm(self) -> i32 => 1\n{chain}struct D: C{layers - 1}\n    func f(self) -> i32\n{body}        return 1\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.BaseLayerWork;
        }
    }

    // SPEC 8.2: two closures of one generic function in a shared Tuple close in each instance's substitution; the Tuple's shared parts
    // are closed once and stay shared, and every part equals the part instantiated alone.
    [Fact]
    public void ClosuresOfASharedTupleCloseInEachInstance()
    {
        const string Source = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () => x\n    let g = func [x] () => x\n    let p = ((f, g), (f, g))\n" +
            "    let q = (p, p)\n    let r = q.0.0.0()\n    let s = q.1.1.1()\n    return\npublic func main() -> ()\n    make(1)\n    make(true)\n    return\n";
        var c = MirBuildTest.Compile(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var environments = nodes.OfType<FunctionKoto>().Where(static x => x.IsAnonymous).Select(static x => x.ClosureOf()!.EnvironmentType!).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "make").Select(x => c.Types.SubstOf(x.CallOf()!)!).ToArray();
        Assert.Equal(2, environments.Length);
        Assert.Equal(2, calls.Length);
        var pair = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, environments);
        var half = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [pair, pair]);
        var whole = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [half, half]);
        var closed = calls.Select(x => c.Types.Apply(whole, x)!).ToArray();
        for (var i = 0; i < calls.Length; i++)
        {
            Assert.Same(closed[i].Components[0], closed[i].Components[1]);
            var parts = closed[i].Components[0].Components[0].Components;
            Assert.Equal(environments.Select(x => c.Types.Apply(x, calls[i])!), parts);
            Assert.All(parts, x => Assert.Same(calls[i], x.ClosureContext));
        }

        Assert.NotSame(closed[0], closed[1]);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(c.BuildMir().Records(MirRecordClass.Collection));
    }

    // DECISIONS BU-19 (R5's condition for the closing table): an edit leaves no closing answer behind, so a closure whose signature an
    // edit changed is instantiated again with the new signature in the next pass. The edited tree is rebound, not built (its replaced
    // declaration keeps the donor's module).
    [Fact]
    public void AnEditedClosureSignatureIsInstantiatedAgain()
    {
        const string Before = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () -> T => x\n    let r = f()\n    return\n";
        const string After = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () -> (T, T) => (x, x)\n    let r = f()\n    return\n";
        const string Main = "public func main() -> ()\n    make(1)\n    return\n";
        var c = MirBuildTest.Compile(Before + Main);
        new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(c.BuildMir().Records(MirRecordClass.Collection));
        Assert.Equal("i32", Binding.DiagnosticTypeName(Result(c)));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        var replacement = KotoTree.Walk(ParseTestHelper.ParseSuccess(After + Main).RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal("(i32, i32)", Binding.DiagnosticTypeName(Result(c)));

        // The closure's result Type in the instance make<i32>.
        static BoundType Result(Compilation c)
        {
            var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
            var environment = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
            var call = nodes.OfType<InvocationKoto>().Single(static x => x.CallOf()?.Target.Name == "make").CallOf()!;
            return c.Binding.ClosureSignature(c.Types.Apply(environment, c.Types.SubstOf(call)!)!)!.Components[1];
        }
    }

    // SPEC 8.13: a part shared below a length-generic Tuple is substituted with the length argument; the call checks and the checked
    // set holds the instance without a record.
    [Fact]
    public void ASharedLengthGenericPartIsSubstitutedWithItsLength()
    {
        const string Source = "func f<length N>(a: [N of i32]) -> i32\n    let t = (a, a)\n    let u = (t, t)\n    return u.0.0[0]\npublic func main() -> ()\n" +
            "    let a: [3 of i32] = [1, 2, 3]\n    require f(a) == 1 else => $abort(\"x\")\n    return\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(c.BuildMir().Records(MirRecordClass.Collection));
        Assert.Equal(1, Instances(set, "f"));
    }

    // SPEC 8.3: a constraint on a shared Tuple whose part names a failed declaration is an Error of that constraint, as for a tree.
    [Fact]
    public void AFailedPartOfASharedTupleFailsItsConstraint()
    {
        const string Source = "struct Bad\n    public var x: Missing\nfunc need<T>(x: ref/T) -> ()\n    T is Copy\n    return\nfunc f(v: ref/((Bad, Bad), (Bad, Bad))) -> ()\n" +
            "    need(v)\n    return\npublic func main() -> ()\n    return\n";
        Assert.Equal(["UnresolvedBinding_Kd", "UnprovenConstraint_Kd"], DiagnosticCorpus.Check(Source).Diagnostics.Select(static x => x.Code));
    }

    // The MIR's walks: A5's cleanup observation (whether a cleanup observes Loans) over shared struct, enum and Tuple parts, generic ones
    // too, the Origin projections and Loan dependencies over shared Origin-carrying parts, the completion of shared Tuples; for the
    // instance chains also their collection and code generation.
    [Theory]
    [InlineData("structs", 28)]
    [InlineData("structUses", 24)]
    [InlineData("enums", 31)]
    [InlineData("lets", 28)]
    [InlineData("genericLets", 26)]
    [InlineData("originLets", 28)]
    [InlineData("chain", 26)]
    [InlineData("chainValues", 26)]
    public void WalksAreLinearInDistinctParts(string shape, int size)
    {
        var c = Checked(Source(shape, size));

        // originLets' last Tuple holds 2^28 references, 2^31 bytes, past the layout size bound: check's one record, at the local's
        // declaration (DECISIONS R5-M44, R5-M156).
        var bound = shape == "originLets" ? "GenerationResourceLimit_Kd let a27 = (a26, a26)" : string.Empty;
        Assert.Equal(bound, MinimalEmissionTest.Problems(c));
        Assert.Equal(bound.Length == 0, c.Analysis.IsVerified);
        if (shape.StartsWith("chain", StringComparison.Ordinal))
        {
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        }
    }

    // SPEC 15.6.6: a part shared at depth 0 and 70 levels down a Tuple keeps the bound's verdict in either order: at 70 a later write
    // meets A5's depth bound, a located OwnershipDepthLimit_Kd at `let d` (DECISIONS R4-M153), where the old analysis's bound retained the
    // Loan and the write conflicted with it (R6-check H5; H3b T48, class 3); without the write there is none. At 70 each Tuple more than
    // 64 levels high is also a layout depth record at check, one per erased Type (DECISIONS R5-M44, R5-M156).
    [Theory]
    [InlineData(70, false, false, "")]
    [InlineData(70, false, true, "OwnershipDepthLimit_Kd")]
    [InlineData(70, true, true, "OwnershipDepthLimit_Kd")]
    public void ADeepSharedPartKeepsTheBoundsVerdict(int depth, bool shallowFirst, bool written, string code)
        => Assert.Equal(Codes(code).Concat(Enumerable.Repeat("GenerationResourceLimit_Kd", Math.Max(0, depth - 64))), Problems(Checked(DeepShared(depth, shallowFirst, written))));

    // H3b T31d: at 60 the later write is valid (SPEC 15.6.6, 16.3.1: a Tuple of references destroys nothing that observes), but A5's depth
    // bound is 32 (the old analysis's was 64, Binding's storage height, BU-12), so the MIR refuses it with a located OwnershipDepthLimit_Kd
    // at `let d` (DECISIONS R4-M153). H5's (e) list (the owner, 2026-11-03, 「推奨どおり」, board O-1): U-B5 (A5's bound 32 -> 64, after
    // PLAN G349's units) restores the acceptance and moves the rows back to ADeepSharedPartKeepsTheBoundsVerdict.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeepSharedPartPastTheMirsBoundIsRefusedPendingUB5(bool shallowFirst)
    {
        var c = Checked(DeepShared(60, shallowFirst, written: true));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        MirTargetSide.Check(c, (DiagnosticCode.OwnershipDepthLimit_Kd, new SyntaxLocation("let d = " + DeepNest(60))));
    }

    // SPEC 16.3.1: the drop of a struct shared in a Tuple observes its Loans, so a later write conflicts; without a drop it does not.
    [Theory]
    [InlineData(false, true, "")]
    [InlineData(true, false, "")]
    [InlineData(true, true, "ComparisonLoanConflict_Kd")]
    public void ADropThroughASharedPartObservesItsLoans(bool drop, bool written, string code)
    {
        var source = "struct Observer {a}\n    public let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n" + (drop ? "    drop => ()\n" : string.Empty) +
            "var value = 42\nlet t = ((Observer.init(value@ref), Observer.init(value@ref)), (Observer.init(value@ref), Observer.init(value@ref)))\n" +
            (written ? "value = 99\n" : string.Empty) + "require value > 0 else => $abort(\"x\")\n";
        Assert.Equal(Codes(code), Problems(Checked(source)));
    }

    // SPEC 15.6.2: a Tuple sharing a borrow's parts keeps the Loan, so a write while it is live conflicts once (the MIR's Loan).
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "ComparisonLoanConflict_Kd")]
    public void ALoanThroughSharedOriginCarryingPartsIsKept(bool written, string code)
    {
        var source = new System.Text.StringBuilder("public func main() -> ()\n    var v = 1\n    let t = v@ref\n    let a0 = (t, t)\n");
        for (var i = 1; i < 8; i++)
        {
            source.Append($"    let a{i} = (a{i - 1}, a{i - 1})\n");
        }

        source.Append(written ? "    v = 2\n" : string.Empty).Append("    let z = a7\n    return\n");
        Assert.Equal(Codes(code), Problems(Checked(source.ToString())));
    }

    // SPEC 8.15: Copy proofs over shared Tuples of an associated projection hold (an interned constraint's projection walk).
    [Fact]
    public void CopyProofsOverSharedProjectionTuplesHold()
    {
        var source = new System.Text.StringBuilder("contract HasElem\n    associate Element\nstruct Src\n    Self is HasElem\n    associate Element is i32\n" +
            "func f<S>(e: S.Element) -> ()\n    S is HasElem\n    S.Element is Copy\n    let a0 = (e, e)\n");
        for (var i = 1; i < 10; i++)
        {
            source.Append($"    let a{i} = (a{i - 1}, a{i - 1})\n");
        }

        source.Append("    return\npublic func main() -> ()\n    f<Src>(1)\n    return\n");
        Assert.Empty(DiagnosticCorpus.Check(source.ToString()).Diagnostics);
    }

    // A ring through a handle beside a shared borrow in a Tuple: the old analysis's bound retained the Loan, so a later write conflicted.
    // Since the MIR decides acceptance (R6-check H5; H3b T62, 1a, the owner 2026-11-03), `t` is unused after the write and Node's
    // destruction observes nothing (SPEC 15.6.3, 15.6.6, 16.3.3): check accepts the program with no record, and a Library build generates
    // main.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARingThroughAHandleKeepsNoLoanPastItsLastUse(bool written)
    {
        var source = "struct Node\n    public var x: i32\n    public var next: Option<rc/Node>\n    public init(x: i32)\n        self.x = x\n        self.next = .None\n" +
            "public func main() -> ()\n    var value = 42\n    let n = Node.init(x: 1)\n    let t = ((n@move, value@ref), 3)\n" + (written ? "    value = 99\n" : string.Empty) + "    return\n";
        Assert.Contains("main", MinimalEmissionTest.GeneratedBodies(source, OutputKind.Library, accepted: true));
    }

    // A closed instance whose Field is a projection is checked in its Place's context.
    [Fact]
    public void AnInstanceWithAProjectionFieldChecks()
    {
        const string Source = "contract HasElem\n    associate Element\nstruct Src\n    Self is HasElem\n    associate Element is i32\nstruct W<S>\n    S is HasElem\n" +
            "    public var e: S.Element\n    public init(e: S.Element) => self.e = e@move\npublic func main() -> ()\n    let w = W<Src>.init(e: 1)\n    let p = (w@move, 2)\n    return\n";
        Assert.Empty(Problems(Checked(Source)));
    }

    // A warm Analyze of a program whose Types share parts allocates nothing: the MIR's analyses keep their storage.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmAnalyzeOfSharedTypesAllocatesNothing()
    {
        var c = Checked(Source("structUses", 12));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // A warm rebind of a program whose Types share parts allocates nothing: the answer tables keep their capacity.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmBindOfSharedTypesAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Source("chainValues", 16));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
    }

    // The source of a shape: structs storing the previous struct twice (PLAN G140), with constructions, a path read, a move and a drop;
    // enums whose two Cases carry the previous enum (storage linear in the size); a chain of generic functions, each calling the next with
    // the Tuple of two of its argument (PLAN G153 (2)), passing a value or nothing; a body whose locals pair the previous local, over i32,
    // a Copy parameter or a borrow.
    internal static string Source(string shape, int size)
    {
        var text = new System.Text.StringBuilder();
        switch (shape)
        {
            case "structs" or "structUses":
                text.Append("struct L0\n    public var x: i32\n    public init(x: i32)\n        self.x = x\n");
                for (var i = 1; i <= size; i++)
                {
                    text.Append($"struct L{i}\n    public var a: L{i - 1}\n    public var b: L{i - 1}\n    public init(a: L{i - 1}, b: L{i - 1})\n        self.a = a@move\n        self.b = b@move\n");
                }

                if (shape == "structs")
                {
                    return text.Append("public func main() -> ()\n    return\n").ToString();
                }

                text.Append("func mk0() -> L0 => L0.init(x: 1)\n");
                for (var i = 1; i <= size; i++)
                {
                    text.Append($"func mk{i}() -> L{i} => L{i}.init(a: mk{i - 1}(), b: mk{i - 1}())\n");
                }

                return text.Append($"func read(v: ref/L{size}) -> i32 => v{string.Concat(Enumerable.Repeat(".a", size))}.x\nfunc pass(v: L{size}) -> L{size} => v@move\n")
                    .Append($"func sink(v: L{size}) -> () => ()\npublic func main() -> ()\n    let v = pass(mk{size}())\n    let r = read(v)\n    sink(v@move)\n    return\n").ToString();
            case "enums":
                text.Append("enum E0\n    Z(i32)\n");
                for (var i = 1; i <= size; i++)
                {
                    text.Append($"enum E{i}\n    A(E{i - 1})\n    B(E{i - 1})\n");
                }

                var value = string.Concat(Enumerable.Range(1, size).Reverse().Select(static i => $"E{i}.A(")) + "E0.Z(1)" + new string(')', size);
                return text.Append($"func pass(v: E{size}) -> E{size} => v@move\nfunc sink(v: E{size}) -> () => ()\nfunc mk() -> E{size} => {value}\n")
                    .Append("public func main() -> ()\n    sink(pass(mk()))\n    return\n").ToString();
            case "chain":
                for (var i = 0; i < size; i++)
                {
                    text.Append($"func f{i}<T>(x: Option<raw/T>) -> ()\n    f{i + 1}<(T, T)>(.None)\n    return\n");
                }

                return text.Append($"func f{size}<T>(x: Option<raw/T>) -> ()\n    return\npublic func main() -> ()\n    f0<i32>(.None)\n    return\n").ToString();
            case "chainValues":
                for (var i = 0; i < size; i++)
                {
                    text.Append($"func f{i}<T>(x: T) -> ()\n    T is Copy\n    f{i + 1}<(T, T)>((x, x))\n    return\n");
                }

                return text.Append($"func f{size}<T>(x: T) -> ()\n    return\npublic func main() -> ()\n    f0<i32>(1)\n    return\n").ToString();
            default:
                text.Append(shape switch
                {
                    "genericLets" => "func f<T>(t: T) -> ()\n    T is Copy\n",
                    "originLets" => "func f(t: ref/i32) -> ()\n",
                    _ => "func f(t: i32) -> ()\n",
                }).Append("    let a0 = (t, t)\n");
                for (var i = 1; i < size; i++)
                {
                    text.Append($"    let a{i} = (a{i - 1}, a{i - 1})\n");
                }

                return text.Append(shape == "originLets" ? "    return\npublic func main() -> ()\n    let x = 1\n    f(x@ref)\n    return\n" : "    return\npublic func main() -> ()\n    f(1)\n    return\n").ToString();
        }
    }

    // A deep-heir chain (valid, SPEC 8.6.2: every level inherits C with Item = Option<i32>): L0 conforms, L(k) : L(k-1), D : L(depth-1),
    // and a use of D.(C).Item.
    private static string HeirChain(int depth)
    {
        var text = new System.Text.StringBuilder("contract C\n    associate Item\nopen struct L0<X>\n    Self is C\n    associate C.Item is X\n");
        for (var i = 1; i < depth; i++)
        {
            text.Append($"open struct L{i}<X> : L{i - 1}<X>\n");
        }

        return text.Append($"struct D : L{depth - 1}<Option<i32>>\nfunc use(x: D.(C).Item) -> i32 => 1\n").ToString();
    }

    private static string[] Codes(string code) => code.Length == 0 ? [] : [code];

    // A main sharing `value@ref` at depth 0 of a Tuple 10 levels high and of one `depth` levels high, in either order, then maybe writing it.
    private static string DeepShared(int depth, bool shallowFirst, bool written)
    {
        var deep = $"    let d = {DeepNest(depth)}\n";
        var shallow = $"    let s = {DeepNest(10)}\n";
        return "public func main() -> ()\n    var value = 42\n" + (shallowFirst ? shallow + deep : deep + shallow) + (written ? "    value = 99\n" : string.Empty) + "    return\n";
    }

    // A checked compilation: the MIR's analyses judge its walks (R6-check H5, DECISIONS R5-M185), the checked set's records publish at
    // check (DECISIONS R5-M156), and the chain shapes generate through MIR code generation, the old generator's instance analyses having
    // left with its selection at G4 (DECISIONS R5-M160).
    private static Compilation Checked(string source) => MinimalEmissionTest.Analyze(source);

    // The codes of the analysis's problems, the MIR's.
    private static string[] Problems(Compilation c) => [.. c.Problems.Select(static x => x.Code.ToString())];

    // The body instances of the functions whose name starts with a prefix.
    private static int Instances(CheckedSet set, string prefix)
    {
        var count = 0;
        foreach (var item in set.Items)
        {
            count += item.Key is { Kind: MonoKind.Body, Body.Def.Decl.Name: { } name } && name.StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0;
        }

        return count;
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        // SPEC 6.2, 8.6.2 (PLAN G631): every inherited path's binding is a node of the inline-layout judge, one per heir, so a cold Bind of a
        // deep inheritance chain allocates linearly in its depth: three depths, each double the one before, within factor 3, as
        // InferenceSlotScalingTest's rows define it.
        [Fact]
        public void AColdBindOfAnInheritanceChainIsLinear()
        {
            int[] depths = [64, 128, 256];
            var compilations = new Compilation[depths.Length];
            var bytes = AllocationMeasurement.MeasureSeries(depths.Length, i =>
            {
                var c = compilations[i] = CompilationTestHelper.Parse(HeirChain(depths[i]));
                return () => c.Bind();
            });
            Assert.All(compilations, static c => Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c)));
            Assert.True(bytes[0] < bytes[1] && MirBorrowsTest.Grows(bytes, 3), $"bytes {string.Join(", ", bytes)}");
        }
    }
}
