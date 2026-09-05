// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;

namespace Kimi.Compiler;

/// <summary>
/// Represents one target-specific compilation of a <see cref="Project"/>.
/// </summary>
/// <remarks>
/// A compilation owns the project's primary <see cref="Kotonoha"/>, records configured
/// external Kotonoha dependencies, exposes conditional-compilation variables, and carries
/// the target information used by later LLVM IR and binary-emission stages.
/// </remarks>
public class Compilation
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    #region FieldAndProperty

    /// <summary>
    /// Gets the compiler service that owns this compilation.
    /// </summary>
    public Kimigayo Kimigayo { get; }

    /// <summary>
    /// Gets the project being compiled.
    /// </summary>
    public Project Project { get; }

    /// <summary>
    /// Gets the parsed target triple.
    /// </summary>
    public TargetTriple TargetTriple { get; private set; } = TargetTriple.Invalid;

    /// <summary>
    /// Gets the intermediate-representation target configuration.
    /// </summary>
    public IrTarget IrTarget { get; private set; } = IrTarget.Invalid;

    /// <summary>
    /// Gets the target pointer width in bits.
    /// </summary>
    public int PointerWidth => this.IrTarget.PointerWidth;

    /// <summary>
    /// Gets the configured external Kotonoha dependencies.
    /// </summary>
    public KotonohaIdentifier[] KotonohaArray { get; }

    /// <summary>
    /// Gets the primary source unit for the project.
    /// </summary>
    public Kotonoha Kotonoha { get; }

    /// <summary>
    /// Gets the variables available to conditional compilation.
    /// </summary>
    public Utf16Hashtable<BasicValue> Variables { get; } = new();

    private readonly UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    // Identifier text repeats heavily across a compilation; sharing one string per spelling
    // keeps the syntax tree small. The table is thread-safe for concurrent parsing.
    private readonly IdentifierTable identifiers = new();

    #endregion

    /// <summary>
    /// Creates a compilation with an empty test project.
    /// </summary>
    /// <param name="useConsoleService">
    /// <see langword="true"/> to use <see cref="ConsoleService"/>;
    /// otherwise, use <see cref="EmptyConsole"/>.
    /// </param>
    /// <returns>A compilation configured for tests.</returns>
    public static Compilation CreateForTest(bool useConsoleService = false)
    {
        IConsoleService consoleService = useConsoleService ? new ConsoleService() : new EmptyConsole();
        var kimigayo = new Kimigayo(consoleService);
        var project = new Project(kimigayo);
        var compilation = new Compilation(kimigayo, project);

        return compilation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Compilation"/> class.
    /// </summary>
    /// <param name="kimigayo">The owning compiler service.</param>
    /// <param name="project">The project to compile.</param>
    public Compilation(Kimigayo kimigayo, Project project)
    {
        ArgumentNullException.ThrowIfNull(kimigayo);
        ArgumentNullException.ThrowIfNull(project);

        this.Kimigayo = kimigayo;
        this.Project = project;
        this.KotonohaArray = project.ProjectFile.KotonohaArray?.ToArray() ?? [];
        this.Kotonoha = new(this, this.Project.Name, string.Empty);
        this.kotonohaIdToKotonoha.Add(this.Kotonoha.Id, this.Kotonoha);
    }

    /// <summary>
    /// Returns the shared string instance for identifier text, creating it on first use.
    /// </summary>
    /// <param name="text">The identifier text.</param>
    /// <returns>A string equal to <paramref name="text"/> shared across this compilation.</returns>
    public string Intern(ReadOnlySpan<char> text)
        => this.identifiers.Intern(text);

    /// <summary>
    /// Configures the compilation for a target triple.
    /// </summary>
    /// <param name="target">The target triple text.</param>
    /// <returns>
    /// <see langword="true"/> when the architecture has a supported pointer width and LLVM data layout;
    /// otherwise, <see langword="false"/> and the target state is reset to invalid.
    /// </returns>
    /// <remarks>
    /// Successful preparation rebuilds the <c>os</c>, <c>windows</c>, <c>linux</c>,
    /// <c>macos</c>, <c>pointerWidth</c>, and <c>debug</c> conditional-compilation variables.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="target"/> is empty or whitespace.</exception>
    public bool Prepare(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var targetTriple = TargetTriple.Parse(target);
        var irTarget = IrTarget.Create(targetTriple);

        this.Variables.Clear();
        if (targetTriple.Arch == Architecture.Unknown ||
            irTarget.PointerWidth == 0 ||
            irTarget.DataLayout.Length == 0)
        {
            this.TargetTriple = TargetTriple.Invalid;
            this.IrTarget = IrTarget.Invalid;
            return false;
        }

        this.TargetTriple = targetTriple;
        this.IrTarget = irTarget;

        // External Kotonoha dependencies will be loaded here.

        // Rebuild target-dependent conditional compilation variables.
        var os = targetTriple.OsName;
        this.Variables.Add("os", new(os));
        this.Variables.Add("windows", new(targetTriple.Os == OsType.Win32));
        this.Variables.Add("linux", new(targetTriple.Os == OsType.Linux));
        this.Variables.Add("macos", new(targetTriple.Os == OsType.MacOSX));
        this.Variables.Add("pointerWidth", new(irTarget.PointerWidth));
        this.Variables.Add("debug", new(this.Project.KimiOptions.Debug));
        this.Variables.Add("release", new(!this.Project.KimiOptions.Debug));

        return true;
    }

    /// <summary>
    /// Attempts to find a source unit by its identifier.
    /// </summary>
    /// <param name="kotonohaId">The source unit identifier.</param>
    /// <param name="kotonoha">The matching source unit, if found.</param>
    /// <returns><see langword="true"/> when a matching source unit is found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKotonoha(uint kotonohaId, [MaybeNullWhen(false)] out Kotonoha kotonoha)
    {
        return this.kotonohaIdToKotonoha.TryGetValue(kotonohaId, out kotonoha);
    }

    /// <summary>
    /// Attempts to find a Koto node within a source unit.
    /// </summary>
    /// <param name="kotonohaId">The source unit identifier.</param>
    /// <param name="kotoId">The Koto identifier.</param>
    /// <param name="koto">The matching node, if found.</param>
    /// <returns><see langword="true"/> when a matching node is found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKoto(uint kotonohaId, ulong kotoId, [MaybeNullWhen(false)] out Koto koto)
    {
        if (this.kotonohaIdToKotonoha.TryGetValue(kotonohaId, out var kotonoha))
        {
            return kotonoha.TryGetKoto(kotoId, out koto);
        }

        koto = default;
        return false;
    }

    internal void ScrubForTest()
    {
        string source;
        var builder = new IndentedStringBuilder();
        try
        {
            // Round-trip the syntax tree and compare its textual representation.
            this.Kotonoha.RootKoto.UnparseAll(ref builder);
            source = builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }

        File.WriteAllText(Path.Combine(this.Project.Directory, Constants.ScrubFileName), source, Utf8WithoutBom);

        var binary = TinyhandSerializer.Serialize(this.Kotonoha);
        this.Kimigayo.WriteLine(LogLevel.Information, $"Source: {(long)source.Length * sizeof(char)} bytes, Binary: {binary.Length} bytes");

        var kotonoha = new Kotonoha(this);
        TinyhandSerializer.DeserializeObject(binary, ref kotonoha);
        if (kotonoha is null)
        {
            return;
        }

        kotonoha.OnDeserialized(this);

        string restored;
        var builder2 = new IndentedStringBuilder();
        try
        {
            kotonoha.RootKoto.UnparseAll(ref builder2);
            restored = builder2.ToString();
        }
        finally
        {
            builder2.Dispose();
        }

        if (!string.Equals(source, restored, StringComparison.Ordinal))
        {
            this.Kimigayo.WriteLine(LogLevel.Error, "Data mismatch detected after serialization");
            File.WriteAllText(Path.Combine(this.Project.Directory, Constants.Scrub2FileName), restored, Utf8WithoutBom);
        }
    }

    internal bool TryGetIdentifier(ReadOnlySpan<char> text, [NotNullWhen(true)] out string? identifier)
        => this.identifiers.TryGetIdentifier(text, out identifier);

    internal bool TryResolveValue(IdentifierNameKoto koto, out BasicValue basicValue)
    {
        return this.Variables.TryGetValue(koto.IdentifierName, out basicValue);
    }
}
