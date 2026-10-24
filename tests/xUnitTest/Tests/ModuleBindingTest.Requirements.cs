// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// Imported requirements and refinements retain current proof and certificate validity.
public partial class ModuleBindingTest
{
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
    public void ImportedProjectionConstraintsAndConditionalDomainsRemainCurrent(int form, bool defaults, bool valid)
    {
        string library;
        string consumer;
        if (form < 2)
        {
            library = "public group Api\n    " + (valid ? "public" : "internal") + " contract Hidden\n        associate Item\n    public struct Local\n        Self is Hidden\n        associate Hidden.Item is i32\n    public contract Origin\n        associate Item\n        func f(self: ref/Self, x: i32) -> i32\n    public struct Source\n        Self is Origin\n        associate Origin.Item is i32\n        public func f(self: ref/Self, x: Local.Hidden.Item) -> i32 => x";
            consumer = form == 0
                ? "group Consumer\n    func take<T>()\n        T is Source.Origin.Item\n        ()\n    func call() => take<i32>()"
                : "contract C\nstruct S<T>\n    Self is C when T is Source.Origin.Item";
        }
        else if (form < 4)
        {
            library = "public group Api\n    " + (valid ? "public" : "internal") + " contract Hidden\n        associate Item\n    public struct Source\n        Self is Hidden\n        associate Hidden.Item is i32\n    " + (form == 2 ? "public" : "internal") + " contract C\n    public struct S<T>\n        Self is C when T is Source.Hidden.Item" + (form == 3 ? "\n            public func value() -> i32 => 1" : string.Empty);
            consumer = form == 2 ? "()" : "group Consumer\n    func call() -> i32 => S<i32>.value()";
        }
        else
        {
            library = "public group Api\n    public contract Origin\n        associate Item\n    public struct Source\n        Self is Origin\n        associate Origin.Item is " + (valid ? "i32" : "string") + "\n    public struct Box<T>\n        T is Origin\n        T.Origin.Item is i32";
            consumer = "group Consumer\n    func take<T>() => ()\n    func call() => take<Box<Source>>()";
        }

        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            if (form is 0 or 3 or 4)
            {
                var call = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
                Assert.Equal(valid, Assert.IsType<InvocationKoto>(call.ExpressionBody).CallOf() is not null);
            }
            else
            {
                var owner = form == 1 ? c.Kotonoha.RootKoto : c.SourceModules[1].RootKoto.NestedContainers.Single(x => x.Name == "Api");
                var type = owner.NestedContainers.Single(x => x.Name == "S");
                var contract = owner.NestedContainers.Single(x => x.Name == "C");
                Assert.Equal(valid, c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!)!.IsVerified);
            }

            if (pass == 0)
            {
                foreach (var module in c.SourceModules)
                {
                    module.OnDeserialized(c);
                }
            }
        }
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
    public void ImportedCompleteTypeConditionsControlConsumerCertificates(int form, bool defaults, bool valid)
    {
        var item = valid ? "i32" : "string";
        var library = form switch
        {
            0 => "public struct Target\n    [2 of " + item + "] is Copy",
            1 => "public struct Target<T>\n    [2 of T] is Copy",
            2 => "public struct Target<T>\n    i32 is T",
            _ => "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + item + "\npublic contract R\n    Source.Origin.Item is Copy\npublic struct Target\n    Self is R",
        };
        var argument = form is 1 or 2 ? "Target<" + item + ">" : "Target";
        var consumer = "group Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()";
        if (form == 4)
        {
            library = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + item;
            consumer = "struct Target\n    [2 of Source.Origin.Item] is Copy\n" + consumer;
        }

        library = "public group Api\n    " + library.Replace("\n", "\n    ", StringComparison.Ordinal);
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            var call = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(call.ExpressionBody).CallOf() is not null);
            if (pass == 0)
            {
                foreach (var module in c.SourceModules)
                {
                    module.OnDeserialized(c);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingImportedClosedConditionsRevokesAndRestoresCalls(bool contract)
    {
        var library = contract
            ? "public group Api\n    public contract R\n        i32 is Copy\n    public struct Target\n        Self is R"
            : "public group Api\n    public struct Target\n        i32 is Copy";
        const string consumer = "alias Lib.Api\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Target>()";
        var c = Create(consumer, library);
        Assert.True(c.Bind().IsComplete);
        var owner = c.SourceModules[1].RootKoto.NestedContainers.Single().NestedContainers.Single(x => x.Name == (contract ? "R" : "Target"));
        var clause = owner.ConstraintNodes[0];
        var original = clause.Left;
        var donor = Create(consumer, library.Replace("i32 is Copy", "string is Copy", StringComparison.Ordinal));
        var replacement = donor.SourceModules[1].RootKoto.NestedContainers.Single().NestedContainers.Single(x => x.Name == owner.Name).ConstraintNodes[0].Left;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        var target = c.SourceModules[1].RootKoto.NestedContainers.Single().NestedContainers.Single(x => x.Name == "Target");
        Assert.Equal(BindingState.Invalid, target.StateOf());
        var call = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Null(Assert.IsType<InvocationKoto>(call.ExpressionBody).CallOf());
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.NotNull(Assert.IsType<InvocationKoto>(call.ExpressionBody).CallOf());
    }

    [Theory]
    [InlineData(false, "(i32,) -> bool", true)]
    [InlineData(true, "(i32,) -> bool", true)]
    [InlineData(false, "((i32,)) -> bool", false)]
    [InlineData(true, "((i32,)) -> bool", false)]
    [InlineData(false, "() -> bool", false)]
    [InlineData(true, "() -> bool", false)]
    public void ImportedFunctionRequirementsPreserveParameterListIdentity(bool defaults, string argument, bool valid)
    {
        const string library = "public group Api\n    public func take<T>()\n        T is (i32) -> bool\n        ()";
        var consumer = "group Consumer\n    func call() => take<" + argument + ">()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, false, valid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportedClosedFunctionIdentityControlsTypeFormation(bool defaults, bool valid)
    {
        var library = "public group Api\n    public struct Target\n        () -> bool is " + (valid ? "()" : "(())") + " -> bool";
        const string consumer = "group Consumer\n    func take<T>() => ()\n    func call() => take<Target>()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        VerifyImportedProjectionCertificate(c, false, valid);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportedLateContractFailuresRevokeExpandedPremises(bool defaults, bool valid)
    {
        var library = "public group Api\n    public contract Marker: Copy\n        string is " + (valid ? "not Copy" : "Copy");
        const string consumer = "group Consumer\n    func take<T>()\n        T is Copy\n        ()\n    func call<T>(value: T)\n        T is Marker\n        take<T>()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Empty(TestDiagnostics.Of(c, "root.kimi"));
            Assert.Empty(TestDiagnostics.Of(c, "library.kimi"));
            Assert.Equal(valid, c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
            Assert.Equal(valid ? ConstraintProof.Proven : ConstraintProof.Error, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
            Assert.Equal(valid, Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).CallOf() is not null);
            foreach (var module in c.SourceModules)
            {
                module.OnDeserialized(c);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendedDependencyContractsResolveProvisionalConsumerConditions(bool defaults)
    {
        const string library = "public group Api\n    public struct Source\n        Self is Future";
        const string consumer = "group Consumer\n    func take<T>()\n        T is Future\n        ()\n    func call() => take<Source>()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Null(Assert.IsType<InvocationKoto>(function.ExpressionBody).CallOf());
        c.SourceModules[1].AddSource(new SourceDocument("Generated.kimi", "public group Api\n    public contract Future"));
        Assert.Empty(TestDiagnostics.Of(c, "Generated.kimi"));
        VerifyImportedProjectionCertificate(c, false, true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportedPendingRefinementsPreserveIndependentEvidence(bool defaults, bool independent)
    {
        const string library = "public group Api\n    public contract Origin\n    public struct Source\n    public contract Marker: Copy\n        Source is Origin\n    public contract Child: Marker";
        var consumer = "group Consumer\n    func take<T>()\n        T is Copy\n        ()\n    func call<T>(value: T)\n        T is Child" + (independent ? "\n        T is Copy" : string.Empty) + "\n        take<T>()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
        Assert.Equal(independent ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
        Assert.Equal(independent, Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).CallOf() is not null);
        c.SourceModules[1].AddSource(new SourceDocument("Generated.kimi", "public group Api\n    public struct Source\n        Self is Origin"));
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).CallOf());
            foreach (var module in c.SourceModules)
            {
                module.OnDeserialized(c);
            }
        }
    }

    [Theory]
    [InlineData("Future")]
    [InlineData("Lib.Api.Future")]
    [InlineData("::Lib.Api.Future")]
    public void AppendedImportedParentsCompleteConsumerConformances(string parent)
    {
        var consumer = "alias Lib.Api\npublic contract Child: " + parent + "\npublic struct Target\n    Self is Child\ngroup Consumer\n    func take<T>()\n        T is Child\n        ()\n    func call() => take<Target>()";
        var c = Create(consumer, "public group Api");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var child = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child");
        Assert.Equal(BindingState.Unresolved, child.StateOf());
        c.SourceModules[1].AddSource(new SourceDocument("Generated.kimi", "public group Api\n    public contract Future"));
        VerifyImportedProjectionCertificate(c, false, true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportedRefinementParentsRetainNameValidation(bool rooted, bool generic)
    {
        var c = Create("public contract Child: " + (rooted ? "::" : string.Empty) + "Lib.Api.Origin" + (generic ? "<i32>" : string.Empty), "public group Api\n    public contract Origin");
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(!generic, c.Bind().IsComplete);
            Assert.Equal(generic ? BindingState.Invalid : BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child").StateOf());
            foreach (var module in c.SourceModules)
            {
                module.OnDeserialized(c);
            }
        }
    }

    [Theory]
    [InlineData(false, "Copy")]
    [InlineData(false, "Owned")]
    [InlineData(true, "Copy")]
    [InlineData(true, "Owned")]
    public void ImportedAssociatedRefinementsSupplyIntrinsicCallEvidence(bool defaults, string capability)
    {
        var library = "public group Api\n    public contract Trait: " + capability + "\n    public contract Elements\n        associate Item is Trait";
        var consumer = "group Consumer\n    func take<U>()\n        U is " + capability + "\n        ()\n    func call<T>(value: T.Item)\n        T is Elements\n        take<T.Item>()";
        var c = Create((defaults ? string.Empty : "alias Lib.Api\n") + consumer, library, configure: (root, _) => root.Alias = defaults ? ["Lib.Api"] : []);
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete);
            var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call");
            Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).CallOf());
            foreach (var module in c.SourceModules)
            {
                module.OnDeserialized(c);
            }
        }
    }
}
