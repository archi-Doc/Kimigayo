// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The instance collector (R5 K1; K package 4.3-4.5, DECISIONS R5-M35): the roots are the closed bodies of the root modules, a
/// closed Fn, getter, setter, default-template or closure call is a target at its site with whether a runtime path reaches it, a callee
/// only the checks reach is in the checked set, and collecting again allocates nothing.</summary>
public sealed class InstanceCollectorTest
{
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

    // K 4.4: every body of the root module whose definition does not instantiate, the top-level statements, the closure of a closed
    // function, both accessors of a closed struct and the default templates among them; no generic body, and library bodies only as the
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
            ["library Fn init in struct Duration", "library Fn init in struct Stopwatch"],
            set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
    }

    // K 4.5 rows 1 and 5: each closed callee is a target at its call, in location order (a default omitted by a call of f is its template's
    // call, the Closure is called through a borrow); the generic callees, pick and Box's init, wait for K4a.
    [Fact]
    public void ClosedCallsAreTargetsInLocationOrder()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(["Fn plain", "Closure host"], Targets(program, set, "Fn host"));
        Assert.Equal(["DefaultTemplate g", "Fn g"], Targets(program, set, "DefaultTemplate f"));
        Assert.Equal(
            ["Fn init", "Setter value.Set", "Getter value.Get", "library Fn init in struct Stopwatch", "Fn host", "Fn after", "DefaultTemplate f", "Fn f"],
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

    // A warm rebind rebuilds the MIR and collects it again, the library's closed bodies as roots too, with the same items and targets and no
    // allocation.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var collector = new InstanceCollector(c.Types);
        var set = collector.Run(c.BuildMir(), 2);
        var (items, targets) = (set.Items.Length, Enumerable.Range(0, set.Items.Length).Sum(x => set.Targets(x).Length));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            collector.Run(c.BuildMir(), 2);
        }));
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
    // the declaration it is in.
    private static string Label(MirProgram program, in MonoItem item)
    {
        var body = program.Bodies[item.BodyOrdinal];
        var name = body.Key.Decl?.Declaration switch
        {
            null => "top",
            FunctionKoto { IsAnonymous: true } closure => Enclosing(closure.Parent!).Name,
            var declaration => ((FunctionKoto)declaration).Name,
        };
        return body.Source.Module == 0 ? $"{body.Key.Kind} {name}" : $"library {body.Key.Kind} {name} in {body.Key.Decl!.Scope.Owner}";

        static FunctionKoto Enclosing(Koto node) => node as FunctionKoto ?? Enclosing(node.Parent!);
    }
}
