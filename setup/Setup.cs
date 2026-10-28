using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class SetupProgram
{
    internal const string Marker="VoiceFlow.7f6d8a21.2026",RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run",UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\VoiceFlow";
    internal static string InstallRoot{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Voice Flow");}}
    internal static string InstalledExe{get{return Path.Combine(InstallRoot,"VoiceFlow.exe");}}
    [STAThread] static int Main(string[] args)
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0&&args[0]=="--self-test")return SelfTest();
        if(args.Length>0&&args[0]=="--install")
        {
            try{Install(true,true);Process.Start(new ProcessStartInfo(InstalledExe,"--signin"){WorkingDirectory=InstallRoot,UseShellExecute=false});return 0;}
            catch(Exception error){Diagnostics.Error("event",error);return 1;}
        }
        bool uninstall=args.Contains("--uninstall");
#if UNINSTALL
        uninstall=true;
#endif
        try{using(var form=new SetupWindow(uninstall))Application.Run(form);return 0;}catch{MessageBox.Show("কাজটি সম্পূর্ণ হয়নি। চলমান অ্যাপ ও রেকর্ডিং নিরাপদ আছে।","Voice Flow");return 1;}
    }
    internal static string Inside(string root,string relative)
    {
        string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new InvalidDataException("Absolute archive path");
        string full=Path.GetFullPath(Path.Combine(prefix,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive escaped destination");return full;
    }
    internal static void Extract(Stream payload,string root)
    {
        Directory.CreateDirectory(root);long total=0;
        using(var archive=new ZipArchive(payload,ZipArchiveMode.Read))
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in archive.Entries)
            {
                if(!names.Add(entry.FullName))throw new InvalidDataException("Duplicate archive path");
                string target=Inside(root,entry.FullName);
                if((entry.ExternalAttributes&0x400)!=0||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Archive links are unsupported");
                total+=entry.Length;if(total>160*1024*1024L||entry.Length<0)throw new InvalidDataException("Archive size exceeded");
                if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(target));using(var source=entry.Open())using(var output=File.Create(target))source.CopyTo(output);
            }
        }
        string manifest=Path.Combine(root,"checksums.sha256");var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string line in File.ReadAllLines(manifest))
        {
            if(line.Length<67||line.Substring(64,2)!="  ")throw new InvalidDataException("Invalid package manifest");
            string relative=line.Substring(66),file=Inside(root,relative);if(!expected.Add(Path.GetFullPath(file)))throw new InvalidDataException("Duplicate checksum");
            using(var sha=SHA256.Create())using(var input=File.OpenRead(file))if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=line.Substring(0,64))throw new InvalidDataException("Package checksum mismatch");
        }
        foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))if(file!=manifest&&!expected.Contains(Path.GetFullPath(file)))throw new InvalidDataException("Unlisted package file");
        foreach(string required in new[]{"VoiceFlow.exe","VoiceFlow.Uninstall.exe","runtime/node.exe","bridge/bridge.cjs","release.json"})if(!File.Exists(Inside(root,required)))throw new InvalidDataException("Required file missing");
    }
    static bool StopInstalled()
    {
        foreach(var process in Process.GetProcessesByName("VoiceFlow"))
        {
            if(!string.Equals(process.MainModule.FileName,InstalledExe,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Voice Flow অন্য ফোল্ডার থেকে চলছে। আগে সেটি বন্ধ করুন।");
            if(!CloseSafely(process.Id)||!process.WaitForExit(5000))return false;
        }
        return true;
    }
    internal static void Install(bool startup,bool desktop)
    {
        object release;using(var framework=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))release=framework==null?null:framework.GetValue("Release");
        if(!Environment.Is64BitOperatingSystem||Convert.ToInt32(release)<461808)throw new InvalidOperationException("Windows x64 এবং .NET Framework 4.7.2 বা নতুন সংস্করণ প্রয়োজন।");
        string parent=Path.GetDirectoryName(InstallRoot),stage=Path.Combine(parent,"VoiceFlow-stage-"+Guid.NewGuid().ToString("N")),backup=Path.Combine(parent,"VoiceFlow-previous-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        bool movedOld=false,movedNew=false;object previousStartup;using(var run=Registry.CurrentUser.CreateSubKey(RunKey))previousStartup=run.GetValue("VoiceFlow");
        var oldShortcuts=new Dictionary<string,byte[]>();foreach(string link in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Voice Flow.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Voice Flow.lnk")})oldShortcuts[link]=File.Exists(link)?File.ReadAllBytes(link):null;
        var oldValues=new Dictionary<string,object>();var oldKinds=new Dictionary<string,RegistryValueKind>();bool oldKey;
        using(var key=Registry.CurrentUser.OpenSubKey(UninstallKey)){oldKey=key!=null;if(key!=null)foreach(string name in key.GetValueNames()){oldValues[name]=key.GetValue(name);oldKinds[name]=key.GetValueKind(name);}}
        try
        {
            using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")){if(payload==null)throw new InvalidDataException("Installer payload unavailable");Extract(payload,stage);}
            if(Directory.Exists(InstallRoot)&&(!File.Exists(Path.Combine(InstallRoot,"install.id"))||File.ReadAllText(Path.Combine(InstallRoot,"install.id"))!=Marker))throw new InvalidOperationException("ইনস্টলেশনের ফোল্ডারে অন্য ফাইল আছে। ফোল্ডারটি যাচাই করুন।");
            if(!StopInstalled())throw new InvalidOperationException("কথা নেওয়া বা সংরক্ষণ চলছে। শেষ হলে installer আবার চালান।");
            if(Directory.Exists(InstallRoot)){Directory.Move(InstallRoot,backup);movedOld=true;}
            Directory.Move(stage,InstallRoot);movedNew=true;File.WriteAllText(Path.Combine(InstallRoot,"install.id"),Marker);
            Register(startup,desktop);
        }
        catch
        {
            if(movedNew&&Directory.Exists(InstallRoot))Directory.Delete(InstallRoot,true);
            if(movedOld&&Directory.Exists(backup))Directory.Move(backup,InstallRoot);
            using(var run=Registry.CurrentUser.CreateSubKey(RunKey)){if(previousStartup==null)run.DeleteValue("VoiceFlow",false);else run.SetValue("VoiceFlow",previousStartup);}
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey,false);
            if(oldKey)using(var key=Registry.CurrentUser.CreateSubKey(UninstallKey))foreach(var value in oldValues)key.SetValue(value.Key,value.Value,oldKinds[value.Key]);
            foreach(var link in oldShortcuts){if(link.Value==null){if(File.Exists(link.Key))File.Delete(link.Key);}else File.WriteAllBytes(link.Key,link.Value);}
            throw;
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
        if(movedOld&&Directory.Exists(backup))try{Directory.Delete(backup,true);}catch{Diagnostics.Event("event");}
    }
    static void Register(bool startup,bool desktop)
    {
        using(var run=Registry.CurrentUser.CreateSubKey(RunKey)){if(startup)run.SetValue("VoiceFlow","\""+InstalledExe+"\"");else run.DeleteValue("VoiceFlow",false);}
        using(var key=Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName","Voice Flow");key.SetValue("DisplayVersion","0.3.3");key.SetValue("Publisher","Abdulla Al Maruf");key.SetValue("InstallLocation",InstallRoot);key.SetValue("DisplayIcon",InstalledExe);
            key.SetValue("UninstallString","\""+Path.Combine(InstallRoot,"VoiceFlow.Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
            key.SetValue("EstimatedSize",(int)(Directory.GetFiles(InstallRoot,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length)/1024),RegistryValueKind.DWord);
        }
        Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Voice Flow.lnk"),false);
        if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Voice Flow.lnk"),false);
    }
    static void Shortcut(string file,bool remove)
    {
        Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type),link=null;
        try
        {
            link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});Type linkType=link.GetType();
            if(remove){string target=Convert.ToString(linkType.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));if(string.Equals(target,InstalledExe,StringComparison.OrdinalIgnoreCase))File.Delete(file);return;}
            foreach(var property in new[]{new[]{"TargetPath",InstalledExe},new[]{"WorkingDirectory",InstallRoot},new[]{"IconLocation",InstalledExe+",0"},new[]{"Description","Voice Flow"}})linkType.InvokeMember(property[0],BindingFlags.SetProperty,null,link,new object[]{property[1]});
            linkType.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
        }
        finally{if(link!=null)Marshal.ReleaseComObject(link);Marshal.ReleaseComObject(shell);}
    }
    static string Literal(string value){return "'"+value.Replace("'","''")+"'";}
    internal static string CleanupScript(int pid,bool deleteHistory)
    {
        return "$ErrorActionPreference = 'Stop'\r\n"+
            "$wait = Get-Process -Id "+pid+" -ErrorAction SilentlyContinue\r\nif ($wait) { $wait.WaitForExit() }\r\n"+
            "$target = [IO.Path]::GetFullPath("+Literal(InstallRoot)+")\r\n"+
            "$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\\Voice Flow'))\r\n"+
            "if ($target -ne $expected) { throw 'Unexpected uninstall path' }\r\n"+
            "if ((Get-Content -LiteralPath (Join-Path $target 'install.id') -Raw) -ne "+Literal(Marker)+") { throw 'Installation marker mismatch' }\r\n"+
            "Remove-Item -LiteralPath $target -Recurse -Force\r\n"+
            "$data = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'VoiceFlow'))\r\n"+
            "if ($data -ne "+Literal(Path.GetFullPath(AppPaths.Data))+") { throw 'Unexpected data path' }\r\n"+
            (deleteHistory?"if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }\r\n":"if (Test-Path -LiteralPath $data) { Get-ChildItem -LiteralPath $data -Force | Where-Object { $_.Name -ne 'History' } | ForEach-Object { $child = [IO.Path]::GetFullPath($_.FullName); if (-not $child.StartsWith($data + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected data child' }; Remove-Item -LiteralPath $child -Recurse -Force } }\r\n")+
            "Remove-Item -LiteralPath $PSCommandPath -Force\r\n";
    }
    internal static void Uninstall(bool deleteHistory)
    {
        if(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\')!=Path.GetFullPath(InstallRoot).TrimEnd('\\')||!File.Exists(Path.Combine(InstallRoot,"install.id"))||File.ReadAllText(Path.Combine(InstallRoot,"install.id"))!=Marker)throw new InvalidOperationException("ইনস্টল করা অ্যাপের uninstall ব্যবহার করুন।");
        if(!StopInstalled())throw new InvalidOperationException("আগে চলমান কথাটি শেষ করুন। এরপর আবার uninstall করুন।");
        using(var run=Registry.CurrentUser.OpenSubKey(RunKey,true)){if(run!=null&&Convert.ToString(run.GetValue("VoiceFlow")).Contains(InstalledExe))run.DeleteValue("VoiceFlow",false);}
        Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Voice Flow.lnk"),true);Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Voice Flow.lnk"),true);
        using(var key=Registry.CurrentUser.OpenSubKey(UninstallKey))if(key!=null&&!string.Equals(Convert.ToString(key.GetValue("InstallLocation")),InstallRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Uninstall registration mismatch");
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey,false);SecretStore.Clear();
        string script=Path.Combine(Path.GetTempPath(),"VoiceFlow-cleanup-"+Guid.NewGuid().ToString("N")+".ps1");File.WriteAllText(script,CleanupScript(Process.GetCurrentProcess().Id,deleteHistory),new UTF8Encoding(true));
        string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
        Process.Start(new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
    }
    delegate bool WindowCallback(IntPtr window,IntPtr ignored);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr ignored);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder title,int count);
    [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr answer);
    static bool CloseSafely(int id)
    {
        IntPtr found=IntPtr.Zero;
        EnumWindows(delegate(IntPtr window,IntPtr ignored){uint pid;Native.GetWindowThreadProcessId(window,out pid);if(pid!=id)return true;var title=new StringBuilder(256);GetWindowText(window,title,title.Capacity);if(title.ToString()=="VoiceFlow.State:idle"||title.ToString()=="VoiceFlow.State:error")found=window;return true;},IntPtr.Zero);
        if(found==IntPtr.Zero)return false;IntPtr answer;return SendMessageTimeout(found,0x8051,(IntPtr)0x4156,IntPtr.Zero,2,3000,out answer)!=IntPtr.Zero&&answer==(IntPtr)1;
    }
    static int SelfTest()
    {
        string root=Path.Combine(Program.Root,"tests","setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(Program.Root,"assets"));
        try
        {
            foreach(string bad in new[]{"../escape","../../Windows/system.ini","C:/escape","/absolute","folder/../../../escape"}){bool rejected=false;try{Inside(root,bad);}catch{rejected=true;}if(!rejected)throw new Exception("Archive traversal accepted");}
            using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))if(payload!=null)Extract(payload,root);
            string link=Path.Combine(root,"Voice Flow.lnk");Shortcut(link,false);if(!File.Exists(link))throw new Exception("Shortcut creation failed");Shortcut(link,true);if(File.Exists(link))throw new Exception("Shortcut removal failed");
            using(var preview=new SetupWindow(false)){var handle=preview.Handle;using(var image=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(image,new Rectangle(Point.Empty,preview.Size));image.Save(Path.Combine(Program.Root,"assets","installer-preview.png"));}}
            string script=CleanupScript(123,false);if(!script.Contains("Unexpected uninstall path")||!script.Contains("Installation marker mismatch")||!script.Contains("Unexpected data child"))throw new Exception("Cleanup guards absent");
            File.WriteAllText(Path.Combine(Program.Root,"tests","setup-results.txt"),"PASS: archive path traversal rejection, packaged file checksums when embedded, required runtime/helper files, native installer preview and uninstall path/marker guards. No live app, registry, shortcuts, microphone, sign-in or user data changed.");return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Program.Root,"tests","setup-results.txt"),"FAIL: "+e);return 1;}
        finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Program.Root,"tests"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
    }
}

internal sealed class SetupWindow : Form
{
    readonly Label title=new Label(),body=new Label(),note=new Label();
    readonly CheckBox option=new CheckBox(),desktop=new CheckBox();
    readonly VoiceButton action=new VoiceButton{Primary=true,Symbol="wave"};
    readonly bool uninstall;
    bool working,done;
    internal SetupWindow(bool remove)
    {
        uninstall=remove;Text=remove?"Voice Flow সরান":"Voice Flow ইনস্টল";ClientSize=new Size(550,410);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Theme.Window(this);
        title.Text=remove?"Voice Flow সরিয়ে ফেলবেন?":"Voice Flow";title.Font=Theme.Font(23);title.SetBounds(32,30,486,51);
        body.Text=remove?"অ্যাপ, shortcuts ও স্বয়ংক্রিয় চালু হওয়া সরবে।\nআপনার sign-in-ও মুছে যাবে।":"ইনস্টল করুন, সাইন ইন করুন, কথা বলে লিখুন।\nপ্রয়োজনীয় runtime সঙ্গে দেওয়া আছে।";body.SetBounds(32,105,486,65);body.ForeColor=Theme.Muted;
        option.Text=remove?"এই কম্পিউটারের রাখা লেখা ও অডিও মুছে দিন":"Windows-এর সঙ্গে চালু করুন";option.Checked=true;option.SetBounds(32,188,486,35);
        desktop.Text="Desktop-এ shortcut রাখুন";desktop.Checked=true;desktop.SetBounds(32,228,486,35);desktop.Visible=!remove;
        action.Text=remove?"Uninstall করুন":"ইনস্টল করুন";action.SetBounds(32,302,486,48);action.Click+=Run;
        note.Text=remove?"শুধু Voice Flow-এর নিজের ফাইল সরানো হবে।":"ব্যক্তিগত preview · public service অনুমতি যাচাই বাকি";note.Font=Theme.Font(9);note.ForeColor=Theme.Muted;note.SetBounds(32,368,486,29);
        Controls.AddRange(new Control[]{title,body,option,desktop,action,note});FormClosing+=delegate(object sender,FormClosingEventArgs e){if(working)e.Cancel=true;};var handle=Handle;foreach(Control control in Controls){var child=control.Handle;}
    }
    async void Run(object sender,EventArgs args)
    {
        if(done){if(!uninstall)Process.Start(new ProcessStartInfo(SetupProgram.InstalledExe,"--signin"){WorkingDirectory=SetupProgram.InstallRoot,UseShellExecute=false});Close();return;}
        bool selected=option.Checked,shortcut=desktop.Checked;working=true;option.Enabled=desktop.Enabled=action.Enabled=false;body.Text=uninstall?"অ্যাপ নিরাপদে বন্ধ করে সরানো হচ্ছে…":"প্রয়োজনীয় ফাইল বসানো হচ্ছে…";
        try
        {
            if(uninstall)SetupProgram.Uninstall(selected);else await Task.Run(()=>SetupProgram.Install(selected,shortcut));
            done=true;body.Text=uninstall?"Voice Flow সরানো হচ্ছে। এই উইন্ডো বন্ধ করলে কাজ শেষ হবে।":"ইনস্টল হয়েছে। নিজের account দিয়ে সাইন ইন করে শুরু করুন।";action.Text=uninstall?"বন্ধ করুন":"Voice Flow খুলুন";option.Visible=desktop.Visible=false;
        }
        catch(Exception error){body.Text=error is InvalidOperationException?error.Message:"কাজটি সম্পূর্ণ হয়নি। আবার চেষ্টা করুন; আপনার রেকর্ডিং বদলানো হয়নি।";Diagnostics.Error("event",error);}
        finally{working=false;action.Enabled=true;if(!done)option.Enabled=desktop.Enabled=true;}
    }
}
