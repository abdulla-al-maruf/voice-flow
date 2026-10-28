using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static class Diagnostics
{
    const int MaxFile = 256 * 1024;
    static readonly object gate = new object();
    static readonly HashSet<string> events = new HashSet<string>(new[] { "startup", "shutdown", "state", "phase", "session-complete", "session-empty", "session-incomplete", "session-cancelled", "signin-browser", "signin-failed", "signin-restored", "signin-save-failed", "history-read-failed", "history-save-failed", "history-recovery-failed", "helper-stopped", "clipboard-failed", "paste-fallback", "fatal", "ui-error", "report-open-failed", "fallback-local", "model-download-failed", "event" });
    internal static void State(string mode){Write("state"," mode="+(new[]{"idle","recording","finishing","signin","error"}.Contains(mode)?mode:"unknown"));}
    internal static void Phase(string phase){Write("phase"," step="+(new[]{"capture","tail","connect","drain","cloud","delivery","history"}.Contains(phase)?phase:"unknown"));}
    internal static void SignIn(string code){Write("signin-failed"," stage="+(new[]{"client-missing","client-unsupported","voice-unavailable","signin-incomplete"}.Contains(code)?code:"unknown"));}
    internal static void Event(string code) { Write(events.Contains(code) ? code : "event", ""); }
    internal static void Counts(string code, long bytes, int characters) { Write(events.Contains(code) ? code : "event", " bytes=" + Math.Max(0, bytes) + " characters=" + Math.Max(0, characters)); }
    internal static void Error(string code, Exception exception)
    { Write(events.Contains(code) ? code : "event", " kind=" + (exception is IOException ? "io" : exception is TimeoutException ? "timeout" : exception is System.Security.Cryptography.CryptographicException ? "protection" : "unexpected")); }
    static void Write(string code, string details)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(AppPaths.Logs); string file = Path.Combine(AppPaths.Logs, "voice-flow.log");
                if (File.Exists(file) && new FileInfo(file).Length > MaxFile)
                {
                    for (int i = 3; i >= 1; i--) { string dest = file + "." + i; if (File.Exists(dest)) File.Delete(dest); string from = i == 1 ? file : file + "." + (i - 1); if (File.Exists(from)) File.Move(from, dest); }
                }
                File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + code + details + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }
    internal static string Summary()
    {
        var result = new StringBuilder("Voice Flow " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + Environment.NewLine);
        result.AppendLine("Windows " + Environment.OSVersion.Version + "; " + (Environment.Is64BitProcess ? "x64" : "x86"));
        result.AppendLine("Audio, transcript, clipboard, account and sign-in credentials are excluded.");
        lock (gate)
        {
            for (int i = 3; i >= 0; i--)
            {
                string file = Path.Combine(AppPaths.Logs, "voice-flow.log" + (i == 0 ? "" : "." + i));
                if (File.Exists(file)) result.Append(File.ReadAllText(file));
            }
        }
        return result.ToString();
    }
}

internal static class AppLinks
{
    internal static string Repository = "https://github.com/abdulla-al-maruf/voice-flow";
    internal static bool Published;
    internal static bool ValidRepository(string value)
    {
        Uri url; return Uri.TryCreate(value, UriKind.Absolute, out url) && url.Scheme == "https" && url.Host == "github.com" && url.Port == 443 && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 && Regex.IsMatch(url.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
    }
    internal static void Load()
    {
        try
        {
            var config = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Program.Root, "release.json")));
            string repo = Convert.ToString(config["repository"]); if (!ValidRepository(repo)) return;
            Repository = repo; Published = config.ContainsKey("published") && Convert.ToBoolean(config["published"]);
        }
        catch { }
    }
    // Only these two key pages can be opened by the key dialog; nothing typed by the user ever becomes a link.
        internal static bool OpenFixed(string url)
    {
        if (!EngineChoice.Paid.Any(engine => EngineChoice.KeyPage(engine) == url)) return false;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch { Diagnostics.Event("report-open-failed"); return false; }
    }
    internal static string IssueUrl { get { return Repository + "/issues/new?template=bug_report.md"; } }
    internal static bool Open(bool updates)
    {
        if (!Published || !ValidRepository(Repository)) return false;
        try { Process.Start(new ProcessStartInfo(updates ? Repository + "/releases/latest" : IssueUrl) { UseShellExecute = true }); return true; }
        catch { Diagnostics.Event("report-open-failed"); return false; }
    }
}
