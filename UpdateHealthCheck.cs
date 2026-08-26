using System.Text.Json;

namespace SWCouponManager;

internal static class UpdateHealthCheck
{
    private static readonly string[] RequiredFiles =
    [
        "SWCouponManager.exe",
        "SWCouponManager.dll",
        "SWCouponManager.deps.json",
        "SWCouponManager.runtimeconfig.json",
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.WinForms.dll",
        Path.Combine("runtimes", "win-x64", "native", "WebView2Loader.dll")
    ];

    public static int Run(string[] args)
    {
        try
        {
            var expected = ReadExpectedVersion(args);
            ValidateInstallLayout(AppContext.BaseDirectory, expected);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    internal static void ValidateInstallLayout(string root, Version expectedVersion)
    {
        foreach (var relative in RequiredFiles)
            if (!File.Exists(Path.Combine(root, relative)))
                throw new InvalidDataException($"업데이트 필수 파일이 없습니다: {relative}");

        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "SWCouponManager.deps.json")))) { }
        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "SWCouponManager.runtimeconfig.json")))) { }

        var assemblyVersion = System.Reflection.AssemblyName
            .GetAssemblyName(Path.Combine(root, "SWCouponManager.dll")).Version;
        if (assemblyVersion is null || assemblyVersion.Major != expectedVersion.Major ||
            assemblyVersion.Minor != expectedVersion.Minor || assemblyVersion.Build != expectedVersion.Build)
            throw new InvalidDataException($"업데이트 버전이 일치하지 않습니다: expected {expectedVersion}, actual {assemblyVersion}");
    }

    private static Version ReadExpectedVersion(string[] args)
    {
        var index = Array.FindIndex(args, x => x.Equals("--expected-version", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !Version.TryParse(args[index + 1], out var version))
            throw new ArgumentException("--expected-version 값이 필요합니다.");
        return version;
    }
}
