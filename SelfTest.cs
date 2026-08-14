namespace SWCouponManager;

internal static class SelfTest
{
    public static int RunLiveScan()
    {
        try
        {
            var result = new CouponSourceService().ScanAsync().GetAwaiter().GetResult();
            Require(result.Codes.Count > 0, "실제 소스에서 쿠폰 후보를 찾지 못함");
            Require(result.SuccessfulSources.Count > 0, "성공한 쿠폰 소스가 없음");
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
                ["TESTCODE1"] = new CouponRecord { Status = "success", Message = "ok" },
                ["RETRYCODE"] = new CouponRecord { Status = "error", Message = "temporary" }
            };

            storage.Save(state);
            state.LastScanCodes = ["TESTCODE1", "RETRYCODE"];
            state.CodeSources["TESTCODE1"] = ["SelfTest"];
            storage.Save(state);

            var reloaded = storage.Load();
            Require(reloaded.Accounts.Count == 1, "계정 복원 실패");
            Require(reloaded.History[account.Id]["TESTCODE1"].Status == "success", "기록 복원 실패");
            Require(reloaded.History[account.Id]["RETRYCODE"].Status == "error", "오류 기록 복원 실패");
            Require(reloaded.LastScanCodes.Contains("TESTCODE1"), "스캔 결과 복원 실패");
            Require(File.Exists(storage.BackupPath), "백업 파일 생성 실패");

            File.WriteAllText(storage.StatePath, "{ damaged json");
            var recovered = storage.Load();
            Require(recovered.Accounts.Count == 1, "손상 파일 백업 복구 실패");
            Require(recovered.History[account.Id]["TESTCODE1"].Status == "success", "백업 기록 복구 실패");

            TestCouponParsers();
            TestHiveResultClassification();
            TestRetryPolicy();
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
        var swgt = """
        <nav>MONSTERSGAME BONUSLETTERS AUGSW2026V7N</nav>
        <script>const id='8769D0CFAAB3'; const hash='ABCDEF0123456789ABCDEF0123456789';</script>
        <style>.ABCDEF0123456789ABCDEF { color: #ffffff; }</style>
        <p>https://example.com/URLTOKEN999</p>
        <p>550e8400-e29b-41d4-a716-446655440000</p>
        <table><tr><td><a href="https://withhive.me/313/AUGSW2026V7N">AUGSW2026V7N</a></td></tr></table>
        """;
        var teams = """
        <script>const id='8769D0CFAAB3';</script>
        <section class="codes"><code> SWCTICKET2HAMBURG </code></section>
        <div>TEAMONLY777</div>
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

        var expected = new[]
        {
            "AUGSW2026V7N", "SWCTICKET2HAMBURG", "INVOCATEUREU26",
            "COM2USMAANSE", "MONSTERSGAME", "BONUSLETTERS", "TEAMONLY777"
        };
        Require(expected.All(actual.Contains), "확정 파서 또는 본문 fallback 후보 누락");
        Require(!actual.Contains("8769D0CFAAB3"), "스크립트 내부 문자열 오탐");
        Require(!actual.Any(x => x.Length >= 20 && x.All(Uri.IsHexDigit)), "긴 해시 오탐");
        Require(!actual.Contains("URLTOKEN999"), "URL 내부 문자열 오탐");
        Require(actual.Count == actual.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "후보 중복 제거 실패");
    }

    private static void TestHiveResultClassification()
    {
        Require(MainForm.Classify("쿠폰 보상 지급이 완료되었습니다.") == "success", "성공 결과 분류 실패");
        Require(MainForm.Classify("이미 사용한 쿠폰 코드입니다.") == "already", "이미 사용 결과 분류 실패");
        Require(MainForm.Classify("만료된 쿠폰입니다.") == "expired", "만료 결과 분류 실패");
        Require(MainForm.Classify("유효한 쿠폰 코드가 아닙니다. 다시 확인해 주세요.") == "invalid", "Hive 무효 결과 분류 실패");
        Require(MainForm.Classify("일시적인 오류가 발생했습니다.") == "error", "오류 결과 분류 실패");
    }

    private static void TestRetryPolicy()
    {
        foreach (var status in new[] { "success", "already", "expired", "invalid" })
            Require(MainForm.IsCompletedStatus(status), $"완료 상태 재시도 차단 실패: {status}");

        Require(!MainForm.IsCompletedStatus("error"), "오류 상태가 재시도 불가로 저장됨");
        Require(!MainForm.IsCompletedStatus(null), "기록 없는 후보가 재시도 불가로 저장됨");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
