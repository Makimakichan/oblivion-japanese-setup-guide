using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Oblivion 日本語化チェック＆英語に戻すガイド")]
[assembly: AssemblyDescription("旧日本語化ファイルの確認・外す手順・元に戻す手順を案内")]
[assembly: AssemblyCompany("Makimakichan999")]
[assembly: AssemblyProduct("Oblivion 日本語化チェック＆英語に戻すガイド")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
namespace MOJCLegacyChecker {
internal static class ExplicitFolder {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,StringBuilder result,uint size,uint flags);
 internal static string Resolve(string path) {
  // Resolve only the root explicitly chosen by the user/OS. Scanner never
  // follows links encountered inside that root. Display the actual root.
  try {using(var h=CreateFile(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
   if(h.IsInvalid)return path;var b=new StringBuilder(32768);uint n=GetFinalPathNameByHandle(h,b,(uint)b.Capacity,0);
   if(n==0||n>=b.Capacity)return path;string s=b.ToString();
   if(s.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))return @"\\"+s.Substring(8);
   return s.StartsWith(@"\\?\",StringComparison.Ordinal)?s.Substring(4):s;
  }}catch(Exception){return path;}
 }
}
internal static class FolderAction {
 internal static ProcessStartInfo Create(string path) {
  string full=Path.GetFullPath(path);
  if(full.IndexOf('"')>=0||(!File.Exists(full)&&!Directory.Exists(full)))throw new IOException("対象が見つかりません。再検査してください。");
  return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),File.Exists(full)?"/select,\""+full+"\"":"\""+full+"\"") {UseShellExecute=true};
 }
}
public sealed class MainForm : Form {
 readonly TextBox game=new TextBox(), mo2=new TextBox(), docs=new TextBox();
 readonly DataGridView grid=new DataGridView();
 readonly TextBox detail=new TextBox();
 readonly Button scan=new Button(), stop=new Button(), folder=new Button(), uninstall=new Button(), technical=new Button();
 readonly CheckBox videos=new CheckBox(), useMo2=new CheckBox();
 readonly Label summary=new Label(), nextStep=new Label();
 readonly System.Collections.Generic.Dictionary<TextBox,Button> browseButtons=new System.Collections.Generic.Dictionary<TextBox,Button>();
 CancellationTokenSource pending;
 bool busy;
 Finding Selected {get {return grid.CurrentRow==null?null:grid.CurrentRow.DataBoundItem as Finding;}}
 public MainForm() {
  SuspendLayout();
  Text="Oblivion 日本語化チェック＆英語に戻すガイド 1.0.0 — Makimakichan999";
  Font=new Font("Yu Gothic UI",10F);AutoScaleMode=AutoScaleMode.Dpi;
  MinimumSize=new Size(900,700);Size=new Size(1180,900);StartPosition=FormStartPosition.CenterScreen;
  var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(16)};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,120));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,126));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
  layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
  Controls.Add(layout);
  layout.Controls.Add(new Label {Text="古い日本語化のファイルが残っているか調べます。\n自動では削除しません。今の環境で遊べている方は、見つかっても消す必要はありません。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(25,70,100)},0,0);
  var inputs=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=3};
  inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
  AddInput(inputs,0,"Oblivionのフォルダー",game);
  AddInput(inputs,1,"MO2のフォルダー",mo2);
  AddInput(inputs,2,"ゲーム設定のフォルダー",docs);
  layout.Controls.Add(inputs,0,1);
  var options=new GroupBox {Text="追加で調べるもの",Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8)};
  var optionRows=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
  for(int n=0;n<4;n++)optionRows.RowStyles.Add(new RowStyle(SizeType.Percent,25));
  useMo2.Text="MO2に入れたMODも調べる";useMo2.Checked=true;useMo2.AutoSize=true;useMo2.Dock=DockStyle.Fill;useMo2.Margin=Padding.Empty;
  useMo2.CheckedChanged+=delegate {UpdateInputs();};
  videos.Text="オープニング・エンディング動画も調べる";videos.AutoSize=true;videos.Dock=DockStyle.Fill;videos.Margin=Padding.Empty;
  videos.CheckedChanged+=async delegate {if(videos.Checked&&!busy&&IsHandleCreated)await RunScan();};
  optionRows.Controls.Add(useMo2,0,0);
  optionRows.Controls.Add(new Label {Text="MO2を使っている方はオン。使っていない方はオフにしてください。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(65,65,65),Padding=new Padding(22,0,0,0)},0,1);
  optionRows.Controls.Add(videos,0,2);
  optionRows.Controls.Add(new Label {Text="チェックするとすぐに検査します。元の英語版の動画と同じか確認します。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(65,65,65),Padding=new Padding(22,0,0,0)},0,3);
  options.Controls.Add(optionRows);layout.Controls.Add(options,0,2);
  var controls=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
  scan.Text="日本語化ファイルを調べる";scan.AutoSize=true;scan.Padding=new Padding(14,4,14,4);scan.BackColor=Color.FromArgb(0,90,158);scan.ForeColor=Color.White;scan.FlatStyle=FlatStyle.Flat;scan.FlatAppearance.BorderSize=0;
  scan.Click+=async delegate {await RunScan();};
  stop.Text="検査を止める";stop.AutoSize=true;stop.Padding=new Padding(8,4,8,4);stop.Enabled=false;stop.Click+=delegate {if(pending!=null)pending.Cancel();};
  controls.Controls.AddRange(new Control[]{scan,stop});layout.Controls.Add(controls,0,3);
  summary.Text="追加で調べるものを選び、青い『日本語化ファイルを調べる』を押してください。\n結果の行を選ぶと、下に対応方法が表示されます。";summary.Dock=DockStyle.Fill;
  layout.Controls.Add(summary,0,4);
  var legend=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true};
  foreach(var pair in new[]{new[]{"keep","緑：残す"},new[]{"candidate","黄：候補・判断を保留"},new[]{"error","赤：再検査"},new[]{"info","灰：保管・参考"}})
   legend.Controls.Add(new Label {Text=pair[1],AutoSize=true,BackColor=RowColor(pair[0]),ForeColor=Color.FromArgb(35,35,35),Padding=new Padding(7,3,7,3),Margin=new Padding(0,0,8,0),BorderStyle=BorderStyle.FixedSingle});
  legend.Controls.Add(new Label {Text="黄色でも、すぐに削除・交換しないでください。",AutoSize=true,Margin=new Padding(0,5,0,0)});
  layout.Controls.Add(legend,0,5);
  var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterWidth=7};
  grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoGenerateColumns=false;grid.RowHeadersVisible=false;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.RowTemplate.Height=28;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
  foreach(string[] c in new[]{new[]{"ActionLabel","対応の目安","19"},new[]{"DisplayStatus","確認結果","21"},new[]{"Name","ファイル名","30"},new[]{"Location","見つかった場所","30"}})grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName=c[0],HeaderText=c[1],AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=int.Parse(c[2]),MinimumWidth=90,SortMode=DataGridViewColumnSortMode.NotSortable});
  grid.DataBindingComplete+=delegate {
   foreach(DataGridViewRow row in grid.Rows) {
    var finding=row.DataBoundItem as Finding;if(finding==null)continue;
    Color color=RowColor(finding.ActionCategory);
    row.DefaultCellStyle.BackColor=color;row.DefaultCellStyle.ForeColor=Color.FromArgb(25,25,25);
    row.DefaultCellStyle.SelectionBackColor=ControlPaint.Dark(color,0.12F);row.DefaultCellStyle.SelectionForeColor=Color.Black;
   }
  };
  grid.SelectionChanged+=delegate {ShowSelected();};
  detail.Multiline=true;detail.ReadOnly=true;detail.ScrollBars=ScrollBars.Vertical;detail.WordWrap=true;detail.Dock=DockStyle.Fill;detail.BackColor=Color.White;detail.Font=new Font("Yu Gothic UI",10.5F);
  split.Panel1.Controls.Add(grid);split.Panel2.Controls.Add(detail);layout.Controls.Add(split,0,6);
  var actionArea=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
  actionArea.RowStyles.Add(new RowStyle(SizeType.Percent,55));actionArea.RowStyles.Add(new RowStyle(SizeType.Percent,45));
  nextStep.Text=Guide.NextStep(null);nextStep.Dock=DockStyle.Fill;nextStep.ForeColor=Color.FromArgb(20,65,90);nextStep.Font=new Font(Font,FontStyle.Bold);
  actionArea.Controls.Add(nextStep,0,0);
  var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
  folder.Text="② ファイルの場所を開く";uninstall.Text="① 対応方法を読む";technical.Text="詳しい検査結果を見る";
  foreach(var button in new[]{uninstall,folder,technical}){button.AutoSize=true;button.Padding=new Padding(5,3,5,3);button.Enabled=false;actions.Controls.Add(button);}
  uninstall.BackColor=Color.FromArgb(0,90,158);uninstall.ForeColor=Color.White;uninstall.FlatStyle=FlatStyle.Flat;uninstall.FlatAppearance.BorderSize=0;
  folder.Click+=delegate {if(Selected==null)return;try {Process.Start(FolderAction.Create(Selected.FilePath));}catch(Exception e){MessageBox.Show(this,e.Message,"場所を開けません",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
  uninstall.Click+=delegate {if(Selected!=null){detail.Text=Guide.Describe(Selected);detail.SelectionStart=0;detail.SelectionLength=0;detail.ScrollToCaret();}};
  technical.Click+=delegate {if(Selected!=null)detail.Text=Guide.Technical(Selected);};
  actionArea.Controls.Add(actions,0,1);layout.Controls.Add(actionArea,0,7);
  layout.Controls.Add(new Label {Text="ボタンは説明やファイルの場所を表示するだけです。削除・交換・復元は実行しません。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(65,65,65)},0,8);
  game.Text=GuessGame();
  mo2.Text=new[]{@"C:\game\MO2\Oblivion\MO2OB",Path.Combine(game.Text,"ModOrganizer"),Path.Combine(game.Text,"Mod Organizer 2")}.FirstOrDefault(x=>File.Exists(Path.Combine(x,"ModOrganizer.ini")))??"";
  UpdateInputs();
  docs.Text=ExplicitFolder.Resolve(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games","Oblivion"));
  AutoScaleDimensions=new SizeF(96F,96F);
  ResumeLayout(true);
  Shown+=async delegate {
   Rectangle available=Screen.FromControl(this).WorkingArea;
   MinimumSize=new Size(Math.Min(MinimumSize.Width,available.Width-40),Math.Min(MinimumSize.Height,available.Height-40));
   Width=Math.Min(Width,available.Width-40);Height=Math.Min(Height,available.Height-40);
   Left=available.Left+(available.Width-Width)/2;Top=available.Top+(available.Height-Height)/2;
   // TableLayoutPanel absolute rows do not reliably scale with the form on
   // older Framework hosts. Size text-bearing rows from actual font metrics.
   int h=TextRenderer.MeasureText("日本語",Font).Height;
   layout.RowStyles[0].Height=h*2+16;
   layout.RowStyles[1].Height=(h+18)*3;
   layout.RowStyles[2].Height=h*5+34;
   layout.RowStyles[3].Height=h+36;
   layout.RowStyles[4].Height=h*3+12;
   layout.RowStyles[5].Height=h*2+16;
   layout.RowStyles[7].Height=h*3+32;
   layout.RowStyles[8].Height=h*2+12;
   inputs.ColumnStyles[0].Width=TextRenderer.MeasureText("ゲーム設定のフォルダー",Font).Width+18;
   inputs.ColumnStyles[2].Width=TextRenderer.MeasureText("参照…",Font).Width+30;
   foreach(DataGridViewColumn col in grid.Columns) col.MinimumWidth=(int)(70*Math.Max(1F,h/16F));
   grid.RowTemplate.Height=h+12;
   layout.PerformLayout();
   split.SplitterDistance=Math.Max(split.Panel1MinSize,Math.Min(split.Height/2,split.Height-split.Panel2MinSize-split.SplitterWidth));
   // Opening the tool gives a report immediately when both locations are found.
   // Users can change either location and re-scan. This only reads files.
   if(File.Exists(Path.Combine(game.Text,"Oblivion.exe"))&&File.Exists(Path.Combine(mo2.Text,"ModOrganizer.ini")))await RunScan();
  };
  FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){pending.Cancel();e.Cancel=true;summary.Text="検査を中止しています。完了後に閉じてください。";}};
 }
 void AddInput(TableLayoutPanel p,int row,string label,TextBox box) {
  p.RowStyles.Add(new RowStyle(SizeType.Percent,33.333F));
  p.Controls.Add(new Label {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);
  box.Dock=DockStyle.Fill;box.Margin=new Padding(3,6,3,3);p.Controls.Add(box,1,row);
  var b=new Button {Text="参照…",Dock=DockStyle.Fill};
  b.Click+=delegate {using(var dialog=new FolderBrowserDialog {Description=label+"を選択",ShowNewFolderButton=false,SelectedPath=box.Text})if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=ExplicitFolder.Resolve(dialog.SelectedPath);};
  browseButtons.Add(box,b);p.Controls.Add(b,2,row);
 }
 static Color RowColor(string category) {
  switch(category) {
   case "keep":return Color.FromArgb(230,245,233);
   case "candidate":case "review":return Color.FromArgb(255,244,209);
   case "error":return Color.FromArgb(253,229,227);
   default:return Color.FromArgb(238,240,243);
  }
 }
 void UpdateInputs() {
  game.ReadOnly=docs.ReadOnly=busy;mo2.ReadOnly=busy;mo2.Enabled=useMo2.Checked;
  useMo2.Enabled=videos.Enabled=!busy;
  foreach(var pair in browseButtons)pair.Value.Enabled=!busy&&(pair.Key!=mo2||useMo2.Checked);
 }
 void ShowSelected() {
  var f=Selected;nextStep.Text=Guide.NextStep(f);detail.Text=f==null?"一覧からファイルを選択すると、確認内容と詳しい手順が表示されます。":f.Detail;
  folder.Enabled=f!=null&&(File.Exists(f.FilePath)||Directory.Exists(f.FilePath));uninstall.Enabled=technical.Enabled=f!=null;
 }
 async Task RunScan() {
  if(busy)return;
  busy=true;scan.Enabled=false;scan.Text="調べています…";stop.Enabled=true;UpdateInputs();
  pending=new CancellationTokenSource();grid.DataSource=null;detail.Clear();summary.Text="検査中です。ファイルは変更しません。終わるまで、そのままお待ちください。";
  string g=game.Text.Trim(),m=mo2.Text.Trim(),d=docs.Text.Trim();bool movie=videos.Checked,checkMo2=useMo2.Checked;
  try {
   var report=await Task.Run(()=>new Scanner(pending.Token,null).Scan(g,m,d,movie,checkMo2));
   grid.DataSource=report.Items.OrderBy(x=>x.ActionCategory=="error"?0:x.ActionCategory=="candidate"?1:x.ActionCategory=="review"?2:x.ActionCategory=="keep"?3:4).ToList();summary.Text=report.Summary;
  } catch(OperationCanceledException){summary.Text="中止しました。検査は未完了です。ファイルは変更していません。";}
    catch(Exception e){summary.Text="検査できませんでした。";MessageBox.Show(this,e.Message,"指定を確認してください",MessageBoxButtons.OK,MessageBoxIcon.Information);}
  finally {busy=false;scan.Enabled=true;scan.Text="日本語化ファイルを調べる";stop.Enabled=false;UpdateInputs();pending.Dispose();pending=null;}
 }
 static string GuessGame() {
  foreach(var path in new[]{@"C:\game\Steam\steamapps\common\Oblivion",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Steam\steamapps\common\Oblivion")})if(File.Exists(Path.Combine(path,"Oblivion.exe")))return path;
  try {using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Bethesda Softworks\Oblivion")){string path=key==null?null:key.GetValue("Installed Path") as string;if(!string.IsNullOrEmpty(path)&&File.Exists(Path.Combine(path,"Oblivion.exe")))return path;}}catch(System.Security.SecurityException){}
  return "";
 }
}
static class Program {
 [STAThread] static void Main() {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
}
}
