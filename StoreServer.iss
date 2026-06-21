; ============================================================
;  JESKO Server — Inno Setup o'rnatuvchi skripti
;  Bu skript "publish" papkasidagi fayllardan bitta setup .exe yasaydi.
;
;  Foydalanish:
;    1) installer\publish.bat ni ishga tushiring (publish papkasi hosil bo'ladi)
;    2) Inno Setup (https://jrsoftware.org/isdl.php) o'rnating
;    3) Shu faylni Inno Setup'da oching va Compile (F9) bosing
;    4) installer\Output\JESKO-Server-Setup.exe hosil bo'ladi
;  Shu setupni do'kondagi server kompyuteriga o'rnatasiz.
; ============================================================

#define AppName "JESKO Server"
#define AppExe "JESKO.Server.exe"
#define AppVer "1.0.0"

[Setup]
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=JESKO
DefaultDirName={autopf}\JESKO Server
DefaultGroupName=JESKO Server
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=JESKO-Server-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; LAN/firewall sozlash uchun admin huquqi kerak
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "uz"; MessagesFile: "compiler:Default.isl"

[Files]
; publish papkasidagi BARCHA fayllar
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\JESKO Server"; Filename: "{app}\{#AppExe}"
Name: "{group}\JESKO Server'ni o'chirish"; Filename: "{uninstallexe}"
; AVTO-ISHGA TUSHISH: kompyuter yonib, foydalanuvchi kirganda server o'zi ishga
; tushadi. "--silent" — bunda sozlamalar oynasi chiqmaydi, faqat tray ikonka.
Name: "{commonstartup}\JESKO Server"; Filename: "{app}\{#AppExe}"; Parameters: "--silent"

[Run]
; Windows Firewall: LAN dagi telefon/kompyuterlar serverga ulana olishi uchun
; dasturga kiruvchi ulanishlarga ruxsat beramiz (har qanday port uchun ishlaydi).
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""JESKO Server"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=any"; \
  Flags: runhidden; StatusMsg: "Windows Firewall sozlanmoqda..."

; O'rnatish tugagach serverni darhol ishga tushirish (ixtiyoriy belgi)
Filename: "{app}\{#AppExe}"; Description: "JESKO Serverni hozir ishga tushirish"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; O'chirishda firewall qoidasini ham olib tashlaymiz
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall delete rule name=""JESKO Server"""; \
  Flags: runhidden

; DIQQAT: ma'lumotlar bazasi (C:\ProgramData\StoreSystem\store.db) ataylab
; o'chirilmaydi — dasturni qayta o'rnatganda ma'lumotlar saqlanib qoladi.
