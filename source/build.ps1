$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework のC#コンパイラーが見つかりません。'}
New-Item -ItemType Directory -Force (Join-Path $root 'output') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root 'private_audit') | Out-Null
$common=@('/nologo','/optimize+','/debug-','/platform:x86','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll',('/win32manifest:'+(Join-Path $root 'src\app.manifest')))
$src=@('KnownData.cs','Scanner.cs','App.cs') | ForEach-Object {Join-Path $root ('src\'+$_)}
& $compiler @common /target:winexe ('/out:'+(Join-Path $root 'output\Oblivion_Japanese_Setup_Guide.exe')) @src
if($LASTEXITCODE -ne 0){throw 'アプリのビルドに失敗しました。'}
& $compiler @common /target:exe /main:CheckerTests /r:System.Web.Extensions.dll ('/out:'+(Join-Path $root 'private_audit\CheckerTests.exe')) @src (Join-Path $root 'tests\CheckerTests.cs')
if($LASTEXITCODE -ne 0){throw 'テストのビルドに失敗しました。'}
& (Join-Path $root 'private_audit\CheckerTests.exe')
if($LASTEXITCODE -ne 0){throw 'テストが失敗しました。'}
