using System.Text.Json;
using SecretManager.App.Services;

// Synthetic installer for the update handoff test. Never included in application packaging.
if (args.Length == 2 && args[0] == "--check-updates")
{
    using var client = UpdateService.CreateClient();
    var result = await new UpdateService(client).CheckAsync(Version.Parse(args[1]), default);
    Console.WriteLine(JsonSerializer.Serialize(new { Status = result.Status.ToString(), Version = result.Release?.Version.ToString() }));
    return 0;
}
if (args.Length == 2 && args[0] == "--parent")
{
    File.WriteAllText(Path.Combine(args[1], "parent-ready"), "ready");
    var timeout = DateTime.UtcNow.AddSeconds(10);
    while (!File.Exists(Path.Combine(args[1], "parent-release")))
    { if (DateTime.UtcNow >= timeout) return 2; await Task.Delay(20); }
    return 0;
}
var directory = args.Single(a => a.StartsWith("/DIR=", StringComparison.Ordinal))[5..].Trim('"');
File.WriteAllText(Path.Combine(directory, "fixture-invocation.json"), JsonSerializer.Serialize(args));
return 0;
