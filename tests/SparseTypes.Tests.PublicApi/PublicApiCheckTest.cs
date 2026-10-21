using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text;
using PublicApiGenerator;

namespace SparseTypes.Tests.PublicApi;

public static class PublicApiCheck
{
    private const string UpdateApprovalsEnvironmentVariable = "SPARSETYPES_UPDATE_PUBLIC_API";

    public static void Check<T>() => Check(typeof(T).Assembly, null);

    public static void CheckAssembly(Assembly assembly, string approvalFileName) => Check(assembly, approvalFileName);

    private static void Check(Assembly assembly, string? approvalFileName)
    {
        approvalFileName ??= $"{assembly.GetName().Name!}.approved.txt";
        var publicApi = Generate(assembly);

        if (Environment.GetEnvironmentVariable(UpdateApprovalsEnvironmentVariable) == "1")
        {
            var sourceApproval = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Approvals", approvalFileName));
            Directory.CreateDirectory(Path.GetDirectoryName(sourceApproval)!);
            File.WriteAllText(sourceApproval, publicApi, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        var approvedApi = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Approvals", approvalFileName));
        publicApi.ReplaceLineEndings("\n").ShouldBe(approvedApi.ReplaceLineEndings("\n"));
    }

    internal static string Generate(Assembly assembly) =>
        assembly.GeneratePublicApi(
            new()
            {
                IncludeTypes = GetPublicTypes(assembly).OrderBy(static type => type.FullName, StringComparer.Ordinal).ToArray(),
                ExcludeAttributes = [typeof(InternalsVisibleToAttribute).FullName!, typeof(TargetFrameworkAttribute).FullName!],
            }
        );

    private static IEnumerable<Type> GetPublicTypes(Assembly assembly) => assembly.GetExportedTypes().DistinctBy(static type => type.FullName);
}

public sealed class PublicApiCheckTest
{
    [Test]
    public void StandaloneFullRuntime() => PublicApiCheck.CheckAssembly(System.Reflection.Assembly.Load("SparseTypes"), "SparseTypes.approved.txt");

    [Test]
    public void StandaloneCore() =>
        PublicApiCheck.CheckAssembly(System.Reflection.Assembly.Load("SparseTypes.Core"), "SparseTypes.Core.approved.txt");

    [Test]
    public void StandaloneFragmentsRuntime() =>
        PublicApiCheck.CheckAssembly(System.Reflection.Assembly.Load("SparseTypes.Fragments"), "SparseTypes.Fragments.approved.txt");

    [Test]
    public void StandaloneGenerator() =>
        PublicApiCheck.CheckAssembly(System.Reflection.Assembly.Load("SparseTypes.Generator"), "SparseTypes.Generator.approved.txt");

    [Test]
    public void StandaloneFragmentsGenerator() =>
        PublicApiCheck.CheckAssembly(
            System.Reflection.Assembly.Load("SparseTypes.Fragments.Generator"),
            "SparseTypes.Fragments.Generator.approved.txt"
        );

    [Test]
    public void RuntimeTargetFrameworksHaveIdenticalPublicApis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var targetFrameworks = new[] { "netstandard2.0", "net6.0", "net10.0" };
        var packages = new[]
        {
            ("SparseTypes", new[] { "SparseTypes", "SparseTypes.Core" }),
            ("SparseTypes.Core", new[] { "SparseTypes.Core" }),
            ("SparseTypes.Fragments", new[] { "SparseTypes.Fragments", "SparseTypes.Core" }),
        };

        foreach (var (packageName, dependencyNames) in packages)
        {
            string? expectedApi = null;
            foreach (var targetFramework in targetFrameworks)
            {
                var packageDirectory = Path.Combine(repositoryRoot, "src", packageName, "bin", configuration, targetFramework);
                var assemblyPath = Path.Combine(packageDirectory, $"{packageName}.dll");
                var dependencyDirectories = dependencyNames
                    .Select(name => Path.Combine(repositoryRoot, "src", name, "bin", configuration, targetFramework))
                    .ToArray();

                var loadContext = new RuntimeTargetLoadContext(new[] { packageDirectory }.Concat(dependencyDirectories).ToArray());
                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                    var api = PublicApiCheck.Generate(assembly);
                    if (expectedApi is null)
                    {
                        expectedApi = api;
                    }
                    else
                    {
                        api.ShouldBe(expectedApi);
                    }
                }
                finally
                {
                    loadContext.Unload();
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SparseTypes.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the SparseTypes repository root.");
    }

    private sealed class RuntimeTargetLoadContext : AssemblyLoadContext
    {
        private readonly string[] _dependencyDirectories;

        public RuntimeTargetLoadContext(params string[] dependencyDirectories)
            : base(isCollectible: true) => _dependencyDirectories = dependencyDirectories;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var directory in _dependencyDirectories)
            {
                var candidate = Path.Combine(directory, $"{assemblyName.Name}.dll");
                if (File.Exists(candidate))
                {
                    return LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        }
    }
}
