using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PremiseSupply;

if (args.Length != 1 || args[0] is not ("matrix" or "inputs" or "added-inputs" or "discriminators" or "boundaries" or "lifecycle"))
    throw new ArgumentException("Kernel inputs|added-inputs|discriminators|matrix|boundaries|lifecycle");
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
var added = args[0] is "added-inputs" or "discriminators";
var inputs = added
    ? JsonSerializer.Deserialize<Input[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "additional-inputs.json")), options)
        ?? throw new InvalidOperationException("Missing additional frozen inputs")
    : Cases.All();
if (args[0] is "inputs" or "added-inputs")
{
    foreach (var input in inputs) Console.WriteLine(JsonSerializer.Serialize(input, options));
    return;
}
var expectedPath = Path.Combine(AppContext.BaseDirectory, added ? "additional-expectations.json" : "expectations.json");
var expectations = JsonSerializer.Deserialize<Expectation[]>(File.ReadAllText(expectedPath), options)
    ?? throw new InvalidOperationException("Missing frozen expectations");
var harness = new Harness(options, expectations);
Console.WriteLine(JsonSerializer.Serialize(new
{
    kind = "identity", assembly = typeof(Harness).Assembly.Location,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Harness).Assembly.Location))),
    mvid = typeof(Harness).Module.ModuleVersionId,
    expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath))),
    mode = args[0], sourceBackedCompletion = false,
}, options));
if (args[0] is "matrix" or "discriminators") harness.Matrix(inputs);
if (args[0] == "boundaries") harness.Boundaries(inputs.Single(x => x.Id == "budget-chain"));
if (args[0] == "lifecycle") harness.Lifecycle(inputs);
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", harness.Checks, harness.Failures }, options));
Environment.ExitCode = harness.Failures == 0 ? 0 : 1;
