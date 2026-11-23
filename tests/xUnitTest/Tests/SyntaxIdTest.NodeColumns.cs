// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

/// <summary>Binding's node families (NodeColumns, DECISIONS BU-220) follow their module's ids: each is resized with the table, handed
/// over to a renumbering and moves its rows with their nodes.</summary>
public partial class SyntaxIdTest
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // A Copy declaration (SPEC 3.5.2): the kept Copy families' rows (CopyDeclarations, CopyByType).
    private const string CopyPoint = "struct Point\n    Self is Copy\n    public var x: i32 = 1\n    public var y: i32 = 2\nlet p = Point.init()\n";

    // G949 D2b-d's d7: by reflection over NodeColumns' fields, so a family added without its Resize, Renumbered, Move or Retire line fails
    // here. Every field is a column: a kept family's PagedColumn moves its row into the renumbered column's spare arena, which Retire
    // clears; a pass or edit family's StampedColumn is handed over unmoved and emptied, its stamp with it (no removed node's row stays
    // reachable), and follows one of Binding's two counters.
    [Fact]
    public void EveryNodeFamilyIsResizedRenumberedAndMoved()
    {
        var fields = typeof(NodeColumns).GetFields(Members);
        Assert.NotEmpty(fields);
        Assert.All(fields, static x => Assert.True(IsKept(x) || IsStamped(x), $"{x.Name} is no column."));
        Assert.All(fields.Where(IsStamped), static x => Assert.Contains(x.FieldType.GetGenericArguments()[1], new[] { typeof(Binding.PassCounter), typeof(Binding.EditCounter) }));
        object columns = default(NodeColumns);
        typeof(NodeColumns).GetMethod("Resize", Members)!.Invoke(columns, [40]);
        var rows = new Dictionary<FieldInfo, object>();
        foreach (var field in fields)
        {
            var outer = field.GetValue(columns)!;
            var column = IsStamped(field) ? Read<object>(outer, "rows") : outer;
            var pages = Read<int>(column, "pages");
            Assert.True(pages * PagedColumn<int>.PageSize >= 40, $"{field.Name} is not resized.");
            var directory = new int[pages]; // A column's directory comes with its first write.
            column.GetType().GetField("directory", Members)!.SetValue(column, directory);
            var arena = Array.CreateInstance(column.GetType().GetGenericArguments()[0], PagedColumn<int>.PageSize);
            arena.SetValue(rows[field] = NonDefault(arena.GetType().GetElementType()!), 1);
            column.GetType().GetField("arena", Members)!.SetValue(column, arena);
            column.GetType().GetField("used", Members)!.SetValue(column, PagedColumn<int>.PageSize);
            directory[0] = 1;
            if (IsStamped(field))
            {
                outer.GetType().GetField("rows", Members)!.SetValue(outer, column);
                outer.GetType().GetField("stamp", Members)!.SetValue(outer, 7UL);
            }

            field.SetValue(columns, outer);
        }

        var moved = new object[] { 1, typeof(NodeColumns).GetMethod("Renumbered", Members)!.Invoke(columns, [40])!, 33 };
        typeof(NodeColumns).GetMethod("Move", Members)!.Invoke(columns, moved);
        typeof(NodeColumns).GetMethod("Retire", Members)!.Invoke(moved[1], null);
        foreach (var field in fields)
        {
            var (source, target) = (field.GetValue(columns)!, field.GetValue(moved[1])!);
            if (IsStamped(field))
            {
                Assert.Equal(7UL, Read<ulong>(target, "stamp"));
                Assert.Same(Read<Array>(Read<object>(source, "rows"), "arena"), Read<Array>(Read<object>(target, "rows"), "arena"));
                Assert.Equal(0, Read<int>(Read<object>(target, "rows"), "used"));
                Assert.True(Get(Read<object>(target, "rows"), 1) is not { } kept || kept.Equals(Activator.CreateInstance(kept.GetType())), $"{field.Name} keeps a row.");
            }
            else
            {
                Assert.Same(Read<Array>(source, "arena"), Read<Array>(target, "spare"));
                Assert.Equal(rows[field], Get(target, 33));
                Assert.All(Read<Array>(target, "spare").Cast<object?>(), static row => Assert.True(row is null || row.Equals(Activator.CreateInstance(row.GetType()))));
            }
        }

        static object? Get(object column, int id) => column.GetType().GetMethod("Get", Members)!.Invoke(column, [id]);
    }

    // G949 D2b-d §1.3 (DECISIONS BU-220; N5-5's n14, one row per counter): a stamped column, the lowest layer, holds its rows while its
    // stamp is the value of the counter it follows. A Bind advances the pass counter only, so a pass column reads stale while an edit
    // column holds; an edit (Invalidate) advances the edit counter, and after a finished pass the pass counter too. A renumbering hands the
    // stale column over unmoved; its first write empties it, keeping its capacity, and stamps it; a later write under the same counter
    // empties nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStampedColumnHoldsUntilItsCounterAdvances(bool pass)
    {
        var c = CompilationTestHelper.Parse(string.Empty);
        Assert.True(c.Bind().IsComplete);
        if (pass)
        {
            Holds<Binding.PassCounter, Binding.EditCounter>(c, () => c.Bind(), 1);
        }
        else
        {
            Holds<Binding.EditCounter, Binding.PassCounter>(c, () => c.NoteSyntaxEdit(c.Kotonoha.RootKoto), 0);
        }
    }

    // PLAN G949 S1-r2: a step reads its pass's rows (In), the family's rows over every module restricted to the pass's modules, in d8's
    // order: over a set of the source modules, the program's symbols; over the library alone, the library's; the two in sequence are every
    // module's (the accessor's Values, for readers outside a pass).
    [Fact]
    public void AFamilysRowsInAPassAreItsModulesRows()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var every = c.Binding.Semantics.Declarations;
        var sources = Rows(every.In(new(c.SourceModules, null, new())));
        var library = Rows(every.In(new([], c.Binding.Library.Kotonoha, new())));
        Assert.NotEmpty(sources);
        Assert.NotEmpty(library);
        Assert.All(sources, symbol => Assert.False(symbol.Declaration.Kotonoha.Syntax.IsLibrary));
        Assert.All(library, symbol => Assert.True(symbol.Declaration.Kotonoha.Syntax.IsLibrary));
        Assert.Equal(Rows(every), [.. sources, .. library]);

        static List<BindingSymbol> Rows(Binding.KeptValues<BindingSymbol, BindingSymbol?> values)
        {
            var rows = new List<BindingSymbol>();
            foreach (var symbol in values)
            {
                rows.Add(symbol);
            }

            return rows;
        }
    }

    // G949 D2b-d's d10, per kept family: an edit's renumbering moves a kept family's rows into the column's spare arena, so the second
    // of two edits allocates no arena: the column's arena is again the one it had before the first, and its first row is the same object
    // at its node's id, which a synthesized node's renumbering may change (Symbols: a declaration's symbol; Receivers: Point's implicit
    // constructor's `self`; EnumConstructions: `.Some(1)`'s plan; CopyDeclarations: a `Self is Copy` clause's declaration; CopyByType:
    // its container's list; OverrideEntries: a Type's implementations by original; SpecializationStorage: Milestone11's specialization
    // entries, reused by every Bind; KnownBases: j4's known base, which the Binds after its first start from; Refusals: the refusal a
    // losing candidate's request past the round bound holds, unsettled, in a valid program, G881b's xovm; GatheredGroups: a call's
    // group gathered from two aliases' groups, the same list refilled at each Bind, R7-N5 B1b-1). The column is read by
    // reflection, since the families' rows differ. FormationCauses (R7-N5 B3-2) has no row: its rows are a failing program's and each
    // Final check's start empties them, so none survives an edit; d7 (EveryNodeFamilyIsResizedRenumberedAndMoved) covers its moves, and
    // PairAnnotationBindingTest.AnEditThatFormsAFieldLeavesItsReadNoFormationCause fails without that `Empty()`. IndexedPatterns (R7-N5
    // B1b-1) has none either: this theory compares a row by reference, and its row is a `(bool, bool)` value; d7 covers its moves, and
    // SyntaxEditInvalidationTest.AnEditedRoleLeavesNoStalePatternRow fails without ResetMatches' `Empty()`.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Symbols", null)]
    [InlineData("Receivers", null)]
    [InlineData("EnumConstructions", null)]
    [InlineData("CopyDeclarations", CopyPoint)]
    [InlineData("CopyByType", CopyPoint)]
    [InlineData("OverrideEntries", "open struct Base<T>\n    public func ordinary(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self, other: objref/Leaf<T>) -> i32 => 1\nstruct Leaf<T> : Base<T>\n    override func read(self: objref/Self, other: objref/Leaf<T>) -> i32\n        return self.ordinary()\n()")]
    [InlineData("SpecializationsByOriginal", "milestone:Milestone11")]
    [InlineData("SpecializationStorage", "milestone:Milestone11")]
    [InlineData("KnownBases", BaseInputFormationBindingTest.J4)]
    [InlineData("OriginDeclarations", "milestone:Milestone13")]
    [InlineData("DiscoveredOrigins", "milestone:Milestone13")]
    [InlineData("OriginRequirementNodes", "milestone:Milestone10")]
    [InlineData("InitializerOrigins", "milestone:Milestone13")]
    [InlineData("PropertyCalls", "milestone:Milestone26")]
    [InlineData("StorageProjections", "milestone:Milestone26")]
    [InlineData("PropertyUpdateStorage", "struct HY<S>\n    public var x: S\n        set(value: S) -> ()\n            storage = value@move\n    public init(x: S) => self.x = x@move\npublic func main()\n    var h = HY<i32>.init(x: 3)\n    h.x += 4\n")]
    [InlineData("PropertyUpdateInputs", "struct HY<S>\n    public var x: S\n        set(value: S) -> ()\n            storage = value@move\n    public init(x: S) => self.x = x@move\npublic func main()\n    var h = HY<i32>.init(x: 3)\n    h.x += 4\n")]
    [InlineData("IndexerCalls", "milestone:Milestone27")]
    [InlineData("ViewRangeCalls", "var values: [2 of i32] = [1, 2]\nvar view = values.sliceUniq()\nlet part = view[1..]\nrequire part.length == 1 else => $abort(\"part\")\n")]
    [InlineData("RangeCalls", "milestone:Milestone23")]
    [InlineData("ResolvedKeys", "milestone:Milestone27")]
    [InlineData("MatchPlans", "milestone:Milestone10")]
    [InlineData("Refusals", "contract Inner\ncontract Pq\n    associate Next is Pq\n    associate Up\n    Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up\nfunc pick<X>(x: ref/X) -> () => ()\nfunc pick<X>(x: ref/X, tag: i32 = 0) -> ()\n    X is Inner\n    return\nfunc f<T>(x: ref/T.(Pq).Up)\n    T is Pq\n    pick(x)\npublic func main() => ()\n")]
    [InlineData("GatheredGroups", "alias A\nalias B\ngroup A\n    public func pick(value: bool) -> i32 => 1\ngroup B\n    public func pick(value: i32) -> i32 => value\nlet n = pick(7)\n")]
    public void AKeptFamilyAlternatesItsArenasAcrossEdits(string family, string? program)
    {
        var c = MinimalEmissionTest.Analyze(program is null ? Source
            : program.StartsWith("milestone:", StringComparison.Ordinal) ? Repository.ReadText("tests", "milestones", program["milestone:".Length..] + ".kimi") : program);
        var field = typeof(NodeColumns).GetField(family, Members)!;
        var rows = Column().GetType().GetMethod("Enumerate", Members)!.Invoke(Column(), null)!;
        Assert.True((bool)rows.GetType().GetMethod("MoveNext")!.Invoke(rows, null)!, $"{family} holds no row of the program.");
        var node = c.Kotonoha.Syntax.Nodes[(int)rows.GetType().GetProperty("Id", Members)!.GetValue(rows)!]!;
        var row = rows.GetType().GetProperty("Current")!.GetValue(rows);
        var before = Read<Array>(Column(), "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(Column(), "arena"));
        Assert.Same(row, Column().GetType().GetMethod("Get", Members)!.Invoke(Column(), [node.SyntaxId]));

        object Column() => field.GetValue(c.Kotonoha.Syntax.Hir.Families)!;
    }

    // G949 D2b-d's d10, per pass or edit family: an edit's renumbering hands the family's column over, stale, so the Bind after each
    // edit empties it at its first write and rewrites its rows in the same arena (AssociatedOrigins: an associated Type's Origin
    // parameters; ShapeContractHeads: the shape contracts of an overload group whose every pair shares a slot; ObjectErasures: an
    // override's call through its receiver's erased view, VirtualEntryErasureTest's program; InheritedOriginTypes: Milestone11's omitted
    // inherited Origins; DeferredConstraints: Milestone10's clause decisions; Matches: Milestone10's match plans; Adaptations: Milestone10's
    // implicit adaptations; VirtualOverrides: that program's override; AliasTargets and ImportedContainers: an unnamed alias of a
    // generic container's group whose struct a parameter names; FailingSteps: a path whose qualifier is missing (N1-13 B1); QualificationFailures: a member read without its `self.`, a failure
    // family's program, which reports its failure at each Bind; R7-N5's: PerCallOrigins, a call only an argument's own per-call Origin
    // would satisfy; PairFollows, a `@follow` of a pair layer;
    // ImplicitPairFollows, Milestone28's implicit follows; PhantomRawOrigins, a raw layer's Phantom slot; InvalidStaticOriginSlots,
    // `static` at a `uniq` slot; EffectBoundRejections, a misplaced effect item; InferredArrayLengths, a length hole's inferred length; ArrayInferenceShapes, an empty literal
    // under a holed annotation; BaseCallFailures, a `base` reference that calls nothing;
    // ObjectErasureFailures, VirtualEntryErasureTest's program calling through another object, which has no Owned proof; B2a-2's
    // ReceiverOperations, a stored Field read and a method call, and ResultContexts, an `if`'s result; B3-1's CallableConstraints, a
    // callee needing `static` under an unproven Callable bound (CallableConstraintRecordTest); B2b-1's UniqIndexing, a Dictionary's
    // user index; B3-2's RequirementGroups, a requirement call through a Type parameter;
    // B1b-2's MemberSelections, a stored Field read and a method call, InferredConstructionTargets, a generic struct's construction
    // without its Type arguments, and QualificationStops, an instance member named without `self`; D-986's FormReads, the kept operands
    // `s == missing`'s Error form reads (HirFormsTest's row), a failure family's program; B6b's query memos written in a Bind: the
    // result checks' of a while loop's condition, the publisher's and the lint walk's of an `if`'s
    // result; the guess rule's is written only after a Bind, by the lint report and MIR:
    // CurrentControlFlowTest.AGuessQueryAskedAgainAnswersTheSameNodes; D-932's
    // ReachedThroughBorrow, a member read below an owned Array's element; B5a's EarlyReads, j4's reads of a projected base not ready;
    // B2b-2's ResolvedSlices, a slice selected by a range value; R7-N5 E's Prerequisites, the consultation range of `s == missing`,
    // which read the missed Name; PartPrerequisites, a struct resting on its rejected attribute; PrerequisiteVisits, the walk of a
    // clause whose subject failed, which IsDerived takes in the Bind; DuplicateDeclarations, a repeated `let`; B1b-1's ResolvingTypes,
    // a written Type's BindType, whose rows are reset as it returns, so the column holds carved pages, not rows; SynthesizedCalls,
    // Milestone23's range calls).
    // Checked after each edit (N5-5's review C1):
    // a kept family's two arenas alternate, so after an even number of edits a kept column's arena is again its first.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("AssociatedOrigins", "contract C\n    associate Item(a) for ref/i32 during a\n")]
    [InlineData("ShapeContractHeads", "candidates")]
    [InlineData("ObjectErasures", "open struct Base<T>\n    public func ordinary(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self, other: objref/Leaf<T>) -> i32 => 1\nstruct Leaf<T> : Base<T>\n    override func read(self: objref/Self, other: objref/Leaf<T>) -> i32\n        return self.ordinary()\n()")]
    [InlineData("InheritedOriginTypes", "milestone:Milestone11")]
    [InlineData("DeferredConstraints", "milestone:Milestone10")]
    [InlineData("Matches", "milestone:Milestone10")]
    [InlineData("Adaptations", "milestone:Milestone10")]
    [InlineData("VirtualOverrides", "open struct Base<T>\n    public func ordinary(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self, other: objref/Leaf<T>) -> i32 => 1\nstruct Leaf<T> : Base<T>\n    override func read(self: objref/Self, other: objref/Leaf<T>) -> i32\n        return self.ordinary()\n()")]
    [InlineData("Specializations", "milestone:Milestone11")]
    [InlineData("ImplicitConstructorDecisions", "open struct Base\n    public init() => ()\nstruct Derived : Base\n    public var y: i32 = 2\nlet d = Derived.init()\n")]
    [InlineData("VirtualEffectBounds", "milestone:Milestone44")]
    [InlineData("AliasTargets", "alias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: Tag) => ()\n")]
    [InlineData("ImportedContainers", "alias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: Tag) => ()\n")]
    [InlineData("FailingSteps", "func f(x: Missing.Inner) => ()\n", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("QualificationFailures", "struct Item\n    var count: i32 = 1\n    func read(self: ref/Self) -> i32 => count\n", DiagnosticCode.QualificationRequired_Kd)]
    [InlineData("PerCallOrigins", "func pick(n: ref/i32) -> ref/i32 => n\nfunc make<T>(action: (ref/i32) -> T) -> i32 => 7\nlet m = make(pick)\n", DiagnosticCode.MissingOriginBinding_Kd)]
    [InlineData("PairFollows", "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\nfunc view<s/T>(box: ref/Box<s/T>) -> ref/T during box\n    s is value or valueborrow\n    return box.item@follow@ref\n")]
    [InlineData("ImplicitPairFollows", "milestone:Milestone28")]
    [InlineData("PhantomRawOrigins", "struct Holder<s/T> {a}\n    s is owner or uniq\n    public let item: s/T during a\n    let p: raw/i32 during a\n", DiagnosticCode.InvalidOriginBinding_Kd)]
    [InlineData("InvalidStaticOriginSlots", "struct Holder<s/T> {a}\n    s is owner or uniq\n    public let item: s/T during a\nfunc g(r: uniq/i32) -> Holder<uniq/i32 during r> during static => $abort(\"u\")\n", DiagnosticCode.InvalidOriginBinding_Kd)]
    [InlineData("EffectBoundRejections", "enum E\n    A\n    effect confined\n    B\n", DiagnosticCode.InvalidEffectBound_Kd)]
    [InlineData("ReceiverOperations", "struct S\n    public let value: i32 = 7\n    public init() => ()\n    public func get(self: ref/Self) -> i32 => self.value\nfunc f(s: ref/S) -> i32 => s.value + s.get()\n")]
    [InlineData("InferredArrayLengths", "let values: [_ of i64] = [1, 2, 3]\n")]
    [InlineData("ArrayInferenceShapes", "let values: [_ of i32] = []\n")]
    [InlineData("BaseCallFailures", "open struct Base\n    public let value: i32 = 7\n    public func read(self: ref/Self) -> i32 => 1\nstruct Derived : Base\n    func f(self: ref/Self) -> i32 => base()\n()", DiagnosticCode.InvalidBaseCall_Kd)]
    [InlineData("ObjectErasureFailures", "open struct Base<T>\n    public func ordinary(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self, other: objref/Leaf<T>) -> i32 => 1\nstruct Leaf<T> : Base<T>\n    override func read(self: objref/Self, other: objref/Leaf<T>) -> i32\n        return other.ordinary()\n()", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("ReachedThroughBorrow", "struct S1\n    internal var x: Array<i32>\nstruct S2\n    internal var x: Array<S1>\nfunc f(s: S2) -> i32 => s.x[0].x[0]\n")]
    [InlineData("ResultContexts", "func f(b: bool) -> i32\n    let x = if b => 1 else => 2\n    return x\n")]
    [InlineData("CallableConstraints", "func apply<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> i32>\n    return f(x)\nfunc pick(x: ref/i32 during static) -> i32 => x@follow\npublic func main() -> ()\n    let m = 1\n    let v = apply(pick, m@ref)\n", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData("UniqIndexing", "let d: Dictionary<i32, i32> = [1: 2]\nlet x = d[1]\n")]
    [InlineData("RequirementGroups", "contract Reader\n    func read(self: ref/Self) -> i32\nfunc f<T>(x: ref/T) -> i32\n    T is Reader\n    return x.read()\n")]
    [InlineData("MemberSelections", "struct S\n    public let value: i32 = 7\n    public init() => ()\n    public func get(self: ref/Self) -> i32 => self.value\nfunc f(s: ref/S) -> i32 => s.value + s.get()\n")]
    [InlineData("InferredConstructionTargets", "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\nlet b = Box.init(1)\n")]
    [InlineData("QualificationStops", "struct Item\n    var count: i32 = 1\n    func read(self: ref/Self) -> i32 => count\n", DiagnosticCode.QualificationRequired_Kd)]
    [InlineData("FormReads", "func f(s: string) -> bool\n    return s == missing\n", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("Prerequisites", "func f(s: string) -> bool\n    return s == missing\n", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("PartPrerequisites", "#Layout(\"bad\")\nstruct S\n", DiagnosticCode.InvalidLayoutAttribute_Kd)]
    [InlineData("PrerequisiteVisits", "func f<U>(value: U)\n    value is D\n    U is Copy\n    ()\n", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("DuplicateDeclarations", "let x = 1\nlet x = 2\n", DiagnosticCode.DuplicateBinding_Kd)]
    [InlineData("ResultCompletions", "func f(n: i32) -> i32\n    var s = 0\n    while s < n => s = s + 1\n    return s\n")]
    [InlineData("PublishedCompletions", "func f(b: bool) -> i32\n    let x = if b => 1 else => 2\n    return x\n")]
    [InlineData("LintCompletions", "func f(b: bool) -> i32\n    let x = if b => 1 else => 2\n    return x\n")]
    [InlineData("EarlyReads", BaseInputFormationBindingTest.J4)]
    [InlineData("ResolvedSlices", "let values: [4 of i32] = [1, 2, 3, 4]\nlet inner = 1..^1\nlet part = values[inner]\n")]
    [InlineData("ResolvingTypes", "let x: i32 = 1\n")]
    [InlineData("SynthesizedCalls", "milestone:Milestone23")]
    public void AStampedFamilyKeepsItsArenaAcrossEdits(string family, string program, DiagnosticCode? reported = null)
    {
        var source = program == "candidates" ? ConstructorInferenceWorkloads.Create(program, 4)
            : program.StartsWith("milestone:", StringComparison.Ordinal) ? Repository.ReadText("tests", "milestones", program["milestone:".Length..] + ".kimi") : program;
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        Assert.True(Bound(), MinimalEmissionTest.Describe(c));
        var field = typeof(NodeColumns).GetField(family, Members)!;
        var (before, used) = (Arena(), Used());
        Assert.NotNull(before);
        Assert.NotEqual(0, used);
        for (var i = 0; i < 2; i++)
        {
            c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
            Assert.True(Bound());
            Assert.Same(before, Arena());
            Assert.Equal(used, Used());
        }

        // A valid program's Bind is complete; a failure family's program reports its failure at each Bind.
        bool Bound() => c.Bind() is var result && (reported is { } code ? c.Binding.Issues.Any(x => x.Code == code) : result.IsComplete);

        Array? Arena() => Read<Array?>(Read<object>(field.GetValue(c.Kotonoha.Syntax.Hir.Families)!, "rows"), "arena");
        int Used() => (int)field.FieldType.GetProperty("Used", Members)!.GetValue(field.GetValue(c.Kotonoha.Syntax.Hir.Families))!;
    }

    // G949 N5-5's n15: an owner-keyed Origin interner's row (originAtoms, openOrigins) keeps its owner's atoms in key order, so a search
    // probes about log N of its N atoms. A function of N reference parameters (N Input atoms on its row) and a call of it with N
    // reference arguments (N open regions on the call's): a warm Bind's probes as N doubles from 128 to 512 grow within factor 3, where a
    // scan of the rows (N squared) takes 4 and N log N about 2.2.
    [Fact]
    public void AnOriginInternerRowIsSearchedInLogarithmicProbes()
    {
        long[] probes = [.. new[] { 128, 256, 512 }.Select(Probes)];
        Assert.True(MirBorrowsTest.Grows(probes, 3), $"probes {string.Join(", ", probes)}");

        static long Probes(int count)
        {
            var c = CompilationTestHelper.Parse(
                $"func f({string.Join(", ", Enumerable.Range(0, count).Select(static i => $"p{i}: ref/i32"))}) -> i32 => p0@follow\n" +
                $"func g() -> i32\n    let n: i32 = 1\n    return f({string.Join(", ", Enumerable.Range(0, count).Select(static _ => "n@ref"))})\n",
                "Hello.kimi");
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            var before = c.Binding.OriginRowWork;
            Assert.True(c.Bind().IsComplete);
            return c.Binding.OriginRowWork - before;
        }
    }

    // G949 D2b-d's d10 for `associatedInference` (PLAN G949 S1-b2 commit 4): a conforming Type's inference batch is kept at its Type's
    // declaration (SPEC 8.6.1), so an edit's renumbering moves it with its node: after two edits it is the same batch at the declaration
    // and the column's arena is again its first. S's `X` is inferred from `x`'s result (SPEC 8.3), so S's conformance has a batch.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnInferenceBatchMovesWithItsTypeAcrossEdits()
    {
        var c = MinimalEmissionTest.Analyze("contract K\n    associate X\n    func x(self: ref/Self) -> Self.X\nstruct S\n    Self is K\n    public func x(self: ref/Self) -> i32 => 0\n()");
        Assert.True(c.Binding.Result.IsComplete);
        var s = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single();
        var batch = c.Kotonoha.Syntax.Hir.Families.AssociatedInference.Get(s.SyntaxId);
        Assert.NotNull(batch);
        var before = Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedInference, "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedInference, "arena"));
        Assert.Same(batch, c.Kotonoha.Syntax.Hir.Families.AssociatedInference.Get(s.SyntaxId));
    }

    private static bool IsKept(FieldInfo field) => field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(PagedColumn<>);

    private static bool IsStamped(FieldInfo field) => field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(StampedColumn<,>);

    // N5-5's n14 for one counter: a column following TCounter and one following TOther, each with a row, then the advance.
    private static void Holds<TCounter, TOther>(Compilation c, Action advance, int otherAfter)
        where TCounter : IStampCounter
        where TOther : IStampCounter
    {
        var (column, other) = (default(StampedColumn<int, TCounter>), default(StampedColumn<int, TOther>));
        column.Grow(40);
        other.Grow(40);
        column.Ref(5, c.Binding) = 1;
        other.Ref(5, c.Binding) = 1;
        Assert.Equal(1, column.Get(5, c.Binding));
        advance();
        Assert.Equal(0, column.Get(5, c.Binding));
        Assert.Equal(otherAfter, other.Get(5, c.Binding));

        var renumbered = column.Renumbered(48);
        Assert.Equal(0, renumbered.Get(5, c.Binding));
        Assert.Equal(0, renumbered.Used);
        Assert.Same(Arena(column), Arena(renumbered));
        renumbered.Ref(44, c.Binding) = 2;
        Assert.Equal(PagedColumn<int>.PageSize, renumbered.Used);
        Assert.Equal(0, renumbered.Get(5, c.Binding));
        Assert.Same(Arena(column), Arena(renumbered));
        renumbered.Ref(6, c.Binding) = 3;
        Assert.Equal(2, renumbered.Get(44, c.Binding));

        static Array Arena(StampedColumn<int, TCounter> x) => Read<Array>(Read<object>(x, "rows"), "arena");
    }

    private static object NonDefault(Type type)
    {
        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 1);
        }

        if (!type.IsValueType)
        {
            // An abstract field (a Koto, as PerCallOriginFact's Argument) takes an instance of a concrete derived type.
            return RuntimeHelpers.GetUninitializedObject(type.IsAbstract ? type.Assembly.GetTypes().First(x => !x.IsAbstract && type.IsAssignableFrom(x)) : type);
        }

        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return NonDefault(inner);
        }

        if (type.IsPrimitive)
        {
            return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        var row = Activator.CreateInstance(type)!;
        var first = type.GetFields(Members)[0];
        first.SetValue(row, NonDefault(first.FieldType));
        return row;
    }

    private static T Read<T>(object column, string name) => (T)column.GetType().GetField(name, Members)!.GetValue(column)!;
}
