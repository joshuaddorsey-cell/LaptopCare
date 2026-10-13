using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LaptopCare {
static class Program {
    delegate bool WindowVisitor(IntPtr h,IntPtr context);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr context);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int length);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,int message,IntPtr wParam,IntPtr lParam);
    internal static void ShowExisting() {
        EnumWindows((h,context)=>{var title=new StringBuilder(256);GetWindowText(h,title,title.Capacity);
            if(title.ToString()=="LaptopCare • Health & repair") {
                uint pid;GetWindowThreadProcessId(h,out pid);
                try{using(var p=Process.GetProcessById((int)pid))if(p.ProcessName.StartsWith("LaptopCare",StringComparison.OrdinalIgnoreCase) && p.SessionId==Process.GetCurrentProcess().SessionId){PostMessage(h,0x802A,IntPtr.Zero,IntPtr.Zero);SetForegroundWindow(h);return false;}}catch(ArgumentException){}
            }return true;},IntPtr.Zero);
    }
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopCare");
    [STAThread] static void Main(string[] args) {
        Directory.CreateDirectory(Data);
        if (args.Length == 2 && args[0] == "--repair") { Repair.Run(args[1]); return; }
        if (args.Length == 1 && args[0] == "--self-test") { Tests.Run(); return; }
        if (args.Length == 1 && args[0] == "--benchmark") {
            var samples=new List<string>();var monitor=new Monitor();using(var own=Process.GetCurrentProcess()) {
                for(int i=0;i<4;i++){own.Refresh();double before=own.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();monitor.Read();own.Refresh();samples.Add("Sample "+i+": elapsed "+watch.Elapsed.TotalSeconds.ToString("0.000")+" s; CPU "+(own.TotalProcessorTime.TotalSeconds-before).ToString("0.000")+" s "+monitor.Timing);}
            }File.WriteAllLines(Path.Combine(Data,"benchmark.txt"),samples);return;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--ui-test") { Tests.Ui(); return; }
        bool fresh; using (var mutex = new Mutex(true, "Local\\LaptopCare.UI", out fresh)) {
            if (!fresh) { ShowExisting(); return; }
            Application.Run(new MainWindow());
        }
    }
    public static void Open(string target) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
    public static void Audit(string text) { File.AppendAllText(Path.Combine(Data,"actions.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
}
[StructLayout(LayoutKind.Sequential)] class MemoryInfo {
    public uint Length = (uint)Marshal.SizeOf(typeof(MemoryInfo)); public uint Load;
    public ulong Total, Available, CommitLimit, CommitAvailable, VirtualTotal, VirtualAvailable, Extended;
}
class AppRow { public int Pid; public string Name, Title; public long Bytes, Started; public bool Closable; }
class Snapshot {
    public MemoryInfo Memory; public double Cpu; public List<AppRow> Apps; public DateTime At;
}
class Monitor {
    public string Timing;
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr handle,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static long Creation(int pid) {
        IntPtr handle=OpenProcess(0x1000,false,pid);if(handle==IntPtr.Zero)return 0;
        try {long created,exited,kernel,user;return GetProcessTimes(handle,out created,out exited,out kernel,out user)?DateTime.FromFileTimeUtc(created).Ticks:0;}finally{CloseHandle(handle);}
    }
    delegate bool WindowVisitor(IntPtr h,IntPtr context);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr context);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int length);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GlobalMemoryStatusEx([In, Out] MemoryInfo info);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    long previousIdle, previousTotal;
    public Snapshot Read() {
        var m = new MemoryInfo(); if (!GlobalMemoryStatusEx(m)) throw new System.ComponentModel.Win32Exception();
        long idle,kernel,user; double cpu = 0;
        if (GetSystemTimes(out idle,out kernel,out user)) {
            long total=kernel+user, elapsed=total-previousTotal;
            if (previousTotal!=0 && elapsed>0) cpu=Math.Max(0,Math.Min(100,100.0*(elapsed-(idle-previousIdle))/elapsed));
            previousIdle=idle; previousTotal=total;
        }
        var rows=new List<AppRow>();
        var watch=Stopwatch.StartNew();
        var windows=new Dictionary<int,string>();
        EnumWindows((h,context)=>{if(IsWindowVisible(h)){uint id;GetWindowThreadProcessId(h,out id);var text=new StringBuilder(256);GetWindowText(h,text,text.Capacity);if(text.Length>0 && !windows.ContainsKey((int)id))windows[(int)id]=text.ToString();}return true;},IntPtr.Zero);
        double windowTime=watch.Elapsed.TotalSeconds;watch.Restart();var processes=Process.GetProcesses();double inventoryTime=watch.Elapsed.TotalSeconds;watch.Restart();
        foreach (var p in processes) using(p) { try {
            string title;windows.TryGetValue(p.Id,out title);
            string name=p.ProcessName;long bytes=p.WorkingSet64;long started=Creation(p.Id);
            rows.Add(new AppRow { Pid=p.Id, Name=name, Title=title ?? "", Bytes=bytes,Started=started, Closable=false });
        } catch (System.ComponentModel.Win32Exception) {} catch (InvalidOperationException) {} }
        Timing="windows="+windowTime.ToString("0.000")+" inventory="+inventoryTime.ToString("0.000")+" properties="+watch.Elapsed.TotalSeconds.ToString("0.000");
        return new Snapshot { Memory=m, Cpu=cpu, Apps=rows.OrderByDescending(x=>x.Bytes).ToList(), At=DateTime.Now };
    }
    public static bool SafeToClose(Process p) {
        string n=p.ProcessName.ToLowerInvariant();
        string[] blocked={"laptopcare","chatgpt","codex","code","devenv","explorer","dism","sfc","chkdsk","powershell","pwsh","cmd","windowsterminal","msiexec","taskmgr","system","registry","winlogon","csrss","lsass","services","svchost","dwm"};
        return p.Id!=Process.GetCurrentProcess().Id && p.SessionId==Process.GetCurrentProcess().SessionId && p.MainWindowHandle!=IntPtr.Zero && !blocked.Contains(n);
    }
    public static string Pressure(MemoryInfo m) { return m.Load>=90 || m.Available<512UL*1024*1024 ? "HIGH" : m.Load>=80 || m.Available<1024UL*1024*1024 ? "ELEVATED" : "NORMAL"; }
    public static string Size(ulong bytes) { return (bytes/1073741824.0).ToString("0.00")+" GB"; }
}
class RepairResult { public string Mode, Started, Finished, Status, Detail; public int? DismExit, SfcExit, DiskExit; }
static class Repair {
    public static readonly string State=Path.Combine(Program.Data,"repair.json"), Log=Path.Combine(Program.Data,"repair.log");
    public static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    public static bool Valid(string mode) { return mode=="scan" || mode=="repair" || mode=="disk"; }
    public static bool WorkerActive() {
        try { using(var m=Mutex.OpenExisting("Global\\LaptopCare.Repair")) {
            bool owned=false;try{owned=m.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
            if(owned)m.ReleaseMutex();return !owned;
        }}catch(WaitHandleCannotBeOpenedException){return false;}catch(UnauthorizedAccessException){return true;}
    }
    public static bool Busy() { foreach(string name in new[]{"dism","sfc","chkdsk"}) { var ps=Process.GetProcessesByName(name); bool found=ps.Length>0; foreach(var p in ps)p.Dispose(); if(found)return true; } return false; }
    public static void Save(RepairResult r) {
        string temp=State+".tmp"; File.WriteAllText(temp,new JavaScriptSerializer().Serialize(r));
        if(File.Exists(State)) File.Replace(temp,State,null); else File.Move(temp,State);
    }
    public static RepairResult Read() { try { return new JavaScriptSerializer().Deserialize<RepairResult>(File.ReadAllText(State)); } catch { return null; } }
    public static string Conclusion(string s, int code) {
        string t=s.ToLowerInvariant();
        if(t.Contains("successfully repaired"))return "SFC reported damaged files repaired. See the full log.";
        if(t.Contains("did not find any integrity violations"))return "SFC reported no integrity violations.";
        if(t.Contains("unable to fix") || t.Contains("could not perform"))return "SFC could not complete all repairs. Review the log.";
        return "SFC exited with code "+code+". Review its final text in the log; no clean-health conclusion inferred.";
    }
    static int Command(string file,string args,out string output) {
        var text=new StringBuilder(); object gate=new object();
        using(var p=new Process()) {
            p.StartInfo=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,file),args) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            if(file=="sfc.exe") {p.StartInfo.StandardOutputEncoding=Encoding.Unicode;p.StartInfo.StandardErrorEncoding=Encoding.Unicode;}
            DataReceivedEventHandler collect=(sender,e)=> { if(e.Data!=null)lock(gate) { text.AppendLine(e.Data); File.AppendAllText(Log,e.Data+Environment.NewLine); } };
            p.OutputDataReceived+=collect; p.ErrorDataReceived+=collect;
            File.AppendAllText(Log,"\r\n"+file+" "+args+"\r\n"); p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit();
            output=text.ToString(); return p.ExitCode;
        }
    }
    public static void Run(string mode) {
        if(!Valid(mode) || !Admin)return;
        bool fresh; using(var guard=new Mutex(true,"Global\\LaptopCare.Repair",out fresh)) {
            if(!fresh)return;
            var r=new RepairResult { Mode=mode,Started=DateTime.Now.ToString("s"),Status="Running",Detail="Checking for existing Windows repairs" };
            try {
                if(Busy()) { r.Status="Blocked"; r.Detail="Another DISM, SFC or CHKDSK process is running. It was left untouched."; return; }
                File.WriteAllText(Log,"LaptopCare "+mode+" started "+r.Started+Environment.NewLine); Save(r);
                string text;
                if(mode=="disk") {
                    r.Detail="Online system-drive scan; no restart scheduled"; Save(r);
                    r.DiskExit=Command("chkdsk.exe",Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\')+" /scan",out text);
                    r.Status=r.DiskExit==0?"Completed":"Needs review"; r.Detail="CHKDSK exit code "+r.DiskExit+". Read the log for its conclusion.";
                } else {
                    r.Detail=mode=="repair"?"Repairing Windows component store (DISM)":"Scanning Windows component store (DISM)"; Save(r);
                    r.DismExit=Command("dism.exe","/Online /Cleanup-Image "+(mode=="repair"?"/RestoreHealth":"/ScanHealth"),out text);
                    if(r.DismExit!=0 && r.DismExit!=3010) { r.Status="Failed"; r.Detail="DISM failed with exit code "+r.DismExit+". SFC was not started."; return; }
                    r.Detail="Checking protected system files (SFC)"; Save(r);
                    r.SfcExit=Command("sfc.exe",mode=="repair"?"/scannow":"/verifyonly",out text);
                    r.Status=r.SfcExit==0?"Completed - review results":"Needs review";
                    r.Detail=Conclusion(text,r.SfcExit.Value)+(r.DismExit==3010?" DISM requests a restart.":"")+" DISM exit code: "+r.DismExit+".";
                }
            } catch(Exception e) { r.Status="Failed"; r.Detail=e.Message; }
            finally { r.Finished=DateTime.Now.ToString("s"); Save(r); Program.Audit("Repair "+mode+": "+r.Status+" "+r.Detail); }
        }
    }
}
class StartupRow { public string Name, Command; public bool Enabled; }
static class Startup {
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    const string Backup="Software\\LaptopCare\\DisabledStartup";
    public static List<StartupRow> Read() {
        var rows=new List<StartupRow>();
        foreach(var pair in new[]{new{Path=RunKey,Enabled=true},new{Path=Backup,Enabled=false}}) using(var key=Registry.CurrentUser.OpenSubKey(pair.Path)) {
            if(key!=null)foreach(string n in key.GetValueNames()) { var v=key.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string; if(v!=null)rows.Add(new StartupRow{Name=n,Command=v,Enabled=pair.Enabled}); }
        }
        return rows.OrderBy(x=>x.Name).ToList();
    }
    public static void Toggle(StartupRow row) {
        using(var active=Registry.CurrentUser.CreateSubKey(RunKey)) using(var backup=Registry.CurrentUser.CreateSubKey(Backup)) {
            var source=row.Enabled?active:backup; var dest=row.Enabled?backup:active;
            Move(source,dest,row);
            Program.Audit((row.Enabled?"Disabled":"Restored")+" startup: "+row.Name);
        }
    }
    public static void Move(RegistryKey source,RegistryKey dest,StartupRow row) {
        if((source.GetValue(row.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string)!=row.Command)throw new Exception("This startup entry changed. Reload before trying again.");
        if(dest.GetValue(row.Name)!=null)throw new Exception("An entry with this name already exists in the destination. No change made.");
        dest.SetValue(row.Name,row.Command,source.GetValueKind(row.Name)); source.DeleteValue(row.Name);
    }
}
class Trend : Control {
    public readonly List<float> Values=new List<float>();
    public Trend() { DoubleBuffered=true; Height=85; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var pen=new Pen(Color.FromArgb(49,65,84))) for(int y=10;y<Height;y+=25)e.Graphics.DrawLine(pen,0,y,Width,y);
        if(Values.Count<2)return;
        PointF[] pts=Values.Select((v,i)=>new PointF(i*(Width-1f)/(Values.Count-1),Height-8-v*(Height-16)/100)).ToArray();
        using(var pen=new Pen(Color.FromArgb(68,210,178),2.5f))e.Graphics.DrawLines(pen,pts);
    }
}
class MainWindow : Form {
    protected override void WndProc(ref Message message) {if(message.Msg==0x802A){ShowDashboard();return;}base.WndProc(ref message);}
    Color bg=Color.FromArgb(15,23,35), panel=Color.FromArgb(25,36,51), ink=Color.FromArgb(230,237,246), accent=Color.FromArgb(68,210,178);
    Label memory, cpu, pressure, advice, footer; Trend graph; DataGridView apps, startup; TextBox repairText;
    NotifyIcon tray; System.Windows.Forms.Timer timer; Monitor monitor=new Monitor(); bool sampling, exiting; Snapshot latest;
    Dictionary<int,AppRow> previous=new Dictionary<int,AppRow>(); Dictionary<int,long> growth=new Dictionary<int,long>(); DateTime baseline=DateTime.Now;
    public MainWindow() {
        Text="LaptopCare • Health & repair"; Size=new Size(1100,800); MinimumSize=new Size(880,680); StartPosition=FormStartPosition.CenterScreen;
        BackColor=bg; ForeColor=ink; Font=new Font("Segoe UI",10); Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        var header=new Panel { Dock=DockStyle.Top,Height=92,Padding=new Padding(24,16,24,10),BackColor=panel };
        header.Controls.Add(new Label{Text="LAPTOPCARE",Font=new Font("Segoe UI",23,FontStyle.Bold),AutoSize=true,Location=new Point(24,10)});
        header.Controls.Add(new Label{Text="Know what is slowing you down. Fix what needs attention.",AutoSize=true,Location=new Point(27,57),ForeColor=Color.LightSteelBlue});
        var tabs=new TabControl{Dock=DockStyle.Fill,Font=Font};
        var home=Page(tabs,"Overview"); var process=Page(tabs,"Memory & apps"); var start=Page(tabs,"Startup"); var repairs=Page(tabs,"Windows repair"); var tools=Page(tabs,"Tools & history");
        footer=new Label{Dock=DockStyle.Bottom,Height=30,Padding=new Padding(20,5,0,0),ForeColor=Color.LightSteelBlue,Text="Starting monitor…"};
        Controls.Add(tabs); Controls.Add(footer); Controls.Add(header);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=6};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,115)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,95)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,125)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); home.Controls.Add(layout);
        var metrics=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3}; for(int i=0;i<3;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
        memory=Metric(metrics,"RAM IN USE",0); cpu=Metric(metrics,"CPU ACTIVITY",1); pressure=Metric(metrics,"MEMORY PRESSURE",2); layout.Controls.Add(metrics,0,0);
        layout.Controls.Add(new Label{Text="Recent RAM samples · 5 seconds open / 15 seconds in tray",Dock=DockStyle.Fill,Padding=new Padding(0,12,0,0)},0,1);
        graph=new Trend{Dock=DockStyle.Fill,BackColor=panel};layout.Controls.Add(graph,0,2);
        advice=new Label{Dock=DockStyle.Fill,Padding=new Padding(12),BackColor=panel,Font=new Font("Segoe UI",12),Text="Reading live Windows measurements…"}; layout.Controls.Add(advice,0,3);
        var quick=new FlowLayoutPanel{Dock=DockStyle.Fill}; quick.Controls.Add(Button("Find memory-heavy apps",()=>tabs.SelectedTab=process)); quick.Controls.Add(Button("Check Windows files",()=>tabs.SelectedTab=repairs)); layout.Controls.Add(quick,0,4);
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="Monitoring is read-only. Repairs and startup changes happen only when you choose them.\r\nMinimize to keep monitoring in the tray. No forced RAM clearing or automatic app termination.",ForeColor=Color.LightSteelBlue,Padding=new Padding(2,8,0,0)},0,5);
        apps=Grid(new[]{"App","PID","RAM (MB)","Change (MB)","Window"}); process.Controls.Add(apps);
        var appActions=Actions(process,"RAM is resident working set, not private memory. Shared pages can appear in multiple apps. Change is since the baseline (up to 10 minutes).");
        appActions.Controls.Add(Button("Ask selected app to close",CloseSelected)); appActions.Controls.Add(Button("Reset baseline",()=>{previous.Clear();growth.Clear();baseline=DateTime.Now;})); appActions.Controls.Add(Button("Open Task Manager",()=>Program.Open("taskmgr.exe")));
        startup=Grid(new[]{"Name","Status","Command"}); start.Controls.Add(startup);
        var startupActions=Actions(start,"Current-user Run entries only. Disabling prevents launch at next sign-in; it does not close the app. Other startup types remain in Windows Settings.");
        startupActions.Controls.Add(Button("Disable / restore selected",ToggleStartup)); startupActions.Controls.Add(Button("Reload",LoadStartup)); startupActions.Controls.Add(Button("All startup apps",()=>Program.Open("ms-settings:startupapps"))); LoadStartup();
        repairText=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,BackColor=panel,ForeColor=ink,Font=new Font("Consolas",10),WordWrap=false}; repairs.Controls.Add(repairText);
        var repairActions=Actions(repairs,"Windows will request administrator permission. Keep the laptop powered and online. Closing this dashboard does not stop an active repair.");
        repairActions.Controls.Add(Button("Check system files",()=>StartRepair("scan"))); repairActions.Controls.Add(Button("Repair Windows",()=>StartRepair("repair"))); repairActions.Controls.Add(Button("Scan system drive",()=>StartRepair("disk"))); repairActions.Controls.Add(Button("Open repair log",()=>{if(File.Exists(Repair.Log))Program.Open(Repair.Log);}));
        var toolLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(20),WrapContents=false}; tools.Controls.Add(toolLayout);
        toolLayout.Controls.Add(new Label{Text="Windows tools, diagnostics and your action history",AutoSize=true,Font=new Font("Segoe UI",16,FontStyle.Bold)});
        toolLayout.Controls.Add(Button("Storage & temporary-file review",()=>Program.Open("ms-settings:storagesense")));
        toolLayout.Controls.Add(Button("Installed apps",()=>Program.Open("ms-settings:appsfeatures")));
        toolLayout.Controls.Add(Button("Reliability history",()=>Process.Start("perfmon.exe","/rel")));
        toolLayout.Controls.Add(Button("Windows Update",()=>Program.Open("ms-settings:windowsupdate")));
        toolLayout.Controls.Add(Button("Export current health report",Export));
        toolLayout.Controls.Add(Button("Open action & repair history",()=>Program.Open(Program.Data)));
        toolLayout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(900,0),Text="Temperature and drive-health telemetry are not reported by this version. Windows does not expose reliable readings on every laptop.\r\nLocal data stays in your Windows profile. No cloud upload or automatic startup is enabled.",ForeColor=Color.LightSteelBlue,Margin=new Padding(6,18,0,0)});
        tray=new NotifyIcon{Icon=Icon,Text="LaptopCare · monitoring",Visible=true}; var menu=new ContextMenuStrip(); menu.Items.Add("Open LaptopCare",null,(s,e)=>ShowDashboard()); menu.Items.Add("Exit",null,(s,e)=>{exiting=true;Close();}); tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowDashboard();
        VisibleChanged+=(s,e)=>{if(timer!=null)timer.Interval=Visible?5000:15000;};
        Resize+=(s,e)=>{if(WindowState==FormWindowState.Minimized)Hide();};
        FormClosing+=(s,e)=>{if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(2500,"LaptopCare","Monitoring in the tray. Choose Exit there to stop.",ToolTipIcon.Info);}};
        FormClosed+=(s,e)=>{timer.Stop();tray.Dispose();};
        timer=new System.Windows.Forms.Timer{Interval=5000};timer.Tick+=async(s,e)=>await RefreshLive(); timer.Start(); Shown+=async(s,e)=>await RefreshLive();
    }
    TabPage Page(TabControl tabs,string name) { var p=new TabPage(name){BackColor=bg,ForeColor=ink,Padding=new Padding(10)};tabs.TabPages.Add(p);return p; }
    Label Metric(TableLayoutPanel row,string title,int col) { var p=new Panel{Dock=DockStyle.Fill,BackColor=panel,Margin=new Padding(0,0,12,10)};p.Controls.Add(new Label{Text=title,AutoSize=true,ForeColor=Color.LightSteelBlue,Location=new Point(12,12)});var value=new Label{AutoSize=true,Font=new Font("Segoe UI",23,FontStyle.Bold),Location=new Point(10,40),ForeColor=accent};p.Controls.Add(value);row.Controls.Add(p,col,0);return value; }
    Button Button(string title,Action action) { var b=new Button{Text=title,AutoSize=true,Height=38,MinimumSize=new Size(150,38),BackColor=panel,ForeColor=ink,FlatStyle=FlatStyle.Flat,Margin=new Padding(5)}; b.FlatAppearance.BorderColor=Color.FromArgb(63,89,113);b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"LaptopCare",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};return b; }
    FlowLayoutPanel Actions(TabPage p,string note) { var area=new FlowLayoutPanel{Dock=DockStyle.Top,Height=125,Padding=new Padding(5)};area.Controls.Add(new Label{Text=note,Width=900,Height=54,ForeColor=Color.LightSteelBlue});area.SetFlowBreak(area.Controls[0],true);p.Controls.Add(area);return area; }
    DataGridView Grid(string[] names) { var g=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=panel,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false}; g.DefaultCellStyle.BackColor=panel;g.DefaultCellStyle.ForeColor=ink;g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(48,77,95);g.DefaultCellStyle.SelectionForeColor=Color.White;g.ColumnHeadersDefaultCellStyle.BackColor=bg;g.ColumnHeadersDefaultCellStyle.ForeColor=ink;g.RowTemplate.Height=32;foreach(string n in names)g.Columns.Add(n,n);return g; }
    void ShowDashboard(){Show();WindowState=FormWindowState.Normal;Activate();}
    async Task RefreshLive() {
        if(sampling)return;sampling=true;
        try {
            var snap=await Task.Run(()=>monitor.Read());if(IsDisposed)return;latest=snap;
            memory.Text=snap.Memory.Load+"%";cpu.Text=snap.Cpu.ToString("0")+"%";pressure.Text=Monitor.Pressure(snap.Memory);pressure.ForeColor=pressure.Text=="NORMAL"?accent:Color.FromArgb(255,185,88);
            graph.Values.Add(snap.Memory.Load);if(graph.Values.Count>120)graph.Values.RemoveAt(0);graph.Invalidate();
            if((DateTime.Now-baseline).TotalMinutes>=10){previous.Clear();growth.Clear();baseline=DateTime.Now;}
            foreach(var a in snap.Apps) { AppRow old; if(!previous.TryGetValue(a.Pid,out old)||old.Started!=a.Started)previous[a.Pid]=a;growth[a.Pid]=a.Bytes-previous[a.Pid].Bytes; }
            if(Visible && apps.Visible) {
                int selected=apps.SelectedRows.Count>0 && apps.SelectedRows[0].Tag!=null?((AppRow)apps.SelectedRows[0].Tag).Pid:-1;
                apps.Rows.Clear(); foreach(var a in snap.Apps.Take(80)) { int i=apps.Rows.Add(a.Name,a.Pid,(a.Bytes/1048576.0).ToString("0.0"),(growth[a.Pid]/1048576.0).ToString("+0.0;-0.0;0.0"),a.Title); apps.Rows[i].Tag=a;if(a.Pid==selected)apps.Rows[i].Selected=true; }
            }
            var top=snap.Apps.FirstOrDefault(); var drive=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            advice.Text=Monitor.Size(snap.Memory.Available)+" available of "+Monitor.Size(snap.Memory.Total)+" physical RAM.\r\n"+(pressure.Text=="NORMAL"?"Memory headroom looks comfortable.":"Memory is tight. Review large apps and close only work you have saved.")+"\r\n"+(top==null?"": "Largest process: "+top.Name+" ("+(top.Bytes/1048576.0).ToString("0")+" MB). ")+"System drive: "+Monitor.Size((ulong)drive.AvailableFreeSpace)+" free.";
            footer.Text="Updated "+snap.At.ToString("T")+" · every "+(timer.Interval/1000)+" seconds · local only · commit used "+Monitor.Size(snap.Memory.CommitLimit-snap.Memory.CommitAvailable)+" / "+Monitor.Size(snap.Memory.CommitLimit);
            tray.Text="LaptopCare · RAM "+snap.Memory.Load+"% · "+pressure.Text;
            RefreshRepair();
        } catch(Exception e){footer.Text="Monitor: "+e.Message;} finally{sampling=false;}
    }
    void CloseSelected() {
        if(apps.SelectedRows.Count==0)return;var a=apps.SelectedRows[0].Tag as AppRow;if(a==null)return;
        using(var p=Process.GetProcessById(a.Pid)) {
            if(p.StartTime.ToUniversalTime().Ticks!=a.Started || !Monitor.SafeToClose(p)) {MessageBox.Show(this,"This app is protected, has no normal window, or changed since the sample. Close it manually if appropriate.");return;}
            if(MessageBox.Show(this,"Ask "+a.Name+" to close? Save your work first. It may display its own save prompt. LaptopCare will not force it to end.","Close app",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            bool sent=p.CloseMainWindow();Program.Audit("Close request to "+a.Name+" PID "+a.Pid+": "+sent);MessageBox.Show(this,sent?"Close request sent. The app may need your attention before exiting.":"The app did not accept a close request.");
        }
    }
    void LoadStartup(){startup.Rows.Clear();foreach(var r in Startup.Read()){int i=startup.Rows.Add(r.Name,r.Enabled?"Enabled":"Disabled by LaptopCare",r.Command);startup.Rows[i].Tag=r;}}
    void ToggleStartup(){if(startup.SelectedRows.Count==0)return;var row=startup.SelectedRows[0].Tag as StartupRow;if(row==null)return;if(MessageBox.Show(this,(row.Enabled?"Disable ":"Restore ")+row.Name+" at next sign-in?", "Startup change",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;Startup.Toggle(row);LoadStartup();}
    void StartRepair(string mode) {
        if(Repair.Busy() || Repair.WorkerActive()){MessageBox.Show(this,"An existing Windows scan or repair is running. No duplicate will be launched.");return;}
        var current=Repair.Read();if(current!=null && current.Status=="Running"){
            if(MessageBox.Show(this,"The previous repair has no active worker or Windows repair process. Its final result is unverified. Mark it interrupted and allow a new scan?", "Incomplete previous repair",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            current.Status="Interrupted - result unverified";current.Finished=DateTime.Now.ToString("s");current.Detail="No active worker found. Review the existing log; completion cannot be confirmed.";Repair.Save(current);Program.Audit(current.Detail);
        }
        string description=mode=="repair"?"Repair the Windows component store with DISM, then repair protected files with SFC. This can take a while and may need Windows Update.":mode=="scan"?"Scan the Windows component store with DISM, then verify protected files with SFC. These commands do not request repairs.":"Run CHKDSK /scan on the system drive. No offline repair or restart is scheduled.";
        if(MessageBox.Show(this,description+"\r\n\r\nContinue? Windows will ask for administrator permission.","Windows maintenance",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--repair "+mode){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});Program.Audit("Requested "+mode);RefreshRepair();
    }
    void RefreshRepair(){var r=Repair.Read();string status=r==null?"No repair has been run by LaptopCare.":r.Status+" · "+r.Mode+"\r\nStarted: "+r.Started+"\r\nFinished: "+r.Finished+"\r\n"+r.Detail;string tail="";try{if(File.Exists(Repair.Log)) {using(var f=new FileStream(Repair.Log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(f.Length>32000)f.Seek(-32000,SeekOrigin.End);using(var reader=new StreamReader(f))tail=reader.ReadToEnd();}}}catch(IOException){}string text=status+"\r\n\r\n"+tail;if(repairText.Text!=text)repairText.Text=text;}
    void Export(){if(latest==null)return;using(var d=new SaveFileDialog{Filter="Text report|*.txt",FileName="LaptopCare-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".txt"}){if(d.ShowDialog(this)!=DialogResult.OK)return;var s=new StringBuilder();s.AppendLine("LaptopCare live health report "+latest.At.ToString("s"));s.AppendLine("RAM load: "+latest.Memory.Load+"%; available: "+Monitor.Size(latest.Memory.Available));s.AppendLine("CPU: "+latest.Cpu.ToString("0.0")+"%; pressure: "+Monitor.Pressure(latest.Memory));s.AppendLine("Process working sets (shared memory may be counted more than once):");foreach(var a in latest.Apps.Take(30))s.AppendLine(a.Name+" PID "+a.Pid+" "+(a.Bytes/1048576.0).ToString("0.0")+" MB");var r=Repair.Read();if(r!=null)s.AppendLine("Latest maintenance: "+r.Status+" "+r.Detail);File.WriteAllText(d.FileName,s.ToString());Program.Audit("Exported health report");Program.Open(d.FileName);}}
}
static class Tests {
    public static void Ui() {
        using(var window=new MainWindow()) {
            window.Show();Application.DoEvents();
            var tabs=window.Controls.OfType<TabControl>().Single();var report=new List<string>();
            foreach(TabPage page in tabs.TabPages) {
                tabs.SelectedTab=page;Application.DoEvents();
                if(page.ClientSize.Width<500 || page.ClientSize.Height<400)throw new Exception("Tab layout too small");
                report.Add("PASS rendered "+page.Text+" "+page.ClientSize);
                foreach(var grid in page.Controls.OfType<DataGridView>()) {
                    if(grid.Bounds.Height<200)throw new Exception("Grid obscured");
                    report.Add("PASS visible grid with "+grid.Columns.Count+" columns");
                }
            }
            window.Hide();Program.ShowExisting();Application.DoEvents();
            if(!window.Visible)throw new Exception("Existing hidden window did not reopen");
            report.Add("PASS hidden window reopens through shortcut message");
            File.WriteAllLines(Path.Combine(Program.Data,"ui-test.txt"),report);
            window.Dispose();
        }
    }
    public static void Run(){var results=new List<string>();try {
        Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);results.Add("PASS "+name);};
        check(!Repair.Valid("repair & del") && Repair.Valid("repair") && Repair.Valid("scan") && Repair.Valid("disk"),"Only fixed maintenance commands accepted");
        check(Repair.Conclusion("Windows Resource Protection did not find any integrity violations.",0).Contains("no integrity"),"Clean SFC conclusion parsed");
        check(Repair.Conclusion("Windows Resource Protection found corrupt files and successfully repaired them.",0).Contains("damaged files repaired"),"Repaired SFC conclusion parsed");
        check(Repair.Conclusion("unknown or localized output",0).Contains("no clean-health conclusion"),"Unknown output cannot become clean result");
        string testKey="Software\\LaptopCare\\Tests\\"+Guid.NewGuid().ToString("N");
        try { using(var source=Registry.CurrentUser.CreateSubKey(testKey+"\\Source"))using(var dest=Registry.CurrentUser.CreateSubKey(testKey+"\\Backup")) {
            var row=new StartupRow{Name="Example",Command="%LOCALAPPDATA%\\example.exe"};source.SetValue(row.Name,row.Command,RegistryValueKind.ExpandString);
            Startup.Move(source,dest,row);check(source.GetValue(row.Name)==null && dest.GetValueKind(row.Name)==RegistryValueKind.ExpandString,"Startup disable preserves registry type");
            Startup.Move(dest,source,row);check((source.GetValue(row.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string)==row.Command,"Startup restore preserves original command");
            dest.SetValue(row.Name,"conflict");bool rejected=false;try{Startup.Move(source,dest,row);}catch{rejected=true;}check(rejected && source.GetValue(row.Name)!=null,"Startup collision leaves original intact");
        }}finally{Registry.CurrentUser.DeleteSubKeyTree(testKey,false);}
        var m=new MemoryInfo{Load=95,Available=2UL*1024*1024*1024};check(Monitor.Pressure(m)=="HIGH","High load classified");m.Load=50;m.Available=400UL*1024*1024;check(Monitor.Pressure(m)=="HIGH","Low available RAM classified");
        var live=new Monitor().Read();check(live.Memory.Total>0 && live.Memory.Available<=live.Memory.Total,"Live Windows memory values valid");check(live.Apps.Count>0,"Live process inventory populated");using(var own=Process.GetCurrentProcess())check(!Monitor.SafeToClose(own),"Monitor protects itself");
        results.Add("Observed "+live.Apps.Count+" readable processes; RAM "+live.Memory.Load+"%");
        File.WriteAllLines(Path.Combine(Program.Data,"self-test.txt"),results);Environment.ExitCode=0;
    }catch(Exception e){results.Add("FAIL "+e);File.WriteAllLines(Path.Combine(Program.Data,"self-test.txt"),results);Environment.ExitCode=1;}}
}
}
