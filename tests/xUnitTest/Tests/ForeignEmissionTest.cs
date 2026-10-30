// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
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
        var module = GeneratedIr.Parse(ir);
        Assert.Equal(2, module.Startup.Instructions.Count(x => x.Callee == module.Function("GetLastError")));
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
        Assert.Equal(name, LlvmModuleText.ExternalName(symbol));
        Assert.Equal(symbol, LlvmModuleText.SymbolFromName(name));
    }

    [Fact]
    public void PointerSignaturesDeclareOpaquePointers()
    {
        var ir = Emit(
            "group Native\n    #LibraryImport(\"codec\", \"touch\")\n    public unsafe func touch(value: raw/i32, other: raw/f64) -> raw/u8\n" +
            "public func main()\n    unsafe => _ = Native.touch(null, null)",
            "static");
        Assert.Contains("declare ptr @touch(ptr, ptr)\n", ir);
    }

    [Fact]
    public void RebindingWithoutTheImportForgetsItsDeclaration()
    {
        var c = Analyze(
            "group Native\n    #LibraryImport(\"codec\", \"gone\")\n    public unsafe func gone() -> i32\npublic func main()\n    unsafe => _ = Native.gone()",
            "static");
        using (var writer = new StringWriter())
        {
            Assert.True(c.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
            Assert.Contains("declare i32 @gone()\n", writer.ToString());
        }

        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Native").Members.OfType<Kimi.Compiler.Parsing.FunctionKoto>().Single();
        Assert.Single(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha);
        Assert.True(function.RemoveAttribute(function.AttributeChain!));
        c.Bind();
        Assert.DoesNotContain(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha);
        using (var writer = new StringWriter())
        {
            Assert.False(c.WriteIr(writer, out _));
            Assert.Equal(string.Empty, writer.ToString());
        }
    }

    [Fact]
    public void PointerValuesPassThroughImportsAndCompare()
    {
        var source = $$"""
            {{Pages()}}
            public func main()
                var freed: i32 = 0
                var p: raw/u8 = null
                require p == null else => $abort("initial")
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                require p != null else => $abort("allocate")
                require null != p else => $abort("reversed")
                let q: raw/u8 = p
                require q == p else => $abort("copy")
                unsafe => freed = Native.free(q, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("pointer ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointer", source, "pointer ok\n");
        Assert.Contains("declare dllimport ptr @VirtualAlloc(ptr, i64, i32, i32)\n", ir);
        Assert.Contains("declare dllimport i32 @VirtualFree(ptr, i64, i32)\n", ir);
        Assert.Matches(@"= call ptr @VirtualAlloc\(ptr null, i64 4096, i32 12288, i32 4\)", ir);
        var body = GeneratedIr.Parse(ir).Startup.Instructions;
        Assert.Contains(body, static x => x.Shape == "icmp ne ptr %, null");
        Assert.Contains(body, static x => x.Shape == "icmp eq ptr %, %");
    }

    [Theory]
    [InlineData("let p: raw/i32 = null\nlet q: raw/u32 = null\nlet r = p == q", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let r = null == null", DiagnosticCode.UnresolvedBinding_Kd, DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("let p: raw/i32 = null\nlet r = p == null")]
    public void PointerEqualityRequiresOneKnownPointerType(string body, params DiagnosticCode[] codes)
    {
        var c = MinimalEmissionTest.Analyze(body);
        Assert.Equal(codes, c.Binding.Issues.Select(static x => x.Code));
        using var writer = new StringWriter();
        Assert.Equal(codes.Length == 0, c.WriteIr(writer, out _));
    }

    [Theory]
    [InlineData("let p: raw/i32 = null\nlet a = unsafe => p as usize")]
    public void UnimplementedPointerOperationsDoNotGenerate(string body)
    {
        var c = MinimalEmissionTest.Analyze(body);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void PointerConversionsPreserveAddresses()
    {
        var source = $$"""
            {{Pages()}}
            public func main()
                var p: raw/u8 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var address: usize = 0
                unsafe => address = p@usize
                require address != 0 else => $abort("address")
                require address % 4096 == 0 else => $abort("alignment")
                var back: raw/u8 = null
                unsafe => back = address@raw/u8
                require back == p else => $abort("round trip")
                var words: raw/i32 = null
                unsafe => words = p@raw/i32
                let same = words@raw/i32
                var bytes: raw/u8 = null
                unsafe => bytes = same@raw/u8
                require bytes == p else => $abort("pointer cast")
                var zero: raw/u8 = p
                unsafe => zero = 0@raw/u8
                require zero == null else => $abort("zero")
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("cast ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerCast", source, "cast ok\n");
        var body = GeneratedIr.Parse(ir).Startup.Instructions;
        Assert.Contains(body, static x => x.Shape == "ptrtoint ptr % to i64");
        Assert.Contains(body, static x => x.Shape == "inttoptr i64 % to ptr");
        Assert.Matches(@"= inttoptr i64 0 to ptr", ir);
    }

    [Theory]
    [InlineData("let p: raw/i32 = null\nlet a = unsafe => p@u64", true)]
    [InlineData("let n: i32 = 1\nlet p = unsafe => n@raw/u8", true)]
    [InlineData("let p: raw/i32 = null\nvar q: raw/u8 = null\nq = p@raw/u8", false)]
    [InlineData("let p: raw/i32 = null\nvar a: usize = 0\na = p@usize", false)]
    [InlineData("var a: usize = 4096\nvar p: raw/u8 = null\np = a@raw/u8", false)]
    [InlineData("let p = null@raw/i32\nvar q: raw/i32 = null\nq = p", false)]
    public void PointerConversionsRequireTheirTypesButNoUnsafeContext(string body, bool mismatch)
    {
        // SPEC 5, 5.4: conversions between raw pointer Types and usize, and Typed Null Formation, need no unsafe context.
        var c = MinimalEmissionTest.Analyze(body);
        Assert.Equal(mismatch, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.TypeMismatch_Kd));
        using var writer = new StringWriter();
        Assert.Equal(!mismatch, c.WriteIr(writer, out _));
    }

    [Fact]
    public void PointerArithmeticDisplacesByStride()
    {
        var source = $$"""
            {{Pages()}}
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var words: raw/i64 = null
                unsafe => words = bytes@raw/i64
                var third: raw/i64 = null
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
                var back: raw/i64 = null
                unsafe => back = third - 1
                require back == words else => $abort("back")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("arithmetic ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerArithmetic", source, "arithmetic ok\n");
        Assert.Contains(GeneratedIr.Parse(ir).Startup.Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 %");
        Assert.DoesNotContain("getelementptr inbounds i8", ir);
    }

    [Theory]
    [InlineData("let p: raw/i32 = null\nlet n: i32 = 1\nlet q = unsafe => p + n", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("var p: raw/i32 = null\nlet n: i32 = 1\nunsafe => p += n", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("var p: raw/i32 = null\nunsafe => p *= 2", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("let p: raw/i32 = null\nlet q = unsafe => 1 + p", DiagnosticCode.OperatorNotDefined_Kd)]
    [InlineData("let p: raw/i32 = null\nlet q: raw/i32 = null\nlet d = unsafe => p - q", DiagnosticCode.TypeMismatch_Kd)]
    public void InvalidPointerArithmeticIsRejected(string body, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(body);
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == code.ToString());
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
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
            Assert.Single(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        }

        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void DereferenceRequiresAnUnsafeContext()
    {
        var c = MinimalEmissionTest.Analyze("let p: raw/i32 = null\nvar v: i32 = 0\nv = *p");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.UnsafeBlockRequired_Kd));
    }

    [Fact]
    public void PointerReadsLoadThePointee()
    {
        var source = $$"""
            {{Pages()}}
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var words: raw/i64 = null
                unsafe => words = bytes@raw/i64
                unsafe => Raw.initialize(words + 3, 0)
                var value: i64 = 1
                unsafe => value = *(words + 3)
                require value == 0 else => $abort("placed")
                unsafe => *(words + 3) = 1234567890123
                unsafe => value = *(words + 3)
                require value == 1234567890123 else => $abort("written")
                var link: raw/u8 = bytes
                var links: raw/raw/u8 = null
                unsafe => links = bytes@raw/raw/u8
                unsafe => Raw.initialize(links, null)
                unsafe => link = *links
                require link == null else => $abort("pointer")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("read ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerRead", source, "read ok\n");
        var body = GeneratedIr.Parse(ir).Startup.Instructions;
        Assert.Contains(body, static x => x.Shape == "load i64, ptr %, align 8");
        Assert.Contains(body, static x => x.Shape == "load ptr, ptr %, align 8");
        Assert.Contains(body, static x => x.Shape == "store i64 1234567890123, ptr %, align 8");
    }

    [Fact]
    public void PointerCompoundAssignmentEvaluatesTheAddressAndOldValueOnce()
    {
        var source = $$"""
            {{Pages()}}
            func target(p: raw/i32) -> raw/i32
                Console.writeLine("target")
                return p
            unsafe func amount(p: raw/i32) -> i32
                Console.writeLine("amount")
                unsafe => *p = 100
                return 3
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var p: raw/i32 = null
                unsafe => p = bytes@raw/i32
                unsafe => Raw.initialize(p, 10)
                unsafe => *target(p) += amount(p)
                var value: i32 = 0
                unsafe => value = *p
                require value == 103 else => $abort("rhs before read")
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
                var links: raw/raw/i32 = null
                unsafe => links = (bytes + 16)@raw/raw/i32
                unsafe => Raw.initialize(links, p)
                unsafe => *links += 2
                unsafe => *links -= 1
                var link: raw/i32 = null
                unsafe => link = *links
                var expected: raw/i32 = null
                unsafe => expected = p + 1
                require link == expected else => $abort("pointer update")
                var floats: raw/f64 = null
                unsafe => floats = (bytes + 32)@raw/f64
                unsafe => Raw.initialize(floats, 1.5)
                unsafe => *floats *= 2.0
                var real: f64 = 0.0
                unsafe => real = *floats
                require real == 3.0 else => $abort("float update")
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("compound ok")
            """;
        // SPEC 13.7.2: the right-hand side runs first; the old value is then read from the located target.
        ScalarEmissionTest.EmitFixture("ForeignPointerCompound", source, "amount\ntarget\ncompound ok\n");
    }

    [Theory]
    [InlineData("Overflow", "2147483647", "+= 1", "KIMI_E_INT_OVERFLOW: Integer overflow")]
    [InlineData("DivideZero", "12", "/= 0", "KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero")]
    [InlineData("Shift", "12", "<<= -1", "KIMI_E_INT_SHIFT_COUNT: Shift count out of range")]
    public void PointerCompoundAssignmentPreservesArithmeticChecks(string name, string initial, string update, string reason)
    {
        var source = $$"""
            {{Pages("raw/i32", free: false)}}
            public func main()
                var p: raw/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => Raw.initialize(p, {{initial}})
                unsafe => *p {{update}}
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerCompound" + name, source, string.Empty, 1, $"Hello.kimi:13:15: abort {reason}\n");
    }

    [Fact]
    public void PointerCompoundAssignmentDoesNotWriteAfterAnRhsReturn()
    {
        var source = $$"""
            {{Pages("raw/i32")}}
            unsafe func early(p: raw/i32)
                unsafe
                    *p += do => return
            public func main()
                var p: raw/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => Raw.initialize(p, 5)
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
        var source = $$"""
            {{Pages()}}
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var flags: raw/bool = null
                unsafe => flags = bytes@raw/bool
                unsafe => Raw.initialize(flags, false)
                var flag = true
                unsafe => flag = *flags
                require not flag else => $abort("placed false")
                unsafe => *flags = not flag
                var byte: u8 = 0
                unsafe => byte = *bytes
                require byte == 1 else => $abort("stored true")
                unsafe => flag = *flags
                require flag else => $abort("loaded true")
                unsafe => Raw.initialize(flags + 1, false)
                unsafe => Raw.initialize(flags + 2, true)
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
        Assert.Contains(GeneratedIr.Parse(ir).Startup.Instructions, static x => x.Shape == "trunc i8 % to i1");
        Assert.Contains(" = zext i1 ", ir);
        Assert.DoesNotContain("load i1,", ir);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("func update(p: raw/bool, n: raw/i32)\n    unsafe\n        *p = not *p\n        *n += 1\npublic func main() => ()")]
    [InlineData("struct R\n    public var n: i32\n    drop => ()\nfunc take(p: raw/R) -> R\n    unsafe => return (*p)@move\npublic func main() => ()")]
    [InlineData("struct R\n    public var n: i32\n    drop => ()\nfunc update(p: raw/R, value: R)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("func update(p: raw/string, value: string)\n    unsafe => *p = value@move\nfunc take(p: raw/string) -> string\n    unsafe => return (*p)@move\npublic func main() => ()")]
    [InlineData("enum E\n    Empty\n    Value(string)\nfunc update(p: raw/E, value: E)\n    unsafe => *p = value@move\nfunc take(p: raw/E) -> E\n    unsafe => return (*p)@move\npublic func main() => ()")]
    [InlineData("enum E<T>\n    Empty\n    Value(T)\nfunc update(p: raw/E<E<string>>, value: E<E<string>>)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("#Layout(\"C\")\nstruct R\n    public var a: u8\n    public var b: u64\n    drop => ()\nfunc update(p: raw/R, value: R)\n    unsafe => *p = value@move\npublic func main() => ()")]
    [InlineData("func update(p: raw/(i32, i64))\n    unsafe\n        let value = *p\n        *p = value\npublic func main() => ()")]
    [InlineData("struct P\n    public var a: u8\n    public var b: (i32, [2 of u16])\nfunc update(p: raw/P)\n    unsafe\n        (*p).b.1[1] += 1\n        let a = (*p).a\n        p[1].b.0 = 3\npublic func main() => ()")]
    [InlineData("func compare(p: raw/string, h: raw/(string, i32))\n    unsafe\n        let a = *p == \"x\"\n        let b = (*h).0 < p[1]\npublic func main() => ()")]
    public void WarmPointerReadWriteAnalysisAndEmissionAllocateNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.WriteIr(TextWriter.Null, out var error), error);

        var success = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            success &= c.Analyze().IsVerified;
            success &= c.WriteIr(TextWriter.Null, out _);
        });
        Assert.True(success);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void PointerIndexingUsesSignedStrideAndAssignmentOrder()
    {
        var source = $$"""
            {{Pages("raw/i64")}}
            func target(p: raw/i64) -> raw/i64
                Console.writeLine("target")
                return p
            func index() -> isize
                Console.writeLine("index")
                return -1
            func amount() -> i64
                Console.writeLine("amount")
                return 9
            unsafe func skipIndex(p: raw/i64)
                unsafe => p[do => return] += amount()
            unsafe func skipRight(p: raw/i64)
                unsafe => target(p)[index()] = do => return
            public func main()
                var p: raw/i64 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var placed: isize = 0
                while placed < 4
                    unsafe => Raw.initialize(p + placed, 0)
                    placed += 1
                var next: raw/i64 = null
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
                    *chosen += label change: do
                        chosen = next
                        require chosen == next else => $abort("changed")
                        exit to change 2
                unsafe => value = p[0]
                require value == 18 else => $abort("target located after the right-hand side")
                unsafe => value = next[0]
                require value == 2 else => $abort("updated target")
                chosen = p
                unsafe
                    value = chosen[label offset: do
                        chosen = next
                        require chosen == next else => $abort("changed")
                        exit to offset 0
                    ]
                require value == 18 else => $abort("secured index base")
                unsafe => skipIndex(p)
                unsafe => skipRight(p)
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("index ok")
            """;
        // SPEC 13.7.2: simple and compound assignments run their right-hand side before locating the target.
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerIndex", source, "amount\ntarget\nindex\namount\ntarget\nindex\namount\nindex ok\n");
        Assert.DoesNotContain("getelementptr inbounds i8", ir);
        Assert.DoesNotContain("call void @__kimi_abort(i32 " + AbortReasons.IndexBounds, ir);
    }

    [Theory]
    [InlineData("let p: raw/i32 = null\nlet n: i32 = 1\nunsafe\n    let v = p[n]")]
    [InlineData("let p: raw/i32 = null\nunsafe\n    let v = p[^1]")]
    [InlineData("let p: raw/i32 = null\nunsafe\n    let v = p[0..1]")]
    [InlineData("let p: raw/i32 = null\nunsafe\n    let v = p[true]")]
    public void PointerIndexingRejectsInvalidOffsets(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    // SPEC 23.1.2: an offset whose syntax failed to parse rests on that syntax Error, so no Type mismatch cascades from it.
    [Fact]
    public void AMalformedPointerOffsetRestsOnItsSyntaxError()
    {
        var c = MinimalEmissionTest.Analyze("let p: raw/i32 = null\nunsafe\n    let v = p[0...1]");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        Assert.True(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("var v: i32 = p[0]")]
    [InlineData("p[0] = 1")]
    [InlineData("p[0] += 1")]
    public void PointerIndexingRequiresAnUnsafeContext(string body)
    {
        var c = MinimalEmissionTest.Analyze("let p: raw/i32 = null\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.UnsafeBlockRequired_Kd));
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
    }

    [Theory]
    [InlineData("let q = p + 1")]
    [InlineData("let q = p - 1")]
    [InlineData("p += 1")]
    [InlineData("p -= 1")]
    public void BoundPointerArithmeticRequiresAnUnsafeContext(string body)
    {
        var c = MinimalEmissionTest.Analyze("var p: raw/i32 = null\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.UnsafeBlockRequired_Kd));
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
    }

    [Fact]
    public void PointerIncrementsPreservePrefixPostfixResultsAndSingleEvaluation()
    {
        var source = $$"""
            {{Pages("raw/i32")}}
            func target(p: raw/i32) -> raw/i32
                Console.writeLine("target")
                return p
            func index() -> isize
                Console.writeLine("index")
                return 0
            public func main()
                var p: raw/i32 = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => Raw.initialize(p, 5)
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
            {{Pages("raw/" + type, free: false)}}
            public func main()
                var p: raw/{{type}} = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => Raw.initialize(p, {{initial}})
                unsafe => {{operation}}
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerIncrement" + name, source, string.Empty, 1, "Hello.kimi:13:15: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    // SPEC 13.2, 13.3.1: a pointer itself has no increment; a float pointee needs an integer operand and a bool pointee a numeric one.
    [Theory]
    [InlineData("i32", "p++", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("f64", "(*p)++", DiagnosticCode.NonIntegerOperand_Kd)]
    [InlineData("bool", "++p[0]", DiagnosticCode.NonNumericOperand_Kd)]
    public void PointerIncrementsRequireIntegerPointees(string type, string operation, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze($"var p: raw/{type} = null\nunsafe => {operation}");
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void CopyAggregatePointersTransferCompleteValues()
    {
        var source = $$"""
            struct Pair
                Self is Copy
                public var first: u8
                public var second: u64
                public var flag: bool
                public init(first: u8, second: u64, flag: bool)
                    self.first = first
                    self.second = second
                    self.flag = flag
            {{Pages()}}
            unsafe func snapshot(p: raw/Pair) -> Pair
                unsafe => return *p
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var pairs: raw/Pair = null
                unsafe => pairs = bytes@raw/Pair
                unsafe
                    Raw.initialize(pairs, Pair.init(0, 0, false))
                    let zero = *pairs
                    require zero.first == 0 and zero.second == 0 and not zero.flag else => $abort("zero")
                    *pairs = Pair.init(7, 1234567890123, true)
                    let old = snapshot(pairs)
                    *pairs = Pair.init(9, 11, false)
                    Raw.initialize(pairs + 1, old)
                    let first = pairs[0]
                    let second = pairs[1]
                    require first.first == 9 and first.second == 11 and not first.flag else => $abort("replace")
                    require second.first == 7 and second.second == 1234567890123 and second.flag else => $abort("copy")
                    require old.first == 7 else => $abort("retained")
                var tuples: raw/(u8, (u64, bool)) = null
                unsafe => tuples = (bytes + 64)@raw/(u8, (u64, bool))
                unsafe
                    Raw.initialize(tuples, (3, (99, true)))
                    let tuple = *tuples
                    require tuple.0 == 3 and tuple.1.0 == 99 and tuple.1.1 else => $abort("tuple")
                var arrays: raw/[3 of i16] = null
                unsafe => arrays = (bytes + 128)@raw/[3 of i16]
                unsafe
                    Raw.initialize(arrays, [4, 5, 6])
                    let array = arrays[0]
                    Raw.initialize(arrays + 1, array)
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
        var source = $$"""
            #Layout("C")
            struct Wide
                public var head: u8 = 0
                public var tail: u64 = 0
            struct Pair
                Self is Copy
                public var first: u8 = 0
                public var second: u64 = 0
            struct Holder
                public var label: string = ""
                public var count: i32 = 0
            {{Pages()}}
            func pick(p: raw/Pair) -> raw/Pair
                Console.writeLine("pick")
                return p
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var pairs: raw/Pair = null
                unsafe => pairs = bytes@raw/Pair
                unsafe
                    Raw.initialize(pairs, Pair.init())
                    Raw.initialize(pairs + 1, Pair.init())
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
                var wide: raw/Wide = null
                unsafe => wide = (bytes + 64)@raw/Wide
                unsafe
                    Raw.initialize(wide, Wide.init())
                    (*wide).tail = 3
                    (*wide).head = 1
                    let tail = *((bytes + 72)@raw/u64)
                    let head = *(bytes + 64)
                    require tail == 3 and head == 1 and (*wide).tail == 3 else => $abort("c layout")
                var tuples: raw/(u8, (u64, bool)) = null
                unsafe => tuples = (bytes + 128)@raw/(u8, (u64, bool))
                unsafe
                    Raw.initialize(tuples, (0, (0, false)))
                    (*tuples).1.0 = 99
                    (*tuples).1.1 = true
                    (*tuples).0 = 3
                    let tuple = *tuples
                    require tuple.0 == 3 and tuple.1.0 == 99 and tuple.1.1 and (*tuples).1.0 == 99 else => $abort("tuple")
                var arrays: raw/[3 of i16] = null
                unsafe => arrays = (bytes + 192)@raw/[3 of i16]
                unsafe
                    Raw.initialize(arrays, [0, 0, 0])
                    Raw.initialize(arrays + 1, [0, 0, 0])
                    (*arrays)[2] = -4
                    arrays[1][0] = 5
                    (*arrays)[0] = 6
                    arrays[1][0] -= 1
                    let array = *arrays
                    require array[0] == 6 and array[1] == 0 and array[2] == -4 and arrays[1][0] == 4 else => $abort("array")
                var holders: raw/Holder = null
                unsafe => holders = (bytes + 256)@raw/Holder
                unsafe
                    Raw.initialize(holders, Holder.init())
                    (*holders).label = "kept"
                    (*holders).count = 41
                    (*holders).count += 1
                    require (*holders).count == 42 else => $abort("count")
                    let taken = (*holders).label@move
                    Console.writeLine(taken)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("subplace ok")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerSubplace", source, "pick\nkept\nsubplace ok\n");
        Assert.Contains(GeneratedIr.Parse(ir).Startup.Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 8");
    }

    [Fact]
    public void ComputedPointerSubplaceIndicesAreCheckedAndOrdered()
    {
        var source = $$"""
            {{Pages()}}
            func pick(p: raw/(u8, [3 of i32])) -> raw/(u8, [3 of i32])
                Console.writeLine("pick")
                return p
            func at(i: isize) -> isize
                Console.writeLine("index")
                return i
            func value(n: i32) -> i32
                Console.writeLine("value")
                return n
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var p: raw/(u8, [3 of i32]) = null
                unsafe => p = bytes@raw/(u8, [3 of i32])
                var grid: raw/[2 of [2 of u16]] = null
                unsafe => grid = (bytes + 64)@raw/[2 of [2 of u16]]
                unsafe
                    Raw.initialize(p, (0, [0, 0, 0]))
                    Raw.initialize(grid, [[0, 0], [0, 0]])
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
        // SPEC 13.7.2: the right-hand side of the compound update runs before its target is located.
        ScalarEmissionTest.EmitFixture("ForeignPointerSubplaceIndex", source, "value\npick\nindex\nvalue\npick\nindex\nindex\nchecked ok\n");
    }

    [Theory]
    [InlineData("High", "3")]
    [InlineData("Negative", "-1")]
    public void ComputedPointerSubplaceIndicesAbortOutsideTheFixedLength(string name, string index)
    {
        var source = $$"""
            {{Pages(free: false)}}
            public func main()
                var p: raw/[3 of i32] = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)@raw/[3 of i32]
                unsafe => Raw.initialize(p, [0, 0, 0])
                var i: isize = {{index}}
                unsafe => (*p)[i] = 1
                Console.writeLine("bad")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerSubplaceBounds" + name, source, string.Empty, 1, "Hello.kimi:14:15: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Fact]
    public void ComputedPointerSubplaceIndicesMayEndALogicalOperand()
    {
        // The bounds-checked element address splits its block; the join of `and` names the block after the check.
        var source = $$"""
            {{Pages(free: false)}}
            public func main()
                var p: raw/[3 of i32] = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)@raw/[3 of i32]
                unsafe => Raw.initialize(p, [0, 0, 0])
                var i: isize = 1
                let flag = true
                var ok = false
                unsafe => ok = flag and (*p)[i] == 0
                require ok else => $abort("and")
                Console.writeLine("ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerSubplaceLogical", source, "ok\n");
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void DependentPointerReadsPreserveTheirCompleteType(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func read(p: raw/({type}))\n    unsafe\n        let value = *p\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("(string, i32)")]
    [InlineData("[2 of string]")]
    public void OwnedAggregatePointerWritesHaveCompleteCleanupPlans(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func update(p: raw/{type}, value: {type})\n    unsafe => *p = value@move\npublic func main() => ()");
        var ir = CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StringPointersMoveAndReplaceCompleteHandles(bool consume)
    {
        var source = $$"""
            {{Pages("raw/string")}}
            func take(p: raw/string) -> string
                unsafe => return (*p)@move
            func use(p: raw/string, flag: bool)
                unsafe
                    Raw.initialize(p, "first")
                    p[0] = "second"
                    Console.writeLine(take(p))
                    Raw.initialize(p + 1, "third")
                    let moved = p[1]@move
                    if flag => Console.writeLine(moved)
                    Raw.initialize(p + 2, "compare")
                    let compared = p[2]@move
                    require compared == "compare" else => $abort("comparison")
                    let skipped = false and take(p + 3) == "skip"
                    require not skipped else => $abort("short circuit")
                    Console.writeLine("done")
            public func main()
                var p: raw/string = null
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
        StringEmissionTest.WriteAuditedFixture(
            name,
            ir,
            stdout,
            "=0;first=1;second=1;third=1;compare=1;skip=0;done=1");
    }

    [Theory]
    [InlineData("*p")]
    [InlineData("p[0]")]
    public void StringPointerComparisonsDoNotInventAnOwningRead(string expression)
    {
        // SPEC 5.2: the handle is inspected in place; no temporary owner, Move or cleanup (its MIR: tests/mir/raw.kimi `derefCompare`,
        // `indexCompare`).
        var c = MinimalEmissionTest.Analyze($"func compare(p: raw/string)\n    unsafe\n        let equal = {expression} == \"text\"\npublic func main() => ()");
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var ir = CompilationTestHelper.WriteIr(c);
    }

    [Fact]
    public void RawStringComparisonsInspectHandlesInPlace()
    {
        var source = $$"""
            struct Holder
                public var label: string
                public var count: i32
                public init(label: string)
                    self.label = label@move
                    self.count = 0
            {{Pages("raw/string")}}
            public func main()
                var p: raw/string = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                var holder: raw/Holder = null
                unsafe => holder = (p@raw/u8 + 256)@raw/Holder
                unsafe
                    Raw.initialize(p, "alpha")
                    Raw.initialize(p + 1, "beta")
                    Raw.initialize(holder, Holder.init("kept"))
                    require *p == "alpha" and p[1] != "alpha" else => $abort("equal")
                    require p[1] > *p and "beta" == p[1] and not (p[1] < *p) else => $abort("order")
                    require (*holder).label >= p[1] and (*holder).label == "kept" else => $abort("field")
                    let a = (*p)@move
                    let b = p[1]@move
                    let k = (*holder).label@move
                    Console.writeLine(a)
                    Console.writeLine(b)
                    Console.writeLine(k)
                var freed: i32 = 0
                unsafe => freed = Native.free(p, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerStringCompare", source, "alpha\nbeta\nkept\ndone\n");
        StringEmissionTest.WriteAuditedFixture(
            "ForeignPointerStringCompare",
            ir,
            "alpha\nbeta\nkept\ndone\n",
            "=0;alpha=1;beta=1;kept=1;done=1");
    }

    [Fact]
    public void EnumPointersPreserveTagsAndActivePayloadOwnership()
    {
        var source = $$"""
            enum E<T>
                Empty
                Value(T)
            struct R
                public var n: i32
                public init(n: i32) => self.n = n
                drop
                    if self.n == 1 => Console.writeLine("one")
                    else => Console.writeLine("two")
            {{Pages()}}
            func take(p: raw/E<R>) -> E<R>
                unsafe => return (*p)@move
            func values(bytes: raw/u8)
                unsafe
                    let p = bytes@raw/E<R>
                    Raw.initialize(p, E<R>.Value(R.init(1)))
                    p[0] = .Value(R.init(2))
                    match take(p)
                        .Empty => $abort("tag")
                        .Value(let value)
                            require value.n == 2 else => $abort("payload")
                            Console.writeLine("value")
                    let text = (bytes + 64)@raw/E<string>
                    Raw.initialize(text, E<string>.Value("first"))
                    *text = .Empty
                    Raw.initialize(text + 1, E<string>.Value("second"))
                    let retained = text[1]@move
                    let aligned = (bytes + 192)@raw/E<u128>
                    Raw.initialize(aligned + 1, E<u128>.Value(340282366920938463463374607431768211455))
                    let wide = aligned[1]@move
                    match wide@move
                        .Empty => $abort("wide tag")
                        .Value(let value)
                            require value == 340282366920938463463374607431768211455 else => $abort("wide value")
                    let nested = (bytes + 320)@raw/E<E<string>>
                    Raw.initialize(nested, E<E<string>>.Value(.Value("nested")))
                    let nestedResult = (*nested)@move
                    Console.writeLine("done")
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                values(bytes)
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerEnum", source, "one\nvalue\ntwo\ndone\n");
        StringEmissionTest.WriteAuditedFixture("ForeignPointerEnum", ir, "one\nvalue\ntwo\ndone\n", "one=1;two=1;value=1;first=1;second=1;nested=1;done=1");
    }

    [Fact]
    public void NonCopyPointerReplacementDestroysOldValueAndTransfersNewOwner()
    {
        var source = $$"""
            struct Resource
                public var value: i32
                public init(value: i32) => self.value = value
                drop
                    if self.value == 0 => Console.writeLine("drop zero")
                    else if self.value == 1 => Console.writeLine("drop one")
                    else => Console.writeLine("drop two")
            struct Empty
                public init() => ()
                drop => Console.writeLine("empty")
            {{Pages()}}
            func make() -> Resource
                Console.writeLine("rhs")
                return Resource.init(1)
            func target(p: raw/Resource) -> raw/Resource
                Console.writeLine("target")
                return p
            func early(p: raw/Resource)
                unsafe
                    p[do => return] = make()
            func replace(bytes: raw/u8)
                unsafe
                    // The values that the assignments below replace or the takes read.
                    Raw.initialize(bytes@raw/Resource, Resource.init(0))
                    Raw.initialize((bytes + 16)@raw/Empty, Empty.init())
                    Raw.initialize((bytes + 32)@raw/(Resource, Resource), (Resource.init(0), Resource.init(0)))
                    Raw.initialize((bytes + 64)@raw/Resource, Resource.init(0))
                    let p = bytes@raw/Resource
                    *target(p) = make()
                    Console.writeLine("stored")
                    p[0] = if true => Resource.init(2) else => Resource.init(1)
                    let result = (*p)@move
                    require result.value == 2 else => $abort("value")
                    let empty = (bytes + 16)@raw/Empty
                    *empty = Empty.init()
                    let emptyResult = (*empty)@move
                    let tuples = (bytes + 32)@raw/(Resource, Resource)
                    *tuples = (Resource.init(1), Resource.init(2))
                    let tuple = (*tuples)@move
                    let stopped = (bytes + 64)@raw/Resource
                    early(stopped)
                    let untouched = (*stopped)@move
                    require untouched.value == 0 else => $abort("early store")
                    Console.writeLine("scope")
            public func main()
                var bytes: raw/u8 = null
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
        var source = $$"""
            struct Resource
                public var value: i32 = 0
                drop
                    require self.value == 0 else => $abort("value")
                    Console.writeLine("drop")
            struct Empty
                drop => Console.writeLine("empty")
            {{Pages()}}
            func take(p: raw/Resource) -> Resource
                unsafe => return (*p)@move
            func use(value: Resource)
                Console.writeLine("use")
            func choose(p: raw/Resource, flag: bool) -> Resource
                unsafe
                    if flag => return p[0]@move
                    return p[1]@move
            func early(p: raw/Resource)
                unsafe
                    let value = (*p)@move
                    defer => Console.writeLine("defer")
                    return
            func reads(bytes: raw/u8)
                unsafe
                    // Each Non-Copy source is placed, then read exactly once; no raw owner is destroyed.
                    Raw.initialize(bytes@raw/Resource, Resource.init())
                    Raw.initialize((bytes + 16)@raw/(Resource, Resource), (Resource.init(), Resource.init()))
                    Raw.initialize((bytes + 32)@raw/[2 of Resource], [Resource.init(), Resource.init()])
                    Raw.initialize((bytes + 64)@raw/Empty, Empty.init())
                    Raw.initialize((bytes + 80)@raw/Resource, Resource.init())
                    Raw.initialize((bytes + 100)@raw/Resource, Resource.init())
                    Raw.initialize((bytes + 112)@raw/Resource, Resource.init())
                    Raw.initialize((bytes + 128)@raw/Resource, Resource.init())
                    let first = take(bytes@raw/Resource)
                    use(first@move)
                    let tuple = (*((bytes + 16)@raw/(Resource, Resource)))@move
                    let array = ((bytes + 32)@raw/[2 of Resource])[0]@move
                    let empty = (*((bytes + 64)@raw/Empty))@move
                    use(choose((bytes + 80)@raw/Resource, true))
                    use(choose((bytes + 96)@raw/Resource, false))
                    early((bytes + 112)@raw/Resource)
                    _ = (*((bytes + 128)@raw/Resource))@move
                    Console.writeLine("scope")
            public func main()
                var bytes: raw/u8 = null
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
        var c = MinimalEmissionTest.Analyze("struct R\n    public var n: i32\n    drop => ()\nfunc test(p: raw/R)\n    unsafe\n        let value = *p\n        let moved = value@move\n        let again = value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.ErrorCount > 0);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void PointerReplacementConsumesTheSourceOwner()
    {
        var c = MinimalEmissionTest.Analyze("struct R\n    public var n: i32\n    drop => ()\nfunc test(p: raw/R, value: R)\n    unsafe => *p = value@move\n    let again = value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.ErrorCount > 0);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void AbortingPointerReplacementDoesNotContinueOrUnwind()
    {
        var source = $$"""
            struct R
                public var n: i32
                public init(n: i32) => self.n = n
                drop
                    if self.n == 0 => $abort("old")
                    Console.writeLine("new cleanup")
            {{Pages("raw/R", free: false)}}
            public func main()
                var p: raw/R = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe => Raw.initialize(p, R.init(0))
                let value = R.init(1)
                Console.writeLine("rhs")
                unsafe => *p = value@move
                Console.writeLine("after")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerOwnedAbort", source, "rhs\n", 1, "Hello.kimi:5:27: abort KIMI_E_ABORT: old\n");
    }

    [Fact]
    public void ZeroStrideArithmeticAndIndexingKeepThePointer()
    {
        // SPEC 5.3: with stride(T) = 0 every displacement is zero, so p + n and p - n are p and p[n] is *p.
        var source = $$"""
            struct Marker
                public init() => ()
                drop => Console.writeLine("marker")
            {{Pages()}}
            func offset() -> isize
                Console.writeLine("offset")
                return 7
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var units: raw/() = null
                unsafe => units = bytes@raw/()
                var moved: raw/() = null
                unsafe => moved = units + offset() - 3
                require moved == units else => $abort("displacement")
                var markers: raw/Marker = null
                unsafe => markers = bytes@raw/Marker
                unsafe
                    Raw.initialize(markers, Marker.init())
                    markers[offset()] = Marker.init() // Replacement destroys the value already at *markers.
                    _ = markers[-2]@move             // Moves out the new value, the same Place, and destroys it.
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("zero stride ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerZeroStride", source, "offset\noffset\nmarker\nmarker\nzero stride ok\n");
    }

    [Fact]
    public void ZeroSizedPointerAccessRetainsEvaluationWithoutReadingBytes()
    {
        var source = $$"""
            struct Empty
                Self is Copy
                public init() => ()
            {{Pages()}}
            func target(p: raw/()) -> raw/()
                Console.writeLine("target")
                return p
            func value()
                Console.writeLine("value")
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                var units: raw/() = null
                unsafe => units = bytes@raw/()
                unsafe
                    Raw.initialize(units, ())
                    let unit = *target(units)
                    *target(units) = value()
                var empty: raw/Empty = null
                unsafe => empty = bytes@raw/Empty
                unsafe
                    Raw.initialize(empty, Empty.init())
                    let item = (*empty)@move
                    Raw.initialize(empty, item)
                var arrays: raw/[0 of i32] = null
                unsafe => arrays = bytes@raw/[0 of i32]
                unsafe
                    Raw.initialize(arrays, [])
                    let array = *arrays
                    *arrays = array
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("zero size ok")
            """;
        ScalarEmissionTest.EmitFixture("ForeignPointerZeroSize", source, "target\nvalue\ntarget\nzero size ok\n");
    }

    [Fact]
    public void PointerTransfersUseTheCLayoutRepresentation()
    {
        var c = MinimalEmissionTest.Analyze(
            "#Layout(\"C\")\nstruct Pair\n    Self is Copy\n    public var a: u8\n    public var b: u64\n" +
            "func read(p: raw/Pair)\n    unsafe\n        let value = *p\npublic func main()\n    let p: raw/Pair = null\n    read(p)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("{ i8, i64 }", ir);
    }

    [Fact]
    public void ExplicitKimigayoLayoutIsMetadataAndUsesTheDefaultPhysicalOrder()
    {
        var source = $$"""
            #Layout("Kimigayo")
            struct Pair
                Self is Copy
                public var a: u8
                public var b: u64
                public init(a: u8, b: u64)
                    self.a = a
                    self.b = b
            {{Pages("raw/Pair")}}
            public func main()
                var p: raw/Pair = null
                unsafe => p = Native.allocate(null, 4096, 12288, 4)
                unsafe
                    Raw.initialize(p, Pair.init(7, 42))
                    let value = *p
                    require value.a == 7 and value.b == 42 else => $abort("fields")
                    require *(p@raw/u64) == 42 else => $abort("order")
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
    public void ZeroStridePointerOperationsAreValid(string type)
    {
        // SPEC 5.3: a zero stride makes every displacement zero; ZeroStrideArithmeticAndIndexingKeepThePointer executes it.
        foreach (var operation in new[] { "p + 0", "p - 0", "p += 0", "p -= 0", "p[0]" })
        {
            var c = MinimalEmissionTest.Analyze($"open struct Empty\nstruct Nested\n    public var value: (Empty, [4 of ()])\nstruct Derived: Empty\nstruct Box<T>\n    public var value: T\nfunc test()\n    var p: raw/{type} = null\n    unsafe => {operation}\npublic func main() => ()");
            Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        }
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("((), i32)")]
    [InlineData("[1 of i32]")]
    [InlineData("Box<i32>")]
    [InlineData("Derived")]
    [InlineData("raw/()")]
    public void NonzeroPointerStrideIsNotRejected(string type)
    {
        var c = MinimalEmissionTest.Analyze($"open struct Base\n    public var value: i32\nstruct Derived: Base\nstruct Box<T>\n    public var value: T\nfunc test(p: raw/{type})\n    unsafe\n        let next = p + 1\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void SymbolicArrayLengthIsNotMistakenForZeroStride()
    {
        var c = MinimalEmissionTest.Analyze("func test<length N>(p: raw/[N of i32])\n    unsafe\n        let next = p + 1\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void CLayoutPointersPreserveWrittenOrderAndStride()
    {
        var source = $$"""
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
                public var a: u8 = 0
                public var b: u64 = 0
                public var c: u8 = 0
            #Layout("C")
            struct Outer
                Self is Copy
                public var lead: u8 = 0
                public var record: Record = Record.init(0, 0, 0)
            #Layout("C")
            struct Generic<T>
                public var lead: u8
                public var value: T
                public init(lead: u8, value: T)
                    self.lead = lead
                    self.value = value@move
            #Layout("C")
            struct Resource
                public var n: i32
                public init(n: i32) => self.n = n
                drop
                    if self.n == 0 => Console.writeLine("old")
                    else => Console.writeLine("new")
            {{Pages()}}
            public func main()
                var bytes: raw/u8 = null
                unsafe => bytes = Native.allocate(null, 4096, 12288, 4)
                unsafe
                    let p = bytes@raw/Record
                    Raw.initialize(p, Record.init(7, 42, 9))
                    Raw.initialize(p + 1, Record.init(11, 99, 13))
                    // The punned reads need the bytes held by Initialized Places, so they precede the Take of p[1].
                    require bytes[0] == 7 and bytes[16] == 9 and bytes[24] == 11 and bytes[40] == 13 else => $abort("C offsets")
                    require *((bytes + 8)@raw/u64) == 42 and *((bytes + 32)@raw/u64) == 99 else => $abort("C alignment")
                    let value = p[1]@move
                    require value.a == 11 and value.b == 99 and value.c == 13 else => $abort("value")
                    let compact = (bytes + 64)@raw/Compact
                    Raw.initialize(compact, Compact.init())
                    let other = *compact
                    require (p + 1)@usize - p@usize == 24 else => $abort("C stride")
                    require (compact + 1)@usize - compact@usize == 16 else => $abort("Kimigayo stride")
                    let outer = (bytes + 128)@raw/Outer
                    Raw.initialize(outer, Outer.init())
                    bytes[136] = 21
                    let nested = *outer
                    require nested.record.a == 21 else => $abort("nested offset")
                    require (outer + 1)@usize - outer@usize == 32 else => $abort("nested stride")
                    let generic = (bytes + 192)@raw/Generic<u64>
                    Raw.initialize(generic, Generic<u64>.init(0, 0))
                    bytes[200] = 23
                    let instantiated = (*generic)@move
                    require instantiated.value == 23 else => $abort("generic offset")
                    let resource = (bytes + 256)@raw/Resource
                    Raw.initialize(resource, Resource.init(0))
                    *resource = Resource.init(1)
                    let moved = (*resource)@move
                var freed: i32 = 0
                unsafe => freed = Native.free(bytes, 0, 32768)
                require freed != 0 else => $abort("free")
                Console.writeLine("C layout")
            """;
        var ir = ScalarEmissionTest.EmitFixture("ForeignPointerCLayout", source, "old\nnew\nC layout\n");
        Assert.Contains("{ i8, i64, i8 }", ir);
    }

    // SPEC 6.7.1, 8.15.3 (DECISIONS BU-48): a raw read by value of a struct whose C layout is unavailable (empty, open or storing a
    // zero-sized Field) needs its layout, so check rejects it at the read (InvalidCLayout_Kd) and generation writes nothing; the declaration
    // alone is no error.
    [Theory]
    [InlineData("struct S")]
    [InlineData("open struct S\n    var n: i32")]
    [InlineData("struct S\n    var empty: ()")]
    [InlineData("struct S\n    var empty: [0 of i32]")]
    [InlineData("struct S\n    var empty: [2 of ()]")]
    public void ARawReadOfAnUnavailableCLayoutCannotGenerate(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\n" + declaration + "\nfunc read(p: raw/S)\n    unsafe\n        let value = *p\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Problems, static x => x.Code == DiagnosticCode.InvalidCLayout_Kd);
        using var writer = new StringWriter();
        Assert.False(c.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    // SPEC 5.2, 5.2.1, 22.6: the documented VirtualAlloc/VirtualFree imports of the raw pointer fixtures. A result's
    // provenance and authority come only from these `- safety:` conditions, and the pages hold no value, so a fixture
    // places what it reads or replaces with Raw.initialize. The group spans 8 lines, 15 with `free`.
    internal static string Pages(string pointer = "raw/u8", bool free = true)
    {
        var allocate = $"""
            group Native
                /// Reserves and commits read-write pages.
                ///
                /// - safety: `address` is null, `kind` is 12288 (commit and reserve) and `protect` is 4 (read-write). A
                ///   nonnull result is the page-aligned start of new raw storage of `size` bytes rounded up to whole pages
                ///   that holds no value; that storage is its provenance, readable and writable until it is released.
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: {pointer}, size: u64, kind: u32, protect: u32) -> {pointer}
            """;
        return !free ? allocate : allocate + "\n" + $"""
                /// Releases pages that `allocate` returned.
                ///
                /// - safety: `address` is an unreleased `allocate` result, `size` is 0 and `kind` is 32768 (release).
                ///   Every Non-Copy value placed in the pages is already destroyed, taken or relocated, as `Raw.release`
                ///   requires (SPEC 5.6); afterwards every pointer into the pages is invalid.
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: {pointer}, size: u64, kind: u32) -> i32
            """;
    }

    private static string Emit(string source, string kind)
    {
        var c = Analyze(source, kind);
        return CompilationTestHelper.WriteIr(c);
    }

    private static Compilation Analyze(string source, string kind)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = kind } };
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return c;
    }
}
