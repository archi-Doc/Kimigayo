namespace Playground;

using Arc.Unit;
using Kimigayo;
using SimplePrompt;
using Tinyhand;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class BuilderFile
{
    [TinyhandObject(ImplicitMemberNameAsKey = true)]
    public partial record class DefaultClass
    {
        public double LangVersion { get; init; } = 0.1d;

        public double Version { get; init; } = 0.1d;
    }

    public string[] Projects { get; init; } = ["aaa"];

    public DefaultClass Default { get; init; } = new();
}

public class Builder
{
    public IConsoleService ConsoleService { get; set; }

    public Builder()
    {
        this.ConsoleService = SimpleConsole.Instance;
    }

    public bool TryReadFile(string file)
    {
        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(file);

        }
        catch
        {
            return false;
        }

        return true;
    }
}

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
        // var tree = CodeTree.Parse("");

        var file = new BuilderFile();
        var st = TinyhandSerializer.SerializeToString(file);
    }
}
