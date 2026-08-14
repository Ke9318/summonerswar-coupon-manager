namespace SWCouponManager;

internal static class SelfTest
{
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
