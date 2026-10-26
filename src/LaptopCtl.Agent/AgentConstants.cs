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
    private static readonly string CommonAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string ProgramDataDirectory =
        Path.Combine(CommonAppData, "LaptopCtl");

    public static readonly string BaseDirectory =
        Path.Combine(LocalAppData, "LaptopCtl", "Agent");

    public static readonly string LogDirectory =
        Path.Combine(ProgramDataDirectory, "logs");

    public static string ConfigFile
    {
        get
        {
            var pdConfig = Path.Combine(ProgramDataDirectory, "appsettings.json");
            if (File.Exists(pdConfig)) return pdConfig;

            var appDirConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(appDirConfig)) return appDirConfig;

            return Path.Combine(BaseDirectory, "agent.json");
        }
    }

    public static string IdentityFile
    {
        get
        {
            // 1. Check ProgramData (written by install.ps1)
            var pdIdentity = Path.Combine(ProgramDataDirectory, "identity.json");
            if (File.Exists(pdIdentity)) return pdIdentity;

            // 2. Check App Directory (C:\Program Files\LaptopCtl\identity.json)
            var appDirIdentity = Path.Combine(AppContext.BaseDirectory, "identity.json");
            if (File.Exists(appDirIdentity)) return appDirIdentity;

            // 3. Fallback to LocalAppData
            var localIdentity = Path.Combine(BaseDirectory, "identity.json");
            if (File.Exists(localIdentity)) return localIdentity;

            // Default to ProgramData path
            return pdIdentity;
        }
    }

    static AgentPaths()
    {
        try { Directory.CreateDirectory(ProgramDataDirectory); } catch {}
        try { Directory.CreateDirectory(BaseDirectory); } catch {}
        try { Directory.CreateDirectory(LogDirectory); } catch {}
    }
}
