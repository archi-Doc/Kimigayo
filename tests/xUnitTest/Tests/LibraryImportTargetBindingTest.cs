// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryImportTargetBindingTest
{
    private const string Import = "#LibraryImport(\"codec\", \"symbol\")";

    // The ProvidingLibrary design's §1 targets and signatures, and its pl-W and pl-L imports (BindImports' form).
    private const string W = "x86_64-pc-windows-msvc";
    private const string L = "x86_64-unknown-linux-musl";
    private const string N = "x86_64-unknown-linux-gnu";
    private const string VP = "(address: raw/u8, size: u64, protect: u32, old: raw/u32) -> i32";
    private const string WR = "(fd: i32, data: raw/u8, count: u64) -> i64";
    private const string SH = "(value: i32) -> ()";
    private const string PlW = "kernel32 VirtualProtect " + VP + "|codec VirtualProtect " + VP;
    private const string PlL = "c write " + WR + "|codec write " + WR;

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
        if (import.Kind != AttributeKind.LibraryImport)
        {
            import = import.AttributeChain!;
        }

        Assert.True(type.RemoveAttribute(import));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void ValidImportDeclarationCompletesBinding()
    {
        var c = AnalyzeImport("group Native\n    #LibraryImport(\"library\", \"symbol\")\n    public unsafe func imported(value: i32) -> i32", "library");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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

    // SPEC 23.1.2, 22.6 (DIAGNOSTICS 4.3): each independent requirement of an import is its own record: the name's supply, each Type without a
    // C counterpart with its ordinal as the condition, and the first Type outside the table, located at it. Every pass keeps them.
    [Theory]
    [InlineData("(a: bool, b: string)", "()", "codec", "InvalidImportSignature_Kd", "Unsupported_Kd")]
    [InlineData("(value: string)", "bool", "codec", "InvalidImportSignature_Kd", "Unsupported_Kd")]
    [InlineData("(a: string, b: string)", "()", "codec", "InvalidImportSignature_Kd", "InvalidImportSignature_Kd")]
    [InlineData("(value: string)", "()", null, "MissingNativeRequirement_Kd", "InvalidImportSignature_Kd")]
    [InlineData("(a: bool)", "()", null, "MissingNativeRequirement_Kd", "Unsupported_Kd")]
    [InlineData("(a: bool, b: string)", "()", null, "MissingNativeRequirement_Kd", "InvalidImportSignature_Kd", "Unsupported_Kd")]
    [InlineData("(a: i32)", "()", null, "MissingNativeRequirement_Kd")]
    public void AnImportReportsEachIndependentRequirement(string parameters, string result, string? requirement, params string[] codes)
    {
        var c = AnalyzeImport("group Native\n    " + Import + "\n    public unsafe func imported" + parameters + " -> " + result, requirement);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(codes.Order(), CompilationTestHelper.BindingErrors(c).Select(static x => x.Code).Order());
            Assert.False(c.Binding.Result.IsComplete);
            c.Bind();
        }
    }

    // The final symbol table's verdicts stay beside a failed supply: the second import conflicts with the first whatever else failed.
    [Fact]
    public void ASymbolConflictStaysBesideAMissingSupply()
    {
        var imports = "    " + Import + "\n    public unsafe func first(a: i32) -> i32\n    " + Import + "\n    public unsafe func second(a: i64) -> i32";
        var c = AnalyzeImport("group Native\n" + imports, null);
        Assert.Equal(
            ["ConflictingImportSignature_Kd", "MissingNativeRequirement_Kd", "MissingNativeRequirement_Kd"],
            CompilationTestHelper.BindingErrors(c).Select(static x => x.Code).Order());
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

    // PLAN G647 (SPEC 22.6): a borrow of a C-exchangeable referent is C-exchangeable through every borrow layer, past the 32 steps the
    // walk once counted; a borrow of bool is the located Unsupported_Kd at every depth.
    [Theory]
    [InlineData("i32", 33, null)]
    [InlineData("i32", 65, null)]
    [InlineData("bool", 2, DiagnosticCode.Unsupported_Kd)]
    [InlineData("bool", 33, DiagnosticCode.Unsupported_Kd)]
    [InlineData("bool", 65, DiagnosticCode.Unsupported_Kd)]
    public void ABorrowIsCExchangeableThroughEveryLayer(string terminal, int layers, DiagnosticCode? code)
    {
        var type = TypeDagScalingTest.RefLayers(layers, terminal);
        var c = AnalyzeImport("group Native\n    " + Import + "\n    public unsafe func imported(value: " + type + ") -> i32", "codec");
        AssertImportRecord(c, code, code is null ? null : type, code is null);
    }

    // The walk's steps grow with the borrow layers (TermWalkWork counts each Type it enters).
    [Fact]
    public void TheCExchangeableWalkIsLinearInTheLayers()
    {
        var work = new[] { 16, 32, 64 }.Select(static layers =>
        {
            var c = AnalyzeImport("group Native\n    " + Import + "\n    public unsafe func imported(value: " + TypeDagScalingTest.RefLayers(layers, "i32") + ") -> i32", "codec");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return (long)c.Binding.TermWalkWork;
        }).ToArray();
        Assert.True(work[1] > work[0] && MirBorrowsTest.Grows(work, 2), string.Join(' ', work));
    }

    // SPEC 22.6 admits a C-layout struct with eligible Fields at any nesting, and (the owner's O-138 (a)) one that reaches itself through a
    // borrow. Until the O-138 (a) checker unit the Field steps of one path stop at a limit (Binding.CLayoutFieldBound, PLAN G647): 32
    // nested structs are judged, and 33, or a struct that reaches itself, are the import's located Unsupported_Kd, never a stack failure.
    [Theory]
    [InlineData(32, null)]
    [InlineData(33, DiagnosticCode.Unsupported_Kd)]
    [InlineData(0, DiagnosticCode.Unsupported_Kd)]
    public void CLayoutFieldStepsStopAtTheLimit(int structs, DiagnosticCode? code)
    {
        var declarations = structs == 0 ? "#Layout(\"C\")\nstruct S0\n    public var v: i32\n    public var next: ref/S0 during static\n"
            : "#Layout(\"C\")\nstruct S1\n    public var v: i32\n" +
                string.Concat(Enumerable.Range(2, structs - 1).Select(static k => $"#Layout(\"C\")\nstruct S{k}\n    public var f: S{k - 1}\n"));
        var c = AnalyzeImport(declarations + "group Native\n    " + Import + $"\n    public unsafe func imported(value: ref/S{structs}) -> i32", "codec");
        AssertImportRecord(c, code, code is null ? null : $"ref/S{structs}", code is null);
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
            c.Project.ProjectFile.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal)
            {
                ["codec"] = new() { Kind = "import" },
            };
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
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
    [InlineData("kernel32", "GetStdHandle", "(which: i32)", "raw/u8", null, false)]
    [InlineData("kernel32", "WriteFile", "(handle: raw/u8, data: raw/u8, length: u32, written: raw/u32, overlapped: raw/u8)", "i32", null, false)]
    [InlineData("kernel32", "GetLastError", "()", "i64", null, true)]
    [InlineData("kernel32", "GetStdHandle", "(which: i32)", "()", null, true)]
    [InlineData("kernel32", "ExitProcess", "(code: i32)", "()", null, true)]
    [InlineData("kernel32", "GetEnvironmentVariableA", "(name: raw/u8, buffer: raw/u8, size: u32)", "i32", null, false)]
    [InlineData("kernel32", "SetHandleInformation", "(handle: raw/u8, mask: u32, flags: u32)", "u32", null, false)]
    [InlineData("kernel32", "SetHandleInformation", "(handle: raw/u8, mask: u32, flags: u32)", "i64", null, true)]
    [InlineData("kernel32", "VirtualAlloc", "()", "raw/u8", null, true)]
    [InlineData("kernel32", "VirtualAlloc", "(address: raw/u8, size: u64, kind: u32, protect: u32)", "raw/u8", null, false)]
    [InlineData("kernel32", "VirtualFree", "(address: raw/u8, size: u64, kind: u32)", "i32", null, false)]
    [InlineData("kimi_backend", "GetLastError", "()", "i32", null, true)]
    [InlineData("codec", "GetLastError", "()", "i32", "codec", true)]
    [InlineData("codec", "HeapAlloc", "(heap: raw/u8, flags: u32, bytes: u64)", "raw/u8", "codec", false)] // B7r3: no longer a runtime declaration.
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

    // IMPL 21.2: the imports of one external symbol agree in their physical signature and their providing library, which is the reserved
    // supply an import names (every Kotonoha's, every row's), or else its defining Kotonoha's logical name (SPEC 22.6, IMPL 20.4; the
    // ProvidingLibrary design §2, R1): two libraries of one Kotonoha are two providers even with one input or one Kind (rows 9-11, K1),
    // and Kind is never compared (one provider, one Kind). Each requirement is its own record at the later import (SPEC 23.1.2, T's ruling
    // 1); a later import's limit Unsupported_Kd gives way to its conflict, a record its own checks make stands beside it (ruling 1a). The
    // old equal-Kind rows' acceptance was unsound (C-501 C1): their conflict is a stronger expectation, not a weaker one. Arguments: the
    // design's row (K: the replaced tests' forms), the target, the configuration and the imports (Configure, Groups).
    [Theory]
    [InlineData("1", W, "codec=import", PlW, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("2", L, "codec=static", PlL, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("3-W", W, "codec=static", "codec shared " + SH + "|codec shared " + SH)]
    [InlineData("3-L", L, "codec=static", "codec shared " + SH + "|codec shared " + SH)]
    [InlineData("4", L, "codec=static", PlW, "Unsupported_Kd@Hello.kimi:G0", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("5", W, "codec=static", PlL, "Unsupported_Kd@Hello.kimi:G0", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("6", N, W + ">codec=import", PlW, "ConflictingImportSupply_Kd@Hello.kimi:G1", "MissingNativeRequirement_Kd@Hello.kimi:G1")]
    [InlineData("7-W", W, "", "kernel32 VirtualProtect " + VP + "|kernel32 VirtualProtect " + VP)]
    [InlineData("7-L", L, "", "kernel32 VirtualProtect " + VP + "|kernel32 VirtualProtect " + VP, "Unsupported_Kd@Hello.kimi:G0", "Unsupported_Kd@Hello.kimi:G1")]
    [InlineData("8-L", L, "", "c write " + WR + "|c write " + WR)]
    [InlineData("8-W", W, "", "c write " + WR + "|c write " + WR, "Unsupported_Kd@Hello.kimi:G0", "Unsupported_Kd@Hello.kimi:G1")]
    [InlineData("9", W, "codec=static codec2=static", "codec shared " + SH + "|codec2 shared " + SH, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("10", W, "codec=static:native/codec.lib codec2=static:native/codec.lib", "codec shared " + SH + "|codec2 shared " + SH, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("11", W, "codec=static codec2=import", "codec shared " + SH + "|codec2 shared " + SH, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("12", W, "codec=static codec2=static", "codec shared " + SH + "|codec2 shared (value: i64) -> ()", "ConflictingImportSignature_Kd@Hello.kimi:G1", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("13", W, "codec=static", "codec shared " + SH + "|codec shared (value: i64) -> ()", "ConflictingImportSignature_Kd@Hello.kimi:G1")]
    [InlineData("14", W, "codec=import", "kernel32 VirtualProtect " + VP + "|codec VirtualProtect (address: raw/u8, flag: bool) -> i32", "ConflictingImportSupply_Kd@Hello.kimi:G1", "Unsupported_Kd@Hello.kimi:bool")]
    [InlineData("15", W, "", PlW, "ConflictingImportSupply_Kd@Hello.kimi:G1", "MissingNativeRequirement_Kd@Hello.kimi:G1")]
    [InlineData("25", W, "codec=import", "kernel32 GetLastError () -> u32|codec GetLastError () -> u32", "ConflictingImportSupply_Kd@Hello.kimi:G1", "ConflictingRuntimeSymbol_Kd@Hello.kimi:G1")]
    [InlineData("28", W, "codec=import", "codec VirtualProtect " + VP + "|kernel32 VirtualProtect " + VP, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("29", W, "codec=static", "codec write " + WR + "|c write " + WR, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("30", L, "codec=import", "codec VirtualProtect " + VP + "|kernel32 VirtualProtect " + VP, "Unsupported_Kd@Hello.kimi:G0", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("31", L, "codec=static", "codec VirtualProtect " + VP + "|kernel32 VirtualProtect " + VP, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("32", L, "codec=import", PlL, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("33", W, "codec=static", "codec shared (value: bool) -> ()|codec shared " + SH + "|codec shared (value: i64) -> ()", "Unsupported_Kd@Hello.kimi:bool", "ConflictingImportSignature_Kd@Hello.kimi:G2")]
    [InlineData("34", W, "codec=import", "codec VirtualProtect (address: raw/u8, flag: bool) -> i32|kernel32 VirtualProtect " + VP, "Unsupported_Kd@Hello.kimi:bool", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("35", W, "codec=import", "kernel32 FooBar () -> i32|codec FooBar () -> i32", "UnavailableReservedImport_Kd@Hello.kimi:G0", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("K1", W, "codec=import codec2=import", "codec shared " + SH + "|codec2 shared " + SH, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("K1b", W, "codec=import codec2=static", "codec shared " + SH + "|codec2 shared " + SH, "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("K2", W, "codec=static", "kernel32 VirtualProtect (size: u64) -> raw/u8|codec VirtualProtect (size: u64) -> raw/u8", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    [InlineData("K3", W, "codec=import", "kimi_backend kimi_helper (size: u64) -> raw/u8|codec kimi_helper (size: u64) -> raw/u8", "UnavailableReservedImport_Kd@Hello.kimi:G0", "ConflictingImportSupply_Kd@Hello.kimi:G1")]
    public void OneExternalSymbolHasOneProvidingLibrary(string row, string target, string configuration, string imports, params string[] expected)
    {
        _ = row;
        AssertRecordsAtImports(BindImports(target, configuration, imports), expected);
    }

    // IMPL 20.4, SPEC 22.6: a program library's name is its own Kotonoha's, so two modules' imports of one symbol from their own codec name
    // two providing libraries, whatever their Kinds; a reserved supply is every module's (DECISIONS BU-92 as T-M13 amends it). The root's
    // import is first; the dependency's conflict replaces BU-92's limit at its import (ruling 1a). Arguments: the design's row, the target,
    // then the root's and the dependency's configuration and imports.
    [Theory]
    [InlineData("16", L, "", "c write " + WR, "", "c write " + WR)]
    [InlineData("17", W, "", "kernel32 VirtualProtect " + VP, "", "kernel32 VirtualProtect " + VP)]
    [InlineData("18", W, "codec=static", "codec shared " + SH, "codec=static", "codec shared " + SH, "ConflictingImportSupply_Kd@library.kimi:G0")]
    [InlineData("19", W, "codec=static", "codec shared " + SH, "codec=import", "codec shared " + SH, "ConflictingImportSupply_Kd@library.kimi:G0")]
    [InlineData("20", W, "", "kernel32 VirtualProtect " + VP, "codec=import", "codec VirtualProtect " + VP, "ConflictingImportSupply_Kd@library.kimi:G0")]
    [InlineData("26", W, "codec=import", "codec VirtualProtect " + VP, "", "kernel32 VirtualProtect " + VP, "ConflictingImportSupply_Kd@library.kimi:G0")]
    [InlineData("27", L, "codec=static", "codec write " + WR, "", "c write " + WR, "ConflictingImportSupply_Kd@library.kimi:G0")]
    public void AProgramLibraryNameIsItsModules(string row, string target, string rootConfiguration, string root, string libraryConfiguration, string library, params string[] expected)
    {
        _ = row;
        var c = ModuleBindingTest.Create(
            "func main() => ()\n" + Groups(root),
            Groups(library),
            configure: (rootFile, libraryFile) =>
            {
                Configure(rootFile, target, rootConfiguration);
                Configure(libraryFile, target, libraryConfiguration);
            },
            row: TargetProfile.Find(target));
        c.Bind();
        AssertRecordsAtImports(c, expected);
    }

    // IMPL 21.2 (the ProvidingLibrary design's R3): the Kimi library's imports are imports of the compilation, so a program's import of
    // Kimi.Windows' kernel32 QueryPerformanceCounter shares its symbol only with its signature and library; on linux-x64 the group is not
    // declared (SPEC 22.8.3), so no other import of the symbol exists there. Arguments: the design's row, the target, the configuration
    // and the import.
    [Theory]
    [InlineData("21", W, "", "kernel32 QueryPerformanceCounter (value: uniq/i64) -> i32")]
    [InlineData("22", W, "", "kernel32 QueryPerformanceCounter () -> i64", "ConflictingImportSignature_Kd@Hello.kimi:G0")]
    [InlineData("23", W, "codec=static", "codec QueryPerformanceCounter (value: uniq/i64) -> i32", "ConflictingImportSupply_Kd@Hello.kimi:G0")]
    [InlineData("24", L, "", "kernel32 QueryPerformanceCounter () -> i64", "Unsupported_Kd@Hello.kimi:G0")]
    public void ProgramImportsShareTheLibrarysSymbolTable(string row, string target, string configuration, string imports, params string[] expected)
    {
        _ = row;
        AssertRecordsAtImports(BindImports(target, configuration, imports), expected);
    }

    // The process heap's three left kernel32's set with the runtime's declarations (the Tatara plan's decision 23, B7r3): an import with the
    // signature the runtime declared them with is unavailable, as is an import of any other kernel32 function the set lacks; row 27's
    // valid twin, VirtualProtect with HeapAlloc's signature, stays accepted.
    [Theory]
    [InlineData("kernel32", "VirtualProtect", "() -> i32", false)]
    [InlineData("kernel32", "VirtualProtect", "(heap: raw/u8, flags: u32, bytes: u64) -> raw/u8", false)]
    [InlineData("kernel32", "GetLastError", "() -> i32", false)]
    [InlineData("kernel32", "CreateFileA", "() -> i32", true)]
    [InlineData("kernel32", "VirtualQuery", "() -> i32", true)]
    [InlineData("kernel32", "GetProcessHeap", "() -> raw/u8", true)]
    [InlineData("kernel32", "HeapAlloc", "(heap: raw/u8, flags: u32, bytes: u64) -> raw/u8", true)]
    [InlineData("kimi_backend", "kimi_helper", "() -> i32", true)]
    public void AReservedSupplyProvidesOnlyItsCatalog(string library, string symbol, string signature, bool unavailable)
    {
        // The backend catalog itself is made of reserved external names, so no kimi_backend import remains.
        var c = AnalyzeImport("group Native\n    #LibraryImport(\"" + library + "\", \"" + symbol + "\")\n    public unsafe func imported" + signature, null);
        Assert.Equal(unavailable, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.UnavailableReservedImport_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.Equal(!unavailable, c.Binding.Result.IsComplete);
    }

    // Row 12 across modules: the dependency's import disagrees in signature and providing library (its own codec), two records.
    [Fact]
    public void ImportSignaturesAreSharedAcrossSourceModules()
    {
        var c = ModuleBindingTest.Create(
            "func main() => ()\ngroup Root\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i64) -> ()",
            "group Native\n    #LibraryImport(\"codec\", \"shared\")\n    public unsafe func call(value: i32) -> ()",
            configure: (root, library) =>
            {
                root.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                library.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
            });
        Assert.False(c.Bind().IsComplete);
        AssertRecordsAtImports(c, ["ConflictingImportSignature_Kd@library.kimi:G0", "ConflictingImportSupply_Kd@library.kimi:G0"]);
    }

    // IMPL 20.4: a requirement belongs to the module defining the import. A dependency module's supplies are not linked yet (DECISIONS BU-92),
    // so its import of a library other than a reserved supply is the located Unsupported_Kd at the #LibraryImport, after the import's own checks:
    // a missing requirement stays the Language error alone. Either way the import is no LibraryImports entry publication would link.
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
                    root.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                }

                if (libraryRequirement)
                {
                    library.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
                }
            });
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(missing, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd));
        var unsupported = c.Binding.Issues.Where(x => x.Code == DiagnosticCode.Unsupported_Kd).ToArray();
        Assert.Equal(missing ? 0 : 1, unsupported.Length);
        Assert.All(unsupported, x => Assert.IsType<AttributeKoto>(x.Node));
        Assert.DoesNotContain(c.Binding.LibraryImports, x => x.Library == "codec");
    }

    // DECISIONS BU-92's twin: a dependency module's kernel32 import is linked, through the generated kernel32 import library.
    [Fact]
    public void ADependencyModuleImportsKernel32()
    {
        var c = ModuleBindingTest.Create("func main() => ()", "group Native\n    #LibraryImport(\"kernel32\", \"VirtualProtect\")\n    public unsafe func imported() -> i32");
        Assert.True(c.Bind().IsComplete);
        Assert.Contains(c.Binding.LibraryImports, x => x.Symbol == "VirtualProtect" && x.Decl.Declaration.CodeContext.Kotonoha != c.Kotonoha);
    }

    // IMPL 20.4: the compiler links its reserved supplies for every module, so a dependency module's kimi_backend import has the
    // root's records alone (DECISIONS BU-92): a catalog symbol is a reserved name (IMPL 21.2), any other one unavailable (IMPL 21.4.7).
    [Theory]
    [InlineData(false, "memcpy", DiagnosticCode.InvalidLibraryImport_Kd)]
    [InlineData(true, "memcpy", DiagnosticCode.InvalidLibraryImport_Kd)]
    [InlineData(true, "kimi_helper", DiagnosticCode.UnavailableReservedImport_Kd)]
    public void AModuleImportsTheBackendSupplyAsTheRootDoes(bool dependency, string symbol, DiagnosticCode code)
    {
        var import = "group Native\n    #LibraryImport(\"kimi_backend\", \"" + symbol + "\")\n    public unsafe func imported() -> i32";
        var c = ModuleBindingTest.Create(dependency ? "func main() => ()" : "func main() => ()\n" + import, dependency ? import : string.Empty);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal([code], c.Binding.Issues.Select(x => x.Code));
    }

    // The Tatara plan's B7b5 (IMPL 20.4, 21.2; SPEC 22 ¶1, Reading 2; design §5 rows 9-11, 31-33, 46): a reserved supply is any target row's
    // supply of the name and the reserved external names are the table's, so an import of a symbol no row's supply of that name provides is
    // unavailable, and one of a reserved name (a memory function, main) invalid, on every target; the selected row decides only the located
    // Unsupported_Kd at the import, for another row's supply or symbol (the Windows import of c's write, the Linux one of kernel32's
    // GetLastError, which joins Windows' runtime declaration with no conflict; both on the staged none-x64 row, whose one supply is its
    // kimi_backend); a target without a row (x86_64-unknown-linux-gnu) reports none (G2). The Tatara plan's B9e (IMPL 21.2, 20.4; design
    // §5 rows 15 and 16, change N1): the none row's kimi_backend symbols, compiler-rt's soft-float routines and its fp_mode.c's, are
    // reserved external names on every target, so a program library's import of one is invalid on each, as is the none row's own
    // kimi_backend import of one (the name checked before the supply); a name outside the catalog stays a program library's.
    // Fail-before (B9e): the windows-x64 import of __divdf3 was accepted, the none row's kimi_backend one UnavailableReservedImport_Kd.
    // Each expected code is a record at the import, in every pass.
    [Theory]
    [InlineData("c", "write", null, "x86_64-unknown-linux-musl", "")]
    [InlineData("c", "write", null, "x86_64-unknown-linux-gnu", "")]
    [InlineData("c", "write", null, "x86_64-pc-windows-msvc", "Unsupported_Kd")]
    [InlineData("c", "write", null, "x86_64-unknown-none", "Unsupported_Kd")]
    [InlineData("kernel32", "GetLastError", null, "x86_64-unknown-linux-musl", "Unsupported_Kd")]
    [InlineData("kernel32", "GetLastError", null, "x86_64-unknown-none", "Unsupported_Kd")]
    [InlineData("kernel32", "GetLastError", null, "x86_64-unknown-linux-gnu", "")]
    [InlineData("c", "getpid", null, "x86_64-pc-windows-msvc", "UnavailableReservedImport_Kd")]
    [InlineData("c", "getpid", null, "x86_64-unknown-linux-musl", "UnavailableReservedImport_Kd")]
    [InlineData("kimi_backend", "kimi_monotonic_clock", null, "x86_64-unknown-linux-musl", "UnavailableReservedImport_Kd")]
    [InlineData("c", "memcpy", null, "x86_64-pc-windows-msvc", "InvalidLibraryImport_Kd")]
    [InlineData("c", "memcpy", null, "x86_64-unknown-linux-musl", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "memset", "codec", "x86_64-pc-windows-msvc", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "memset", "codec", "x86_64-unknown-linux-musl", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "main", "codec", "x86_64-pc-windows-msvc", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "main", "codec", "x86_64-unknown-linux-musl", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "__divdf3", "codec", "x86_64-pc-windows-msvc", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "__divdf3", "codec", "x86_64-unknown-linux-musl", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "__divdf3", "codec", "x86_64-unknown-none", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "__fe_getround", "codec", "x86_64-pc-windows-msvc", "InvalidLibraryImport_Kd")]
    [InlineData("codec", "codec_divide", "codec", "x86_64-pc-windows-msvc", "")]
    [InlineData("kimi_backend", "__divdf3", null, "x86_64-unknown-none", "InvalidLibraryImport_Kd")]
    public void ReservedSuppliesAndNamesAreTheTablesOnEveryTarget(string library, string symbol, string? requirement, string target, string code)
    {
        var import = "#LibraryImport(\"" + library + "\", \"" + symbol + "\")";
        var c = AnalyzeImport("group Native\n    " + import + "\n    public unsafe func imported() -> i32", requirement, target: target);
        AssertRecordsAtImports(c, code.Length == 0 ? [] : [code + "@Hello.kimi:G0"]);
    }

    // IMPL 20.4: the project file accepts a requirement's Kind under the target's key, and the selected row's Kinds decide each import of
    // that library: an import library's imports are each the located Unsupported_Kd on a static-only row (linux-x64,
    // x86_64-unknown-none) and have no record on windows-x64, as a static library's have on x86_64-unknown-none.
    [Theory]
    [InlineData("x86_64-unknown-linux-musl", "import", "Unsupported_Kd")]
    [InlineData("x86_64-unknown-none", "import", "Unsupported_Kd")]
    [InlineData("x86_64-unknown-none", "static", "")]
    [InlineData("x86_64-pc-windows-msvc", "import", "")]
    public void AKindOutsideTheRowsKindsIsUnsupported(string target, string kind, string code)
    {
        var file = ProjectFile.Load(Encoding.UTF8.GetBytes("NativeRequirements={\"" + target + "\"={codec={Kind=\"" + kind + "\"}}}"))!;
        Assert.Equal(kind, file.NativeRequirements[target]["codec"].Kind);
        var c = BindImports(target, "codec=" + kind, "codec first () -> i32|codec second () -> i32");
        AssertRecordsAtImports(c, code.Length == 0 ? [] : [code + "@Hello.kimi:G0", code + "@Hello.kimi:G1"]);
    }

    // The Tatara plan's G1 (i) (SPEC 22 ¶1, IMPL 21.2; design §5 row 45): a runtime declaration's conflict is judged against every row's
    // declarations, so codec's GetLastError conflicts with Windows' on every target, a target without a row included, where the missing
    // requirement is its own record (SPEC 23.1.2).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "ConflictingRuntimeSymbol_Kd")]
    [InlineData("x86_64-unknown-linux-musl", "ConflictingRuntimeSymbol_Kd")]
    [InlineData("x86_64-unknown-linux-gnu", "ConflictingRuntimeSymbol_Kd MissingNativeRequirement_Kd")]
    public void ARuntimeDeclarationsConflictIsTargetIndependent(string target, string codes)
    {
        var c = Compilation.CreateForTest();
        foreach (var row in TargetProfile.Rows)
        {
            c.Project.ProjectFile.NativeRequirements[row.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        }

        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "group Native\n    #LibraryImport(\"codec\", \"GetLastError\")\n    public func lastError() -> i32"));
        c.Bind();
        Assert.Equal(codes.Split(' ').Order(StringComparer.Ordinal), CompilationTestHelper.BindingErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    // The Tatara plan's B7b5 (IMPL 20.4, 21.5.2, R1; DECISIONS BU-92; design §5 rows 9 and 38): c's write on linux-x64 is linked, imported by
    // the root or by a dependency module (the compiler links every reserved supply for every module), and the module takes the C path:
    // it requires c alone and defines main (B7b2b's frame).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLinuxRowLinksItsCSupplyForEveryModule(bool dependency)
    {
        const string Write = "group Native\n    #LibraryImport(\"c\", \"write\")\n    public unsafe func write(fd: i32, data: raw/u8, count: u64) -> i64";
        var c = dependency
            ? ModuleBindingTest.Create("public func main() => ()", Write, row: TargetProfile.Linux)
            : AnalyzeImport(Write + "\npublic func main() => ()", null, target: TargetProfile.Linux.Target);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(dependency, Assert.Single(c.Binding.LibraryImports, x => x.Library == "c").Decl.Declaration.CodeContext.Kotonoha != c.Kotonoha);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Equal([("c", "static")], c.Generated.Libraries);
        Assert.Contains("define i32 @main() #0 {\n", ir, StringComparison.Ordinal);
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

    // A program bound for a target triple (windows-x64 unless one is given), the requirement, if any, under its key.
    private static Compilation AnalyzeImport(string source, string? requirement, bool combinedSupply = false, string? target = null)
    {
        var c = Compilation.CreateForTest();
        target ??= TargetProfile.Windows.Target;
        if (requirement is not null)
        {
            if (combinedSupply)
            {
                c.Project.ProjectFile.NativeLibraries[target] = new() { [requirement] = new() { Kind = "static", Input = requirement + ".lib" } };
            }
            else
            {
                c.Project.ProjectFile.NativeRequirements[target] = new(StringComparer.Ordinal) { [requirement] = new() { Kind = "import" } };
            }
        }

        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        return c;
    }

    // A program of groups G0, G1, ... importing `library symbol signature` each (`|` between them), bound for a target (Configure).
    private static Compilation BindImports(string target, string configuration, string imports)
    {
        var c = Compilation.CreateForTest();
        Configure(c.Project.ProjectFile, target, configuration);
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Groups(imports)));
        c.Bind();
        return c;
    }

    // Groups G0, G1, ..., each one import `library symbol signature` (`|` between them) of a function call.
    private static string Groups(string imports)
        => string.Concat(imports.Split('|').Select(static (x, i) =>
        {
            var parts = x.Split(' ', 3);
            return $"group G{i}\n    #LibraryImport(\"{parts[0]}\", \"{parts[1]}\")\n    public unsafe func call{parts[2]}\n";
        }));

    // A project file's native entries, space-separated: `name=Kind` a requirement, `name=Kind:input` a supply with that Input, under the
    // target's key, or under another target's key `target>name=...`.
    private static void Configure(ProjectFile file, string target, string configuration)
    {
        foreach (var entry in configuration.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = entry.IndexOf('>');
            var key = at < 0 ? target : entry[..at];
            var parts = entry[(at + 1)..].Split('=', ':');
            if (parts.Length == 3)
            {
                if (!file.NativeLibraries.TryGetValue(key, out var supplies))
                {
                    file.NativeLibraries[key] = supplies = new();
                }

                supplies[parts[0]] = new() { Kind = parts[1], Input = parts[2] };
            }
            else
            {
                if (!file.NativeRequirements.TryGetValue(key, out var requirements))
                {
                    file.NativeRequirements[key] = requirements = new(StringComparer.Ordinal);
                }

                requirements[parts[0]] = new() { Kind = parts[1] };
            }
        }
    }

    // Each expected record in order, `Code@document:G<i>` at the i-th #LibraryImport of its document (by span order) or `Code@document:text`
    // at another node, and the verdict (complete exactly when none is expected), in this pass and after a rebind.
    private static void AssertRecordsAtImports(Compilation c, string[] expected)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(expected, CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code}@{x.Path}:{At(c, x)}"));
            Assert.Equal(expected.Length == 0, c.Binding.Result.IsComplete);
            c.Bind();
        }

        static string? At(Compilation c, TestDiagnostic record)
            => record.Text?.StartsWith("#LibraryImport", StringComparison.Ordinal) != true ? record.Text
                : $"G{c.Diagnostics.FindDocument(record.Path!)!.SourceText.AsSpan(0, record.Span.Start).Count("#LibraryImport".AsSpan())}";
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
