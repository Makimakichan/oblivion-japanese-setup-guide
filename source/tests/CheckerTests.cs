using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using MOJCLegacyChecker;
static class CheckerTests {
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);checks++;}
 static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static void Put(string p,string s){Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,s,new UTF8Encoding(false));}
 static Dictionary<string,string> Snapshot(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Hash(p)+"|"+File.GetLastWriteTimeUtc(p).Ticks);}
 [STAThread] static int Main(string[] args){try {
  if(args.Length>0&&args[0]=="--live") {
   var r=new Scanner(CancellationToken.None,null).Scan(args[1],args[2],args[3],true);
   Console.WriteLine(new JavaScriptSerializer {MaxJsonLength=8000000}.Serialize(new {summary=r.Summary,errors=r.Errors,items=r.Items}));return r.Errors==0?0:2;
  }
  string temp=Path.Combine(Path.GetTempPath(),"MOJC_Checker_Test_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
  try {
   string g=Path.Combine(temp,"game with 日本語"),m=Path.Combine(temp,"mo2"),d=Path.Combine(temp,"documents");
   Put(Path.Combine(g,"Oblivion.exe"),"unknown executable, not a legacy verdict");
   Put(Path.Combine(g,"Data","Oblivion.esm"),"unknown esm");
   Put(Path.Combine(g,"Data","Oblivion - Misc.bsa"),"no archive edit");
   string exact=Path.Combine(g,"Data","OBSE","Plugins","OBJP_CompleteUIStringsTranslator.dll");Put(exact,"fixture signature");
   Put(Path.Combine(g,"Data","menus","strings.xml"),"current UI must be retained");
   Put(Path.Combine(g,"obse_loader.exe"),"shared loader");
   Put(Path.Combine(g,"Oblivion.exe.Original.old"),"backup unknown");
   Put(Path.Combine(d,"obja.ini"),"old config");
   Put(Path.Combine(d,"Oblivion.ini"),"[Fonts]\nsFontFile_1=Data\\Fonts\\Petrock.fnt");
   Put(Path.Combine(m,"ModOrganizer.ini"),"[Settings]\nbase_directory=%BASE_DIR%\\instance\nmod_directory=%BASE_DIR%\\mods\nprofiles_directory=%BASE_DIR%\\profiles\noverwrite_directory=%BASE_DIR%\\overwrite");
   string b=Path.Combine(m,"instance");
   Put(Path.Combine(b,"profiles","Active","modlist.txt"),"+Mixed\n-Disabled\n");
   Put(Path.Combine(b,"profiles","Other","modlist.txt"),"-Mixed\n-Disabled\n");
   Put(Path.Combine(b,"profiles","Other","Oblivion.ini"),"[Fonts]\nsFontFile_1=Data\\Fonts\\Petrock.fnt");
   Put(Path.Combine(b,"mods","Mixed","OBSE","Plugins","OBJP_CompleteUIStringsTranslator.dll"),"fixture signature");
   Put(Path.Combine(b,"mods","Mixed","OBSE","Plugins","SkyBSA.dll"),"shared archive loader");
   Put(Path.Combine(b,"mods","Disabled","JPMOD_GOTY.esp"),"unknown legacy version");
   Put(Path.Combine(b,"mods","Disabled","OBSE","Plugins","OBJP_CompleteUIStringsTranslator.dll.mohidden"),"hidden old helper");
   Put(Path.Combine(b,"overwrite","OBSE","Plugins","obja.dll"),"old engine");
   var signatures=new[]{new[]{@"OBSE\Plugins\OBJP_CompleteUIStringsTranslator.dll",Hash(exact),"test"},new[]{@"menus\strings.xml",new string('0',64),"test"}};
   var before=Snapshot(temp);
   var r=new Scanner(CancellationToken.None,null,signatures).Scan(g,m,d,false);
   Check(r.Errors==0,"fixture complete");
   Check(r.Items.Count(x=>x.Code=="uihelper"&&x.Status=="旧版と一致")==2,"two exact helper signatures");
   Check(r.Items.Any(x=>x.Code=="uihelper"&&x.Usage.Contains("Active")&&!x.Usage.Contains("Other")),"profile-specific enabled metadata");
   Check(r.Items.Any(x=>x.Name=="JPMOD_GOTY.esp"&&x.Usage.Contains("無効")&&x.Status=="要確認"),"inactive unknown legacy is not exact");
   Check(r.Items.Any(x=>x.Name=="strings.xml"&&x.Status=="別内容・判定保留"),"same-name current UI is protected");
   Check(r.Items.Any(x=>x.Code=="shared"&&x.Name=="SkyBSA.dll"),"SkyBSA retained");
   Check(r.Items.Any(x=>x.Code=="shared"&&x.Name=="obse_loader.exe"),"xOBSE retained");
   Check(r.Items.Any(x=>x.Code=="backup"),"restore candidate is not garbage");
   Check(r.Items.Any(x=>x.Location.Contains("overwrite")&&x.Name=="obja.dll"),"overwrite scanned");
   Check(r.Items.Any(x=>x.Code=="fontIni"),"old font references detected");
   Check(r.Items.Any(x=>x.Code=="fontIni"&&x.Location.Contains("Other")),"profile font references detected");
   Check(r.Items.Any(x=>x.Code=="hidden"&&x.Status=="MO2で非表示"),"hidden DLL not called active");
   Check(Scanner.Resolve(@"\\server\share\mods",m,m)==@"\\server\share\mods","ordinary UNC preserved");
   Check(Scanner.Resolve(@"\\\\server\\share\\mods",m,m)==@"\\server\share\mods","QSettings UNC decoded");
   Check(r.Items.Single(x=>x.Code=="exe").Evidence.Contains("断定できません"),"unknown exe is not called legacy");
   Check(r.Items.Single(x=>x.Code=="bsa").Evidence.Contains("断定できません"),"timestamp is not attribution");
   var helper=r.Items.First(x=>x.Code=="uihelper");
   Check(helper.Detail.Contains("新しい機能は増えません")&&helper.Detail.Contains("ファイルの場所を開く"),"helper migration guidance and folder label");
   Check(!helper.Detail.Contains("SHA-256")&&Guide.Technical(helper).Contains("SHA-256"),"technical hashes only in detailed view");
   Check(helper.Detail.Contains("問題なく遊べているなら、作業は不要"),"working installs not encouraged to remove files");
   Check(!helper.Detail.Contains("保存フォルダーを開く"),"ambiguous folder label absent");
   Check(helper.ActionCategory=="candidate"&&r.Items.First(x=>x.Code=="shared").ActionCategory=="keep","confirmed legacy candidates separated from shared files");
   Check(r.Items.Single(x=>x.Code=="exe").ActionCategory=="review"&&r.Items.First(x=>x.Name=="strings.xml").ActionCategory=="review","unknown EXE and different UI do not become replacement instructions");
   Check(new Finding {Code="error",Status="未検査"}.ActionCategory=="error"&&r.Items.First(x=>x.Code=="backup").ActionCategory=="info","unreadable files and preserved backups distinguished");
   var unconfirmedHelper=new Finding {Code="uihelper",Status="要確認",Name="OBJP_CompleteUIStringsTranslator.dll",Evidence="",Sha256=""};
   Check(Guide.Uninstall(unconfirmedHelper).Contains("外してよいか判断できません")&&Guide.AdvancedUninstall(unconfirmedHelper)==Guide.Uninstall(unconfirmedHelper),"unknown helper cannot bypass review in detailed view");
   Check(Guide.Describe(unconfirmedHelper).Contains("中身の一致は確認できていません"),"unknown helper identity not asserted");
   var engine=new Finding {Code="legacy",Status="要確認",Name="obja.dll",Evidence="",Sha256=""};
   Check(Guide.Uninstall(engine).Contains("先に同じ販売元・同じ版の英語EXE")&&Guide.Uninstall(engine).Contains("旧パッチをもう一度実行して解除しようとしない"),"patched EXE dependency warning before removing legacy runtime");
   var plugin=new Finding {Code="legacy",Status="旧版と一致",Name="JPMOD_GOTY.esp",Evidence="",Sha256=""};
   Check(Guide.Uninstall(plugin).Contains("Missing Masters")&&Guide.Uninstall(plugin).Contains("Bashed Patch"),"plugin dependency and merged translation guidance");
   Check(Guide.Uninstall(helper).Contains("別プロファイルにも影響")&&Guide.Uninstall(helper).Contains("ファイルは複製されません"),"MO2 shared-file changes not portrayed as profile isolated");
   Check(Guide.Uninstall(helper).Contains("右側のESP一覧のチェックだけでは無効になりません"),"DLL and UI require left-pane or file handling");
   var overrideFile=new Finding {Code="originalOverride",Status="要確認",Name="Knights.esp",Evidence="",Sha256=""};
   Check(Guide.Uninstall(overrideFile).Contains("公式DLCそのものは削除しません")&&!Guide.Restore(overrideFile).Contains("ゲームファイルの整合性を確認"),"MO2 original override separated from physical file repair");
   Check(Guide.Restore(r.Items.First(x=>x.Code=="backup")).Contains("英語原本とは限りません"),"generic backup is not assumed English original");
   Check(Guide.Restore(r.Items.Single(x=>x.Code=="exe")).Contains("元の場所・元のファイル名")&&Guide.Restore(r.Items.Single(x=>x.Code=="exe")).Contains("現在のファイルをゲーム外へ控え"),"restore specifies backup and target without first deleting current EXE");
   var launch=FolderAction.Create(exact);Check(launch.FileName.EndsWith("explorer.exe")&&launch.Arguments=="/select,\""+exact+"\"","folder action selects correct file and does not execute it");
   var after=Snapshot(temp);Check(before.Count==after.Count&&before.All(x=>after[x.Key]==x.Value),"all fixture bytes and modified times unchanged");
   var cancelled=new CancellationTokenSource();cancelled.Cancel();bool cancelledOk=false;try{new Scanner(cancelled.Token,null).Scan(g,m,d,false);}catch(OperationCanceledException){cancelledOk=true;}Check(cancelledOk,"cancellation");
   var missing=new Scanner(CancellationToken.None,null).Scan(g,Path.Combine(temp,"missing"),d,false);Check(missing.Errors>0,"missing MO2 is incomplete");
   var noMo2=new Scanner(CancellationToken.None,null).Scan(g,"",d,false,false);Check(noMo2.Errors==0&&noMo2.Items.Any(x=>x.Code=="scope"),"MO2-free use explicitly scoped");
   Put(Path.Combine(m,"ModOrganizer.ini"),"mod_directory=%UNKNOWN%\\mods");var unresolved=new Scanner(CancellationToken.None,null).Scan(g,m,d,false);Check(unresolved.Errors>0,"unknown path macro is incomplete");
   // A Windows junction is created only inside this disposable fixture.
   string link=Path.Combine(temp,"junction"),target=Path.Combine(temp,"target");Directory.CreateDirectory(target);
   using(var proc=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})) {proc.StandardOutput.ReadToEnd();proc.StandardError.ReadToEnd();proc.WaitForExit();Check(proc.ExitCode==0,"fixture junction creation");}
   try {Put(Path.Combine(target,"ModOrganizer.ini"),"[Settings]");var linked=new Scanner(CancellationToken.None,null).Scan(g,link,d,false);Check(linked.Errors>0&&linked.Items.Any(x=>x.Evidence.Contains("ジャンクション")),"linked directories not followed");Check(string.Equals(ExplicitFolder.Resolve(link),target,StringComparison.OrdinalIgnoreCase),"explicitly selected root resolves to displayed real path");}finally{Directory.Delete(link);}
   using(var form=new MainForm()) {Check(form.Text.Contains("Makimakichan999"),"GUI initializes with correct credits");}
   Console.WriteLine("PASS "+checks+" checks; fixture files unchanged by scanner; GUI construction passed.");
  } finally {Directory.Delete(temp,true);}
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
