// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class CompilerFunctionReferenceTest
{
    [Theory]
    [InlineData("Item", "let f = Raw.allocate<u8>\nlet p = f(2)\nunsafe => Raw.release(p)")]
    [InlineData("Erased", "let f: (isize) -> raw/u8 = Raw.allocate\nlet p = f(2)\nunsafe => Raw.release(p)")]
    [InlineData("Moved", "let f: (isize) -> raw/u8 = Raw.allocate\nlet g = f@move\nlet p = g(0)\nunsafe => Raw.release(p)")]
    [InlineData("Callable", "func make<F>(f: ref/F) -> raw/u8\n    F is Callable<(isize) -> raw/u8>\n    return f(2)\nlet p = make(Raw.allocate<u8>)\nunsafe => Raw.release(p)")]
    [InlineData("Zero", "let f = Raw.allocate<()>\nlet p = f(10)\nunsafe => Raw.release(p)")]
    [InlineData("Generic", "func make<T>() -> raw/T\n    let f = Raw.allocate<T>\n    return f(2)\nlet p = make<u8>()\nunsafe => Raw.release(p)")]
    [InlineData("Default", "func make(f: (isize) -> raw/u8 = Raw.allocate<u8>) -> raw/u8 => f(2)\nlet p = make()\nunsafe => Raw.release(p)")]
    public void ExecutesRawAllocation(string name, string source)
        => ScalarEmissionTest.EmitFixture("CompilerReference" + name, source, string.Empty);

    [Theory]
    [InlineData("Item", "let f = Raw.allocate<u8>\nlet p = f(-1)", "Hello.kimi:2:9")]
    [InlineData("Erased", "let f: (isize) -> raw/u8 = Raw.allocate\nlet p = f(-1)", "Hello.kimi:2:9")]
    [InlineData("Helper", "func make(f: ref/((isize) -> raw/u8)) -> raw/u8 => f(-1)\nlet f: (isize) -> raw/u8 = Raw.allocate\nlet p = make(f)", "Hello.kimi:1:52")]
    public void ReportsTheActualInvocation(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("CompilerReferenceAbort" + name, source, string.Empty, 1, location + ": abort KIMI_E_ARG_RANGE: Argument out of range\n");

    [Theory]
    [InlineData("let f: (ref/string) -> () = Console.writeLine\nf(\"ok\")")]
    public void ConsoleEntriesErase(string source)
        => ScalarEmissionTest.EmitFixture("CompilerReferenceString", source, "ok\n");

    [Theory]
    [InlineData("Object", "let f: (i32) -> obj/i32 = Kimi.Intrinsics.makeObj\nlet value = f(42)\nrequire value@follow == 42 else => $abort(\"object\")")]
    [InlineData("Rc", "let f = Kimi.Intrinsics.makeRc<i32>\nlet value = f(42)\nrequire value@follow == 42 else => $abort(\"rc\")")]
    [InlineData("Arc", "let f: (i32) -> arc/i32 = Kimi.Intrinsics.makeArc\nlet value = f(42)\nrequire value@follow == 42 else => $abort(\"arc\")")]
    [InlineData("Text", "let f: (ref/i32) -> string = Text.toString\nlet n = 42\nrequire f(n@ref) == \"42\" else => $abort(\"format\")")]
    [InlineData("Heap", "let f = Text.heap\nlet buffer = f(0)")]
    [InlineData("ArrayAppend", "let f: (uniq/Array<i32>, i32) -> () = Array<i32>.append\nvar a: Array<i32> = []\nf(a@uniq, 42)\nrequire a[0] == 42 else => $abort(\"append\")")]
    [InlineData("ArrayPop", "let f = Array<i32>.pop\nvar a: Array<i32> = [42]\nmatch f(a@uniq)\n    .Some(let n) => require n == 42 else => $abort(\"pop\")\n    .None => $abort(\"none\")")]
    [InlineData("Replace", "let f: (uniq/string, string) -> () = Kimi.Intrinsics.replace\nvar text = \"before\"\nf(text@uniq, \"after\")\nrequire text == \"after\" else => $abort(\"replace\")")]
    [InlineData("Exchange", "let f = Kimi.Intrinsics.exchange<i32>\nvar n = 7\nrequire f(n@uniq, 42) == 7 and n == 42 else => $abort(\"exchange\")")]
    [InlineData("Swap", "let f: (uniq/string, uniq/string) -> () = Kimi.Intrinsics.swap\nvar a = \"a\"\nvar b = \"b\"\nf(a@uniq, b@uniq)\nrequire a == \"b\" and b == \"a\" else => $abort(\"swap\")")]
    [InlineData("ArrayReserve", "let reserve = Array<string>.reserve\nlet clear = Array<string>.clear\nlet shrink = Array<string>.shrinkToFit\nvar a: Array<string> = [\"a\"]\nreserve(a@uniq, 8)\nclear(a@uniq)\nshrink(a@uniq)\nrequire a.length == 0 else => $abort(\"clear\")")]
    [InlineData("ZeroAppend", "let f = Array<()>.append\nvar a: Array<()> = []\nf(a@uniq, ())\nrequire a.length == 1 else => $abort(\"unit\")")]
    [InlineData("ZeroExchange", "let f = Kimi.Intrinsics.exchange<()>\nvar n = ()\nf(n@uniq, ())")]
    [InlineData("StringExchange", "let f: (uniq/string, string) -> string = Kimi.Intrinsics.exchange\nvar n = \"old\"\nlet old = f(n@uniq, \"new\")\nrequire old == \"old\" and n == \"new\" else => $abort(\"exchange\")")]
    [InlineData("Clone", "let f: (ref/(rc/i32)) -> rc/i32 = Kimi.Intrinsics.clone\nlet a = Kimi.Intrinsics.makeRc(42)\nlet b = f(a@ref)\nrequire b@follow == 42 else => $abort(\"clone\")")]
    [InlineData("Fixed", "let f = Text.fixed<4>\nvar a: [4 of u8] = [4 of 0]\nlet b = f(a@uniq)")]
    [InlineData("ArrayOrder", "let insert = Array<i32>.insert<isize>\nlet remove = Array<i32>.remove<isize>\nlet swap = Array<i32>.swap<isize, isize>\nvar a: Array<i32> = [1, 2]\ninsert(a@uniq, 1, 3)\nswap(a@uniq, 0, 2)\nrequire remove(a@uniq, 1) == 3 and a[0] == 2 else => $abort(\"array\")")]
    [InlineData("ArrayString", "let remove = Array<string>.remove<isize>\nvar a: Array<string> = [\"a\"]\nrequire remove(a@uniq, 0) == \"a\" else => $abort(\"remove\")")]
    [InlineData("TryFormat", "let f = Text.tryFormat<i32, 4>\nlet n = 42\nvar bytes = [4 of 0@u8]\nmatch f(n@ref, bytes@uniq)@move\n    .Ok(let view) => require view.length == 2 else => $abort(\"format\")\n    .Err(_) => $abort(\"full\")")]
    [InlineData("HeapMembers", "let make = Text.heap\nlet clear = Text.HeapBuffer.clear\nvar heap = make(0)\nclear(heap@uniq)")]
    public void RuntimeEntriesShareExistingPlans(string name, string source)
        => ScalarEmissionTest.EmitFixture("CompilerReference" + name, source, string.Empty);

    [Theory]
    [InlineData("Item", "let show = Text.toString<Value<i32>>")]
    [InlineData("Erased", "let show: (ref/Value<i32>) -> string = Text.toString")]
    public void ReferencesUseTheSelectedUserFormatter(string name, string declaration)
    {
        const string Prefix = """
            struct Value<T>
                Self is Utf8Format
                let value: T
                public init(value: T) => self.value = value@move
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write("word")
            """;
        ScalarEmissionTest.EmitFixture("CompilerReferenceUserFormat" + name, Prefix + "\n" + declaration + "\nlet n = Value<i32>.init(42)\nrequire show(n@ref) == \"word\" else => $abort(\"format\")", string.Empty);
    }

    [Fact]
    public void ReferencesKeepTheWriterCallbackAndItsLoan()
    {
        const string Source = """
            struct Destination<T>
                Self is BufferWriter
                let value: T
                public var count: i32 = 0
                public init(value: T) => self.value = value@move
                public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
                    self.count += 1
                    return .Err(BufferFull.init())
            let make = Text.writer<Destination<i32>>
            var destination = Destination<i32>.init(42)
            var writer = make(destination@uniq)
            match (writer@uniq).write(123)@move
                .Ok(_) => $abort("expected full")
                .Err(_) => ()
            require destination.count == 1 else => $abort("reservation count")
            """;
        ScalarEmissionTest.EmitFixture("CompilerReferenceUserWriter", Source, string.Empty);
    }

    [Fact]
    public void Utf8ReferenceCarriesItsInputLoan()
        => ScalarEmissionTest.EmitFixture("CompilerReferenceUtf8", "let convert = Text.utf8\nlet text = \"ok\"\nlet view = convert(text@ref)\nConsole.writeLine(view)", "ok\n");

    [Theory]
    [InlineData("SourceItem", "let f = FromEnd<i32>.resolve\nlet p: FromEnd<i32> = ^4\nlet n = f(p, 3)", "Hello.kimi:3:9")]
    [InlineData("SourceErased", "let f: (FromEnd<i32>, isize) -> isize = FromEnd<i32>.resolve\nlet p: FromEnd<i32> = ^4\nlet n = f(p, 3)", "Hello.kimi:3:9")]
    [InlineData("SecondCall", "let f: (isize) -> raw/u8 = Raw.allocate\nlet p = f(0)\nunsafe => Raw.release(p)\nlet q = f(-1)", "Hello.kimi:4:9")]
    public void ReusedAndSourceEntriesKeepTheInvocation(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("CompilerReference" + name, source, string.Empty, 1, location + ": abort KIMI_E_ARG_RANGE: Argument out of range\n");

    [Fact]
    public void OversizedAllocationReportsTheInvocation()
        => ScalarEmissionTest.EmitFixture("CompilerReferenceOversized", "let f: (isize) -> raw/i64 = Raw.allocate\nlet p = f(4611686018427387904)", string.Empty, 1, "Hello.kimi:2:9: abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit\n");

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void CreatingItemsAndErasingEmptyEnvironmentsDoesNotAllocate()
        => NativeAllocationAudit.WriteFixture("CompilerReferenceEmpty", "let f = Raw.allocate<u8>\nlet g: (isize) -> raw/u8 = f\nlet h = g@move", 0, 0, 0);

    [Theory]
    [InlineData("Replace", "let f = Kimi.Intrinsics.replace<Value>\nf(n@uniq, Value.init())")]
    [InlineData("Exchange", "let f = Kimi.Intrinsics.exchange<Value>\nlet previous = f(n@uniq, Value.init())")]
    [InlineData("Swap", "let f = Kimi.Intrinsics.swap<Value>\nvar other = Value.init()\nf(n@uniq, other@uniq)")]
    public void ZeroSizedUpdatesPreserveDestruction(string name, string update)
    {
        const string Prefix = "struct Value\n    public init() => ()\n    drop => Console.writeLine(\"drop\")\nvar n = Value.init()\n";
        ScalarEmissionTest.EmitFixture("CompilerReferenceZeroDrop" + name, Prefix + update, "drop\ndrop\n");
    }

    [Fact]
    public void ReplacedUserValuesKeepTheirOwnDestructorLocation()
    {
        const string Source = "struct Value\n    public init() => ()\n    drop => $abort(\"drop\")\nvar n = Value.init()\nlet f = Kimi.Intrinsics.replace<Value>\nf(n@uniq, Value.init())";
        ScalarEmissionTest.EmitFixture("CompilerReferenceDrop", Source, string.Empty, 1, "Hello.kimi:3:13: abort KIMI_E_ABORT: drop\n");
    }

    // U9 connects all nested per-call input Origins, shared with user functions; this is independent of entry generation.
    [Fact]
    public void NestedInputOriginsKeepTheirExistingLocatedBoundary()
    {
        var errors = DiagnosticCorpus.Check("let f = Text.validateUtf8\nvar bytes: Array<u8> = [65]\nmatch f(bytes.slice(..))@move\n    .Ok(let view) => require view.length == 1 else => $abort(\"validate\")\n    .Err(_) => $abort(\"utf8\")").Diagnostics;
        var error = Assert.Single(errors);
        Assert.Equal(nameof(Kimi.DiagnosticCode.UnsupportedBinding_Kd), error.Code);
    }

    [Fact]
    public void Utf8EntryWithAFixedOriginCanBeErased()
    {
        const string Source = "let f: (Text.Utf8Slice{t}) -> () = Console.writeLine\n    origin t.source == static";
        Assert.Empty(DiagnosticCorpus.Check(Source).Diagnostics);
        ScalarEmissionTest.EmitFixture("CompilerReferenceUtf8Erasure", Source, string.Empty);
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("let f: (isize) -> raw/u8 = Raw.allocate\nlet p = f(2)\nunsafe => Raw.release(p)\nlet show: (ref/string) -> () = Console.writeLine\nshow(\"ok\")")]
    [InlineData("let f = Kimi.Intrinsics.makeRc<i32>\nlet value = f(42)\nlet show = Text.toString<i32>\n_ = show(value@follow)")]
    [InlineData("let f = Array<string>.append\nvar a: Array<string> = []\nf(a@uniq, \"a\")\nlet clear = Array<string>.clear\nclear(a@uniq)")]
    public void WarmAdaptersReuseTheirPhysicalEntries(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(Kimi.Compiler.OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let f = Raw.release<u8>")]
    [InlineData("let f = Raw.initialize<u8>")]
    [InlineData("let f = Raw.slice<u8>")]
    public void UnsafeEntriesStayForbidden(string source)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal(DiagnosticCategory.Language, error.Category));
    }
}
