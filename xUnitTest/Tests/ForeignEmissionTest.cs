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
