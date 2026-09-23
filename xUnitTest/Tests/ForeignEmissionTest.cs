// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ForeignEmissionTest
{
    private const string LastError = """
        group Native
            #LibraryImport("kernel32", "GetLastError")
            public unsafe func lastError() -> u32
        public func main()
            var first: u32 = 0
            var second: u32 = 1
            unsafe => first = Native.lastError()
            unsafe => second = Native.lastError()
            require first == second else => $abort("changed")
            Console.writeLine("same")
        """;

    [Fact]
    public void KernelImportCallsShareTheRuntimeDeclaration()
    {
        var ir = ScalarEmissionTest.EmitFixture("ForeignLastError", LastError, "same\n");
        Assert.Single(Regex.Matches(ir, @"declare [^\n]*@GetLastError\("));
        Assert.Contains("declare dllimport i32 @GetLastError()\n", ir);
        var body = ir[ir.IndexOf("define internal void @__kimi_f0(", StringComparison.Ordinal)..];
        Assert.Equal(2, Regex.Matches(body[..body.IndexOf("\n}\n", StringComparison.Ordinal)], @"= call i32 @GetLastError\(\)").Count);
    }

    [Theory]
    [InlineData("static", "declare double @mix(i8, i16, i32, i64, float, double)\n")]
    [InlineData("import", "declare dllimport double @mix(i8, i16, i32, i64, float, double)\n")]
    public void ImportDeclaresItsPhysicalSignatureAndSupplyKind(string kind, string declaration)
    {
        var source = """
            group Native
                #LibraryImport("codec", "mix")
                public unsafe func mix(a: i8, b: u16, c: i32, d: i64, e: f32, f: f64) -> f64
                #LibraryImport("codec", "notify")
                public unsafe func notify(value: u32) -> ()
            public func main()
                var total: f64 = 0.0
                unsafe => total = Native.mix(-1, 65535, 3, -4, 0.5, 2.25)
                unsafe => Native.notify(7)
            """;
        var ir = Emit(source, kind);
        Assert.Contains(declaration, ir);
        Assert.Contains(kind == "import" ? "declare dllimport void @notify(i32)\n" : "declare void @notify(i32)\n", ir);
        Assert.Matches(@"= call double @mix\(i8 -1, i16 -1, i32 3, i64 -4, float 0x3FE0000000000000, double 0x4002000000000000\)", ir);
        Assert.Matches(@"\n  call void @notify\(i32 7\)\n", ir);
    }

    [Fact]
    public void DeclarationsOfOneSymbolShareOneExternalFunction()
    {
        var source = """
            group A
                #LibraryImport("codec", "shared")
                public unsafe func call(value: i32) -> i32
            group B
                #LibraryImport("codec", "shared")
                public unsafe func call(value: u32) -> u32
            public func main()
                var a: i32 = 0
                var b: u32 = 0
                unsafe => a = A.call(1)
                unsafe => b = B.call(2)
            """;
        var ir = Emit(source, "static");
        Assert.Single(Regex.Matches(ir, @"declare [^\n]*@shared\("));
        Assert.Matches(@"= call i32 @shared\(i32 1\)", ir);
        Assert.Matches(@"= call i32 @shared\(i32 2\)", ir);
    }

    [Fact]
    public void SymbolsOutsideLlvmIdentifiersAreQuoted()
    {
        var ir = Emit("group Native\n    #LibraryImport(\"codec\", \"?value@@YAHXZ\")\n    public unsafe func value() -> i32\npublic func main()\n    var v: i32 = 0\n    unsafe => v = Native.value()", "import");
        Assert.Contains("declare dllimport i32 @\"?value@@YAHXZ\"()\n", ir);
        Assert.Contains("= call i32 @\"?value@@YAHXZ\"()\n", ir);
    }

    [Theory]
    [InlineData("plain_name.$-1", "plain_name.$-1")]
    [InlineData("1digit", "\"1digit\"")]
    [InlineData("?value@@YAHXZ", "\"?value@@YAHXZ\"")]
    [InlineData("a\"b\\c d", "\"a\\22b\\5Cc d\"")]
    [InlineData("é\t", "\"\\C3\\A9\\09\"")]
    public void ExternalNamesRoundTrip(string symbol, string name)
    {
        Assert.Equal(name, LlvmModuleWriter.ExternalName(symbol));
        Assert.Equal(symbol, LlvmModuleWriter.SymbolFromName(name));
    }

    [Fact]
    public void PointerSignaturesDeclareOpaquePointers()
    {
        var ir = Emit("group Native\n    #LibraryImport(\"codec\", \"touch\")\n    public unsafe func touch(value: unsafe/i32, other: unsafe/f64) -> unsafe/u8\npublic func main() => ()", "static");
        Assert.Contains("declare ptr @touch(ptr, ptr)\n", ir);
    }

    [Fact]
    public void RebindingWithoutTheImportForgetsItsDeclaration()
    {
        var c = Analyze("group Native\n    #LibraryImport(\"codec\", \"gone\")\n    public unsafe func gone() -> i32\npublic func main() => ()", "static");
        using (var writer = new StringWriter())
        {
            Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
            Assert.Contains("declare i32 @gone()\n", writer.ToString());
        }

        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Native").Members.OfType<Kimi.Compiler.Parsing.FunctionKoto>().Single();
        Assert.Single(c.Binding.LibraryImports);
        Assert.True(function.RemoveAttribute(function.AttributeChain!));
        c.Bind();
        Assert.Empty(c.Binding.LibraryImports);
        using (var writer = new StringWriter())
        {
            Assert.False(c.Emission.WriteIr(writer, out _));
            Assert.Equal(string.Empty, writer.ToString());
        }
    }

    [Fact]
    public void PointerValuesPassThroughImportsAndCompare()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var freed: i32 = 0
                var p: unsafe/u8 = null
                require p == null else => $abort("initial")
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                require p != null else => $abort("allocate")
                require null != p else => $abort("reversed")
                let q: unsafe/u8 = p
                require q == p else => $abort("copy")
                unsafe => freed = Native.free(q, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("pointer ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointer", source, "pointer ok\n");
        Assert.Contains("declare dllimport ptr @VirtualAlloc(ptr, i64, i32, i32)\n", ir);
        Assert.Contains("declare dllimport i32 @VirtualFree(ptr, i64, i32)\n", ir);
        Assert.Matches(@"= call ptr @VirtualAlloc\(ptr null, i64 4096, i32 12288, i32 4\)", ir);
        Assert.Matches(@"= icmp ne ptr %v\d+, null", ir);
        Assert.Matches(@"= icmp eq ptr %v\d+, %v\d+", ir);
    }

    [Theory]
    [InlineData("let p: unsafe/i32 = null\nlet q: unsafe/i32 = null\nlet r = p < q", true)]
    [InlineData("let p: unsafe/i32 = null\nlet q: unsafe/u32 = null\nlet r = p == q", true)]
    [InlineData("let r = null == null", false)]
    [InlineData("let p: unsafe/i32 = null\nlet r = p == null", false)]
    public void PointerEqualityRequiresOneKnownPointerType(string body, bool mismatch)
    {
        var c = MinimalEmissionTest.Analyze(body);
        Assert.Equal(mismatch, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.TypeMismatch_Kd));
        using var writer = new StringWriter();
        Assert.Equal(!mismatch && body.Contains("p ==", StringComparison.Ordinal), c.Emission.WriteIr(writer, out _));
    }

    [Theory]
    [InlineData("let p: unsafe/i32 = null\nlet a = unsafe => p as usize")]
    public void UnimplementedPointerOperationsDoNotGenerate(string body)
    {
        var c = MinimalEmissionTest.Analyze(body);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void PointerConversionsPreserveAddresses()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var p: unsafe/u8 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var address: usize = 0
                unsafe => address = p@usize
                require address != 0 else => $abort("address")
                require address % 4096 == 0 else => $abort("alignment")
                var back: unsafe/u8 = null
                unsafe => back = address@unsafe/u8
                require back == p else => $abort("round trip")
                var words: unsafe/i32 = null
                unsafe => words = p@unsafe/i32
                let same = words@unsafe/i32
                var bytes: unsafe/u8 = null
                unsafe => bytes = same@unsafe/u8
                require bytes == p else => $abort("pointer cast")
                var zero: unsafe/u8 = p
                unsafe => zero = 0@unsafe/u8
                require zero == null else => $abort("zero")
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("cast ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerCast", source, "cast ok\n");
        Assert.Matches(@"= ptrtoint ptr %v\d+ to i64", ir);
        Assert.Matches(@"= inttoptr i64 %v\d+ to ptr", ir);
        Assert.Matches(@"= inttoptr i64 0 to ptr", ir);
    }

    [Theory]
    [InlineData("let p: unsafe/i32 = null\nlet a = unsafe => p@u64", true)]
    [InlineData("let n: i32 = 1\nlet p = unsafe => n@unsafe/u8", true)]
    [InlineData("let p: unsafe/i32 = null\nvar q: unsafe/u8 = null\nq = p@unsafe/u8", false)]
    [InlineData("let p: unsafe/i32 = null\nvar a: usize = 0\na = p@usize", false)]
    public void PointerConversionsRequireTheirTypesAndUnsafeContext(string body, bool mismatch)
    {
        var c = MinimalEmissionTest.Analyze(body);
        Assert.Equal(mismatch, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.TypeMismatch_Kd));
        if (!mismatch)
        {
            Assert.Contains(c.Ownership.ControlFlow!.Issues, x => x.Message.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
    }

    [Fact]
    public void PointerArithmeticDisplacesByStride()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var words: unsafe/i64 = null
                unsafe => words = bytes@unsafe/i64
                var third: unsafe/i64 = null
                let count: isize = 3
                unsafe => third = words + count
                var start: usize = 0
                var moved: usize = 0
                unsafe => start = words@usize
                unsafe => moved = third@usize
                require moved - start == 24 else => $abort("plus")
                unsafe => third -= 2
                unsafe => moved = third@usize
                require moved - start == 8 else => $abort("minus")
                unsafe => third += 2
                unsafe => moved = third@usize
                require moved - start == 24 else => $abort("compound plus")
                unsafe => third -= 2
                var back: unsafe/i64 = null
                unsafe => back = third - 1
                require back == words else => $abort("back")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("arithmetic ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerArithmetic", source, "arithmetic ok\n");
        Assert.Matches(@"= getelementptr i8, ptr %v\d+, i64 %", ir);
        Assert.DoesNotContain("getelementptr inbounds i8", ir);
    }

    [Theory]
    [InlineData("let p: unsafe/i32 = null\nlet n: i32 = 1\nlet q = unsafe => p + n")]
    [InlineData("var p: unsafe/i32 = null\nlet n: i32 = 1\nunsafe => p += n")]
    [InlineData("var p: unsafe/i32 = null\nunsafe => p *= 2")]
    [InlineData("let p: unsafe/() = null\nlet q = unsafe => p + 1")]
    [InlineData("let p: unsafe/i32 = null\nlet q = unsafe => 1 + p")]
    [InlineData("let p: unsafe/i32 = null\nlet q: unsafe/i32 = null\nlet d = unsafe => p - q")]
    public void InvalidPointerArithmeticIsRejected(string body)
    {
        var c = MinimalEmissionTest.Analyze(body);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.ControlFlow!.Issues.Count == 0);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
    }

    [Theory]
    [InlineData("let n: i32 = 1\nvar v: i32 = 0\nunsafe => v = *n", DiagnosticCode.TypeMismatch_Kd)]
    public void DereferenceBindsToThePointeeButDoesNotGenerateYet(string body, DiagnosticCode? binding)
    {
        var c = MinimalEmissionTest.Analyze(body);
        if (binding is { } code)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == code);
        }
        else
        {
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Assert.Equal(1, c.Ownership.Result.UnsupportedCount);
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void DereferenceRequiresAnUnsafeContext()
    {
        var c = MinimalEmissionTest.Analyze("let p: unsafe/i32 = null\nvar v: i32 = 0\nv = *p");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.ControlFlow!.Issues, x => x.Message.Contains("Unsafe", StringComparison.Ordinal));
    }

    [Fact]
    public void PointerReadsLoadThePointee()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var words: unsafe/i64 = null
                unsafe => words = bytes@unsafe/i64
                var value: i64 = 1
                unsafe => value = *(words + 3)
                require value == 0 else => $abort("zeroed")
                unsafe => *(words + 3) = 1234567890123
                unsafe => value = *(words + 3)
                require value == 1234567890123 else => $abort("written")
                var link: unsafe/u8 = bytes
                var links: unsafe/unsafe/u8 = null
                unsafe => links = bytes@unsafe/unsafe/u8
                unsafe => link = *links
                require link == null else => $abort("pointer")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("read ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerRead", source, "read ok\n");
        Assert.Matches(@"= load i64, ptr %v\d+, align 8\n", ir);
        Assert.Matches(@"= load ptr, ptr %v\d+, align 8\n", ir);
        Assert.Matches(@"\n  store i64 1234567890123, ptr %v\d+, align 8\n", ir);
    }

    [Fact]
    public void PointerCompoundAssignmentEvaluatesTheAddressAndOldValueOnce()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func target(p: unsafe/i32) -> unsafe/i32
                Console.writeLine("target")
                return p
            unsafe func amount(p: unsafe/i32) -> i32
                Console.writeLine("amount")
                unsafe => *p = 100
                return 3
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var p: unsafe/i32 = null
                unsafe => p = bytes@unsafe/i32
                unsafe => *p = 10
                unsafe => *target(p) += amount(p)
                var value: i32 = 0
                unsafe => value = *p
                require value == 13 else => $abort("read before rhs")
                unsafe => *p -= 1
                unsafe => *p *= 5
                unsafe => *p /= 4
                unsafe => *p %= 8
                unsafe => *p &= 6
                unsafe => *p |= 8
                unsafe => *p ^= 2
                unsafe => *p <<= 2
                unsafe => *p >>= 1
                unsafe => value = *p
                require value == 24 else => $abort("operators")
                var links: unsafe/unsafe/i32 = null
                unsafe => links = (bytes + 16)@unsafe/unsafe/i32
                unsafe => *links = p
                unsafe => *links += 2
                unsafe => *links -= 1
                var link: unsafe/i32 = null
                unsafe => link = *links
                var expected: unsafe/i32 = null
                unsafe => expected = p + 1
                require link == expected else => $abort("pointer update")
                var floats: unsafe/f64 = null
                unsafe => floats = (bytes + 32)@unsafe/f64
                unsafe => *floats = 1.5
                unsafe => *floats *= 2.0
                var real: f64 = 0.0
                unsafe => real = *floats
                require real == 3.0 else => $abort("float update")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("compound ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerCompound", source, "target\namount\ncompound ok\n");
    }

    [Theory]
    [InlineData("Overflow", "2147483647", "+= 1", "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("DivideZero", "12", "/= 0", "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero")]
    [InlineData("Shift", "12", "<<= -1", "KIMI_E_INT_SHIFT_COUNT: Shift count out of range")]
    public void PointerCompoundAssignmentPreservesArithmeticChecks(string name, string initial, string update, string reason)
    {
        var source = $$"""
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/i32, size: u64, kind: u32, protect: u32) -> unsafe/i32
            public func main()
                var p: unsafe/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => *p = {{initial}}
                unsafe => *p {{update}}
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerCompound" + name, source, string.Empty, 1, $"Hello.kimi:8:15: abort {reason}\n");
    }

    [Fact]
    public void PointerCompoundAssignmentDoesNotWriteAfterAnRhsReturn()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/i32, size: u64, kind: u32, protect: u32) -> unsafe/i32
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/i32, size: u64, kind: u32) -> i32
            unsafe func early(p: unsafe/i32)
                unsafe
                    *p += do => return
            public func main()
                var p: unsafe/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => *p = 5
                unsafe => early(p)
                var value: i32 = 0
                unsafe => value = *p
                require value == 5 else => $abort("unexpected write")
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("return ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerCompoundReturn", source, "return ok\n");
    }

    [Fact]
    public void BooleanPointersUseByteStorageAndBooleanComputation()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var flags: unsafe/bool = null
                unsafe => flags = bytes@unsafe/bool
                var flag = true
                unsafe => flag = *flags
                require not flag else => $abort("zero false")
                unsafe => *flags = not flag
                var byte: u8 = 0
                unsafe => byte = *bytes
                require byte == 1 else => $abort("stored true")
                unsafe => flag = *flags
                require flag else => $abort("loaded true")
                unsafe => *(flags + 1) = false
                unsafe => *(flags + 2) = true
                unsafe => byte = *(bytes + 1)
                require byte == 0 else => $abort("stored false")
                unsafe => byte = *(bytes + 2)
                require byte == 1 else => $abort("stride")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("bool ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerBool", source, "bool ok\n");
        Assert.Contains(" = trunc i8 %storage", ir);
        Assert.Contains(" = zext i1 ", ir);
        Assert.DoesNotContain("load i1,", ir);
    }

    [Theory]
    [InlineData("func update(p: unsafe/bool, n: unsafe/i32)\n    unsafe\n        *p = not *p\n        *n += 1\npublic func main() => ()")]
    [InlineData("struct R\n    public var n: i32\n    deinit => ()\nfunc take(p: unsafe/R) -> R\n    unsafe => return *p\npublic func main() => ()")]
    [InlineData("struct R\n    public var n: i32\n    deinit => ()\nfunc update(p: unsafe/R, value: R)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("func update(p: unsafe/string, value: string)\n    unsafe => *p = value@move\nfunc take(p: unsafe/string) -> string\n    unsafe => return *p\npublic func main() => ()")]
    [InlineData("enum E\n    Empty\n    Value(string)\nfunc update(p: unsafe/E, value: E)\n    unsafe => *p = value@move\nfunc take(p: unsafe/E) -> E\n    unsafe => return *p\npublic func main() => ()")]
    [InlineData("enum E<T>\n    Empty\n    Value(T)\nfunc update(p: unsafe/E<E<string>>, value: E<E<string>>)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("#Layout(\"C\")\nstruct R\n    public var a: u8\n    public var b: u64\n    deinit => ()\nfunc update(p: unsafe/R, value: R)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("func update(p: unsafe/(i32, i64))\n    unsafe\n        let value = *p\n        *p = value\npublic func main() => ()")]
    [InlineData("struct P\n    public var a: u8\n    public var b: (i32, [2 of u16])\nfunc update(p: unsafe/P)\n    unsafe\n        (*p).b.1[1] += 1\n        let a = (*p).a\n        p[1].b.0 = 3\npublic func main() => ()")]
    [InlineData("func compare(p: unsafe/string, h: unsafe/(string, i32))\n    unsafe\n        let a = *p == \"x\"\n        let b = (*h).0 < p[1]\npublic func main() => ()")]
    public void WarmPointerReadWriteAnalysisAndEmissionAllocateNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var success = true;
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void PointerIndexingUsesSignedStrideAndAssignmentOrder()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/i64, size: u64, kind: u32, protect: u32) -> unsafe/i64
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/i64, size: u64, kind: u32) -> i32
            func target(p: unsafe/i64) -> unsafe/i64
                Console.writeLine("target")
                return p
            func index() -> isize
                Console.writeLine("index")
                return -1
            func amount() -> i64
                Console.writeLine("amount")
                return 9
            unsafe func skipIndex(p: unsafe/i64)
                unsafe => p[do => return] += amount()
            unsafe func skipRight(p: unsafe/i64)
                unsafe => target(p)[index()] = do => return
            public func main()
                var p: unsafe/i64 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var next: unsafe/i64 = null
                unsafe => next = p + 1
                unsafe => target(next)[index()] = amount()
                unsafe => target(next)[index()] += amount()
                var value: i64 = 0
                unsafe => value = p[0]
                require value == 18 else => $abort("result")
                unsafe => value = next[-1]
                require value == 18 else => $abort("negative")
                let offset: isize = 2
                unsafe => next[offset] = 45
                unsafe => value = p[3]
                require value == 45 else => $abort("stride")
                var chosen = p
                unsafe
                    *chosen += change: do
                        chosen = next
                        require chosen == next else => $abort("changed")
                        exit to change: 2
                unsafe => value = p[0]
                require value == 20 else => $abort("secured address")
                chosen = p
                unsafe
                    value = chosen[offset: do
                        chosen = next
                        require chosen == next else => $abort("changed")
                        exit to offset: 0
                    ]
                require value == 20 else => $abort("secured index base")
                unsafe => skipIndex(p)
                unsafe => skipRight(p)
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("index ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerIndex", source, "amount\ntarget\nindex\ntarget\nindex\namount\nindex ok\n");
        Assert.DoesNotContain("getelementptr inbounds i8", ir);
        Assert.DoesNotContain("call void @__kimi_abort(i32 " + WindowsLowering.IndexBoundsReason, ir);
    }

    [Theory]
    [InlineData("let p: unsafe/i32 = null\nlet n: i32 = 1\nunsafe\n    let v = p[n]")]
    [InlineData("let p: unsafe/i32 = null\nunsafe\n    let v = p[^1]")]
    [InlineData("let p: unsafe/i32 = null\nunsafe\n    let v = p[0..1]")]
    [InlineData("let p: unsafe/i32 = null\nunsafe\n    let v = p[0...1]")]
    [InlineData("let p: unsafe/i32 = null\nunsafe\n    let v = p[true]")]
    [InlineData("let p: unsafe/() = null\nunsafe\n    let v = p[0]")]
    public void PointerIndexingRejectsInvalidOffsets(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("var v: i32 = p[0]")]
    [InlineData("p[0] = 1")]
    [InlineData("p[0] += 1")]
    public void PointerIndexingRequiresAnUnsafeContext(string body)
    {
        var c = MinimalEmissionTest.Analyze("let p: unsafe/i32 = null\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.ControlFlow!.Issues, x => x.Message.Contains("Unsafe", StringComparison.Ordinal));
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
    }

    [Theory]
    [InlineData("let q = p + 1")]
    [InlineData("let q = p - 1")]
    [InlineData("p += 1")]
    [InlineData("p -= 1")]
    public void BoundPointerArithmeticRequiresAnUnsafeContext(string body)
    {
        var c = MinimalEmissionTest.Analyze("var p: unsafe/i32 = null\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.ControlFlow!.Issues, x => x.Message.Contains("Unsafe", StringComparison.Ordinal));
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
    }

    [Fact]
    public void PointerIncrementsPreservePrefixPostfixResultsAndSingleEvaluation()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/i32, size: u64, kind: u32, protect: u32) -> unsafe/i32
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/i32, size: u64, kind: u32) -> i32
            func target(p: unsafe/i32) -> unsafe/i32
                Console.writeLine("target")
                return p
            func index() -> isize
                Console.writeLine("index")
                return 0
            public func main()
                var p: unsafe/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => *p = 5
                var a: i32 = 0
                var b: i32 = 0
                var c: i32 = 0
                var d: i32 = 0
                unsafe => a = (*target(p))++
                unsafe => b = ++*target(p)
                unsafe => c = p[index()]--
                unsafe => d = --p[index()]
                var value: i32 = 0
                unsafe => value = *p
                require a == 5 and b == 7 and c == 7 and d == 5 and value == 5 else => $abort("increment result")
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("increment ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerIncrement", source, "target\ntarget\nindex\nindex\nincrement ok\n");
    }

    [Theory]
    [InlineData("Signed", "i8", "-128", "(*p)--")]
    [InlineData("Unsigned", "u64", "18446744073709551615", "++p[0]")]
    public void PointerIncrementsCheckOverflow(string name, string type, string initial, string operation)
    {
        var source = $$"""
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/{{type}}, size: u64, kind: u32, protect: u32) -> unsafe/{{type}}
            public func main()
                var p: unsafe/{{type}} = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => *p = {{initial}}
                unsafe => {{operation}}
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerIncrement" + name, source, string.Empty, 1, "Hello.kimi:8:15: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData("i32", "p++")]
    [InlineData("f64", "(*p)++")]
    [InlineData("bool", "++p[0]")]
    public void PointerIncrementsRequireIntegerPointees(string type, string operation)
    {
        var c = MinimalEmissionTest.Analyze($"var p: unsafe/{type} = null\nunsafe => {operation}");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void CopyAggregatePointersTransferCompleteValues()
    {
        var source = """
            struct Pair
                Self is Copy
                public var first: u8
                public var second: u64
                public var flag: bool
                public init(first: u8, second: u64, flag: bool)
                    self.first = first
                    self.second = second
                    self.flag = flag
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            unsafe func snapshot(p: unsafe/Pair) -> Pair
                unsafe => return *p
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var pairs: unsafe/Pair = null
                unsafe => pairs = bytes@unsafe/Pair
                unsafe
                    let zero = *pairs
                    require zero.first == 0 and zero.second == 0 and not zero.flag else => $abort("zero")
                    *pairs = Pair.init(7, 1234567890123, true)
                    let old = snapshot(pairs)
                    *pairs = Pair.init(9, 11, false)
                    pairs[1] = old
                    let first = pairs[0]
                    let second = pairs[1]
                    require first.first == 9 and first.second == 11 and not first.flag else => $abort("replace")
                    require second.first == 7 and second.second == 1234567890123 and second.flag else => $abort("copy")
                    require old.first == 7 else => $abort("retained")
                var tuples: unsafe/(u8, (u64, bool)) = null
                unsafe => tuples = (bytes + 64)@unsafe/(u8, (u64, bool))
                unsafe
                    *tuples = (3, (99, true))
                    let tuple = *tuples
                    require tuple.0 == 3 and tuple.1.0 == 99 and tuple.1.1 else => $abort("tuple")
                var arrays: unsafe/[3 of i16] = null
                unsafe => arrays = (bytes + 128)@unsafe/[3 of i16]
                unsafe
                    arrays[0] = [4, 5, 6]
                    let array = arrays[0]
                    arrays[1] = array
                    let second = arrays[1]
                    require array[0] == 4 and second[1] == 5 and second[2] == 6 else => $abort("array")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("aggregate ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerAggregate", source, "aggregate ok\n");
    }

    [Fact]
    public void PointerSubplacesAccessOnlyTheirStoredParts()
    {
        var source = """
            #Layout("C")
            struct Wide
                public var head: u8
                public var tail: u64
            struct Pair
                Self is Copy
                public var first: u8
                public var second: u64
            struct Holder
                public var label: string
                public var count: i32
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func pick(p: unsafe/Pair) -> unsafe/Pair
                Console.writeLine("pick")
                return p
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var pairs: unsafe/Pair = null
                unsafe => pairs = bytes@unsafe/Pair
                unsafe
                    (*pairs).second = 1234567890123
                    (*pairs).first = 7
                    pairs[1].first = 9
                    (*pick(pairs)).second += 5
                    pairs[1].second = (*pairs).second
                    (*pairs).first++
                    let whole = *pairs
                    let next = pairs[1]
                    require whole.first == 8 and whole.second == 1234567890128 else => $abort("fields")
                    require next.first == 9 and next.second == 1234567890128 else => $abort("indexed")
                var wide: unsafe/Wide = null
                unsafe => wide = (bytes + 64)@unsafe/Wide
                unsafe
                    (*wide).tail = 3
                    (*wide).head = 1
                    let tail = *((bytes + 72)@unsafe/u64)
                    let head = *(bytes + 64)
                    require tail == 3 and head == 1 and (*wide).tail == 3 else => $abort("c layout")
                var tuples: unsafe/(u8, (u64, bool)) = null
                unsafe => tuples = (bytes + 128)@unsafe/(u8, (u64, bool))
                unsafe
                    (*tuples).1.0 = 99
                    (*tuples).1.1 = true
                    (*tuples).0 = 3
                    let tuple = *tuples
                    require tuple.0 == 3 and tuple.1.0 == 99 and tuple.1.1 and (*tuples).1.0 == 99 else => $abort("tuple")
                var arrays: unsafe/[3 of i16] = null
                unsafe => arrays = (bytes + 192)@unsafe/[3 of i16]
                unsafe
                    (*arrays)[2] = -4
                    arrays[1][0] = 5
                    (*arrays)[0] = 6
                    arrays[1][0] -= 1
                    let array = *arrays
                    require array[0] == 6 and array[1] == 0 and array[2] == -4 and arrays[1][0] == 4 else => $abort("array")
                var holders: unsafe/Holder = null
                unsafe => holders = (bytes + 256)@unsafe/Holder
                unsafe
                    // Zeroed handles are valid empty Static strings (SPEC 22.5.5).
                    (*holders).label = "kept"
                    (*holders).count = 41
                    (*holders).count += 1
                    require (*holders).count == 42 else => $abort("count")
                    let taken = (*holders).label
                    Console.writeLine(taken)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("subplace ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerSubplace", source, "pick\nkept\nsubplace ok\n");
        Assert.Matches(@"= getelementptr i8, ptr %v\d+, i64 8\n", ir);
    }

    [Fact]
    public void ComputedPointerSubplaceIndicesAreCheckedAndOrdered()
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func pick(p: unsafe/(u8, [3 of i32])) -> unsafe/(u8, [3 of i32])
                Console.writeLine("pick")
                return p
            func at(i: isize) -> isize
                Console.writeLine("index")
                return i
            func value(n: i32) -> i32
                Console.writeLine("value")
                return n
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var p: unsafe/(u8, [3 of i32]) = null
                unsafe => p = bytes@unsafe/(u8, [3 of i32])
                var grid: unsafe/[2 of [2 of u16]] = null
                unsafe => grid = (bytes + 64)@unsafe/[2 of [2 of u16]]
                unsafe
                    (*pick(p)).1[at(2)] = value(7)
                    (*pick(p)).1[at(1)] += value(5)
                    p[0].1[at(0)] = (*p).1[2] * 3
                    var i: isize = 0
                    while i < 2
                        var j: isize = 0
                        while j < 2
                            grid[0][i][j] = (i * 2 + j)@u16
                            j += 1
                        i += 1
                    grid[0][1][0]++
                    let tuple = *p
                    let square = *grid
                    require tuple.1[0] == 21 and tuple.1[1] == 5 and tuple.1[2] == 7 else => $abort("tuple")
                    require square[0][1] == 1 and square[1][0] == 3 and square[1][1] == 3 else => $abort("grid")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("checked ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerSubplaceIndex", source, "value\npick\nindex\npick\nindex\nvalue\nindex\nchecked ok\n");
    }

    [Theory]
    [InlineData("High", "3")]
    [InlineData("Negative", "-1")]
    public void ComputedPointerSubplaceIndicesAbortOutsideTheFixedLength(string name, string index)
    {
        var source = $$"""
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
            public func main()
                var p: unsafe/[3 of i32] = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)@unsafe/[3 of i32]
                var i: isize = {{index}}
                unsafe => (*p)[i] = 1
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerSubplaceBounds" + name, source, string.Empty, 1, "Hello.kimi:8:15: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Theory]
    [InlineData("position", false)]
    [InlineData("constant", true)]
    [InlineData("container", false)]
    [InlineData("index", true)]
    public void IncompletePointerProjectionPlansRejectBeforeWriting(string mutation, bool computed)
    {
        var place = computed ? "(*p).1[i]" : "(*p).1[1]";
        var c = MinimalEmissionTest.Analyze($"func update(p: unsafe/(u8, [2 of i16]), i: isize)\n    unsafe => {place} = 1\npublic func main() => ()");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "update");
        var projection = body.Values.FindLastIndex(x => x.Kind == OwnershipValueKind.PointerProject);
        var outer = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.PointerProject);
        Assert.True(projection > outer && outer >= 0);
        if (mutation == "position")
        {
            body.Values[projection] = body.Values[projection] with { Constant = 2 };
        }
        else if (mutation == "constant")
        {
            body.Values[projection] = body.Values[projection] with { Constant = 0 };
        }
        else if (mutation == "container")
        {
            // The array element's container must be the array, not the enclosing Tuple.
            body.ValueOperands[body.Values[projection].Start] = body.ValueOperands[body.Values[outer].Start];
        }
        else
        {
            body.ValueOperands[body.Values[projection].Start + 1] = body.ValueOperands[body.Values[projection].Start];
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void DependentPointerReadsRemainUnsupported(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func read(p: unsafe/({type}))\n    unsafe\n        let value = *p\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.UnsupportedCount > 0);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("string")]
    [InlineData("(string, i32)")]
    [InlineData("[2 of string]")]
    public void OwnedAggregatePointerWritesHaveCompleteCleanupPlans(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func update(p: unsafe/{type}, value: {type})\n    unsafe => *p = value@move\npublic func main() => ()");
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StringPointersMoveAndReplaceCompleteHandles(bool consume)
    {
        var source = """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/string, size: u64, kind: u32, protect: u32) -> unsafe/string
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/string, size: u64, kind: u32) -> i32
            func take(p: unsafe/string) -> string
                unsafe => return *p
            func use(p: unsafe/string, flag: bool)
                unsafe
                    // Zeroed handles are valid empty Static strings (SPEC 22.5.5).
                    *p = "first"
                    p[0] = "second"
                    Console.writeLine(take(p))
                    p[1] = "third"
                    let moved = p[1]
                    if flag => Console.writeLine(moved)
                    p[2] = "compare"
                    let compared = p[2]
                    require compared == "compare" else => $abort("comparison")
                    let skipped = false and take(p + 3) == "skip"
                    require not skipped else => $abort("short circuit")
                    Console.writeLine("done")
            public func main()
                var p: unsafe/string = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                use(p, true)
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
            """;
        source = consume ? source : source.Replace("use(p, true)", "use(p, false)", StringComparison.Ordinal);
        var name = consume ? "ForeignPointerString" : "ForeignPointerStringRetained";
        var stdout = consume ? "second\nthird\ndone\n" : "second\ndone\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, stdout, "=3;first=1;second=1;third=1;compare=2;skip=0;done=1");
    }

    [Theory]
    [InlineData("*p")]
    [InlineData("p[0]")]
    public void StringPointerComparisonsDoNotInventAnOwningRead(string expression)
    {
        // SPEC 5.2: the handle is inspected in place; no temporary owner, Move or cleanup.
        var c = MinimalEmissionTest.Analyze($"func compare(p: unsafe/string)\n    unsafe\n        let equal = {expression} == \"text\"\npublic func main() => ()");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "compare");
        Assert.DoesNotContain(body.Values, x => x.Kind == OwnershipValueKind.PointerLoad);
        Assert.DoesNotContain(body.Places, x => ReferenceEquals(x.Type, BoundType.String) && x.Source.ToString() == expression);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Fact]
    public void RawStringComparisonsInspectHandlesInPlace()
    {
        var source = """
            struct Holder
                public var label: string
                public var count: i32
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/string, size: u64, kind: u32, protect: u32) -> unsafe/string
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/string, size: u64, kind: u32) -> i32
            public func main()
                var p: unsafe/string = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var holder: unsafe/Holder = null
                unsafe => holder = (p@unsafe/u8 + 256)@unsafe/Holder
                unsafe
                    *p = "alpha"
                    p[1] = "beta"
                    (*holder).label = "kept"
                    require *p == "alpha" and p[1] != "alpha" else => $abort("equal")
                    require p[1] > *p and "beta" == p[1] and not (p[1] < *p) else => $abort("order")
                    require (*holder).label >= p[1] and (*holder).label == "kept" else => $abort("field")
                    let a = *p
                    let b = p[1]
                    let k = (*holder).label
                    Console.writeLine(a)
                    Console.writeLine(b)
                    Console.writeLine(k)
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerStringCompare", source, "alpha\nbeta\nkept\ndone\n");
        StringEmissionTest.WriteAuditedFixture("ForeignPointerStringCompare", source, ir, "alpha\nbeta\nkept\ndone\n", "=3;alpha=3;beta=2;kept=2;done=1");
    }

    [Fact]
    public void EnumPointersPreserveTagsAndActivePayloadOwnership()
    {
        var source = """
            enum E<T>
                Empty
                Value(T)
            struct R
                public var n: i32
                public init(n: i32) => self.n = n
                deinit
                    if self.n == 1 => Console.writeLine("one")
                    else => Console.writeLine("two")
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func take(p: unsafe/E<R>) -> E<R>
                unsafe => return *p
            func values(bytes: unsafe/u8)
                unsafe
                    let p = bytes@unsafe/E<R>
                    // Tag zero denotes Empty, so no payload is read or destroyed.
                    *p = .Value(R.init(1))
                    p[0] = .Value(R.init(2))
                    match take(p)
                        .Empty => $abort("tag")
                        .Value(let value)
                            require value.n == 2 else => $abort("payload")
                            Console.writeLine("value")
                    let text = (bytes + 64)@unsafe/E<string>
                    *text = .Value("first")
                    *text = .Empty
                    text[1] = .Value("second")
                    let retained = text[1]
                    let aligned = (bytes + 192)@unsafe/E<u128>
                    aligned[1] = .Value(340282366920938463463374607431768211455)
                    let wide = aligned[1]
                    match wide@move
                        .Empty => $abort("wide tag")
                        .Value(let value)
                            require value == 340282366920938463463374607431768211455 else => $abort("wide value")
                    let nested = (bytes + 320)@unsafe/E<E<string>>
                    *nested = .Value(.Value("nested"))
                    let nestedResult = *nested
                    Console.writeLine("done")
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                values(bytes)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerEnum", source, "one\nvalue\ntwo\ndone\n");
        StringEmissionTest.WriteAuditedFixture("ForeignPointerEnum", source, ir, "one\nvalue\ntwo\ndone\n", "one=1;two=1;value=1;first=1;second=1;nested=1;done=1");
    }

    [Fact]
    public void NonCopyPointerReplacementDestroysOldValueAndTransfersNewOwner()
    {
        var source = """
            struct Resource
                public var value: i32
                public init(value: i32) => self.value = value
                deinit
                    if self.value == 0 => Console.writeLine("drop zero")
                    else if self.value == 1 => Console.writeLine("drop one")
                    else => Console.writeLine("drop two")
            struct Empty
                public init() => ()
                deinit => Console.writeLine("empty")
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func make() -> Resource
                Console.writeLine("rhs")
                return Resource.init(1)
            func target(p: unsafe/Resource) -> unsafe/Resource
                Console.writeLine("target")
                return p
            func early(p: unsafe/Resource)
                unsafe
                    p[do => return] = make()
            func replace(bytes: unsafe/u8)
                unsafe
                    let p = bytes@unsafe/Resource
                    *target(p) = make()
                    Console.writeLine("stored")
                    p[0] = if true => Resource.init(2) else => Resource.init(1)
                    let result = *p
                    require result.value == 2 else => $abort("value")
                    let empty = (bytes + 16)@unsafe/Empty
                    *empty = Empty.init()
                    let emptyResult = *empty
                    let tuples = (bytes + 32)@unsafe/(Resource, Resource)
                    *tuples = (Resource.init(1), Resource.init(2))
                    let tuple = *tuples
                    let stopped = (bytes + 64)@unsafe/Resource
                    early(stopped)
                    let untouched = *stopped
                    require untouched.value == 0 else => $abort("early store")
                    Console.writeLine("scope")
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                replace(bytes)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerOwnedReplace", source, "rhs\ntarget\ndrop zero\nstored\ndrop one\nempty\ndrop zero\ndrop zero\nrhs\ndrop one\nscope\ndrop zero\ndrop two\ndrop one\nempty\ndrop two\ndone\n");
    }

    [Fact]
    public void NonCopyAggregatePointerReadsAcquireOneOwner()
    {
        var source = """
            struct Resource
                public var value: i32
                deinit
                    require self.value == 0 else => $abort("value")
                    Console.writeLine("drop")
            struct Empty
                deinit => Console.writeLine("empty")
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func take(p: unsafe/Resource) -> Resource
                unsafe => return *p
            func use(value: Resource)
                Console.writeLine("use")
            func choose(p: unsafe/Resource, flag: bool) -> Resource
                unsafe
                    if flag => return p[0]
                    return p[1]
            func early(p: unsafe/Resource)
                unsafe
                    let value = *p
                    defer => Console.writeLine("defer")
                    return
            func reads(bytes: unsafe/u8)
                unsafe
                    // VirtualAlloc's zero bytes are valid initialized scalar fields.
                    // Each Non-Copy source is read exactly once; no raw owner is destroyed.
                    let first = take(bytes@unsafe/Resource)
                    use(first@move)
                    let tuple = *((bytes + 16)@unsafe/(Resource, Resource))
                    let array = ((bytes + 32)@unsafe/[2 of Resource])[0]
                    let empty = *((bytes + 64)@unsafe/Empty)
                    use(choose((bytes + 80)@unsafe/Resource, true))
                    use(choose((bytes + 96)@unsafe/Resource, false))
                    early((bytes + 112)@unsafe/Resource)
                    *((bytes + 128)@unsafe/Resource)
                    Console.writeLine("scope")
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                reads(bytes)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerOwnedRead", source, "use\ndrop\nuse\ndrop\nuse\ndrop\ndefer\ndrop\ndrop\nscope\nempty\ndrop\ndrop\ndrop\ndrop\ndone\n");
    }

    [Fact]
    public void MovingAPointerReadValuePreventsItsReuse()
    {
        var c = MinimalEmissionTest.Analyze("struct R\n    public var n: i32\n    deinit => ()\nfunc test(p: unsafe/R)\n    unsafe\n        let value = *p\n        let moved = value@move\n        let again = value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.ErrorCount > 0);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void PointerReplacementConsumesTheSourceOwner()
    {
        var c = MinimalEmissionTest.Analyze("struct R\n    public var n: i32\n    deinit => ()\nfunc test(p: unsafe/R, value: R)\n    unsafe => *p = value@move\n    let again = value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.ErrorCount > 0);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void AbortingPointerReplacementDoesNotContinueOrUnwind()
    {
        var source = """
            struct R
                public var n: i32
                public init(n: i32) => self.n = n
                deinit
                    if self.n == 0 => $abort("old")
                    Console.writeLine("new cleanup")
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/R, size: u64, kind: u32, protect: u32) -> unsafe/R
            public func main()
                var p: unsafe/R = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                let value = R.init(1)
                Console.writeLine("rhs")
                unsafe => *p = value@move
                Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerOwnedAbort", source, "rhs\n", 1, "Hello.kimi:5:27: abort KIMI_E_ABORT: old\n");
    }

    [Fact]
    public void ZeroSizedPointerAccessRetainsEvaluationWithoutReadingBytes()
    {
        var source = """
            struct Empty
                Self is Copy
                public init() => ()
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            func target(p: unsafe/()) -> unsafe/()
                Console.writeLine("target")
                return p
            func value()
                Console.writeLine("value")
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var units: unsafe/() = null
                unsafe => units = bytes@unsafe/()
                unsafe
                    let unit = *target(units)
                    *target(units) = value()
                var empty: unsafe/Empty = null
                unsafe => empty = bytes@unsafe/Empty
                unsafe
                    let item = *empty
                    *empty = item
                var arrays: unsafe/[0 of i32] = null
                unsafe => arrays = bytes@unsafe/[0 of i32]
                unsafe
                    let array = *arrays
                    *arrays = array
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("zero size ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerZeroSize", source, "target\nvalue\ntarget\nzero size ok\n");
    }

    [Theory]
    [InlineData("address")]
    [InlineData("source")]
    [InlineData("acquisition")]
    [InlineData("operation")]
    public void IncompletePointerAggregatePlansRejectBeforeWriting(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("func update(p: unsafe/(i32, i64))\n    unsafe => *p = (1, 2)\npublic func main() => ()");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "update");
        var store = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.PointerStore);
        Assert.True(store >= 0);
        if (mutation == "address")
        {
            body.ValueOperands[body.Values[store].Start] = body.Values.Count;
        }
        else if (mutation == "acquisition")
        {
            body.OperationStorage[store] = body.Operations[store] with { Acquisition = AcquisitionKind.None };
        }
        else if (mutation == "operation")
        {
            body.OperationStorage[store] = body.Operations[store] with { Kind = OwnershipOperationKind.Produce };
        }
        else
        {
            body.Values[store] = body.Values[store] with { Constant = -1 };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void PointerTransfersUseTheCLayoutRepresentation()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct Pair\n    Self is Copy\n    public var a: u8\n    public var b: u64\nfunc read(p: unsafe/Pair)\n    unsafe\n        let value = *p\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Contains("{ i8, i64 }", writer.ToString());
    }

    [Fact]
    public void ExplicitKimigayoLayoutIsMetadataAndUsesTheDefaultPhysicalOrder()
    {
        var source = """
            #Layout("Kimigayo")
            struct Pair
                Self is Copy
                public var a: u8
                public var b: u64
                public init(a: u8, b: u64)
                    self.a = a
                    self.b = b
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/Pair, size: u64, kind: u32, protect: u32) -> unsafe/Pair
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/Pair, size: u64, kind: u32) -> i32
            public func main()
                var p: unsafe/Pair = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe
                    *p = Pair.init(7, 42)
                    let value = *p
                    require value.a == 7 and value.b == 42 else => $abort("fields")
                    require *(p@unsafe/u64) == 42 else => $abort("order")
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("layout")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerExplicitKimigayoLayout", source, "layout\n");
    }

    [Theory]
    [InlineData("()")]
    [InlineData("((), ())")]
    [InlineData("[0 of i32]")]
    [InlineData("[3 of ()]")]
    [InlineData("Empty")]
    [InlineData("Nested")]
    [InlineData("Derived")]
    [InlineData("Box<()>")]
    [InlineData("[2 of Box<Empty>]")]
    public void ZeroStridePointerOperationsAreLanguageErrors(string type)
    {
        foreach (var operation in new[] { "p + 0", "p - 0", "p += 0", "p -= 0", "p[0]" })
        {
            var c = MinimalEmissionTest.Analyze($"open struct Empty\nstruct Nested\n    public var value: (Empty, [4 of ()])\nstruct Derived: Empty\nstruct Box<T>\n    public var value: T\nfunc test()\n    var p: unsafe/{type} = null\n    unsafe => {operation}\npublic func main() => ()");
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
            using var writer = new StringWriter();
            Assert.False(c.Emission.WriteIr(writer, out _));
            Assert.Empty(writer.ToString());
        }
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("((), i32)")]
    [InlineData("[1 of i32]")]
    [InlineData("Box<i32>")]
    [InlineData("Derived")]
    [InlineData("unsafe/()")]
    public void NonzeroPointerStrideIsNotRejected(string type)
    {
        var c = MinimalEmissionTest.Analyze($"open struct Base\n    public var value: i32\nstruct Derived: Base\nstruct Box<T>\n    public var value: T\nfunc test(p: unsafe/{type})\n    unsafe\n        let next = p + 1\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void SymbolicArrayLengthIsNotMistakenForZeroStride()
    {
        var c = MinimalEmissionTest.Analyze("func test<length N>(p: unsafe/[N of i32])\n    unsafe\n        let next = p + 1\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void CLayoutPointersPreserveWrittenOrderAndStride()
    {
        var source = """
            #Layout("C")
            struct Record
                Self is Copy
                public var a: u8
                public var b: u64
                public var c: u8
                public init(a: u8, b: u64, c: u8)
                    self.a = a
                    self.b = b
                    self.c = c
            struct Compact
                Self is Copy
                public var a: u8
                public var b: u64
                public var c: u8
            #Layout("C")
            struct Outer
                Self is Copy
                public var lead: u8
                public var record: Record
            #Layout("C")
            struct Generic<T>
                public var lead: u8
                public var value: T
            #Layout("C")
            struct Resource
                public var n: i32
                public init(n: i32) => self.n = n
                deinit
                    if self.n == 0 => Console.writeLine("old")
                    else => Console.writeLine("new")
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: unsafe/u8, size: u64, kind: u32, protect: u32) -> unsafe/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: unsafe/u8, size: u64, kind: u32) -> i32
            public func main()
                var bytes: unsafe/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                unsafe
                    let p = bytes@unsafe/Record
                    p[0] = Record.init(7, 42, 9)
                    p[1] = Record.init(11, 99, 13)
                    let value = p[1]
                    require value.a == 11 and value.b == 99 and value.c == 13 else => $abort("value")
                    require bytes[0] == 7 and bytes[16] == 9 and bytes[24] == 11 and bytes[40] == 13 else => $abort("C offsets")
                    require *((bytes + 8)@unsafe/u64) == 42 and *((bytes + 32)@unsafe/u64) == 99 else => $abort("C alignment")
                    let compact = (bytes + 64)@unsafe/Compact
                    let other = *compact
                    require (p + 1)@usize - p@usize == 24 else => $abort("C stride")
                    require (compact + 1)@usize - compact@usize == 16 else => $abort("Kimigayo stride")
                    let outer = (bytes + 128)@unsafe/Outer
                    bytes[136] = 21
                    let nested = *outer
                    require nested.record.a == 21 else => $abort("nested offset")
                    require (outer + 1)@usize - outer@usize == 32 else => $abort("nested stride")
                    let generic = (bytes + 192)@unsafe/Generic<u64>
                    bytes[200] = 23
                    let instantiated = *generic
                    require instantiated.value == 23 else => $abort("generic offset")
                    let resource = (bytes + 256)@unsafe/Resource
                    *resource = Resource.init(1)
                    let moved = *resource
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("C layout")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerCLayout", source, "old\nnew\nC layout\n");
        Assert.Contains("{ i8, i64, i8 }", ir);
    }

    [Theory]
    [InlineData("struct S")]
    [InlineData("open struct S\n    var n: i32")]
    [InlineData("struct S\n    var empty: ()")]
    [InlineData("struct S\n    var empty: [0 of i32]")]
    [InlineData("struct S\n    var empty: [2 of ()]")]
    public void InvalidCLayoutsCannotGenerate(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\n" + declaration + "\nfunc read(p: unsafe/S)\n    unsafe\n        let value = *p\npublic func main() => ()");
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    private static string Emit(string source, string kind)
    {
        var c = Analyze(source, kind);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        return writer.ToString();
    }

    private static Compilation Analyze(string source, string kind)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = kind } };
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        return c;
    }
}
