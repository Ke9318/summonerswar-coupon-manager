namespace SWCouponManager;

internal static class DisposableUpdateTransaction
{
    internal static void Apply(
        string stagingDirectory,
        string destinationDirectory,
        Action<string> validate,
        int? injectFailureAfterCopies = null)
    {
        var files = Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories);
        var backup = Path.Combine(Path.GetTempPath(), "SWCouponManagerUpdateRollback", Guid.NewGuid().ToString("N"));
        var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var source in files)
            {
                var relative = Path.GetRelativePath(stagingDirectory, source);
                var current = Path.Combine(destinationDirectory, relative);
                if (!File.Exists(current)) continue;
                existed.Add(relative);
                var saved = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(current, saved, true);
            }

            var copied = 0;
            foreach (var source in files)
            {
                var relative = Path.GetRelativePath(stagingDirectory, source);
                var current = Path.Combine(destinationDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                File.Copy(source, current, true);
                copied++;
                if (injectFailureAfterCopies == copied)
                    throw new IOException("synthetic post-copy failure");
            }
            validate(destinationDirectory);
        }
        catch
        {
            foreach (var source in files)
            {
                var relative = Path.GetRelativePath(stagingDirectory, source);
                var current = Path.Combine(destinationDirectory, relative);
                var saved = Path.Combine(backup, relative);
                if (existed.Contains(relative))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                    File.Copy(saved, current, true);
                }
                else if (File.Exists(current))
                    File.Delete(current);
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(backup, true); } catch { }
        }
    }
}
