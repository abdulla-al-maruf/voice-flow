using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    internal static Action BeforeFatal;
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test") { AppPaths.Data=Path.Combine(Root,"tests","isolated-"+Guid.NewGuid().ToString("N"));return SelfTests.Run(); }
            AppLinks.Load();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){Fatal(e.Exception);};
            AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs e){Fatal(e.ExceptionObject as Exception);};
            bool created;
            using (var instance = new Mutex(true, "Local\\VoiceFlowStandalone-" + Environment.UserName, out created))
            {
                if (!created) { MessageBox.Show("Voice Flow ট্রেতে চলছে। সবুজ আইকনে ডাবল ক্লিক করুন।", "Voice Flow"); return 0; }
                Application.Run(new VoiceFlowApp(args.Length > 0 && args[0] == "--signin"));
            }
            return 0;
        }
        catch (Exception exception)
        {
            Diagnostics.Error("fatal",exception);
            MessageBox.Show("Voice Flow শুরু হয়নি। অ্যাপটি আবার খুলুন অথবা installer দিয়ে মেরামত করুন।", "Voice Flow"); return 1;
        }
    }
    internal static void Log(string message)
    {
        Diagnostics.Event("event");
    }
    static void Fatal(Exception exception)
    {
        Diagnostics.Error("fatal",exception);
        try{if(BeforeFatal!=null)BeforeFatal();}catch{ }
        Environment.Exit(1);
    }
}
