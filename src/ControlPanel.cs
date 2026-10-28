using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Black, white and yellow on a light grey page. Yellow is only ever a fill (never text on white); black carries emphasis.
internal static class Theme
{
    internal static readonly Color Page = Color.FromArgb(235,235,232), Background = Color.White, Surface = Color.FromArgb(243,243,240), Ink = Color.FromArgb(17,17,17), Muted = Color.FromArgb(88,88,82),
        Accent = Color.FromArgb(255,214,0), AccentDeep = Color.FromArgb(247,200,0), AccentSoft = Color.FromArgb(255,243,190), Line = Color.FromArgb(226,226,222), Danger = Color.FromArgb(190,40,35), Good = Color.FromArgb(12,122,74), Warn = Color.FromArgb(160,92,0);
    internal static Font Font(float size) { return new Font(Pill.FontName(), size, FontStyle.Regular, GraphicsUnit.Point); }
    internal static Font Bold(float size) { return new Font(Pill.FontName(), size, FontStyle.Bold, GraphicsUnit.Point); }
    internal static void Window(Form form)
    {
        form.BackColor = Background; form.ForeColor = Ink; form.Font = Font(12); form.Icon = Visuals.Icon("idle");
        form.AutoScaleDimensions=new SizeF(96,96);form.AutoScaleMode = AutoScaleMode.Dpi; form.StartPosition = FormStartPosition.CenterScreen;
        form.HandleCreated += delegate { try { int dark = 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, 4); } catch { } };
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static void Symbol(Graphics g, string name, Rectangle box, Color color)
    {
        var saved = g.Save(); g.TranslateTransform(box.X, box.Y); g.ScaleTransform(box.Width / 24f, box.Height / 24f); g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            if (name == "clock") { g.DrawEllipse(pen,3,3,18,18); g.DrawLines(pen,new[]{new Point(12,7),new Point(12,12),new Point(16,14)}); }
            else if (name == "report") { g.DrawRectangle(pen,4,4,16,13); g.DrawLines(pen,new[]{new Point(7,17),new Point(7,21),new Point(12,17)}); g.DrawLine(pen,8,8,16,8); g.DrawLine(pen,8,12,13,12); }
            else if (name == "arrow") { g.DrawLines(pen,new[]{new Point(11,5),new Point(4,12),new Point(11,19)}); g.DrawLine(pen,4,12,21,12); }
            else if (name == "play") g.DrawPolygon(pen,new[]{new Point(8,4),new Point(20,12),new Point(8,20)});
            else if (name == "stop") g.DrawRectangle(pen,6,6,12,12);
            else if (name == "copy") { g.DrawRectangle(pen,8,7,12,14); g.DrawLines(pen,new[]{new Point(5,17),new Point(3,17),new Point(3,3),new Point(15,3),new Point(15,4)}); }
            else if (name == "trash") { g.DrawLine(pen,4,6,20,6); g.DrawRectangle(pen,9,3,6,3); g.DrawLines(pen,new[]{new Point(6,6),new Point(7,21),new Point(17,21),new Point(18,6)}); g.DrawLine(pen,10,10,10,17); g.DrawLine(pen,14,10,14,17); }
            else if (name == "retry") { g.DrawArc(pen,4,4,16,16,220,310); g.DrawLines(pen,new[]{new Point(4,3),new Point(4,10),new Point(11,10)}); }
            else if (name == "more") { using(var b=new SolidBrush(color)) for(int i=0;i<3;i++)g.FillEllipse(b,3f+i*7,10.25f,3.5f,3.5f); }
            else if (name == "check") g.DrawLines(pen,new[]{new Point(4,12),new Point(10,18),new Point(20,6)});
            else { float[] h={6,12,18,12,6}; for(int i=0;i<5;i++)g.DrawLine(pen,4+i*4,12-h[i]/2,4+i*4,12+h[i]/2); }
        }
        g.Restore(saved);
    }
}

internal sealed class VoiceButton : Button
{
    internal string Symbol = "";
    // Primary: yellow. Dark: black with a yellow icon. Quiet: no fill. Big: a large tile with the label at the bottom-left.
    internal bool Primary, Quiet, Danger, Dark, Big;
    internal Color? Fill;
    bool hover;
    internal VoiceButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=Theme.Font(12);Height=44;BackColor=Color.White;
        MouseEnter+=delegate{hover=true;Invalidate();};MouseLeave+=delegate{hover=false;Invalidate();};GotFocus+=delegate{Invalidate();};LostFocus+=delegate{Invalidate();};
    }
    static Color Darker(Color c,int by){return Color.FromArgb(c.A,Math.Max(0,c.R-by),Math.Max(0,c.G-by),Math.Max(0,c.B-by));}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
        Color basis=Fill??Theme.Surface,bg,ink,iconInk;
        if(!Enabled){bg=Primary||Dark||!Quiet?Theme.Surface:BackColor;ink=Theme.Muted;iconInk=Theme.Muted;}
        else if(Primary){bg=hover?Theme.AccentDeep:Theme.Accent;ink=iconInk=Theme.Ink;}
        else if(Dark){bg=hover?Color.FromArgb(48,48,48):Theme.Ink;ink=Color.White;iconInk=Theme.Accent;}
        else if(Quiet){bg=hover?Theme.Surface:BackColor;ink=Danger?Theme.Danger:Theme.Ink;iconInk=ink;}
        else{bg=hover?Darker(basis,12):basis;ink=Danger?Theme.Danger:Theme.Ink;iconInk=ink;}
        int radius=Big?Math.Max(18,Height/6):Math.Min(Height/3,16);
        using(var path=Pill.Rounded(new Rectangle(0,0,Width-1,Height-1),radius))using(var brush=new SolidBrush(bg))g.FillPath(brush,path);
        if(Big)
        {
            if(Symbol.Length>0)Theme.Symbol(g,Symbol,new Rectangle(Height/6,Height/6,Height/4,Height/4),iconInk);
            using(var big=Theme.Bold(18))TextRenderer.DrawText(g,Text,big,new Rectangle(Height/6,Height/2,Width-Height/3,Height/2-Height/9),ink,TextFormatFlags.Left|TextFormatFlags.Bottom|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.WordBreak);
        }
        else
        {
            int icon=Symbol.Length>0?22:0;
            if(Text.Length==0&&icon>0)Theme.Symbol(g,Symbol,new Rectangle((Width-icon)/2,(Height-icon)/2,icon,icon),iconInk);
            else
            {
                if(icon>0)Theme.Symbol(g,Symbol,new Rectangle(16,(Height-icon)/2,icon,icon),iconInk);
                TextRenderer.DrawText(g,Text,Font,new Rectangle(16+icon+(icon>0?10:0),0,Width-32-icon-(icon>0?10:0),Height),ink,TextFormatFlags.VerticalCenter|(icon>0?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            }
        }
        if(Focused)using(var pen=new Pen(Theme.Ink,2))using(var focus=Pill.Rounded(new Rectangle(3,3,Width-7,Height-7),Math.Max(4,radius-3))){pen.DashStyle=DashStyle.Dot;g.DrawPath(pen,focus);}
    }
}

// A rounded block with a small caption and one large value, like the black tiles in the reference.
internal sealed class Tile : Control
{
    internal string Caption="",Value="";
    internal bool Dark=true;
    internal Tile(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);TabStop=false;BackColor=Color.White;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
        using(var path=Pill.Rounded(new Rectangle(0,0,Width-1,Height-1),Math.Max(18,Height/6)))using(var brush=new SolidBrush(Dark?Theme.Ink:Theme.AccentSoft))g.FillPath(brush,path);
        int pad=Height/6;
        using(var small=Theme.Font(11.5f))TextRenderer.DrawText(g,Caption,small,new Rectangle(pad,pad,Width-2*pad,Height/3),Dark?Color.FromArgb(200,200,194):Theme.Muted,TextFormatFlags.Left|TextFormatFlags.Top|TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak);
        using(var big=new Font("Segoe UI Semibold",22,FontStyle.Regular,GraphicsUnit.Point))TextRenderer.DrawText(g,Value,big,new Rectangle(pad,Height/2,Width-2*pad,Height/2-Height/9),Dark?Color.White:Theme.Ink,TextFormatFlags.Left|TextFormatFlags.Bottom|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ControlPanel : Form
{
    readonly Label status=new Label(),details=new Label(),privacy=new Label(),engineLabel=new Label(),engineNote=new Label(),engineState=new Label();
    readonly VoiceButton primary=new VoiceButton{Primary=true,Big=true,Symbol="wave"},history=new VoiceButton{Symbol="clock",Text="সাম্প্রতিক কথা",Fill=Theme.Surface},more=new VoiceButton{Symbol="more",Fill=Theme.Surface},enginePick=new VoiceButton{Fill=Color.White},engineAction=new VoiceButton{Dark=true},engineGet=new VoiceButton{Primary=true};
    readonly Tile hotkeyTile=new Tile{Caption="চাপুন, বলুন, আবার চাপুন"};
    readonly ContextMenuStrip engineMenu=new ContextMenuStrip{ShowImageMargin=false};
    readonly LinkLabel browser=new LinkLabel();
    readonly ToolTip tips=new ToolTip();
    readonly ContextMenuStrip support=new ContextMenuStrip();
    readonly ToolStripMenuItem reportItem=new ToolStripMenuItem("সমস্যা জানান"),signout=new ToolStripMenuItem("সাইন আউট"),updates=new ToolStripMenuItem("আপডেট দেখুন"),export=new ToolStripMenuItem("সমস্যার লগ সংরক্ষণ"),close=new ToolStripMenuItem("অ্যাপ বন্ধ করুন");
    internal string SignInIssue="";
    internal string Engine="google";
    internal bool GoogleReady;
    bool hasGet;
    internal event Action SignIn,BrowserSignIn,Speak,ShowHistory,Report,ExportLogs,Updates,SignOut,Quit,EngineAction,EngineLink;
    internal event Action<string> EngineChanged;
    internal bool AllowClose;
    internal Button PrimaryButton{get{return primary;}}
    float Scale96{get{return DeviceDpi/96f;}}
    int U(int value){return (int)Math.Round(value*Scale96);}
    internal ControlPanel()
    {
        Text="Voice Flow";ClientSize=new Size(800,730);MinimumSize=new Size(760,750);MaximizeBox=false;Theme.Window(this);BackColor=Theme.Page;
        status.Font=Theme.Bold(24);status.ForeColor=Theme.Ink;details.Font=Theme.Font(14.5f);details.ForeColor=Theme.Muted;
        privacy.Text="শেষ ২০টি কথা · লেখা ও অডিও এই কম্পিউটারেই থাকে";privacy.ForeColor=Theme.Muted;privacy.Font=Theme.Font(11);
        engineLabel.Text="ভয়েস ইঞ্জিন";engineLabel.ForeColor=Theme.Ink;engineLabel.Font=Theme.Bold(12);engineNote.ForeColor=Theme.Muted;engineNote.Font=Theme.Font(12);
        foreach(Label label in new[]{status,details,privacy})label.BackColor=Color.White;
        foreach(Label label in new[]{engineLabel,engineNote,engineState})label.BackColor=Theme.Surface;engineState.Font=Theme.Bold(12);engineState.TextAlign=ContentAlignment.MiddleRight;
        browser.Text="সাইন-ইন পাতা আবার খুলুন";browser.LinkColor=browser.ActiveLinkColor=Theme.Ink;browser.BackColor=Color.White;browser.Visible=false;
        engineGet.Visible=false;
        enginePick.BackColor=engineAction.BackColor=engineGet.BackColor=Theme.Surface;
        enginePick.AccessibleName="ভয়েস ইঞ্জিন বাছুন";
        foreach(string name in EngineChoice.Known){string chosen=name;var item=new ToolStripMenuItem(EngineChoice.Label(name));item.Tag=name;item.Click+=delegate{if(EngineChanged!=null)EngineChanged(chosen);};engineMenu.Items.Add(item);}MenuStyle.Apply(engineMenu);
        enginePick.Click+=delegate{engineMenu.Show(enginePick,new Point(0,enginePick.Height+2));};
        engineAction.Click+=delegate{if(EngineAction!=null)EngineAction();};engineGet.Click+=delegate{if(EngineLink!=null)EngineLink();};
        primary.Click+=delegate{if(primary.Tag as string=="signin"){if(SignIn!=null)SignIn();}else if(Speak!=null)Speak();};
        history.Click+=delegate{if(ShowHistory!=null)ShowHistory();};browser.LinkClicked+=delegate{if(BrowserSignIn!=null)BrowserSignIn();};
        reportItem.Click+=delegate{if(Report!=null)Report();};export.Click+=delegate{if(ExportLogs!=null)ExportLogs();};updates.Click+=delegate{if(Updates!=null)Updates();};signout.Click+=delegate{if(SignOut!=null)SignOut();};close.Click+=delegate{if(Quit!=null)Quit();};
        signout.Visible=!Edition.Public;support.Items.AddRange(new ToolStripItem[]{new ToolStripMenuItem("Voice Flow 0.3.3"){Enabled=false},new ToolStripSeparator(),reportItem,export,updates,signout,new ToolStripSeparator(),close});MenuStyle.Apply(support);
        more.Click+=delegate{support.Show(more,new Point(more.Width,more.Height+2),ToolStripDropDownDirection.BelowLeft);};
        Controls.AddRange(new Control[]{status,details,hotkeyTile,primary,browser,engineLabel,engineState,enginePick,engineAction,engineGet,engineNote,history,more,privacy});
        foreach(Control control in Controls){control.AccessibleName=control.Text;if(control is Label)control.TabStop=false;}
        more.AccessibleName="সহায়তা ও অ্যাপের বিকল্প";tips.SetToolTip(more,"সমস্যা জানান, লগ, আপডেট, সাইন আউট");history.AccessibleName="সাম্প্রতিক কথা";
        FormClosing+=delegate(object sender,FormClosingEventArgs args){if(!AllowClose){args.Cancel=true;Hide();}};
        var handle=Handle;LayoutControls();
    }
    protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(status!=null)LayoutControls();}
    // One white card on a grey page: a big heading, one yellow action, one black key hint, then the engine choice.
    void LayoutControls()
    {
        int w=ClientSize.Width,x=U(56),inner=w-2*x,gap=U(12),top=U(48);
        history.SetBounds(w-x-U(52)-gap-U(230),top+U(2),U(230),U(52));more.SetBounds(w-x-U(52),top+U(2),U(52),U(52));
        status.SetBounds(x,top,history.Left-x-gap,U(84));details.SetBounds(x,top+U(88),inner,U(34));
        int tilesTop=U(186),tilesHeight=U(156),keyWidth=U(250);
        primary.SetBounds(x,tilesTop,inner-keyWidth-gap,tilesHeight);hotkeyTile.SetBounds(w-x-keyWidth,tilesTop,keyWidth,tilesHeight);
        browser.SetBounds(x,tilesTop+tilesHeight+U(2),inner,U(26));
        int box=U(378),pad=U(24);
        engineLabel.SetBounds(x+pad,box+U(18),U(260),U(28));engineState.SetBounds(x+pad+U(260),box+U(18),inner-2*pad-U(260),U(28));enginePick.SetBounds(x+pad,box+U(54),U(340),U(54));int right=inner-2*pad-U(352),buttons=hasGet?(right-U(8))/2:right;engineGet.SetBounds(x+pad+U(352),box+U(54),buttons,U(54));engineAction.SetBounds(x+pad+U(352)+(hasGet?buttons+U(8):0),box+U(54),buttons,U(54));
        engineNote.SetBounds(x+pad,box+U(122),inner-2*pad,U(118));
        privacy.SetBounds(x,ClientSize.Height-U(68),inner,U(28));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Pill.Rounded(new Rectangle(U(20),U(20),ClientSize.Width-U(40),ClientSize.Height-U(40)),U(30)))using(var brush=new SolidBrush(Theme.Background))g.FillPath(brush,path);
        int x=U(56),box=U(378);
        using(var path=Pill.Rounded(new Rectangle(x,box,ClientSize.Width-2*x,U(256)),U(22)))using(var brush=new SolidBrush(Theme.Surface))g.FillPath(brush,path);
    }
    internal void UpdateState(string mode,string shortcut,bool ready,bool helperReady,string ignored,bool recovery,bool canStart=true,int count=0)
    {
        bool busy=mode=="recording"||mode=="finishing"||mode=="signin";
        status.Text=mode=="recording"?"শুনছি…":mode=="finishing"?"শেষের কথাগুলো আসছে…":mode=="signin"?"সংযোগ তৈরি হচ্ছে…":ready?"কথা বলার জন্য প্রস্তুত":Engine=="google"?"শুরু করতে সাইন ইন করুন":"ইঞ্জিন তৈরি করতে হবে";
        details.Text=mode=="signin"?"ব্রাউজারে সাইন-ইন শেষ হলে এখানে ফিরে আসুন।":mode=="recording"?"শেষ হলে একই শর্টকাট আবার চাপুন।":mode=="finishing"?"একটু অপেক্ষা করুন।":ready?"যেখানে লিখবেন, সেখানে কার্সর রাখুন।":Engine=="google"?"আপনার Google account দিয়ে সংযোগ করুন।":"নিচের ভয়েস ইঞ্জিন অংশে দেখুন কী লাগবে।";
        if(SignInIssue.Length>0&&mode=="error"){details.Text=SignInIssue;status.Text="সংযোগটি সম্পূর্ণ হয়নি";}
        hotkeyTile.Value=shortcut;hotkeyTile.Invalidate();
        primary.Text=mode=="recording"?"থামিয়ে লেখা নিন":mode=="finishing"?"সম্পূর্ণ হচ্ছে…":mode=="signin"?"সংযুক্ত হচ্ছে…":ready?"কথা বলে দেখুন":Engine=="google"?"Google দিয়ে সাইন ইন":"ইঞ্জিন তৈরি নয়";
        primary.Symbol=mode=="recording"?"stop":"wave";primary.Primary=mode!="recording";primary.Dark=mode=="recording";
        primary.Tag=ready?"speak":"signin";primary.Enabled=mode=="recording"||!busy&&(ready?helperReady&&canStart:Engine=="google");primary.AccessibleName=primary.Text;primary.Invalidate();
        history.Text="সাম্প্রতিক কথা"+(count>0?" · "+count:"");history.Enabled=!busy;reportItem.Enabled=!busy;updates.Enabled=!busy&&AppLinks.Published;signout.Enabled=!busy&&GoogleReady;close.Enabled=!busy;
        enginePick.Enabled=!busy;
    }
    // note: one line under the picker; action: the single button next to it (hidden when there is nothing to do).
    internal void UpdateEngine(string current,string note,string action,bool actionEnabled,string link=null,string state="",bool good=false)
    {
        Engine=current;enginePick.Text=EngineChoice.Label(current)+"  ▾";foreach(ToolStripMenuItem item in engineMenu.Items)item.Text=((string)item.Tag==current?"✓ ":"    ")+EngineChoice.Label((string)item.Tag);
        engineNote.Text=note;engineState.Text=state;engineState.ForeColor=good?Theme.Good:Theme.Warn;hasGet=link!=null;engineGet.Visible=hasGet;if(link!=null){engineGet.Text=link;engineGet.AccessibleName=link;}engineAction.Visible=action!=null;if(action!=null){engineAction.Text=action;engineAction.AccessibleName=action;engineAction.Enabled=actionEnabled&&enginePick.Enabled;}
        LayoutControls();
    }
    internal void ShowSignInLink(bool visible){browser.Visible=visible;}
    internal void Notice(string text){details.Text=text;}
    internal void Open(){if(!Visible)Show();WindowState=FormWindowState.Normal;Activate();}
    protected override void Dispose(bool disposing){if(disposing){tips.Dispose();support.Dispose();engineMenu.Dispose();}base.Dispose(disposing);}
}
