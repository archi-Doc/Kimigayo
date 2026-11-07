// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Writes syntax as source text: a Container's qualified Name, an attribute chain and declaration modifiers.
// Input: published nodes and ModifierKind values. Output: text, in the caller's IndentedStringBuilder or as a string.
// Invariants: it reads no tokens, reports nothing and changes no node.
public static partial class Parser
{
    /// <summary>Writes the qualified name of a declaration container.</summary>
    /// <param name="koto">The innermost declaration container.</param>
    /// <param name="builder">The destination builder.</param>
    public static void WriteQualifiedNameTo(DeclarationContainerKoto? koto, ref IndentedStringBuilder builder)
    {
        if (koto is null || koto.IsRoot)
        {
            return;
        }

        if (koto.Parent is DeclarationContainerKoto parent && !parent.IsRoot)
        {
            WriteQualifiedNameTo(parent, ref builder);
            builder.Append(Constants.DotChar);
        }

        builder.Append(koto.Name);
    }

    /// <summary>Writes an attribute chain as source text, outermost attribute first.</summary>
    /// <param name="attribute">The first attribute in the chain.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="options">The output options.</param>
    /// <param name="mergeFragmentLayouts">Whether equivalent Layout specifications from separate container fragments are written once.</param>
    public static void UnparseAttribute(AttributeKoto? attribute, ref IndentedStringBuilder builder, KotoWriteOptions options, bool mergeFragmentLayouts = false)
    {
        if (attribute is null)
        {
            return;
        }

        if (mergeFragmentLayouts)
        {
            string? mode = null;
            var fragment = -1;
            for (var current = attribute; current is not null; current = current.AttributeChain)
            {
                if (current.Kind != AttributeKind.Layout)
                {
                    continue;
                }

                if (current.LayoutMode is not { } currentMode || (mode is not null && mode != currentMode) || fragment == current.FragmentOrdinal)
                {
                    mergeFragmentLayouts = false;
                    break;
                }

                mode = currentMode;
                fragment = current.FragmentOrdinal;
            }
        }

        var layoutWritten = false;
        WriteChain(attribute, ref builder, mergeFragmentLayouts, ref layoutWritten);
        builder.AppendTrailingSpaceOrLineFeed(options);

        static void WriteChain(AttributeKoto attribute, ref IndentedStringBuilder builder, bool mergeFragmentLayouts, ref bool layoutWritten)
        {
            if (attribute.AttributeChain is { } previous)
            {
                WriteChain(previous, ref builder, mergeFragmentLayouts, ref layoutWritten);
                if (mergeFragmentLayouts && layoutWritten && attribute.LayoutMode is not null)
                {
                    return;
                }

                builder.Append(' ');
            }

            attribute.WriteTo(ref builder);
            if (mergeFragmentLayouts)
            {
                layoutWritten |= attribute.LayoutMode is not null;
            }
        }
    }

    /// <summary>Writes an attribute chain to a string.</summary>
    /// <param name="attribute">The first attribute in the chain.</param>
    /// <returns>The attribute source text.</returns>
    public static string UnparseAttribute(AttributeKoto? attribute)
    {
        if (attribute is null)
        {
            return string.Empty;
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            UnparseAttribute(attribute, ref builder, KotoWriteOptions.AppendSpace);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Writes declaration modifiers as source text.</summary>
    /// <param name="kind">The modifiers to write.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="writeOptions">The output options.</param>
    public static void WriteTo(this ModifierKind kind, ref IndentedStringBuilder builder, KotoWriteOptions writeOptions)
    {
        if (kind == ModifierKind.NoModifier)
        {
            return;
        }

        var accText = kind.ExtractAccessibilityModifiers().ToText();
        if (accText.Length > 0)
        {
            builder.Append(accText);
        }

        if (kind.HasFlag(ModifierKind.Static))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.StaticKeyword);
        }

        if (kind.HasFlag(ModifierKind.Open))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.OpenKeyword);
        }

        if (kind.HasFlag(ModifierKind.Unsafe))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.UnsafeKeyword);
        }

        if ((kind & ModifierKind.Virtual) != 0)
        {
            builder.EnsureTrailingSpace();
            builder.Append("virtual");
        }

        if ((kind & ModifierKind.Override) != 0)
        {
            builder.EnsureTrailingSpace();
            builder.Append("override");
        }

        builder.AppendTrailingSpaceOrLineFeed(writeOptions);
    }

    /// <summary>Converts declaration modifiers to source text.</summary>
    /// <param name="kind">The modifiers to convert.</param>
    /// <param name="addSpace">Whether to append a trailing space.</param>
    /// <returns>The modifier source text.</returns>
    public static string ToText(this ModifierKind kind, bool addSpace = false)
    {
        var accText = kind.ExtractAccessibilityModifiers() switch
        {
            ModifierKind.Public => Constants.PublicKeyword,
            ModifierKind.Protected => Constants.ProtectedKeyword,
            ModifierKind.Private => Constants.PrivateKeyword,
            ModifierKind.Internal => Constants.InternalKeyword,
            ModifierKind.ProtectedOrInternal => "protected internal",
            ModifierKind.ProtectedAndInternal => "private protected",
            _ => string.Empty,
        };

        if ((kind & (ModifierKind.Static | ModifierKind.Open | ModifierKind.Unsafe | ModifierKind.Virtual | ModifierKind.Override)) == 0)
        {
            return addSpace && accText.Length > 0 ? accText + " " : accText;
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            kind.WriteTo(ref builder, addSpace ? KotoWriteOptions.AppendSpace : KotoWriteOptions.None);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
