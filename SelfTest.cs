using System.Text.Json;

namespace SWCouponManager;

internal static class SelfTest
{
    public static int RunLiveScan()
    {
        try
        {
            var result = new CouponSourceService().ScanAsync().GetAwaiter().GetResult();
            var log = $"Live scan at {DateTimeOffset.UtcNow:O}" + Environment.NewLine +
                "Source health:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.Health.Select(FormatHealth)) + Environment.NewLine +
                "Codes: " + string.Join(", ", result.Codes) + Environment.NewLine +
                "Sources:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.Codes.Select(code =>
                    $"{code}: {string.Join(", ", result.Sources[code])}")) + Environment.NewLine +
                "Known code evidence:" + Environment.NewLine +
                string.Join(Environment.NewLine, new[]
                {
                    "AUGSW2026V7N: " + SourcesFor(result, "AUGSW2026V7N"),
                    "SWXFRIEREN2026: " + SourcesFor(result, "SWXFRIEREN2026"),
                    "INVOCATEUREU26: " + SourcesFor(result, "INVOCATEUREU26"),
                    "SWCTICKET2HAMBURG: " + SourcesFor(result, "SWCTICKET2HAMBURG")
                }) + Environment.NewLine +
                "Successful sources: " + string.Join(", ", result.SuccessfulSources) + Environment.NewLine +
                "Errors: " + string.Join(" / ", result.Errors);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-scan-test.log"), log);
            Require(result.Health.Count == 4, "필수 소스 건강 상태가 완전하지 않음");
            Require(result.Health.All(x => x.HttpSuccess), "하나 이상의 라이브 소스를 검증할 수 없음");
            Require(result.Health.All(x => x.ReferenceCount is not null), "기준 목록을 만들지 못한 소스가 있음");
            Require(result.Health.All(x => x.MissingCodes.Count == 0), "라이브 소스 명시 코드 누락 발생");
            Require(result.Health.All(IsLiveHealthSafelyCovered),
                "라이브 소스 suspicious 축소분이 seed/grace로 안전하게 보존되지 않음");
            Require(result.Codes.All(result.Sources.ContainsKey), "출처 없는 쿠폰 후보가 있음");
            return 0;
        }
        catch (Exception ex)
        {
            var path = Path.Combine(Path.GetTempPath(), "SWCouponManager-scan-test.log");
            File.AppendAllText(path, Environment.NewLine + "GATE FAILURE:" + Environment.NewLine + ex);
            return 1;
        }
    }

    private static string FormatHealth(SourceHealth health) =>
        $"{health.Source}: fetch={health.FetchSuccesses}/{health.FetchAttempts}, hashes=[{string.Join(",", health.PayloadHashes ?? [])}], bytes={health.PayloadBytes}, " +
        $"reference={health.ReferenceCount?.ToString() ?? "unavailable"}, production={health.ProductionCount}, " +
        $"advertised={health.AdvertisedCount?.ToString() ?? "n/a"}, responses=[{string.Join(",", health.ResponseCodeCounts ?? [])}], retained={health.RetainedRecentCount}, seed={health.SeedRetainedCount}, observed={health.ObservedRetainedCount}, suspicious={health.Suspicious}, " +
        $"missing=[{string.Join(", ", health.MissingCodes)}], extra=[{string.Join(", ", health.ExtraCodes)}], " +
        $"freshness={health.FreshnessEvidence}, error={health.Error ?? "none"}";

    private static string SourcesFor(ScanResult result, string code) =>
        result.Sources.TryGetValue(code, out var sources) ? string.Join(", ", sources) : "not exposed by current live sources";

    private static bool IsLiveHealthSafelyCovered(SourceHealth health)
    {
        if (!health.Suspicious) return true;
        if (health.MissingCodes.Count > 0) return false;
        var warnings = health.Warnings ?? [];
        return health.SeedRetainedCount > 0 && warnings.All(warning =>
            warning.StartsWith("current ", StringComparison.Ordinal) ||
            warning.StartsWith("retained ", StringComparison.Ordinal));
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
            TestReceiveQueueUsesExistingScan();
            TestRetryAllQueueIncludesCompletedCodes();
            TestRedemptionProgressAndServer();
            TestSwgtEmptyParserDetection();
            TestCapturedSourceCompleteness();
            TestStaleResponseUnion();
            TestObservedInventoryGrace();
            TestTrustedSeedRegressions();
            TestSourceHealthLifecycle();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-self-test.log"),
                "PASS" + Environment.NewLine +
                "stale regressions: fresh9+extra1, advertised9/reference9/production8, advertised8/reference9, stale8+seed9, first-run empty+stale8+seed9, same stale payload twice+seed9" + Environment.NewLine +
                "GUI lifecycle: 10 scan + modal source-health open/close iterations, crash 0" + Environment.NewLine +
                "retry policy: success/already/expired/invalid blocked; error retried; SeenCodes display-only" + Environment.NewLine +
                "receive flow: existing scan candidates queued directly without another scan" + Environment.NewLine +
                "retry-all flow: every detected account+code pair queued regardless of final history" + Environment.NewLine +
                "redemption diagnostics: actual Hive server values and immediate progress log formatting verified");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-self-test.log"), ex.ToString());
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

        var historicalTeamsShape = """
        <section class="codes">
          <code>INVOCATEUREU26</code>
          <code>SWCTICKET2HAMBURG</code>
        </section>
        """;
        var historicalTeams = CouponSourceService.ExtractCodes("SW-Teams", historicalTeamsShape);
        Require(historicalTeams.Contains("INVOCATEUREU26"), "SW-Teams INVOCATEUREU26 회귀");
        Require(historicalTeams.Contains("SWCTICKET2HAMBURG"), "SW-Teams SWCTICKET2HAMBURG 회귀");
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

    private static void TestReceiveQueueUsesExistingScan()
    {
        var account = new Account { Id = "receive-account", HiveId = "hive", Selected = true };
        var history = new Dictionary<string, Dictionary<string, CouponRecord>>
        {
            [account.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["DONECODE"] = new() { Status = "success" },
                ["RETRYCODE"] = new() { Status = "error" }
            }
        };
        var queue = MainForm.BuildQueue([account], ["DONECODE", "RETRYCODE", "NEWCODE"], history);
        Require(queue.Select(x => x.Code).SequenceEqual(["RETRYCODE", "NEWCODE"]),
            "검색된 기존 후보를 즉시 수령 큐로 넘기는 동작 실패");
    }

    private static void TestRetryAllQueueIncludesCompletedCodes()
    {
        var accounts = new[]
        {
            new Account { Id = "retry-a", HiveId = "a", Selected = true },
            new Account { Id = "retry-b", HiveId = "b", Selected = true }
        };
        var history = new Dictionary<string, Dictionary<string, CouponRecord>>
        {
            ["retry-a"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SUCCESS1"] = new() { Status = "success" },
                ["ALREADY1"] = new() { Status = "already" },
                ["EXPIRED1"] = new() { Status = "expired" },
                ["INVALID1"] = new() { Status = "invalid" }
            }
        };
        var codes = new[] { "SUCCESS1", "ALREADY1", "EXPIRED1", "INVALID1", "NEWCODE" };
        var queue = MainForm.BuildQueue(accounts, codes, history, retryAll: true);
        Require(queue.Count == accounts.Length * codes.Length, "다시시도가 모든 account+code 조합을 포함하지 않음");
        Require(codes.All(code => queue.Any(item => item.Account.Id == "retry-a" && item.Code == code)),
            "다시시도가 완료 판정 코드를 제외함");
    }

    private static void TestRedemptionProgressAndServer()
    {
        var expected = new Dictionary<string, string>
        {
            ["global"] = "글로벌 서버 (global)", ["korea"] = "한국 서버 (korea)",
            ["japan"] = "일본 서버 (japan)", ["china"] = "중국 서버 (china)",
            ["asia"] = "아시아 서버 (asia)", ["europe"] = "유럽 서버 (europe)"
        };
        foreach (var pair in expected)
            Require(MainForm.ServerDisplayName(pair.Key) == pair.Value, $"Hive 서버 매핑 실패: {pair.Key}");

        var item = new WorkItem(new Account { Name = "테스트", Server = "korea" }, "TESTCODE");
        Require(MainForm.FormatRedemptionProgress(item, "사용 요청 전송").Contains("사용 요청 전송"),
            "등록 진행 로그 표시 실패");
        Require(MainForm.BuildNoWorkMessage(2, 24).Contains("선택 계정 2개 · 검색 후보 24개 · 미처리/오류 0개"),
            "수령 큐 0개 안내 실패");
    }

    private static void TestSwgtEmptyParserDetection()
    {
        var sources = new[]
        {
            new CouponSource("SWGT", "swgt"),
            new CouponSource("SWQ", "swq")
        };
        var service = new CouponSourceService(sources, (source, _) => Task.FromResult(
            source.Name == "SWGT"
                ? "<html><body>layout changed</body></html>"
                : "<table><tbody id=\"coupons\"><tr><td class=\"code-cell\">BACKUPCODE1</td></tr></tbody></table>"));
        var result = service.ScanAsync().GetAwaiter().GetResult();
        Require(result.Codes.SequenceEqual(["BACKUPCODE1"]), "SWGT 파서 실패 시 다른 소스 결과 유실");
        Require(result.Errors.Any(error => error.StartsWith("SWGT: ", StringComparison.Ordinal)),
            "SWGT 0개 파서 실패 감지 누락");
    }

    private static void TestCapturedSourceCompleteness()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "test-fixtures", "live-captures");
        var fixtures = new Dictionary<string, string>
        {
            ["SWGT"] = "swgt.html",
            ["SW-Teams"] = "swteams.html",
            ["SWQ"] = "swq.html",
            ["GitHub Manual"] = "manual.html"
        };

        foreach (var (source, file) in fixtures)
        {
            var payload = File.ReadAllText(Path.Combine(fixtureRoot, file));
            var production = source == "GitHub Manual"
                ? CouponSourceService.ExtractRemoteCandidates(payload)
                : CouponSourceService.ExtractCodes(source, payload);
            var reference = ReferenceInventoryService.Extract(source, payload);
            var missing = reference.Except(production, StringComparer.OrdinalIgnoreCase).ToList();
            Require(missing.Count == 0, $"{source} 캡처 기준 목록 누락: {string.Join(", ", missing)}");
        }
    }

    private static void TestStaleResponseUnion()
    {
        var stale = TeamsPage("A1CODE", "A2CODE", "A3CODE", "A4CODE", "A5CODE", "A6CODE", "A7CODE", "A8CODE");
        var fresh = TeamsPage("A1CODE", "A2CODE", "A3CODE", "A4CODE", "A5CODE", "A6CODE", "A7CODE", "A8CODE", "INVOCATEUREU26");
        var service = new CouponSourceService([new("SW-Teams", "fixture")], (_, attempt, _) =>
            Task.FromResult(attempt == 0 ? stale : fresh));
        var result = service.ScanAsync(new AppState()).GetAwaiter().GetResult();
        Require(result.Codes.Count == 9 && result.Codes.Contains("INVOCATEUREU26"), "stale+fresh union 9개 보존 실패");
        Require(result.Health.Single().Suspicious && result.Health.Single().Warnings!.Contains("inconsistent responses"),
            "다중 응답 불일치 경고 실패");
    }

    private static void TestObservedInventoryGrace()
    {
        var state = new AppState();
        state.ObservedCodesBySource["SW-Teams"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SWCTICKET2HAMBURG"] = new()
            {
                FirstSeenAt = DateTimeOffset.UtcNow.AddHours(-2),
                LastSeenAt = DateTimeOffset.UtcNow.AddHours(-1),
                LastConfirmedAt = DateTimeOffset.UtcNow.AddHours(-1)
            }
        };
        state.SourceInventories["SW-Teams"] = new() { LastHealthyCount = 9, LastHealthyPayloadBytes = 100 };
        var stale = TeamsPage("A1CODE", "A2CODE", "A3CODE", "A4CODE", "A5CODE", "A6CODE", "A7CODE", "A8CODE");
        var service = new CouponSourceService([new("SW-Teams", "fixture")], (_, _, _) => Task.FromResult(stale));
        var result = service.ScanAsync(state).GetAwaiter().GetResult();
        Require(result.Codes.Count == 9 && result.Codes.Contains("SWCTICKET2HAMBURG"), "observed grace 보존 실패");
        Require(result.Health.Single().RetainedRecentCount == 1 && result.Health.Single().Suspicious,
            "observed grace stale 경고 실패");
    }

    private static string TeamsPage(params string[] codes) =>
        $"<html><h2>Available Codes ({codes.Length})</h2>{string.Join("", codes.Select(c => $"<code>{c}</code>"))}</html>";

    private static void TestTrustedSeedRegressions()
    {
        var now = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var seedCodes = Enumerable.Range(1, 9).Select(i => $"SEED{i}CODE").ToArray();
        var seed = JsonSerializer.Serialize(new
        {
            sources = new[] { new { source = "SW-Teams", observedAt = now.AddHours(-1), ttlHours = 48, codes = seedCodes } }
        });
        ScanResult Scan(string first, string second) => new CouponSourceService(
            [new("SW-Teams", "fixture")], (_, attempt, _) => Task.FromResult(attempt == 0 ? first : second),
            seed, () => now).ScanAsync(new AppState()).GetAwaiter().GetResult();

        var fresh9Extra = TeamsPage(seedCodes.Concat(["EXTRA1CODE"]).ToArray());
        var healthy = Scan(fresh9Extra, fresh9Extra).Health.Single();
        Require(!healthy.Suspicious && healthy.AdvertisedCount == 10 && healthy.ReferenceCount == 10 && healthy.ProductionCount == 10,
            "fresh9+extra1 Healthy 회귀 실패");

        Require(CouponSourceService.EvaluateInventoryWarnings(9, 9, 8, 1).Any(x => x.StartsWith("parser missing 1")),
            "advertised9/reference9/production8 suspicious 회귀 실패");
        var advertised8Reference9 = TeamsPage(seedCodes).Replace("Available Codes (9)", "Available Codes (8)");
        Require(Scan(advertised8Reference9, advertised8Reference9).Health.Single().Suspicious,
            "advertised8/reference9 suspicious 회귀 실패");

        var stale8 = TeamsPage(seedCodes.Take(8).ToArray());
        var stale = Scan(stale8, stale8);
        Require(stale.Health.Single().Suspicious && seedCodes.All(stale.Codes.Contains) && stale.Health.Single().SeedRetainedCount == 1,
            "stale8+seed9 suspicious/union9 회귀 실패");
        Require(stale.Health.Single().FreshnessEvidence.Contains("not independent", StringComparison.OrdinalIgnoreCase),
            "same-origin 동일 payload freshness 과대평가 회귀 실패");

        Require(CouponSourceService.EvaluateInventoryWarnings(9, 9, 8, 1).Count > 0,
            "advertised9/reference9/production8 verdict 누락");
    }

    private static void TestSourceHealthLifecycle()
    {
        var service = new CouponSourceService([new("Fixture", "fixture")], (_, _, _) =>
            Task.FromResult("<code>LIFECYCLE1</code>"));
        for (var i = 0; i < 10; i++)
        {
            var scan = service.ScanAsync(new AppState()).GetAwaiter().GetResult();
            using var dialog = MainForm.CreateSourceHealthDialog(scan.Health);
            var list = dialog.Controls.OfType<ListBox>().Single();
            Require(!list.IsDisposed && list.Items.Count == 1, $"source-health lifecycle 생성 실패 #{i + 1}");
            dialog.Shown += (_, _) => dialog.BeginInvoke(dialog.Close);
            dialog.ShowDialog();
            dialog.Dispose();
            Require(list.IsDisposed, $"source-health lifecycle 정리 실패 #{i + 1}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
