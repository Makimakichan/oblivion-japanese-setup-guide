using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MOJCLegacyChecker {
public sealed class Finding {
 public string Status { get; set; }
 public string Name { get; set; }
 public string Location { get; set; }
 public string Usage { get; set; }
 public string FilePath { get; set; }
 public string Code { get; set; }
 public string Evidence { get; set; }
 public string Sha256 { get; set; }
 public string DisplayStatus { get { return Guide.FriendlyStatus(Status); } }
 public string ActionCategory { get { return Guide.ActionCategory(this); } }
 public string ActionLabel { get { return Guide.ActionLabel(this); } }
 public string DisplayUsage { get { return Usage=="物理ファイル"?"この場所にあるファイル":Usage.Replace("有効指定：","使う設定：").Replace("（競合勝者・実読込は未確認）",""); } }
 public string Detail { get { return Guide.Describe(this); } }
}
public sealed class Report {
 public readonly List<Finding> Items = new List<Finding>();
 public int Errors;
 public string Summary {
  get { return string.Format("古い日本語化と同じファイル：{0} 件　確認が必要：{1} 件　同じ名前の別ファイル：{2} 件　調べられなかった項目：{3} 件\r\n見つかっても、今の環境で遊べていれば消す必要はありません。切り替えたい場合だけ、下の説明を確認してください。",
   Items.Count(x=>x.Status=="旧版と一致"), Items.Count(x=>x.Status=="要確認"), Items.Count(x=>x.Status=="別内容・判定保留"), Errors); }
 }
}
public sealed class Scanner {
 readonly CancellationToken cancel;
 readonly Action<string> progress;
 readonly Report report = new Report();
 readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,List<string>> known;
 public Scanner(CancellationToken token, Action<string> notify) : this(token,notify,KnownData.Legacy) {}
 internal Scanner(CancellationToken token, Action<string> notify, string[][] signatures) {
  cancel=token; progress=notify ?? delegate {};
  known=signatures.GroupBy(x=>x[0],StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.Select(y=>y[1]).ToList(),StringComparer.OrdinalIgnoreCase);
 }
 void Add(string code,string status,string path,string location,string usage,string evidence,string hash) {
  report.Items.Add(new Finding { Code=code,Status=status,Name=Path.GetFileName(path),FilePath=path,Location=location,Usage=usage,Evidence=evidence,Sha256=hash??"" });
 }
 void Error(string path,string message) {
  if(report.Items.Any(x=>x.Code=="error"&&string.Equals(x.FilePath,path,StringComparison.OrdinalIgnoreCase)&&x.Evidence==message))return;
  report.Errors++; Add("error","未検査",path,"検査範囲","未確認",message,null);
 }
 bool Safe(string path) {
  // Refuse every reparse component, including links in a selected root's ancestors.
  try {
   string full=Path.GetFullPath(path);
   for(string p=full; !string.IsNullOrEmpty(p); p=Path.GetDirectoryName(p)) {
    if ((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) { Error(path,"リンク／ジャンクションをたどらず、検査を省略しました。リンク先の実フォルダーを指定してください。"); return false; }
    if (p==Path.GetPathRoot(p)) break;
   }
   return true;
  } catch(Exception e) { Error(path,"パスを確認できませんでした："+e.Message); return false; }
 }
 string[] Entries(string folder,bool dirs,bool optional) {
  cancel.ThrowIfCancellationRequested();
  // Directory.Exists alone hides access errors. Enumerate and distinguish absence.
  try {
   FileAttributes a=File.GetAttributes(folder);
   if ((a&FileAttributes.Directory)==0) { Error(folder,"フォルダーではありません。");return new string[0]; }
   if(!Safe(folder)) return new string[0];
   return dirs?Directory.GetDirectories(folder):Directory.GetFiles(folder);
  } catch(DirectoryNotFoundException) { if(!optional) Error(folder,"指定フォルダーがありません。"); }
    catch(FileNotFoundException) { if(!optional) Error(folder,"指定フォルダーがありません。"); }
    catch(Exception e) { Error(folder,"一覧を読み取れませんでした："+e.Message); }
  return new string[0];
 }
 bool Present(string path) {
  try { return (File.GetAttributes(path)&FileAttributes.Directory)==0; }
  catch(FileNotFoundException) { return false; }
  catch(DirectoryNotFoundException) { return false; }
  catch(Exception e) { Error(path,"存在確認に失敗しました："+e.Message);return false; }
 }
 string Hash(string path) {
  cancel.ThrowIfCancellationRequested();
  if(!Safe(path)) return null;
  try {
   // Deny concurrent writers during hashing. Locked/changing files are not a clean result.
   using(FileStream f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
   using(SHA256 sha=SHA256.Create()) {
    byte[] buffer=new byte[1024*1024]; int n;
    while((n=f.Read(buffer,0,buffer.Length))!=0) { cancel.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,n,null,0); }
    sha.TransformFinalBlock(new byte[0],0,0);
    return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
   }
  } catch(OperationCanceledException) { throw; }
    catch(Exception e) { Error(path,"内容を読み取れませんでした："+e.Message);return null; }
 }
 public static bool IsLegacyName(string filename) {
  return Regex.IsMatch(filename,@"^(obja|objp|jpmod|jp_dlc|jp_movie|jp_books|jp_names|jp_dialogue|jp_quests|jp_ui)",RegexOptions.IgnoreCase)
   || Regex.IsMatch(filename,@"^(TES4_JaPatch|BSAのタイムスタンプ変更)",RegexOptions.IgnoreCase);
 }
 static bool IsDocument(string rel) {
  string e=Path.GetExtension(rel).ToLowerInvariant();
  return e==".txt"||e==".md"||e==".pdb"||e==".cpp"||e==".h"||rel.IndexOf(@"\Source\",StringComparison.OrdinalIgnoreCase)>=0;
 }
 static bool IsBackup(string name) { return Regex.IsMatch(name,@"\.(old|bak|backup)$",RegexOptions.IgnoreCase); }
 static bool IsShared(string name) {
  return Regex.IsMatch(name,@"^(obse(_.*)?\.(dll|exe)|xobse.*\.(dll|exe)|SkyBSA\.dll|MenuQue\.dll|NorthernUI\.dll)$",RegexOptions.IgnoreCase);
 }
 void InspectFile(string root,string rel,string origin,string usage) {
  cancel.ThrowIfCancellationRequested();
  string path=Path.GetFullPath(Path.Combine(root,rel));
  if(!Present(path)||!seen.Add(path)) return;
  if(IsShared(Path.GetFileName(path))) { if(Safe(path))Add("shared","維持する部品",path,origin,usage,"日本語化専用ではない共通部品です。旧日本語化という理由だけで削除しないでください。",null);return; }
  string hash=Hash(path); if(hash==null)return;
  List<string> matches;
  bool exact=known.TryGetValue(rel,out matches)&&matches.Contains(hash);
  bool legacy=IsLegacyName(Path.GetFileName(path));
  string code=rel.EndsWith(".mohidden",StringComparison.OrdinalIgnoreCase)?"hidden":IsDocument(rel)?"document":IsBackup(rel)?"backup":Path.GetFileName(path).Equals("OBJP_CompleteUIStringsTranslator.dll",StringComparison.OrdinalIgnoreCase)?"uihelper":Path.GetExtension(path).Equals(".bat",StringComparison.OrdinalIgnoreCase)?"timestampTool":legacy?"legacy":"sharedPath";
  string status=code=="hidden"?"MO2で非表示":code=="document"?"資料・実行対象外":code=="backup"?"復元候補":exact?"旧版と一致":legacy?"要確認":"別内容・判定保留";
  Add(code,status,path,origin,usage,exact?"記録した旧配布物／旧導入ファイルと SHA-256 が一致しました。":legacy?"旧日本語化に関係する名前です。内容の一致は確認できていません。名前だけでは削除を判断しません。":"旧版と同じ配置名ですが、内容が違います。別のUI MODや編集済みファイルの可能性があります。",hash);
 }
 void DataRoot(string root,string origin,string usage) {
  progress(origin); cancel.ThrowIfCancellationRequested();
  foreach(string rel in known.Keys) InspectFile(root,rel,origin,usage);
  foreach(string sub in new[]{"",@"OBSE\Plugins",@"Fonts"}) {
   foreach(string f in Entries(Path.Combine(root,sub),false,true)) {
    string name=Path.GetFileName(f);
    if(IsLegacyName(name)||IsShared(name)||IsBackup(name)||Regex.IsMatch(name,@"(Petrock|SAIMOJI|TKaisho)",RegexOptions.IgnoreCase))
     InspectFile(root,Path.Combine(sub,name),origin,usage);
   }
  }
 }
 void Exe(string game) {
  string p=Path.Combine(game,"Oblivion.exe"), h=Hash(p);if(h==null)return;
  string laa="PEヘッダーを確認できませんでした。";
  try { using(var f=File.OpenRead(p)) using(var b=new BinaryReader(f)) {
   if(b.ReadUInt16()!=0x5a4d)throw new InvalidDataException();f.Position=0x3c;int off=b.ReadInt32();
   if(off<64||off>f.Length-24)throw new InvalidDataException(); f.Position=off;if(b.ReadUInt32()!=0x4550)throw new InvalidDataException();f.Position=off+22;
   laa=(b.ReadUInt16()&0x20)!=0?"4GB対応（LAA）は有効です。":"4GB対応（LAA）は無効です。";
  }}catch(Exception) { /* Unknown file is reported below; never infer a legacy patch. */ }
  bool plain=h==KnownData.SteamOriginalExe, large=h==KnownData.SteamLaaExe;
  Add("exe",plain||large?"既知のSteam版":"要確認",p,"ゲーム本体","物理ファイル",(plain?"記録したSteam英語版原本と一致。":large?"記録したSteam英語版にLAAだけを有効化した内容と一致。":"登録したSteam版の内容と異なります。GOG版・別ビルド・別パッチもあり得るため、旧日本語パッチ適用とは断定できません。")+"\r\n"+laa,h);
 }
 void Originals(string data) {
  foreach(string[] item in KnownData.Originals) {
   string p=Path.Combine(data,item[0]); if(!Present(p))continue;
   string h=Hash(p);if(h==null)continue;
   Add("original",h==item[1]?"英語原本と一致":"要確認",p,"ゲーム本体 Data","物理ファイル",h==item[1]?"記録した英語原本と内容が一致しました。MO2側の同名上書きは別途確認してください。":"登録した英語原本と異なります。版の違いや正当な修正もあるため、直ちに削除・復元しないでください。",h);
  }
 }
 void Movies(string data) {
  string[][] videos={new[]{"OblivionIntro.bik","5eb30b0e8f022085a367d282ccb9e8007eecc6edc4d2f2e10d8625bc92a2830e"},new[]{"OblivionOutro.bik","6a8086f3b3f98894cc4c36af6755382315bac0147dbd5fe2545734f34b5e4583"}};
  foreach(var v in videos) {string p=Path.Combine(data,"Video",v[0]);if(!Present(p))continue;string h=Hash(p);if(h!=null)Add("movie",h==v[1]?"原本動画と一致":"要確認",p,"ゲーム本体 Video","物理ファイル",h==v[1]?"Steam検証後の原本動画と一致しました。":"原本動画と異なります。字幕焼き込み版などの可能性があります。GOG版等との同一性は未確認です。",h);}
 }
 static Dictionary<string,string> ReadIni(string file) {
  var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var line in File.ReadAllLines(file)) {int i=line.IndexOf('=');if(i<1||line.TrimStart().StartsWith(";"))continue;d[line.Substring(0,i).Trim()]=line.Substring(i+1).Trim().Trim('"');}return d;
 }
 internal static string Resolve(string value,string root,string baseDir) {
  // QSettings escapes backslashes; also accept an ordinary UNC path pasted by a user.
  if(value.StartsWith(@"\\\\",StringComparison.Ordinal)||!value.StartsWith(@"\\",StringComparison.Ordinal))value=value.Replace(@"\\",@"\");
  value=Environment.ExpandEnvironmentVariables(value.Replace("%BASE_DIR%",baseDir).Replace("%BASE_DIR",baseDir));
  if(value.Contains("%")||value.StartsWith("@",StringComparison.Ordinal))throw new InvalidDataException("未対応のMO2パス表記です："+value);
  return Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(root,value));
 }
 void Mo2(string root) {
  if(string.IsNullOrWhiteSpace(root)) {Error("MO2 未指定","MO2は検査していません。使っている場合はインスタンスのフォルダーを指定してください。");return;}
  string ini=Path.Combine(root,"ModOrganizer.ini");
  if(!Present(ini)||!Safe(ini)) {Error(root,"ModOrganizer.ini があるインスタンスのフォルダーを指定してください。プログラム本体とは別の場所の場合があります。");return;}
  string mods,profiles,overwrite;
  try {
   var cfg=ReadIni(ini);string v;string baseDir=cfg.TryGetValue("base_directory",out v)&&v.Length>0?Resolve(v,root,root):root;
   mods=cfg.TryGetValue("mod_directory",out v)&&v.Length>0?Resolve(v,root,baseDir):Path.Combine(baseDir,"mods");
   profiles=cfg.TryGetValue("profiles_directory",out v)&&v.Length>0?Resolve(v,root,baseDir):Path.Combine(baseDir,"profiles");
   overwrite=cfg.TryGetValue("overwrite_directory",out v)&&v.Length>0?Resolve(v,root,baseDir):Path.Combine(baseDir,"overwrite");
  }catch(Exception e){Error(ini,"MO2の配置を解決できませんでした："+e.Message);return;}
  var enabled=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);int profileCount=0;int profileErrors=report.Errors;
  foreach(var dir in Entries(profiles,true,false)) {
   string ml=Path.Combine(dir,"modlist.txt");
   if(!Present(ml)||!Safe(ml)) {Error(ml,"MOD有効状態を読めません。");continue;}
   try {
    foreach(var line in File.ReadAllLines(ml)) if(line.StartsWith("+")) {string name=line.Substring(1);List<string> list;if(!enabled.TryGetValue(name,out list))enabled[name]=list=new List<string>();list.Add(Path.GetFileName(dir));}
    profileCount++;
    FontIni(Path.Combine(dir,"Oblivion.ini"),"MO2プロファイル："+Path.GetFileName(dir));
   }catch(Exception e){Error(ml,e.Message);}
  }
  bool profilesOk=profileCount>0&&report.Errors==profileErrors;
  if(profileCount==0)Error(profiles,"プロファイルを確認できませんでした。有効／無効は未確認です。");
  foreach(string dir in Entries(mods,true,false)) {
   if(!Safe(dir))continue;
   List<string> use; string name=Path.GetFileName(dir);
   string usage=enabled.TryGetValue(name,out use)?"有効指定："+string.Join("、",use):profilesOk?"確認した全プロファイルで無効":"有効状態は未確認";
   DataRoot(dir,"MO2："+name,usage+"（競合勝者・実読込は未確認）");
   foreach(var item in KnownData.Originals) {
    string p=Path.Combine(dir,item[0]);if(!Present(p))continue;string h=Hash(p);if(h==null)continue;
    Add("originalOverride","要確認",p,"MO2："+name,usage,"原本と同じ名前の上書きがあります。翻訳済み原本や別の正当な変更か、内容・競合画面で確認してください。",h);
   }
  }
  DataRoot(overwrite,"MO2：overwrite","内容がある場合はMO2で上書きされ得ます。実読込は未確認");
 }
 void FontIni(string f,string origin) {
  if(!Present(f)||!Safe(f))return;
  try {string[] refs=File.ReadAllLines(f).Where(x=>Regex.IsMatch(x,@"Petrock|SAIMOJI|TKaisho|obja",RegexOptions.IgnoreCase)).ToArray();if(refs.Length>0)Add("fontIni","要確認",f,origin,"INIに記載（実際の適用は未確認）","旧フォント／OBJAを指す設定があります：\r\n"+string.Join("\r\n",refs),null);}catch(Exception e){Error(f,e.Message);}
 }
 public Report Scan(string game,string mo2,string documents,bool movies,bool inspectMo2=true) {
  game=Path.GetFullPath(game);
  if(!Present(Path.Combine(game,"Oblivion.exe")))throw new ArgumentException("Oblivion.exe があるフォルダーを指定してください。");
  if(!Safe(game))return report;
  Exe(game);DataRoot(game,"ゲーム本体の直下","物理ファイル");
  string data=Path.Combine(game,"Data");
  if(Entries(data,false,false).Length>0) {DataRoot(data,"ゲーム本体 Data","物理ファイル");Originals(data);if(movies)Movies(data);}
  foreach(string f in Entries(data,false,true).Where(x=>Path.GetExtension(x).Equals(".bsa",StringComparison.OrdinalIgnoreCase))) {
   if(!Safe(f))continue;
   Add("bsa","日時情報",f,"ゲーム本体 Data","物理ファイル","更新日時："+File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm:ss")+"\r\n日時だけで旧BATの実行有無は断定できません。内容は読み込んでいません。",null);
  }
  if(!string.IsNullOrWhiteSpace(documents)) {
   foreach(string f in Entries(documents,false,true)) if(Path.GetFileName(f).Equals("obja.ini",StringComparison.OrdinalIgnoreCase))InspectFile(documents,Path.GetFileName(f),"ユーザー設定","ゲーム外の設定");
   foreach(string f in Entries(documents,false,true).Where(x=>Regex.IsMatch(Path.GetFileName(x),@"^Oblivion.*\.ini$",RegexOptions.IgnoreCase))) {
    FontIni(f,"ユーザー設定");
   }
  }
  if(inspectMo2)Mo2(mo2);
  else Add("scope","MO2は対象外","MO2","利用者の指定","調べていません","MO2を調べない指定で検査しました。",null);
  return report;
 }
}
public static class Guide {
 public static string ActionCategory(Finding f) {
  if(f.Code=="error"||f.Status=="未検査")return "error";
  if(new[]{"document","backup","bsa","timestampTool","hidden","scope"}.Contains(f.Code))return "info";
  if(f.Status=="旧版と一致")return "candidate";
  if(f.Status=="要確認"||f.Status=="別内容・判定保留")return "review";
  if(f.Code=="shared"||f.Status=="既知のSteam版"||f.Status=="英語原本と一致"||f.Status=="原本動画と一致")return "keep";
  return "review";
 }
 public static string ActionLabel(Finding f) {
  switch(ActionCategory(f)) {
   case "keep":return "残す";
   case "candidate":return "交換・取り外し候補";
   case "review":return "判断を保留";
   case "error":return "再検査が必要";
   default:return "保管・参考";
  }
 }
 public static string FriendlyStatus(string s) {
  switch(s) {
   case "旧版と一致": return "古い日本語化と同じ";
   case "要確認": return "確認が必要";
   case "別内容・判定保留":return "同じ名前・中身は別";
   case "維持する部品":return "残してください";
   case "既知のSteam版":return "確認できたSteam版";
   case "未検査":return "調べられませんでした";
   case "資料・実行対象外":return "説明書などの資料";
   case "復元候補":return "予備のファイル";
   default:return s;
  }
 }
 public static string NextStep(Finding f) {
  if(f==null)return "一覧からファイルを選ぶと、次にすることがここに表示されます。";
  switch(ActionCategory(f)) {
   case "keep":return "次にすること：このファイルは残します。作業は不要です。";
   case "candidate":return "次にすること：英語へ戻す・別の日本語化へ替える場合だけ、①で手順を読んでください。";
   case "review":return "次にすること：まだ削除・交換しません。①で確認方法を読んでください。";
   case "error":return "次にすること：①で調べられなかった理由と確認方法を読み、検査をやり直します。";
   default:return f.Code=="scope"?"次にすること：MO2を使う方は、上の追加項目にチェックして調べ直してください。":"次にすること：保管用・参考の項目です。そのまま残してください。";
  }
 }
 public static string Describe(Finding f) {
  string restoreHeading=(f.Code=="exe"||f.Code=="original"||f.Code=="movie")?"元の英語ファイルを用意する方法":"作業を取り消して、変更前の状態に戻す方法";
  return f.Name+"\r\n対応の目安："+f.ActionLabel+" ／ "+f.DisplayStatus+"\r\n\r\n【このファイルについて】\r\n"+Role(f)+"\r\n\r\n【このファイルへの対応】\r\n"+Uninstall(f)+"\r\n\r\n【"+restoreHeading+"】\r\n"+Restore(f);
 }
 public static string Technical(Finding f) {
  return f.Status+" — "+f.Name+"\r\n\r\n場所："+f.FilePath+"\r\n配置："+f.Location+"\r\n有効状態："+f.Usage+"\r\n\r\n【確認した内容】\r\n"+f.Evidence+(f.Sha256.Length>0?"\r\nSHA-256："+f.Sha256:"")+"\r\n\r\n【詳細な対応方法】\r\n"+AdvancedUninstall(f)+"\r\n\r\n【詳細な復元方法】\r\n"+AdvancedRestore(f);
 }
 static string Role(Finding f) {
  switch(f.Code) {
   case "uihelper":return f.Status=="旧版と一致"?"日誌の日付や召喚生物の名前などを日本語向けにする、登録済みの追加機能と一致しました。文字を描くエンジンとは担当が違います。":"旧UI補助DLLと同じ名前ですが、中身の一致は確認できていません。別版や変更済みファイルの可能性があるため、名前だけで外しません。";
   case "exe":return "Oblivionを起動する本体です。消すとゲームを起動できなくなります。";
   case "original":return "ゲーム本編または追加コンテンツ（DLC）のデータです。消さずに残してください。";
   case "originalOverride":return "MO2の中に、ゲーム本体と同じ名前のデータがあります。古い翻訳か、別のMODの変更か、まだ判断できません。";
   case "movie":return "オープニング、またはエンディングの動画です。";
   case "shared":return "ほかのMODでも使う部品の名前です。旧日本語化を外すためだけに消しません。この項目では中身の正しさや改造の有無までは確認していません。";
   case "bsa":return "ゲームの画像や音などをまとめたデータです。この項目では、ファイルの日付だけを確認しています。";
   case "timestampTool":return "ゲームのデータを読む順序に関係する、古い日付変更ツールです。置いてあるだけでは動きません。";
   case "document":return "説明書や開発用の資料です。これ自体がゲームの日本語表示を動かすものではありません。";
   case "backup":return "何かを変更したときの予備ファイルと思われます。名前だけでは、正しい元データか判断できません。";
   case "hidden":return "MO2ですでに使わない状態にしてあるファイルです。";
   case "fontIni":return "設定ファイルに旧フォント・OBJAに関連する文字が見つかりました。コメントや現在のUIでも使う設定の可能性があります。";
   case "error":return "この場所は調べられませんでした。古いファイルがない、という意味ではありません。\r\n"+f.Evidence;
   case "scope":return "『MO2に入れたMODも調べる』のチェックが外れています。今回はゲーム本体と設定フォルダーだけを調べています。";
   default:return f.Status=="旧版と一致"?"記録してある古い日本語化のファイルと、中身が同じでした。見つかっただけで不具合があるとは限りません。":"古い日本語化と同じ名前ですが、中身は同じと確認できていません。今使っているMODのファイルかもしれません。";
  }
 }
 public static string Uninstall(Finding f) {
  if(f.Code=="scope")return "MO2を使っていない方は、そのままで大丈夫です。使っている方は、画面上部の『MO2に入れたMODも調べる』にチェックを入れて調べ直してください。";
  if(f.Code=="shared"||f.Code=="document"||f.Code=="backup"||f.Code=="bsa"||f.Code=="timestampTool"||f.Code=="hidden")return "今は消さずに残してください。この項目が見つかっただけなら、作業は不要です。";
  if(f.Code=="exe"||f.Code=="original"||f.Code=="movie")return (f.Status=="要確認"?"記録してある元データとは違いました。ただし、販売元やゲームの版が違うだけの場合もあります。今すぐ削除せず、必要なら下の『元の英語ファイルを用意する方法』を確認してください。":"確認した範囲では、元データとして認識できました。このファイルに対する作業は不要です。")+(f.Code=="exe"&&f.Evidence.Contains("LAA）は有効")?"\r\nゲームが使えるメモリーを増やす『4GB対応』も有効です。これだけを理由に旧日本語化とは判断しません。":"");
  if(f.Code=="error")return "画面上部の場所が正しいか確認して、もう一度調べてください。リンク先を調べられない場合は、実際にファイルが入っている場所を『参照…』で選び直します。";
  if(f.Code=="fontIni")return "設定ファイル全体は消さないでください。先に別の場所へコピーを残します。コメント行を含む場合があるので、見つかった文字だけで書き換えません。\r\n別の日本語化・UIを使う場合：その導入説明に従ってフォント設定を変更します。\r\n標準の英語UIへ戻す場合：同じ版の未変更のOblivion_default.iniなどで標準の[Fonts]設定を確認し、実際に使われるOblivion.iniの該当設定だけを戻します。INI全体を上書きしません。\r\nMO2でプロファイル専用の設定を使っている場合は、そのプロファイルのINIも確認します。フォント設定を戻す前に、参照先のフォントだけを外さないでください。分からなければ変更を止めてください。";
  if(f.Code=="originalOverride")return "MO2の上書きファイルです。英語原本と同名でも、翻訳や別の修正が入っている場合があります。いまは残し、MO2の提供元・競合画面とMODの説明で役割を確認してください。\r\n不要な旧翻訳だけの上書きだと確認できた場合は、対象プロファイルでそのMODを無効にします。混在MODの場合は、対象だけを非表示にする前に、他の機能への影響も確認します。\r\nゲーム本体側の同名データも英語原本か確認してください。Steamの修復だけでは、MO2内の上書きは取り外せません。Oblivion.esmや公式DLCそのものは削除しません。";
  if(f.Status!="旧版と一致")return "このツールだけでは、外してよいか判断できません。消さずに残してください。『詳しい検査結果を見る』で場所と内容を確認し、導入したMODの説明と照らし合わせてください。"+EngineWarning(f);
  return "今の環境で問題なく遊べているなら、作業は不要です。英語に戻す場合や、別の日本語化へ替える場合だけ、次の順で進めます。\r\n\r\n1. ゲームを終了し、セーブ・MOD・設定の控えを先に作ります。\r\n2. 『ファイルの場所を開く』で対象を確認します。同じ内容でも、現在のUIが再利用している場合があります。今使うMODの必要ファイルなら残します。\r\n3. MO2で旧日本語化だけをまとめたMODなら、試験用にコピーしたプロファイルで、そのMODのチェックを外します。DLLやUIファイルは、右側のESP一覧のチェックだけでは無効になりません。\r\n4. 混在MODなら、MO2の対象MODの情報画面にあるファイル一覧で、確認した旧ファイルだけを非表示にします。ファイルの非表示・移動は同じMODフォルダーを使う別プロファイルにも影響します。プロファイルをコピーしただけではファイルは複製されません。\r\n5. ゲームへ直接入れたファイルやoverwrite内の対象は、ゲームとMO2を終了してから、確認した旧ファイルだけを外へ移して保管します。元の場所をメモし、フォルダー全体は消しません。\r\n6. 他のMODやゲーム本体側の同名ファイルが現れるため、再検査とMO2の競合確認を行います。本編・DLC・動画を書き換えていた場合は、英語原本の復元も別に必要です。Steamの修復は、後から追加した旧DLLやUIを取り除く手順の代わりにはなりません。\r\n7. 使う日本語化・UIの導入説明に従い、ゲーム内の表示を確認します。最初は元のセーブへ上書き保存しないでください。"+EngineWarning(f)+DependencyWarning(f)+(f.Code=="uihelper"?"\r\n\r\nこのファイルを外すだけでは、新しい機能は増えません。切り替え先が日付と召喚生物名の表示に対応していることも確認してください。":"");
 }
 static string EngineWarning(Finding f) {
  return Regex.IsMatch(f.Name??"",@"^obja.*\.(dll|exe)$",RegexOptions.IgnoreCase)?"\r\n\r\n【旧エンジンを外す順序】\r\nGOG版・パッケージ版などでOblivion.exeへ日本語パッチを当てている場合は、先に同じ販売元・同じ版の英語EXEを用意します。日本語パッチ済みEXEを残したまま、必要なobja.dllだけを外すと起動できなくなる可能性があります。英語EXEへの復元と旧エンジンの取り外しを終えてから起動します。旧パッチをもう一度実行して解除しようとしないでください。MO2やショートカットが旧obja_loader.exeを起動する設定なら、移行先の手順に合わせて起動先も変更します。":"";
 }
 static string DependencyWarning(Finding f) {
  string ext=Path.GetExtension(f.Name??"");
  if(ext.Equals(".esp",StringComparison.OrdinalIgnoreCase)||ext.Equals(".esm",StringComparison.OrdinalIgnoreCase))return "\r\n\r\n【翻訳プラグインを外す前に】\r\nそのESP/ESMを必要とするパッチが残っていないか確認します。『必要なファイルがない』『Missing Masters』が出たまま起動しません。Bashed Patchなどへ旧翻訳を取り込んでいる場合は、新しい構成に合わせて作り直す必要があります。このツールは依存関係やセーブの移行可否を判定しません。";
  if(f.Code=="sharedPath")return "\r\n\r\n【UI・画像を外す前に】\r\n字幕背景などは複数ファイルで動きます。画像だけを抜くなど、必要な組み合わせを途中で崩さず、今使うUIの説明に従って旧機能一式を切り替えます。";
  return "";
 }
 public static string Restore(Finding f) {
  if(f.Code=="error"||f.Code=="scope")return "このツールは何も変更していないので、戻す作業はありません。";
  if(f.Code=="shared"||f.Code=="document"||f.Code=="bsa"||f.Code=="timestampTool"||f.Code=="hidden")return "何も変更していなければ、戻す作業はありません。";
  if(f.Code=="backup")return "予備ファイルが英語原本とは限りません。どの変更で作られた控えか、対応する販売元・版・元の名前を確認します。分からないものは実行・上書きせず保管します。確認できた場合だけ、現在のファイルも別に控えたうえで手動で戻します。";
  if(f.Code=="originalOverride")return "MO2で外した上書きを元へ戻す場合は、同じプロファイルでMODを再び有効にするか、非表示にしたファイルを表示へ戻します。新しい上書きと競合しないことも確認します。\r\n英語原本を使いたい場合は、不要と確認できた旧上書きを外し、ゲーム本体側の同名データが原本であることを確認します。公式データの有効設定まで外さないでください。";
  if(f.Code=="exe"||f.Code=="original"||f.Code=="movie")return "【正しい予備ファイルがある場合】\r\n同じ販売元・同じ版の英語原本か確認します。『old』『English』『backup』という名前だけでは判断できません。ゲームとMO2を終了し、現在のファイルをゲーム外へ控えてから、確認した原本を元の場所・元のファイル名へ戻します。予備ファイルも別に残してください。\r\n\r\n【予備ファイルがない場合】\r\n先にセーブ、MOD、設定を別の場所へコピーして保管します。\r\nSteam版：SteamのライブラリでOblivionを右クリック → プロパティ → インストール済みファイル →『ゲームファイルの整合性を確認』。\r\nGOG版：購入した同じ版の公式インストーラーを使い、既存の環境と混ざらない別フォルダーから英語原本を用意します。インストール先を分けられない場合は上書きで進めず、販売元の修復方法を確認してください。\r\n\r\n修復は選んだ1個だけでなく、EXE・DLC・BSA・動画なども元に戻す場合があります。後から追加した旧DLLやUI、MO2内の上書き、個人のINI設定を片付ける代わりにはなりません。\r\n修復後は4GB対応、起動先、UI設定を確認します。MO2を使う方はMO2から起動を確認してください。別販売元のEXEや、無関係なダウンロード元の原本を混ぜないでください。";
  return "ゲームを終了します。MO2でチェックを外しただけなら、同じプロファイルで同じMODにチェックを入れ直します。非表示にしたファイルはMO2で表示に戻します。\r\n手動で移したファイルは、MO2も終了してからメモした元の場所へ戻します。別の新しいファイルがある場合は、無確認で上書きしません。\r\n文字の設定を変えた場合は、変更した設定項目を控えから戻します。\r\n日本語パッチ済みEXEと旧DLLを使っていた環境へ戻す場合は、片方だけを戻して起動せず、元の組み合わせと起動先を確認します。新旧の日本語化を重ねないよう、切り替え先の説明も確認してください。";
 }
 public static string AdvancedUninstall(Finding f) {
  // Detailed and beginner views share the same decision gates.
  return Uninstall(f);
 }
 public static string AdvancedRestore(Finding f) {
  if(f.Code=="bsa"||f.Code=="timestampTool")return "元の日時を記録していなければ正確な逆操作はできません。BSAを現在の日付へそろえず、導入しているSkyBSA等の説明に合わせます。BATを取り外しても、変更済みのBSA日時は戻りません。BSA内容の破損と日時の違いは別です。";
  return Restore(f);
 }
}
}
