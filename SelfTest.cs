namespace SWCouponManager;

internal static class SelfTest
{
    public static int RunLiveScan()
    {
        try
        {
            var result = new CouponSourceService().ScanAsync().GetAwaiter().GetResult();
            var falsePositives = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "4797F9EE", "8769D0CFAAB3", "999999PX",
                "COM2USMAANSE", "MONSTERS36", "MONSTERSGAME"
            };
            Require(result.Codes.Count > 0, "실제 소스에서 쿠폰을 찾지 못함");
            Require(result.Codes.All(code => !falsePositives.Contains(code)), "실제 소스 오탐 발견");
            Require(result.Codes.Contains("AUGSW2026V7N", StringComparer.OrdinalIgnoreCase) ||
                    result.Codes.Contains("SWCTICKET2HAMBURG", StringComparer.OrdinalIgnoreCase),
                    "현재 알려진 정상 쿠폰을 찾지 못함");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-scan-test.log"),
                "Codes: " + string.Join(", ", result.Codes) + Environment.NewLine +
                "Errors: " + string.Join(" / ", result.Errors));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-scan-test.log"), ex.ToString());
            return 1;
        }
    }

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerSelfTest", Guid.NewGuid().ToString("N"));

        try
        {
            var storage = new AppStorage(root);
            var account = new Account
            {
                Id = "test-account",
                Name = "테스트",
                HiveId = "local-only",
                Selected = true
            };
            var state = new AppState { Accounts = [account] };
            state.History[account.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["TESTCODE1"] = new CouponRecord { Status = "success", Message = "ok" }
            };

            storage.Save(state);
            state.LastScanCodes = ["TESTCODE1"];
            state.CodeSources["TESTCODE1"] = ["SelfTest"];
            storage.Save(state);

            var reloaded = storage.Load();
            Require(reloaded.Accounts.Count == 1, "계정 복원 실패");
            Require(reloaded.History[account.Id]["TESTCODE1"].Status == "success", "기록 복원 실패");
            Require(reloaded.LastScanCodes.Contains("TESTCODE1"), "스캔 결과 복원 실패");
            Require(File.Exists(storage.BackupPath), "백업 파일 생성 실패");

            File.WriteAllText(storage.StatePath, "{ damaged json");
            var recovered = storage.Load();
            Require(recovered.Accounts.Count == 1, "손상 파일 백업 복구 실패");
            Require(recovered.History[account.Id]["TESTCODE1"].Status == "success", "백업 기록 복구 실패");

            TestCouponParsers();
            TestHiveResultClassification();

            return 0;
        }
        catch
        {
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void TestCouponParsers()
    {
        var falsePositives = new[]
        {
            "4797F9EE", "8769D0CFAAB3", "999999PX",
            "COM2USMAANSE", "MONSTERS36", "MONSTERSGAME"
        };

        var swgt = """
        <nav>4797F9EE MONSTERSGAME</nav>
        <table><tr><td><a href="https://withhive.me/313/AUGSW2026V7N">AUGSW2026V7N</a></td></tr></table>
        """;
        var teams = """
        <script>const id='8769D0CFAAB3';</script>
        <section class="codes"><code> SWCTICKET2HAMBURG </code></section>
        """;
        var swq = """
        <table>
          <tr><td>COM2USMAANSE</td><td>Expired</td></tr>
          <tr><td>INVOCATEUREU26</td><td>Active</td></tr>
        </table>
        """;

        var actual = CouponSourceService.ExtractCodes("SWGT", swgt)
            .Concat(CouponSourceService.ExtractCodes("SW-Teams", teams))
            .Concat(CouponSourceService.ExtractCodes("SWQ", swq))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Require(actual.SetEquals(["AUGSW2026V7N", "SWCTICKET2HAMBURG", "INVOCATEUREU26"]),
                "출처별 쿠폰 파서 결과 오류");
        Require(falsePositives.All(x => !actual.Contains(x)), "가짜 쿠폰 오탐 회귀");
    }

    private static void TestHiveResultClassification()
    {
        Require(MainForm.Classify("쿠폰 선물 지급이 완료되었습니다.") == "success", "성공 결과 분류 실패");
        Require(MainForm.Classify("이미 사용된 쿠폰 코드입니다.") == "already", "이미 사용 결과 분류 실패");
        Require(MainForm.Classify("만료된 쿠폰입니다.") == "expired", "만료 결과 분류 실패");
        Require(MainForm.Classify("유효한 쿠폰 코드가 아닙니다. 다시 확인해 주세요.") == "invalid",
                "Hive 무효 결과 분류 실패");
        Require(MainForm.Classify("일시적인 오류가 발생 되었습니다.") == "error", "오류 결과 분류 실패");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
