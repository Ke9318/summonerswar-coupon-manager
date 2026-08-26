using System.Text.RegularExpressions;

namespace SWCouponManager;

internal static class RedemptionResultClassifier
{
    internal static string Classify(string message)
    {
        var m = message.ToLowerInvariant();
        if (Regex.IsMatch(m, "already|used|이미\\s*사용|사용한|등록된")) return "already";
        if (Regex.IsMatch(m, "expired|만료")) return "expired";
        if (Regex.IsMatch(m, "success|complete|reward|성공|완료|보상|지급")) return "success";
        if (Regex.IsMatch(m, "invalid|not valid|유효하지|유효한.*아닙니다|존재하지|wrong|잘못된|없는 쿠폰")) return "invalid";
        if (Regex.IsMatch(m, "error|오류|fail|실패|timeout|timed out|network|connection|일시적|네트워크|연결"))
            return "error";
        return "ambiguous";
    }
}
