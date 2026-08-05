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

    public Kimigayo Kimigayo { get; }

    public Project Project { get; }

    /*public KimiOptions KimiOptions { get; private set; }

    public ProjectFile ProjectFile { get; }

    public string ProjectName { get; }*/

    public string Target { get; private set; } = string.Empty;

    public TargetTriple TargetTriple { get; private set; } = TargetTriple.Empty;

    public KotonohaIdentifier[] KotonohaArray { get; private set; } = [];

    public Kotonoha Kotonoha { get; private set; }

    public Utf16Hashtable<LimitedValue> Variables { get; private set; } = new();

    private UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    #endregion

    public static Compilation CreateForTest()
    {
        var kimigayo = new Kimigayo(new EmptyConsole());
        var project = new Project(kimigayo);
        var compilation = new Compilation(kimigayo, project);

        return compilation;
    }

    public Compilation(Kimigayo kimigayo, Project project)
    {
        this.Kimigayo = kimigayo;
        this.Project = project;
        this.Kotonoha = new(this, this.Project.Name, string.Empty);
    }

    public bool Prepare(string target)
    {
        if (!TargetTripleParser.TryParse(target, out var targetTriple))
        {
            return false;
        }

        this.Target = target;
        this.TargetTriple = targetTriple;

        // External Kotonoha

        // Compilation Variables
        this.Variables.Clear();

        var os = this.TargetTriple.OperatingSystem;
        this.Variables.Add("os", new(os));
        this.Variables.Add("windows", new(string.Equals(os, "Windows", StringComparison.InvariantCultureIgnoreCase)));
        this.Variables.Add("linux", new(string.Equals(os, "linux", StringComparison.InvariantCultureIgnoreCase)));
        this.Variables.Add("macos", new(string.Equals(os, "macos", StringComparison.InvariantCultureIgnoreCase)));

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
        if (this.Kotonoha is null)
        {
            return;
        }

        var builder = new IndentedStringBuilder();
        var builder2 = new IndentedStringBuilder();
        try
        {
            this.Kotonoha.RootKoto.UnparseAll(ref builder);

            var path = Path.Combine(this.Project.Directory, Constants.ScrubFileName);
            var st = builder.ToString();
            File.WriteAllText(path, st, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var bin = TinyhandSerializer.Serialize(this.Kotonoha);
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
        return this.Variables.TryGetValue(koto.Identifier, out limitedValue);
    }
}
