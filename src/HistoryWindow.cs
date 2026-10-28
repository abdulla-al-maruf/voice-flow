using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

internal sealed class HistoryWindow : Form
{
    readonly HistoryStore store;
    readonly ListBox list=new ListBox();
    readonly Label title=new Label(),note=new Label(),empty=new Label();
    readonly VoiceButton play=new VoiceButton{Primary=true,Symbol="play",Text="শুনুন"},copy=new VoiceButton{Symbol="copy",Text="কপি"},retry=new VoiceButton{Symbol="retry",Text="আবার নিন"},delete=new VoiceButton{Symbol="trash",Text="মুছুন",Danger=true},clear=new VoiceButton{Quiet=true,Text="সব মুছুন",Danger=true};
    SoundPlayer player;MemoryStream playing;
    internal event Action<HistoryEntry> Retry;
    internal event Action Changed;
    internal event Action<string> Copy;
    internal bool CanRetry;
    bool busy;
    internal void SetBusy(bool value,bool canRetry){busy=value;CanRetry=canRetry;if(busy)StopPlayback();list.Enabled=!busy;UpdateActions();}
    internal HistoryWindow(HistoryStore history)
    {
        store=history;Text="সাম্প্রতিক কথা · Voice Flow";ClientSize=new Size(720,660);MinimumSize=new Size(680,600);Theme.Window(this);
        title.Text="সাম্প্রতিক কথা";title.Font=Theme.Bold(22);note.Text="শেষ ২০টি · লেখা ও অডিও এই কম্পিউটারেই থাকে";note.ForeColor=Theme.Muted;note.Font=Theme.Font(12);
        list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=100;list.BorderStyle=BorderStyle.None;list.BackColor=Theme.Background;list.ForeColor=Theme.Ink;list.IntegralHeight=false;
        list.AccessibleName="শেষ ২০টি কথার তালিকা";list.DrawItem+=DrawEntry;list.SelectedIndexChanged+=delegate{StopPlayback();UpdateActions();};
        empty.Text="এখনো কোনো কথা রাখা হয়নি।\nকথা বলে লিখলে এখানে পাওয়া যাবে।";empty.ForeColor=Theme.Muted;empty.TextAlign=ContentAlignment.MiddleCenter;
        play.Click+=delegate{Play();};copy.Click+=delegate{var entry=Selected;if(entry!=null&&Copy!=null)Copy(entry.Text);};
        retry.Click+=delegate{var entry=Selected;if(entry!=null&&Retry!=null){Close();Retry(entry);}};
        delete.Click+=async delegate{var entry=Selected;if(entry==null)return;StopPlayback();try{await store.Delete(entry.Id);Reload();if(Changed!=null)Changed();}catch{note.Text="মোছা যায়নি। আবার চেষ্টা করুন।";}};
        clear.Click+=async delegate{if(MessageBox.Show(this,"রাখা সব লেখা ও অডিও মুছে যাবে। মুছবেন?","ইতিহাস মুছুন",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;StopPlayback();try{await store.Clear();Reload();if(Changed!=null)Changed();}catch{note.Text="মোছা যায়নি। আবার চেষ্টা করুন।";}};
        Controls.AddRange(new Control[]{title,note,list,empty,play,copy,retry,delete,clear});FormClosed+=delegate{StopPlayback();};Resize+=delegate{LayoutItems();};LayoutItems();Reload();var handle=Handle;
    }
    internal HistoryEntry Selected{get{return list.SelectedItem as HistoryEntry;}}
    internal void Reload(){list.Items.Clear();list.Items.AddRange(store.List());if(list.Items.Count>0)list.SelectedIndex=0;empty.Visible=list.Items.Count==0;UpdateActions();}
    void LayoutItems(){int w=ClientSize.Width,h=ClientSize.Height;title.SetBounds(28,24,w-165,39);clear.SetBounds(w-143,25,115,38);note.SetBounds(28,78,w-56,28);list.SetBounds(16,124,w-32,h-220);empty.SetBounds(28,165,w-56,150);int each=(w-68)/4;play.SetBounds(22,h-70,each,44);copy.SetBounds(30+each,h-70,each,44);retry.SetBounds(38+2*each,h-70,each,44);delete.SetBounds(46+3*each,h-70,each,44);}
    void UpdateActions(){var entry=Selected;play.Enabled=delete.Enabled=!busy&&entry!=null;copy.Enabled=!busy&&entry!=null&&entry.Text.Length>0;retry.Enabled=!busy&&entry!=null&&CanRetry&&!entry.Confirmed;clear.Enabled=!busy&&list.Items.Count>0;}
    void DrawEntry(object sender,DrawItemEventArgs e)
    {
        if(e.Index<0)return;var entry=(HistoryEntry)list.Items[e.Index];bool selected=(e.State&DrawItemState.Selected)!=0;using(var b=new SolidBrush(selected?Theme.AccentSoft:Theme.Background))e.Graphics.FillRectangle(b,e.Bounds);
        Theme.Symbol(e.Graphics,entry.Confirmed?"check":entry.Status=="empty"?"wave":"retry",new Rectangle(e.Bounds.X+15,e.Bounds.Y+17,20,20),entry.Confirmed?Theme.Good:Theme.Muted);
        string time=entry.Created.ToLocalTime().ToString("dd MMM · h:mm tt")+"   "+TimeSpan.FromSeconds(entry.Seconds).ToString(@"m\:ss");
        using(var timeFont=new Font("Segoe UI",11))TextRenderer.DrawText(e.Graphics,time,timeFont,new Rectangle(e.Bounds.X+48,e.Bounds.Y+10,e.Bounds.Width-65,22),Theme.Muted,TextFormatFlags.NoPrefix);
        string text=entry.Text.Length>0?entry.Text:entry.Status=="empty"?"লেখা পাওয়া যায়নি · অডিও রাখা আছে":"অসম্পূর্ণ · অডিও থেকে আবার চেষ্টা করা যাবে";if(!entry.Confirmed&&entry.Text.Length>0)text="অসম্পূর্ণ · "+text;
        TextRenderer.DrawText(e.Graphics,text,Font,new Rectangle(e.Bounds.X+48,e.Bounds.Y+38,e.Bounds.Width-66,43),Theme.Ink,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(selected&&(e.State&DrawItemState.Focus)!=0)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(e.Bounds,-2,-2),Theme.Ink,Theme.Surface);
    }
    void Play(){if(playing!=null){StopPlayback();return;}var entry=Selected;if(entry==null)return;try{playing=new MemoryStream(WaveBytes(store.Audio(entry.Id)));player=new SoundPlayer(playing);player.Load();player.Play();play.Text="থামান";play.Symbol="stop";}catch{StopPlayback();note.Text="অডিও চালানো যায়নি।";}}
    void StopPlayback(){if(player!=null){player.Stop();player.Dispose();player=null;}if(playing!=null){playing.Dispose();playing=null;}play.Text="শুনুন";play.Symbol="play";}
    internal static byte[] WaveBytes(byte[] pcm)
    {
        using(var buffer=new MemoryStream())using(var writer=new BinaryWriter(buffer)){writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);return buffer.ToArray();}
    }
}
