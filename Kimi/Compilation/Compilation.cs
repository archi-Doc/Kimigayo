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

    private Kotonoha? projectKotonoha;

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

    public bool Prepare(string target)
    {
        if (!TargetTripleParser.TryParse(target, out var targetTriple))
        {
            return false;
        }

        this.Target = target;
        this.TargetTriple = targetTriple;

        // Prepare Kotonoha

        this.projectKotonoha = new(this.ProjectName, string.Empty);

        return true;
    }

    public void Parse(PathAndSource pathAndSource)
    {
        if (this.projectKotonoha is null)
        {
            return;
        }

        this.projectKotonoha.AddSource(this, pathAndSource);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKotonoha(uint kotonohaId, [MaybeNullWhen(false)] out Kotonoha kotonoha)
    {
        return this.kotonohaIdToKotonoha.TryGetValue(kotonohaId, out kotonoha);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetKoto(ulong kotoId, [MaybeNullWhen(false)] out Koto koto)
    {
        if (this.kotonohaIdToKotonoha.TryGetValue((uint)(kotoId >> 32), out var kotonoha))
        {
            return kotonoha.TryGetKoto(kotoId, out koto);
        }

        koto = default;
        return false;
    }
}
