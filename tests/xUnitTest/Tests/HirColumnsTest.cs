// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The published HIR columns (docs/dev/COMPILER_ARCHITECTURE.md, R2b) over the Kimi library and every milestone program:
/// every node of the tree has an Op, every call Op its call, every selected symbol a Res, a parameter read from a later default the
/// prepared slot, every jump its target, every evaluated node its completion and its Place, and every consumed child its Adjust;
/// publishing changes no other result and allocates nothing again.</summary>
public sealed class HirColumnsTest
{
    // Virtual bodies over a generic base whose applicability has premises, and over a closed base.
    private const string VirtualSource =
        "contract Marker\nstruct NonCopy\n    public init() => ()\n    drop => ()\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    public virtual func first(self: objref/Self) -> i32 => 1\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return value@follow\n" +
        "    public virtual func last(self: objref/Self) -> i32 => 3\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    override func copy(self: objref/Self, value: ref/T) -> T => value@follow\n    override func last(self: objref/Self) -> i32 => 30\n" +
        "open struct Plain\n    public init() => ()\n    public virtual func size(self: objref/Self) -> i32 => 1\nstruct Sub : Plain\n    public init() => ()\n" +
        "    override func size(self: objref/Self) -> i32 => 2\nlet a = Leaf<i32>.init()@obj\nlet b = Leaf<NonCopy>.init()@obj\nlet p = Sub.init()@obj\n" +
        "require a.copy(7) == 7 and a.last() == 30 and b.last() == 30 and b.first() == 1 and p.size() == 2 else => $abort(\"virtual\")\n";

    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in SemanticSnapshotTest.CaseNames())
            {
                if (name == "library" || name.StartsWith("milestone/", StringComparison.Ordinal))
                {
                    data.Add(name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryNodeIsPublished(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        HirTables.Publish(c);
        var problems = Problems(module);
        Assert.True(problems.Count == 0, $"{problems.Count} unpublished facts:\n{string.Join('\n', problems.Take(40))}");
    }

    // SPEC 7.2.3: a later default reads the slot its call prepared for an earlier parameter; a closure there reads its capture.
    [Fact]
    public void ParametersReadInLaterDefaultsAreThePreparedSlots()
    {
        var c = MinimalEmissionTest.Analyze("func run(k: i32, m: i32 = k + 1, action: (i32) -> i32 = func [k] (v) => v + k, other: (i32) -> i32 = func (v) => v + m) -> i32 => action(m) + other(k)\n" +
            "require run(1) == 6 else => $abort(\"run\")\n");
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var function = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "run");
        var reads = Reachable(c.Kotonoha).Where(x => x is IdentifierNameKoto && HirTables.Op(x) == HirOp.Name && IsWithin(x, function)).Select(static x => HirTables.ResOf(x));

        // k in the default of m; v, the capture k; v, the capture m; then action, m, other and k in the body.
        Assert.Equal([HirRes.PreparedParam, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Param, HirRes.Param, HirRes.Param], reads);
    }

    // A virtual or override body Binding bound is built, the conditional one too; the corpus has none.
    [Fact]
    public void VirtualBodiesArePublished()
    {
        var c = MinimalEmissionTest.Analyze(VirtualSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var functions = Reachable(c.Kotonoha).OfType<FunctionKoto>().Where(static x => x.IsVirtual || x.IsOverride).ToArray();
        Assert.Equal(7, functions.Length);
        Assert.All(functions, static x => Assert.Equal(HirOp.Body, HirTables.Op(x)));
    }

    // SPEC 14.10: a return, an exit, a call of a Never function and a loop no exit leaves diverge, and so does an if or a match
    // whose every arm diverges; a loop an exit leaves completes.
    [Fact]
    public void CompletionFollowsTheNeverRule()
    {
        var c = MinimalEmissionTest.Analyze("func choose(c: bool) -> i32\n    if c\n        return 1\n    else\n        return 2\n" +
            "func pick(x: i32) -> i32\n    match x\n        0 => return 1\n        _ => $abort(\"pick\")\n" +
            "func spin() -> i32\n    var i: i32 = 0\n    loop\n        i = i + 1\n" +
            "func count() -> i32\n    var i: i32 = 0\n    loop\n        require i < 3 else => exit\n        i = i + 1\n    return i\n" +
            "require choose(true) == 1 and pick(0) == 1 and count() == 3 else => $abort(\"completion\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var nodes = Reachable(c.Kotonoha);
        var functions = nodes.OfType<FunctionKoto>().Where(static x => x.Name is "choose" or "pick" or "spin" or "count").ToDictionary(static x => x.Name);
        HirCompletion First<T>(string function)
            where T : Koto => HirTables.CompletionOf(nodes.OfType<T>().First(x => IsWithin(x, functions[function])));

        Assert.Equal(HirCompletion.Diverges, First<IfKoto>("choose"));
        Assert.Equal(HirCompletion.Diverges, First<JumpKoto>("choose"));
        Assert.Equal(HirCompletion.Diverges, First<MatchKoto>("pick"));
        Assert.Equal(HirCompletion.Diverges, First<InvocationKoto>("pick"));
        Assert.Equal(HirCompletion.Diverges, First<LoopKoto>("spin"));
        Assert.Equal(HirCompletion.Completes, First<LoopKoto>("count"));
        Assert.Equal(HirCompletion.Diverges, First<JumpKoto>("count"));
        Assert.Equal(HirCompletion.Completes, First<RequireKoto>("count"));
    }

    // SPEC 3.5, 10.2, 15.1.5, 15.1.6: a Copy Place is Copied, a stored exclusive reference Reborrowed, a Place a ref parameter takes
    // shared-borrowed in place, an @move operand moved, a Subject borrowed in place, and a value taken as a Temporary.
    [Fact]
    public void AdjustStatesTheRecordedOperations()
    {
        const string Source = "func read(p: ref/i32) -> i32 => p@follow\nfunc bump(target: uniq/i32) => target@follow += 1\nfunc count(text: string) -> i32 => 1\n" +
            "func twice(slot: uniq/i32)\n    bump(slot)\nfunc pick(value: Option<i32>) -> i32\n    match value\n        .Some(let inner) => return inner\n        .None => return 0\n" +
            "var number: i32 = 1\nlet owned = \"owned\"\ntwice(number@uniq)\nlet copied = number\n" +
            "require read(number) == 2 and count(owned@move) == 1 and copied == 2 and pick(.Some(5)) == 5 else => $abort(\"adjust\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var adjusted = Reachable(c.Kotonoha).Where(static x => HirTables.AdjustOf(x).Acquire != HirAcquire.Unset)
            .Select(x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {HirTables.AdjustOf(x).Acquire}/{HirTables.AdjustOf(x).Adaptation}");
        // The require's condition operands are Temporaries, and the callee of $abort a group's operand, which the group acquires.
        string[] expected =
        [
            "p@follow Copy/None", "p None/None", "target@follow None/None", "target None/None", "1 Temporary/None", "1 Temporary/None",
            "slot Reborrow/None", "value None/SharedBorrow", "let inner None/SharedBorrow", "inner None/ReferentRead", "0 Temporary/None",
            "1 Temporary/None", "\"owned\" Temporary/None", "number@uniq Temporary/None", "number None/None", "number Copy/None",
            "read(number) == 2 and count(owned@move) == 1 and copied == 2 and pick(.Some(5)) == 5 Temporary/None",
            "read(number) == 2 and count(owned@move) == 1 and copied == 2 Temporary/None", "read(number) == 2 and count(owned@move) == 1 Temporary/None",
            "read(number) == 2 Temporary/None", "read(number) Temporary/None", "number None/SharedBorrow", "2 Temporary/None",
            "count(owned@move) == 1 Temporary/None", "count(owned@move) Temporary/None", "owned@move Temporary/None", "owned Move/None",
            "1 Temporary/None", "copied == 2 Temporary/None", "copied Copy/None", "2 Temporary/None", "pick(.Some(5)) == 5 Temporary/None",
            "pick(.Some(5)) Temporary/None", ".Some(5) Temporary/None", "5 Temporary/None", "5 Temporary/None", "abort(\"adjust\") None/None",
            "\"adjust\" Temporary/None",
        ];
        Assert.Equal(expected, adjusted);
    }

    // SPEC 3.4.1, 4.6.9, 7.1.1, 15.1.3, 15.1.6: a Field through two reference layers follows both, a literal fixed-array position is a
    // ConstIndex and a runtime one an Index, a user index publishes the element Place, which an update through its Field takes by indexUniq
    // (its receiver borrowed exclusively) and a read by index, and a borrowed Case payload is its Downcast.
    [Fact]
    public void PlaceShapeStatesThePlaceForms()
    {
        const string Source = "struct Point\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
            "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
            "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
            "    public func index(self, key: ref/isize) -> place ref/T during self => self.first\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/T during self => self.first\n" +
            "func through(p: ref/(ref/Point during a)) -> i32 => p.x\nfunc pick(value: Option<Point>) -> i32\n    match value\n        .Some(let inner) => return inner.x\n        .None => return 0\n" +
            "var pts = Pair<Point>.init(Point.init(1), Point.init(2))\npts[0].x = 5\nlet fixed: [3 of i32] = [1, 2, 3]\nlet i: isize = 2\nlet p = Point.init(3)\nlet r = p@ref\n" +
            "require through(r@ref) == 3 and pick(.Some(Point.init(4))) == 4 and pts[1].x == 5 and fixed[1] + (fixed)[i] == 5 else => $abort(\"place\")\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var start = Source.IndexOf("func through", StringComparison.Ordinal);
        var places = Reachable(c.Kotonoha).Where(x => x.Span.Start >= start && HirTables.PlaceOf(x).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value))
            .Select(x => $"{Source.Substring(x.Span.Start, x.Span.Length)} {HirTables.PlaceOf(x).Kind}+{HirTables.PlaceOf(x).Layers} {HirTables.AdjustOf(x).Adaptation}");
        // A Field's receiver keeps the access Binding recorded for it (SPEC 3.4.1: one reference through its layers, a shared access of a
        // user index's element); its layers are followed from its own Type.
        string[] expected =
        [
            "p.x Field+0 None", "p Local+2 ReferenceRead", "value Local+0 SharedBorrow", "let inner Downcast+0 SharedBorrow", "inner.x Field+0 None",
            "inner Local+1 None", "pts[0].x Field+0 None", "pts[0] CallResult+0 SharedBorrow", "pts Local+0 ExclusiveBorrow", "p Local+0 None",
            "r Local+0 None", "pts[1].x Field+0 None", "pts[1] CallResult+0 SharedBorrow", "pts Local+0 SharedBorrow", "fixed[1] ConstIndex+0 None",
            "fixed Local+0 None", "(fixed)[i] Index+0 None", "(fixed) Local+0 None", "fixed Local+0 None", "i Local+0 None",
        ];
        Assert.Equal(expected, places);
    }

    // Publishing before the ownership analysis leaves every Binding, control-flow and ownership fact and diagnostic unchanged.
    [Theory]
    [MemberData(nameof(Corpus))]
    public void PublishIsInvisible(string name)
        => Assert.Equal(SemanticSnapshotTest.Take(name, false).Text, SemanticSnapshotTest.Take(name, true).Text);

    // Not a gate (R2b U3): with KIMI_ADJUST_SHADOW naming a file, writes each consumed node of the corpus with its Adjust beside the Copy,
    // Move, read, borrow and Pattern operations the ownership analysis records at it, and a count per pair, for the R4 difference ledger.
    [Fact]
    public void AdjustShadowReport()
    {
        var path = Environment.GetEnvironmentVariable("KIMI_ADJUST_SHADOW");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var rows = new List<string> { "case\tlocation\tkind\top\tadjust\townership" };
        var pairs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            HirTables.Publish(c);
            c.Ownership.Analyze();
            var operations = new Dictionary<Koto, SortedSet<string>>(ReferenceEqualityComparer.Instance);
            foreach (var operation in c.Ownership.Bodies.SelectMany(static x => x.Operations))
            {
                var kind = operation.Kind switch
                {
                    OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern => operation.Kind + ":" + operation.Acquisition,
                    OwnershipOperationKind.Borrow => operation.LoanMode == LoanRequirement.Uniq ? "ExclusiveBorrow" : "SharedBorrow",
                    OwnershipOperationKind.Read => "Read",
                    _ => null,
                };

                if (kind is not null && operation.Source is { } source)
                {
                    (operations.TryGetValue(source, out var set) ? set : operations[source] = new(StringComparer.Ordinal)).Add(kind);
                }
            }

            foreach (var node in Reachable(module))
            {
                if (HirTables.AdjustOf(node) is { Acquire: not HirAcquire.Unset } adjust)
                {
                    var key = $"{adjust.Acquire}/{adjust.Adaptation}\t{(operations.TryGetValue(node, out var set) ? string.Join('+', set) : "-")}";
                    rows.Add($"{name}\t{node.CodeContext.SourceDocument?.Path}:{node.Span.Start}\t{node.Akind}\t{HirTables.Op(node)}\t{key}");
                    pairs[key] = pairs.GetValueOrDefault(key) + 1;
                }
            }
        }

        File.WriteAllLines(path, rows);
        File.WriteAllLines(path + ".summary.txt", pairs.OrderByDescending(static x => x.Value).ThenBy(static x => x.Key, StringComparer.Ordinal).Select(static x => $"{x.Value}\t{x.Key}"));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmPublishAllocatesNothing(bool virtualBodies)
    {
        var c = virtualBodies ? MinimalEmissionTest.Analyze(VirtualSource) : SemanticSnapshotTest.Bind("milestone/Milestone26").Compilation;
        HirTables.Publish(c);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            HirTables.Publish(c);
        }));
    }

    // Every parsed node in the tree has an Op, a call Op its call, a selected symbol its Res (the prepared slot exactly where the
    // parameter is read in a later default of its function), a jump the target it transfers to, and an evaluated node its completion;
    // every child an Op consumes has its Adjust: a call's argument and receiver the adaptation of the operation of a call that takes them,
    // unless Binding recorded one at the node itself, and a Pattern binding its acquisition.
    private static List<string> Problems(Kotonoha module)
    {
        var table = module.Syntax;
        var binding = module.Compilation.Binding;
        var problems = new List<string>();
        var operations = new Dictionary<Koto, HashSet<HirAdaptation>>(ReferenceEqualityComparer.Instance);
        void Expect(Koto source, HirAdaptation adaptation)
        {
            if (!binding.TryGetAdaptation(source, out _))
            {
                (operations.TryGetValue(source, out var set) ? set : operations[source] = []).Add(adaptation);
            }
        }

        bool Parsed(Koto? node) => node is not null && ReferenceEquals(node.SyntaxOwner, table) && node.SyntaxId <= table.ParsedCount;
        foreach (var node in Reachable(module))
        {
            // A synthesized node in the tree belongs to the expansion that made it.
            if (!Parsed(node))
            {
                continue;
            }

            var op = HirTables.Op(node);
            var where = $"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {node.StateOf()} {op}";
            if (op == HirOp.Unset || (op == HirOp.Call && CallPlan.Of(node) is null))
            {
                problems.Add(where);
            }

            if (node.SymbolOf() is { } symbol &&
                (HirTables.ResOf(node) == HirRes.None || (HirTables.ResOf(node) == HirRes.PreparedParam) != DefaultParameters.InLaterDefault(node, symbol)))
            {
                problems.Add($"{where} {symbol.Kind} {HirTables.ResOf(node)}");
            }

            if (op is HirOp.Jump or HirOp.Continue && (HirTables.PlanOf(node).Plan is not Koto target || !ReferenceEquals(target, KotoHelper.ResolveTransferTarget((JumpKoto)node))))
            {
                problems.Add($"{where} without its target");
            }

            // An evaluated node has its completion, a Never-typed one diverging; a node the builder does not evaluate has none.
            var evaluated = op is not (HirOp.Unset or HirOp.Syntax or HirOp.Body);
            var completion = HirTables.CompletionOf(node);
            if (evaluated == (completion == HirCompletion.Unset) || (evaluated && ReferenceEquals(node.TypeOf(), BoundType.Never) && completion != HirCompletion.Diverges))
            {
                problems.Add($"{where} completion {completion}");
            }

            // An evaluated node and a node used in place have their Place, a stored Field, an explicit follow and a read name a Place, and
            // the layers a consumer follows are layers of the Type, every one of them under a stored Field, an element or a sequence's metadata.
            var place = HirTables.PlaceOf(node);
            var projection = op is HirOp.Field or HirOp.Index || (op == HirOp.PrimitiveOp && node is MemberAccessKoto);
            if ((evaluated || HirTables.AdjustOf(node).Acquire == HirAcquire.None) == (place.Kind == HirPlaceKind.Unset) || !Peel(node.TypeOf(), place.Layers, out _) ||
                ((op is HirOp.Field or HirOp.Deref || (op == HirOp.Name && HirTables.ResOf(node) != HirRes.Decl)) && place.Kind == HirPlaceKind.Value) ||
                (projection && node is BinaryKoto { Left: var left } && (!Peel(left.TypeOf(), HirTables.PlaceOf(left).Layers, out var projected) || IsLayer(projected))))
            {
                problems.Add($"{where} place {place} of {node.TypeOf()}");
            }

            if (op is HirOp.Unset or HirOp.Error || (op == HirOp.Syntax && node is not FunctionKoto))
            {
                continue;
            }

            foreach (var child in Consumed(node, op))
            {
                if (Parsed(child) && HirTables.AdjustOf(child!).Acquire == HirAcquire.Unset)
                {
                    problems.Add($"{where} consumes {child!.Akind}@{child.Span.Start} without its Adjust");
                }
            }

            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                if (call.Kind != CalleeKind.Value && call.Receiver is { } receiver)
                {
                    Expect(receiver, HirAdjust.Of(call.ReceiverOperation));
                }

                foreach (var argument in call.ArgumentOperations)
                {
                    if (argument.Source is { } source)
                    {
                        Expect(source, HirAdjust.Of(argument));
                    }
                }
            }

            if (node is MatchKoto match && binding.TryGetMatch(match, out var plan))
            {
                foreach (var position in plan!.Positions)
                {
                    if (position.Kind == BoundPatternKind.Binding && Parsed(position.Source) && HirTables.AdjustOf(position.Source) != HirAdjust.Of(position.Acquisition, position.AccessMode))
                    {
                        problems.Add($"{where} binds {position.Source.Span.Start} {position.Acquisition} as {HirTables.AdjustOf(position.Source)}");
                    }
                }
            }
        }

        foreach (var (source, adaptations) in operations)
        {
            if (Parsed(source) && !adaptations.Contains(HirTables.AdjustOf(source).Adaptation))
            {
                problems.Add($"{source.CodeContext.SourceDocument?.Path}:{source.Span.Start} {source.Akind} takes {HirTables.AdjustOf(source)} for {string.Join(',', adaptations)}");
            }
        }

        return problems;
    }

    // The children an Op consumes (SPEC 3.5, 10.2, 15.1.6): its own operands, conditions, Subject, initializer, result or jump operand,
    // then each call's receiver, value callee and arguments, and an enum Case's payloads.
    private static IEnumerable<Koto?> Consumed(Koto node, HirOp op)
    {
        IEnumerable<Koto?> own = (node, op) switch
        {
            (FunctionKoto function, _) => function.Parameters.Select(static x => x.DefaultValue).Append(op is HirOp.Body or HirOp.Closure ? Result(function.ExpressionBody) : null),
            (PropertyAccessorKoto accessor, HirOp.Body) => [Result(accessor.Body)],
            (PropertyKoto property, HirOp.Body) => [property.InitializerKoto],
            (VariableKoto variable, HirOp.Let) => [variable.InitializerKoto],
            (CodeBlockKoto block, HirOp.Block) => [Result(block.TrailingExpression)],
            (DoKoto scoped, HirOp.Do) => [Result(scoped.Body)],
            (LabeledKoto label, HirOp.Group) => [label.Target],
            (MemberAccessKoto member, HirOp.Field or HirOp.PrimitiveOp) => [member.Left],
            (BinaryKoto binary, HirOp.Group or HirOp.Borrow or HirOp.Deref or HirOp.Convert or HirOp.RuntimeTypeTest) => [binary.Left],
            (BinaryKoto binary, HirOp.Index or HirOp.Assign or HirOp.CompoundAssign or HirOp.PrimitiveOp or HirOp.Logical) => [binary.Left, binary.Right],
            (UnaryKoto unary, HirOp.Group or HirOp.Deref or HirOp.PrimitiveOp or HirOp.Range or HirOp.CompoundAssign) => [unary.Operand],
            (RangeKoto range, HirOp.Range) => [range.Start, range.End],
            (TupleLiteralKoto tuple, _) => tuple.Elements,
            (ArrayLiteralKoto array, _) => array.Elements,
            (DictionaryLiteralKoto dictionary, _) => dictionary.Entries.SelectMany(static x => new[] { x.Key, x.Value }),
            (JumpKoto jump, HirOp.Jump) => [jump.Expression],
            (IfKoto conditional, _) => conditional.Branches.SelectMany(static x => new[] { x.Condition, Result(x.Body) }).Append(Result(conditional.ElseBody)),
            (WhileKoto loop, _) => [loop.Condition],
            (RequireKoto require, _) => [require.Condition],
            (TestVerificationKoto verification, _) => [verification.Condition, verification.Message],
            (DiscardKoto discard, _) => [discard.Operand],
            (MatchKoto match, _) => match.Arms.SelectMany(static x => new[] { x.Guard, Result(x.Body) }).Prepend(match.Expression),
            (ForKoto loop, HirOp.For) => [loop.Iterable],
            _ => [],
        };

        var calls = Enumerable.Range(0, 8).Select(x => CallPlan.Of(node, x)).TakeWhile(static x => x is not null)
            .SelectMany(static x => x!.ArgumentOperations.ToArray().Select(static y => y.Source).Prepend(x.Kind == CalleeKind.Value ? x.CalleeValue : x.Receiver));
        var payloads = op == HirOp.Aggregate && node.CodeContext.Compilation.Binding.TryGetEnumConstruction(node, out var construction)
            ? construction!.PayloadOperations.ToArray().Select(static x => x.Source) : [];
        return own.Concat(calls).Concat(payloads);

        // A body or operand that supplies its construct's value.
        static Koto? Result(Koto? source) => source is not null && KotoHelper.IsValueContext(source) ? source : null;
    }

    // SPEC 3.4.1, 13.5.5.1: a reference, object handle or pair layer, which selection follows implicitly.
    private static bool IsLayer(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Components.Count: 1, Semantics: not (SemanticsKind.Owner or SemanticsKind.Raw or SemanticsKind.Parameter) } || Binding.TryPairLayer(type, out _, out _);

    private static bool Peel(BoundType? type, int layers, out BoundType? result)
    {
        for (; layers > 0 && IsLayer(type); layers--)
        {
            type = Binding.TryPairLayer(type, out _, out var target) ? target : type!.Components[0];
        }

        result = type;
        return layers == 0;
    }

    private static bool IsWithin(Koto node, Koto ancestor)
    {
        for (Koto? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Koto> Reachable(Kotonoha module)
    {
        var nodes = new List<Koto>();
        var collector = new Collector(nodes);
        collector.Visit(module.RootKoto);
        return nodes;
    }

    private sealed class Collector(List<Koto> nodes) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            nodes.Add(node);
            base.Visit(node);
        }
    }
}
