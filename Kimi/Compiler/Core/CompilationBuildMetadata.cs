// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Records prepared build inputs; it is not a complete artifact cache key.</summary>
/// <param name="TargetTriple">The complete target triple, including environment.</param>
/// <param name="Debug">The selected build mode.</param>
/// <param name="LanguageVersion">The effective language version.</param>
/// <param name="CompilerVersion">The compiler version and module build identity.</param>
/// <param name="CompileTimeValues">The immutable prepared scalar environment.</param>
public sealed record CompilationBuildMetadata(
    string TargetTriple,
    bool Debug,
    string LanguageVersion,
    string CompilerVersion,
    IReadOnlyDictionary<string, BasicValue> CompileTimeValues);
