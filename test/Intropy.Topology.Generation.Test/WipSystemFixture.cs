using System.Reflection;

namespace Intropy.Topology.Generation.Test;

/// <summary>
/// A work-in-progress system assembly — placeholder transport, one-sided topic — built
/// from test/Intropy.Topology.WipSystem on first use. Kept out of the solution so its
/// ISystemDefinition never interferes with discovery in the real test assemblies.
/// </summary>
internal static class WipSystemFixture
{
    private static readonly Lazy<Assembly> s_assembly = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Assembly Assembly => s_assembly.Value;

    private static Assembly Build()
    {
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Intropy.Topology.WipSystem"));
        var build = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", projectDir, "-c", "Debug", "--nologo", "-v", "q" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        build.WaitForExit();
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WIP system fixture build failed:{Environment.NewLine}{build.StandardOutput.ReadToEnd()}{build.StandardError.ReadToEnd()}");
        }

        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Path.Combine(projectDir, "bin", "Debug", "net10.0", "Intropy.Topology.WipSystem.dll"));
    }
}
