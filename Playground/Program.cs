namespace Playground;

using Arc.Unit;
using Kimigayo;
using Kimigayo.Project;
using Kimigayo.Solution;
using SimplePrompt;
using Tinyhand;

public class KimiParser
{
    public KimiParser()
    {
    }
}

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var builder = new Solution();
        builder.TryReadFile("aaa");
        builder.Build();
        // var tree = CodeTree.Parse("");
    }
}
