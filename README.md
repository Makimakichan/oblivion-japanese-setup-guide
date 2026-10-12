# Oblivion Japanese Setup Check and English Restore Guide

Version **1.0.0** · Author: **Makimakichan999**

Full source and build instructions for the Windows application submitted to
[Nexus Mods, mod 56713](https://www.nexusmods.com/oblivion/mods/56713).
This repository was prepared for the additional source review requested by
Nexus Mods Support. Its availability does not indicate that Nexus has approved
the submitted executable.

The application has a Japanese interface. It identifies known legacy Japanese
localization files in selected Classic Oblivion and Mod Organizer 2 folders,
compares SHA-256 metadata, and explains manual removal or restoration options.
The scanner reads files; it does not automatically delete, replace, patch, or
restore game files. It does not send scan results over the network or request
administrator privileges.

## Relationship to the submitted file

The `source/` directory, `README_JA.txt`, and `LICENSE.txt` are copied byte for
byte from `Oblivion_Japanese_Setup_Guide_1.0.0.zip`, submitted as Nexus file
**1000046902**. The application source, build script, tests, and original license
have not been changed for this repository. This English README, the Git
configuration files, and the Windows build workflow are additional repository
documentation/configuration.

The repository contains no prebuilt executable, private scan results, game
files, or legacy localization binaries. The metadata in `KnownData.cs` consists
of filenames and hashes, not the contents of those third-party files.

## Build and run the automated checks

### Requirements

- Windows with **.NET Framework 4.8** and **Windows PowerShell 5.1**.
- A writable local folder on a Windows drive (NTFS for the junction test).
- No Visual Studio, .NET SDK, NuGet packages, game installation, MO2, xOBSE, or
  MenuQue is required to build the application and run its fixture tests.
- Use a normal Windows account; an elevated administrator session is not
  required. The build was tested on Windows 11.

This is a .NET Framework Windows Forms project compiled directly with
`csc.exe`; there is no solution or project file and no package restore step.

### Steps

1. Download this repository with **Code → Download ZIP** and extract it, or clone
   it with Git. For a fixed review snapshot, use the commit link supplied in the
   support reply. Keep all files and their relative paths together.
2. Open **Windows PowerShell** in the extracted repository's root folder (the
   folder containing this README, `LICENSE.txt`, and `source`).
3. Run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
   ```

   `-ExecutionPolicy Bypass` applies only to that PowerShell process; the command
   does not change the computer's persistent execution policy. Review the
   script before running it.

4. The script compiles the application, compiles the test executable, and runs
   the tests. A successful run exits with code `0` and prints:

   ```text
   PASS 42 checks; fixture files unchanged by scanner; GUI construction passed.
   ```

   In the calling PowerShell window, `$LASTEXITCODE` shows the exit code. The
   script stops with an error if compilation or a test fails.

5. The application is produced at:

   ```text
   source\output\Oblivion_Japanese_Setup_Guide.exe
   ```

   The test executable is produced at:

   ```text
   source\private_audit\CheckerTests.exe
   ```

   Both output directories are ignored by Git. No separate runtime assets need
   to be copied next to the application.

6. Optionally open the built application:

   ```powershell
   & .\source\output\Oblivion_Japanese_Setup_Guide.exe
   ```

   Running the GUI is separate from running the tests. The GUI may automatically
   start a read-only scan when it finds both a game folder and an MO2 folder.
   For normal usage and the limits of its guidance, see `README_JA.txt`.

### Compiler and references

The build script uses the Windows-provided compiler at:

```text
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
```

It builds an **x86** Windows Forms executable with optimizations enabled and
debug output disabled. It uses the framework's standard assemblies
`System.Core.dll`, `System.Windows.Forms.dll`, and `System.Drawing.dll`.
The test build also references `System.Web.Extensions.dll`.
`source/src/app.manifest` requests `asInvoker`, not elevation.

If the compiler is missing, verify that .NET Framework 4.8 is installed. A
modern `dotnet` SDK by itself is not a substitute for this build script's
.NET Framework compiler. If the junction test fails, use a writable local NTFS
folder and inspect the reported error rather than skipping the test.

The script does not enable deterministic compilation. Matching source files
does not by itself guarantee a byte-identical rebuilt executable across build
runs or machines.

### Automated repository build

The **Build and fixture checks** workflow runs the same build command on a
GitHub-hosted Windows Server 2022 runner. Its result is available in the
repository's Actions tab. It does not publish compiled executables or upload
test artifacts. This is a build/fixture check, not an in-game or full GUI test.

## Source layout and review notes

| File | Purpose |
| --- | --- |
| `source/src/App.cs` | Windows Forms UI, explicit folder selection, and Explorer selection action. |
| `source/src/Scanner.cs` | Read-only scan, classification, and Japanese guidance text. |
| `source/src/KnownData.cs` | Known filenames and SHA-256 metadata. |
| `source/src/app.manifest` | Application manifest and privilege level. |
| `source/tests/CheckerTests.cs` | Self-contained fixture checks and GUI construction check. |
| `source/build.ps1` | Application build, test build, and test execution. |
| `README_JA.txt` | Original Japanese usage instructions and limitations. |
| `LICENSE.txt` | Original license, preserved without modification. |

The ordinary test run creates a uniquely named directory under the Windows
temporary directory, writes its own fixtures, checks scanner behavior and
fixture content/modified-time preservation, creates a junction within that
fixture, constructs the GUI without showing it, and removes its temporary
fixture afterwards. These writes are test setup and cleanup, not changes to
the user's game files. The build also writes the two output directories listed
above. The optional `--live` test-program mode is not used by `build.ps1` or by
these build instructions.

The application can open Windows Explorer to select a file when the user clicks
its folder button. Its folder detection also reads the game's registry entry;
it does not write to the registry. It does not launch a legacy localization
patch or execute a detected game/mod file.

The fixture checks are not a certification of every installation, game
migration, or operating system. In-game behavior, Windows 10, and Steam
Deck/Proton compatibility are outside this repository's build verification.

## License and credits

Copyright (c) 2026 **Makimakichan999**. All rights reserved.
See `LICENSE.txt` for the existing terms. Source visibility does not change
those terms: redistribution, publication of modified versions, or publication
of incorporated code requires the author's permission.

Product names belong to their respective owners. No third-party game or legacy
localization implementation is included.
