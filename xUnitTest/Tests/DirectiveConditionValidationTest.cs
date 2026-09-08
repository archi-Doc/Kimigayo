// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class DirectiveConditionValidationTest
{
    [Theory]
    [InlineData("false and 1")]
    [InlineData("1 and false")]
    [InlineData("true or 1")]
    [InlineData("1 or true")]
    [InlineData("not (false and 1)")]
    [InlineData("not (true or 1)")]
    [InlineData("false and (true or 1)")]
    [InlineData("true or (false and 1)")]
    [InlineData("missing and 1")]
    [InlineData("missing or 1")]
    [InlineData("(false and 1) == false")]
    public void KnownErrorsOverrideShortCircuitTruth(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar excluded = 1");

        Assert.Contains(
            compilation.Kotonoha.DiagnosticCollection.GetArray(),
            x => x.Entry.Name == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("false and missing", false)]
    [InlineData("missing and false", false)]
    [InlineData("true or missing", true)]
    [InlineData("missing or true", true)]
    [InlineData("not (false and missing)", true)]
    [InlineData("not (true or missing)", false)]
    [InlineData("(false and missing) == false", true)]
    [InlineData("false and (T is i32)", false)]
    [InlineData("true or (T is i32)", true)]
    public void EarlyTruthRetainsValidationWithoutDeferringSelection(string condition, bool selected)
    {
        // The incomplete target proves that an early-false condition still skips target parsing.
        var target = selected ? "var retained = 1" : "var incomplete =";
        var compilation = Parse($"#if {condition}\n{target}");
        var root = compilation.Kotonoha.RootKoto;
        var obligation = Assert.Single(root.PendingDirectiveConditions);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Same(root, obligation.Scope);
        Assert.Same(compilation.Kotonoha, obligation.Condition.CodeContext.Kotonoha);
        Assert.Equal(condition, obligation.Condition.ToString());
        Assert.Contains(obligation.Condition, compilation.AnalyzeControlFlow().PendingBinding);
        if (selected)
        {
            Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        }
        else
        {
            Assert.Null(compilation.Kotonoha.GeneratedFunction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildConfigurationDoesNotEraseUnknownNameObligations(bool debug)
    {
        var compilation = Parse("#if debug and missing\nvar conditional = 1", debug);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        var obligation = Assert.Single(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
        Assert.Equal("missing", Assert.IsType<IdentifierNameKoto>(Assert.IsType<AndKoto>(obligation.Condition).Right).IdentifierName);
        if (debug)
        {
            var directive = Assert.IsType<CompileTimeIfKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
            Assert.Same(obligation.Condition, directive.Condition);
            Assert.Same(directive, directive.Condition.Parent);
        }
        else
        {
            Assert.Null(compilation.Kotonoha.GeneratedFunction);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("false and missing")]
    [InlineData("true or missing")]
    public void SelectedCaseGroupRetainsLaterArmConditions(string laterCondition)
    {
        var compilation = Parse($"""
            func select()
                #case true
                    return
                #case {laterCondition}
                    return
                #case _
                    return
            """);
        var function = Assert.IsType<FunctionKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = function.Body!;
        var obligation = Assert.Single(body.PendingDirectiveConditions);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal(laterCondition, obligation.Condition.ToString());
        Assert.Same(body, obligation.Scope);
        Assert.IsType<CodeBlockKoto>(Assert.Single(body.Items));
        Assert.Contains(obligation.Condition, ControlFlowAnalysis.Analyze(function).PendingBinding);
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
    }

    [Fact]
    public void LaterCaseOperandErrorIsDiagnosedAfterSelection()
    {
        var compilation = Parse("#case true\n    ()\n#case false and 1\n    ()");

        Assert.Contains(
            compilation.Kotonoha.DiagnosticCollection.GetArray(),
            x => x.Entry.Name == nameof(DiagnosticCode.ConditionMustBeBool_Kd));
    }

    [Fact]
    public void ExcludingAStackedPrefixDoesNotLoseItsCondition()
    {
        var compilation = Parse("#if missing\n#if false\nvar incomplete =");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal("missing", Assert.Single(compilation.Kotonoha.RootKoto.PendingDirectiveConditions).Condition.ToString());
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Fact]
    public void NestedScopesAndDeclarationDirectiveBodiesKeepTheirOwnObligations()
    {
        var compilation = Parse("""
            struct Container<T>
                #if false and outerMissing
                var incomplete:
                #if true
                    #if false and innerMissing
                    var incomplete:
                func nested<U>()
                    #if false and (U is i32)
                    var incomplete =
                    ()
            """);
        var structure = Assert.Single(compilation.Kotonoha.RootKoto.NestedDeclarationContainers);
        var declarationBody = Assert.IsType<CodeBlockKoto>(structure.Members[0]);
        var function = Assert.IsType<FunctionKoto>(structure.Members[1]);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
        Assert.Equal("false and outerMissing", Assert.Single(structure.PendingDirectiveConditions).Condition.ToString());
        Assert.Same(structure, Assert.Single(structure.PendingDirectiveConditions).Scope);
        Assert.Equal("false and innerMissing", Assert.Single(declarationBody.PendingDirectiveConditions).Condition.ToString());
        Assert.Same(declarationBody, Assert.Single(declarationBody.PendingDirectiveConditions).Scope);
        Assert.Same(function.Body, Assert.Single(function.Body!.PendingDirectiveConditions).Scope);
        Assert.Same(function, function.Body.Parent);
    }

    [Fact]
    public void ExcludedTargetDoesNotCreateNestedValidationObligations()
    {
        var compilation = Parse("#if false\n    #if nestedMissing\n    var incomplete =");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Fact]
    public void LabeledBlockReportsValidationPendingWithoutChangingControlFlow()
    {
        var compilation = Parse("work:\n    #if false and missing\n    var incomplete =\n    exit from work");
        var labeled = Assert.IsType<LabeledKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
        var body = Assert.IsType<CodeBlockKoto>(labeled.Target);
        var obligation = Assert.Single(body.PendingDirectiveConditions);
        var analysis = compilation.AnalyzeControlFlow();

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Empty(analysis.Issues);
        Assert.Contains(obligation.Condition, analysis.PendingBinding);
        Assert.Same(body, obligation.Scope);
        Assert.Single(body.Items);
    }

    [Fact]
    public void AppendingSourcesPreservesEachConditionsOriginalDocument()
    {
        var compilation = Compilation.CreateForTest();
        var context = compilation.Kotonoha.CreateCodeContext();
        var first = new SourceDocument("first.kimi", "#if false and firstMissing\nvar incomplete =");
        var second = new SourceDocument("second.kimi", "#if true or secondMissing\nvar value = 1");
        context.Parse(compilation.Kotonoha.RootKoto, first);
        context.Parse(compilation.Kotonoha.RootKoto, second);

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Collection(
            compilation.Kotonoha.RootKoto.PendingDirectiveConditions,
            obligation => Assert.Same(first, obligation.SourceDocument),
            obligation => Assert.Same(second, obligation.SourceDocument));
    }

    [Fact]
    public void SerializationRebuildsValidationObligationsWithoutDuplication()
    {
        var compilation = Parse("#if false and missing\nvar incomplete =");
        var bytes = TinyhandSerializer.Serialize(compilation.Kotonoha);
        var restored = new Kotonoha(compilation);
        TinyhandSerializer.DeserializeObject(bytes, ref restored);
        restored!.OnDeserialized(compilation);
        restored.OnDeserialized(compilation);
        var obligation = Assert.Single(restored.RootKoto.PendingDirectiveConditions);

        Assert.Same(restored.RootKoto, obligation.Scope);
        Assert.Same(restored, obligation.Condition.CodeContext.Kotonoha);
        Assert.Equal("false and missing", obligation.Condition.ToString());
        Assert.Null(restored.GeneratedFunction);
    }

    [Theory]
    [InlineData("false and true")]
    [InlineData("true or false")]
    [InlineData("windows and pointerWidth == 64")]
    public void FullyValidatedConditionsDoNotCreateBindingObligations(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar value = 1");

        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Empty(compilation.Kotonoha.RootKoto.PendingDirectiveConditions);
    }

    private static Compilation Parse(string source, bool debug = false)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.KimiOptions.Debug = debug;
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        return compilation;
    }
}
