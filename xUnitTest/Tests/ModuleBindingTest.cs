// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ModuleBindingTest
{
    [Theory]
    [InlineData("public func main() => Lib.Api.value()", true)]
    [InlineData("alias Lib.Api\npublic func main() => value()", true)]
    [InlineData("public func main() => ::Lib.Api.value()", true)]
    [InlineData("public func main() => Api.value()", false)]
    [InlineData("public func main() => value()", false)]
    [InlineData("public group Lib\npublic func main() => ()", false)]
    public void DirectNamesAreModuleLocal(string root, bool expected)
    {
        var compilation = Create(root, "public group Api\n    public func value() => ()");
        Assert.Equal(expected, compilation.Bind().IsComplete);
        if (expected)
        {
            Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(compilation.Ownership.Analyze().IsVerified);
            Assert.False(compilation.Emission.Validate(out _));
        }
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    [InlineData("private", false)]
    public void ImportedMembersRetainAccess(string access, bool expected)
    {
        var compilation = Create("public func main() => Lib.Api.value()", $"public group Api\n    {access} func value() => ()");
        Assert.Equal(expected, compilation.Bind().IsComplete);
    }

    [Theory]
    [InlineData("public func unused() => missing()", false, true)]
    [InlineData("public func unused()\n    let text = \"moved\"\n    ::Core.writeLine(text)\n    ::Core.writeLine(text)", true, false)]
    [InlineData("public func unused() => ()", true, true)]
    public void UnusedDependencyBodiesAreChecked(string library, bool bound, bool owned)
    {
        var compilation = Create("public func main() => ()", library);
        Assert.Equal(bound, compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        if (bound)
        {
            Assert.Equal(owned, compilation.Ownership.Analyze().IsVerified);
        }
    }

    [Fact]
    public void DependencyRuntimeCannotBecomeApplicationStartup()
    {
        var compilation = Create("public func main() => ()", "::Core.writeLine(\"illegal\")");
        Assert.True(compilation.Bind().IsComplete);
        Assert.False(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Contains(compilation.Binding.StartupIssues, x => x.Code == DiagnosticCode.LibraryRuntimeBody_Kd);
    }

    [Fact]
    public void DependencyTestsAreNotProductDeclarations()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()\n    #Test\n    public func test() => TestOnly.missing()");
        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => Lib.Api.test()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void AliasesDoNotLeakBetweenDocumentsOrModules()
    {
        var compilation = Create("alias Lib.Api\npublic func main() => value()", "public group Api\n    public func value() => ()");
        compilation.Kotonoha.AddSource(new SourceDocument("other.kimi", "public func other() => value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void DependenciesResolveTheirOwnDirectNames()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => Child.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => Child.Api.value()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void RebindingRetainsModuleSymbolsAndInvalidatesAllBodies()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
        Assert.False(compilation.Ownership.Result.IsVerified);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        compilation.SourceModules[1].AddSource(new SourceDocument("bad.kimi", "public func broken() => missing()"));
        Assert.False(compilation.Bind().IsComplete);
        Assert.False(compilation.Ownership.Analyze().IsVerified);
    }

    [Fact]
    public void CompileTimeSettingsBelongToEachModule()
    {
        var compilation = Create("#if feature\npublic func main() => Lib.Api.value()", "#if feature\npublic func broken() => missing()\n#if not feature\npublic group Api\n    public func value() => ()", configure: (root, library) =>
        {
            root.CompileTimeSettings.Add("feature", new() { Bool = true });
            library.CompileTimeSettings.Add("feature", new() { Bool = false });
        });
        Assert.True(compilation.Bind().IsComplete, string.Join("; ", compilation.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + " | " + string.Join("; ", compilation.SourceModules.SelectMany(x => x.DiagnosticCollection.GetArray()).Select(x => x.ToString())));
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("Lib.Api", true)]
    [InlineData("Lib.Api.", false)]
    [InlineData("Api", false)]
    [InlineData("Lib.Missing", false)]
    public void DefaultAliasesResolveDirectContainerPaths(string alias, bool expected)
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = [alias]);
        Assert.Equal(expected, compilation.Bind().IsComplete);
    }

    [Fact]
    public void LibraryDefaultsDoNotLeakToTheCaller()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => helper()\nprivate group Implementation\n    public func helper() => ()", configure: (_, library) => library.Alias = ["Implementation"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "public func bad() => helper()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void PreparedDefaultAliasesAreFrozen()
    {
        var aliases = new[] { "Lib.Api" };
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = aliases);
        aliases[0] = "Missing";
        Assert.True(compilation.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasesCombineOverloadsWithoutChangingSymbolIdentity(bool reverse)
    {
        var aliases = reverse ? "alias Lib.Second\nalias Lib.First" : "alias Lib.First\nalias Lib.Second";
        var compilation = Create(aliases + "\npublic func main()\n    choose(1@i32)\n    choose(true)", "public group First\n    public func choose(value: i32) => ()\npublic group Second\n    public func choose(value: bool) => ()");
        Assert.True(compilation.Bind().IsComplete);
        compilation.Binding.CheckStartup(OutputKind.Application);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
        Assert.True(compilation.Bind().IsComplete);
    }

    [Fact]
    public void ExplicitAliasesPrecedeDefaultsAndCannotUseDefaultsToResolvePaths()
    {
        var compilation = Create("alias Local\npublic group Local\n    public func value() -> i32 => 1\npublic func main()\n    let x: i32 = value()", "public group Api\n    public func value() -> bool => true", configure: (root, _) => root.Alias = ["Lib.Api"]);
        Assert.True(compilation.Bind().IsComplete);
        compilation.Kotonoha.AddSource(new SourceDocument("bad.kimi", "alias Api\npublic func bad() => ()"));
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void RootQualificationDoesNotUseAnAlias()
    {
        var compilation = Create("alias Lib\npublic func main() => ::Api.value()", "public group Api\n    public func value() => ()");
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void ModuleReparseRebuildsLookupAndOwnership()
    {
        var compilation = Create("public func main() => Lib.Api.value()", "public group Api\n    public func value() => ()");
        Assert.True(compilation.Bind().IsComplete);
        foreach (var module in compilation.SourceModules)
        {
            module.OnDeserialized(compilation);
        }

        Assert.True(compilation.Bind().IsComplete);
        Assert.True(compilation.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(compilation.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InaccessibleOverloadsDoNotHideAccessibleCandidates(bool defaults)
    {
        var compilation = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public func main() => choose(1@i32)", "public group Api\n    private func choose(value: bool) => ()\n    public func choose(value: i32) => ()", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.True(compilation.Bind().IsComplete);
    }

    [Fact]
    public void AliasTargetAccessIsCheckedAtItsDeclaration()
    {
        var compilation = Create("alias Outer.Hidden\npublic group Outer\n    private group Hidden\n        public func value() => ()\n    public func use() => value()\npublic func main() => Outer.use()", "public func unused() => ()");
        Assert.False(compilation.Bind().IsComplete);
    }

    [Fact]
    public void WarmModuleBindingAndOwnershipAllocateNothing()
    {
        var compilation = Create("public func main() => value()", "public group Api\n    public func value() => ()", configure: (root, _) => root.Alias = ["Lib.Api"]);
        void Analyze()
        {
            if (!compilation.Bind().IsComplete || !compilation.Binding.CheckStartup(OutputKind.Application).IsComplete || !compilation.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Module analysis failed.");
            }
        }

        for (var i = 0; i < 100; i++)
        {
            Analyze();
        }

        Assert.Equal(0, AllocationMeasurement.Measure(Analyze));
    }

    [Theory]
    [InlineData("func f(x: Lib.Api.Box, y: Lib.Api.Box<i32>) => ()", false, true)]
    [InlineData("func f(x: ::Lib.Api.Box, y: ::Lib.Api.Box<i32>) => ()", false, true)]
    [InlineData("alias Lib.Api\nfunc f(x: Box, y: Box<i32>) => ()", false, true)]
    [InlineData("func f(x: Box, y: Box<i32>) => ()", true, true)]
    [InlineData("struct Box<T, U>\nfunc f(x: Box<i32>) => ()", true, false)]
    [InlineData("alias Local\ngroup Local\n    public struct Box<T, U>\nfunc f(x: Box<i32>) => ()", true, false)]
    public void ModuleTypeAritiesUseTheCommittedLookupStage(string source, bool defaults, bool expected)
    {
        var c = Create(source, "public group Api\n    public struct Box\n    public struct Box<T>", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.Equal(expected, c.Bind().IsComplete);
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("public", false, "func expose(value: Source.C.Element) => ()")]
    [InlineData("internal", true, "func expose(value: Source.C.Element) => ()")]
    [InlineData("public", false, "let value: Source.C.Element = 1")]
    [InlineData("internal", true, "let value: Source.C.Element = 1")]
    [InlineData("public", false, "enum Result\n        Item(Source.C.Element)")]
    [InlineData("internal", true, "enum Result\n        Item(Source.C.Element)")]
    [InlineData("public", false, "struct Result<T>\n        T is Source.C.Element")]
    [InlineData("internal", true, "struct Result<T>\n        T is Source.C.Element")]
    public void UnusedDependencyApisRetainProjectionAccess(string access, bool valid, string declaration)
    {
        var c = Create("public func main() => ()", "public group Api\n    internal contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32\n    " + access + " " + declaration);
        Assert.True(c.Bind().IsComplete == valid, string.Join(", ", c.Binding.Issues.Select(x => x.Code)));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("library.kimi").GetArray());
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.BindingFailure == BindingFailure.Access);
        }

        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false, "Source.Element")]
    [InlineData(false, "Source.C.Element")]
    [InlineData(true, "Source.Element")]
    [InlineData(true, "Source.C.Element")]
    public void ImportedProjectionUsesTheOriginalDomains(bool defaults, string type)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public group Export\n    public func identity(value: " + type + ") -> i32 => value", "public group Api\n    public contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => x.Code)));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerAliasesCannotWidenLibraryApiDomains(bool defaults)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "public func main() => ()", "public group Api\n    contract C\n        associate Element\n    public struct Source\n        Self is C\n        associate C.Element is i32\n    public enum Result\n        Item(Source.C.Element)", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.BindingFailure == BindingFailure.Access);
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedGenericCallArgumentsRequireValidCompleteTypes(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func take<T>() => ()\n    func call() => take<Box<" + argument + ">>()", "public group Api\n    public struct Box<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("root.kimi").GetArray());
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).BoundCall is not null);
        }
    }

    [Theory]
    [InlineData(false, "public", true)]
    [InlineData(true, "public", true)]
    [InlineData(false, "internal", false)]
    [InlineData(true, "internal", false)]
    public void ImportedCallTypesObserveLateLibraryApiValidation(bool defaults, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n    public struct Source\n        Self is Hidden\n    public enum E<T>\n        T is Hidden\n        A\n    public func take(value: E<Source>) -> E<Source> => value";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func call(value: E<Source>) => take(value)", library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("root.kimi").GetArray());
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("library.kimi").GetArray());
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).BoundCall is not null);
            if (!valid)
            {
                Assert.Contains(c.Binding.Issues, x => x.Node.CodeContext.Kotonoha == c.SourceModules[1] && x.Node.BindingFailure == BindingFailure.Access);
            }
        }
    }

    [Theory]
    [InlineData(false, "public", true)]
    [InlineData(true, "public", true)]
    [InlineData(false, "internal", false)]
    [InlineData(true, "internal", false)]
    public void ImportedPropertyAndPatternTypesObserveLateLibraryApiValidation(bool defaults, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n    public struct Source\n        Self is Hidden\n    public enum E<T>\n        T is Hidden\n        A";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct Storage\n    var value: E<Source>\ngroup Consumer\n    func inspect(value: E<Source>) => match value\n        _ => ()", library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var storage = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Storage");
            Assert.Equal(valid, storage.Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            Assert.True(c.Binding.TryGetMatch(Assert.IsType<MatchKoto>(function.ExpressionBody), out var plan));
            Assert.Equal(valid ? MatchCoverageState.Exhaustive : MatchCoverageState.Invalid, plan!.Coverage.State);
        }
    }

    [Theory]
    [InlineData(false, "public", false)]
    [InlineData(true, "public", false)]
    [InlineData(false, "protected", false)]
    [InlineData(true, "protected", false)]
    [InlineData(false, "protected internal", false)]
    [InlineData(true, "protected internal", false)]
    [InlineData(false, "internal", true)]
    [InlineData(true, "internal", true)]
    [InlineData(false, "private protected", true)]
    [InlineData(true, "private protected", true)]
    [InlineData(false, "private", true)]
    [InlineData(true, "private", true)]
    public void ImportedInheritedNamesUseTheDerivedModulesAccess(bool defaults, string access, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct S: Base\n    public func f() => ()", "public group Api\n    public open struct Base\n        " + access + " func f(self: ref/Self) => ()", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, structure.BindingState);
            if (!valid)
            {
                Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, structure) && x.Code == DiagnosticCode.DuplicateBinding_Kd);
            }
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedBaseConstraintsControlDerivedCertificates(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "contract C\nstruct S: Base<" + argument + ">\n    Self is C", "public group Api\n    public open struct Base<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
            Assert.Equal(valid, c.Binding.GetConformanceDefinition(structure.BoundType!, contract.BoundSymbol!)!.IsVerified);
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedAssociatedDefinitionsRequireValidInputs(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "struct S\n    Self is C\n    associate C.Item is Box<" + argument + ">", "public group Api\n    public struct Box<T>\n        T is i32\n    public contract C\n        associate Item", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var structure = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            var contract = c.SourceModules[1].RootKoto.NestedContainers.Single(x => x.Name == "Api").NestedContainers.Single(x => x.Name == "C");
            Assert.Equal(valid, c.Binding.GetConformanceDefinition(structure.BoundType!, contract.BoundSymbol!)!.IsVerified);
        }
    }

    [Theory]
    [InlineData(false, "i32", true)]
    [InlineData(true, "i32", true)]
    [InlineData(false, "string", false)]
    [InlineData(true, "string", false)]
    public void ImportedRuntimeTestTargetsRequireValidInputs(bool defaults, string argument, bool valid)
    {
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + "group Consumer\n    func f(x: objref/Box<i32>) -> bool => x is Box<" + argument + ">", "public group Api\n    public struct Box<T>\n        T is i32", configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
            var test = Assert.IsType<IsKoto>(function.ExpressionBody);
            Assert.Equal(valid, test.BoundRuntimeTest is not null);
            Assert.Equal(valid, c.Binding.TypeSystem.IsBoundRuntimeTypeTest(test));
        }
    }

    [Theory]
    [InlineData(false, false, "i32", true)]
    [InlineData(false, true, "i32", true)]
    [InlineData(true, false, "i32", true)]
    [InlineData(true, true, "i32", true)]
    [InlineData(false, false, "string", false)]
    [InlineData(false, true, "string", false)]
    [InlineData(true, false, "string", false)]
    [InlineData(true, true, "string", false)]
    public void ImportedExpressionProjectionsRetainInputConstraints(bool defaults, bool runtime, string argument, bool valid)
    {
        var library = "public group Api\n    public contract Origin\n        associate Item\n    public struct Source<T>\n        T is i32\n        Self is Origin\n        associate Origin.Item is i32";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + ProjectionConsumer(runtime, "Source<" + argument + ">.Origin.Item"), library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, runtime, valid);
    }

    [Theory]
    [InlineData(false, false, "public", true)]
    [InlineData(false, true, "public", true)]
    [InlineData(true, false, "public", true)]
    [InlineData(true, true, "public", true)]
    [InlineData(false, false, "internal", false)]
    [InlineData(false, true, "internal", false)]
    [InlineData(true, false, "internal", false)]
    [InlineData(true, true, "internal", false)]
    public void ImportedExpressionProjectionsRetainWitnessValidity(bool defaults, bool runtime, string access, bool valid)
    {
        var library = "public group Api\n    " + access + " contract Hidden\n        associate Item\n    public struct Local\n        Self is Hidden\n        associate Hidden.Item is i32\n    public contract Origin\n        associate Item\n        func f(self: ref/Self, x: i32) -> i32\n    public struct Source\n        Self is Origin\n        associate Origin.Item is i32\n        public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + ProjectionConsumer(runtime, "Source.Origin.Item"), library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, runtime, valid);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    [InlineData(3, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, false)]
    [InlineData(4, false, true)]
    [InlineData(4, true, false)]
    [InlineData(4, true, true)]
    [InlineData(5, false, false)]
    [InlineData(5, false, true)]
    [InlineData(5, true, false)]
    [InlineData(5, true, true)]
    [InlineData(6, false, false)]
    [InlineData(6, false, true)]
    [InlineData(6, true, false)]
    [InlineData(6, true, true)]
    public void ImportedDeclarationProjectionsRetainLateWitnessValidity(int form, bool defaults, bool valid)
    {
        var library = "public group Api\n    " + (valid ? "public" : "internal") + " contract Hidden\n        associate Item\n    public struct Local\n        Self is Hidden\n        associate Hidden.Item is i32\n    public open struct Base<T>\n    public contract Origin\n        associate Item\n        func f(self: ref/Self, x: i32) -> i32\n    public struct Source\n        Self is Origin\n        associate Origin.Item is i32\n        public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x";
        var consumer = form switch
        {
            0 => "group Consumer\n    func take(x: Source.Origin.Item) -> i32 => x\n    func call() -> i32 => take(1)",
            1 => "struct S\n    var field: Source.Origin.Item",
            2 => "contract C\nenum S\n    A(Source.Origin.Item)\n    Self is C",
            3 => "contract C\nstruct S: Base<Source.Origin.Item>\n    Self is C",
            4 => "contract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Source.Origin.Item",
            5 => "contract C\n    associate Item is Source.Origin.Item\nstruct S\n    Self is C",
            _ => "contract C\n    associate Item\nstruct S<T>\n    Self is C when T is Copy\n        associate C.Item is Source.Origin.Item",
        };
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Verify();
        foreach (var module in c.SourceModules)
        {
            module.OnDeserialized(c);
        }

        Verify();
        void Verify()
        {
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("root.kimi").GetArray());
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("library.kimi").GetArray());
            Assert.Equal(valid, c.Bind().IsComplete);
            if (form == 0)
            {
                var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
                Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.ExpressionBody).BoundCall is not null);
                return;
            }

            var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
            if (form == 1)
            {
                Assert.Equal(valid, type.Members.OfType<PropertyKoto>().Single().BoundSymbol!.Property!.IsVerified);
                return;
            }

            var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
            Assert.All(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.Paths, path => Assert.Equal(valid, path.IsVerified));
        }
    }

    private static string ProjectionConsumer(bool runtime, string argument)
        => runtime
            ? "struct Target<T>\ngroup Consumer\n    func call(x: objref/Target<i32>) -> bool => x is Target<" + argument + ">"
            : "group Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()";

    private static void VerifyImportedProjectionCertificate(Compilation c, bool runtime, bool valid)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("root.kimi").GetArray());
            Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("library.kimi").GetArray());
            Assert.Equal(valid, c.Bind().IsComplete);
            var expression = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody;
            Assert.Equal(valid, runtime ? Assert.IsType<IsKoto>(expression).BoundRuntimeTest is not null : Assert.IsType<InvocationKoto>(expression).BoundCall is not null);
            if (pass == 0)
            {
                foreach (var module in c.SourceModules)
                {
                    module.OnDeserialized(c);
                }
            }
        }
    }

    private static Compilation Create(string rootSource, string librarySource, string? childSource = null, Action<ProjectFile, ProjectFile>? configure = null)
    {
        var compilation = Compilation.CreateForTest();
        var project = compilation.Project;
        project.ProjectFile.Dependencies.Add("Lib", new() { PackageId = "library", PackageVersion = "1", Project = "library.kimiproj" });
        var root = new DependencyNode("root", new("root.kimiproj", project.ProjectFile, [], []), -1, string.Empty);
        var library = new DependencyNode("library@1", new("library.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 0, "Lib");
        configure?.Invoke(project.ProjectFile, library.Input.Configuration);
        root.Edges.Add("Lib", library.Key);
        DependencyNode[] nodes = [root, library];
        if (childSource is not null)
        {
            var child = new DependencyNode("child@1", new("child.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 1, "Child");
            library.Edges.Add("Child", child.Key);
            nodes = [root, library, child];
        }

        Assert.True(compilation.Prepare(WindowsProfile.Target, new(nodes)));
        compilation.SourceModules[0].AddSource(new SourceDocument("root.kimi", rootSource));
        compilation.SourceModules[1].AddSource(new SourceDocument("library.kimi", librarySource));
        if (childSource is not null)
        {
            compilation.SourceModules[2].AddSource(new SourceDocument("child.kimi", childSource));
        }

        return compilation;
    }
}
