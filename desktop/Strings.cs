using System.Collections.Generic;
using System.Globalization;

namespace AgentHud
{
    // Widget language. "auto" follows the Windows display language (Chinese → 中文, otherwise English);
    // the menu switch stores "zh" or "en". Text that the local service sends (quota sources and status
    // messages, written in Chinese) is mapped to English here when the widget is in English.
    public static class Lang
    {
        public static bool English { get; private set; }
        public static string Setting { get; private set; }

        static Lang() { Apply(null); }

        public static void Apply(string setting)
        {
            Setting = setting == "zh" || setting == "en" ? setting : "auto";
            English = Setting == "en" || (Setting == "auto" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh");
        }

        public static string T(string zh, string en) { return English ? en : zh; }

        static readonly Dictionary<string, string> ServerText = new Dictionary<string, string>
        {
            { "Codex 官方额度接口", "Codex official quota API" },
            { "Codex 会话快照", "Codex session snapshot" },
            { "Claude 桌面账户额度", "Claude desktop account" },
            { "Claude Code 状态栏", "Claude Code status line" },
            { "最近一次模型请求", "latest model request" },
            { "桌面版账户额度读取通道已准备好，尚未启用连接。", "The Claude desktop account connection is available but not enabled." },
            { "未找到 Claude 桌面版登录连接，请先打开 Claude 并登录。", "No Claude desktop sign-in found. Open Claude and sign in first." },
            { "Claude 桌面版尚未提供账户连接，请打开 Code 标签页并确认已登录。", "Claude desktop hasn't provided an account connection yet. Open its Code tab and make sure you're signed in." },
            { "Claude 桌面版登录连接已过期，请打开 Claude，让它自动续期。", "The Claude desktop sign-in has expired. Open Claude so it can renew it." },
            { "暂时无法读取 Claude 的加密账户连接，将稍后重试。", "Can't read Claude's encrypted account connection right now; will retry later." },
            { "当前 Claude 版本的账户存储格式尚未支持。", "This Claude version's account storage format isn't supported yet." },
            { "发现多个 Claude 组织的有效连接，无法确定当前额度所属组织。", "Found connections for several Claude organizations; can't tell which one the quota belongs to." },
            { "Claude 账户连接已失效，请在 Claude 桌面版中重新登录。", "The Claude account connection is no longer valid. Sign in again in Claude desktop." },
            { "Claude 额度接口拒绝了此账户连接。", "Claude's quota service rejected this account connection." },
            { "Claude 额度查询被限流，已降低查询频率，稍后自动重试。", "Claude quota requests are rate limited; querying less often and retrying later." },
            { "Claude 额度服务暂不可用，稍后自动重试。", "Claude's quota service is temporarily unavailable; will retry later." },
            { "Claude 返回了响应，但没有提供额度百分比。", "Claude responded but didn't include a quota percentage." },
            { "读取 Codex 额度超时", "Reading the Codex quota timed out" },
            { "Codex 额度连接不可用", "The Codex quota connection is unavailable" },
            { "Codex 暂未提供额度，请检查登录状态", "Codex hasn't reported a quota yet; check that you're signed in" },
            { "官方额度接口暂不可用，显示最近会话快照", "Official quota API unavailable; showing the latest session snapshot" },
            { "检测到 Claude 桌面版会话，未收到终端状态栏数据；当前桥接不能自动显示桌面版额度。", "Claude desktop sessions found, but no terminal status-line data; the bridge can't show the desktop quota." },
            { "状态栏已运行，但 Claude 未返回额度字段；字段取决于账户类型和版本。", "The status line runs, but Claude returned no quota fields; they depend on account type and version." },
            { "已安装终端版状态栏桥接，等待 Claude Code CLI 返回数据。", "Terminal status-line bridge installed; waiting for data from the Claude Code CLI." },
            { "尚未接入 Claude Code CLI 状态栏。", "The Claude Code CLI status line isn't connected yet." },
        };

        static readonly Dictionary<string, string> WindowWords = new Dictionary<string, string>
        {
            { "短周期", "Short window" }, { "长周期", "Long window" }, { "支出额度", "Spend limit" },
        };
        static readonly System.Text.RegularExpressions.Regex Span = new System.Text.RegularExpressions.Regex(@"^(.*?)(\d+) (分钟|小时|天)$");

        public static string Server(string text)
        {
            string english;
            if (!English || text == null) return text;
            if (ServerText.TryGetValue(text, out english) || WindowWords.TryGetValue(text, out english)) return english;
            // Quota window names such as "5 小时", "7 天" or "Opus · 7 天".
            var span = Span.Match(text);
            if (!span.Success) return text;
            var n = int.Parse(span.Groups[2].Value, CultureInfo.InvariantCulture);
            var unit = span.Groups[3].Value == "分钟" ? "min" : span.Groups[3].Value == "小时" ? (n == 1 ? "hour" : "hours") : (n == 1 ? "day" : "days");
            return span.Groups[1].Value + n + " " + unit;
        }
    }
}
