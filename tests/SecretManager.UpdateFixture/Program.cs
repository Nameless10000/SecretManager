using System.Text.Json;

// Synthetic installer for the update handoff test. Never included in application packaging.
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
