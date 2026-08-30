// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;

namespace Kimi.Compiler;

/// <summary>
/// Holds the target configuration and symbols for a project compilation.
/// </summary>
public class Compilation
{
    #region FieldAndProperty

    /// <summary>
    /// Gets the compiler service that owns this compilation.
    /// </summary>
    public Kimigayo Kimigayo { get; }

    /// <summary>
    /// Gets the project being compiled.
    /// </summary>
    public Project Project { get; }

    /*public KimiOptions KimiOptions { get; private set; }

    public ProjectFile ProjectFile { get; }

    public string ProjectName { get; }*/

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
    public KotonohaIdentifier[] KotonohaArray { get; private set; } = [];

    /// <summary>
    /// Gets the primary source unit for the project.
    /// </summary>
    public Kotonoha Kotonoha { get; private set; }

    /// <summary>
    /// Gets the variables available to conditional compilation.
    /// </summary>
    public Utf16Hashtable<BasicValue> Variables { get; private set; } = new();

    private UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    #endregion

    /// <summary>
    /// Creates a compilation with an empty test project.
    /// </summary>
    /// <returns>A compilation configured for tests.</returns>
    public static Compilation CreateForTest()
    {
        var kimigayo = new Kimigayo(new EmptyConsole());
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
        this.Kimigayo = kimigayo;
        this.Project = project;
        this.Kotonoha = new(this, this.Project.Name, string.Empty);
    }

    /// <summary>
    /// Configures the compilation for a target triple.
    /// </summary>
    /// <param name="target">The target triple text.</param>
    /// <returns><see langword="true"/> when preparation succeeds.</returns>
    public bool Prepare(string target)
    {
        this.TargetTriple = TargetTriple.Parse(target);
        this.IrTarget = IrTarget.Create(this.TargetTriple);

        // External Kotonoha dependencies will be loaded here.

        // Rebuild target-dependent conditional compilation variables.
        this.Variables.Clear();

        var os = this.TargetTriple.OsName;
        this.Variables.Add("os", new(os));
        this.Variables.Add("windows", new(string.Equals(os, "windows", StringComparison.InvariantCultureIgnoreCase)));
        this.Variables.Add("linux", new(string.Equals(os, "linux", StringComparison.InvariantCultureIgnoreCase)));
        this.Variables.Add("macos", new(string.Equals(os, "macos", StringComparison.InvariantCultureIgnoreCase)));
        this.Variables.Add("pointerWidth", new(this.IrTarget.PointerWidth));

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
        if (this.Kotonoha is null)
        {
            return;
        }

        var builder = new IndentedStringBuilder();
        var builder2 = new IndentedStringBuilder();
        try
        {
            // Round-trip the syntax tree and compare its textual representation.
            this.Kotonoha.RootKoto.UnparseAll(ref builder);

            var path = Path.Combine(this.Project.Directory, Constants.ScrubFileName);
            var st = builder.ToString();
            File.WriteAllText(path, st, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var bin = TinyhandSerializer.Serialize(this.Kotonoha);
            this.Kimigayo.WriteLine(LogLevel.Information, $"Source: {st.Length * 2} bytes, Binary: {bin.Length} bytes");

            var kotonoha = new Kotonoha(this);
            TinyhandSerializer.DeserializeObject(bin, ref kotonoha);
            if (kotonoha is null)
            {
                return;
            }

            kotonoha.OnDeserialized(this);
            kotonoha.RootKoto.UnparseAll(ref builder2);
            var path2 = Path.Combine(this.Project.Directory, Constants.Scrub2FileName);
            var st2 = builder2.ToString();
            if (!st.SequenceEqual(st2))
            {
                this.Kimigayo.WriteLine(LogLevel.Error, "Data mismatch detected after serialization");
                File.WriteAllText(path2, st2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            // kotonoha.Root.WriteTo(ref builder2);
            // var sb2 = builder2.ToString();
        }
        finally
        {
            builder.Dispose();
            builder2.Dispose();
        }
    }

    internal bool TryResolveValue(IdentifierNameKoto koto, out BasicValue basicValue)
    {
        return this.Variables.TryGetValue(koto.IdentifierName, out basicValue);
    }
}
