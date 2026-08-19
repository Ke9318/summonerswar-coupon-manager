using System.Text.Json;

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
                Server = "europe",
                Selected = true
            };
            var state = new AppState { Accounts = [account] };
            state.History[account.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["TESTCODE1"] = new CouponRecord { Status = "success", Message = "ok" },
                ["RETRYCODE"] = new CouponRecord { Status = "error", Message = "temporary" },
                ["WORLD"] = new CouponRecord { Status = "invalid", Message = "past false positive" },
                ["DELETEDMANUAL1"] = new CouponRecord { Status = "invalid", Message = "removed from manual" }
            };

            storage.Save(state);
            state.LastScanCodes = ["TESTCODE1", "RETRYCODE"];
            state.CodeSources["TESTCODE1"] = ["SelfTest"];
            storage.Save(state);

            var reloaded = storage.Load();
            Require(reloaded.Accounts.Count == 1, "계정 복원 실패");
            Require(reloaded.Accounts[0].Server == "europe", "계정별 서버 복원 실패");
            Require(reloaded.History[account.Id]["TESTCODE1"].Status == "success", "기록 복원 실패");
            Require(reloaded.History[account.Id]["RETRYCODE"].Status == "error", "오류 기록 복원 실패");
            Require(reloaded.History[account.Id]["WORLD"].Status == "invalid", "과거 invalid 오탐 기록 복원 실패");
            Require(reloaded.History[account.Id]["DELETEDMANUAL1"].Status == "invalid", "삭제된 Manual 코드 기록 복원 실패");
            Require(reloaded.LastScanCodes.Contains("TESTCODE1"), "스캔 결과 복원 실패");
            Require(File.Exists(storage.BackupPath), "백업 파일 생성 실패");

            File.WriteAllText(storage.StatePath, "{ damaged json");
            var recovered = storage.Load();
            Require(recovered.Accounts.Count == 1, "손상 파일 백업 복구 실패");
            Require(recovered.History[account.Id]["TESTCODE1"].Status == "success", "백업 기록 복구 실패");
            Require(recovered.History[account.Id]["WORLD"].Status == "invalid", "백업에서 과거 오탐 기록 복구 실패");

            TestCouponParsers();
            TestHiveResultClassification();
            TestRetryPolicy();
            TestServerSelection();
            TestSeenCodes();
            TestSourceMerging();
            TestSourceFailureIsolation();
            TestExplicitSourcePreservation();
            TestHistoryControlsQueue();
            TestSwgtEmptyParserDetection();
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
        <nav>ABOUT ACCESS ACTIVELY ACCOUNT COMMUNITY DASHBOARD PASSWORD USERNAME PRIVACY CONTACT JAVASCRIPT WINDOWS</nav>
        <script>const code='SCRIPTCOUPON123';</script>
        <a href="https://withhive.me/313/912XUXIECHUANQI">받기</a>
        <code>12YJUSTHALFWAY</code>
        <p>Coupon code: ENTERTHESWCERA</p>
        <p>WASWIRDSWC2026 coupon</p>
        """;
        var teams = """
        <table><tr><th>Name</th><th>Coupon Code</th></tr>
          <tr><td>Final</td><td>SWC2026ROADTOWF</td></tr>
          <tr><td>Final</td><td>IGYEORA4WF2026</td></tr>
        </table>
        <script type="application/json">{"promoCode":"OQKR1STWFNUGU"}</script>
        """;
        var swq = """
        <table>
          <tr><th>Code</th><th>Status</th></tr>
          <tr><td>4MINGYIDAOXIAN</td><td>Expired</td></tr>
          <tr><td>YYDSSWC26ZAN</td><td>Active</td></tr>
        </table>
        """;

        var actual = CouponSourceService.ExtractCodes("SWGT", swgt)
            .Concat(CouponSourceService.ExtractCodes("SW-Teams", teams))
            .Concat(CouponSourceService.ExtractCodes("SWQ", swq))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expected = new[]
        {
            "912XUXIECHUANQI", "12YJUSTHALFWAY", "ENTERTHESWCERA",
            "WASWIRDSWC2026", "SWC2026ROADTOWF", "IGYEORA4WF2026",
            "OQKR1STWFNUGU", "4MINGYIDAOXIAN", "YYDSSWC26ZAN"
        };
        var uiWords = new[]
        {
            "ABOUT", "ACCESS", "ACTIVELY", "ACCOUNT", "COMMUNITY", "DASHBOARD",
            "PASSWORD", "USERNAME", "PRIVACY", "CONTACT", "JAVASCRIPT", "WINDOWS"
        };
        Require(expected.All(actual.Contains), "쿠폰 문맥 후보 누락");
        Require(uiWords.All(word => !actual.Contains(word)), "일반 UI 단어가 후보에 포함됨");
        Require(!actual.Contains("SCRIPTCOUPON123"), "JavaScript 내부 문자열 오탐");
        Require(actual.Count == actual.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "후보 중복 제거 실패");

        var remote = CouponSourceService.ExtractRemoteCandidates("""
        {"codes":[{"code":"SWC2026ROADTOWF","source":"manual"}]}
        """);
        Require(remote.SequenceEqual(["SWC2026ROADTOWF"]), "원격 후보 JSON 파싱 실패");
    }

    private static void TestHiveResultClassification()
    {
        Require(MainForm.Classify("쿠폰 보상 지급이 완료되었습니다.") == "success", "성공 결과 분류 실패");
        Require(MainForm.Classify("이미 사용한 쿠폰 코드입니다.") == "already", "이미 사용 결과 분류 실패");
        Require(MainForm.Classify("만료된 쿠폰입니다.") == "expired", "만료 결과 분류 실패");
        Require(MainForm.Classify("유효한 쿠폰 코드가 아닙니다. 다시 확인해 주세요.") == "invalid", "Hive 무효 결과 분류 실패");
        Require(MainForm.Classify("일시적인 오류가 발생했습니다.") == "error", "오류 결과 분류 실패");
        Require(MainForm.Classify("예상하지 못한 새 응답 문구") == "error", "미분류 Hive 응답 재시도 보존 실패");
    }

    private static void TestRetryPolicy()
    {
        foreach (var status in new[] { "success", "already", "expired", "invalid" })
            Require(MainForm.IsCompletedStatus(status), $"완료 상태 재시도 차단 실패: {status}");

        Require(!MainForm.IsCompletedStatus("error"), "오류 상태가 재시도 불가로 저장됨");
        Require(!MainForm.IsCompletedStatus(null), "기록 없는 후보가 재시도 불가로 저장됨");
    }

    private static void TestServerSelection()
    {
        foreach (var server in new[] { "global", "korea", "japan", "china", "asia", "europe" })
            Require(MainForm.NormalizeServer(server) == server, $"서버 값 보존 실패: {server}");

        Require(MainForm.NormalizeServer(null) == "korea", "기존 계정 기본 서버 보정 실패");
        Require(MainForm.NormalizeServer("unknown") == "korea", "잘못된 서버 기본값 보정 실패");
    }

    private static void TestSeenCodes()
    {
        var seen = new HashSet<string>(["OLD123"], StringComparer.OrdinalIgnoreCase);
        var newCodes = MainForm.GetNewCodes(["old123", "NEW456", "NEW456"], seen);
        Require(newCodes.SequenceEqual(["NEW456"]), "SeenCodes 신규 판단 실패");
    }

    private static void TestSourceMerging()
    {
        var result = CouponSourceService.MergeResults(
        [
            new("SWGT", ["SHAREDCODE1"], null),
            new("SW-Teams", ["sharedcode1"], null)
        ]);
        Require(result.Codes.Count == 1, "소스 간 코드 중복 제거 실패");
        Require(result.Sources["SHAREDCODE1"].SequenceEqual(["SW-Teams", "SWGT"]),
            "중복 코드 출처 목록 보존 실패");
    }

    private static void TestSourceFailureIsolation()
    {
        var sources = new[]
        {
            new CouponSource("Good", "good"),
            new CouponSource("Broken", "broken"),
            new CouponSource("GitHub Manual", "manual")
        };
        var service = new CouponSourceService(sources, (source, _) => source.Name switch
        {
            "Good" => Task.FromResult("<code>VALIDCODE123</code>"),
            "Broken" => throw new HttpRequestException("offline"),
            _ => Task.FromResult("{ invalid json")
        });

        var result = service.ScanAsync().GetAwaiter().GetResult();
        Require(result.Codes.SequenceEqual(["VALIDCODE123"]), "일부 소스 실패 시 정상 결과 유실");
        Require(result.Errors.Count == 2, "웹/원격 후보 실패가 독립 오류로 기록되지 않음");
    }

    private static void TestExplicitSourcePreservation()
    {
        var required = new[]
        {
            "4MINGYIDAOXIAN", "YYDSSWC26ZAN", "H4MBURGISWAITING", "HURRASWC2026",
            "SWC2026JUELEBA", "912XUXIECHUANQI", "12YJUSTHALFWAY", "ENTERTHESWCERA",
            "WASWIRDSWC2026", "SWC2026ROADTOWF", "IGYEORA4WF2026", "OQKR1STWFNUGU"
        };

        var swgtCodes = required.Take(10).ToArray();
        var swgtHtml = string.Join("", swgtCodes.Select(code => $"<code>{code}</code>"));
        var swgt = CouponSourceService.ExtractCodes("SWGT", swgtHtml);
        Require(swgt.Count == 10 && swgtCodes.All(swgt.Contains), "SWGT 명시적 코드 전부 보존 실패");

        var teamsHtml = "<table><tr><th>Coupon Code</th></tr>" +
            string.Join("", required.Select(code => $"<tr><td>{code}</td></tr>")) + "</table>";
        var teams = CouponSourceService.ExtractCodes("SW-Teams", teamsHtml);
        Require(required.All(teams.Contains), "SW-Teams 명시적 코드 전부 보존 실패");

        var swqHtml = "<table><tr><th>Code</th></tr>" +
            string.Join("", required.Select(code => $"<tr><td>{code}</td></tr>")) + "</table>";
        var swq = CouponSourceService.ExtractCodes("SWQ", swqHtml);
        Require(required.All(swq.Contains), "SWQ 명시적 코드 전부 보존 실패");

        var manualJson = JsonSerializer.Serialize(new { codes = required });
        var manual = CouponSourceService.ExtractRemoteCandidates(manualJson);
        Require(required.All(manual.Contains), "GitHub Manual 코드 전부 보존 실패");

        var unusual = CouponSourceService.ExtractCodes("SWGT",
            "<code>ABOUT</code><code>AAAAAAAA</code><code>12345</code><code>ABC123</code>");
        Require(new[] { "ABOUT", "AAAAAAAA", "12345", "ABC123" }.All(unusual.Contains),
            "명시적 코드에 과도한 모양 필터 적용");

        var currentSwgtShape = """
        <button class="btn-clipboard" data-clipboard-text="AUGSW2026V7N"></button>
        <a class="hasVisited gameCodeLink" data-gamecode="AUGSW2026V7N">AUGSW2026V7N</a>
        <a class="gameCodeLink hasVisited" data-gamecode="SWXFRIEREN2026">SWXFRIEREN2026</a>
        """;
        var currentSwgtCodes = CouponSourceService.ExtractCodes("SWGT", currentSwgtShape);
        Require(currentSwgtCodes.Contains("AUGSW2026V7N"), "SWGT Active data 속성 코드 누락");
        Require(currentSwgtCodes.Contains("SWXFRIEREN2026"), "SWGT gameCodeLink 코드 누락");
    }

    private static void TestHistoryControlsQueue()
    {
        var accountId = "account-a";
        var history = new Dictionary<string, Dictionary<string, CouponRecord>>
        {
            [accountId] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SUCCESS1"] = new() { Status = "success" },
                ["ALREADY1"] = new() { Status = "already" },
                ["EXPIRED1"] = new() { Status = "expired" },
                ["INVALID1"] = new() { Status = "invalid" },
                ["ERROR1"] = new() { Status = "error" },
                ["WORLD"] = new() { Status = "invalid" },
                ["DELETEDMANUAL1"] = new() { Status = "invalid" }
            }
        };

        foreach (var code in new[] { "SUCCESS1", "ALREADY1", "EXPIRED1", "INVALID1", "WORLD", "DELETEDMANUAL1" })
            Require(!MainForm.ShouldProcess(history, accountId, code), $"확정 결과 재시도 차단 실패: {code}");
        Require(MainForm.ShouldProcess(history, accountId, "ERROR1"), "error 다음 실행 재시도 실패");
        Require(MainForm.ShouldProcess(history, accountId, "SEENBUTUNTRIED"),
            "SeenCodes와 무관한 계정별 미처리 후보 실행 실패");
        Require(MainForm.ShouldProcess(history, "account-b", "INVALID1"), "다른 계정 독립 처리 실패");
    }

    private static void TestSwgtEmptyParserDetection()
    {
        var sources = new[]
        {
            new CouponSource("SWGT", "swgt"),
            new CouponSource("SWQ", "swq")
        };
        var service = new CouponSourceService(sources, (source, _) => Task.FromResult(
            source.Name == "SWGT" ? "<html><body>layout changed</body></html>" : "<code>BACKUPCODE1</code>"));
        var result = service.ScanAsync().GetAwaiter().GetResult();
        Require(result.Codes.SequenceEqual(["BACKUPCODE1"]), "SWGT 파서 실패 시 다른 소스 결과 유실");
        Require(result.Errors.Any(error => error == "SWGT: 쿠폰 코드를 찾지 못했습니다."),
            "SWGT 0개 파서 실패 감지 누락");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
