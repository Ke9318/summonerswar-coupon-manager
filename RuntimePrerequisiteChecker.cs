using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace SWCouponManager;

internal static class RuntimePrerequisiteChecker
{
    private const string WebView2DownloadUrl =
        "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";

    public static bool EnsureAvailable()
    {
        // A framework-dependent .NET apphost checks Microsoft.WindowsDesktop.App 8
        // before managed code starts and shows Microsoft's install prompt when absent.
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrWhiteSpace(version))
                return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
        }
        catch (Exception ex)
        {
            CrashReporter.Report(ex);
            return false;
        }

        var answer = MessageBox.Show(
            "Microsoft Edge WebView2 Runtime이 필요합니다.\n\n" +
            "설치 페이지를 여시겠습니까? 설치 후 프로그램을 다시 실행해 주세요.",
            "WebView2 Runtime 필요",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebView2DownloadUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"설치 페이지를 열지 못했습니다. 다음 주소에서 설치해 주세요.\n\n{WebView2DownloadUrl}\n\n{ex.Message}",
                    "WebView2 Runtime 필요",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        return false;
    }
}
