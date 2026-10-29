// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        if (shape.StartsWith("chain", StringComparison.Ordinal))
        {
            Assert.Equal(size + 1, Instances(set, "f"));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.Empty(set.Records);
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
        Assert.Empty(new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records);
        Assert.Equal("i32", Binding.DiagnosticTypeName(Result(c)));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        var replacement = KotoTree.Walk(ParseTestHelper.ParseSuccess(After + Main).RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(set.Records);
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

    // The old analysis's walks (the owner's D5 exception): whether a cleanup observes Loans over shared struct, enum and Tuple parts,
    // generic ones too, Origin projections and Loan dependencies over shared Origin-carrying parts, the completion of shared Tuples; for
    // the instance chains also the old Emission's instance analyses.
    [Theory]
    [InlineData("structs", 28)]
    [InlineData("structUses", 24)]
    [InlineData("enums", 31)]
    [InlineData("lets", 28)]
    [InlineData("genericLets", 26)]
    [InlineData("originLets", 28)]
    [InlineData("chain", 26)]
    [InlineData("chainValues", 26)]
    public void OldAnalysisWalksAreLinearInDistinctParts(string shape, int size)
    {
        var c = OldPath(Source(shape, size));

        // originLets' last Tuple holds 2^28 references, 2^31 bytes, past the layout size bound: check's one record, at the local's
        // declaration (DECISIONS R5-M44), on every pipeline (DECISIONS R5-M156).
        var bound = shape == "originLets" ? "GenerationResourceLimit_Kd let a27 = (a26, a26)" : string.Empty;
        Assert.Equal(bound, MinimalEmissionTest.Problems(c));
        Assert.Equal(bound.Length == 0, c.Analysis.IsVerified);
        if (shape.StartsWith("chain", StringComparison.Ordinal))
        {
            Assert.True(c.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        }
    }

    // SPEC 15.6.6: a part shared at depth 0 and 60 or 70 levels down a Tuple keeps the bound's verdict in either order: at 70 a later
    // write conflicts with the Loan the bound retains, at 60 it does not. At 70 each Tuple more than 64 levels high is also a layout depth
    // record at check, one per erased Type (DECISIONS R5-M44), on every pipeline (DECISIONS R5-M156).
    [Theory]
    [InlineData(60, false, true, "")]
    [InlineData(60, true, true, "")]
    [InlineData(70, false, false, "")]
    [InlineData(70, false, true, "ComparisonLoanConflict_Kd")]
    [InlineData(70, true, true, "ComparisonLoanConflict_Kd")]
    public void ADeepSharedPartKeepsTheBoundsVerdict(int depth, bool shallowFirst, bool written, string code)
    {
        var deep = $"    let d = {Nest(depth)}\n";
        var shallow = $"    let s = {Nest(10)}\n";
        var source = "public func main() -> ()\n    var value = 42\n" + (shallowFirst ? shallow + deep : deep + shallow) + (written ? "    value = 99\n" : string.Empty) + "    return\n";
        Assert.Equal(Codes(code).Concat(Enumerable.Repeat("GenerationResourceLimit_Kd", Math.Max(0, depth - 64))), Problems(OldPath(source)));

        static string Nest(int levels) => new string('(', levels) + "value@ref" + string.Concat(Enumerable.Range(0, levels).Select(static i => $", {i})"));
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
        Assert.Equal(Codes(code), Problems(OldPath(source)));
    }

    // SPEC 15.6.2: a Tuple sharing a borrow's parts keeps the Loan, so a write while it is live conflicts once (AddType's set per Place).
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
        Assert.Equal(Codes(code), Problems(OldPath(source.ToString())));
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

    // A ring through a handle reaches the bound, which retains the Loan; a closed instance whose Field is a projection is checked in its
    // Place's context.
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "ComparisonLoanConflict_Kd")]
    public void ARingThroughAHandleRetainsTheLoan(bool written, string code)
    {
        var source = "struct Node\n    public var x: i32\n    public var next: Option<rc/Node>\n    public init(x: i32)\n        self.x = x\n        self.next = .None\n" +
            "public func main() -> ()\n    var value = 42\n    let n = Node.init(x: 1)\n    let t = ((n@move, value@ref), 3)\n" + (written ? "    value = 99\n" : string.Empty) + "    return\n";
        Assert.Equal(Codes(code), Problems(OldPath(source)));
    }

    [Fact]
    public void AnInstanceWithAProjectionFieldChecks()
    {
        const string Source = "contract HasElem\n    associate Element\nstruct Src\n    Self is HasElem\n    associate Element is i32\nstruct W<S>\n    S is HasElem\n" +
            "    public var e: S.Element\n    public init(e: S.Element) => self.e = e@move\npublic func main() -> ()\n    let w = W<Src>.init(e: 1)\n    let p = (w@move, 2)\n    return\n";
        Assert.Empty(Problems(OldPath(Source)));
    }

    // A warm Analyze of a program whose Types share parts allocates nothing: the old analysis's answer tables keep their capacity.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmAnalyzeOfSharedTypesAllocatesNothing()
    {
        var c = OldPath(Source("structUses", 12));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    // A warm rebind of a program whose Types share parts allocates nothing: the answer tables keep their capacity.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmBindOfSharedTypesAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Source("chainValues", 16));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
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

    private static string[] Codes(string code) => code.Length == 0 ? [] : [code];

    // A checked compilation pinned to the old generator: the old analysis's rows judge its walks, and the chain shapes generate through the
    // old Emission's instance analyses; the checked set's records publish at check under either generator (DECISIONS R5-M156).
    private static Compilation OldPath(string source) => MinimalEmissionTest.Analyze(source, codegen: CompilerCodegen.Legacy);

    // The codes of the analysis's problems: the old path's, then the MIR's records.
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
}
