// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Structural ratchets of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md). Each limit is the count at
/// the stage that set it; a count may fall but never rise. A deleted construct counts as zero.</summary>
public class ArchitectureRulesTest
{
    [Theory]
    [InlineData("Koto-keyed dictionaries in Binding", "src/Kimi/Compiler/Binding", "*.cs", @"Dictionary<\w*Koto\b", 104)]
    [InlineData("Supports* gates", "src/Kimi/Compiler", "*.cs", @"\bbool\s+Supports\w*\s*[(<]", 21)]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 448)]
    [InlineData("Unsupported diagnostic codes", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"Category=""Unsupported""", 1)]
    [InlineData("Advice in the diagnostic catalog", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"(?m)^\s*Advice=", 0)]
    [InlineData("Parsing calls into Binding", "src/Kimi/Compiler/Parsing", "*.cs", @"\bBinding\.\w+\(", 1)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = Directory.EnumerateFiles(Path.Combine(Repository.Root, directory), pattern, SearchOption.AllDirectories)
            .Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    [Theory]
    [InlineData("Kimi.Compiler.OwnershipOperationKind", 39)]
    [InlineData("Kimi.Compiler.OwnershipValueKind", 31)]
    [InlineData("Kimi.Compiler.EmissionOpcode", 49)]
    [InlineData("Kimi.Compiler.CompilerFunctionKind", 76)]
    [InlineData("Kimi.Compiler.BindingFailure", 89)]
    public void VocabularyDoesNotGrow(string type, int limit)
    {
        var members = typeof(Compilation).Assembly.GetType(type) is { } enumeration ? Enum.GetNames(enumeration).Length : 0;
        Assert.True(members <= limit, $"{type} has {members} members; the limit is {limit}.");
    }
}
