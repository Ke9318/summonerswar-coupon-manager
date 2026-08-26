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
            TestUnattendedQueueNeverRetriesTerminalHistory();
            TestAttemptJournalLifecycle();
            TestRedemptionAttemptPersistence();
            TestBackgroundAgentScheduler();
            TestBackgroundAgentLifecycle();
            TestTrayOwnerLifecycle();
            TestLoginStartRegistration();
            TestTrustedCandidateManifest();
            TestTrustedAutomaticPlanner();
            TestTrustedAutomaticCycle();
            TestTrustedManifestInbox();
            TestCloudWatcherPublication();
            TestDisposableUpdateTransaction();
            TestDisposableUnattendedEndToEnd();
            TestAnomalyEvidenceAndRepairGate();
            TestRedemptionProgressAndServer();
            TestSwgtEmptyParserDetection();
            TestCapturedSourceCompleteness();
            TestStaleResponseUnion();
            TestObservedInventoryGrace();
            TestTrustedSeedRegressions();
            TestSourceHealthLifecycle();
            TestUpdateChecksum();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SWCouponManager-self-test.log"),
                "PASS" + Environment.NewLine +
                "stale regressions: fresh9+extra1, advertised9/reference9/production8, advertised8/reference9, stale8+seed9, first-run empty+stale8+seed9, same stale payload twice+seed9" + Environment.NewLine +
                "GUI lifecycle: 10 scan + modal source-health open/close iterations, crash 0" + Environment.NewLine +
                "retry policy: success/already/expired/invalid blocked; error retried; SeenCodes display-only" + Environment.NewLine +
                "receive flow: existing scan candidates queued directly without another scan" + Environment.NewLine +
                "retry-all flow: every detected account+code pair queued regardless of final history" + Environment.NewLine +
                "unattended foundation: terminal suppression, attempt lifecycle, ambiguity quarantine, manifest integrity/privacy" + Environment.NewLine +
                "background-agent foundation: trusted publish/inbox/restart end-to-end, automatic planning/cycle isolation, single-instance lease, bounded catch-up, non-overlap, pause/resume, cancellation, failure backoff, tray create/dispose and close policy, login-start specification" + Environment.NewLine +
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

    private static void TestUpdateChecksum()
    {
        var path = Path.Combine(Path.GetTempPath(), "SWCouponManager-checksum-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "synthetic update payload");
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            GitHubUpdateService.VerifySha256(path, hash + "  SWCouponManager-win-x64.zip");

            var rejected = false;
            try { GitHubUpdateService.VerifySha256(path, new string('0', 64)); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "불일치 업데이트 체크섬을 거부하지 않음");
        }
        finally
        {
            try { File.Delete(path); } catch { }
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
        Require(MainForm.Classify("예상하지 못한 새 응답 문구") == "ambiguous", "미분류 Hive 응답 격리 실패");
    }

    private static void TestRetryPolicy()
    {
        foreach (var status in new[] { "success", "already", "expired", "invalid" })
            Require(MainForm.IsCompletedStatus(status), $"완료 상태 재시도 차단 실패: {status}");

        Require(!MainForm.IsCompletedStatus("error"), "오류 상태가 재시도 불가로 저장됨");
        Require(MainForm.IsAutomaticBlockedStatus("ambiguous"), "모호한 결과 자동 재시도 차단 실패");
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

    private static void TestUnattendedQueueNeverRetriesTerminalHistory()
    {
        var account = new Account { Id = "auto-a", HiveId = "synthetic", Selected = true };
        var history = new Dictionary<string, Dictionary<string, CouponRecord>>
        {
            [account.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["DONE1"] = new() { Status = "success" },
                ["DONE2"] = new() { Status = "already" }
            }
        };
        var automatic = MainForm.BuildQueue([account], ["DONE1", "DONE2", "NEW1"], history);
        Require(automatic.Count == 1 && automatic[0].Code == "NEW1",
            "자동 큐가 수동 retryAll처럼 terminal history를 포함함");
    }

    private static void TestAttemptJournalLifecycle()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var state = new AppState();
        var history = new Dictionary<string, Dictionary<string, CouponRecord>>();
        var journal = new AttemptJournal(state, () => now);

        Require(journal.CanQueue("a", " code 1 ", history), "신규 attempt queue 차단");
        var queued = journal.Queue("a", " code 1 ", "corr-1");
        Require(queued.CorrelationId == "corr-1" && state.Attempts["a"].ContainsKey("CODE1"),
            "attempt identity/정규화 저장 실패");
        Require(!journal.CanQueue("a", "CODE1", history), "queued attempt 중복 허용");
        journal.MarkExecuting("a", "CODE1");
        journal.MarkVerifying("a", "CODE1");
        journal.MarkAmbiguous("a", "CODE1");
        Require(!journal.CanQueue("a", "CODE1", history), "ambiguous attempt 자동 재전송 허용");

        journal.Queue("b", "TEMP1");
        journal.MarkExecuting("b", "TEMP1");
        journal.MarkTemporaryFailure("b", "TEMP1", TimeSpan.FromMinutes(5));
        Require(!journal.CanQueue("b", "TEMP1", history), "backoff 이전 temporary retry 허용");
        now = now.AddMinutes(5);
        Require(journal.CanQueue("b", "TEMP1", history), "backoff 이후 temporary retry 차단");
        for (var failure = 2; failure <= 3; failure++)
        {
            journal.Queue("b", "TEMP1");
            journal.MarkExecuting("b", "TEMP1");
            journal.MarkTemporaryFailure("b", "TEMP1", TimeSpan.FromMinutes(5));
            now = now.AddMinutes(5 * (1 << (failure - 1)));
        }
        Require(state.Attempts["b"]["TEMP1"].TemporaryFailures == 3 &&
                !journal.CanQueue("b", "TEMP1", history),
            "temporary retry 상한이 재큐잉 사이에 보존되지 않음");

        Require(journal.CanQueue("other-account", "CODE1", history),
            "한 계정의 attempt가 다른 계정의 동일 쿠폰을 차단함");

        journal.Queue("c", "DONE1");
        journal.MarkExecuting("c", "DONE1");
        journal.MarkTerminal("c", "DONE1", "success");
        Require(!journal.CanQueue("c", "DONE1", history), "terminal attempt 재실행 허용");

        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerAttemptTest", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new AppStorage(root);
            storage.Save(state);
            var restored = storage.Load();
            Require(restored.Attempts["a"]["CODE1"].Status == AttemptStatus.Ambiguous,
                "restart 후 ambiguous attempt 복원 실패");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestTrustedCandidateManifest()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var evidence = new string('a', 64);
        var json = $$"""
        {
          "schemaVersion": 1,
          "generatedAt": "{{now:O}}",
          "expiresAt": "{{now.AddHours(6):O}}",
          "minimumClientVersion": "1.5.0",
          "candidates": [{
            "code": " code 1 ",
            "sources": ["SWGT", "Official", "SWGT"],
            "firstObservedAt": "{{now.AddMinutes(-5):O}}",
            "lastObservedAt": "{{now:O}}",
            "evidenceHashes": ["{{evidence}}"],
            "policyId": "recall-explicit-v1"
          }]
        }
        """;
        var hash = TrustedCandidateManifestService.ComputeSha256(json);
        var manifest = TrustedCandidateManifestService.ParseAndValidate(json, hash, new Version(1, 5, 0), now);
        Require(manifest.Candidates.Single().Code == "CODE1", "manifest coupon 정규화 실패");
        Require(manifest.Candidates.Single().Sources.SequenceEqual(["Official", "SWGT"]),
            "manifest source attribution 정규화 실패");

        var rejectedHash = false;
        try { TrustedCandidateManifestService.ParseAndValidate(json, new string('0', 64), new Version(1, 4, 6), now); }
        catch (InvalidDataException) { rejectedHash = true; }
        Require(rejectedHash, "손상 manifest checksum 허용");

        var privateJson = json.Replace("\"candidates\":", "\"accountId\":\"forbidden\",\"candidates\":");
        var rejectedPrivate = false;
        try { TrustedCandidateManifestService.ParseAndValidate(privateJson,
            TrustedCandidateManifestService.ComputeSha256(privateJson), new Version(1, 5, 0), now); }
        catch (InvalidDataException) { rejectedPrivate = true; }
        Require(rejectedPrivate, "cloud manifest account field 허용");
    }

    private static void TestBackgroundAgentScheduler()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var runs = 0;
        var active = 0;
        var maxActive = 0;
        BackgroundAgentScheduler? scheduler = null;
        async Task FakeDelay(TimeSpan delay, CancellationToken ct)
        {
            now += delay;
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        scheduler = new BackgroundAgentScheduler(async ct =>
        {
            active++;
            maxActive = Math.Max(maxActive, active);
            runs++;
            await Task.Yield();
            active--;
            if (runs == 2) scheduler!.Pause();
        }, now.AddHours(-24), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1), () => now, FakeDelay);

        var initialLastCompleted = scheduler.LastCompletedAt;
        Require(scheduler.Start(), "background scheduler 첫 start 실패");
        Require(!scheduler.Start(), "background scheduler 중복 start 허용");
        SpinWait.SpinUntil(() => scheduler.IsPaused && scheduler.LastCompletedAt > initialLastCompleted,
            TimeSpan.FromSeconds(2));
        Require(runs == 2 && maxActive == 1, "catch-up이 burst 실행되거나 작업이 겹침");
        Require(scheduler.LastCompletedAt > initialLastCompleted, "scheduler 완료 시각이 저장되지 않음");
        scheduler.Resume();
        SpinWait.SpinUntil(() => runs >= 3, TimeSpan.FromSeconds(2));
        scheduler.StopAsync().GetAwaiter().GetResult();
        Require(!scheduler.IsRunning, "scheduler cancellation 후 loop가 남음");
        scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var failures = 0;
        var delays = new List<TimeSpan>();
        BackgroundAgentScheduler? failing = null;
        failing = new BackgroundAgentScheduler(ct =>
        {
            failures++;
            if (failures == 3) failing!.Pause();
            throw new InvalidOperationException("synthetic scheduler failure");
        }, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1), () => now,
        async (delay, ct) =>
        {
            delays.Add(delay);
            now += delay;
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        });
        failing.Start();
        SpinWait.SpinUntil(() => failing.IsPaused, TimeSpan.FromSeconds(2));
        failing.StopAsync().GetAwaiter().GetResult();
        Require(delays.Take(3).SequenceEqual(new[]
            { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4) }),
            "scheduler failure backoff가 bounded exponential 순서를 따르지 않음");
        failing.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void TestTrustedAutomaticPlanner()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
        var eligible = new Account { Id = "eligible", HiveId = "synthetic", Server = "global", Selected = true };
        var duplicate1 = new Account { Id = "duplicate", HiveId = "synthetic-1", Server = "korea", Selected = true };
        var duplicate2 = new Account { Id = "duplicate", HiveId = "synthetic-2", Server = "europe", Selected = true };
        var state = new AppState
        {
            Accounts =
            [
                eligible,
                new Account { Id = "not-selected", HiveId = "synthetic", Server = "korea", Selected = false },
                new Account { Id = "bad-server", HiveId = "synthetic", Server = "unknown", Selected = true },
                duplicate1,
                duplicate2
            ]
        };
        state.History[eligible.Id] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DONE1"] = new() { Status = "success" }
        };
        var journal = new AttemptJournal(state, () => now);
        journal.Queue(eligible.Id, "AMBIG1");
        journal.MarkExecuting(eligible.Id, "AMBIG1");
        journal.MarkAmbiguous(eligible.Id, "AMBIG1");
        journal.Queue(eligible.Id, "BACKOFF1");
        journal.MarkExecuting(eligible.Id, "BACKOFF1");
        journal.MarkTemporaryFailure(eligible.Id, "BACKOFF1", TimeSpan.FromMinutes(5));

        TrustedCandidate Candidate(string code) => new()
        {
            Code = code,
            Sources = ["Synthetic Source"],
            EvidenceHashes = [new string('a', 64)],
            FirstObservedAt = now.AddMinutes(-1),
            LastObservedAt = now
        };
        var manifest = new TrustedCandidateManifest
        {
            GeneratedAt = now,
            ExpiresAt = now.AddHours(1),
            Candidates = [Candidate("NEW1"), Candidate("DONE1"), Candidate("AMBIG1"), Candidate("BACKOFF1")]
        };

        var plan = new TrustedAutomaticPlanner(state, () => now).Build(manifest);
        Require(plan.Items.Count == 1 && plan.Items[0].Account.Id == eligible.Id &&
                plan.Items[0].Candidate.Code == "NEW1",
            "trusted automatic planner가 terminal/ambiguous/backoff 또는 부적격 계정을 포함함");
        Require(plan.Items[0].Candidate.Sources.SequenceEqual(["Synthetic Source"]),
            "automatic work item에서 manifest attribution 손실");
        Require(plan.SkippedAccounts == 4 && plan.SkippedCandidates == 3,
            "automatic planner의 비식별 skip 집계 실패");

        var rejected = false;
        try
        {
            new TrustedAutomaticPlanner(new AppState()).Build(new TrustedCandidateManifest
            {
                Candidates = [Candidate(" not-normalized ")]
            });
        }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "planner가 미검증 candidate를 허용함");
    }

    private static void TestTrustedAutomaticCycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerAutomaticCycleTest", Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
            var account = new Account { Id = "automatic", HiveId = "synthetic", Server = "asia", Selected = true };
            var state = new AppState { Accounts = [account] };
            var storage = new AppStorage(root);
            TrustedCandidate Candidate(string code) => new()
            {
                Code = code,
                Sources = ["Synthetic Source"],
                EvidenceHashes = [new string('b', 64)],
                FirstObservedAt = now.AddMinutes(-1),
                LastObservedAt = now
            };
            var manifest = new TrustedCandidateManifest
            {
                GeneratedAt = now,
                ExpiresAt = now.AddHours(1),
                Candidates = [Candidate("SUCCESS1"), Candidate("FAIL1"), Candidate("SUCCESS2")]
            };
            var cycle = new TrustedAutomaticCycle(state, storage, () => now);
            var result = cycle.ExecuteAsync(manifest, (item, submitted, correlationId, _) =>
            {
                Require(correlationId.Length == 32, "automatic adapter에 correlation ID가 전달되지 않음");
                if (item.Candidate.Code == "FAIL1")
                    throw new InvalidOperationException("synthetic isolated failure");
                submitted();
                return Task.FromResult(("success", "synthetic success"));
            }, CancellationToken.None).GetAwaiter().GetResult();

            Require(result == new AutomaticCycleResult(3, 2, 1),
                "automatic cycle 결과/실패 격리 집계 실패");
            var restored = storage.Load();
            Require(restored.History[account.Id].Keys.Order().SequenceEqual(["SUCCESS1", "SUCCESS2"]),
                "automatic cycle terminal history 저장 또는 실패 격리 실패");
            Require(restored.Attempts[account.Id]["FAIL1"].Status == AttemptStatus.TemporaryFailure,
                "automatic cycle 제출 전 실패가 retry 상태로 저장되지 않음");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancellationObserved = false;
            try
            {
                cycle.ExecuteAsync(new TrustedCandidateManifest { Candidates = [Candidate("CANCEL1")] },
                    (_, _, _, _) => Task.FromResult(("success", "unexpected")), cancelled.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancellationObserved = true; }
            Require(cancellationObserved && !state.Attempts[account.Id].ContainsKey("CANCEL1"),
                "automatic cycle cancellation 전에 attempt가 생성됨");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestTrustedManifestInbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerManifestInboxTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
            var manifestPath = Path.Combine(root, "candidate-manifest.json");
            var checksumPath = manifestPath + ".sha256";
            Require(TrustedManifestInbox.TryRead(manifestPath, checksumPath, new Version(1, 4, 6), now) is null,
                "manifest inbox가 미게시 상태를 오류/작업으로 처리함");
            var json = $$"""
            {
              "schemaVersion": 1,
              "generatedAt": "{{now:O}}",
              "expiresAt": "{{now.AddHours(1):O}}",
              "minimumClientVersion": "1.5.0",
              "candidates": [{
                "code": "INBOX1",
                "sources": ["Synthetic Source"],
                "firstObservedAt": "{{now.AddMinutes(-1):O}}",
                "lastObservedAt": "{{now:O}}",
                "evidenceHashes": ["{{new string('c', 64)}}"],
                "policyId": "recall-explicit-v1"
              }]
            }
            """;
            File.WriteAllText(manifestPath, json);
            File.WriteAllText(checksumPath, TrustedCandidateManifestService.ComputeSha256(json));
            var manifest = TrustedManifestInbox.TryRead(manifestPath, checksumPath, new Version(1, 5, 0), now);
            Require(manifest?.Candidates.Single().Code == "INBOX1", "manifest inbox 검증/읽기 실패");

            var checksumReads = 0;
            var changedDuringRead = false;
            try
            {
                TrustedManifestInbox.TryRead(manifestPath, checksumPath, new Version(1, 5, 0), now,
                    _ => true,
                    path => path == checksumPath && ++checksumReads == 2
                        ? new string('0', 64)
                        : path == checksumPath
                            ? TrustedCandidateManifestService.ComputeSha256(json)
                            : json);
            }
            catch (IOException) { changedDuringRead = true; }
            Require(changedDuringRead, "manifest 게시 중 checksum 변경을 허용함");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestCloudWatcherPublication()
    {
        var now = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var hashIndex = 0;
        SourceHealth Health(string source) => new(
            source, true, 1000, 1, 1, [], [], null, 1, 1,
            [new string("abcdef"[hashIndex++ % 6], 64)], [1], 1, 0, false, []);
        var health = new[] { Health("SWGT"), Health("SW-Teams"), Health("SWQ"), Health("GitHub Manual") }.ToList();
        health[0] = health[0] with { ExtraCodes = ["NOISE"] };
        var scan = new ScanResult(
            ["CODE2", "NOISE", "CODE1"],
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["CODE1"] = ["SWGT", "GitHub Manual"],
                ["CODE2"] = ["SWQ"],
                ["NOISE"] = ["SWGT"]
            },
            ["SWGT", "SW-Teams", "SWQ", "GitHub Manual"], [], health);
        var manifest = CloudWatcher.BuildManifest(scan, now);
        Require(manifest.Candidates.Select(x => x.Code).SequenceEqual(["CODE1", "CODE2"]),
            "cloud watcher candidate 순서 또는 production-only extra 제거 실패");
        Require(manifest.Candidates[0].Sources.SequenceEqual(["GitHub Manual", "SWGT"]) &&
                manifest.Candidates[0].EvidenceHashes.All(x => x.Length == 64),
            "cloud watcher attribution/evidence 연결 실패");
        Require(CloudWatcher.Serialize(manifest) == CloudWatcher.Serialize(manifest),
            "cloud watcher serialization이 결정적이지 않음");

        var suspiciousHealth = health.ToList();
        suspiciousHealth[0] = suspiciousHealth[0] with { Suspicious = true, Warnings = ["synthetic"] };
        var rejected = false;
        try { CloudWatcher.BuildManifest(scan with { Health = suspiciousHealth }, now); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "cloud watcher가 suspicious 필수 소스를 게시함");

        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerCloudWatcherTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CloudWatcher.PublishAtomically(manifest, root);
            var manifestPath = Path.Combine(root, "candidate-manifest.json");
            var checksumPath = manifestPath + ".sha256";
            var parsed = TrustedManifestInbox.TryRead(manifestPath, checksumPath, new Version(1, 5, 0), now);
            Require(parsed?.Candidates.Count == 2, "watcher 출력과 Windows inbox 계약 불일치");
            var originalJson = File.ReadAllText(manifestPath);
            var originalChecksum = File.ReadAllText(checksumPath);
            var moves = 0;
            var replacement = CloudWatcher.BuildManifest(scan, now.AddMinutes(1));
            var rollbackObserved = false;
            try
            {
                CloudWatcher.PublishAtomically(replacement, root, (source, destination, overwrite) =>
                {
                    moves++;
                    if (moves == 2) throw new IOException("synthetic checksum move failure");
                    File.Move(source, destination, overwrite);
                });
            }
            catch (IOException) { rollbackObserved = true; }
            Require(rollbackObserved && File.ReadAllText(manifestPath) == originalJson &&
                    File.ReadAllText(checksumPath) == originalChecksum,
                "watcher publication 실패 시 이전 manifest 쌍 복구 실패");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestDisposableUpdateTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerDisposableUpdateTest", Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install");
        var state = Path.Combine(root, "external-state.json");
        Directory.CreateDirectory(install);
        try
        {
            File.WriteAllText(Path.Combine(install, "unrelated.txt"), "preserve unrelated");
            File.WriteAllText(state, "synthetic external state");
            var unrelatedBefore = File.ReadAllText(Path.Combine(install, "unrelated.txt"));
            var stateBefore = File.ReadAllText(state);
            DisposableUpdateTransaction.Apply(AppContext.BaseDirectory, install,
                path => UpdateHealthCheck.ValidateInstallLayout(path, new Version(1, 5, 0)));
            Require(File.ReadAllText(Path.Combine(install, "unrelated.txt")) == unrelatedBefore &&
                    File.ReadAllText(state) == stateBefore,
                "성공 update가 unrelated/install 외부 state를 변경함");

            var before = Directory.GetFiles(install, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(install, path), File.ReadAllBytes,
                    StringComparer.OrdinalIgnoreCase);
            var failed = false;
            try
            {
                DisposableUpdateTransaction.Apply(AppContext.BaseDirectory, install,
                    _ => throw new InvalidOperationException("health should not run"),
                    injectFailureAfterCopies: 3);
            }
            catch (IOException) { failed = true; }
            var after = Directory.GetFiles(install, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(install, path), File.ReadAllBytes,
                    StringComparer.OrdinalIgnoreCase);
            Require(failed && before.Count == after.Count && before.All(pair =>
                    after.TryGetValue(pair.Key, out var bytes) && pair.Value.SequenceEqual(bytes)),
                "post-copy 실패 rollback이 설치 파일을 byte-for-byte 복원하지 못함");
            Require(File.ReadAllText(state) == stateBefore,
                "rollback이 synthetic external state를 변경함");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestDisposableUnattendedEndToEnd()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerUnattendedE2E", Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 8, 27, 1, 0, 0, TimeSpan.Zero);
            var hashes = new[] { '1', '2', '3', '4' };
            var names = new[] { "SWGT", "SW-Teams", "SWQ", "GitHub Manual" };
            var health = names.Select((name, i) => new SourceHealth(
                name, true, 100, 1, 1, [], [], null, 1, 1,
                [new string(hashes[i], 64)], [1], 1, 0, false, [])).ToList();
            var scan = new ScanResult(["E2ECODE1"],
                new(StringComparer.OrdinalIgnoreCase) { ["E2ECODE1"] = ["SWGT", "SW-Teams"] },
                names.ToList(), [], health);
            CloudWatcher.PublishAtomically(CloudWatcher.BuildManifest(scan, now), root);
            var manifestPath = Path.Combine(root, "candidate-manifest.json");
            var manifest = TrustedManifestInbox.TryRead(
                manifestPath, manifestPath + ".sha256", new Version(1, 5, 0), now)
                ?? throw new InvalidOperationException("disposable manifest missing");

            var storage = new AppStorage(Path.Combine(root, "agent-state"));
            var state = new AppState
            {
                Accounts = [new Account { Id = "e2e-account", HiveId = "synthetic", Server = "global", Selected = true }]
            };
            storage.Save(state);
            var first = new TrustedAutomaticCycle(state, storage, () => now)
                .ExecuteAsync(manifest, (_, submitted, _, _) =>
                {
                    submitted();
                    return Task.FromResult(("success", "synthetic e2e success"));
                }, CancellationToken.None).GetAwaiter().GetResult();
            Require(first == new AutomaticCycleResult(1, 1, 0), "disposable first catch-up 실행 실패");

            var restarted = storage.Load();
            var second = new TrustedAutomaticCycle(restarted, storage, () => now.AddMinutes(20))
                .ExecuteAsync(manifest, (_, _, _, _) =>
                    throw new InvalidOperationException("terminal coupon resent after restart"),
                    CancellationToken.None).GetAwaiter().GetResult();
            Require(second == new AutomaticCycleResult(0, 0, 0) &&
                    restarted.History["e2e-account"]["E2ECODE1"].Status == "success" &&
                    restarted.Attempts["e2e-account"]["E2ECODE1"].Status == AttemptStatus.Terminal,
                "restart/catch-up terminal suppression 실패");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void TestAnomalyEvidenceAndRepairGate()
    {
        var now = new DateTimeOffset(2026, 8, 27, 2, 0, 0, TimeSpan.Zero);
        var health = new List<SourceHealth>
        {
            new("Synthetic Source", true, 1000, 8, 9, ["MISSINGCODE"], ["EXTRAUI"],
                "synthetic redacted anomaly", 2, 2, [new string('d', 64)], [8, 8], 9, 0, true,
                ["parser missing 1"])
        };
        var scan = new ScanResult(["CODE1"],
            new(StringComparer.OrdinalIgnoreCase) { ["CODE1"] = ["Synthetic Source"] },
            ["Synthetic Source"], ["Synthetic Source: parser regression"], health);
        var first = AnomalyEvidenceService.Create(scan, "corr-1", now, "1.5.0", "parser-1");
        var second = AnomalyEvidenceService.Create(scan, "corr-2", now.AddMinutes(1), "1.5.0", "parser-1");
        Require(first.EvidenceGroup == second.EvidenceGroup,
            "동일 payload/parser 증거가 equivalence group으로 압축되지 않음");
        var json = AnomalyEvidenceService.Serialize(first);
        Require(!json.Contains("MISSINGCODE", StringComparison.Ordinal) &&
                !json.Contains("EXTRAUI", StringComparison.Ordinal) &&
                json.Contains("missingCount", StringComparison.Ordinal),
            "anomaly evidence가 코드/원문을 노출하거나 redacted count를 잃음");
        Require(AnomalyEvidenceService.Classify(new RepairProposal(
                "source-parser", ["source-parser"], HasRegressionFixture: true)) == RepairEligibility.ReviewOnly,
            "fixture가 있는 source parser 수정이 review-only로 분류되지 않음");
        Require(AnomalyEvidenceService.Classify(new RepairProposal(
                "source-parser", ["account"], HasRegressionFixture: true)) == RepairEligibility.Blocked &&
                AnomalyEvidenceService.Classify(new RepairProposal(
                    "source-parser", ["source-parser"], HasRegressionFixture: false)) == RepairEligibility.Blocked &&
                AnomalyEvidenceService.Classify(new RepairProposal(
                    "feature", ["source-parser"], HasRegressionFixture: true)) == RepairEligibility.Blocked,
            "critical/no-fixture/non-parser 변경이 자동 review gate를 통과함");
    }

    private static void TestLoginStartRegistration()
    {
        var spec = LoginStartRegistrationFactory.Create(@"C:\Synthetic App\SWCouponManager.exe");
        Require(spec.Name == "SWCouponManager Background Agent" &&
                spec.CommandLine == "\"C:\\Synthetic App\\SWCouponManager.exe\" --background",
            "login-start 등록 명세의 quoting/argument 실패");
        var rejected = false;
        try { LoginStartRegistrationFactory.Create("SWCouponManager.exe"); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "login-start 상대 경로 허용");
    }

    private static void TestBackgroundAgentLifecycle()
    {
        Require(BackgroundAgentLifecycle.DecideClose(backgroundEnabled: true, explicitExit: false) ==
                WindowCloseDisposition.HideToTray,
            "background 활성 창 닫기가 tray 숨김으로 결정되지 않음");
        Require(BackgroundAgentLifecycle.DecideClose(backgroundEnabled: false, explicitExit: false) ==
                WindowCloseDisposition.Exit &&
                BackgroundAgentLifecycle.DecideClose(backgroundEnabled: true, explicitExit: true) ==
                WindowCloseDisposition.Exit,
            "비활성/명시 종료 정책이 프로세스를 남김");

        var mutexName = "Local\\SWCouponManager-SelfTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstanceLease.TryAcquire(mutexName);
        Require(first is not null, "single-instance 첫 lease 획득 실패");
        using var second = SingleInstanceLease.TryAcquire(mutexName);
        Require(second is null, "single-instance 중복 lease 허용");
    }

    private static void TestTrayOwnerLifecycle()
    {
        for (var i = 0; i < 10; i++)
        {
            var owner = new TrayOwner(() => { }, () => { }, () => { });
            owner.SetPaused(i % 2 == 0);
            Require(!owner.IsDisposed, $"tray owner 조기 dispose #{i + 1}");
            owner.Dispose();
            owner.Dispose();
            Require(owner.IsDisposed, $"tray owner dispose 실패 #{i + 1}");
        }
    }

    private static void TestRedemptionAttemptPersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "SWCouponManagerRedemptionAttemptTest", Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
            var storage = new AppStorage(root);
            var state = new AppState();
            var account = new Account { Id = "synthetic-account", HiveId = "synthetic" };

            async Task<(AppState state, AttemptRecord attempt)> Execute(
                string code, Func<Action, Task<(string status, string message)>> adapter)
            {
                var current = storage.Load();
                var coordinator = new RedemptionAttemptCoordinator(current, storage, () => now, TimeSpan.FromMinutes(1));
                string? loggedCorrelation = null;
                await coordinator.ExecuteAsync(new WorkItem(account, code), adapter, id => loggedCorrelation = id);
                var restored = storage.Load();
                Require(loggedCorrelation == restored.Attempts[account.Id][code].CorrelationId,
                    "attempt correlation이 audit 경계로 전달되지 않음");
                return (restored, restored.Attempts[account.Id][code]);
            }

            var terminal = Execute("TERMINAL1", submitted =>
            {
                submitted();
                return Task.FromResult(("success", "synthetic success"));
            }).GetAwaiter().GetResult();
            Require(terminal.attempt.Status == AttemptStatus.Terminal,
                "terminal 결과가 제출 경계 뒤 영속화되지 않음");

            var preSubmit = Execute("PRESUBMIT1", _ =>
                Task.FromResult(("error", "synthetic pre-submit failure"))).GetAwaiter().GetResult();
            Require(preSubmit.attempt.Status == AttemptStatus.TemporaryFailure && preSubmit.attempt.RetryAfter > now,
                "제출 전 오류가 bounded retry로 영속화되지 않음");

            try
            {
                Execute("POSTSUBMIT1", submitted =>
                {
                    submitted();
                    throw new InvalidOperationException("synthetic crash after submit");
                }).GetAwaiter().GetResult();
                throw new InvalidOperationException("제출 후 합성 crash가 발생하지 않음");
            }
            catch (InvalidOperationException ex) when (ex.Message == "synthetic crash after submit") { }

            var restarted = storage.Load();
            Require(restarted.Attempts[account.Id]["POSTSUBMIT1"].Status == AttemptStatus.Ambiguous,
                "제출 후 crash가 restart 시 ambiguous로 격리되지 않음");
            var restartedJournal = new AttemptJournal(restarted, () => now.AddHours(1));
            Require(!restartedJournal.CanQueue(account.Id, "POSTSUBMIT1", restarted.History),
                "restart 후 ambiguous 시도가 자동 재전송됨");

            try
            {
                Execute("CANCELBEFORE1", _ => Task.FromCanceled<(string, string)>(new(true)))
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            var cancelledBefore = storage.Load();
            Require(cancelledBefore.Attempts[account.Id]["CANCELBEFORE1"].Status == AttemptStatus.TemporaryFailure,
                "제출 전 cancellation이 bounded retry 상태로 저장되지 않음");

            try
            {
                Execute("CANCELAFTER1", submitted =>
                {
                    submitted();
                    return Task.FromCanceled<(string, string)>(new(true));
                }).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            var cancelledAfter = storage.Load();
            Require(cancelledAfter.Attempts[account.Id]["CANCELAFTER1"].Status == AttemptStatus.Ambiguous,
                "제출 후 cancellation이 ambiguous로 격리되지 않음");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
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
