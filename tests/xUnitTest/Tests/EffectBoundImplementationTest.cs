// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.11.3: a conformance checks each witness against the bounds its Contract and the ancestors declare, from
// the declarations alone; user Contracts bound their requirements exactly as the standard ones do.
public partial class EffectBoundImplementationTest
{
    // Row 33's Board (SPEC 22.4.5, the Tatara plan's decision 45): the Kernel's complete Board whose `write` reads the immutable static
    // Screen.width; MirAnalysisTest.Requirements records the rule with it.
    internal static readonly string ScreenBoard = StartupBindingTest.Derive(
        KernelSources.Board + KernelSources.BoardTerminate, (Cells, "(self.cell / Screen.width + 1) * Screen.width")) + "group Screen\n    public let width: isize = 80\n";

    private const string StableSources = "contract Source\n    func take(self: uniq/Self) -> i32\ncontract StableSource: Source\n    effect Source.take confined\n";

    private const string TakingCursor = "struct Cursor<T>\n    T is C\n    Self is Step\n    var inner: T\n    public func step(self: uniq/Self)\n        var item = self.inner.get()\n        _ = item.take()\n";

    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n" +
        "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Step = "contract Step\n    func step(self: uniq/Self)\n        effect confined\n";

    private const string Box = "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n" +
        "    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self\n        Console.writeLine(\"step\")\n        return self.value@uniq\n";

    private const string Cleanup = "struct Cleanup {step}\n    let cursor: uniq/Cursor during step\n    public init(cursor: uniq/Cursor during step) => self.cursor = cursor@move\n    ";

    private const string Stepped = "struct Cursor\n    Self is Step\n    public var count: i32 = 0\n    public func step(self: uniq/Self)\n        do\n            let cleanup = Cleanup.init(self)\n";

    private const string Main = "public func main() => ()\n";

    // Rows 31-36 and C-586's twins: the Kernel (KernelSources' Hello and complete Board in one source), the texts they replace in it
    // (Hello's line, `write`'s first line, `clock`'s body and the line feed's cell) and A4's records at the Board's members.
    private const string Kernel = KernelSources.Hello + KernelSources.Board + KernelSources.BoardTerminate;
    private const string HelloLine = "Console.writeLine(\"Hello\")\n";
    private const string Bytes = "        let bytes = text.bytes()\n";
    private const string Ticks = "        self.ticks += 1\n        return self.ticks\n";
    private const string Cells = "(self.cell / 80 + 1) * 80";
    private const string Write = "Ownership.PlatformStatic UnsatisfiedEffectBound_Kd public func write(self: uniq/Self, stream: i32, text: Text.Utf8Slice) -> i64";
    private const string Clock = "Ownership.PlatformStatic UnsatisfiedEffectBound_Kd public func clock(self: uniq/Self) -> i64";
    private const string Start = "Ownership.PlatformStatic UnsatisfiedEffectBound_Kd public func start(boot: Boot) -> Self";
    private const string CountedRecords = "Binding Unsupported_Kd Metrics.writes@ref | Collection Unsupported_Kd public var writes: i64 = 0";

    // ASuppliedContractsBoundIsReadAtEveryDepth's conformances: the call's conforming Type is a requirement's result, or a Type an
    // identity premise makes one with a projection at depth 2.
    private const string DeepMeter = "struct Meter<T>\n    T is Node\n    Self is Report\n    var reading: T\n    public func report(self: ref/Self) -> i32\n        return self.reading.deep().ping()\n";

    private const string IdentityMeter = "struct Meter<T>\n    T is Node\n    T.(Node).Other is T.(Node).Next.(Node).Item\n    Self is Report\n    var reading: T.(Node).Other\n" +
        "    public func report(self: ref/Self) -> i32\n        return self.reading.ping()\n";

    // P42's StaticStorage record at Sink's settable static, Metrics.puts (DECISIONS R5-M82; STATUS Static members): a MIR row of every
    // program with Sink.
    private static readonly (DiagnosticCode Code, SyntaxLocation At) PutsStorage =
        (DiagnosticCode.Unsupported_Kd, MirTargetSide.At(Sink, "var puts: isize = 0"));

    // The freestanding row, whose program supplies the platform (TargetProfile.ProgramPlatform).
    private static readonly TargetProfile Freestanding = TargetProfile.Find("x86_64-unknown-none")!;

    [Theory]
    [InlineData("struct CountingSink\n    Self is Sink\n    var count: isize = 0\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        self.count += 1\n        return .Ok(())\n", true)]
    [InlineData("struct Forward<W>\n    W is Sink\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", true)]
    [InlineData("struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", false)]
    [InlineData("struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", false)]
    [InlineData("contract Plain\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\nstruct Loose<W>\n    W is Plain\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", false)]
    public void ConfinedRejectsEnvironmentEffects(string declarations, bool valid)
        => AssertBound(MinimalEmissionTest.Analyze(Sink + declarations + Main), valid, "Self is Sink", PutsStorage);

    // SPEC 8.11.2: an environment effect obtains authority over mutable state from the environment; accesses made with
    // authority from the inputs are none, even through a raw pointer or a borrow of static storage.
    [Theory]
    [InlineData("struct RawSink\n    Self is Sink\n    var slot: raw/i32\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            *self.slot = value\n        return .Ok(())\n", true)]
    [InlineData("struct DeviceSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        unsafe\n            let register = 0x40000000@raw/i32\n            *register = value\n        return .Ok(())\n", false)]
    [InlineData("group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public unsafe func query(value: raw/i64) -> i32\nstruct ForeignSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        let output: raw/i64 = null\n        unsafe => _ = Native.query(output)\n        return .Ok(())\n", false)]
    [InlineData("struct Counter\n    var count: isize = 0\n    public func increment(self: uniq/Self) => self.count += 1\ncontract Tally\n    func add(self: ref/Self, total: uniq/Counter)\n        effect confined\nstruct Adder\n    Self is Tally\n    public func add(self: ref/Self, total: uniq/Counter)\n        total.increment()\n", true)]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> Array<i32>\n        effect confined\nstruct M\n    Self is Maker\n    var n: i32 = 4\n    public func make(self: ref/Self) -> Array<i32>\n        var result = Array<i32>.init(capacity: 2)\n        result.appendCopies([self.n, self.n][..])\n        return result@move\n", true)]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> Array<i32>\n        effect confined\nstruct M\n    Self is Maker\n    var n: i32 = 4\n    public func make(self: ref/Self) -> Array<i32>\n        let values: [2 of i32] = [self.n, self.n]\n        return values.slice().toArray()\n", true)]
    [InlineData("contract Filler\n    func fill(self: ref/Self, values: uniq/Array<i32>)\n        effect confined\nstruct F\n    Self is Filler\n    public func fill(self: ref/Self, values: uniq/Array<i32>)\n        var view = values.sliceUniq()\n        view[0] = 7\n", true)]
    public void ConfinedClassifiesAuthorityByItsSource(string declarations, bool valid)
        => AssertBound(MinimalEmissionTest.Analyze(Sink + declarations + Main), valid, "Self is Sink", PutsStorage);

    // SPEC 8.11.2: reading an immutable static of a Transferable Type (`i32`) is no environment effect; its initializer (`2 + 3`) enters
    // the summary and has none, so the confined witness keeps its bound. Binding refused the initializer's destructions with one
    // Unsupported_Kd at `Self is Read`; since the MIR decides acceptance (H5, DECISIONS R5-M185) A4 judges the bound (1a, the owner
    // 2026-11-03): check publishes no record, and a Library build (every root, K11) is accepted and generates S.read.
    [Fact]
    public void AnImmutableStaticsComputedInitializerIsNoEnvironmentEffect()
    {
        const string Source = "contract Read\n    func read(self: ref/Self) -> i32\n        effect confined\ngroup G\n    public let value: i32 = 2 + 3\n" +
            "struct S\n    Self is Read\n    public func read(self: ref/Self) -> i32 => G.value\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Contains("read", MinimalEmissionTest.GeneratedBodies(Source, OutputKind.Library, accepted: true));
    }

    // SPEC 8.11.3, 23.1.2 (DECISIONS BU-38): a witness reaching storage that rests on a part Binding rejected (Bad's Field m) reads it as
    // instantiated, so the program has the records its repair has, beside Binding's own. Since the MIR decides acceptance (H5, DECISIONS
    // R5-M185), these are check's records (R6-check H3b T-12):
    // - none through a forwarded call, a forwarded Type argument, a dictionary, a requirement, formatting, or a comparison through a
    //   Contract that bounds equals as confined (QuietEq);
    // - A4's IncompatibleContractImplementation_Kd at `Self is Reset` through a forwarded environment effect, and through `same<T>`'s
    //   comparison: a requirement call on an abstract Self uses the bounds of the Contracts its premises prove, never an
    //   implementation's (SPEC 8.11.3; A4's X2, DECISIONS R4-M22), and Equatable bounds no effect of equals;
    // - for storage nested past the implementation's height (Box65), the limits' records at the call (SPEC 23.1.1; PLAN G128), the
    //   conformance valid (SPEC 8.11.2): A5's OwnershipDepthLimit_Kd (R4-M153) and the instance collector's GenerationResourceLimit_Kd
    //   (Layout's); as a Type argument alone (deepArgument), which no walk meets, it is an instance (R7-T1: the collector reads no
    //   storage verdict; before, its Unsupported_Kd at the call).
    [Theory]
    [InlineData("forward", "")]
    [InlineData("environment", "IncompatibleContractImplementation_Kd @ Self is Reset")]
    [InlineData("argument", "")]
    [InlineData("dictionary", "")]
    [InlineData("requirement", "")]
    [InlineData("format", "")]
    [InlineData("equals", "IncompatibleContractImplementation_Kd @ Self is Reset")]
    [InlineData("quiet", "")]
    [InlineData("pair", "OwnershipDepthLimit_Kd @ U.pair(self.item@ref, self.big@ref); GenerationResourceLimit_Kd @ self.item@ref")]
    [InlineData("deep", "OwnershipDepthLimit_Kd @ U.wrap(self.item@ref); GenerationResourceLimit_Kd @ self.item@ref")]
    [InlineData("deepArgument", "")]
    public void StorageBindingRejectedKeepsItsRepairsRecords(string form, string repaired)
    {
        Assert.Equal(repaired, string.Join("; ", Issues("i32")));
        Assert.Equal(Issues("i32").Append("UnresolvedBinding_Kd @ Missing").Order(), Issues("Missing").Order());

        // Binding's records, then check's (the MIR's and the collector's), each as its code and its subject's first line.
        string[] Issues(string field)
        {
            var c = MinimalEmissionTest.Analyze(HolderSource(form, field));
            return [.. c.Binding.Issues.Select(static x => $"{x.Code} @ {First(x.Node)}"),
                .. c.Problems.Select(x => $"{x.Code} @ {First(c.SyntaxOf(x.Subject))}")];
        }
    }

    // R7-T1 (B's ruling 14, labelled V: SPEC 8.11.2, 8.13.3; IMPL 21.3.1; the Middle's RP17): the deepArgument form, Box^65<i32> as a
    // Type argument alone through a forwarded call, which no walk meets, is generated and runs.
    [Fact]
    public void ATypeArgumentPastBindingsBoundExecutes() => ScalarEmissionTest.EmitFixture("EffectBoundDeepArgument", HolderSource("deepArgument", "i32"), string.Empty);

    // SPEC 8.11.2: every function the compiler serves has one effect class, Binding's category of it (TyCtxt.Bodyless) and, for an
    // intrinsic, MIR's row of it (MirDescriptors; MirEffects reads the two). Every library function or $abort that is no body is an
    // intrinsic, and of those Console output and the test temporary directory alone are environment effects (the Storage and Raw
    // families, the handles, Text.writer and the fixed failures act only on their inputs and the allocator), with, in the Linux row's
    // library, the platform layer's clock (Time.monotonicNanoseconds, the Tatara plan's B7b6; windows-x64's library, the selection of
    // the one without a target, declares none), and on every x86-64 row Kimi.X86's pause, readTimestamp and cpuid, machine instructions
    // (IMPL 21.6; OSF1a, design C3; OSF1b); a library body is no bodyless category.
    [Theory]
    [InlineData("", "WriteStdout,TestTempDirectory,Instruction,Instruction,Instruction")]
    [InlineData("x86_64-unknown-linux-musl", "WriteStdout,TestTempDirectory,MonotonicClock,Instruction,Instruction,Instruction")]
    public void EveryCompilerFunctionHasOneEffectClass(string target, string intrinsics)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(target.Length == 0 || compilation.Prepare(target));
        var library = compilation.Library;
        var environment = new List<IntrinsicId>();
        foreach (var symbol in library.RegisteredSymbols.ToArray().Append(library.Abort))
        {
            var kind = TyCtxt.Bodyless(symbol);
            Assert.True(
                symbol.Definition.Kind switch
                {
                    DispositionKind.Body => kind is not (BodylessKind.Intrinsic or BodylessKind.Shim or BodylessKind.Form),
                    DispositionKind.Form => kind == BodylessKind.Form, // Storage.viewStart (L2): the builder writes its MIR form.
                    _ => kind == BodylessKind.Intrinsic,
                },
                symbol.Name);
            if (kind == BodylessKind.Intrinsic && (MirDescriptors.Of(symbol.Definition.Id).Effects & MirEffect.Environment) != 0)
            {
                environment.Add(symbol.Definition.Id);
            }
        }

        Assert.Equal(intrinsics.Split(',').Select(static x => Enum.Parse<IntrinsicId>(x)).Order(), environment.Order());
    }

    // SPEC 8.11.2: each of the eight built-in targets (Binding.PrepareBuiltinTargets: equality, comparison and the six arithmetic
    // operations) is a shim, which acts on its inputs (MirEffects.Class's Shim arm), and the tuple comparisons' calls target two of them.
    [Fact]
    public void EachBuiltinTargetIsAShim()
    {
        var c = MinimalEmissionTest.Analyze("let same = (1, 2) == (1, 2)\nlet less = (1, 2) < (3, 4)");
        var shims = KotoTree.Walk(c.Kotonoha.RootKoto)
            .SelectMany(x => Enumerable.Range(0, 8).Select(i => CallPlan.Of(c.Binding, x, i)).TakeWhile(static y => y is not null).OfType<CallPlan>())
            .Where(static x => x.Target.Definition.Kind == DispositionKind.Shim).Select(static x => x.Target).Distinct().ToArray();
        var contracts = shims.Select(static x => TyCtxt.Contract(x).LibraryDeclaration!.Value).ToArray();
        Assert.Equal(2, contracts.Length);
        Assert.Contains(KimiDeclarationId.Equatable, contracts);
        Assert.Contains(KimiDeclarationId.Comparable, contracts);
        var targets = new[] { (BindingSymbol?)Field("builtinEquals"), (BindingSymbol?)Field("builtinCompare") }
            .Concat((BindingSymbol?[])Field("builtinArithmetic")!).Select(static x => x!).ToArray();
        Assert.Subset(targets.ToHashSet(), shims.ToHashSet());
        Assert.Equal(
            new[] { KimiDeclarationId.Equatable, KimiDeclarationId.Comparable, KimiDeclarationId.Addable, KimiDeclarationId.Subtractable, KimiDeclarationId.Multipliable, KimiDeclarationId.Dividable, KimiDeclarationId.RemainderProvider, KimiDeclarationId.Negatable },
            targets.Select(static x => TyCtxt.Contract(x).LibraryDeclaration!.Value));
        Assert.All(targets, static x => Assert.Equal(BodylessKind.Shim, TyCtxt.Bodyless(x)));

        // A built-in target Binding prepares (Binding.PrepareBuiltinTargets), by its field.
        object? Field(string name) => typeof(Binding).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Binding);
    }

    // SPEC 8.11.3, 8.11.2, 15.6.4: confined is judged over every body the witness reaches, which its calls select: a helper, a recursive
    // generic helper, a called function's one summary per declaration over its whole closed specialization set, whatever its key (by Type,
    // length or a compound key, matching or not), the accessor or indexer a Place's use selects, and a destructor ownership analysis plans;
    // a member no call reaches is not judged. (These forms were preserves results
    // checks of Iterator conformances before the adopted proposal Abstract Calls Reach by Paths removed that bound.)
    [Theory]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self)\n        Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self)\n        self.count += 1\n    public func step(self: uniq/Self) => self.update()\n", true)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<T>(self: uniq/Self, depth: i32)\n        if depth > 0 => self.update<T>(depth - 1)\n        Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<i32>(2)\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<T>(self: uniq/Self, depth: i32)\n        if depth > 0 => self.update<T>(depth - 1)\n        self.count += 1\n    public func step(self: uniq/Self) => self.update<i32>(2)\n", true)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.count += 1\n", true)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<T>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => self.count += 1\n    public func step(self: uniq/Self) => self.update<T>()\n", true)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<bool>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<i32>()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<length M>(self: uniq/Self) => self.count += 1\n    specialize func update<3>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<2>()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<length M>(self: uniq/Self) => self.count += 1\n    specialize func update<3>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<3>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<(i32, bool)>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<(T, bool)>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<(i32, bool)>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<(T, i32)>()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n        set(value: i32) -> () => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.count = 1\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n        set(value: i32) -> () => storage = value\n    public func step(self: uniq/Self) => self.count = 1\n", true)]
    [InlineData(Box + "struct Cursor\n    Self is Step\n    var box: Box\n    public func step(self: uniq/Self) => self.box[0] += 1\n", false)]
    [InlineData(Box + "struct Cursor\n    Self is Step\n    var box: Box\n    public func step(self: uniq/Self) => _ = self.box[0]\n", true)]
    [InlineData(Cleanup + "drop => Console.writeLine(\"step\")\n" + Stepped, false)]
    [InlineData(Cleanup + "drop => self.cursor.count += 1\n" + Stepped, true)]
    public void ConfinedJudgesEveryReachedBody(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Step + declarations + Main);
        var rejected = c.Binding.Issues.Any(static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd) ||
            c.Problems.Any(static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        Assert.True(valid != rejected, MinimalEmissionTest.Describe(c));
        Assert.True(!valid || (c.Binding.Result.IsComplete && c.Analysis.IsVerified), MinimalEmissionTest.Describe(c));
    }

    // Editing a reached helper, generic helper or selected specialization revokes the conformance, and restoring it restores the
    // conformance and its generation, repeatedly on one compilation.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HelperEditRevokesAndRestoresTheConformance(int kind)
    {
        var source = Step + "struct Cursor\n    Self is Step\n    public var count: i32 = 0\n    public init() => ()\n    func update(self: uniq/Self) => self.count += 1\n" +
            "    public func step(self: uniq/Self) => self.update()\nvar cursor = Cursor.init()\ncursor.step()\nrequire cursor.count == 1 else => $abort(\"count\")\n";
        if (kind != 0)
        {
            source = source.Replace("func update(", "func update<T>(", StringComparison.Ordinal).Replace("self.update()", "self.update<i32>()", StringComparison.Ordinal);
        }

        if (kind == 2)
        {
            source = source.Replace("    public func step", "    specialize func update<i32>(self: uniq/Self) => self.count += 1\n    public func step", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.True(c.Generate(output), MinimalEmissionTest.Describe(c));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "update" && x.IsSpecialization == (kind == 2));
        var original = function.ExpressionBody!;
        var donor = ParseTestHelper.ParseSuccess(source.Replace("=> self.count += 1", "=> Console.writeLine(\"step\")", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "update" && x.IsSpecialization == (kind == 2)).ExpressionBody!;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(KotoHelper.Replace(function, original, replacement));
            output.GetStringBuilder().Clear();
            Assert.False(c.Generate(output));
            Assert.Empty(output.ToString());
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            c.Analyze();
            AssertBound(c, valid: false, "Self is Step");
            Assert.True(KotoHelper.Replace(function, replacement, original));
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            Assert.True(c.Generate(output), MinimalEmissionTest.Describe(c));
        }
    }

    // SPEC 8.11.1: the bound is the Contract's guarantee, so only Self is StableSource is checked against it.
    [Theory]
    [InlineData("Source", true)]
    [InlineData("StableSource", false)]
    public void OnlyTheDeclaringContractObligates(string contract, bool valid)
    {
        const string Declarations = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\ncontract StableSource: Source\n    effect Source.take confined\n";
        var c = MinimalEmissionTest.Analyze(Declarations + $"struct Noisy\n    Self is {contract}\n    associate Source.Item is i32\n    public func take(self: uniq/Self) -> Option<i32>\n        Console.writeLine(\"take\")\n        return .None\n" + Main);
        AssertBound(c, valid, "Self is " + contract);
    }

    // IMPL 21.6, SPEC 8.11.2 (OSF's row 26; design C3): a machine instruction is an environment effect, so a `confined` implementation
    // that executes one fails its bound on a hosted row and on the freestanding one; reading the register through its input is no such
    // effect (RawStorageTest.RawOperationsAreNoEnvironmentEffects, the twin).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "X86.pause()\n        unsafe => return Raw.readVolatile(self.p)")]
    [InlineData("x86_64-pc-windows-msvc", "return X86.readTimestamp()@u32")]
    [InlineData("x86_64-unknown-none", "X86.pause()\n        unsafe => return Raw.readVolatile(self.p)")]
    [InlineData("x86_64-unknown-none", "return X86.readTimestamp()@u32")]
    public void AnInstructionIsAnEnvironmentEffect(string triple, string body)
    {
        const string Probe = "contract Probe\n    func read(self: uniq/Self) -> u32\n        effect confined\n" +
            "struct Register\n    Self is Probe\n    internal let p: raw/u32\n    public init(p: raw/u32) => self.p = p\n    public func read(self: uniq/Self) -> u32\n        ";
        AssertBound(MinimalEmissionTest.Analyze(TargetProfile.Find(triple)!, Probe + body + "\n", "Hello.kimi", OutputKind.Library), false, "Self is Probe");
    }

    // Warm rebinding of user-declared bounds and their witness summaries allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmUserEffectBoundChecksDoNotAllocate()
    {
        const string Declarations = "struct Forward<W>\n    W is Sink\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n";
        var c = MinimalEmissionTest.Analyze(Sink + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 8.11.3, 8.3.2, 8.5.4: a Contract's bound reaches an associated Type that a premise makes conform, for every Origin argument of its family;
    // without the bound the conformance is rejected at its Self clause.
    [InlineData("C7", "g234", StableSources + "contract C\n    associate Item(s) is StableSource and Copy\n    func get(self: uniq/Self during s) -> Self.Item(s)\n        effect confined\n" + Step + TakingCursor, "", Explicit = true)]
    [InlineData("guard", "g234p", StableSources + "contract C\n    associate Item is StableSource and Copy\n    func get(self: uniq/Self) -> Self.Item\n        effect confined\n" + Step + TakingCursor, "")]
    [InlineData("guard", "g234n", StableSources + "contract C\n    associate Item(s) is Source and Copy\n    func get(self: uniq/Self during s) -> Self.Item(s)\n        effect confined\n" + Step + TakingCursor, "IncompatibleContractImplementation_Kd: Self is Step")]
    public void ABoundReachesAnAssociatedFamily(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-C2c2 c2-3b (SPEC 8.11.3, 8.3.2, 8.2.3): a requirement call uses the bounds of the Contracts its premises prove for the
    // conforming Type, at every depth and whatever its spelling: a Type that is a requirement's result (ebd) or stands behind an
    // identity premise (ebi). Where the supplied Contract declares no bound, the conformance is rejected at its Self clause.
    [Theory]
    [InlineData("ebd2", "Quiet", "    func deep(self: ref/Self) -> ref/Self.(Node).Next.(Node).Item\n        effect confined\n", DeepMeter, "")]
    [InlineData("ebd3", "Quiet", "    func deep(self: ref/Self) -> ref/Self.(Node).Next.(Node).Next.(Node).Item\n        effect confined\n", DeepMeter, "")]
    [InlineData("ebd2n", "Loud", "    func deep(self: ref/Self) -> ref/Self.(Node).Next.(Node).Item\n        effect confined\n", DeepMeter, "IncompatibleContractImplementation_Kd: Self is Report")]
    [InlineData("ebi2", "Quiet", "", IdentityMeter, "")]
    [InlineData("ebi2n", "Loud", "", IdentityMeter, "IncompatibleContractImplementation_Kd: Self is Report")]
    [InlineData("ebi4", "Quiet", "", "struct Meter<T, U>\n    T is Quiet\n    T is U\n    Self is Report\n    var reading: U\n    public func report(self: ref/Self) -> i32\n        return self.reading.ping()\n", "")]
    public void ASuppliedContractsBoundIsReadAtEveryDepth(string form, string item, string requirement, string meter, string records)
    {
        var program = "contract Quiet\n    func ping(self: ref/Self) -> i32\n        effect confined\ncontract Loud\n    func ping(self: ref/Self) -> i32\n" +
            $"contract Node\n    associate Next is Node\n    associate Item is {item}\n    associate Other\n{requirement}" +
            "contract Report\n    func report(self: ref/Self) -> i32\n        effect confined\n" + meter + Main;
        CompilationTestHelper.AssertRecordsAtLines(form, program, records);
    }

    // R7 plan Z23 (SPEC 8.11.2, 8.15.2, 4.7.1 step 3): A4 reads each case's own selection: a `confined` implementation whose bare take
    // of v[0] Copies in the ref case runs index there, which writes the Console (e1, accepted on dev 5dd6543e6, which read indexUniq in
    // every view: a Q13 move), as `s is ref` alone does (e1r); `s is uniq` alone selects indexUniq, which reaches no environment (e1v).
    [Theory]
    [InlineData("ref or uniq", "IncompatibleContractImplementation_Kd @ Self is Poke")]
    [InlineData("ref", "IncompatibleContractImplementation_Kd @ Self is Poke")]
    [InlineData("uniq", "")]
    public void AnIndexSelectedByCaseHasItsCasesEffects(string cases, string expected)
    {
        var source = "struct Two<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    public init(first: T) => self.first = first@move\n" +
            "    public func index(self, key: ref/isize) -> ref/T during self\n        Console.writeLine(\"index\")\n        return self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" +
            $"contract Poke\n    func poke<s/T>(self: uniq/Self, v: uniq/Two<s/T>)\n        s is {cases}\n        effect confined\n" +
            $"struct Holder\n    Self is Poke\n    public init() => ()\n    public func poke<s/T>(self: uniq/Self, v: uniq/Two<s/T>)\n        s is {cases}\n" +
            "        let x = v[0]\n        _ = x\nConsole.writeLine(\"ok\")\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(expected, string.Join("; ", c.Binding.Issues.Select(static x => $"{x.Code} @ {First(x.Node)}")
            .Concat(c.Problems.Select(x => $"{x.Code} @ {First(c.SyntaxOf(x.Subject))}"))));
    }

    // Rows 31-36 and C-586's twins (SPEC 22.4.5 ¶2, the Tatara plan's decision 45: a platform member whose effect summary, SPEC 15.6.4,
    // lists a static Field, a first-access initialization included, or has unknown effects is an error at the member), checked as a
    // build checks them (CheckStartup before Analyze) on the freestanding row unless named: A4's UnsatisfiedEffectBound_Kd under
    // Ownership.PlatformStatic at each failing member, each judged alone (SPEC 23.1.2), beside the program's other records (P42's
    // StaticStorage record at a settable static, DECISIONS R5-M82, and Binding's located Unsupported_Kd at a static's Borrow; STATUS
    // Static members). The rule reads no startup record, so a hosted row's Unsupported_Kd stands beside it (35); a failed conformance
    // owes nothing (36, SPEC 8.6.3); an environment effect alone accesses no static storage (the Console twin, SPEC 22.4.5 ¶3); a
    // callee's static access and a destruction's are the member's (34, E2, E3). A requirement call that takes its effects from a
    // `confined` bound may read an immutable static, so it fails as an unbounded one does (C-591's R4c and R4d, R4, and R4cf through a
    // Callable input; T's rulings 134 and 136), and a closed Contract's `tryResolve` reads none (R4p, SPEC 8.7.5).
    [Theory]
    [InlineData("31", CountedRecords + " | " + Write)]
    [InlineData("32", "")]
    [InlineData("33", Write)]
    [InlineData("33v", "")]
    [InlineData("34", "Collection Unsupported_Kd public var count: i64 = 0 | " + Clock)]
    [InlineData("35", CountedRecords + " | " + Write + " | Startup Unsupported_Kd Self is Platform")]
    [InlineData("36", "Binding MissingContractImplementation_Kd Self is Platform | " + CountedRecords)]
    [InlineData("console", "")]
    [InlineData("E1", Write)]
    [InlineData("E1v", "")]
    [InlineData("E2", "Collection Unsupported_Kd public var ticks: i64 = 0 | " + Clock + " | " + Write)]
    [InlineData("E2v", "")]
    [InlineData("E3", "Collection Unsupported_Kd public var count: i64 = 0 | " + Write)]
    [InlineData("E3v", "")]
    [InlineData("E4", Start)]
    [InlineData("E4v", "")]
    [InlineData("R4c", Write)]
    [InlineData("R4d", Write)]
    [InlineData("R4", Write)]
    [InlineData("R4p", "")]
    [InlineData("R4cf", Write)]
    public void APlatformMemberAccessesNoStaticStorage(string row, string records)
    {
        var (target, source) = PlatformProgram(row);
        Assert.Equal(records, PlatformRecords(MinimalEmissionTest.Check(target, source, "Hello.kimi", OutputKind.Application)));
    }

    // Row 19 (SPEC 15.6.4: a summary includes every function its body calls): the forwarding root struct's `write` calls the Library
    // Board's, which writes a static, so it fails at its own declaration; the Library's struct is no platform and owes nothing.
    [Fact]
    public void AForwardedStaticAccessFailsAtThePlatformsMember()
    {
        var c = ModuleBindingTest.Create(StartupBindingTest.Forwarding, StartupBindingTest.ForwardedLibrary, row: Freestanding);
        c.Check();
        Assert.Equal(
            "Collection Unsupported_Kd public var writes: i64 = 0 | Ownership.PlatformStatic UnsatisfiedEffectBound_Kd " +
            "public func write(self: uniq/Self, stream: i32, text: Text.Utf8Slice) -> i64 => self.inner.write(stream, text)",
            PlatformRecords(c));
    }

    // R6-check H3b T40: since the MIR decides acceptance (H5, DECISIONS R5-M185), A4 judges an effect bound where Binding did. Binding
    // completes; a violated bound refuses the program with A4's IncompatibleContractImplementation_Kd at the conformance's `Self is C`
    // clause, and a kept one leaves no record. The MIR side is compared exactly (MirTargetSide): beside the bound's record it holds the
    // program's other records, such as P42's StaticStorage record, which the collector reports at a settable static such as Metrics.puts
    // (DECISIONS R5-M82; STATUS Static members).
    internal static void AssertBound(Compilation c, bool valid, string conformance, params (DiagnosticCode Code, SyntaxLocation At)[] others)
    {
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(valid || !c.Accepts, MinimalEmissionTest.Describe(c));
        (DiagnosticCode, SyntaxLocation)[] bound = valid ? [] : [(DiagnosticCode.IncompatibleContractImplementation_Kd, MirTargetSide.At(conformance))];
        MirTargetSide.Check(c, [.. bound, .. others]);
    }

    // StorageBindingRejectedKeepsItsRepairsRecords's program of a form: Holder's reset over its item, Bad's Field m of the given Type.
    private static string HolderSource(string form, string field)
    {
        var item = form.StartsWith("deep", StringComparison.Ordinal) ? RetentionTest.Box65 : "Bad";
        var (conformance, witness) = form switch
        {
            "requirement" => ("Named", "    public func name(self: ref/Self) -> i32 => 1\n"),
            "format" => ("Utf8Format", "    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(1)\n"),
            "equals" or "quiet" => (form == "quiet" ? "QuietEq" : "Equatable", "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n"),
            _ => (null, string.Empty),
        };
        var (call, helpers) = form switch
        {
            "forward" or "deep" => ("U.wrap(self.item@ref)", "    public func touch<T>(x: ref/T) => ()\n    public func wrap<T>(x: ref/T) => U.touch(x)\n"),
            "environment" => ("U.wrap(self.item@ref)", "    public func touch<T>(x: ref/T) => Console.writeLine(\"t\")\n    public func wrap<T>(x: ref/T) => U.touch(x)\n"),
            "argument" or "deepArgument" => ($"_ = U.outer<{item}>()", "    public func outer<T>() -> i32 => U.inner<T>()\n    public func inner<T>() -> i32 => 0\n"),
            "dictionary" => ("_ = U.keyed(self.item@ref)", "    public func keyed<T>(x: ref/T) -> i32\n        var d: Dictionary<i32, Option<T>> = [:]\n        return 0\n"),
            "requirement" => ("_ = U.ask(self.item@ref)", "    public func ask<T>(x: ref/T) -> i32\n        T is Named\n        return x.name()\n"),
            "format" => ("_ = U.show(self.item@ref)", "    public func show<T>(x: ref/T) -> string\n        T is Utf8Format\n        return Text.toString(x)\n"),
            "equals" or "quiet" => ("_ = U.same(self.item@ref)", $"    public func same<T>(x: ref/T) -> bool\n        T is {conformance}\n        return x == x\n"),
            _ => ("U.pair(self.item@ref, self.big@ref)", "    public func touch<T, S>(x: ref/T, y: ref/S) => ()\n    public func pair<T, S>(x: ref/T, y: ref/S) => U.touch(x, y)\n"),
        };
        return "contract Reset\n    func reset(self: uniq/Self)\n        effect confined\ncontract Named\n    func name(self: ref/Self) -> i32\n        effect confined\n" +
            "contract QuietEq: Equatable\n    effect Equatable.equals confined\n" +
            RetentionTest.Box + "struct Bad\n" + (conformance is null ? string.Empty : $"    Self is {conformance}\n") + $"    public var m: {field}\n" + witness +
            $"struct Holder\n    Self is Reset\n    var item: {item}\n" + (form == "pair" ? $"    var big: {RetentionTest.Box65}\n" : string.Empty) +
            $"    public func reset(self: uniq/Self)\n        {call}\ngroup U\n" + helpers + Main;
    }

    // Rows 31-36 and C-586's twins: each row's target and program, the Kernel derived by exact replacements (ruling 121 N7). Row 31 is
    // C-458's: Hello reads a settable static through a Borrow and `write` writes it.
    private static (TargetProfile Row, string Source) PlatformProgram(string row)
    {
        const string layout = "group Layout\n    public let pageBase: usize = 0x0100_0000\n";
        const string field = "    var ticks: i64\n"; // The Board's last field and its init's last line.
        const string set = "        self.ticks = 0\n";
        return row switch
        {
            "31" => (Freestanding, CountedProgram(Kernel)),
            "32" => (Freestanding, StartupBindingTest.Derive(Kernel, (field, field + "    var writes: i64\n"), (set, set + "        self.writes = 0\n"), (Bytes, "        self.writes += 1\n" + Bytes))),
            "33" => (Freestanding, KernelSources.Hello + ScreenBoard),
            "33v" => (Freestanding, StartupBindingTest.Derive(Kernel, (field, field + "    var width: isize\n"), (set, set + "        self.width = 80\n"), (Cells, "(self.cell / self.width + 1) * self.width"))),
            "34" => (Freestanding, StartupBindingTest.Derive(Kernel, (Ticks, "        Log.note()\n" + Ticks)) + "group Log\n    public var count: i64 = 0\n    public func note() => Log.count += 1\n"),
            "35" => (TargetProfile.Windows, CountedProgram(Kernel)),
            "36" => (Freestanding, CountedProgram(KernelSources.Hello + KernelSources.Board)),
            "console" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        Console.writeLine(\"write\")\n" + Bytes))),
            "E1" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        var watch = Time.Stopwatch.init()\n        watch.start()\n" + Bytes))),
            "E1v" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        let now = self.clock()\n" + Bytes))),
            "E2" => (Freestanding, StartupBindingTest.Derive(Kernel, (Ticks, "        Metrics.ticks += 1\n        return Metrics.ticks\n"), (Bytes, "        _ = self.clock()\n" + Bytes)) +
                "group Metrics\n    public var ticks: i64 = 0\n"),
            "E2v" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        _ = self.clock()\n" + Bytes))),
            "E3" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        let mark = Mark.init()\n" + Bytes)) +
                "group Log\n    public var count: i64 = 0\nstruct Mark\n    public init() => ()\n    drop => Log.count += 1\n"),
            "E3v" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        let mark = Mark.init()\n" + Bytes)) + "struct Mark\n    public init() => ()\n    drop => ()\n"),
            "E4" => (Freestanding, StartupBindingTest.Derive(Kernel, ("window + 0x0100_0000", "window + Layout.pageBase")) + layout),
            "E4v" => (Freestanding, Kernel + layout),
            "R4c" => (Freestanding, WideProgram("        effect confined\n", "Screen.width")),
            "R4d" => (Freestanding, WideProgram("        effect confined\n", "80")),
            "R4" => (Freestanding, WideProgram(string.Empty, "80")),
            "R4p" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, Bytes + "        let first: isize = 0\n        _ = bytes.tryGet(first)\n"))),
            "R4cf" => (Freestanding, StartupBindingTest.Derive(Kernel, (Bytes, "        let read = func () => Tables.scale\n        _ = Apply.run(read@ref)\n" + Bytes)) +
                "group Tables\n    public let scale: i64 = 3\ngroup Apply\n    public func run<F>(f: ref/F) -> i64\n        F is Callable<() -> i64>\n            effect confined\n        return f()\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };

        // R4c, R4d and R4: `write` divides by Util.wide<Eighty>(), whose requirement call on the abstract T takes its effects from Wide's
        // bound, if any; Eighty's witness returns `width`.
        static string WideProgram(string bound, string width) => StartupBindingTest.Derive(Kernel, (Cells, "(self.cell / Util.wide<Eighty>() + 1) * 80")) +
            "group Screen\n    public let width: isize = 80\ncontract Wide\n    func width() -> isize\n" + bound +
            $"struct Eighty\n    Self is Wide\n    public func width() -> isize => {width}\ngroup Util\n    public func wide<T>() -> isize\n        T is Wide\n        return T.width()\n";

        static string CountedProgram(string kernel) => StartupBindingTest.Derive(
            kernel,
            (HelloLine, "let seen = Metrics.writes@ref\nConsole.writeLine(\"tick\")\nlet after = seen@follow\n"),
            (Bytes, "        Metrics.writes += 1\n" + Bytes)) + "group Metrics\n    public var writes: i64 = 0\n";
    }

    // A checked program's records (SPEC 23.1.2: each independent failure's own), in ordinal order and " | "-joined, each its source, its
    // code and the first line of its subject: the startup check's ("Startup"), Binding's ("Binding") and the MIR's, a problem under its
    // requirement and a construction or collection record under its class.
    private static string PlatformRecords(Compilation c)
    {
        var problems = c.BuildMir().Problems.All.ToArray();
        return string.Join(" | ", c.Binding.StartupIssues.Select(static x => $"Startup {x.Code} {First(x.Node)}")
            .Concat(c.Binding.Issues.Select(static x => $"Binding {x.Code} {First(x.Node)}"))
            .Concat(problems.Select(x => $"{x.RecordClass?.ToString() ?? x.Requirement.ToString()} {x.Code} {First(c.SyntaxOf(x.Subject))}"))
            .Order(StringComparer.Ordinal));
    }

    // A node's first line.
    private static string First(Koto node) => node.ToString().Split('\n')[0].Trim();
}
