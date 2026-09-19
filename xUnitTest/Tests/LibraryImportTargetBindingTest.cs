// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryImportTargetBindingTest
{
    [Theory]
    [InlineData("contract C\n#LibraryImport(\"library\", \"symbol\")\nstruct S\n    Self is C")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\ncontract C\nstruct S\n    Self is C")]
    public void NonFunctionImportTargetCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        var restoredType = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var restoredContract = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(restored.Binding.GetConformanceDefinition(restoredType.BoundType!, restoredContract.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void ImportOnPropertyCannotCertifyStorage()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    #LibraryImport(\"library\", \"symbol\")\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
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
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Layout(\"C\")\n#LibraryImport(\"library\", \"symbol\")")]
    [InlineData("#LibraryImport(\"library\", \"symbol\")\n#Layout(\"C\")")]
    public void RemovingImportFromChainRestoresConformance(string markers)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n" + markers + "\nstruct S\n    Self is C");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        var definition = c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!;
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
    public void ForeignFunctionSupportRemainsUnimplemented()
    {
        var c = AnalyzeImport("group Native\n    #LibraryImport(\"library\", \"symbol\")\n    public unsafe func imported(value: i32) -> i32", "library");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.InvalidLibraryImport_Kd or DiagnosticCode.MissingNativeRequirement_Kd);
        Assert.False(c.Binding.Result.IsComplete);
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
        Assert.False(c.Binding.Result.IsComplete);
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
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"memcpy\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"__kimi_entry\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"llvm.trap\")\n    public unsafe func imported() -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported(value: i32 = 1) -> i32")]
    [InlineData("#LibraryImport(\"codec\", \"symbol\")\n    public unsafe func imported<T>(value: i32) -> i32")]
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
    public void ImportPlacementIsRestricted(string source, bool invalid)
    {
        var c = AnalyzeImport(source, "codec");
        Assert.Equal(invalid, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidLibraryImport_Kd));
        Assert.False(c.Binding.Result.IsComplete);
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
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(missing, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.MissingNativeRequirement_Kd));
    }

    private static Compilation AnalyzeImport(string source, string? requirement, bool combinedSupply = false)
    {
        var c = Compilation.CreateForTest();
        if (requirement is not null)
        {
            if (combinedSupply)
            {
                c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new(StringComparer.Ordinal) { [requirement] = new() { Kind = "static", Input = requirement + ".lib" } };
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

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }
}
