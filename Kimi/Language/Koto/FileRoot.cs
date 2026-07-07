// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class FileRoot
{
    public DiagnosticCollection Diagnostic { get; }

    public GroupKoto CurrentGroup { get; private set; }

    private readonly GroupKoto rootGroup;
    private readonly HashSet<string> alias = new();
    private readonly Utf16Hashtable<GroupKoto> namespaceToGroupNode = new();

    public FileRoot(DiagnosticCollection diagnostic)
    {
        this.rootGroup = new(new FrontendMetadata(default!, default, default!));
        this.Diagnostic = diagnostic;
        this.SetNamespace(Constants.DefaultNamespace);
    }

    [MemberNotNull(nameof(CurrentGroup))]
    public void SetNamespace(ReadOnlySpan<char> qualifiedName)
    {
        this.CurrentGroup = this.rootGroup.GetOrAddGroup(qualifiedName, KotoKind.Namespace);
    }

    public void Parse(ref TokenReader reader)
    {
        while (reader.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }
            else if (token.Kind == TokenKind.At)
            {// @Attribute
                _ = KotoParser.ConsumeAttribute(ref reader);
                /*if (koto is not null)
                {
                    this.CurrentGroup.Add(koto);

                    var sb = new StringBuilder();
                    using var writer = new StringWriter(sb);
                    KotoHelper.Dump(koto, writer);
                    var st = sb.ToString();
                }*/
            }
            else if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                /*if (!this.allowTopLevelKeyword)
                {
                    // goto UnexpectedTopLevelKeyword;
                }*/

                reader.Advance();
                var list = KotoHelper.ValidateAndGetNamespace2(ref reader);
                var aliasKoto = new AliasKoto(ref reader, list);
                this.CurrentGroup.Add(aliasKoto);
                // this.alias.Add(qualifiedName);
            }
            else
            {// Delegate processing to CurrentGroup because this token is not a top-level keyword.
                break;
            }

            this.CurrentGroup.Parse(ref reader);
        }
    }
}
