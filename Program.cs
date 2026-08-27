namespace SWCouponManager;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();
        if (args.Contains("--scan-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.RunLiveScan();
        if (args.Contains("--audit-history", StringComparer.OrdinalIgnoreCase))
            return HistoryAudit.Run(args);
        if (args.Contains("--update-health-check", StringComparer.OrdinalIgnoreCase))
            return UpdateHealthCheck.Run(args);
        if (args.Contains("--build-manifest", StringComparer.OrdinalIgnoreCase))
            return CloudWatcher.RunAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--anomaly-evidence", StringComparer.OrdinalIgnoreCase))
            return AnomalyEvidenceService.RunAsync(args).GetAwaiter().GetResult();
        if (args.Contains("--disposable-update-client", StringComparer.OrdinalIgnoreCase))
            return RunDisposableUpdateClient(args);

        try
        {
            ApplicationConfiguration.Initialize();
            if (!RuntimePrerequisiteChecker.EnsureAvailable())
                return 2;

            var singleInstanceProbe = args.Contains("--single-instance-probe", StringComparer.OrdinalIgnoreCase);
            using var instanceLease = SingleInstanceLease.TryAcquire("Local\\SWCouponManager");
            if (instanceLease is null)
            {
                if (singleInstanceProbe) return 3;
                MessageBox.Show(
                    "SWCouponManager가 이미 실행 중입니다. 알림 영역이나 열린 창을 확인해 주세요.",
                    "SWCouponManager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }
            if (singleInstanceProbe) return 0;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => CrashReporter.Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                CrashReporter.Report(e.ExceptionObject as Exception ?? new Exception("알 수 없는 오류"));
            var smoke = args.Contains("--gui-smoke-test", StringComparer.OrdinalIgnoreCase);
            var allowTerminalOverride = args.Contains("--approved-live-retry-terminal-one", StringComparer.OrdinalIgnoreCase);
            var approvedLiveOne = allowTerminalOverride ||
                args.Contains("--approved-live-redemption-one", StringComparer.OrdinalIgnoreCase);
            AppStorage? smokeStorage = null;
            if (smoke)
            {
                var dataIndex = Array.FindIndex(args, arg => arg.Equals("--data-dir", StringComparison.OrdinalIgnoreCase));
                if (dataIndex < 0 || dataIndex + 1 >= args.Length || !Path.IsPathFullyQualified(args[dataIndex + 1]))
                    throw new ArgumentException("GUI smoke에는 절대 --data-dir 경로가 필요합니다.");
                smokeStorage = new AppStorage(Path.GetFullPath(args[dataIndex + 1]));
                var syntheticState = smokeStorage.Load();
                syntheticState.BackgroundAutomationEnabled = true;
                syntheticState.BackgroundAutomationPaused = false;
                smokeStorage.Save(syntheticState);
            }
            string? liveResultPath = null;
            if (approvedLiveOne)
            {
                var resultIndex = Array.FindIndex(args, arg => arg.Equals("--result-file", StringComparison.OrdinalIgnoreCase));
                if (resultIndex < 0 || resultIndex + 1 >= args.Length || !Path.IsPathFullyQualified(args[resultIndex + 1]))
                    throw new ArgumentException("승인된 live one 테스트에는 절대 --result-file 경로가 필요합니다.");
                liveResultPath = Path.GetFullPath(args[resultIndex + 1]);
            }
            var form = smoke
                ? new MainForm(smokeStorage!, suppressStartupNetwork: true)
                : approvedLiveOne
                    ? new MainForm(new AppStorage(), suppressStartupNetwork: false, approvedLiveOne: true,
                        resultPath: liveResultPath, allowTerminalOverride: allowTerminalOverride)
                    : new MainForm();
            if (smoke)
            {
                var holdIndex = Array.FindIndex(args, arg => arg.Equals("--gui-smoke-hold-ms", StringComparison.OrdinalIgnoreCase));
                var holdMs = holdIndex >= 0 && holdIndex + 1 < args.Length && int.TryParse(args[holdIndex + 1], out var parsed)
                    ? Math.Clamp(parsed, 250, 30000) : 1000;
                var timer = new System.Windows.Forms.Timer { Interval = holdMs };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    form.RequestExplicitExit();
                };
                timer.Start();
            }
            Application.Run(form);
            return approvedLiveOne ? form.ApprovedLiveOneExitCode : 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex);
            return 1;
        }
    }

    private static int RunDisposableUpdateClient(string[] args)
    {
        try
        {
            string Required(string name)
            {
                var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException($"{name} 값이 필요합니다.");
                return args[index + 1];
            }
            var version = Version.Parse(Required("--expected-version"));
            var update = new UpdateInfo(version, "v" + version.ToString(3),
                Required("--zip-url"), Required("--checksum-url"));
            new GitHubUpdateService().DownloadAndRestartAsync(update).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}

internal static class CrashReporter
{
    public static void Report(Exception exception)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SWCouponManager");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "fatal.log"),
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        try
        {
            MessageBox.Show(
                "프로그램에서 처리하지 못한 오류가 발생했습니다. %LOCALAPPDATA%\\SWCouponManager\\fatal.log를 확인해 주세요.",
                "SWCouponManager 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }
    }
}
