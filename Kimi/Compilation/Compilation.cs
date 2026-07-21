// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;

namespace Kimigayo.Language;

public class Compilation
{
    #region FieldAndProperty

    public KimiControl KimiControl { get; }

    public KimiOptions KimiOptions { get; private set; }

    public ProjectFile ProjectFile { get; }

    public string ProjectName { get; }

    public string Target { get; private set; } = string.Empty;

    public TargetTriple TargetTriple { get; private set; } = TargetTriple.Empty;

    public KotonohaIdentifier[] KotonohaArray { get; private set; } = [];

    public Kotonoha? ProjectKotonoha { get; private set; }

    private UInt32Hashtable<Kotonoha> kotonohaIdToKotonoha = new();

    #endregion

    public Compilation(KimiControl kimiControl, KimiOptions kimiOptions, ProjectFile projectFile, string projectName)
    {
        this.KimiControl = kimiControl;
        this.KimiOptions = kimiOptions;
        this.ProjectFile = projectFile;
        this.ProjectName = projectName;
    }

    public CodeContext CreateCodeContext(Kotonoha kotonoha, string[]? aliases = default)
    {
        return new(this, kotonoha);
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
        this.ProjectKotonoha = new(this, this.ProjectName, string.Empty);

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

        using var writer = new StringWriter();
        this.ProjectKotonoha.RootKoto.UnparseAll(writer);
        var sb = writer.ToString();

        var bin = TinyhandSerializer.Serialize(this.ProjectKotonoha);
        var kotonoha = new Kotonoha(this);
        TinyhandSerializer.DeserializeObject(bin, ref kotonoha);
        if (kotonoha is null)
        {
            return;
        }

        using var writer2 = new StringWriter();
        kotonoha.RootKoto.UnparseTo(writer2);
        var sb2 = writer2.ToString();
    }
}
