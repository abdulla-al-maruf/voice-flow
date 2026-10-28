using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class ConfirmedDelivery
{
    internal static async Task<string> Send(string text,bool automatic,Action<string> copy,Func<Task<bool>> paste)
    {
        try{copy(text);}catch{Diagnostics.Event("clipboard-failed");return "unavailable";}
        if(automatic)try{if(await paste())return "pasted";}catch{ }
        Diagnostics.Event("paste-fallback");return "clipboard";
    }
}

internal sealed class VoiceFlowApp : ApplicationContext
{
    readonly Hotkeys hotkeys=new Hotkeys();
    readonly Pill pill=new Pill();
    readonly ControlPanel panel=new ControlPanel();
    readonly AudioPipe audio=new AudioPipe(false);
    readonly HistoryStore history=new HistoryStore(AppPaths.History);
    Backend backend;
    HistoryWindow historyWindow;
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem heading,toggle,cancel,quit,historyMenu;
    readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer{Interval=100};
    readonly Icon idleIcon=Visuals.Icon("idle"),recordingIcon=Visuals.Icon("recording"),finishingIcon=Visuals.Icon("finishing"),errorIcon=Visuals.Icon("error");
    readonly Stopwatch duration=new Stopwatch();
    TaskCompletionSource<bool> connected;
    TaskCompletionSource<Dictionary<string,object>> completed;
    string mode="idle",last="",draft="",shortcut="ট্রে থেকে খুলুন",authorizationUrl="",captureId;
    DateTime captureCreated;
    HistoryEntry retryRecording;
    bool ready,helperReady,quitting,manual,preferred,resumePending,offlineResume,fallbackUsed;
    string sessionEngine=EngineChoice.Default;
    int session,ticks,historyCount;
    long checkpointOffset;
    IntPtr target,targetFocus;
    uint targetPid;
    byte[] unsavedAudio;
    string unsavedId,unsavedText,unsavedStatus;
    DateTime unsavedCreated;
    internal VoiceFlowApp(bool signInAtStartup=false)
    {
        history.Recover();historyCount=history.List().Length;
        Program.BeforeFatal=delegate{audio.StopCapture();Checkpoint();try{history.Flush().Wait(2000);}catch{ }};
        hotkeys.Pressed+=delegate(int id){if(id==2)Cancel();else if(id==1||id==10)Toggle(false);};
        pill.Level=delegate{return audio.Level;};pill.ExitIfIdle=SafeExit;
        var menu=new ContextMenuStrip{ShowImageMargin=false};
        heading=new ToolStripMenuItem("Voice Flow"){Enabled=false,Tag="heading"};
        toggle=new ToolStripMenuItem("কথা বলে দেখুন",null,delegate{Toggle(true);});
        historyMenu=new ToolStripMenuItem("সাম্প্রতিক কথা",null,delegate{ShowHistory();});
        cancel=new ToolStripMenuItem("এই কথা বাতিল",null,delegate{Cancel();});
        quit=new ToolStripMenuItem("অ্যাপ বন্ধ করুন",null,delegate{Quit();});
        menu.Items.AddRange(new ToolStripItem[]{heading,new ToolStripSeparator(),new ToolStripMenuItem("Voice Flow খুলুন",null,delegate{panel.Open();}),toggle,cancel,historyMenu,new ToolStripMenuItem("সমস্যা জানান",null,delegate{Report();}),new ToolStripSeparator(),quit});
        MenuStyle.Apply(menu);tray=new NotifyIcon{Icon=idleIcon,Text="Voice Flow",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{panel.Open();};
        panel.SignIn+=SignIn;panel.Speak+=delegate{Toggle(true);};panel.BrowserSignIn+=OpenSignInBrowser;panel.ShowHistory+=ShowHistory;panel.Report+=Report;panel.ExportLogs+=ExportLogs;
        panel.Updates+=delegate{if(!AppLinks.Open(true))panel.Notice("প্রকাশের পরে এখান থেকে আপডেট পাওয়া যাবে।");};panel.SignOut+=SignOut;panel.Quit+=Quit;panel.EngineChanged+=ChooseEngine;panel.EngineAction+=EngineAction;panel.EngineLink+=delegate{AppLinks.OpenFixed(EngineChoice.KeyPage(EngineChoice.Current));};
        RegisterShortcut();SetMode("idle");refresh.Tick+=Refresh;refresh.Start();StartBackend();
        var startup=new System.Windows.Forms.Timer{Interval=400};
        startup.Tick+=delegate
        {
            if(Native.OriginalVoiceBusy()||!helperReady||resumePending)return;
            startup.Stop();startup.Dispose();if(quitting)return;
            if(!EngineUsable()&&!offlineResume||signInAtStartup)panel.Open();if(signInAtStartup&&!ready)SignIn();
        };startup.Start();Diagnostics.Event("startup");
    }
    bool Busy{get{return mode=="recording"||mode=="finishing"||mode=="signin";}}
    void Post(Action action){if(quitting||panel.IsDisposed)return;try{panel.BeginInvoke(action);}catch(InvalidOperationException){}}
    void StartBackend()
    {
        if(backend!=null)backend.Dispose();var own=new Backend();backend=own;helperReady=false;
        own.Message+=delegate(Dictionary<string,object> data){Post(delegate{if(backend==own)OnMessage(data);});};
        own.Stopped+=delegate{Post(delegate{if(backend!=own)return;helperReady=ready=false;Diagnostics.Event("helper-stopped");if(mode=="recording"||mode=="finishing")Fail();else{SetMode("error");panel.SignInIssue="ভয়েস সংযোগ বন্ধ হয়েছে। আবার সংযোগ করুন।";Update();}});};
        own.Start();
    }
    void RegisterShortcut()
    {
        if(Native.RegisterHotKey(hotkeys.Handle,1,0x4002,0x20)){shortcut="Ctrl+Space";preferred=true;return;}
        if(Native.RegisterHotKey(hotkeys.Handle,1,0x4003,0x20)){shortcut="Ctrl+Alt+Space";return;}
        if(Native.RegisterHotKey(hotkeys.Handle,1,0x4007,0x20))shortcut="Ctrl+Shift+Alt+Space";
    }
    void SignIn()
    {
        if(Busy)return;
        if(!helperReady){try{StartBackend();}catch{panel.Notice("ভয়েস সংযোগ শুরু হয়নি। লগ সংরক্ষণ করে জানান।");}return;}
        ready=false;authorizationUrl="";panel.ShowSignInLink(false);panel.SignInIssue="";SetMode("signin");
        try{backend.Send(new{type="signin"});}catch{SetMode("error");Problem("সাইন-ইন শুরু হয়নি। আবার চেষ্টা করুন।");}
    }
    void SignOut(){if(Busy)return;try{SecretStore.Clear();backend.Send(new{type="signout"});ready=false;SetMode("idle");}catch{panel.Notice("সাইন আউট সম্পূর্ণ হয়নি। আবার চেষ্টা করুন।");}}
    internal static bool IsGoogleSignInUrl(string value)
    {
        Uri url;return Uri.TryCreate(value,UriKind.Absolute,out url)&&url.Scheme=="https"&&url.Host=="accounts.google.com"&&url.Port==443&&url.UserInfo.Length==0&&url.AbsolutePath=="/o/oauth2/v2/auth";
    }
    void OpenSignInBrowser(){if(mode!="signin"||!IsGoogleSignInUrl(authorizationUrl))return;try{Process.Start(new ProcessStartInfo(authorizationUrl){UseShellExecute=true});Diagnostics.Event("signin-browser");}catch{panel.Notice("ব্রাউজার খুলতে নিচের সাইন-ইন লিংকে চাপুন।");}}
    async void Toggle(bool testInPanel)
    {
        if(quitting)return;if(mode=="recording"){await Finish(false);return;}if(Busy)return;
        if(unsavedAudio!=null&&!await SavePending()){Problem("অডিওটি রাখতে জায়গা পাওয়া যাচ্ছে না। ডিস্কে জায়গা খালি করে আবার চেষ্টা করুন।");return;}
        if(!helperReady||!EngineUsable())
        {
            // A stopped helper or a startup without internet reconnects quietly; only a real sign-out needs the panel.
            if(!helperReady){try{StartBackend();pill.Set("আবার সংযোগ হচ্ছে",Pill.Amber,1500);}catch{panel.Open();}return;}
            if(EngineChoice.Current=="google"&&offlineResume&&!resumePending&&ResumeSaved()){pill.Set("আবার সংযোগ হচ্ছে",Pill.Amber,1500);return;}
            panel.Open();return;
        }
        manual=testInPanel;target=Native.GetForegroundWindow();uint thread=Native.GetWindowThreadProcessId(target,out targetPid);
        var info=new Native.GUITHREADINFO{cbSize=Marshal.SizeOf(typeof(Native.GUITHREADINFO))};targetFocus=Native.GetGUIThreadInfo(thread,ref info)?info.hwndFocus:IntPtr.Zero;
        if(targetPid==(uint)Process.GetCurrentProcess().Id)manual=true;pill.Area=Screen.FromHandle(target).WorkingArea;
        try
        {
            ++session;draft="";retryRecording=null;fallbackUsed=false;sessionEngine=EngineChoice.Current;captureId=Guid.NewGuid().ToString("N");captureCreated=DateTime.UtcNow;checkpointOffset=0;
            connected=new TaskCompletionSource<bool>();completed=new TaskCompletionSource<Dictionary<string,object>>();
            Diagnostics.Phase("capture");audio.StartCapture(true);duration.Restart();SetMode("recording");pill.Set("শুনছি",Pill.Red,0);backend.Send(StartMessage(sessionEngine));
        }catch(Exception error){Diagnostics.Error("session-incomplete",error);Fail();}
    }
    void Checkpoint()
    {
        if(captureId==null||retryRecording!=null)return;byte[] delta=audio.Since(checkpointOffset);checkpointOffset+=delta.Length;
        history.Checkpoint(captureId,captureCreated,delta).ContinueWith(task=>{if(task.IsFaulted){var observed=task.Exception;Diagnostics.Event("history-save-failed");}});
    }
    async Task Persist(string text,string status)
    {
        Checkpoint();
        byte[] pcm=audio.Recording;string id=captureId;DateTime created=captureCreated;captureId=null;retryRecording=null;
        if(pcm.Length==0)return;
        try{await history.Save(id,created,text,status,pcm);historyCount=history.List().Length;}
        catch{unsavedAudio=pcm;unsavedId=id;unsavedCreated=created;unsavedText=text;unsavedStatus=status;Diagnostics.Event("history-save-failed");Problem("রেকর্ডিং মেমোরিতে আছে; ডিস্কে সংরক্ষণ হয়নি। জায়গা খালি করুন।");}
    }
    // Nothing said: a silent press must not take one of the 20 history places. Audio with sound stays retryable.
    async Task KeepEmpty()
    {
        if(retryRecording!=null){captureId=null;retryRecording=null;return;}
        if(audio.HasSound){await Persist("","empty");return;}
        string id=captureId;captureId=null;
        if(id==null)return;
        Task deleting=history.Delete(id).ContinueWith(t=>{if(t.IsFaulted){var observed=t.Exception;Diagnostics.Event("history-save-failed");}});
    }
    async Task<bool> SavePending()
    {
        try{await history.Save(unsavedId,unsavedCreated,unsavedText,unsavedStatus,unsavedAudio);unsavedAudio=null;historyCount=history.List().Length;return true;}catch{return false;}
    }
    // fallback: the same recording is being re-read by the local model after Google failed, so the result is pasted like a live one.
    async Task Finish(bool recovery,bool fallback=false,string fallbackText=null)
    {
        int current=session;SetMode("finishing");pill.Set(fallback?(fallbackText??"লোকাল মডেলে লেখা হচ্ছে"):recovery?"লেখা উদ্ধার হচ্ছে":"লেখা সম্পূর্ণ হচ্ছে",Pill.Amber,0,draft);
        try
        {
            Diagnostics.Phase("tail");if(!recovery)await audio.CaptureTail();if(current!=session||quitting)return;
            Diagnostics.Phase("connect");if(await Task.WhenAny(connected.Task,Task.Delay(8000))!=connected.Task)throw new TimeoutException();await connected.Task;Diagnostics.Phase("drain");await audio.Drain();if(current!=session||quitting)return;
            backend.Send(new{type="stop",id=current});audio.EndSession();Diagnostics.Phase("cloud");// A healthy service answers in about 2 s; allow extra time only for long recordings (0.1 s per recorded second).
            if(await Task.WhenAny(completed.Task,Task.Delay(12000+(int)(audio.CapturedBytes/320)))!=completed.Task)throw new TimeoutException();
            var result=await completed.Task;if(current!=session||quitting)return;
            bool ok=result.ContainsKey("ok")&&Convert.ToBoolean(result["ok"]);long sent=result.ContainsKey("sentBytes")?Convert.ToInt64(result["sentBytes"]):-1;
            string text=Value(result,"text").Trim();bool empty=result.ContainsKey("empty")&&Convert.ToBoolean(result["empty"]);
            if(empty&&sent==audio.CapturedBytes&&Value(result,"grpcStatus")=="0"&&text.Length==0&&draft.Trim().Length==0)
            {await KeepEmpty();duration.Stop();draft="";audio.StartCapture(false);SetMode("idle");pill.Dismiss();Diagnostics.Event("session-empty");return;}
            if(!ok||sent!=audio.CapturedBytes||text.Length==0)throw new IOException("Incomplete result");
            last=text;draft="";duration.Stop();Diagnostics.Phase("delivery");string delivery=await ConfirmedDelivery.Send(text,!manual&&(!recovery||fallback),SetClipboard,Paste);
            Diagnostics.Phase("history");await Persist(text,delivery=="clipboard"?"clipboard":"complete");audio.StartCapture(false);SetMode("idle");
            if(delivery=="pasted")pill.Set("লেখা বসেছে",Pill.Green,550);else pill.Dismiss();
            if(delivery=="unavailable")Problem("লেখা পাওয়া গেছে, কিন্তু clipboard-এ রাখা যায়নি। সাম্প্রতিক কথা থেকে কপি করুন।");
            Diagnostics.Counts("session-complete",sent,text.Length);
        }catch(Exception error){Diagnostics.Error("session-incomplete",error);if(current==session&&!quitting)Fail();}
    }
    void OnMessage(Dictionary<string,object> data)
    {
        string type=Value(data,"type");
        if(type=="boot")
        {
            helperReady=true;if(!ResumeSaved())Update();return;
        }
        if(type=="credentials")
        {
            try{if(data.ContainsKey("clear"))SecretStore.Clear();else if(data.ContainsKey("bundle"))SecretStore.Save(new JavaScriptSerializer().Serialize(data["bundle"]));}catch{Diagnostics.Event("signin-save-failed");panel.Notice("এইবার সাইন-ইন মনে রাখা যায়নি।");}return;
        }
        if(type=="authorization"){if(mode!="signin"||!IsGoogleSignInUrl(Value(data,"url")))return;authorizationUrl=Value(data,"url");panel.ShowSignInLink(true);OpenSignInBrowser();return;}
        if(type=="auth")
        {resumePending=false;authorizationUrl="";panel.ShowSignInLink(false);ready=data.ContainsKey("ready")&&Convert.ToBoolean(data["ready"]);offlineResume=!ready&&data.ContainsKey("offline")&&Convert.ToBoolean(data["offline"]);if(data.ContainsKey("restored"))Diagnostics.Event("signin-restored");if(mode=="signin")SetMode("idle");else Update();return;}
        if(type=="signing-in"){SetMode("signin");return;}
        if(type=="error")
        {
            if(mode=="recording"||mode=="finishing")Fail();else
            {resumePending=false;string code=Value(data,"code");panel.SignInIssue=code=="client-missing"?"এই পরীক্ষার সংস্করণে সাইন-ইন অংশ পাওয়া যায়নি।":code=="client-unsupported"?"সাইন-ইন অংশের আপডেটের সঙ্গে সংযোগ মিলছে না।":code=="voice-unavailable"?"ভয়েস সেবা যুক্ত হয়নি। ইন্টারনেট ও account-এর অনুমতি দেখুন।":"সাইন-ইন শেষ হয়নি। আবার চেষ্টা করুন।";SetMode("error");Diagnostics.SignIn(code);}return;
        }
        if(!data.ContainsKey("id")||Convert.ToInt32(data["id"])!=session)return;
        if(type=="connected")connected.TrySetResult(true);else if(type=="text"){draft=Value(data,"text");if(mode=="recording"||mode=="finishing")pill.Preview(draft);}
        else if(type=="result"){if(mode!="finishing"){Fail();return;}completed.TrySetResult(data);}else if(type=="session-error")Fail();
    }
    bool downloading;
    int downloadPercent;
    // Google needs its sign-in; the other engines need their downloaded model or their saved key.
    bool EngineUsable(){string name=EngineChoice.Current;return name=="google"?ready:EngineChoice.Ready(name);}
    object StartMessage(string engine){return new{type="start",id=session,address=audio.Address,engine=engine,apiKey=EngineChoice.Paid.Contains(engine)?KeyStore.Get(engine):null};}
    void ChooseEngine(string name){if(Busy)return;try{EngineChoice.Current=name;}catch{Update();panel.Notice("ইঞ্জিন বদলানো যায়নি।");return;}Update();}
    void EngineAction()
    {
        if(Busy)return;string name=EngineChoice.Current;
        if(EngineChoice.Paid.Contains(name))
        {
            string key=KeyDialog.Ask(panel,name,KeyStore.Has(name));if(key==null)return;
            try{if(key.Length==0)KeyStore.Remove(name);else if(KeyStore.Valid(key))KeyStore.Save(name,key);else{panel.Notice("চাবিটি ঠিক মনে হচ্ছে না। আবার দেখুন।");return;}}
            catch{Update();panel.Notice("চাবি সংরক্ষণ করা যায়নি।");return;}
            Update();return;
        }
        if(!EngineChoice.LocalReady&&!downloading)DownloadModel();
    }
    void DownloadModel()
    {
        downloading=true;downloadPercent=0;Update();
        Task.Run(delegate{ModelDownload.Run(EngineChoice.LocalModel,delegate(int percent){Post(delegate{if(percent!=downloadPercent){downloadPercent=percent;Update();}});});}).ContinueWith(task=>Post(delegate
        {
            downloading=false;var observed=task.Exception;Update();
            if(task.IsFaulted){Diagnostics.Event("model-download-failed");panel.Notice("মডেল নামানো যায়নি। ইন্টারনেট দেখে আবার চেষ্টা করুন।");}
        }));
    }
    // The engine block: a status chip, plain numbered steps for what is missing, at most two buttons (get key, then paste it).
    void UpdateEngineArea()
    {
        string name=EngineChoice.Current,note,action=null,get=null,state;bool enabled=true,good,local=EngineChoice.LocalReady;
        string percent=downloading?"নামানো হচ্ছে… "+downloadPercent+"%":null;
        if(name=="google")
        {
            good=ready;state=ready?"✓ প্রস্তুত":"সাইন-ইন বাকি";
            note=(ready?"Google সাইন-ইন আছে।":"১. ওপরের হলুদ বোতামে চাপুন, Google দিয়ে সাইন ইন করুন।")+"\n"+(local?"Google না চললে নিজে থেকে লোকাল মডেলে লেখা হবে।":"ইন্টারনেট ছাড়া ব্যাকআপ চাইলে নিচের বোতামে একবার লোকাল মডেল (প্রায় ৭১ MB) নামিয়ে রাখুন।");
            if(!local){action=percent??"লোকাল মডেল নামান";enabled=!downloading;}
        }
        else if(name=="local")
        {
            good=local;state=local?"✓ প্রস্তুত":"মডেল নামানো বাকি";
            if(local)note="মডেল নামানো আছে। ইন্টারনেট ছাড়া চলে, অডিও এই কম্পিউটার ছেড়ে যায় না।\nকথা শেষ হলে লেখা একবারে আসে।";
            else{note="ইন্টারনেট ছাড়া চলবে।\n১. \"মডেল নামান\" চাপুন (প্রায় ৭১ MB, একবারই, নামানোর সময় ইন্টারনেট লাগবে)।\n২. নামা শেষ হলে আর কিছু করতে হবে না।";action=percent??"মডেল নামান";enabled=!downloading;}
        }
        else
        {
            string company=EngineChoice.Company(name);bool has=KeyStore.Has(name);good=has;state=has?"✓ চাবি আছে":"চাবি বাকি";
            if(has){note="চাবি সংরক্ষিত আছে, কথা বলতে প্রস্তুত।\nঅডিও "+company+"-এর সার্ভারে যায়, খরচ আপনার account-এ।";action="চাবি বদলান";}
            else{note="১. \"চাবি নিন\" চাপুন, "+company+"-এর সাইন-ইন পাতা খুলবে।\n২. সেখানে নতুন API key বানিয়ে কপি করুন।\n৩. \"চাবি বসান\" চেপে পেস্ট করুন। কাজ শেষ।\nঅডিও "+company+"-এর সার্ভারে যায়, খরচ আপনার account-এ।";action="চাবি বসান";get="চাবি নিন";}
        }
        panel.UpdateEngine(name,note,action,enabled,get,state,good);
    }
    bool ResumeSaved()
    {
        if(Edition.Public)return false;string stored=SecretStore.Load();if(stored==null)return false;
        try{var saved=new JavaScriptSerializer().DeserializeObject(stored);resumePending=true;SetMode("signin");panel.Notice("আগের সাইন-ইন যাচাই হচ্ছে…");backend.Send(new{type="resume",bundle=saved});return true;}
        catch{resumePending=false;SetMode("idle");return false;}
    }
    static string Value(Dictionary<string,object> data,string key){return data.ContainsKey(key)&&data[key]!=null?Convert.ToString(data[key]):"";}
    async void Fail()
    {
        if(quitting||captureId==null)return;int old=session++;duration.Stop();audio.StopCapture();audio.ExpectDisconnect();try{backend.Send(new{type="cancel",id=old});}catch{ }
        audio.EndSession();if(connected!=null)connected.TrySetCanceled();if(completed!=null)completed.TrySetCanceled();SetMode("finishing");
        if(!fallbackUsed&&retryRecording==null&&sessionEngine!="local"&&EngineChoice.LocalReady&&backend!=null&&backend.Alive&&audio.HasSound){await FallbackLocal();return;}
        // A failed retry keeps the entry exactly as it was before the retry.
        if(retryRecording!=null){captureId=null;retryRecording=null;}else await Persist(draft,"interrupted");draft="";audio.StartCapture(false);SetMode("idle");pill.Dismiss();Diagnostics.Event("session-incomplete");Problem("কথাটি সম্পূর্ণ হয়নি। অডিও সাম্প্রতিক কথায় রাখা আছে।");
    }
    // Google failed: give the same recording to the offline model once. If that fails too, Fail() keeps the audio as before.
    async Task FallbackLocal()
    {
        string failed=sessionEngine;fallbackUsed=true;sessionEngine="local";byte[] recording=audio.Recording;++session;draft="";
        connected=new TaskCompletionSource<bool>();completed=new TaskCompletionSource<Dictionary<string,object>>();
        audio.StartCapture(false);audio.Append(recording);Diagnostics.Event("fallback-local");
        try{backend.Send(StartMessage("local"));}catch{Fail();return;}
        await Finish(true,true,EngineChoice.Paid.Contains(failed)?EngineChoice.Company(failed)+"-এ কাজ হয়নি (চাবি বা ক্রেডিট দেখুন)। লোকাল মডেলে লেখা হচ্ছে":null);
    }
    async void Recover(HistoryEntry entry)
    {
        if(Busy||!EngineUsable()||!helperReady)return;try
        {
            byte[] recording=history.Audio(entry.Id);++session;manual=true;draft="";retryRecording=entry;captureId=entry.Id;captureCreated=entry.Created;
            connected=new TaskCompletionSource<bool>();completed=new TaskCompletionSource<Dictionary<string,object>>();audio.StartCapture(false);audio.Append(recording);sessionEngine=EngineChoice.Current;backend.Send(StartMessage(sessionEngine));await Finish(true);
        }catch{if(captureId!=null)Fail();else Problem("রেকর্ডিংটি খোলা যায়নি।");}
    }
    void Cancel()
    {
        if(mode!="recording")return;int old=session++;audio.ExpectDisconnect();audio.EndSession();duration.Stop();try{backend.Send(new{type="cancel",id=old});}catch{ }
        if(connected!=null)connected.TrySetCanceled();if(completed!=null)completed.TrySetCanceled();
        if(captureId!=null&&retryRecording==null)history.Delete(captureId).ContinueWith(t=>{if(t.IsFaulted){var observed=t.Exception;Diagnostics.Event("history-save-failed");}});
        captureId=null;retryRecording=null;draft="";audio.StartCapture(false);SetMode("idle");pill.Dismiss();Diagnostics.Event("session-cancelled");
    }
    void Refresh(object sender,EventArgs args)
    {
        ticks++;
        if(mode=="recording")
        {if(audio.Error!=null||backend==null||!backend.Alive){Fail();return;}if(ticks%10==0)Checkpoint();if(duration.ElapsedMilliseconds>=300000){Finish(false).ContinueWith(delegate{});return;}}
        if(ticks%20==0&&!preferred&&mode=="idle"&&Native.RegisterHotKey(hotkeys.Handle,10,0x4002,0x20)){Native.UnregisterHotKey(hotkeys.Handle,1);preferred=true;shortcut="Ctrl+Space";Update();}
    }
    async Task<bool> Paste()
    {
        uint pid;uint thread=Native.GetWindowThreadProcessId(target,out pid);var info=new Native.GUITHREADINFO{cbSize=Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
        // shortcut: browser fields may share a native HWND; keep the caret in the intended field until paste.
        if(target==IntPtr.Zero||!Native.IsWindow(target)||targetPid!=pid||Native.GetForegroundWindow()!=target||!Native.GetGUIThreadInfo(thread,ref info)||targetFocus!=IntPtr.Zero&&info.hwndFocus!=targetFocus)return false;
        var timer=Stopwatch.StartNew();while(ModifiersHeld()&&timer.ElapsedMilliseconds<1200)await Task.Delay(25);if(ModifiersHeld()||Native.GetForegroundWindow()!=target)return false;
        var keys=new[]{Native.Key(0x11,false),Native.Key(0x56,false),Native.Key(0x56,true),Native.Key(0x11,true)};bool inserted=Native.SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(Native.INPUT)))==keys.Length;
        if(!inserted){var release=new[]{Native.Key(0x56,true),Native.Key(0x11,true)};Native.SendInput((uint)release.Length,release,Marshal.SizeOf(typeof(Native.INPUT)));}return inserted;
    }
    static bool ModifiersHeld(){return Native.GetAsyncKeyState(0x11)<0||Native.GetAsyncKeyState(0x10)<0||Native.GetAsyncKeyState(0x12)<0;}
    static void SetClipboard(string text){for(int i=0;;i++)try{Clipboard.SetText(text,TextDataFormat.UnicodeText);return;}catch(ExternalException){if(i>=7)throw;Thread.Sleep(40);}}
    void ShowHistory()
    {
        if(Busy)return;if(historyWindow!=null&&!historyWindow.IsDisposed){historyWindow.Activate();return;}
        historyWindow=new HistoryWindow(history){CanRetry=EngineUsable()&&helperReady};historyWindow.Reload();historyWindow.Copy+=delegate(string text){try{SetClipboard(text);}catch{Problem("কপি করা যায়নি। আবার চেষ্টা করুন।");}};
        historyWindow.Retry+=Recover;historyWindow.Changed+=delegate{historyCount=history.List().Length;Update();};historyWindow.Show(panel);
    }
    void Report(){if(!AppLinks.Published){panel.Notice("GitHub repository প্রকাশের পরে এই লিংক চালু হবে। এখন সহায়তা মেনু থেকে লগ রাখা যাবে।");return;}if(!AppLinks.Open(false))panel.Notice("ব্রাউজার খোলা যায়নি। রিপোর্টের লিংক: "+AppLinks.IssueUrl);}
    void ExportLogs(){try{using(var save=new SaveFileDialog{Filter="Diagnostic log (*.txt)|*.txt",FileName="VoiceFlow-diagnostics.txt"})if(save.ShowDialog(panel)==DialogResult.OK)File.WriteAllText(save.FileName,Diagnostics.Summary(),Program.Utf8);}catch{panel.Notice("লগ সংরক্ষণ করা যায়নি।");}}
    void SetMode(string value){mode=value;pill.Text="VoiceFlow.State:"+(value=="idle"&&unsavedAudio!=null?"idle-recovery":value);Diagnostics.State(value);Native.UnregisterHotKey(hotkeys.Handle,2);if(mode=="recording")Native.RegisterHotKey(hotkeys.Handle,2,0x4000,0x1B);Update();}
    void Update()
    {
        if(quitting)return;panel.GoogleReady=ready;panel.Engine=EngineChoice.Current;panel.UpdateState(mode,shortcut,EngineUsable(),helperReady,"",false,true,historyCount);UpdateEngineArea();
        heading.Text=mode=="recording"?"শুনছি":mode=="finishing"?"লেখা আসছে":EngineUsable()?"প্রস্তুত · "+shortcut:EngineChoice.Current=="google"?"Google দিয়ে সাইন ইন করুন":"ইঞ্জিন তৈরি নয়";
        toggle.Enabled=EngineUsable()&&helperReady&&(mode=="idle"||mode=="error"||mode=="recording");toggle.Text=mode=="recording"?"থামিয়ে লেখা নিন":"কথা বলে দেখুন";toggle.ShortcutKeyDisplayString=shortcut;
        historyMenu.Enabled=!Busy;cancel.Visible=mode=="recording";quit.Enabled=!Busy;
        if(historyWindow!=null&&!historyWindow.IsDisposed)historyWindow.SetBusy(Busy,EngineUsable()&&helperReady);
        tray.Icon=mode=="recording"?recordingIcon:mode=="finishing"?finishingIcon:mode=="error"?errorIcon:idleIcon;tray.Text="Voice Flow · "+(mode=="recording"?"শুনছি":mode=="finishing"?"লেখা আসছে":shortcut);
    }
    void Problem(string message){tray.ShowBalloonTip(4500,"Voice Flow",message,ToolTipIcon.Warning);panel.Notice(message);}
    async void Quit()
    {
        if(Busy)return;
        if(unsavedAudio!=null&&!await SavePending())
        {
            if(MessageBox.Show(panel,"একটি রেকর্ডিং ডিস্কে রাখা যায়নি। বন্ধ করলে শুধু মেমোরিতে থাকা সেই অডিও মুছে যাবে। তবুও বন্ধ করবেন?","অসম্পূর্ণ সংরক্ষণ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            unsavedAudio=null;
        }
        try{await history.Flush();}catch{ }ExitThread();
    }
    bool SafeExit(){if(Busy||unsavedAudio!=null||!history.Flush().IsCompleted)return false;ExitThread();return true;}
    protected override void ExitThreadCore()
    {
        quitting=true;Program.BeforeFatal=null;++session;refresh.Stop();refresh.Dispose();Native.UnregisterHotKey(hotkeys.Handle,1);Native.UnregisterHotKey(hotkeys.Handle,2);Native.UnregisterHotKey(hotkeys.Handle,10);
        audio.Dispose();if(backend!=null)backend.Dispose();hotkeys.Dispose();pill.Dispose();if(historyWindow!=null)historyWindow.Dispose();tray.Visible=false;tray.Dispose();panel.AllowClose=true;panel.Close();panel.Dispose();
        idleIcon.Dispose();recordingIcon.Dispose();finishingIcon.Dispose();errorIcon.Dispose();Diagnostics.Event("shutdown");base.ExitThreadCore();
    }
}
