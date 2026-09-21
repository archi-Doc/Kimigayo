// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class CompilationSpecificationTest
{
    [Theory]
    [InlineData("pointerWidth >= 64")]
    [InlineData("os.startsWith(\"win\")")]
    [InlineData("SizeOf<T>() == 8")]
    [InlineData("flags & mask == 0")]
    [InlineData("1 + 2 == 3")]
    [InlineData("1.0 == 1.0")]
    [InlineData("'a' == 'a'")]
    [InlineData("null == null")]
    [InlineData("[1] == [1]")]
    [InlineData("values[0] == 1")]
    [InlineData("1@i64 == 1")]
    [InlineData("\"\\(1)\" == \"1\"")]
    [InlineData("T is List<i32>")]
    [InlineData("value is ref/i32 x")]
    [InlineData("9223372036854775808 == 0")]
    [InlineData("-9223372036854775809 == 0")]
    [InlineData("-setting == 0")]
    public void DisallowedConditionsAreErrorsEvenWhenShortCircuited(string expression)
    {
        foreach (var condition in new[] { expression, $"false and ({expression})", $"true or ({expression})" })
        {
            var compilation = Parse($"#if {condition}\nvar excluded = 1");
            Assert.Contains(compilation.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
            Assert.Null(compilation.Kotonoha.GeneratedFunction);
        }
    }

    [Theory]
    [InlineData("-9223372036854775808 == -9223372036854775808")]
    [InlineData("+9223372036854775807 == 9223372036854775807")]
    [InlineData("0x10 == 16")]
    [InlineData("-1 != +1")]
    [InlineData("not false and (true or false)")]
    [InlineData("\"Windows\" != \"windows\"")]
    public void AllowedScalarConditionsEvaluateWithoutBinding(string condition)
    {
        var compilation = Parse($"#if {condition}\nvar selected = 1");
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.IsType<FieldKoto>(Assert.Single(compilation.Kotonoha.GeneratedFunction!.Body!.Items));
    }

    [Theory]
    [InlineData("T is i32")]
    [InlineData("s is ref")]
    [InlineData("T is External.Contracts.Comparable")]
    [InlineData("T is not Comparable")]
    public void TypeDependentConditionsAreRejectedWithoutBinding(string requirement)
    {
        var compilation = Parse($"#if false and ({requirement})\nvar incomplete =");
        Assert.Contains(compilation.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.InvalidCompileTimeCondition_Kd));
        Assert.Null(compilation.Kotonoha.GeneratedFunction);
    }

    [Fact]
    public void ExclusionDoesNotRetractGrammarErrorsFromAlreadyParsedBodies()
    {
        Assert.Empty(Parse("#if false\nvar incomplete =").Kotonoha.DiagnosticCollection.GetArray());
        Assert.NotEmpty(Parse("#if pendingName\nvar incomplete =").Kotonoha.DiagnosticCollection.GetArray());
        Assert.NotEmpty(Parse("#switch\n    #case true\n        ()\n    #case _\n        var incomplete =").Kotonoha.DiagnosticCollection.GetArray());
        Assert.NotEmpty(Parse("#if false\nvar text = \"unterminated").Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("amd64-pc-win32-msvc", "windows", "x86_64", 64)]
    [InlineData("x86_64-pc-windows-msvc", "windows", "x86_64", 64)]
    [InlineData("arm64-apple-macosx13.0", "macos", "aarch64", 64)]
    [InlineData("i686-unknown-linux-gnu", "linux", "x86", 32)]
    [InlineData("x86_64-unknown-unrecognized", "unknown", "x86_64", 64)]
    public void PreparedTargetValuesAreCanonicalAndConsistent(string target, string os, string arch, int width)
    {
        foreach (var debug in new[] { false, true })
        {
            var compilation = Compilation.CreateForTest();
            compilation.Project.KimiOptions.Debug = debug;
            Assert.True(compilation.Prepare(target));
            Assert.Equal(os, compilation.Variables["os"].String);
            Assert.Equal(arch, compilation.Variables["arch"].String);
            Assert.Equal(width, compilation.Variables["pointerWidth"].I64);
            Assert.Equal(BasicValueKind.I64, compilation.Variables["pointerWidth"].Kind);
            foreach (var flag in new[] { "windows", "linux", "macos" })
            {
                Assert.Equal(os == flag, compilation.Variables[flag].Bool);
            }

            Assert.Equal(debug, compilation.Variables["debug"].Bool);
            Assert.Equal(!debug, compilation.Variables["release"].Bool);
            Assert.Equal(target, compilation.BuildMetadata!.TargetTriple);
            Assert.Equal(Compilation.CurrentLanguageVersion, compilation.BuildMetadata.LanguageVersion);
            Assert.Equal(Compilation.CompilerVersion, compilation.BuildMetadata.CompilerVersion);
        }
    }

    [Fact]
    public void ProjectSettingsAreTypedCopiedAndSerialized()
    {
        var compilation = Compilation.CreateForTest();
        var settings = compilation.Project.ProjectFile.CompileTimeSettings;
        settings.Add("feature", new() { Bool = true });
        settings.Add("limit", new() { Integer = -2 });
        settings.Add("flavor", new() { String = "vanilla" });
        compilation.Project.ProjectFile.LangVersion = Compilation.CurrentLanguageVersion;
        var restored = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(TinyhandSerializer.SerializeToUtf8(compilation.Project.ProjectFile))!;
        Assert.True(restored.CompileTimeSettings["feature"].Bool);
        Assert.Equal(-2, restored.CompileTimeSettings["limit"].Integer);
        Assert.Equal("vanilla", restored.CompileTimeSettings["flavor"].String);
        Assert.Equal(Compilation.CurrentLanguageVersion, restored.LangVersion);

        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        settings["feature"].Bool = false;
        compilation.Project.KimiOptions.Debug = true;
        Assert.True(compilation.Variables["feature"].Bool);
        Assert.False(compilation.BuildMetadata!.Debug);
        compilation.Kotonoha.CreateCodeContext().Parse(
            compilation.Kotonoha.RootKoto,
            "#if feature and limit == -2 and flavor == \"vanilla\"\nvar selected = 1");
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
        Assert.NotNull(compilation.Kotonoha.GeneratedFunction);
        Assert.Empty(Compilation.CreateForTest().Project.ProjectFile.CompileTimeSettings);
        Assert.Throws<InvalidOperationException>(() => compilation.Prepare("x86_64-unknown-linux-gnu"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void DirectiveConstantNamesAreCaseSensitiveIndependentlyOfCulture(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var compilation = Compilation.CreateForTest();
            var settings = compilation.Project.ProjectFile.CompileTimeSettings;
            settings.Add("Feature", new() { Bool = true });
            settings.Add("limit", new() { Integer = 2 });
            settings.Add("Flavor", new() { String = "Vanilla" });
            Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
            Assert.True(compilation.Variables["windows"].Bool);
            Assert.Equal(2, compilation.BuildMetadata!.CompileTimeValues["limit"].I64);

            var source = """
                #if windows and not linux and not macos and os == "windows" and arch == "x86_64" and pointerWidth == 64 and release and not debug
                var builtinSelected = 1
                #if Feature and limit == 2 and Flavor == "Vanilla" and Flavor != "vanilla"
                var settingSelected = 2
                #switch
                    #case linux
                        var excluded = 3
                    #case windows and Feature and limit == 2 and Flavor == "Vanilla"
                        var matchSelected = 4
                    #case _
                        var fallback = 5
                """;
            compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);

            Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
            Assert.Collection(
                compilation.Kotonoha.GeneratedFunction!.Body!.Items,
                node => Assert.Equal("builtinSelected", Assert.IsType<FieldKoto>(node).NameKoto.IdentifierName),
                node => Assert.Equal("settingSelected", Assert.IsType<FieldKoto>(node).NameKoto.IdentifierName),
                node => Assert.Equal("matchSelected", Assert.IsType<FieldKoto>(node).NameKoto.IdentifierName));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("Feature", "FEATURE")]
    [InlineData("FEATURE", "Feature")]
    public void SettingNamesDifferingOnlyInCaseRemainDistinct(string first, string second)
    {
        var compilation = Compilation.CreateForTest();
        var settings = compilation.Project.ProjectFile.CompileTimeSettings;
        settings.Add(first, new() { Bool = true });
        settings.Add(second, new() { Bool = false });

        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        Assert.True(compilation.Variables[first].Bool);
        Assert.False(compilation.Variables[second].Bool);
        Assert.True(compilation.BuildMetadata!.CompileTimeValues[first].Bool);
        Assert.False(compilation.BuildMetadata.CompileTimeValues[second].Bool);
        Assert.False(compilation.Variables.ContainsKey("feature"));
        Assert.Empty(compilation.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("os")]
    [InlineData("windows")]
    [InlineData("linux")]
    [InlineData("macos")]
    [InlineData("release")]
    [InlineData("e\u0301")]
    [InlineData("arch")]
    [InlineData("debug")]
    [InlineData("pointerWidth")]
    [InlineData("if")]
    [InlineData("true")]
    [InlineData("bad-name")]
    [InlineData("")]
    public void InvalidSettingNamesFailPreparationWithoutPartialState(string name)
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.ProjectFile.CompileTimeSettings[name] = new() { Bool = true };
        Assert.False(compilation.Prepare("x86_64-pc-windows-msvc"));
        Assert.Empty(compilation.Variables);
        Assert.Same(TargetTriple.Invalid, compilation.TargetTriple);
        Assert.Null(compilation.BuildMetadata);
        Assert.Contains(compilation.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.InvalidCompileTimeSetting_Kd));
    }

    [Fact]
    public void SettingMustSpecifyExactlyOneValue()
    {
        foreach (var setting in new CompileTimeSetting?[]
        {
            null,
            new(),
            new() { Bool = false, Integer = 0 },
            new() { Bool = true, String = string.Empty },
            new() { Integer = 0, String = string.Empty },
            new() { Bool = true, Integer = 0, String = string.Empty },
        })
        {
            var compilation = Compilation.CreateForTest();
            compilation.Project.ProjectFile.CompileTimeSettings["feature"] = setting!;
            Assert.False(compilation.Prepare("x86_64-pc-windows-msvc"));
        }
    }

    [Fact]
    public void FormerLanguageVersionIsRejectedBeforeParsing()
    {
        var compilation = Compilation.CreateForTest();
        compilation.Project.ProjectFile.LangVersion = "0.0.1";
        Assert.False(compilation.Prepare("x86_64-pc-windows-msvc"));
    }

    [Fact]
    public async Task LanguageVersionIsInheritedOverridableAndNeverSilentlyIgnored()
    {
        var compilation = Compilation.CreateForTest();
        var project = compilation.Project;
        var solution = new Solution(compilation.Kimigayo);
        solution.Projects.Add("test", project);
        project.ProjectFile.OutputKind = OutputKind.Library;
        solution.SolutionFile.Configuration.LangVersion = "future";
        Assert.False(await solution.Check(TestContext.Current.CancellationToken));
        Assert.Empty(project.BuildMetadata);
        project.ProjectFile.LangVersion = Compilation.CurrentLanguageVersion;
        compilation.Kimigayo.GetOrAddDiagnosticCollection(project.Name).ClearDiagnostic();
        Assert.True(await solution.Check(TestContext.Current.CancellationToken));
        Assert.Equal(Compilation.CurrentLanguageVersion, Assert.Single(project.BuildMetadata).LanguageVersion);
    }

    [Fact]
    public void SourceContextsIdentifySnapshotsRatherThanPaths()
    {
        var compilation = Compilation.CreateForTest();
        var root = compilation.Kotonoha.RootKoto;
        var entryPoint = compilation.Kotonoha.CreateCodeContext();
        var first = new SourceDocument("same.kimi", "var first = 1");
        var second = new SourceDocument("same.kimi", "var second = 2");
        entryPoint.Parse(root, first);
        entryPoint.Parse(root, second);
        var items = compilation.Kotonoha.GeneratedFunction!.Body!.Items;
        Assert.NotSame(items[0].CodeContext, items[1].CodeContext);
        Assert.Same(first, items[0].CodeContext.SourceDocument);
        Assert.Same(second, items[1].CodeContext.SourceDocument);
        Assert.Null(entryPoint.SourceDocument);
        items[0].AddDiagnostic(DiagnosticCode.TypeMismatch_Kd);
        Assert.Same(first, Assert.Single(compilation.Kotonoha.DiagnosticCollection.GetArray()).SourceDocument);
        Assert.Throws<InvalidOperationException>(() => items[0].CodeContext.Parse(root, second));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void ProjectFileLoadingPreservesDistinctNamesAndValues(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var file = ProjectFile.Load("""
                CompileTimeSettings={Feature={Bool=true} FEATURE={Bool=false} WINDOWS={Bool=false} 機能={Integer=-9223372036854775808} é={String="Vanilla"}}
                """u8)!;
            var restored = ProjectFile.Load(TinyhandSerializer.SerializeToUtf8(file))!;
            Assert.Equal(5, restored.CompileTimeSettings.Count);
            Assert.True(restored.CompileTimeSettings["Feature"].Bool);
            Assert.False(restored.CompileTimeSettings["FEATURE"].Bool);
            Assert.Equal(long.MinValue, restored.CompileTimeSettings["機能"].Integer);
            Assert.Equal("Vanilla", restored.CompileTimeSettings["é"].String);

            var c = Compilation.CreateForTest();
            c.Project.ProjectFile.CompileTimeSettings = restored.CompileTimeSettings;
            Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
            c.Kotonoha.CreateCodeContext().Parse(
                c.Kotonoha.RootKoto,
                "#if windows and not WINDOWS and Feature and not FEATURE and 機能 == -9223372036854775808 and é == \"Vanilla\"\nvar selected = 1");
            Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
            Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData("CompileTimeSettings={Feature={Bool=true} Feature={Bool=false}}")]
    [InlineData("CompileTimeSettings={Feature={Bool=true} \"Feature\"={Bool=false}}")]
    [InlineData("CompileTimeSettings={\"Featu\\u0072e\"={Bool=true} Feature={Bool=false}}")]
    [InlineData("CompileTimeSettings={Feature={Bool=true}} CompileTimeSettings={Feature={Bool=false}}")]
    public void ProjectFileLoadingRejectsDuplicateDecodedSettingNames(string source)
    {
        var exception = Assert.Throws<TinyhandException>(() => ProjectFile.Load(System.Text.Encoding.UTF8.GetBytes(source)));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Theory]
    [InlineData("CompileTimeSettings={}")]
    [InlineData("Targets={\"x86_64-pc-windows-msvc\"} // CompileTimeSettings={Feature={} Feature={}}\nCompileTimeSettings={Feature={Bool=true}}")]
    [InlineData("CompileTimeSettings={text={String=\"Feature={Bool=true} Feature={Bool=false}\"}}")]
    public void ProjectFileLoadingUsesTinyhandGrammar(string source)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(source);
        var expected = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8)!;
        var actual = ProjectFile.Load(utf8)!;
        Assert.Equal(TinyhandSerializer.SerializeToUtf8(expected), TinyhandSerializer.SerializeToUtf8(actual));
    }

    [Theory]
    [InlineData("{ CompileTimeSettings={} }")]
    [InlineData("CompileTimeSettings = {\n    Feature = { Bool = true }\n}")]
    public void ProjectFileLoadingPreservesInvalidTinyhandGrammar(string source)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(source);
        Assert.ThrowsAny<TinyhandException>(() => TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(utf8));
        Assert.ThrowsAny<TinyhandException>(() => ProjectFile.Load(utf8));
    }

    [Theory]
    [InlineData("WINDOWS")]
    [InlineData("POINTERWIDTH == 64")]
    [InlineData("feature")]
    [InlineData("false and WINDOWS")]
    [InlineData("true or WINDOWS")]
    public void MisspelledConditionNamesRetainSourceDiagnostics(string condition)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.CompileTimeSettings["Feature"] = new() { Bool = true };
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        var source = new SourceDocument("case-sensitive.kimi", $"#if {condition}\nvar excluded = 1");
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        var diagnostic = Assert.Single(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), diagnostic.Entry.Name);
        Assert.Same(source, diagnostic.SourceDocument);
        var name = condition.Contains("WINDOWS", StringComparison.Ordinal) ? "WINDOWS" : condition.StartsWith("POINTERWIDTH", StringComparison.Ordinal) ? "POINTERWIDTH" : "feature";
        Assert.Equal(4 + condition.IndexOf(name, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal(name.Length, diagnostic.Span.Length);
        Assert.Null(c.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("#switch\n    #case true\n        ()\n    #case WINDOWS\n        ()")]
    [InlineData("#switch\n    #case true\n        ()\n    #case _\n        #if WINDOWS\n            ()")]
    public void UnselectedSwitchConditionsStillRejectMisspelledNames(string source)
    {
        var c = Parse(source);
        Assert.Equal(nameof(DiagnosticCode.UnknownCompileTimeName_Kd), Assert.Single(c.Kotonoha.DiagnosticCollection.GetArray()).Entry.Name);
        Assert.Empty(Parse("#if false\n    #if WINDOWS\n        ()").Kotonoha.DiagnosticCollection.GetArray());
    }

    [Fact]
    public void PreparationRebuildsSnapshotsAfterSuccessAndFailure()
    {
        var c = Compilation.CreateForTest();
        var settings = c.Project.ProjectFile.CompileTimeSettings;
        settings["Feature"] = new() { Bool = true };
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        var original = c.BuildMetadata!;
        settings["Feature"].Bool = false;
        settings["FEATURE"] = new() { Bool = true };
        Assert.True(c.Prepare("x86_64-unknown-linux-gnu"));
        Assert.True(original.CompileTimeValues["windows"].Bool);
        Assert.True(original.CompileTimeValues["Feature"].Bool);
        Assert.False(original.CompileTimeValues.ContainsKey("FEATURE"));
        Assert.False(c.Variables["Feature"].Bool);
        Assert.True(c.Variables["FEATURE"].Bool);
        settings["windows"] = new() { Bool = true };
        Assert.False(c.Prepare("x86_64-pc-windows-msvc"));
        Assert.Empty(c.Variables);
        Assert.Null(c.BuildMetadata);
        Assert.Same(TargetTriple.Invalid, c.TargetTriple);
        settings.Remove("windows");
        c.Kotonoha.DiagnosticCollection.ClearDiagnostic();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        Assert.False(c.Variables["Feature"].Bool);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Fact]
    public void WarmCaseSensitiveLookupDoesNotAllocate()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.CompileTimeSettings["Feature"] = new() { Bool = true };
        c.Project.ProjectFile.CompileTimeSettings["FEATURE"] = new() { Bool = false };
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            for (var i = 0; i < 1024; i++)
            {
                if (!c.Variables.TryGetValue("Feature", out var first) || !first.Bool ||
                    !c.Variables.TryGetValue("FEATURE", out var second) || second.Bool ||
                    c.BuildMetadata!.CompileTimeValues.ContainsKey("feature"))
                {
                    throw new InvalidOperationException("Incorrect case-sensitive lookup.");
                }
            }
        }));
    }

    private static Compilation Parse(string source)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));
        compilation.Kotonoha.CreateCodeContext().Parse(compilation.Kotonoha.RootKoto, source);
        return compilation;
    }
}
