using System.Reflection;

namespace LaptopCtl.Agent;

public static class AgentVersion
{
    public static readonly string Current =
        typeof(AgentVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
}

public static class AgentPaths
{
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string BaseDirectory =
        Path.Combine(AppData, "LaptopCtl", "Agent");

    public static readonly string LogDirectory =
        Path.Combine(BaseDirectory, "logs");

    public static readonly string ConfigFile =
        Path.Combine(BaseDirectory, "agent.json");

    public static readonly string IdentityFile =
        Path.Combine(BaseDirectory, "identity.json");

    static AgentPaths()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
