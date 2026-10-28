using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// The user's own API keys, kept Windows-protected like the sign-in. Never logged, never shown again after saving.
internal static class KeyStore
{
    static string FileName { get { return Path.Combine(AppPaths.Data, "engine-keys.vault"); } }
    static Dictionary<string, string> Read()
    {
        try { return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(AppPaths.Unprotect(File.ReadAllBytes(FileName)))) ?? new Dictionary<string, string>(); }
        catch { return new Dictionary<string, string>(); }
    }
    static void Write(Dictionary<string, string> keys)
    {
        if (keys.Count == 0) { if (File.Exists(FileName)) File.Delete(FileName); return; }
        AppPaths.Atomic(FileName, delegate(BinaryWriter writer) { writer.Write(AppPaths.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(keys)))); });
    }
    internal static bool Valid(string key) { return key != null && Regex.IsMatch(key, @"^[\x21-\x7e]{8,512}$"); }
    internal static string Get(string engine) { string key; return Read().TryGetValue(engine, out key) ? key : null; }
    internal static bool Has(string engine) { return Valid(Get(engine)); }
    internal static void Save(string engine, string key)
    {
        if (!EngineChoice.Paid.Contains(engine) || !Valid(key)) throw new ArgumentException("Invalid engine key");
        var keys = Read(); keys[engine] = key; Write(keys);
    }
    internal static void Remove(string engine) { var keys = Read(); if (keys.Remove(engine)) Write(keys); }
}

// Downloads the offline Bangla model (SayedShaun/bangla-zipformer-onnx, int8, CC BY-SA 4.0) into the user's own data folder.
// Every file is checked against the size and SHA-256 of the copy that was tested, so a changed or damaged download is rejected.
internal static class ModelDownload
{
    const string Base = "https://huggingface.co/SayedShaun/bangla-zipformer-onnx/resolve/main/";
    static readonly string[][] Files =
    {
        new[] { "encoder.onnx", "int8/encoder.onnx", "72828389", "43d5bdfc8e956c8137b8652fca347a4deb8ae6742cc0a2840db47c02a5c4cd4a" },
        new[] { "decoder.onnx", "int8/decoder.onnx", "540689", "a183a765346a050c8577dc947cb96e69beecb8a83a73ebef48ad0cdd387e18df" },
        new[] { "joiner.onnx", "int8/joiner.onnx", "259417", "bd9d5b3a02b0550fc09e55f222dce6ac72d38396b5c5087c462d67d0cb840fb4" },
        new[] { "tokens.txt", "tokens.txt", "6578", "8edb5b3ef2587207c86cc3966f3f729ec3845250d3b040e5ba916ce935fcae2b" },
        new[] { "LICENSE", "LICENSE", "20138", "28a9529c7d0bb4dc51f4bf5c116a3d16ef247a052f7591466768ddf563fd1cf5" }
    };
    internal static long TotalBytes { get { return Files.Sum(f => long.Parse(f[2])); } }
    internal static bool Matches(string path, string size, string hash)
    {
        try
        {
            if (new FileInfo(path).Length != long.Parse(size)) return false;
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create()) return string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""), hash, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    internal static void Run(string dir, Action<int> progress)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Directory.CreateDirectory(dir);
        long done = 0, total = TotalBytes;
        foreach (var f in Files)
        {
            string target = Path.Combine(dir, f[0]), part = target + ".part";
            if (File.Exists(target) && Matches(target, f[2], f[3])) { done += long.Parse(f[2]); progress((int)(done * 100 / total)); continue; }
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(Base + f[1]); request.UserAgent = "VoiceFlow"; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { output.Write(buffer, 0, read); done += read; progress((int)Math.Min(99, done * 100 / total)); }
                }
                if (!Matches(part, f[2], f[3])) throw new InvalidDataException("Downloaded model file does not match");
                if (File.Exists(target)) File.Delete(target);
                File.Move(part, target);
            }
            finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
        }
        progress(100);
    }
}

// Free, harmless key check for Chariot: it only asks for the account's credit balance (no audio, no cost).
internal static class KeyCheck
{
    // "ok", "bad" (the service refused the key) or "unknown" (no internet or an unexpected answer).
    internal static string Chariot(string key)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create("https://api.chariot.in/v1/credits"); request.Timeout = 10000; request.UserAgent = "VoiceFlow";
            request.Headers.Add("chariotai-api-key", key);
            using (var response = (HttpWebResponse)request.GetResponse()) return response.StatusCode == HttpStatusCode.OK ? "ok" : "unknown";
        }
        catch (WebException error)
        {
            var response = error.Response as HttpWebResponse;
            return response != null && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) ? "bad" : "unknown";
        }
        catch { return "unknown"; }
    }
}

// The key dialog walks through it: open the website, sign in, make a key, paste it. Returns null when cancelled, "" to remove the saved key.
internal sealed class KeyDialog : Form
{
    readonly TextBox box = new TextBox();
    readonly Label state = new Label();
    readonly VoiceButton save = new VoiceButton { Dark = true, Text = "সংরক্ষণ" }, verify = new VoiceButton { Quiet = true, Text = "চাবি যাচাই" };
    readonly string engine;
    string result;
    KeyDialog(string engineName, bool hasKey)
    {
        engine = engineName; string company = EngineChoice.Company(engine);
        Text = company + " চাবি · Voice Flow"; ClientSize = new Size(520, 492); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Theme.Window(this);
        var title = new Label { Text = company + "-এর চাবি বসান", Font = Theme.Bold(18), ForeColor = Theme.Ink, AutoSize = false };
        var credit = new Label { Text = engine == "sarvam" ? "নতুন Sarvam account-এ ₹১০০ ফ্রি ক্রেডিট পাওয়া যায়। এতে বেশ কিছুক্ষণ কথা লেখা যাবে।" : "Chariot-এর দাম account খোলার পরে তাদের পাতায় দেখা যায়।", ForeColor = Theme.Muted, Font = Theme.Font(12), AutoSize = false };
        var step1 = new Label { Text = "১. ওয়েবসাইট খুলুন, সাইন ইন করে নতুন API key বানান", ForeColor = Theme.Ink, Font = Theme.Bold(12), AutoSize = false };
        var open = new VoiceButton { Primary = true, Text = company + "-এর চাবির পাতা খুলুন" };
        var step2 = new Label { Text = "২. key কপি করে এখানে পেস্ট করুন", ForeColor = Theme.Ink, Font = Theme.Bold(12), AutoSize = false };
        var paste = new VoiceButton { Quiet = true, Symbol = "copy", Text = "পেস্ট" };
        var cancel = new VoiceButton { Quiet = true, Text = "বাতিল" }; var remove = new VoiceButton { Quiet = true, Danger = true, Text = "চাবি মুছুন" };
        var privacy = new Label { Text = "এই ইঞ্জিন বেছে নিলে আপনার কথার অডিও " + company + "-এর সার্ভারে যাবে, আর খরচ আপনার নিজের account-এ লাগবে। চাবি এই কম্পিউটারে সুরক্ষিত থাকে, আর দেখা যায় না।", ForeColor = Theme.Muted, Font = Theme.Font(11.5f), AutoSize = false };
        box.UseSystemPasswordChar = true; box.BackColor = Theme.Surface; box.ForeColor = Theme.Ink; box.BorderStyle = BorderStyle.FixedSingle; box.Font = new Font("Segoe UI", 13); box.AccessibleName = company + " চাবি"; box.MaxLength = 600;
        state.ForeColor = Theme.Muted; state.Font = Theme.Font(12); state.AutoSize = false;
        open.Click += delegate { AppLinks.OpenFixed(EngineChoice.KeyPage(engine)); };
        paste.Click += delegate { try { string text = (Clipboard.GetText() ?? "").Trim(); if (text.Length > 0 && text.Length <= 600) box.Text = text; else Show("ক্লিপবোর্ডে চাবি পাওয়া যায়নি। আগে চাবিটি কপি করুন।", Theme.Warn); } catch { Show("ক্লিপবোর্ড পড়া যায়নি।", Theme.Warn); } };
        box.TextChanged += delegate { Refresh(); };
        save.Click += delegate { result = box.Text.Trim(); DialogResult = DialogResult.OK; };
        cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
        remove.Click += delegate { result = ""; DialogResult = DialogResult.OK; };
        verify.Click += delegate
        {
            string key = box.Text.Trim(); verify.Enabled = false; Show("যাচাই হচ্ছে…", Theme.Muted);
            Task.Run(() => KeyCheck.Chariot(key)).ContinueWith(task => { try { BeginInvoke((Action)delegate { verify.Enabled = KeyStore.Valid(box.Text.Trim()); if (task.Result == "ok") Show("✓ Chariot চাবিটি মেনে নিয়েছে।", Theme.Good); else if (task.Result == "bad") Show("Chariot এই চাবি মানছে না। আবার কপি করে দেখুন।", Theme.Danger); else Show("যাচাই করা গেল না। ইন্টারনেট দেখুন, অথবা এমনিই সংরক্ষণ করুন।", Theme.Warn); }); } catch (InvalidOperationException) { } });
        };
        title.SetBounds(24, 18, 472, 34); credit.SetBounds(24, 56, 472, 44);
        step1.SetBounds(24, 112, 472, 26); open.SetBounds(24, 142, 472, 46);
        step2.SetBounds(24, 204, 472, 26); box.SetBounds(24, 236, 352, 30); paste.SetBounds(388, 230, 108, 42);
        state.SetBounds(24, 276, 472, 44); privacy.SetBounds(24, 318, 472, 90);
        int x = 24; save.SetBounds(x, 424, 150, 46); x += 158; verify.Visible = engine == "chariot"; if (verify.Visible) { verify.SetBounds(x, 424, 120, 46); x += 128; }
        cancel.SetBounds(x, 424, 90, 46); x += 98; remove.Visible = hasKey; if (hasKey) remove.SetBounds(x, 424, 120, 46);
        Controls.AddRange(new Control[] { title, credit, step1, open, step2, box, paste, state, privacy, save, verify, cancel, remove }); AcceptButton = save; CancelButton = cancel;
        Refresh();
    }
    void Show(string text, Color color) { state.Text = text; state.ForeColor = color; }
    // Save and verify only when the key looks right; the message says what is wrong.
    new void Refresh()
    {
        string key = box.Text.Trim(); bool valid = KeyStore.Valid(key);
        save.Enabled = valid; verify.Enabled = valid;
        if (key.Length == 0) Show("", Theme.Muted);
        else if (valid) Show("✓ চাবির আকার ঠিক আছে।", Theme.Good);
        else if (key.Length < 8) Show("চাবিটি খুব ছোট। পুরোটা কপি হয়েছে কি না দেখুন।", Theme.Warn);
        else Show("চাবিতে ফাঁকা জায়গা বা অদ্ভুত অক্ষর আছে। শুধু চাবিটুকু কপি করুন।", Theme.Warn);
    }
    internal static KeyDialog ForPreview(string engine, bool hasKey) { return new KeyDialog(engine, hasKey); }
    internal static string Ask(IWin32Window owner, string engine, bool hasKey)
    {
        using (var dialog = new KeyDialog(engine, hasKey)) return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.result : null;
    }
}
