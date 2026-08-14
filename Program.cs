namespace SWCouponManager;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => CrashReporter.Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                CrashReporter.Report(e.ExceptionObject as Exception ?? new Exception("알 수 없는 오류"));
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex);
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
