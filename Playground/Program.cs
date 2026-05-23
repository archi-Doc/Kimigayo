namespace Playground;

using Arc.Unit;
using Kimigayo;

public class Builder
{
    public IConsoleService ConsoleService { get; set; }

    public Builder()
    {
        this.ConsoleService = SimplePrompt.
    }
}

public class KimiParser
{
    public KimiParser()
    {
    }

    public CodeTree TryParse(ReadOnlySpan<char> text)
    {
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var tree = CodeTree.Parse("");
    }
}
