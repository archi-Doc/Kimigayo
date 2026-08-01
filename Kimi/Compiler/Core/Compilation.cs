// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public class Compilation
{
    #region FieldAndProperty

    public KimiControl KimiControl { get; }

    public Project Project { get; }

    /*public KimiOptions KimiOptions { get; private set; }

    public ProjectFile ProjectFile { get; }

    public string ProjectName { get; }*/

    public string Target { get; private set; } = string.Empty;

    public TargetTriple TargetTriple { get; private set; } = TargetTriple.Empty;

    public KotonohaIdentifier[] KotonohaArray { get; private set; } = [];

    public Kotonoha? ProjectKotonoha { get; private set; }

    public Utf16Hashtable<LimitedValue> CompilationVariables { get; private set; } = new();

    private UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    #endregion

    public Compilation(KimiControl kimiControl, Project project)
    {
        this.KimiControl = kimiControl;
        this.Project = project;
    }

    public CodeContext CreateCodeContext(Kotonoha kotonoha, ReadOnlyMemory<char> sourceText, string[]? aliases = default)
    {
        return new(this, kotonoha, sourceText);
    }

    [MemberNotNullWhen(true, nameof(ProjectKotonoha))]
    public bool Prepare(string target)
    {
        if (!TargetTripleParser.TryParse(target, out var targetTriple))
        {
            return false;
        }

        this.Target = target;
        this.TargetTriple = targetTriple;

        // External Kotonoha

        // Project Kotonoha
        this.ProjectKotonoha = new(this, this.Project.Name, string.Empty);

        // CompilationVariables
        this.CompilationVariables.Clear();

        var os = this.TargetTriple.OperatingSystem;
        this.CompilationVariables.Add("os", new(os));
        this.CompilationVariables.Add("windows", new(string.Equals(os, "Windows", StringComparison.InvariantCultureIgnoreCase)));
        this.CompilationVariables.Add("linux", new(string.Equals(os, "linux", StringComparison.InvariantCultureIgnoreCase)));
        this.CompilationVariables.Add("macos", new(string.Equals(os, "macos", StringComparison.InvariantCultureIgnoreCase)));

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKotonoha(uint kotonohaId, [MaybeNullWhen(false)] out Kotonoha kotonoha)
    {
        return this.kotonohaIdToKotonoha.TryGetValue(kotonohaId, out kotonoha);
    }

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
        if (this.ProjectKotonoha is null)
        {
            return;
        }

        var builder = new IndentedStringBuilder();
        var builder2 = new IndentedStringBuilder();
        try
        {
            this.ProjectKotonoha.Root.UnparseAll(ref builder);

            var path = Path.Combine(this.Project.Directory, Constants.ScrubFileName);
            var st = builder.ToString();
            File.WriteAllText(path, st, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var bin = TinyhandSerializer.Serialize(this.ProjectKotonoha);
            var kotonoha = new Kotonoha(this);
            TinyhandSerializer.DeserializeObject(bin, ref kotonoha);
            if (kotonoha is null)
            {
                return;
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

    internal bool TryResolveValue(IdentifierNameKoto koto, out LimitedValue limitedValue)
    {
        return this.CompilationVariables.TryGetValue(koto.Identifier, out limitedValue);
    }
}
