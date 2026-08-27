using Microsoft.Win32;

namespace SWCouponManager;

internal sealed record LoginStartRegistration(string Name, string ExecutablePath, string Arguments)
{
    internal string CommandLine => $"\"{ExecutablePath}\" {Arguments}";
}

internal static class LoginStartRegistrationFactory
{
    internal static LoginStartRegistration Create(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("로그인 시작 실행 파일은 절대 경로여야 합니다.", nameof(executablePath));
        return new LoginStartRegistration(
            "SWCouponManager Background Agent",
            Path.GetFullPath(executablePath),
            "--background");
    }
}

internal static class LoginStartRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static void Ensure(string executablePath, Action<string, string>? setValue = null)
    {
        var spec = LoginStartRegistrationFactory.Create(executablePath);
        setValue ??= (name, commandLine) =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows 로그인 시작 레지스트리를 열 수 없습니다.");
            key.SetValue(name, commandLine, RegistryValueKind.String);
        };
        setValue(spec.Name, spec.CommandLine);
    }

    internal static void Remove(Action<string>? deleteValue = null)
    {
        deleteValue ??= name =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        };
        deleteValue("SWCouponManager Background Agent");
    }
}

internal static class BackgroundActivation
{
    internal static void Enable(AppStorage storage, string executablePath,
        Action<string, string>? setValue = null, Action<string>? rollbackDelete = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        LoginStartRegistrationService.Ensure(executablePath, setValue);
        try
        {
            var state = storage.Load();
            state.BackgroundAutomationEnabled = true;
            state.BackgroundAutomationPaused = false;
            storage.Save(state);
        }
        catch
        {
            LoginStartRegistrationService.Remove(rollbackDelete);
            throw;
        }
    }
}
