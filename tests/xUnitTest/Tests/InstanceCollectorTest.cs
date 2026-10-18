// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The instance collector (R5 K1, K4a; K package 4.3-4.6, DECISIONS R5-M35, R5-M40): the roots are the closed bodies of the root
/// modules, a Fn, getter, setter, default-template or closure call is a target at its site with whether a runtime path reaches it, a
/// generic callee an instance keyed by its erased substitution, a callee only the checks reach is in the checked set, the admission
/// limits and Binding's storage bound are located records, the work grows linearly and collecting again allocates nothing.</summary>
public sealed class InstanceCollectorTest
{
    // A generic function over a Copy T, and a generic struct over one, whose bodies the cases complete.
    private const string Outer = "func outer<T>(x: T) -> T\n    T is Copy\n";
    private const string CopyBox = "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n";

    // The F3 reproducers' nested functions (X40): one level into a generic function, one there calling a selected specialization, two
    // levels in, in a block, in a generic struct's method and in a specialization inside a generic struct.
    private const string OneLevel = Outer + "    func middle(y: T) -> T => y\n    return middle(x)\n";

    private const string OneLevelSpecialized = "func describe<T>() -> i32 => 1\nspecialize func describe<i32>() -> i32 => 2\nfunc outer<T>(x: T) -> i32\n" +
        "    T is Copy\n    func inner() -> i32 => describe<T>()\n    return inner()\n";

    private const string TwoLevels = Outer + "    func middle(y: T) -> T\n        func inner(z: T) -> T => z\n        return inner(y)\n    return middle(x)\n";

    private const string InBlock = Outer + "    if true\n        func inner(z: T) -> T => z\n        return inner(x)\n    return x\n";

    private const string InMethod = CopyBox + "    public func get(self: ref/Self) -> T\n        func inner(z: T) -> T => z\n        return inner(self.value)\n" +
        Outer + "    return Box<T>.init(x).get()\n";

    private const string InSpecialization = CopyBox + "    public func f<U>(x: U) -> i32 => 1\n    specialize func f<i32>(x: i32) -> i32\n" +
        "        func inner(y: i32) -> i32 => y\n        return inner(2)\nfunc outer<T>(x: T) -> i32\n    T is Copy\n    return Box<T>.f(5)\n";

    // A generic function nested in a closed function, which has no instance to carry.
    private const string InClosed = "func host() -> i32\n    func pick<U>(x: U) -> U => x@move\n    return pick(1)\n" + Outer + "    _ = host()\n    return x\n";

    // Closed functions, a generic one and a generic struct, a closure, calls only the checks reach, a computed property with both
    // accessors, mutually recursive defaults (template calls) and top-level statements that call them and the library.
    private const string Source = "func plain() -> i32 => 1\nfunc pick<T>(value: T) -> T => value\n" +
        "func host() -> i32\n    let add = func [] (x: i32) -> i32 => x + 1\n    return add(plain())\n" +
        "func after() -> i32\n    return 1\n    _ = later()\n    _ = Kimi.Time.Duration.init(microseconds: 5)\n    return 2\nfunc later() -> i32 => 3\n" +
        "func f(n: i32, value: i32 = if n == 0 => 1 else => g(n - 1)) -> i32 => value\n" +
        "func g(n: i32, value: i32 = if n == 0 => 2 else => f(n - 1)) -> i32 => value\n" +
        "struct Box<T>\n    public var value: T\n    public init(value: T) => self.value = value\n" +
        "struct Cell\n    var stored: i32\n    public computed value: i32\n        get(self: ref/Self) -> i32 => self.stored\n" +
        "        set(self: uniq/Self, value: i32) -> () => self.stored = value\n    public init(stored: i32) => self.stored = stored\n" +
        "var cell = Cell.init(3)\ncell.value = 4\nlet box = Box<i32>.init(cell.value)\nvar watch = Kimi.Time.Stopwatch.init()\n" +
        "require host() == 3 and pick(box.value) == 4 and after() == 1 and f(2) == 1 else => $abort(\"collect\")\n";

    // The F3 reproducers' shapes (X40): the declarations, the nested function, its items now and the refused call (null: none).
    public static TheoryData<string, string, int, string?> NestedCases => new()
    {
        { OneLevel, "middle", 2, null },
        { OneLevelSpecialized, "inner", 2, null },
        { TwoLevels, "inner", 0, "inner(y)" },
        { InBlock, "inner", 0, "inner(x)" },
        { InMethod, "inner", 0, "inner(self.value)" },
        { InSpecialization, "inner", 0, "inner(2)" },
        { InClosed, "pick", 1, null },
    };

    // K 4.4: every body of the root module whose definition does not instantiate, the top-level statements, the closure of a closed
    // function, both accessors of a closed struct and the default templates among them; generic bodies and library bodies only as the
    // targets of calls.
    [Fact]
    public void TheRootsAreTheClosedBodiesOfTheRootModules()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(
            [
                "Closure host", "DefaultTemplate f", "DefaultTemplate g", "Fn after", "Fn f", "Fn g", "Fn host", "Fn init", "Fn later", "Fn plain",
                "Fn top", "Getter value.Get", "Setter value.Set",
            ],
            set.Items[..set.RootCount].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Fn init [Box<i32>]", "Fn pick [i32]", "library Fn init in struct Duration", "library Fn init in struct Stopwatch"],
            set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
    }

    // K 4.5 rows 1 and 5: each callee is a target at its call, in location order (a default omitted by a call of f is its template's call,
    // the Closure is called through a borrow, pick and Box's init are instances).
    [Fact]
    public void ClosedCallsAreTargetsInLocationOrder()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(["Fn plain", "Closure host"], Targets(program, set, "Fn host"));
        Assert.Equal(["DefaultTemplate g", "Fn g"], Targets(program, set, "DefaultTemplate f"));
        Assert.Equal(
            [
                "Fn init", "Setter value.Set", "Getter value.Get", "Fn init [Box<i32>]", "library Fn init in struct Stopwatch", "Fn host", "Fn pick [i32]",
                "Fn after", "DefaultTemplate f", "Fn f",
            ],
            Targets(program, set, "Fn top"));
        for (var item = 0; item < set.Items.Length; item++)
        {
            var targets = set.Targets(item).ToArray();
            Assert.Equal(targets.OrderBy(static x => x.At.Block).ThenBy(static x => x.At.Index), targets);
            Assert.All(targets, x => Assert.Equal(MirTerminatorKind.Call, program.Bodies[set.Items[item].BodyOrdinal].Blocks[x.At.Block].Terminator.Kind));
        }
    }

    // SPEC 8.15.3, K 4.5: a call only the checks reach (after a return) is a target without the runtime bit, and a library body only such a
    // call reaches is in the checked set.
    [Fact]
    public void ACheckOnlyCallKeepsItsCalleeInTheCheckedSet()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(["Fn later (check)", "library Fn init in struct Duration (check)"], Targets(program, set, "Fn after"));
        Assert.All(set.Targets(Item(program, set, "Fn top")).ToArray(), static x => Assert.True(x.Runtime));
    }

    // K 4.3: a repeated run of a program version keeps its set, and a program with validator findings has an empty one.
    [Fact]
    public void AVersionIsCollectedOnceAndFindingsEmptyTheSet()
    {
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types);
        var set = collector.Run(program, 1);
        var items = set.Items.Length;
        Assert.Same(set, collector.Run(program, 1));
        Assert.Equal(items, set.Items.Length);
        program.Findings.Add(new(MirViolation.Callee, program.Bodies[0].Source));
        Assert.Equal(0, new InstanceCollector(c.Types).Run(program, 1).Items.Length);
    }

    // KD1, KD3: a generic callee is an instance keyed by its definition and erased substitution, represented by the substitution that first
    // admitted it, in the one view it selects; every item's key is erased exactly when its definition instantiates; Origin-only variants
    // are one item.
    [Fact]
    public void AGenericCalleeIsAnInstanceOfItsErasedSubstitution()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nfunc keep<T>(value: T) -> T\n    T is Copy\n    return value\n" +
            "func host(a: ref/i32, b: ref/i32) -> i32\n    _ = keep(a)\n    _ = keep(b)\n    return 0\nlet x = 1\nlet y = 2\n" +
            "require host(x@ref, y@ref) == 0 and pick(3) == 3 and pick(true) else => $abort(\"pick\")\n");
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.All(set.Items.ToArray(), x => Assert.Equal(x.Key.Body.Def.Decl is { } decl && c.Types.Instantiates(decl), x.Key.Body.Erased is not null));
        Assert.All(set.Items.ToArray(), x => Assert.Same(x.Key.Body.Erased, x.Representative is null ? null : c.Types.Erase(x.Representative)));
        Assert.Equal(["Fn keep [ref/i32]", "Fn pick [bool]", "Fn pick [i32]"], set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
        var keep = Item(program, set, "Fn keep [ref/i32]");
        Assert.Equal([keep, keep], set.Targets(Item(program, set, "Fn host")).ToArray().Select(static x => x.Item).Where(x => x == keep));
    }

    // K 4.6: a recursion under one substitution reuses its item; one whose key grows stops when 16 items of its definition are on the chain,
    // with GenerationResourceLimit_Kd at the recursive call, and collection goes on.
    [Fact]
    public void RecursionReusesItsItemAndAGrowingKeyStopsAtItsCall()
    {
        var c = MirBuildTest.Compile(GenericStorageEmissionTest.Box + "func count<T>(n: i32, value: T) -> i32\n    T is Copy\n    if n == 0 => return 0\n" +
            "    return count(n - 1, value) + 1\nfunc grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\n" +
            "require count(2, 7) == 2 and grow<i32>(1, 3) == 0 else => $abort(\"recursion\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types).Run(program, 1);
        var count = Item(program, set, "Fn count [i32]");
        Assert.Contains(set.Targets(count).ToArray(), x => x.Item == count);
        Assert.Equal(16, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn grow", StringComparison.Ordinal)));
        var record = Assert.Single(set.Records);
        Assert.Equal((DiagnosticCode.GenerationResourceLimit_Kd, "grow<Box<T>>(Box<T>.init(value@move), n - 1)"), (record.Code, c.SyntaxOf(record.Subject).ToString()));
    }

    // PLAN G128, PD30: an instance whose stored Types nest past Binding's bound is the capability table's storage-nesting gap, Unsupported_Kd
    // at the requesting call, while the twin one level below is an instance: Option^31<ref/i32> (storage depth 63) and Array^64<i32> pass,
    // Option^32<ref/i32> (65) and Array^65<i32> do not.
    [Theory]
    [InlineData("Option", 31, true)]
    [InlineData("Option", 32, false)]
    [InlineData("Array", 64, true)]
    [InlineData("Array", 65, false)]
    public void StorageNestingIsAGapAtTheRequestingCall(string wrapper, int depth, bool admitted)
    {
        var type = string.Concat(Enumerable.Repeat(wrapper + "<", depth)) + (wrapper == "Option" ? "ref/i32" : "i32") + new string('>', depth);
        var value = wrapper == "Option" ? ".None" : "[]";
        var c = MirBuildTest.Compile($"func pick<T>(value: T) -> T => value@move\npublic func main() -> ()\n    let v: {type} = {value}\n    let w = pick(v@move)\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types).Run(program, 1);
        Assert.Equal(admitted, set.Items.ToArray().Any(x => Label(program, x).StartsWith("Fn pick", StringComparison.Ordinal)));
        Assert.Equal(admitted ? [] : [(DiagnosticCode.Unsupported_Kd, "pick(v@move)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // SPEC 6.1.3, 8.13.3, DECISIONS R5-M40 (the F3 reproducers, X40): an instance of a nested function holds its enclosing instance. A call
    // one level into a generic function carries it, so each instance of the enclosing function has its own item; a call two levels in,
    // from a block, in a generic struct's method or in a specialization inside a generic struct does not, so the edge is the
    // EnclosingInstance gap at the call until K5, whose calls carry it (then two items each). Every admitted representative closes its body.
    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedFunctionsNeedTheirEnclosingInstance(string declarations, string nested, int items, string? refused)
    {
        var c = MirBuildTest.Compile(declarations + "require outer(3) == outer(3) and outer(true) == outer(true) else => $abort(\"nested\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types).Run(program, 1);
        Assert.Equal(items, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn " + nested + " [", StringComparison.Ordinal)));
        Assert.Equal(refused is null ? [] : [(DiagnosticCode.Unsupported_Kd, refused)], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.All(set.Items.ToArray(), x => Assert.True(x.Representative is null || Closes(c.Types, program.Bodies[x.BodyOrdinal], x.Representative), Label(program, x)));
    }

    // K 4.3: a representative that leaves its body open is a broken contract, InternalInvariant_Kd at the call and no item. Binding never
    // gives such a call, so the test forges one: its substitution becomes the callee's own identity.
    [Fact]
    public void AnOpenRepresentativeIsABrokenContract()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nlet x = pick(1)\n");
        var program = c.BuildMir();
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        var block = Enumerable.Range(0, top.Blocks.Length)
            .Single(b => top.Blocks[b].Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Symbol.Name: "pick" } });
        var call = top.Blocks[block].Terminator;
        top.Terminate(block, call with { Callee = call.Callee with { Subst = c.Types.Identity(call.Callee.Symbol!.Declaration) } });
        var set = new InstanceCollector(c.Types).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, "pick(1)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x).StartsWith("Fn pick", StringComparison.Ordinal));
    }

    // X19: an item past depth 128 on its admission chain, and the 1,025th item of one definition, are GenerationResourceLimit_Kd at their calls.
    [Fact]
    public void TheDepthAndSetLimitsStopAtTheirCalls()
    {
        var chain = string.Concat(Enumerable.Range(0, 130).Select(static i => $"func step{i}<T>(value: T) -> T => {(i == 129 ? "value@move" : $"step{i + 1}<T>(value@move)")}\n"));
        var c = MirBuildTest.Compile(chain + "let x = step0<i32>(1)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types).Run(c.BuildMir(), 1);
        Assert.Equal([(DiagnosticCode.GenerationResourceLimit_Kd, "step128<T>(value@move)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Equal(128, set.Items.ToArray().Max(static x => x.Depth));

        var sizes = string.Concat(Enumerable.Range(1, 1025).Select(static n => $"_ = size<{n}>()\n"));
        c = MirBuildTest.Compile("func size<length N>() -> isize => N\n" + sizes);
        var program = c.BuildMir();
        set = new InstanceCollector(c.Types).Run(program, 1);
        Assert.Equal([(DiagnosticCode.GenerationResourceLimit_Kd, "size<1025>()")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Equal(1024, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn size", StringComparison.Ordinal)));
    }

    // K 4.9 with a second, non-root module: a record caused in its inlined default moves to the root module's omitting call, and one caused
    // in its body to the root-module call that first admitted the chain.
    [Fact]
    public void TheLocationRuleLeavesNonRootModules()
    {
        var deep = string.Concat(Enumerable.Repeat("Array<", 65)) + "i32" + new string('>', 65);
        var library = "public struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\npublic group Api\n" +
            $"    public func probe<T>(value: T) -> i32 => 0\n    public func take(x: i32, extra: i32 = probe<{deep}>([])) -> i32 => x + extra\n" +
            "    public func grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\n";
        var c = ModuleBindingTest.Create("public func main() -> ()\n    _ = Lib.Api.take(1)\n    _ = Lib.Api.grow<i32>(1, 3)\n    return\n", library);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var set = new InstanceCollector(c.Types).Run(c.BuildMir(), 1);
        Assert.Equal(
            [(DiagnosticCode.Unsupported_Kd, "Lib.Api.take(1)"), (DiagnosticCode.GenerationResourceLimit_Kd, "Lib.Api.grow<i32>(1, 3)")],
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.All(set.Records, static x => Assert.Equal(0, x.Subject.Module));
    }

    // K 4.6, the K package's 2x/4x counters: with twice and four times the item Types, the collector admits and replays twice and four times
    // as much.
    [Fact]
    public void TheWorkGrowsLinearlyWithTheInstances()
    {
        var work = new[] { 8, 16, 32 }.Select(static n =>
        {
            var c = MirBuildTest.Compile(Shape(n));
            var collector = new InstanceCollector(c.Types);
            var set = collector.Run(c.BuildMir(), 1);
            Assert.Empty(set.Records);
            return (Items: (double)set.Items.Length, Work: (double)collector.Work);
        }).ToArray();
        Assert.InRange(work[1].Items / work[0].Items, 1.8, 2.2);
        Assert.InRange(work[2].Items / work[0].Items, 3.6, 4.4);
        Assert.InRange(work[1].Work / work[0].Work, 1.8, 2.2);
        Assert.InRange(work[2].Work / work[0].Work, 3.6, 4.4);

        // N struct Types, each passed through a chain of three generic functions.
        static string Shape(int n) => "func first<T>(value: T) -> T => second(value@move)\nfunc second<T>(value: T) -> T => third(value@move)\n" +
            "func third<T>(value: T) -> T => value@move\n" + string.Concat(Enumerable.Range(0, n).Select(static i =>
                $"struct S{i}\n    public var value: i32\n    public init(value: i32) => self.value = value\nlet s{i} = first(S{i}.init({i}))\n"));
    }

    // A warm rebind rebuilds the MIR and collects it again with the same items and targets, instances included, and no allocation.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone30").Compilation;
        var collector = new InstanceCollector(c.Types);
        var set = collector.Run(c.BuildMir(), 1);
        var (items, targets) = (set.Items.Length, Enumerable.Range(0, set.Items.Length).Sum(x => set.Targets(x).Length));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            collector.Run(c.BuildMir(), 1);
        }));
        Assert.True(set.Items.ToArray().Count(static x => x.Representative is not null) > 40);
        Assert.True(items > 50 && targets > 20, $"{items} items, {targets} targets");
        Assert.Equal((items, targets), (set.Items.Length, Enumerable.Range(0, set.Items.Length).Sum(x => set.Targets(x).Length)));
    }

    // S2-S4 X15: one row per intrinsic, and only ErasedWriter, StaticEnsure, the handle operations and DestroyValues name a target.
    [Fact]
    public void OnlyTheWhitelistedIntrinsicsCarryATarget()
    {
        var targets = Enum.GetValues<IntrinsicId>().Where(static x => x != IntrinsicId.None && IntrinsicRows.Of(x).Target != IntrinsicTarget.None);
        Assert.Equal(
            [
                IntrinsicId.DestroyValues, IntrinsicId.ErasedWriter, IntrinsicId.MakeObj, IntrinsicId.MakeRc, IntrinsicId.MakeArc, IntrinsicId.Clone,
                IntrinsicId.TypeTest, IntrinsicId.StaticEnsure,
            ],
            targets);
        Assert.Throws<InvalidOperationException>(static () => IntrinsicRows.Of(IntrinsicId.None));
    }

    // K 4.3: a representative is closed and makes every local Type of its body closed.
    private static bool Closes(TyCtxt types, MirBody body, Subst representative)
        => representative.Types.ToArray().Append(representative.Declaring).Append(representative.Self).All(static x => x is not { ContainsParameter: true }) &&
            body.Locals.ToArray().All(x => types.Apply(x.Type, representative) is { ContainsParameter: false });

    private static (MirProgram Program, CheckedSet Set) Collect(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (program, new InstanceCollector(c.Types).Run(program, 1));
    }

    private static int Item(MirProgram program, CheckedSet set, string label)
        => Enumerable.Range(0, set.Items.Length).Single(x => Label(program, set.Items[x]) == label);

    private static string[] Targets(MirProgram program, CheckedSet set, string label)
        => set.Targets(Item(program, set, label)).ToArray().Select(x => Label(program, set.Items[x.Item]) + (x.Runtime ? string.Empty : " (check)")).ToArray();

    // A body's kind and definition, a closure by the function enclosing it, a module's top-level statements as top, a library body with
    // the declaration it is in, an instance with its representative's declaring Type and Type arguments.
    private static string Label(MirProgram program, in MonoItem item)
    {
        var body = program.Bodies[item.BodyOrdinal];
        var name = body.Key.Decl?.Declaration switch
        {
            null => "top",
            FunctionKoto { IsAnonymous: true } closure => Enclosing(closure.Parent!).Name,
            var declaration => ((FunctionKoto)declaration).Name,
        };
        var instance = item.Representative is { } subst
            ? " [" + string.Join(", ", (subst.Declaring is { } owner ? [owner] : Array.Empty<BoundType?>()).Concat(subst.Types.ToArray()).Select(static x => TyCtxt.Name(x!))) + "]"
            : string.Empty;
        return (body.Source.Module == 0 ? $"{body.Key.Kind} {name}" : $"library {body.Key.Kind} {name} in {body.Key.Decl!.Scope.Owner}") + instance;

        static FunctionKoto Enclosing(Koto node) => node as FunctionKoto ?? Enclosing(node.Parent!);
    }
}
