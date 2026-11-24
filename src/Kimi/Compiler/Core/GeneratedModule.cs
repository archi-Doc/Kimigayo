// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

/// <summary>What a written module states for its link, the generation result beside the module text (R9-B1; Compilation.Generated):
/// published by the generation entry from the decisions it and code generation made, only for the request that wrote the module and
/// until the next generation request or analysis, and read by the toolchain's publication, which writes it into the link manifest and
/// reads no IR text and no Binding state.</summary>
/// <param name="OutputKind">The module's output form at the write: a Library has no entry; an Application's and a test build's module has
/// one.</param>
/// <param name="Libraries">The native libraries the module requires, the library and Kind of every validated <c>#LibraryImport</c>, one
/// per library (ModuleCodegen.Libraries, by reference).</param>
/// <param name="Externals">The external functions the module declares, one per symbol in declaration order, each read for its
/// <c>Symbol</c> and <c>DllImport</c> (ModuleCodegen.Externals, by reference).</param>
/// <param name="RuntimeImports">The imports the module's runtime (a test build's: the test runtime) declares and no external names, each
/// a kernel32 import in its dllimport form (ModuleCodegen.RuntimeImports, by reference; LinkAmbiguity, T's ruling 32): with
/// <c>Externals</c>, every external the module declares, each once.</param>
/// <param name="Entry">The symbol the module's start defines, its entry (IMPL 21.4.2, 21.5.2; ModuleCodegen.Entry, the frame's one
/// decision): <c>main</c> on the C path, else the row's entry symbol, a test build's included; null for a Library.</param>
internal readonly record struct GeneratedModule(
    OutputKind OutputKind,
    IReadOnlyList<(string Library, string Kind)> Libraries,
    IReadOnlyList<ExternalFunction> Externals,
    IReadOnlyList<string> RuntimeImports,
    string? Entry);
