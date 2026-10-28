// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryImportTargetBindingTest
{
    private const string Import = "#LibraryImport(\"codec\", \"symbol\")";

    [Theory]
    [InlineData("contract C\n#LibraryImport(\"library\", \"symbol\")\nstruct S\n    Self is C")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\ncontract C\nstruct S\n    Self is C")]
    public void NonFunctionImportTargetCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        var restoredType = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var restoredContract = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(restored.Binding.GetConformanceDefinition(restoredType.TypeOf()!, restoredContract.SymbolOf()!)!.IsVerified);
    }

    [Fact]
    public void ImportOnPropertyCannotCertifyStorage()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    #LibraryImport(\"library\", \"symbol\")\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.SymbolOf()!.Property!.IsVerified);
    }

    [Fact]
    public void ImportOnEnumCannotPublishConstruction()
    {
        var c = MinimalEmissionTest.Analyze("#LibraryImport(\"library\", \"symbol\")\nenum E\n    Empty\ngroup Consumer\n    func make() -> E => .Empty");
        Assert.False(c.Binding.Result.IsComplete);
        var use = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single().ExpressionBody!;
        Assert.False(c.Binding.TryGetEnumConstruction(use, out _));
    }

    [Fact]
    public void ImportOnGroupInvalidatesNestedProperty()
    {
        var c = MinimalEmissionTest.Analyze("#LibraryImport(\"library\", \"symbol\")\ngroup G\n    let value: i32 = 1");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.SymbolOf()!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Layout(\"C\")\n#LibraryImport(\"library\", \"symbol\")")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\n#Layout(\"C\")")]
    public void RemovingImportFromChainRestoresConformance(string markers)
    {
        // SPEC 21.1 rejects empty C structs; the field keeps this conformance test valid.
        var c = MinimalEmissionTest.Analyze("contract C\n" + markers + "\nstruct S\n    var n: i32\n    Self is C");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        var definition = c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!;
        Assert.False(definition.IsVerified);
        var import = type.AttributeChain!;
        if (import.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "LibraryImport" })
        {
            import = import.AttributeChain!;
        }

        Assert.True(type.RemoveAttribute(import));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void ValidImportDeclarationCompletesBinding()
    {
        var c = AnalyzeImport("group Native\n    #LibraryImport(\"library\", \"symbol\")\n    public unsafe func imported(value: i32) -> i32", "library");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var import = Assert.Single(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha);
        Assert.Equal(("library", "symbol", "import"), (import.Library, import.Symbol, import.Kind));

        // R5-M25: the import names its definition the way MirDefKey does, by the function's symbol, and a rebind keeps it.
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Native").Members.OfType<FunctionKoto>().Single();
        Assert.Same(function, import.Decl.Declaration);
        Assert.Same(function.SymbolOf(), import.Decl);
        Assert.Equal(BindingSymbolKind.Function, import.Decl.Kind);
        Assert.True(c.Bind().IsComplete);
        Assert.Same(import.Decl, Assert.Single(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha).Decl);
    }

    [Theory]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")", "codec", true, false)]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")", "codec", false, false)]
    [InlineData("#LibraryImport(\"kernel32\", \"ExitProcess\")", null, false, false)]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")", null, false, true)]
    [InlineData("#LibraryImport(\"Codec\", \"symbol\")", "codec", true, true)]
    public void ImportNameRequiresADefiningModuleRequirement(string attribute, string? requirement, bool combinedSupply, bool missing)
    {
        var c = AnalyzeImport("group Native\n    " + attribute + "\n    public unsafe func imported() -> i32", requirement, combinedSupply);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLibraryImport_Kd);
        Assert.Equal(missing, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd));
        var complete = !missing && !attribute.Contains("ExitProcess", StringComparison.Ordinal);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        Assert.Equal(complete, c.Bind().IsComplete);
        Assert.Equal(missing, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd));
    }

    [Theory]
    [InlineData("#LibraryImport(\"codec\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\", \"extra\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"\", \"symbol\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"sym\\0bol\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"sym\\(1)bol\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(name: \"codec\", \"symbol\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(codec, \"symbol\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported() -> i32 => 1")]
    [InlineData("#LibraryImport(\"codec\", \"memcpy\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"__chkstk\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"_fltused\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"__kimi_entry\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"llvm.trap\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported(value: i32 = 1) -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported<T>(value: i32) -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"first\")\n    #LibraryImport(\"codec\", \"second\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported() -> i32")]
    public void InvalidImportDeclarationsAreDiagnosed(string declaration)
    {
        var c = AnalyzeImport("group Native\n    " + declaration, "codec");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLibraryImport_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd);
    }

    [Theory]
    [InlineData("struct S\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func make(value: i32) -> i32", false)]
    [InlineData("struct S\n    var value: i32\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func read(self: S) -> i32", true)]
    [InlineData("contract C\n    #LibraryImport(\"codec\", \"symbol\")\n    unsafe func required() -> i32", true)]
    [InlineData("group Native\n    func outer()\n        #LibraryImport(\"codec\", \"symbol\")\n        unsafe func local() -> i32", true)]
    [InlineData("struct S<T>\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func make(value: i32) -> i32", true)]
    [InlineData("struct S<T>\n    public group Native\n        #LibraryImport(\"codec\", \"symbol\")\n        public unsafe func make(value: i32) -> i32", true)]
    [InlineData("struct S\n    public group Native\n        #LibraryImport(\"codec\", \"symbol\")\n        public unsafe func make(value: i32) -> i32", false)]
    public void ImportPlacementIsRestricted(string source, bool invalid)
    {
        var c = AnalyzeImport(source, "codec");
        Assert.Equal(invalid, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidLibraryImport_Kd));
        Assert.Equal(!invalid, c.Binding.Result.IsComplete);
    }

    // SPEC 22.6, one decision per Type: a Type of the table is accepted; a Type without a C counterpart is one error at the import, even
    // beside an Unsupported Type; any other Type outside the table, such as the nonnull Option of a borrow, whose representation is not
    // laid out yet, is one Unsupported record at the first such Type in source order.
    [Theory]
    [InlineData("(a: i8, b: u8, c: i16, d: u16, e: i32, f: u32, g: i64, h: u64, i: f32, j: f64, k: raw/i32)", "()", null, null)]
    [InlineData("()", "u8", null, null)]
    [InlineData("(value: raw/u8)", "raw/u8", null, null)]
    [InlineData("()", "f64", null, null)]
    [InlineData("(value: uniq/i64)", "i32", null, null)]
    [InlineData("(value: ref/[4 of u8], next: uniq/raw/u8)", "()", null, null)]
    [InlineData("(value: raw/string)", "()", null, null)]
    [InlineData("(value: string)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("()", "string", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: ref/string)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: Array<i32>)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: uniq/Array<i32>)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: ref/Dictionary<i32, i32>)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: (i32) -> i32)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(a: bool, b: string)", "()", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: string)", "bool", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("(value: bool)", "()", DiagnosticCode.Unsupported_Kd, "bool")]
    [InlineData("(value: char)", "()", DiagnosticCode.Unsupported_Kd, "char")]
    [InlineData("(value: isize)", "()", DiagnosticCode.Unsupported_Kd, "isize")]
    [InlineData("(value: usize)", "()", DiagnosticCode.Unsupported_Kd, "usize")]
    [InlineData("(value: i128)", "()", DiagnosticCode.Unsupported_Kd, "i128")]
    [InlineData("(value: (i32, i32))", "()", DiagnosticCode.Unsupported_Kd, "(i32, i32)")]
    [InlineData("(value: ())", "i32", DiagnosticCode.Unsupported_Kd, "()")]
    [InlineData("(value: Option<i32>)", "()", DiagnosticCode.Unsupported_Kd, "Option<i32>")]
    [InlineData("(value: [4 of u8])", "()", DiagnosticCode.Unsupported_Kd, "[4 of u8]")]
    [InlineData("()", "bool", DiagnosticCode.Unsupported_Kd, "bool")]
    [InlineData("()", "u128", DiagnosticCode.Unsupported_Kd, "u128")]
    [InlineData("()", "Never", DiagnosticCode.Unsupported_Kd, "Never")]
    [InlineData("(value: Wrapping<u8>)", "()", DiagnosticCode.Unsupported_Kd, "Wrapping<u8>")]
    [InlineData("()", "Wrapping<i64>", DiagnosticCode.Unsupported_Kd, "Wrapping<i64>")]
    [InlineData("(value: ref/Wrapping<i32>)", "Wrapping<i64>", DiagnosticCode.Unsupported_Kd, "ref/Wrapping<i32>")]
    [InlineData("(value: Slice<u8>{view})", "()", DiagnosticCode.Unsupported_Kd, "Slice<u8>{view}")]
    [InlineData("(value: UniqSlice<u8>{view})", "()", DiagnosticCode.Unsupported_Kd, "UniqSlice<u8>{view}")]
    [InlineData("(value: uniq/(i32, i32))", "()", DiagnosticCode.Unsupported_Kd, "uniq/(i32, i32)")]
    [InlineData("(value: ref/bool)", "()", DiagnosticCode.Unsupported_Kd, "ref/bool")]
    [InlineData("(a: i32, b: bool, c: char)", "bool", DiagnosticCode.Unsupported_Kd, "bool")]
    [InlineData("(value: Option<ref/i32 during a>)", "()", DiagnosticCode.Unsupported_Kd, "Option<ref/i32 during a>")]
    [InlineData("(value: Option<ref/string during a>)", "()", DiagnosticCode.Unsupported_Kd, "Option<ref/string during a>")]
    public void ImportSignaturesFollowTheInitialWindowsCAbi(string parameters, string result, DiagnosticCode? code, string? at)
    {
        var c = AnalyzeImport("group Native\n    " + Import + "\n    public unsafe func imported" + parameters + " -> " + result, "codec");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        AssertImportRecord(c, code, at, code is null);
    }

    // SPEC 22.6: a borrow of a C-layout struct whose Fields are all C-exchangeable passes as a pointer. The struct's published
    // C-layout fact (DECISIONS R5-M26) and its prepared Field Types decide it, in every pass. A struct or enum by value, an object
    // borrow and a borrow of any other referent are Unsupported; an object handle (obj, rc, arc) has no C counterpart.
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: i32", "(value: uniq/S)", null, null)]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: i32", "(value: ref/S)", null, null)]
    [InlineData("#Layout(\"C\")\nstruct Inner\n    public var a: i32\n#Layout(\"C\")\nstruct S\n    public var inner: Inner\n    public var bytes: [2 of u8]", "(value: ref/S)", null, null)]
    [InlineData("#Layout(\"C\")\nstruct S<T>\n    public var a: T", "(value: uniq/S<i64>)", null, null)]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: i32", "(value: S)", DiagnosticCode.Unsupported_Kd, "S")]
    [InlineData("struct S\n    public var a: i32", "(value: ref/S)", DiagnosticCode.Unsupported_Kd, "ref/S")]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: bool", "(value: ref/S)", DiagnosticCode.Unsupported_Kd, "ref/S")]
    [InlineData("struct Inner\n    public var a: i32\n#Layout(\"C\")\nstruct S\n    public var inner: Inner", "(value: uniq/S)", DiagnosticCode.Unsupported_Kd, "uniq/S")]
    [InlineData("#Layout(\"C\")\nopen struct S\n    public var a: i32", "(value: ref/S)", DiagnosticCode.Unsupported_Kd, "ref/S")]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: i32\n    public var none: [0 of i32]", "(value: uniq/S)", DiagnosticCode.Unsupported_Kd, "uniq/S")]
    [InlineData("enum E\n    First\n    Second", "(value: E)", DiagnosticCode.Unsupported_Kd, "E")]
    [InlineData("struct S\n    public var a: i32", "(value: objref/S during a)", DiagnosticCode.Unsupported_Kd, "objref/S during a")]
    [InlineData("#Layout(\"C\")\nstruct S\n    public var a: i32", "(value: objuniq/S during a)", DiagnosticCode.Unsupported_Kd, "objuniq/S during a")]
    [InlineData("struct S\n    public var a: i32", "(value: ref/(objref/S during a))", DiagnosticCode.Unsupported_Kd, "ref/(objref/S during a)")]
    [InlineData("struct S\n    public var a: i32", "(value: obj/S)", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("struct S\n    public var a: i32", "(value: ref/rc/S)", DiagnosticCode.InvalidImportSignature_Kd, null)]
    [InlineData("struct S\n    public var a: i32", "(value: arc/S)", DiagnosticCode.InvalidImportSignature_Kd, null)]
    public void CLayoutStructBorrowsAreCExchangeable(string declarations, string parameters, DiagnosticCode? code, string? at)
    {
        var c = AnalyzeImport(declarations + "\ngroup Native\n    " + Import + "\n    public unsafe func imported" + parameters + " -> i32", "codec");
        AssertImportRecord(c, code, at, code is null);
    }

    // A borrow of Weak, which Binding does not support yet, has only Weak's own record at its Type. (A borrow of a struct whose C layout is
    // unavailable is 22.6's Unsupported_Kd above since DECISIONS R4-M134 Q8: the declaration is no error.)
    [Theory]
    [InlineData("struct S\n    public var a: i32", "(value: ref/Weak<rc/S>)", DiagnosticCode.Unsupported_Kd, "Weak")]
    public void AnImportRestsOnItsTypesOwnError(string declarations, string parameters, DiagnosticCode? code, string? at)
    {
        var c = AnalyzeImport(declarations + "\ngroup Native\n    " + Import + "\n    public unsafe func imported" + parameters + " -> i32", "codec");
        AssertImportRecord(c, code, at, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportEligibilityReadsTheTypesRejectedAttribute(bool testBuild)
    {
        const string Declaration = "#Layout(\"C\")\nstruct S\n    public var value: i32\n";
        const string Use = "group Native\n    " + Import + "\n    public unsafe func imported(value: ref/S) -> i32";
        foreach (var marker in new[] { string.Empty, "#Test\n" })
        {
            var c = Compilation.CreateForTest();
            c.IsTestBuild = testBuild;
            c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal)
            {
                ["codec"] = new() { Kind = "import" },
            };
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", marker + Declaration + Use));
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.Equal(marker.Length == 0, c.Bind().IsComplete);
                var imports = c.Binding.LibraryImports.Where(x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha).ToArray();
                var errors = CompilationTestHelper.BindingErrors(c);
                if (marker.Length == 0)
                {
                    Assert.Single(imports);
                    Assert.Empty(errors);
                }
                else
                {
                    Assert.Empty(imports);
                    var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
                    Assert.Equal(BindingState.Invalid, type.StateOf());
                    Assert.Equal(BindingFailure.None, type.FailureOf());
                    var cause = Assert.IsType<AttributeKoto>(Assert.Single(c.Binding.FailureCauses(type)!).Subject);
                    var error = Assert.Single(errors);
                    Assert.Equal("InvalidTestDefinition_Kd", error.Code);
                    Assert.Equal(cause.Span, error.Span);
                    Assert.Equal("#Test", error.Text);
                }
            }
        }
    }

    [Theory]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> i32", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: u32) -> u32", false)]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: raw/i8) -> ()", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: raw/f64) -> ()", false)]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> i32", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i64) -> i32", true)]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> i32", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()", true)]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: f32) -> ()", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()", true)]
    [InlineData("group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()", "group B\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32, other: i32) -> ()", true)]
    public void SameExternalSymbolRequiresOnePhysicalSignature(string first, string second, bool conflict)
    {
        var c = AnalyzeImport(first + "\n" + second, "codec");
        Assert.Equal(conflict, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.ConflictingImportSignature_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.InvalidImportSignature_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
    }

    [Theory]
    [InlineData("kernel32", "GetLastError", "()", "i32", null, false)]
    [InlineData("kernel32", "GetProcessHeap", "()", "raw/u8", null, false)]
    [InlineData("kernel32", "GetStdHandle", "(which: i32)", "raw/u8", null, false)]
    [InlineData("kernel32", "WriteFile", "(handle: raw/u8, data: raw/u8, length: u32, written: raw/u32, overlapped: raw/u8)", "i32", null, false)]
    [InlineData("kernel32", "GetLastError", "()", "i64", null, true)]
    [InlineData("kernel32", "GetStdHandle", "(which: i32)", "()", null, true)]
    [InlineData("kernel32", "ExitProcess", "(code: i32)", "()", null, true)]
    [InlineData("kernel32", "GetEnvironmentVariableA", "(name: raw/u8, buffer: raw/u8, size: u32)", "i32", null, false)]
    [InlineData("kernel32", "SetHandleInformation", "(handle: raw/u8, mask: u32, flags: u32)", "u32", null, false)]
    [InlineData("kernel32", "SetHandleInformation", "(handle: raw/u8, mask: u32, flags: u32)", "i64", null, true)]
    [InlineData("kernel32", "VirtualAlloc", "()", "raw/u8", null, false)]
    [InlineData("codec", "GetLastError", "()", "i32", "codec", true)]
    [InlineData("codec", "symbol", "()", "i32", "codec", false)]
    public void RuntimeDeclarationsAreSharedOnlyByAgreeingImports(string library, string symbol, string parameters, string result, string? requirement, bool conflict)
    {
        var c = AnalyzeImport(
            "group Native\n    #LibraryImport(\"" + library + "\", \"" + symbol + "\")\n    public unsafe func imported" + parameters + " -> " + result,
            requirement);
        Assert.Equal(conflict, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.ConflictingRuntimeSymbol_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.InvalidImportSignature_Kd or
            DiagnosticCode.ConflictingImportSignature_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.Equal(!conflict, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("static", "static", false)]
    [InlineData("import", "import", false)]
    [InlineData("static", "import", true)]
    [InlineData("import", "static", true)]
    public void OneExternalSymbolRequiresOneSupplyKind(string first, string second, bool conflict)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal)
        {
            ["codec"] = new() { Kind = first },
            ["codec2"] = new() { Kind = second },
        };
        Assert.True(c.Prepare(WindowsProfile.Target));
        var source = "group A\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()\n" +
            "group B\n    #LibraryImport(\"codec2\", \"shared\")\n    public unsafe func call(value: i32) -> ()";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        Assert.Equal(conflict, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.ConflictingImportSupply_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.InvalidImportSignature_Kd or
            DiagnosticCode.ConflictingImportSignature_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
    }

    [Fact]
    public void ReservedSupplyKindsParticipateInSymbolAgreement()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        Assert.True(c.Prepare(WindowsProfile.Target));
        var source = "group A\n    #LibraryImport(\"kernel32\", \"VirtualAlloc\")\n    public unsafe func reserve(size: u64) -> raw/u8\n" +
            "group B\n    #LibraryImport(\"codec\", \"VirtualAlloc\")\n    public unsafe func reserve(size: u64) -> raw/u8";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();

        // The generated kernel32 import library is an import supply; the static requirement disagrees.
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ConflictingImportSupply_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.ConflictingImportSignature_Kd or
            DiagnosticCode.ConflictingRuntimeSymbol_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
    }

    [Fact]
    public void SupplyKindsAgreeAcrossSourceModules()
    {
        var c = ModuleBindingTest.Create(
            "func main() => ()\ngroup Root\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()",
            "group Native\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()",
            configure: (root, library) =>
            {
                root.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                library.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "import" } };
            });
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ConflictingImportSupply_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.ConflictingImportSignature_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
    }

    [Theory]
    [InlineData("kernel32", "VirtualAlloc", false)]
    [InlineData("kernel32", "GetLastError", false)]
    [InlineData("kernel32", "CreateFileA", true)]
    [InlineData("kernel32", "VirtualQuery", true)]
    [InlineData("kimi_backend", "kimi_helper", true)]
    public void AReservedSupplyProvidesOnlyItsCatalog(string library, string symbol, bool unavailable)
    {
        // The backend catalog itself is made of reserved external names, so no kimi_backend import remains.
        var c = AnalyzeImport("group Native\n    #LibraryImport(\"" + library + "\", \"" + symbol + "\")\n    public unsafe func imported() -> i32", null);
        Assert.Equal(unavailable, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.UnavailableReservedImport_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.Equal(!unavailable, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ImportSignaturesAreSharedAcrossSourceModules()
    {
        var c = ModuleBindingTest.Create(
            "func main() => ()\ngroup Root\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i64) -> ()",
            "group Native\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()",
            configure: (root, library) =>
            {
                root.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                library.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
            });
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ConflictingImportSignature_Kd);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public void RequirementsBelongToTheDefiningModule(bool rootRequirement, bool libraryRequirement, bool missing)
    {
        var c = ModuleBindingTest.Create(
            "func main() => ()",
            "group Native\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported() -> i32",
            configure: (root, library) =>
            {
                if (rootRequirement)
                {
                    root.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                }

                if (libraryRequirement)
                {
                    library.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                }
            });
        Assert.Equal(!missing, c.Bind().IsComplete);
        Assert.Equal(missing, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd));
    }

    [Theory]
    [InlineData("value: i32", false)]
    [InlineData("! value: i32", false)]
    [InlineData("value => local: i32", false)]
    [InlineData("value: i32 = 1", true)]
    [InlineData("! value: i32 = 1", true)]
    public void ImportNameOmissionIsIndependentOfForbiddenDefaults(string parameter, bool invalid)
    {
        var source = "group Native\n    #LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported(" + parameter + ") -> i32";
        var c = AnalyzeImport(source, "codec");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(invalid, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidLibraryImport_Kd));
    }

    private static Compilation AnalyzeImport(string source, string? requirement, bool combinedSupply = false)
    {
        var c = Compilation.CreateForTest();
        if (requirement is not null)
        {
            if (combinedSupply)
            {
                c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { [requirement] = new() { Kind = "static", Input = requirement + ".lib" } };
            }
            else
            {
                c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { [requirement] = new() { Kind = "import" } };
            }
        }

        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        return c;
    }

    // The import's one record and its location, an error at the import and Unsupported at its Type, in this pass and after a rebind.
    private static void AssertImportRecord(Compilation c, DiagnosticCode? code, string? at, bool complete)
    {
        string[] expected = code is null ? [] : [$"{code} {at ?? Import}"];
        for (var pass = 0; pass < 2; pass++)
        {
            var records = CompilationTestHelper.BindingErrors(c).Where(static x => x.Code is nameof(DiagnosticCode.InvalidImportSignature_Kd) or nameof(DiagnosticCode.Unsupported_Kd));
            Assert.Equal(expected, records.Select(static x => $"{x.Code} {x.Text}"));
            Assert.Equal(complete, c.Binding.Result.IsComplete);
            c.Bind();
        }
    }
}
