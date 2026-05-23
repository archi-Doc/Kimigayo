namespace Playground;

using Arc.Unit;
using Kimigayo;
using Kimigayo.Builder;
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

        var builder = new Builder();
        builder.TryReadFile("aaa");
        builder.Build();
        // var tree = CodeTree.Parse("");

        var file = new BuilderFile();
        var st = TinyhandSerializer.SerializeToString(file);
    }
}
