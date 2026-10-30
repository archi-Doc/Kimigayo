// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.11.3: a conformance checks each witness against the bounds its Contract and the ancestors declare, from
// the declarations alone; user Contracts bound their requirements exactly as the standard ones do.
public class EffectBoundImplementationTest
{
    private const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n" +
        "group Metrics\n    public var puts: isize = 0\n    public func record() => Metrics.puts += 1\n";

    private const string Step = "contract Step\n    func step(self: uniq/Self)\n        effect confined\n";

    private const string Box = "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n" +
        "    public func index(self, key: ref/isize) -> ref/i32 during self => self.value@ref\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/i32 during self\n        Console.writeLine(\"step\")\n        return self.value@uniq\n";

    private const string Cleanup = "struct Cleanup {step}\n    let cursor: uniq/Cursor during step\n    public init(cursor: uniq/Cursor during step) => self.cursor = cursor@move\n    ";

    private const string Stepped = "struct Cursor\n    Self is Step\n    public var count: i32 = 0\n    public func step(self: uniq/Self)\n        do\n            let cleanup = Cleanup.init(self)\n";

    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData("struct CountingSink\n    Self is Sink\n    var count: isize = 0\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        self.count += 1\n        return .Ok(())\n", true)]
    [InlineData("struct Forward<W>\n    W is Sink\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", true)]
    [InlineData("struct LoggingSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Metrics.record()\n        return .Ok(())\n", false)]
    [InlineData("struct ConsoleSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        Console.writeLine(\"put\")\n        return .Ok(())\n", false)]
    [InlineData("contract Plain\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\nstruct Loose<W>\n    W is Plain\n    Self is Sink\n    var inner: W\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        return self.inner.put(value)\n", false)]
    public void ConfinedRejectsEnvironmentEffects(string declarations, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Sink + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

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
    {
        var c = MinimalEmissionTest.Analyze(Sink + declarations + Main);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // SPEC 8.11.2: an effect the summary cannot classify yet is one Unsupported_Kd at the conformance: the destructions of a static
    // initializer other than a constant (reading an immutable static is no environment effect).
    [Theory]
    [InlineData("contract Read\n    func read(self: ref/Self) -> i32\n        effect confined\ngroup G\n    public let value: i32 = 2 + 3\nstruct S\n    Self is Read\n    public func read(self: ref/Self) -> i32 => G.value\n", "Self is Read", nameof(DiagnosticCode.Unsupported_Kd))]
    public void AnUnclassifiedEffectIsAnImplementationLimit(string declarations, string at, string code)
    {
        var source = declarations + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, source.IndexOf(at, StringComparison.Ordinal), at.Length), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }

    // SPEC 8.11.3, 23.1.2 (DECISIONS BU-38): a witness reaching storage that rests on a part Binding rejected (Bad's Field m) reads it as
    // instantiated, so its conformance has the records its repair has, beside Binding's own: none through a forwarded call, a forwarded
    // Type argument, a dictionary, a requirement, formatting or a comparison over it, the repair's violation through a forwarded
    // environment effect, and Unsupported_Kd for a storage limit beside it or alone (fail-before: Unsupported_Kd at each conformance, the
    // rejected part read as a limit).
    [Theory]
    [InlineData("forward", "")]
    [InlineData("environment", "IncompatibleContractImplementation_Kd @ Self is Reset")]
    [InlineData("argument", "")]
    [InlineData("dictionary", "")]
    [InlineData("requirement", "")]
    [InlineData("format", "")]
    [InlineData("equals", "")]
    [InlineData("pair", "Unsupported_Kd @ Self is Reset")]
    [InlineData("deep", "Unsupported_Kd @ Self is Reset")]
    [InlineData("deepArgument", "Unsupported_Kd @ Self is Reset")]
    public void StorageBindingRejectedViolatesNothing(string form, string repaired)
    {
        Assert.Equal(repaired, string.Join("; ", Issues("i32")));
        Assert.Equal(Issues("i32").Append("UnresolvedBinding_Kd @ Missing").Order(), Issues("Missing").Order());

        string[] Issues(string field)
        {
            var item = form.StartsWith("deep", StringComparison.Ordinal) ? RetentionTest.Box65 : "Bad";
            var (conformance, witness) = form switch
            {
                "requirement" => ("Named", "    public func name(self: ref/Self) -> i32 => 1\n"),
                "format" => ("Utf8Format", "    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(1)\n"),
                "equals" => ("Equatable", "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n"),
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
                "equals" => ("_ = U.same(self.item@ref)", "    public func same<T>(x: ref/T) -> bool\n        T is Equatable\n        return x == x\n"),
                _ => ("U.pair(self.item@ref, self.big@ref)", "    public func touch<T, S>(x: ref/T, y: ref/S) => ()\n    public func pair<T, S>(x: ref/T, y: ref/S) => U.touch(x, y)\n"),
            };
            var source = "contract Reset\n    func reset(self: uniq/Self)\n        effect confined\ncontract Named\n    func name(self: ref/Self) -> i32\n        effect confined\n" +
                RetentionTest.Box + "struct Bad\n" + (conformance is null ? string.Empty : $"    Self is {conformance}\n") + $"    public var m: {field}\n" + witness +
                $"struct Holder\n    Self is Reset\n    var item: {item}\n" + (form == "pair" ? $"    var big: {RetentionTest.Box65}\n" : string.Empty) +
                $"    public func reset(self: uniq/Self)\n        {call}\ngroup U\n" + helpers + Main;
            return [.. MinimalEmissionTest.Analyze(source).Binding.Issues.Select(static x => $"{x.Code} @ {x.Node.ToString().Split('\n')[0].Trim()}")];
        }
    }

    // SPEC 8.11.2: every compiler function has one effect class. The Storage and Raw families act only on their inputs and the
    // allocator; Console output and the test temporary directory are environment effects; formatting dispatch is followed to its
    // witness before classification. A new kind is unclassified until it is placed here.
    [Fact]
    public void EveryCompilerFunctionHasOneEffectClass()
    {
        foreach (var kind in Enum.GetValues<CompilerFunctionKind>())
        {
            var expected = kind switch
            {
                CompilerFunctionKind.None => CompilerFunctionEffect.Unclassified,
                CompilerFunctionKind.WriteLine or CompilerFunctionKind.WriteLineUtf8 or CompilerFunctionKind.TestTempDirectory => CompilerFunctionEffect.Environment,
                CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat => CompilerFunctionEffect.Formatting,
                _ => CompilerFunctionEffect.Inputs,
            };
            Assert.True(expected == IntrinsicDisposition.Effect(kind), kind.ToString());
        }
    }

    // SPEC 4.9.6: the Kimi body of each operation that publishes its summary stays within its row with abstract Type parameters,
    // under confined; `K` equality is the only parameter-dependent requirement a row publishes, and the capacity and clear rows
    // publish none at Binding (destructions are checked after ownership analysis).
    [Fact]
    public void PublishedOperationBodiesStayWithinTheirRows()
    {
        var c = MinimalEmissionTest.Analyze(Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var equals = ((ContractKoto)c.Library.GetSymbol(KimiDeclarationId.Equatable)!.Declaration).Members.OfType<FunctionKoto>().Single(static x => x.Name == "equals").SymbolOf();
        var operations = 0;
        foreach (var entry in KimiLibraryCatalog.Entries)
        {
            var operation = c.Library.GetSymbol(entry.Id)!;
            if (KimiLibraryCatalog.Row(operation) is null || entry.Function != CompilerFunctionKind.None)
            {
                continue; // A compiler function's row has no Kimi body to verify.
            }

            if (entry.Implementation is { } implementation)
            {
                // A bodiless constructor's summary is its linked implementation's, which is checked as its own entry.
                Assert.Same(c.Library.GetSymbol(implementation), c.Library.ConstructorImplementation(operation));
                continue;
            }

            var published = KimiLibraryCatalog.Row(operation)!.Value.Runs is null ? null : equals;
            var (valid, violation, node) = c.Binding.SummarizePublishedOperation(operation, published);
            Assert.True(valid, $"{entry.Name}: {violation} at {node}");

            operations++;
        }

        Assert.Equal(11, operations);
    }

    // SPEC 8.11.3, 8.11.2: confined is judged over every body the witness reaches, which its calls select: a helper, a recursive generic
    // helper, every specialization the call may still select (by Type, length or a compound key), the accessor or indexer a Place's use
    // selects, and a destructor ownership analysis plans; a member no call reaches is not judged. (These forms were preserves results
    // checks of Iterator conformances before the adopted proposal Abstract Calls Reach by Paths removed that bound.)
    [Theory]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self)\n        Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self)\n        self.count += 1\n    public func step(self: uniq/Self) => self.update()\n", true)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<T>(self: uniq/Self, depth: i32)\n        if depth > 0 => self.update<T>(depth - 1)\n        Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<i32>(2)\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<T>(self: uniq/Self, depth: i32)\n        if depth > 0 => self.update<T>(depth - 1)\n        self.count += 1\n    public func step(self: uniq/Self) => self.update<i32>(2)\n", true)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.count += 1\n", true)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<T>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => self.count += 1\n    public func step(self: uniq/Self) => self.update<T>()\n", true)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<bool>()\n", true)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<i32>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<i32>()\n", false)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<length M>(self: uniq/Self) => self.count += 1\n    specialize func update<3>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<2>()\n", true)]
    [InlineData("struct Cursor\n    Self is Step\n    var count: i32 = 0\n    func update<length M>(self: uniq/Self) => self.count += 1\n    specialize func update<3>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<3>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<(i32, bool)>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<(T, bool)>()\n", false)]
    [InlineData("struct Cursor<T>\n    Self is Step\n    var count: i32 = 0\n    func update<U>(self: uniq/Self) => self.count += 1\n    specialize func update<(i32, bool)>(self: uniq/Self) => Console.writeLine(\"step\")\n    public func step(self: uniq/Self) => self.update<(T, i32)>()\n", true)]
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
            Assert.False(c.Bind().IsComplete);
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
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
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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
}
