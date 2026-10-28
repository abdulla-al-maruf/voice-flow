using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class SelfTests
{
    static int checks;
    static void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Snapshot(Form form,string name)
    {
        var formHandle=form.Handle;form.PerformLayout();
        foreach(Control control in form.Controls){var handle=control.Handle;}
        using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(Program.Root,"assets",name+".png"));}
    }
    static void StorageChecks()
    {
        var store=new HistoryStore(AppPaths.History);string[] ids=Enumerable.Range(0,21).Select(i=>Guid.NewGuid().ToString("N")).ToArray();var when=DateTime.UtcNow;
        byte[] pcm=Encoding.UTF8.GetBytes("PRIVATE_AUDIO_SENTINEL_0123456789_");if(pcm.Length%2!=0)Array.Resize(ref pcm,pcm.Length+1);
        for(int i=0;i<21;i++)store.Save(ids[i],when.AddSeconds(i),"বাংলা English পরীক্ষা "+i,"complete",pcm).GetAwaiter().GetResult();
        Assert(store.List().Length==20,"History exceeded twenty");Assert(!File.Exists(Path.Combine(AppPaths.History,ids[0]+".vfh")),"Oldest audio survived eviction");
        var newest=store.List()[0];Assert(newest.Id==ids[20]&&store.Audio(newest.Id).SequenceEqual(pcm),"History order or audio roundtrip failed");
        var reopened=new HistoryStore(AppPaths.History);reopened.Recover();Assert(reopened.List().Length==20,"History did not survive restart");
        reopened.Save(newest.Id,newest.Created,"আবার পাওয়া লেখা","clipboard",pcm).GetAwaiter().GetResult();Assert(reopened.List().Length==20&&reopened.List()[0].Text=="আবার পাওয়া লেখা","Retry duplicated history");
        string interrupted=Guid.NewGuid().ToString("N");reopened.Checkpoint(interrupted,when.AddMinutes(1),pcm).GetAwaiter().GetResult();reopened.Checkpoint(interrupted,when.AddMinutes(1),pcm).GetAwaiter().GetResult();
        using(var torn=new FileStream(Path.Combine(AppPaths.History,interrupted+".journal"),FileMode.Append))torn.WriteByte(255);
        reopened.Recover();var recovered=reopened.List().First(x=>x.Id==interrupted);Assert(!recovered.Confirmed&&recovered.Status=="interrupted","Crash result was marked confirmed");Assert(reopened.Audio(interrupted).SequenceEqual(pcm.Concat(pcm)),"Torn final checkpoint destroyed complete earlier audio");
        Assert(reopened.List().Length==20&&!Directory.GetFiles(AppPaths.History,"*.journal").Any(),"Crash recovery bypassed cap or retained journal");
        string cancelled=Guid.NewGuid().ToString("N");reopened.Checkpoint(cancelled,when,pcm).GetAwaiter().GetResult();reopened.Delete(cancelled).GetAwaiter().GetResult();reopened.Recover();Assert(!reopened.List().Any(x=>x.Id==cancelled),"Cancelled recording reappeared after restart");
        foreach(string file in Directory.GetFiles(AppPaths.History)){string bytes=Encoding.UTF8.GetString(File.ReadAllBytes(file));Assert(!bytes.Contains("PRIVATE_AUDIO_SENTINEL")&&!bytes.Contains("বাংলা English পরীক্ষা"),"Private history was plaintext");}
        using(var preview=new HistoryWindow(reopened){CanRetry=true}){preview.Reload();Snapshot(preview,"history-preview");preview.SetBusy(true,false);Assert(!preview.Controls.OfType<VoiceButton>().Any(x=>x.Enabled),"History actions can interrupt recording");}
        reopened.Delete(interrupted).GetAwaiter().GetResult();Assert(reopened.List().Length==19&&!File.Exists(Path.Combine(AppPaths.History,interrupted+".vfh")),"Delete did not remove audio and text");
        SecretStore.Save("PRIVATE_TOKEN_SENTINEL");Assert(SecretStore.Load()=="PRIVATE_TOKEN_SENTINEL","Protected sign-in failed roundtrip");Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Data,"signin.vault"))).Contains("PRIVATE_TOKEN_SENTINEL"),"Saved sign-in is plaintext");SecretStore.Clear();Assert(SecretStore.Load()==null,"Sign-out retained credentials");
        Diagnostics.Error("fatal",new IOException("PRIVATE_TOKEN_SENTINEL PRIVATE_AUDIO_SENTINEL C:\\private-user"));Diagnostics.Event("PRIVATE_TOKEN_SENTINEL");Diagnostics.State("PRIVATE_TOKEN_SENTINEL");Diagnostics.Phase("PRIVATE_TOKEN_SENTINEL");Diagnostics.SignIn("PRIVATE_TOKEN_SENTINEL");Assert(!Diagnostics.Summary().Contains("PRIVATE_"),"Diagnostics exposed exception details");
        for(int i=0;i<6;i++){File.WriteAllText(Path.Combine(AppPaths.Logs,"voice-flow.log"),new string('x',262145));Diagnostics.Event("startup");}
        Assert(Directory.GetFiles(AppPaths.Logs).Length<=4,"Diagnostic rotation is unbounded");
        Assert(EngineChoice.Current==EngineChoice.Default,"Default engine is wrong");File.WriteAllText(Path.Combine(AppPaths.Data,"engine.txt"),"unknown-engine");Assert(EngineChoice.Current==EngineChoice.Default,"Unknown engine was used");
        EngineChoice.Current="local";Assert(EngineChoice.Current=="local","Engine choice did not survive");
        Assert(!EngineChoice.LocalReady,"Local model reported ready without its files");Directory.CreateDirectory(EngineChoice.LocalModel);foreach(string f in new[]{"encoder.onnx","decoder.onnx","joiner.onnx","tokens.txt"})File.WriteAllText(Path.Combine(EngineChoice.LocalModel,f),"x");Assert(EngineChoice.LocalReady,"Local model files were not found");
        foreach(string name in new[]{"sarvam","chariot"}){Assert(!KeyStore.Has(name)&&!EngineChoice.Ready(name),"Engine ready without a key");}
        KeyStore.Save("sarvam","PRIVATE_ENGINE_KEY_0123456789");Assert(KeyStore.Get("sarvam")=="PRIVATE_ENGINE_KEY_0123456789"&&EngineChoice.Ready("sarvam")&&!EngineChoice.Ready("chariot"),"Engine key did not roundtrip");
        Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Data,"engine-keys.vault"))).Contains("PRIVATE_ENGINE_KEY"),"Engine key stored in plain text");
        bool refused=false;try{KeyStore.Save("sarvam","short");}catch(ArgumentException){refused=true;}Assert(refused&&!KeyStore.Valid("has space in it")&&!KeyStore.Valid(null),"Bad key accepted");
        refused=false;try{KeyStore.Save("google","PRIVATE_ENGINE_KEY_0123456789");}catch(ArgumentException){refused=true;}Assert(refused,"Key saved for a non-paid engine");
        EngineChoice.Current="chariot";Assert(EngineChoice.Current=="chariot","Paid engine choice did not survive");EngineChoice.Current="local";
        KeyStore.Remove("sarvam");Assert(!KeyStore.Has("sarvam")&&!File.Exists(Path.Combine(AppPaths.Data,"engine-keys.vault")),"Engine key not removed");
        string fake=Path.Combine(AppPaths.Data,"fake-model.bin");File.WriteAllText(fake,"not the model");Assert(!ModelDownload.Matches(fake,"13","0000"),"Wrong model file accepted");Assert(ModelDownload.TotalBytes==73655211,"Model size list changed");
        reopened.Clear().GetAwaiter().GetResult();Assert(!Directory.GetFiles(AppPaths.History).Any(),"Clear-all left recording artifacts");
    }
    static void DeliveryChecks()
    {
        string copied="";int pasted=0;
        string result=ConfirmedDelivery.Send("বাংলা English",true,text=>copied=text,()=>{pasted++;return Task.FromResult(false);}).GetAwaiter().GetResult();
        Assert(result=="clipboard"&&copied=="বাংলা English"&&pasted==1,"Rejected paste discarded confirmed text");
        result=ConfirmedDelivery.Send("শুধু copy",false,text=>copied=text,()=>{pasted++;return Task.FromResult(true);}).GetAwaiter().GetResult();Assert(result=="clipboard"&&pasted==1,"Manual or recovered text auto-pasted");
        result=ConfirmedDelivery.Send("রাখা লেখা",true,text=>{throw new System.Runtime.InteropServices.ExternalException();},()=>{pasted++;return Task.FromResult(true);}).GetAwaiter().GetResult();Assert(result=="unavailable"&&pasted==1,"Clipboard failure reported success or attempted paste");
        result=ConfirmedDelivery.Send("পরের কথা",true,text=>copied=text,()=>Task.FromResult(true)).GetAwaiter().GetResult();Assert(result=="pasted"&&copied=="পরের কথা","Paste fallback poisoned the next delivery");
    }
    internal static int Run()
    {
        string directory=Path.Combine(Program.Root,"tests");Directory.CreateDirectory(directory);
        try
        {
            Assert(VoiceFlowApp.IsGoogleSignInUrl("https://accounts.google.com/o/oauth2/v2/auth?state=test"),"Official sign-in rejected");
            foreach(string url in new[]{"https://accounts.google.com.evil.test/o/oauth2/v2/auth","http://accounts.google.com/o/oauth2/v2/auth","https://accounts.google.com:444/o/oauth2/v2/auth","https://evil@accounts.google.com/o/oauth2/v2/auth","file:///C:/test.exe","https://accounts.google.com/other"})Assert(!VoiceFlowApp.IsGoogleSignInUrl(url),"Unsafe sign-in accepted");
            foreach(string url in new[]{"https://github.com.evil.test/a/b","http://github.com/a/b","https://user@github.com/a/b","https://github.com/a/b?token=private","https://github.com/a/b/c"})Assert(!AppLinks.ValidRepository(url),"Unsafe support URL accepted");
            Assert(AppLinks.ValidRepository(AppLinks.Repository)&&AppLinks.IssueUrl.EndsWith("/issues/new?template=bug_report.md"),"Report does not open issue-writing page");Assert(!AppLinks.Open(false),"Unpublished repository was opened");
            StorageChecks();SynchronizationContext.SetSynchronizationContext(null);DeliveryChecks();Visuals.RenderAssets();
            using(var panel=new ControlPanel())
            {
                panel.UpdateState("idle","Ctrl+Space",true,true,"THIS_MUST_NOT_APPEAR",false,true,20);Assert(!panel.Controls.OfType<TextBox>().Any(),"Main window still retains a transcript box");
                if(Edition.Public)panel.UpdateEngine("local","মডেল নামানো আছে। ইন্টারনেট ছাড়া চলে, অডিও এই কম্পিউটার ছেড়ে যায় না।\nকথা শেষ হলে লেখা একবারে আসে।","মডেল নামান",true,null,"✓ প্রস্তুত",true);if(!Edition.Public)panel.UpdateEngine("google","Google সাইন-ইন আছে।\nইন্টারনেট ছাড়া ব্যাকআপ চাইলে নিচের বোতামে একবার লোকাল মডেল (প্রায় ৭১ MB) নামিয়ে রাখুন।","লোকাল মডেল নামান",true,null,"✓ প্রস্তুত",true);Snapshot(panel,"control-panel-preview");
                panel.UpdateEngine("sarvam","১. \"চাবি নিন\" চাপুন, Sarvam-এর সাইন-ইন পাতা খুলবে।\n২. সেখানে নতুন API key বানিয়ে কপি করুন।\n৩. \"চাবি বসান\" চেপে পেস্ট করুন। কাজ শেষ।\nঅডিও Sarvam-এর সার্ভারে যায়, খরচ আপনার account-এ।","চাবি বসান",true,"চাবি নিন","চাবি বাকি",false);panel.UpdateState("idle","Ctrl+Space",false,true,"",false,true,20);Snapshot(panel,"engine-preview");using(var dialog=KeyDialog.ForPreview("sarvam",false))Snapshot(dialog,"key-dialog-preview");Assert(!AppLinks.OpenFixed("https://evil.example/")&&!AppLinks.OpenFixed(EngineChoice.KeyPage("sarvam")+"?x=1"),"Unlisted link opened");panel.UpdateEngine("google","Google সাইন-ইন আছে।",null,true);panel.UpdateState("idle","Ctrl+Space",true,true,"",false,true,20);
                panel.UpdateState("signin","Ctrl+Space",false,true,"",false);panel.ShowSignInLink(true);Snapshot(panel,"signin-preview");Assert(!panel.PrimaryButton.Enabled,"Duplicate sign-in is enabled");
                panel.UpdateState("finishing","Ctrl+Space",true,true,"",false);Assert(!panel.PrimaryButton.Enabled,"Finishing permits another capture");
                panel.UpdateState("idle","Ctrl+Space",true,true,"",false,true,20);panel.ShowSignInLink(false);Assert(panel.PrimaryButton.Enabled,"Clipboard fallback leaves recording disabled");
                panel.Scale(new SizeF(1.25f,1.25f));Snapshot(panel,"control-panel-125-preview");
            }
            SynchronizationContext.SetSynchronizationContext(null);
            using(var audio=new AudioPipe(false))
            {
                var prefix=Enumerable.Range(0,65538).Select(i=>(byte)(i%251)).ToArray();var tail=Enumerable.Range(0,10002).Select(i=>(byte)(i%127)).ToArray();audio.Append(prefix);int port=int.Parse(audio.Address.Split(':')[1]);
                using(var client=new TcpClient("127.0.0.1",port)){client.ReceiveTimeout=5000;audio.Append(tail);byte[] received=new byte[prefix.Length+tail.Length];int count=0;while(count<received.Length){int n=client.GetStream().Read(received,count,received.Length-count);Assert(n>0,"Audio ended early");count+=n;}audio.Drain().GetAwaiter().GetResult();Assert(received.SequenceEqual(prefix.Concat(tail)),"Beginning or ending bytes lost");Assert(audio.CapturedBytes==audio.SentBytes,"Byte counts differ");Assert(audio.Since(prefix.Length).SequenceEqual(tail),"Checkpoint offset dropped audio");audio.ExpectDisconnect();}
                audio.StartCapture(false);using(var client=new TcpClient("127.0.0.1",port)){client.ReceiveTimeout=5000;audio.Append(new byte[]{23,45});Assert(client.GetStream().ReadByte()==23&&client.GetStream().ReadByte()==45,"Stale session bytes");audio.ExpectDisconnect();}
            }
            using(var audio=new AudioPipe(false)){var timer=Stopwatch.StartNew();Task voice=Task.Run(async delegate{while(timer.ElapsedMilliseconds<700){audio.Append(new byte[]{0,16,0,16});await Task.Delay(25);}});audio.CaptureTail().GetAwaiter().GetResult();voice.GetAwaiter().GetResult();Assert(timer.ElapsedMilliseconds>=820,"Final syllable grace shortened");}
            using(var backend=new Backend())using(var boot=new ManualResetEventSlim(false)){backend.Message+=delegate(System.Collections.Generic.Dictionary<string,object> data){if(data.ContainsKey("type")&&Convert.ToString(data["type"])=="boot")boot.Set();};backend.Start();Assert(boot.Wait(10000)&&backend.Alive,"Packaged helper failed boot");}
            File.WriteAllText(Path.Combine(directory,"native-results.txt"),"PASS: "+checks+" native assertions; encrypted 20-entry text/audio retention and eviction; restart and torn-journal recovery; cancellation/deletion; token vault; privacy-safe bounded diagnostics; clipboard failure and next delivery; no main transcript box; support URL validation; UI states; complete audio boundaries; final syllable grace; own Node boot/cleanup. No real microphone, clipboard, pasted input, Google account, browser navigation or user data was used. 125% preview is simulated scaling, not a second physical display.",Program.Utf8);return 0;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(directory,"native-results.txt"),"FAIL: "+exception,Program.Utf8);return 1;}
        finally{string full=Path.GetFullPath(AppPaths.Data);if(full.StartsWith(Path.GetFullPath(Path.Combine(Program.Root,"tests"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(full))Directory.Delete(full,true);}
    }
}
