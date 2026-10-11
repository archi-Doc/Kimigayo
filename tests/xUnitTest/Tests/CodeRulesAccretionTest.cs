// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace XunitTest;

public class CodeRulesAccretionTest
{
    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    public async Task ParameterCountIncludesExtensionReceiver(int count, int expected)
    {
        var parameters = "this int p0," + string.Join(',', Enumerable.Range(1, count - 1).Select(i => "int p" + i));
        Assert.Equal(expected, (await Rule("static class C { static void M(" + parameters + ") { } }", "KR2001")).Length);
    }

    [Theory]
    [InlineData("class C { C(int a,int b) {} }", 1)]
    [InlineData("class C { void M() { void L(int a,int b) {} } }", 1)]
    [InlineData("class C { int this[int a,int b] => 0; }", 1)]
    [InlineData("delegate void D(int a,int b);", 1)]
    [InlineData("class C { public static C operator +(C a,C b) => a; }", 1)]
    [InlineData("record C(int a,int b);", 0)]
    [InlineData("class C(int a,int b) { }", 0)]
    [InlineData("class C { System.Func<int,int,int> f = (a,b) => a; }", 0)]
    public async Task ParameterRulesOwnTheDeclaredOperations(string source, int expected)
        => Assert.Equal(expected, (await Rule(source, "KR2001", "max_parameters", 1)).Length);

    [Theory]
    [InlineData("interface C { void M(out int a); }", 0)]
    [InlineData("interface C { void M(out int a,out int b); }", 1)]
    [InlineData("interface C { void M(ref int a,out int b); }", 0)]
    public async Task OutParametersCountOnlyOut(string source, int expected)
        => Assert.Equal(expected, (await Rule(source, "KR2002")).Length);

    [Theory]
    [InlineData("bool a,bool b", 1)]
    [InlineData("bool? a,bool? b", 1)]
    [InlineData("B a,B? b", 1)]
    [InlineData("B a,ref B b", 0)]
    [InlineData("B a,in B b", 0)]
    [InlineData("B a,int b", 0)]
    [InlineData("int a,string b", 0)]
    public async Task BooleanParametersUseValueTypesIncludingAliases(string parameters, int expected)
        => Assert.Equal(expected, (await Rule("using B=System.Boolean; interface C { void M(" + parameters + "); }", "KR2004")).Length);

    [Theory]
    [InlineData("out string failure", 1)]
    [InlineData("out string? ErrorText", 1)]
    [InlineData("out S failureReason", 1)]
    [InlineData("out S? MESSAGE", 1)]
    [InlineData("out int problem", 0)]
    [InlineData("ref string error", 0)]
    [InlineData("out string value", 0)]
    public async Task FailureTextUsesSemanticStringAndFailureName(string parameter, int expected)
        => Assert.Equal(expected, (await Rule("using S=System.String; interface C { void M(" + parameter + "); }", "KR2003")).Length);

    [Theory]
    [InlineData("M(true);", 1)]
    [InlineData("M(((false)));", 1)]
    [InlineData("M(value:true);", 0)]
    [InlineData("bool b=true; M(b);", 0)]
    [InlineData("new C(true);", 1)]
    [InlineData("C c=new(true);", 1)]
    [InlineData("void L(bool value) {} L(false);", 1)]
    [InlineData("System.Console.WriteLine(true);", 0)]
    [InlineData("System.Action<bool> f=M; f(true);", 0)]
    public async Task BooleanArgumentsResolveOnlySourceOperations(string body, int expected)
        => Assert.Equal(expected, (await Rule("class C { C(bool value) {} static void M(bool value) {} void Test(){" + body + "} }", "KR2005")).Length);

    [Fact]
    public async Task ConstructorInitializerAndReducedExtensionAreSourceOperations()
    {
        const string source = "class C { C(bool value){} C():this(true){} void M(){this.Ext(false);} } static class E { public static void Ext(this C c,bool value){} }";
        Assert.Equal(2, (await Rule(source, "KR2005")).Length);
    }

    [Theory]
    [InlineData("class C { void M(){ ; ; } }", 0)]
    [InlineData("class C { void M(){ { ; ; ; } } }", 1)]
    [InlineData("class C { void M(){ void L(){ ; ; ; } } }", 1)]
    [InlineData("class C { void M(){ System.Action f=()=>{ ; ; }; } }", 1)]
    [InlineData("class C { int P { get { ; ; return 0; } set { ; } } }", 1)]
    [InlineData("class C { int this[int i] { get { ; ; return 0; } set { ; } } }", 1)]
    [InlineData("class C { event System.Action E { add { ; ; } remove { ; } } }", 1)]
    [InlineData("class C { System.Action a=()=>{ ; ; ; },b=()=>{ ; ; }; }", 1)]
    public async Task StatementsHaveOneMemberOwner(string source, int expected)
        => Assert.Equal(expected, (await Rule(source, "KR2006", "max_statements", 2)).Length);

    [Theory]
    [InlineData("if(b){} else if(b){} else if(b){}", 0)]
    [InlineData("if(b){if(b){if(b){}}}", 1)]
    [InlineData("if(b){System.Action a=()=>{if(b){if(b){}}};}", 0)]
    [InlineData("if(b){void L(){if(b){if(b){}}}}", 0)]
    [InlineData("while(b){do{for(;;){break;}}while(b);}", 1)]
    [InlineData("try{lock(this){using(var x=new System.IO.MemoryStream()){}}}finally{}", 1)]
    [InlineData("foreach(var x in new int[0]){switch(x){case 0:if(b){} break;}}", 1)]
    public async Task NestingResetsCallablesAndKeepsElseIfFlat(string body, int expected)
        => Assert.Equal(expected, (await Rule("class C { void M(bool b){" + body + "} }", "KR2007", "max_nesting_depth", 2)).Length);

    [Theory]
    [InlineData("a&&a&&a&&a", 0)]
    [InlineData("a&&a&&a&&a&&a", 1)]
    [InlineData("(a&&a&&a)&&(a&&a&&a)", 0)]
    [InlineData("a||a||a||a||a", 1)]
    [InlineData("i is >0 and >1 and >2 and >3 and >4", 1)]
    [InlineData("i is 0 or 1 or 2 or 3 or 4", 1)]
    [InlineData("i is (0 or 1 or 2) or (3 or 4 or 5)", 0)]
    public async Task LogicalChainsRespectOperatorAndParenthesisBoundaries(string expression, int expected)
        => Assert.Equal(expected, (await Rule("class C { bool M(bool a,int i)=>" + expression + "; }", "KR2008")).Length);

    [Theory]
    [InlineData("int a=0,int b=0", 0)]
    [InlineData("int a=0,int b=0,int c=0", 1)]
    [InlineData("int a=0,int b=0,params int[] rest", 0)]
    public async Task OptionalParametersExcludeParams(string parameters, int expected)
        => Assert.Equal(expected, (await Rule("class C { void M(" + parameters + "){} }", "KR2010")).Length);

    [Theory]
    [InlineData("class C { void M() { } }")]
    [InlineData("/// <summary><see cref=\"C\"/></summary>\nclass C { /* nested <xml/> */ void M() { } }")]
    [InlineData("#if true\nclass C { void M() { } }\n#else\nclass Ignored { int x = 123; }\n#endif")]
    public async Task FileTokensShareTheBudgetCounterAndIgnoreTrivia(string source)
    {
        var tokens = CodeRulesSource.CountTokens(source);
        Assert.Empty(await Rule(source, "KR2009", "max_file_tokens", tokens));
        var found = Assert.Single(await Rule("// comment\n" + source, "KR2009", "max_file_tokens", tokens - 1));
        Assert.Contains("value " + tokens, found.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileTokenMeasurementExcludesMissingTokensOnInvalidInput()
    {
        const string source = "class C { void M() {";
        var compilation = CodeRulesTestHelper.Compilation(source);
        var tokens = CodeRulesSource.CountTokens(source);
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options("max_file_tokens", tokens - 1));
        var found = Assert.Single(diagnostics, x => x.Id == "KR2009");
        Assert.Contains("value " + tokens, found.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001");
    }

    [Theory]
    [InlineData("src/Kimi/Compiler/Analysis/Test.cs")]
    [InlineData("src/Kimi/Compiler/Emission/Test.cs")]
    public async Task LegacyPathsRemainOutsideAccretion(string path)
    {
        var compilation = CodeRulesTestHelper.Compilation(("class C { void M(){ ; ; ; } }", path));
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options("max_statements", 2));
        Assert.DoesNotContain(diagnostics, x => x.Id.StartsWith("KR2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PartialMeasurementsBelowLimitDoNotAppearInOrdinaryReport()
    {
        var compilation = CodeRulesTestHelper.Compilation("partial class C { partial void M(); }", "partial class C { partial void M(){ ; ; } }");
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options(mode: "Report"));
        Assert.DoesNotContain(diagnostics, x => x.Id == "KR9001");
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(61, 1)]
    [InlineData(120, 1)]
    [InlineData(240, 1)]
    public async Task StatementMeasurementScalesWithOneBodyTraversal(int statements, int expected)
        => Assert.Equal(expected, (await Rule("class C { void M(){" + new string(';', statements) + "} }", "KR2006")).Length);

    internal static Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions Options(string? limit = null, int value = 0, string mode = "Enforce", string baseline = "")
        => CodeRulesTestHelper.Options(mode, baseline, config =>
        {
            config["kimi_code_rules.scope.density.exclude"] = "src/Kimi/";
            config["kimi_code_rules.scope.architecture.exclude"] = "src/Kimi/";
            config["kimi_code_rules.scope.syntax_dispatch.exclude"] = "src/Kimi/";
            if (limit is not null)
            {
                config["kimi_code_rules." + limit] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        });

    private static async Task<ImmutableArray<Diagnostic>> Rule(string source, string id, string? limit = null, int value = 0)
    {
        var compilation = CodeRulesTestHelper.Compilation(source);
        Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options(limit, value));
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001");
        return diagnostics.Where(x => x.Id == id).ToImmutableArray();
    }
}
