using System.Runtime.CompilerServices;

namespace Kimigayo.Language;

internal abstract record ConditionNode;

internal sealed record ConditionBinaryNode(ConditionBinaryOperator Operator, ConditionNode Left, ConditionNode Right) : ConditionNode;

internal sealed record ConditionUnaryNode(ConditionUnaryOperator Operator, ConditionNode Operand) : ConditionNode;

internal sealed record ConditionIdentifierNode(ReadOnlyMemory<char> Name) : ConditionNode;

internal sealed record ConditionStringNode(ReadOnlyMemory<char> Value) : ConditionNode;

internal enum ConditionBinaryOperator
{
    Equals,
    NotEquals,
    And,
    Or,
}

internal enum ConditionUnaryOperator
{
    Not,
}

internal readonly struct ConditionParseResult
{
    public ConditionParseResult(bool success, ConditionNode? node, int nextIndex)
    {
        this.Success = success;
        this.Node = node;
        this.NextIndex = nextIndex;
    }

    public bool Success { get; }

    public ConditionNode? Node { get; }

    public int NextIndex { get; }
}

internal ref struct ConditionParser
{
    private readonly ReadOnlySpan<Token> tokens;
    private int index;

    public ConditionParser(ReadOnlySpan<Token> tokens)
    {
        this.tokens = tokens;
        this.index = 0;
    }

    public ConditionParseResult ParseConditionDirective()
    {
        // #Condition(...)
        if (!this.TryConsume(TokenKind.Sharp))
        {
            return default;
        }

        if (!this.TryConsumeIdentifier("Condition"))
        {
            return default;
        }

        if (!this.TryConsume(TokenKind.OpenParenthesis))
        {
            return default;
        }

        var expression = this.ParseExpression();
        if (expression is null)
        {
            return default;
        }

        if (!this.TryConsume(TokenKind.CloseParenthesis))
        {
            return default;
        }

        return new(true, expression, this.index);
    }

    private ConditionNode? ParseExpression()
        => this.ParseOr();

    private ConditionNode? ParseOr()
    {
        var left = this.ParseAnd();
        if (left is null)
        {
            return null;
        }

        while (this.TryConsume(TokenKind.BarBar))
        {
            var right = this.ParseAnd();
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(
                ConditionBinaryOperator.Or,
                left,
                right);
        }

        return left;
    }

    private ConditionNode? ParseAnd()
    {
        var left = this.ParseEquality();
        if (left is null)
        {
            return null;
        }

        while (this.TryConsume(TokenKind.AmpersandAmpersand))
        {
            var right = this.ParseEquality();
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(
                ConditionBinaryOperator.And,
                left,
                right);
        }

        return left;
    }

    private ConditionNode? ParseEquality()
    {
        var left = this.ParseUnary();
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            if (this.TryConsume(TokenKind.EqualsEquals))
            {
                var right = this.ParseUnary();
                if (right is null)
                {
                    return null;
                }

                left = new ConditionBinaryNode(
                    ConditionBinaryOperator.Equals,
                    left,
                    right);

                continue;
            }

            if (this.TryConsume(TokenKind.ExclamationEquals))
            {
                var right = this.ParseUnary();
                if (right is null)
                {
                    return null;
                }

                left = new ConditionBinaryNode(
                    ConditionBinaryOperator.NotEquals,
                    left,
                    right);

                continue;
            }

            return left;
        }
    }

    private ConditionNode? ParseUnary()
    {
        if (this.TryConsume(TokenKind.Exclamation))
        {
            var operand = this.ParseUnary();
            if (operand is null)
            {
                return null;
            }

            return new ConditionUnaryNode(
                ConditionUnaryOperator.Not,
                operand);
        }

        return this.ParsePrimary();
    }

    private ConditionNode? ParsePrimary()
    {
        if (this.TryConsume(TokenKind.OpenParenthesis))
        {
            var expression = this.ParseExpression();
            if (expression is null)
            {
                return null;
            }

            if (!this.TryConsume(TokenKind.CloseParenthesis))
            {
                return null;
            }

            return expression;
        }

        if (this.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Identifier)
            {
                this.index++;
                return new ConditionIdentifierNode(token.Text);
            }

            if (token.Kind == TokenKind.Literal)
            {
                this.index++;
                return new ConditionStringNode(UnquoteStringLiteral(token.Text));
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPeek(out Token token)
    {
        if ((uint)this.index < (uint)this.tokens.Length)
        {
            token = this.tokens[this.index];
            return true;
        }

        token = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryConsume(TokenKind kind)
    {
        if ((uint)this.index < (uint)this.tokens.Length &&
            this.tokens[this.index].Kind == kind)
        {
            this.index++;
            return true;
        }

        return false;
    }

    private bool TryConsumeIdentifier(ReadOnlySpan<char> name)
    {
        if ((uint)this.index >= (uint)this.tokens.Length)
        {
            return false;
        }

        var token = this.tokens[this.index];
        if (token.Kind != TokenKind.Identifier)
        {
            return false;
        }

        if (!token.Text.Span.Equals(name, StringComparison.Ordinal))
        {
            return false;
        }

        this.index++;
        return true;
    }

    private static ReadOnlyMemory<char> UnquoteStringLiteral(ReadOnlyMemory<char> text)
    {
        var span = text.Span;

        if (span.Length >= 2 &&
            span[0] == '"' &&
            span[^1] == '"')
        {
            return text.Slice(1, text.Length - 2);
        }

        return text;
    }
}
