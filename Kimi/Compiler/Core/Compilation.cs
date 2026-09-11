// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler.Lexing;
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
    /// <summary>The only language version currently implemented by this compiler.</summary>
    public const string CurrentLanguageVersion = "0.0.1";

    /// <summary>Gets the version and deterministic module identity of this compiler build.</summary>
    public static string CompilerVersion { get; } =
        $"{typeof(Compilation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} ({typeof(Compilation).Module.ModuleVersionId:D})";

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
    /// Gets the variables available to conditional compilation, with ordinal case-insensitive name lookup.
    /// </summary>
    public IReadOnlyDictionary<string, BasicValue> Variables { get; private set; } =
        new ReadOnlyDictionary<string, BasicValue>(new Dictionary<string, BasicValue>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Gets the inputs recorded on successful preparation, or null when preparation failed.</summary>
    public CompilationBuildMetadata? BuildMetadata { get; private set; }

    private readonly UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    // Identifier text repeats heavily across a compilation; sharing one string per spelling
    // keeps the syntax tree small. The table is thread-safe for concurrent parsing.
    private readonly IdentifierTable identifiers = new();

    private bool hasParsedSource;

    /// <summary>Gets reusable semantic analysis storage for this compilation.</summary>
    public Binding Binding => field ??= new(this);

    /// <summary>Gets this compilation's compiler-owned Core requirement identities.</summary>
    public CoreIntrinsics Core => this.Binding.Core;

    private OwnershipAnalysis? ownership;

    /// <summary>Gets reusable ownership CFG analysis. Verification is separate from executable emission.</summary>
    public OwnershipAnalysis Ownership => this.ownership ??= new(this);

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
    /// <see langword="true"/> when the language version, settings, pointer width, and LLVM data layout are supported;
    /// otherwise, <see langword="false"/> and the target state is reset to invalid.
    /// </returns>
    /// <remarks>
    /// Successful preparation rebuilds the <c>os</c>, <c>windows</c>, <c>linux</c>,
    /// <c>macos</c>, <c>arch</c>, <c>pointerWidth</c>, <c>debug</c>, <c>release</c>, and Project settings.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="target"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Source parsing has already started in this compilation.</exception>
    public bool Prepare(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (this.hasParsedSource)
        {
            throw new InvalidOperationException("Create a new Compilation to change inputs after parsing source.");
        }

        this.Variables = new ReadOnlyDictionary<string, BasicValue>(new Dictionary<string, BasicValue>(StringComparer.OrdinalIgnoreCase));
        this.TargetTriple = TargetTriple.Invalid;
        this.IrTarget = IrTarget.Invalid;
        this.BuildMetadata = null;
        var languageVersion = this.Project.ProjectFile.LangVersion ?? this.Project.SolutionLanguageVersion ?? CurrentLanguageVersion;
        if (languageVersion != CurrentLanguageVersion)
        {
            this.Kotonoha.DiagnosticCollection.Add(default, DiagnosticCode.UnsupportedLanguageVersion_Kd, languageVersion, CurrentLanguageVersion);
            return false;
        }

        var targetTriple = TargetTriple.Parse(target);
        var irTarget = IrTarget.Create(targetTriple);

        if (targetTriple.Arch == Architecture.Unknown ||
            irTarget.PointerWidth == 0 ||
            irTarget.DataLayout.Length == 0)
        {
            this.TargetTriple = TargetTriple.Invalid;
            this.IrTarget = IrTarget.Invalid;
            return false;
        }

        // External Kotonoha dependencies will be loaded here.

        // Rebuild target-dependent conditional compilation variables.
        var os = targetTriple.Os switch
        {
            OsType.Win32 => "windows",
            OsType.MacOSX => "macos",
            _ => targetTriple.Os.ToString().ToLowerInvariant(),
        };
        var debug = this.Project.KimiOptions.Debug;
        var variables = new Dictionary<string, BasicValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["os"] = new(os),
            ["arch"] = new(targetTriple.Arch.ToString().ToLowerInvariant()),
            ["windows"] = new(os == "windows"),
            ["linux"] = new(os == "linux"),
            ["macos"] = new(os == "macos"),
            ["pointerWidth"] = new(irTarget.PointerWidth),
            ["debug"] = new(debug),
            ["release"] = new(!debug),
        };
        foreach (var (name, setting) in this.Project.ProjectFile.CompileTimeSettings)
        {
            if (!this.TryGetIdentifier(name, out _) || !TokenHelper.GetKeywordOrIdentifierKind(name).IsIdentifierOrContextualKeyword() ||
                variables.ContainsKey(name) || setting is null || !setting.TryGetValue(out var value))
            {
                this.Kotonoha.DiagnosticCollection.Add(default, DiagnosticCode.InvalidCompileTimeSetting_Kd, name);
                return false;
            }

            variables.Add(name, value);
        }

        this.TargetTriple = targetTriple;
        this.IrTarget = irTarget;
        this.Variables = new ReadOnlyDictionary<string, BasicValue>(variables);
        this.BuildMetadata = new(target, debug, languageVersion, CompilerVersion, this.Variables);

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

    /// <summary>Analyzes control flow after parsing and compile-time directive selection.</summary>
    /// <param name="types">Type facts supplied by Binding, or syntax-only facts when omitted.</param>
    /// <returns>Definite errors, inferred contracts, and obligations pending further Binding.</returns>
    public ControlFlowAnalysis AnalyzeControlFlow(ControlFlowTypeSystem? types = null)
        => ControlFlowAnalysis.Analyze(this.Kotonoha.RootKoto, types);

    /// <summary>Runs provisional Binding, the reserved Mod stage, final Binding, and Bound checking.</summary>
    /// <returns>The final Binding summary; incomplete semantics never certify success.</returns>
    public BindingResult Bind()
    {
        this.Binding.Bind(BindingMode.Provisional);
        this.RunMods();
        return this.Binding.Bind(BindingMode.Final);
    }

    internal void InvalidateOwnership() => this.ownership?.Invalidate();

    internal bool TryGetIdentifier(ReadOnlySpan<char> text, [NotNullWhen(true)] out string? identifier)
        => this.identifiers.TryGetIdentifier(text, out identifier);

    internal void BeginSourceParsing() => this.hasParsedSource = true;

    internal bool TryResolveValue(IdentifierNameKoto koto, out BasicValue basicValue)
    {
        return this.Variables.TryGetValue(koto.IdentifierName, out basicValue);
    }

    private void RunMods()
    {
        // Reserved boundary: execute each ready Mod once; after its append phase integrate
        // declarations and call Binding.Bind(Provisional) before the next Mod reads semantics.
        // No Mod API or execution is implemented yet. Never mutate syntax during final Binding.
    }
}
