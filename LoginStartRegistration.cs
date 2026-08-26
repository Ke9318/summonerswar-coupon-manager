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
