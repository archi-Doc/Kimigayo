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

    // SPEC 8.3.2, 8.6.1 (basewalk): each level of a 40-deep instance of a declaration with `A is C` and `Self is C` proves `A is C`
    // through B's conformance, whose evidence rests on the instance's formation (InstanceFormation), which reads the level the running
    // walk has already formed, so each level is formed once per query. Formed again per level, the walk meets 2^40 steps (45c9fab40 took
    // over 400 s at depth 24), so the check has a deadline and fails, never hangs.
    [Fact]
    public async Task ANestedConstrainedInstanceIsFormedOncePerQuery()
    {
        var type = string.Concat(Enumerable.Repeat("B<", 40)) + "L" + new string('>', 40);
        var source = "contract C\n    func m(self: ref/Self) -> i32\nstruct L\n    Self is C\n    public func m(self: ref/Self) -> i32 => 0\n" +
            "struct B<A>\n    A is C\n    Self is C\n    public var a: A\n    public func m(self: ref/Self) -> i32 => 0\n" +
            $"func f(x: ref/{type}) -> i32\n    return 0\nlet present = 1";
        var cancel = TestContext.Current.CancellationToken;
        var run = Task.Run(() => MinimalEmissionTest.Analyze(source), cancel);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
        var c = await run;
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // R7-C2e2 e2-5a: a component summary lists only the constrained instances its own storage writes (never a component's, mapped), so
    // a family whose storage recurses through ever larger instance arguments, n levels deep and back to itself, is summarized once per
    // declaration; a list that grew with the mapping stalled here (prototype efda54a52), so the check has a deadline and fails, never hangs.
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    public async Task AnInstanceFamilyIsSummarizedOncePerDeclaration(int depth)
    {
        var source = "contract C\nstruct Box<T>\n    T is C\n    Self is C\n" +
            string.Concat(Enumerable.Range(0, depth).Select(i => $"struct D{i}<T>\n    T is C\n    let next: Array<D{(i + 1) % depth}<Box<T>>>\n")) +
            "struct S\n    Self is C\nstruct H\n    let d: D0<S>\nfunc inspect(a: H) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n";
        var cancel = TestContext.Current.CancellationToken;
        var run = Task.Run(() => MinimalEmissionTest.Analyze(source), cancel);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
        var c = await run;
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 15.6.4, DECISIONS R4-M181 (G408B-15, G408B-16): a chain of 40 stored structs passes HandedBound with (dch40f) or without
    // (dch40) a callable at its end, and both programs are valid and accepted, as on dev. The erasure's Retains of the Closure holding C1
    // returns `()`, and dch40f's `() -> ()` has no `during` clause, so by SPEC no storage of it holds an Origin or a Loan: no holder the
    // rows name can carry one (HolderCarries), no retention verdict reads the part past the bound, and R4-M181's limit, located where a
    // check rests on the guard, does not apply. Guard rows (every record, Binding's and A5's).
    [Theory]
    [InlineData("    let v: i32\n")]
    [InlineData("    let f: () -> ()\n")]
    public void AStoredChainPastTheHandedBoundWhereNoHolderCarriesIsAccepted(string last)
    {
        var source = string.Concat(Enumerable.Range(1, 39).Select(static i => $"struct C{i}\n    let n: C{i + 1}\n")) + "struct C40\n" + last +
            "func inspect(a: C1) -> ()\n    let g: () -> () = func [a@move] () => ()\n    return\n";
        Assert.Equal("clean", DiagnosticCorpus.RecordProfile(source));
    }

    // SPEC 15.3.5 Fixed points, 8.3.3 (R7-C2e2 e2-5a, Revision 2's chain row): a chain of N declarations, each storing a constrained
    // instance of the next (S<D(i+1)>, S needing its argument Owned), open or closed into a ring of Owned works, is formed once per
    // declaration: the capability evaluations per added declaration stay flat from N = 16 to 256 and the coinductive steps do not grow
    // (the engine's bound is O(N (N + M)) per request), and the program is valid either way (a ring of Owned works alone is Owned's
    // greatest fixed point).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChainOfStoredInstancesIsFormedOncePerDeclaration(bool ring)
    {
        var cancel = TestContext.Current.CancellationToken;
        var counts = new List<(int N, int Evaluations, int Steps)>();
        foreach (var n in new[] { 16, 64, 256 })
        {
            var source = "struct S<U>\n    U is Owned\n    public var u: U\n" +
                string.Concat(Enumerable.Range(0, n).Select(i => i + 1 < n || ring ? $"struct D{i}\n    let next: Array<S<D{(i + 1) % n}>>\n" : $"struct D{i}\n    let x: i32\n")) +
                "func needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(d: ref/D0) -> ()\n    needOwned(d)\n";
            var run = Task.Run(
                () =>
                {
                    var c = CompilationTestHelper.ParseSuccess(source);
                    Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
                    return (n, c.Binding.CapabilityEvaluationWork, c.Binding.CoinductiveStepWork);
                },
                cancel);
            Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
            counts.Add(await run);
        }

        var text = string.Join(", ", counts.Select(static x => $"N {x.N}: {x.Evaluations} evaluations, {x.Steps} coinductive steps"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        // Per added declaration (the library's fixed part cancels): 15 evaluations from N = 16 to 64 and from 64 to 256 on 6b91a1305.
        var low = (counts[1].Evaluations - counts[0].Evaluations) / 48.0;
        var high = (counts[2].Evaluations - counts[1].Evaluations) / 192.0;
        Assert.True(high <= low * 1.5, text);
        Assert.True(counts[0].Steps == counts[2].Steps, text);
    }

    // SPEC 11.6 (DECISIONS BU-267; G1220-1 C11): the header walk visits each Type once per request, so its visits per
    // added declaration stay flat: an inferred Property requesting Owned and Copy of a chain of N stored declarations (chain), N
    // Properties each requesting one 32-deep chain (fan), and N inferred Properties each typed through the next's header (nested, SPEC
    // 11.6's demand of another header, f2's twin). Each program is valid: no request's Type contains its Property.
    [Theory]
    [InlineData("chain")]
    [InlineData("fan")]
    [InlineData("nested")]
    public async Task AnInferredHeaderVisitsEachContainedPartOncePerRequest(string shape)
    {
        const string Tags = "group Tags\n    public func tagOf<T>() -> i32\n        T is Owned\n        return 0\n    public func dupOf<T>() -> i32\n        T is Copy\n        return 0\n";
        var cancel = TestContext.Current.CancellationToken;
        var sizes = shape == "nested" ? new[] { 16, 32, 64 } : new[] { 16, 64, 256 };
        var counts = new List<(int N, int Visits)>();
        foreach (var n in sizes)
        {
            var source = Tags + shape switch
            {
                "chain" => Chain(n) + "struct Entry\n    var tag = Tags.tagOf<D0>()\n    var dup = Tags.dupOf<D0>()\n",
                "fan" => Chain(32) + string.Concat(Enumerable.Range(0, n).Select(static j => $"struct E{j}\n    var tag = Tags.tagOf<D0>()\n")),
                _ => string.Concat(Enumerable.Range(0, n).Select(i => i + 1 < n ? $"struct A{i}\n    var a = Tags.tagOf<A{i + 1}>()\n" : $"struct A{i}\n    var a = 0\n")),
            };
            var run = Task.Run(
                () =>
                {
                    var c = CompilationTestHelper.ParseSuccess(source);
                    Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
                    return (n, c.Binding.HeaderWalkWork);
                },
                cancel);
            Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
            counts.Add(await run);
        }

        var text = string.Join(", ", counts.Select(static x => $"N {x.N}: {x.Visits} visits"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        var low = (counts[1].Visits - counts[0].Visits) / (double)(counts[1].N - counts[0].N);
        var high = (counts[2].Visits - counts[1].Visits) / (double)(counts[2].N - counts[1].N);
        Assert.True(low > 0 && high <= low * 1.5, text);

        static string Chain(int n) => string.Concat(Enumerable.Range(0, n).Select(static i => $"struct D{i}\n    Self is Copy\n    var next: D{i + 1}\n")) +
            $"struct D{n}\n    Self is Copy\n    var x: i32\n";
    }

    // SPEC 8.15.2, 8.3.4 (the owner's O-417 (a); the Front's C-397 C1): the capability engine judges a pair layer once per case its own
    // binder admits, each case's complete Type a part (SymbolicCapability), so a Copy requirement's capability evaluations grow
    // linearly with the layers that share one binder in a flat Tuple (N = 32, 64, 128) and with distinct binders, nested two to a layer,
    // up to the case bound (d = 2, 4, 6 binders in scope: 4, 16 and 64 cases), never with the product of the cases. Each program is valid
    // (every case Copies; 9485340f9 left each Unknown), and each check has a deadline, so it fails, never hangs.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APairLayerIsJudgedOncePerCaseOfItsOwnBinder(bool nested)
    {
        const string Need = "func needCopy<V>(x: V) -> i32\n    V is Copy\n    return 0\n";
        var cancel = TestContext.Current.CancellationToken;
        var sizes = nested ? new[] { 2, 4, 6 } : new[] { 32, 64, 128 };
        var counts = new List<(int Size, int Evaluations)>();
        foreach (var size in sizes)
        {
            var source = Need + (nested ? NestedBinders(size) : SharedBinder(size));
            var run = Task.Run(
                () =>
                {
                    var c = CompilationTestHelper.ParseSuccess(source);
                    Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
                    return (size, c.Binding.CapabilityEvaluationWork);
                },
                cancel);
            Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60), cancel)));
            counts.Add(await run);
        }

        var text = string.Join(", ", counts.Select(static x => $"{x.Size}: {x.Evaluations} evaluations"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        // Per added layer or binder (the library's fixed part cancels): a product over the cases would quadruple from d = 4 to 6.
        var low = (counts[1].Evaluations - counts[0].Evaluations) / (double)(sizes[1] - sizes[0]);
        var high = (counts[2].Evaluations - counts[1].Evaluations) / (double)(sizes[2] - sizes[1]);
        Assert.True(high <= low * 1.5, text);

        static string SharedBinder(int n)
            => "func k<s/T>(marker: s/T) -> i32\n    s is owner or ref\n" + string.Concat(Enumerable.Range(0, n).Select(static i => $"    var a{i}: i32 = {i}\n")) +
                $"    let t = ({string.Join(", ", Enumerable.Range(0, n).Select(static i => $"a{i}@s"))})\n    return needCopy(t)\n";

        // d / 2 parameters x_j: s_2j/(s_2j+1/U_j), each a layer of one binder over the original pair of the next, in one Tuple.
        static string NestedBinders(int d)
        {
            var pairs = Enumerable.Range(0, d / 2).ToArray();
            return $"func k<{string.Join(", ", pairs.Select(static j => $"s{2 * j}/T{j}, s{(2 * j) + 1}/U{j}"))}>(" +
                string.Join(", ", pairs.Select(static j => $"m{2 * j}: s{2 * j}/T{j}, m{(2 * j) + 1}: s{(2 * j) + 1}/U{j}, x{j}: s{2 * j}/(s{(2 * j) + 1}/U{j})")) + ") -> i32\n" +
                string.Concat(Enumerable.Range(0, d).Select(static i => $"    s{i} is owner or ref\n")) + string.Concat(pairs.Select(static j => $"    U{j} is Copy\n")) +
                $"    let t = ({string.Join(", ", pairs.Select(static j => $"x{j}"))})\n    return needCopy(t)\n";
        }
    }

    // SPEC 8.3.3 Recursion and Finality (R7-M33; basewalk): with Z's clause, `S<D> is C` rests on `S<D>`'s formation, `D is Owned`,
    // whose storage writes `Z<S<D>>`, formed only when `S<D> is C`: the conformance rests on itself, so the coinductive step's assumption
    // of `D is Owned` is not read through the newer `(S<D>, C)` obligation, also where the walk forming `Z<S<D>>` decided `S<D>` under
    // that assumption first (a nested formation reads only a part decided on settled verdicts). Without Z's clause the ring of Owned works
    // holds (SPEC 15.3.5), alone and read by B's clause.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void AnAssumedOwnedWorkIsNotReadThroughANewerObligation(bool clause, bool read)
    {
        var source = "contract C\n    func m(self: ref/Self) -> i32\nstruct Z<A>\n" + (clause ? "    A is C\n" : string.Empty) + "    public var a: A\n" +
            "struct S<U>\n    U is Owned\n    Self is C\n    public var u: U\n    public func m(self: ref/Self) -> i32 => 0\nstruct D\n    let next: Array<Z<S<D>>>\n" +
            (read ? "struct B<A>\n    A is C\n    Self is C\n    public var a: A\n    public func m(self: ref/Self) -> i32 => 0\nfunc f(x: ref/B<S<D>>) -> i32\n    return 0\n" : string.Empty);
        var c = CompilationTestHelper.ParseSuccess(source);
        var complete = c.Bind().IsComplete;
        string[] issues = [.. c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)];
        // The records 45c9fab40 gives (the field, each Z<S<D>> and S<D> occurrence the storage formation proves, and B<S<D>> where read),
        // one per written occurrence (R7-V1c).
        string[] own = ["UnprovenConstraint_Kd S<D>", "UnprovenConstraint_Kd Z<S<D>>", "UnprovenConstraint_Kd let next: Array<Z<S<D>>>"];
        string[] expected = !clause ? [] : read ? ["UnprovenConstraint_Kd B<S<D>>", "UnprovenConstraint_Kd S<D>", .. own] : own;
        Assert.Equal(!clause, complete);
        Assert.Equal(expected, issues);
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

    // SPEC 6.2.2, 9.6 (DECISIONS BU-220, R7-N5 B7c): member paths are interned by their content, so the lookups of N instantiations of a
    // generic derived struct (one clause reached with N Types) and of M sealed leaves of one middle layer (Mid's clause reached under M
    // parents) intern one path per distinct key, walk linear layers, and a warm Bind allocates none (a scanned row per clause was quadratic
    // in both: C-518, 131,328 steps at N = 512 and 1,055,744 at M = 1,024).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberPathsAreLinearInTheirUses(bool fanIn)
    {
        var sizes = new[] { 128, 256, 512 };
        var work = sizes.Select(Work).ToArray();
        var text = string.Join(' ', work);
        Assert.Equal(sizes.Select(x => fanIn ? (2 * x) + 1 : x), work.Select(static x => x.Paths));
        Assert.True(work[2].Walks - work[1].Walks <= (2 * (work[1].Walks - work[0].Walks)) + 400, text);
        Assert.All(work, static x => Assert.Equal(0, x.Allocated));

        (int Paths, int Walks, long Allocated) Work(int n)
        {
            var source = new System.Text.StringBuilder(fanIn
                ? "open struct Base\n    public var item: i32\n    public func get(self: ref/Self) -> i32 => self.item\nopen struct Mid: Base\n    public var other: i32\n"
                : "open struct Base<T>\n    public var item: i32\n    public func get(self: ref/Self) -> i32 => self.item\nstruct Leaf<T>: Base<T>\n    public var other: i32\n");
            for (var i = 0; i < n; i++)
            {
                source.Append(fanIn
                    ? $"struct D{i}: Mid\n    public func read(self: ref/Self) -> i32 => self.item + self.get()\n"
                    : $"struct Tag{i}\n    public var v: i32\nfunc use{i}(x: ref/Leaf<Tag{i}>) -> i32 => x.get()\n");
            }

            var c = CompilationTestHelper.ParseSuccess(source.ToString());
            Assert.True(c.Bind().IsComplete);
            var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
            Assert.True(c.Binding.Result.IsComplete);
            return (c.Binding.MemberPathCount, c.Binding.MemberWalkWork, allocated);
        }
    }

    // SPEC 9.6 (R7-N1 c-b, Q20 B1): a member access's left reading evaluates the left's Type walk once per access (a failed walk beside
    // the value reading is published, so inner accesses read it) and its value walk only where the Type reading may succeed, so binding
    // a qualified chain is linear in its length: N nested groups to a function (`G1...GN.f()`), and N groups, a static value and N Field
    // steps (`G1...GN.v.a...a.n`; a per-level re-walk of either was quadratic in N on a prototype).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMemberAccessLeftIsReadOncePerAccess(bool mixed)
    {
        var work = new[] { 30, 60, 120 }.Select(Work).ToArray();
        var text = string.Join(' ', work);
        Assert.True(work[2].Name - work[1].Name <= (2 * (work[1].Name - work[0].Name)) + 400, text);
        Assert.True(work[2].Member - work[1].Member <= (2 * (work[1].Member - work[0].Member)) + 400, text);

        (int Name, int Member) Work(int n)
        {
            var source = new System.Text.StringBuilder();
            if (mixed)
            {
                for (var i = 1; i <= n; i++)
                {
                    source.Append($"struct S{i}\n").Append(i == n ? "    public let n: i32\n    public init() => self.n = 1\n" : $"    public let a: S{i + 1}\n    public init() => self.a = S{i + 1}.init()\n");
                }
            }

            for (var i = 1; i <= n; i++)
            {
                source.Append(' ', 4 * (i - 1)).Append(i == 1 ? "group G1" : $"public group G{i}").Append('\n');
            }

            source.Append(' ', 4 * n).Append(mixed ? "public let v: S1 = S1.init()\n" : "public func f() -> i32 => 1\n");
            var path = string.Join('.', Enumerable.Range(1, n).Select(static i => $"G{i}")) + (mixed ? ".v" + string.Concat(Enumerable.Repeat(".a", n - 1)) + ".n" : ".f()");
            source.Append("func g() -> i32\n");
            for (var k = 0; k < 10; k++)
            {
                source.Append($"    let x{k} = {path}\n");
            }

            source.Append("    return 0\n");
            var c = CompilationTestHelper.Parse(source.ToString());
            c.Bind();
            return (c.Binding.NameWalkWork, c.Binding.MemberWalkWork);
        }
    }

    // SPEC 8.3.2, 8.5.2 (R7-N1 c-b, F's N1c ruling 28: K3; Q20 B1): a member access's left through a projection chain
    // `T.(C1).X.(C2).X...(CN).X`, each level's Contract supplying the next level's requirement and the last one's clause the normal form M,
    // is evaluated under its use scope's one kept layer (BindingScope.EvaluatedPremises), so the identity classes binding 10 uses of M's
    // Case builds are linear in N (a layer per evaluation level was quadratic: 4,260/16,100/35,540 at N = 10/20/30 for 20 uses).
    [Fact]
    public void AProjectionChainInALeftIsEvaluatedUnderOneLayer()
    {
        // Fails fast (ruling 29, C1): the 30 -> 60 step is checked before N = 120 is built.
        var (w30, w60) = (Work(30), Work(60));
        Assert.True(w60 <= (2 * w30) + 400, $"{w30} {w60}");
        var w120 = Work(120);
        Assert.True(w120 - w60 <= (2 * (w60 - w30)) + 400, $"{w30} {w60} {w120}");

        static int Work(int n)
        {
            var source = new System.Text.StringBuilder("enum M\n    A\n    public func f(self) -> i32 => 1\n");
            for (var i = 1; i <= n; i++)
            {
                source.Append($"contract C{i}\n").Append(i == n ? $"    associate X\n    Self.(C{i}).X is M\n" : $"    associate X is C{i + 1}\n");
            }

            var path = "T" + string.Concat(Enumerable.Range(1, n).Select(static i => $".(C{i}).X"));
            source.Append("func g<T>() -> i32\n    T is C1\n");
            for (var k = 0; k < 10; k++)
            {
                source.Append($"    let x{k} = {path}.A.f()\n");
            }

            source.Append("    return 0\n");
            var c = CompilationTestHelper.Parse(source.ToString());
            Assert.True(c.Bind().IsComplete);
            return c.Binding.IdentityClassWork;
        }
    }

    // SPEC 8.3.2, 8.5.3 (R7-N1 c-b, F's N1c rulings 43, 44; e2-7, G1406-55 S2): a use through the root of a Contract chain D0 <- D1 <-
    // ... <- DN (C bounds Item by DN, its parent D(N-1) declares `Self.(D0).X is M`) reads that identity in its view's closure, so each
    // of 10 uses is accepted (G1406-42 (1): the named Q13 class 1 acceptance move from 10 Unsupported_Kd). Its closure index work is
    // quadratic in N, each D_k's view filing its k ancestors' clauses in its request entry (n^2/2 + 8.5n + 26; dev, two entries a D_k:
    // n^2 + 6n + 26), PLAN G1496: the row asserts the measured values as an upper bound, removed by G1496's fix, which restores a linear
    // bound.
    [Fact]
    public void AContractChainUseIsAcceptedWithinItsMeasuredClosureIndexWork()
    {
        var (w30, w60, w120) = (Work(30), Work(60), Work(120));
        Assert.True(w30 <= 731 && w60 <= 2336 && w120 <= 8246, $"{w30} {w60} {w120}");

        static int Work(int n)
        {
            var binding = ChainUses(n, "    associate X\n", "    Self.(D0).X is M\n", ".A");
            Assert.Empty(binding.Issues);
            return binding.ClosureIndexWork;
        }
    }

    // SPEC 8.3.2, 8.5.3 (F's N1c rulings 42-44; G1406-55 S2): UnreadIdentity reads the subject's declaring Contract's clauses and the
    // bound's supply (ContractSupply: the bound and each Contract it inherits), so a member missing over a projection through the root
    // of the chain, whose identity `Self.(D0).X is Self.(D0).Y` (D(N-1)) leaves it a projection, reads work linear in N; each of 10
    // uses records the located Unsupported_Kd (TypePathFailure).
    [Fact]
    public void AnUnreadIdentityIsDecidedInWorkLinearInTheContractChain()
    {
        var (w30, w60) = (Work(30), Work(60));
        Assert.True(w30 < w60 && w60 <= (2 * w30) + 200, $"{w30} {w60}"); // The supply walk reads the bound's ancestors (ruling 44's N2).
        var w120 = Work(120);
        Assert.True(w120 - w60 <= (2 * (w60 - w30)) + 200, $"{w30} {w60} {w120}");

        static int Work(int n)
        {
            var binding = ChainUses(n, "    associate X\n    associate Y\n", "    Self.(D0).X is Self.(D0).Y\n", ".Zz");
            Assert.Equal(10, binding.Issues.Count(static x => x.Code == Kimi.DiagnosticCode.Unsupported_Kd));
            return binding.UnreadIdentityWork;
        }
    }

    // SPEC 8.6.2 (PLAN G602): a reversed chain of clauses through inherited conformances (L_i: S_i.(HasBase).Parent, S_i's Parent L_(i-1),
    // declared from the far end) binds with no stack failure, and the InheritedConformances query's clause readings beyond those of the
    // same chain written nominally are linear in its length.
    [Fact]
    public void InheritedConformanceDemandsAreLinearInTheChain()
    {
        var work = new[] { 64, 128, 256 }.Select(static levels => Work(levels, true) - Work(levels, false)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 64, string.Join(' ', work));

        static int Work(int levels, bool projected)
        {
            var chain = string.Concat(Enumerable.Range(1, levels).Reverse().Select(i => (projected ? $"open struct L{i}: S{i}.(HasBase).Parent\n" : $"open struct L{i}: L{i - 1}\n") + $"    public init(): base() => ()\nstruct S{i}\n    Self is HasBase\n    associate HasBase.Parent is L{i - 1}\n"));
            var c = CompilationTestHelper.ParseSuccess($"contract HasBase\n    associate Parent\nopen struct Root\n    public func r(self) -> i32 => 1\n    public init() => ()\nstruct D: L{levels}.(HasBase).Parent\n    public init(): base() => ()\nfunc f(d: ref/D) -> i32 => d.r()\n{chain}open struct L0\n    Self is HasBase\n    associate HasBase.Parent is Root\n    public init() => ()\n");
            Assert.True(c.Bind().IsComplete);
            return c.Binding.InheritedConformanceWork;
        }
    }

    // SPEC 8.8.2 (PLAN G665): an inherited path's scope and conditions read its declared path's premises at the declared Self composed at
    // registration, so on a deep-heir chain of a conditional conformance with a use at every level the premise steps are linear in its depth.
    [Fact]
    public void InheritedPremiseStepsAreLinearInTheChain()
    {
        var work = new[] { 64, 128, 256 }.Select(static depth => Work(depth)).ToArray();
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 64, string.Join(' ', work));

        static int Work(int depth)
        {
            var c = CompilationTestHelper.ParseSuccess(ConditionalHeirChain(depth));
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.InheritedPremiseWork;
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
            var uses = string.Concat(Enumerable.Range(0, reads).Select(static i => $"func g{i}(a: ref/A0.(HasBase).Parent) => ()\n"));
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
        var closed = calls.Select(x => c.Types.Apply(whole, x).Type!).ToArray();
        for (var i = 0; i < calls.Length; i++)
        {
            Assert.Same(closed[i].Components[0], closed[i].Components[1]);
            var parts = closed[i].Components[0].Components[0].Components;
            Assert.Equal(environments.Select(x => c.Types.Apply(x, calls[i]).Type!), parts);
            Assert.All(parts, x => Assert.Same(calls[i], x.InstanceContext));
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
            return c.Binding.ClosureSignature(c.Types.Apply(environment, c.Types.SubstOf(call)!).Type!)!.Components[1];
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

    // SPEC 8.3: a constraint on a shared Tuple whose part names a failed declaration is an Error of that constraint, as for a tree. Bad
    // opts in to Copy, so the proof walks the shared Tuple and reads the failed Field: without the opt-in, Bad is Refuted by its
    // declaration before any part is read (SPEC 3.5.2; G1220-1 O2, G1220-2). The opt-in's own record at 2:5 is a dependent one (PLAN
    // G1181's remainder).
    [Fact]
    public void AFailedPartOfASharedTupleFailsItsConstraint()
    {
        const string Source = "struct Bad\n    Self is Copy\n    public var x: Missing\nfunc need<T>(x: ref/T) -> ()\n    T is Copy\n    return\nfunc f(v: ref/((Bad, Bad), (Bad, Bad))) -> ()\n" +
            "    need(v)\n    return\npublic func main() -> ()\n    return\n";
        Assert.Equal(
            ["UnprovenConstraint_Kd 2:5", "UnresolvedBinding_Kd 3:19", "UnprovenConstraint_Kd 8:5"],
            DiagnosticCorpus.LocatedRecords(Source).Select(static x => string.Join(' ', x.Split(' ', 3)[..2])));
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

    // PLAN G492-a (ii) (design review M1): a `let` DAG used after its declaration, valid (SPEC 15.6.5: every leaf is `p`'s): a generic
    // call fitting the whole opened DAG, a `let` copy and a projection to a leaf (use), or a Closure capturing it and returning a leaf
    // (capture). At 26 the DAG has 2^27 paths, so a walk per path hangs. Revision 2: a `let` chain whose every level meets the two locals
    // of the level before (meet; valid, SPEC 15.3.6: each value is `(p and q)`), read in its own body as its values: at 26 a read per path
    // through the values would take 2^26 steps.
    [Theory]
    [InlineData("use", 12)]
    [InlineData("use", 20)]
    [InlineData("use", 26)]
    [InlineData("capture", 12)]
    [InlineData("capture", 20)]
    [InlineData("capture", 26)]
    [InlineData("meet", 12)]
    [InlineData("meet", 20)]
    [InlineData("meet", 26)]
    public void AUsedLetDagIsLinearInDistinctParts(string shape, int depth)
    {
        var c = Checked(LetDagUse(shape, depth));
        Assert.Equal(string.Empty, MinimalEmissionTest.Problems(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A warm rebind and a warm Analyze of a used `let` DAG allocate nothing: the opening's and the walks' tables keep their capacity.
    [Theory]
    [InlineData("use")]
    [InlineData("meet")]
    [Trait("Purpose", "Allocation")]
    public void AWarmPassOverAUsedLetDagAllocatesNothing(string shape)
    {
        var c = Checked(LetDagUse(shape, 12));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
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

    // A `let` DAG t_{k+1} = (t_k, t_k), t_0 = (p, p), and its use: a leaf through `keep`, a `let` copy and depth + 1 projections, or a
    // Closure that captures it (the review's du1 and du2 rows).
    // The chain programs: D0 (`d0`) <- D1 <- ... <- Dn, D(n-1) stating `identity`, C bounding Item by Dn, and g's 10 uses
    // `T.(C).Item.(D0).X` followed by `use`, bound.
    private static Binding ChainUses(int n, string d0, string identity, string use)
    {
        var source = new System.Text.StringBuilder("enum M\n    A\n    public func f(self) -> i32 => 1\ncontract D0\n" + d0);
        for (var i = 1; i <= n; i++)
        {
            source.Append($"contract D{i}: D{i - 1}\n").Append(i == n - 1 ? identity : string.Empty);
        }

        source.Append($"contract C\n    associate Item is D{n}\nfunc g<T>() -> i32\n    T is C\n");
        for (var k = 0; k < 10; k++)
        {
            source.Append($"    let x{k} = T.(C).Item.(D0).X{use}\n");
        }

        source.Append("    return 0\n");
        var c = CompilationTestHelper.Parse(source.ToString());
        c.Bind();
        return c.Binding;
    }

    private static string LetDagUse(string shape, int depth)
    {
        if (shape == "meet")
        {
            var chain = new System.Text.StringBuilder("func use(p: ref/i64, q: ref/i64, k: bool) -> ref/i64 during (p and q)\n    let a0 = p\n    let b0 = q\n");
            for (var i = 1; i <= depth; i++)
            {
                chain.Append($"    let a{i} = if k => a{i - 1} else => b{i - 1}\n    let b{i} = if k => b{i - 1} else => a{i - 1}\n");
            }

            return chain.Append($"    return a{depth}\npublic func main() => ()\n").ToString();
        }

        var text = new System.Text.StringBuilder("func keep<T>(x: T) -> T\n    return x@move\nfunc use(p: ref/i64) -> ref/i64 during p\n    let t0 = (p, p)\n");
        for (var i = 1; i <= depth; i++)
        {
            text.Append($"    let t{i} = (t{i - 1}, t{i - 1})\n");
        }

        var path = string.Join('.', Enumerable.Range(0, depth + 1).Select(static i => i % 2));
        text.Append(shape == "use" ? $"    let u = keep(t{depth})\n    let w = u\n    return w.{path}\n" : $"    let f = func [t{depth}] () -> ref/i64 during p => t{depth}.{path}\n    return f()\n");
        return text.Append("public func main() => ()\n").ToString();
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

    // A deep-heir chain of a conditional conformance (valid, SPEC 8.8.1, 8.8.2, 8.6.2): L0 conforms when X is Copy, L(k) : L(k-1), and a
    // use of L(k)<i32>.(C).Item at every level.
    private static string ConditionalHeirChain(int depth)
    {
        var text = new System.Text.StringBuilder("contract C\n    associate Item\nopen struct L0<X>\n    Self is C when X is Copy\n        associate C.Item is X\n");
        for (var i = 1; i < depth; i++)
        {
            text.Append($"open struct L{i}<X> : L{i - 1}<X>\nfunc use{i}(x: L{i}<i32>.(C).Item) -> i32 => 1\n");
        }

        return text.ToString();
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
        public void AColdBindOfAnInheritanceChainIsLinear() => ColdBindIsLinear(HeirChain);

        // Z6 Revision 8 (C-462's C1): a cold Bind of HolderChain, whose publication's fixed point takes one round per holder
        // (TheFitMarkCrossesAChainOfHoldersOneRoundPerHolder), allocates linearly in its length: no round allocates (MayWrite's member
        // scratch is reused).
        [Fact]
        public void AColdBindOfAHolderChainIsLinear() => ColdBindIsLinear(HolderChain);

        // SPEC 8.6.2, 8.8.2 (PLAN G665): a warm rebind of a conditional inheritance chain, every heir's scope and every level's
        // conditions included, allocates nothing.
        [Fact]
        public void AWarmRebindOfAnInheritanceChainAllocatesNothing()
        {
            var c = CompilationTestHelper.ParseSuccess(ConditionalHeirChain(64));
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        }

        // A cold Bind of a chain at three lengths, each double the one before: each completes, and its allocation grows within factor 3.
        private static void ColdBindIsLinear(Func<int, string> chain)
        {
            int[] lengths = [64, 128, 256];
            var compilations = new Compilation[lengths.Length];
            var bytes = AllocationMeasurement.MeasureSeries(lengths.Length, i =>
            {
                var c = compilations[i] = CompilationTestHelper.Parse(chain(lengths[i]));
                return () => c.Bind();
            });
            Assert.All(compilations, static c => Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c)));
            Assert.True(bytes[0] < bytes[1] && MirBorrowsTest.Grows(bytes, 3), $"bytes {string.Join(", ", bytes)}");
        }
    }
}
