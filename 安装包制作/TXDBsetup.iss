; TXDB 安装包脚本，当前阶段直接打包 Debug 输出目录。
; 发布正式版本时只需要把 SourceRoot 改成 Release 输出目录即可。

#define AppName "TXDB"
#define AppVersion "1.0.0.2"
#define SourceRoot "..\TXDB\bin\Debug"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
WizardStyle=modern
DefaultDirName=D:\TXDB
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\TXDB.exe
SetupIconFile=..\Access\Logo.ico
Compression=lzma2
SolidCompression=yes
OutputDir=installation
OutputBaseFilename=TXDB-V{#AppVersion}-install-x86
ChangesAssociations=yes

[Languages]
Name: zh; MessagesFile: "compiler:Default.isl"

[Files]
; 只打包主程序和运行依赖，不打包 pdb、xml 文档、用户配置、连接信息和保存查询。
Source: "{#SourceRoot}\TXDB.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\TXDB.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\10940_x86\SQLite.Interop.dll"; DestDir: "{app}\10940_x86"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\TXDB"; Filename: "{app}\TXDB.exe"
Name: "{autodesktop}\TXDB"; Filename: "{app}\TXDB.exe"
