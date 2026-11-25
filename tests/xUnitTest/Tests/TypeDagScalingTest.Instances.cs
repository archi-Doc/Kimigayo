// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G243's O1 to O3 (DECISIONS BU-55): R4's V16 chain on instances, <c>D_i&lt;T&gt; { x: D_{i+1}&lt;(T, T)&gt; }</c>, has
/// 2n distinct instance Types whose last Type argument is a Tuple tree of 2^(n-1) leaves. Each row's chain is 40 long, so a walk that
/// re-visits shared parts meets 2^40 paths; Binding and every call's retention (TyCtxt.Retains, which A5 requests) stay linear in the
/// distinct parts. The MIR builder is not run: its own walks over these shapes are open (PLAN).</summary>
public partial class TypeDagScalingTest
{
    // The chain through Tuples, fixed arrays and enum payloads, and with raw leaves: the declarations' inline layouts (their summaries,
    // DECISIONS BU-65, and CompleteProjections); a member path to the last instance and a generic callee at it (CheckTypeConstraints, ProveTypeLengths, the
    // callee's atom walk); a comparison of the Tuple tree (ComparisonProof), a Copy requirement on it (DependentType), a called closure
    // capturing it (LacksImage). With Origin-carrying leaves (`ref`, PLAN G309) the calls' Contract rewrites (ContractType), the
    // candidates' and results' Origin tests (the Origin summary), the published result and a Closure with two captures, one of which names
    // the result's Origin.
    [Theory]
    [InlineData("tuple", 40)]
    [InlineData("array", 40)]
    [InlineData("enum", 40)]
    [InlineData("raw", 40)]
    [InlineData("ref", 40)]
    public void InstanceChainWalksAreLinearInDistinctParts(string form, int size)
    {
        var c = MirBuildTest.Compile(InstanceChain(form, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var calls = new List<string>();
        foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>())
        {
            if (node.CallOf() is { Kind: not CalleeKind.Value, Target.LibraryDeclaration: null } call)
            {
                var definition = node.Parent;
                while (definition is not FunctionKoto)
                {
                    definition = definition!.Parent;
                }

                var function = (FunctionKoto)definition;
                Assert.NotNull(c.Types.Retains(call.Target, c.Types.SubstOf(call), function.SymbolOf(), []));
                calls.Add($"{function.Name} {call.Target.Name}");
            }
        }

        Assert.Equal(form == "enum" ? "g f" : "g f, k h, copies cp", string.Join(", ", calls));
    }

    // DECISIONS BU-113: a generic callee's declared input is the chain over its own Type parameter, `fg<T>(d: uniq/D1<T>, …)`, so the
    // retention walks pair every instantiated part of D1<i32> with its declared part of D1<T>, a Tuple DAG of 2^39 leaves on each side;
    // each (part, declared part) pair is walked once, and the contract finishes.
    [Theory]
    [InlineData("tuple", 40)]
    [InlineData("raw", 40)]
    public void ADeclaredDagIsPairedOncePerPart(string form, int size)
    {
        var root = form == "raw" ? "raw/i32" : "i32";
        var c = MirBuildTest.Compile(InstanceChain(form, size) + $"func fg<T>(d: uniq/D1<T>, p: raw/i32) => ()\nfunc gg(d: uniq/D1<{root}>) => fg(d, null)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var gg = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "gg");
        var call = KotoTree.Walk(gg).OfType<InvocationKoto>().Single().CallOf()!;
        Assert.NotNull(c.Types.Retains(call.Target, c.Types.SubstOf(call), gg.SymbolOf(), []));
    }

    // PLAN G935 (blocker 1; DECISIONS Q20 B1): Writes' walk (WrittenThrough, its reach set by WriteReach) over a handed writer's parameter,
    // a diamond chain `N_i { a: N_(i+1), b: N_(i+1) }` of 2^30 paths within StorageBound, past an exclusive layer (reach Exclusive) and
    // past an owning handle (reach Own, FreshBelow); the kept Type T (abstract in both functions, so y's Loan may reach its storage and
    // Filled asks whether `set` writes T) lies nowhere in it, so the walk reads every part, each (part, reach) once at its shallowest depth
    // (retentionReaches). Without the memo the row does not finish (a probe with Enters' test disabled ran past 150 s).
    [Theory]
    [InlineData("uniq/N0", 30)]
    [InlineData("uniq/(rc/N0)", 30)]
    public void AHandedWritersParameterIsWalkedOncePerPartAndReach(string slot, int size)
    {
        var chain = string.Concat(Enumerable.Range(0, size).Select(static i => $"struct N{i}\n    public var a: N{i + 1}\n    public var b: N{i + 1}\n"));
        var c = MirBuildTest.Compile(chain + $"struct N{size}\n    public var v: i32\npublic func main() => ()\n" +
            $"func fw<T, E>(x: uniq/T, y: E, ku: ref/i32 during u, set: ({slot} during u, E) -> ()) -> ()\n    return\n" +
            $"func gw<T>(x: uniq/T, y: uniq/i32 during o, ku: ref/i32 during u, set: ({slot} during u, uniq/i32 during o) -> ()) => fw<T, uniq/i32 during o>(x, y, ku, set@move)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var gw = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "gw");
        var call = KotoTree.Walk(gw).OfType<InvocationKoto>().Single().CallOf()!;
        Assert.NotNull(c.Types.Retains(call.Target, c.Types.SubstOf(call), gw.SymbolOf(), []));
    }

    // D-933 (DECISIONS Q20 B1): the raw-source walk of a struct's stored parts (Binding.StoredRawSources) over a diamond chain `N_i { a:
    // N_(i+1), b: N_(i+1) }` of 2^30 paths within StorageBound, whose leaf holds a raw Field, reached past the input's exclusive layer:
    // each part once per position (retentionStored), so the contract finishes. Without the memo the walk reads every path.
    [Fact]
    public void AStoredDagIsWalkedOncePerPartForItsRawSources()
    {
        const int Size = 30;
        var chain = string.Concat(Enumerable.Range(0, Size).Select(static i => $"struct N{i}\n    public var a: N{i + 1}\n    public var b: N{i + 1}\n"));
        var c = MirBuildTest.Compile(chain + $"struct N{Size}\n    public var q: raw/u8\npublic func main() => ()\n" +
            "func keepN(s: raw/u8, v: uniq/N0) => ()\nfunc gn(v: uniq/N0) => keepN(null, v)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var gn = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "gn");
        var call = KotoTree.Walk(gn).OfType<InvocationKoto>().Single().CallOf()!;
        Assert.NotNull(c.Types.Retains(call.Target, c.Types.SubstOf(call), gn.SymbolOf(), []));
    }

    // SPEC 8.2: a projection chain over a Type parameter, `Dup<…Dup<T>.Item…>.Item`, normalizes to a Tuple DAG of T in a generic body: each
    // level's specification is rewritten in a nested query (ContractType's guard), each distinct part once (DECISIONS BU-64). `rd` reads
    // `h.x` through `ref/` as a Copy (`T is Copy`): a bare non-Copy read is SPEC 3.5's error, and a borrowed referent offers no Take (15.1.5).
    [Fact]
    public void AnAbstractProjectionDagIsNormalizedOncePerPart()
    {
        var type = "T";
        for (var i = 0; i < 40; i++)
        {
            type = $"Dup<{type}>.Item";
        }

        var c = MirBuildTest.Compile("contract HasItem\n    associate Item\nstruct Dup<T>\n    Self is HasItem\n    associate HasItem.Item is (T, T)\n" +
            $"struct H<T>\n    internal var x: {type}\nfunc rd<T>(h: ref/H<T>) -> ()\n    T is Copy\n    let v = h.x\n    return\nfunc pass<T>(h: H<T>) -> H<T> => h@move\n" +
            "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "v").NameKoto.TypeOf()!;
        for (var k = 0; k < 40; k++)
        {
            Assert.Same(read.Components[0], read.Components[1]);
            read = read.Components[0];
        }

        Assert.Equal(BoundTypeKind.Parameter, read.Kind);
    }

    // SPEC 8.4: a Contract's requirement over a projection chain of its Type parameter, conformed by `struct X<U>` with `Self is Get<U>`,
    // reaches the shared parts through the conformance path: ApplyContractEnvironment's StoredType query per distinct part, quadratic in
    // distinct parts (DECISIONS BU-64's open limit, PLAN G309), milliseconds at n = 40.
    [Fact]
    public void AConformanceOverAProjectionDagFinishes()
    {
        var (required, written) = ("T", "U");
        for (var i = 0; i < 40; i++)
        {
            (required, written) = ($"Dup<{required}>.Item", $"Dup<{written}>.Item");
        }

        var c = MirBuildTest.Compile("contract HasItem\n    associate Item\nstruct Dup<T>\n    Self is HasItem\n    associate HasItem.Item is (T, T)\n" +
            $"contract Get<T>\n    func get(self: ref/Self, x: ref/{required}) -> ()\nstruct X<U>\n    Self is Get<U>\n    var n: i32\n" +
            $"    public func get(self: ref/Self, x: ref/{written}) -> () => ()\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // PLAN G310 (DECISIONS BU-65): the chain closed by Dn storing D1<T> puts every declaration on one inline cycle, each reported once;
    // each declaration's walk visits its own written storage and the summaries it reads, and the component settles in a bounded number
    // of rounds, so 40 declarations whose instances hold 2^39-leaf Tuple trees stay linear.
    [Theory]
    [InlineData("tuple", 40)]
    [InlineData("array", 40)]
    public void AnInlineCycleThroughAChainIsLinearInDistinctParts(string form, int size)
    {
        var argument = form == "array" ? "([2 of T], [2 of T])" : "(T, T)";
        var text = new System.Text.StringBuilder();
        for (var i = 1; i < size; i++)
        {
            text.Append($"struct D{i}<T>\n    public var x: D{i + 1}<{argument}>\n");
        }

        text.Append($"struct D{size}<T>\n    public var x: T\n    public var back: D1<T>\npublic func main() => ()\n");
        var c = MirBuildTest.Compile(text.ToString());
        var diagnosed = c.Binding.Issues.Where(static x => x.Code == Kimi.DiagnosticCode.InvalidInlineLayout_Kd).Select(static x => ((DeclarationContainerKoto)x.Node).Name);
        Assert.Equal(Enumerable.Range(1, size).Select(static i => $"D{i}").Order(StringComparer.Ordinal), diagnosed.Order(StringComparer.Ordinal));
    }

    // DECISIONS BU-12 (R7-T1, C-406's C2): the storage verdict (Binding.PrepareTypeStorage, the one storage query) decides each distinct Type of a
    // storage once per pass, so an inline Box and a fixed array nested N deep cost linear in N: Prepared within the height bound (64),
    // Limit past it, and the pass's memo gives the same verdict again; a walk that stalls fails at the deadline, never hangs.
    [Theory]
    [InlineData(30, "Prepared")]
    [InlineData(60, "Prepared")]
    [InlineData(120, "Limit")]
    public async Task AStorageVerdictIsLinearInItsNesting(int depth, string expected)
    {
        var boxes = string.Concat(Enumerable.Repeat("Box<", depth)) + "i32" + new string('>', depth);
        var arrays = string.Concat(Enumerable.Repeat("[1 of ", depth)) + "i32" + new string(']', depth);
        var source = $"struct Box<T>\n    public var value: T\nfunc f(b: {boxes}, a: {arrays}) -> ()\n    return\npublic func main() => ()\n";
        var cancel = TestContext.Current.CancellationToken;
        var run = Task.Run(
            () =>
            {
                var c = MirBuildTest.Compile(source);
                var types = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters
                    .Select(static x => x.Type.TypeOf()!).ToArray();
                return types.Select(c.Binding.PrepareTypeStorage).Concat(types.Select(c.Binding.PrepareTypeStorage)).ToArray();
            },
            cancel);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
        Assert.Equal(Enumerable.Repeat(expected, 4), (await run).Select(static x => x.ToString()));
    }

    // D1 to Dn (structs, or enums whose Case A holds the part), each storing the next at its Type argument doubled (a Tuple, or a Tuple of
    // two fixed arrays), the last storing the argument; D1 at i32, at `raw/i32` for `raw`, or at `ref/i32 during a` for `ref`, whose
    // Closure with two captures also takes a second chain `during b`. A struct chain's functions read the path to Dn's argument.
    private static string InstanceChain(string form, int n)
    {
        var argument = form == "array" ? "([2 of T], [2 of T])" : "(T, T)";
        var text = new System.Text.StringBuilder();
        for (var i = 1; i <= n; i++)
        {
            var part = i < n ? $"D{i + 1}<{argument}>" : "T";
            text.Append(form == "enum" ? $"enum D{i}<T>\n    A({part})\n    B\n" : $"struct D{i}<T>\n    public var x: {part}\n");
        }

        var root = form switch { "raw" => "D1<raw/i32>", "ref" => "D1<ref/i32 during a>", _ => "D1<i32>" };
        text.Append($"func f(d: uniq/{root}, p: raw/i32) => ()\nfunc g(d: uniq/{root}) => f(d, null)\npublic func main() => ()\n");
        if (form == "enum")
        {
            return text.ToString();
        }

        var path = string.Concat(Enumerable.Repeat(".x", n));
        text.Append($"func h<T>(d: uniq/T, p: raw/i32) => ()\nfunc k(d: uniq/{root}) => h(d{path[..^2]}@uniq, null)\n")
            .Append($"func cp<T>(x: T) -> (T, T)\n    T is Copy\n    return (x, x)\nfunc copies(d: ref/{root}) -> ()\n    let p = cp(d{path})\n    return\n")
            .Append($"func captures(d: ref/{root}) -> ()\n    let v = d{path}\n    let f = func [v] () => v\n    let w = f()\n    return\n");
        return form switch
        {
            "tuple" => text.Append($"func equal(a: ref/{root}, b: ref/{root}) -> bool => a{path} == b{path}\n").ToString(),
            "ref" => text.Append($"func both(d: ref/{root}, e: ref/D1<ref/i32 during b>) -> ()\n    let v = d{path}\n    let u = e{path}\n")
                .Append("    let f = func [v, u] () => v\n    let w = f()\n    return\n").ToString(),
            _ => text.ToString(),
        };
    }
}
