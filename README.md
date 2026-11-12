# KeySwitcher

**English** | [Русский](README.ru.md)

RU/EN keyboard layout auto-switcher for Windows 10/11. Lives in the notification area and starts with Windows.

## Features

- **Automatic layout correction.** Type `ghbdtn` and press Space — you get `привет` and the layout switches to Russian. Works both ways (`руддщ` → `hello`). Can be turned off from the tray menu or in settings.
- **Early mode** (optional): the word is fixed while you type, after 3–8 letters, if its beginning clearly doesn't belong to the current language.
- **Double Shift** — force-convert the last word (press again to convert it back). If there is no word (you clicked with the mouse or selected text), the selected text is converted.
- **Undo**: Backspace right after an automatic correction restores the original text and layout. The word is remembered as an exception (can be disabled).
- **Word exceptions** and **app exceptions** (games, terminals, password managers). KeySwitcher does nothing in excluded apps.
- **Pause** — `Ctrl+Alt+P` (configurable): disables the program completely.
- **Caps Lock**: `пРИВЕТ` → `Привет` and Caps Lock is turned off; `Ctrl+Alt+U` cycles the case of the selection: lower → UPPER → Title Case.
- The tray icon shows the current layout (EN blue, RU red, grey — paused/excluded app), with a quiet click on each automatic correction.

Russian and any English keyboard layout must be installed.

## Building

Requires .NET SDK 10.

```bash
dotnet build
dotnet test
```

Single exe file (requires the .NET Desktop Runtime 10):

```bash
dotnet publish src/KeySwitcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Code signing (Smart App Control)

When Smart App Control is on, Windows runs only signed exe/dll files. A self-signed certificate won't do: you need a code signing certificate from a trusted certificate authority.

Signing is built into the build and turns on once a certificate is configured. After that, `dotnet build`, `dotnet test` and `dotnet publish` sign `KeySwitcher.exe`, `KeySwitcher.dll` and the test assembly automatically. `signtool.exe` is downloaded from a Microsoft NuGet package, so the Windows SDK is not required.

**Option 1 — certificate in the Windows certificate store** (an OV certificate from Certum, Sectigo, SSL.com, etc.; since 2023 the key must live on a hardware token or in the cloud, e.g. Certum SimplySign — the certificate still shows up in the store):

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint
setx KsSignThumbprint <thumbprint>
```

**Option 2 — Azure Artifact Signing** (formerly Trusted Signing): Microsoft issues and stores the certificate. You need an Azure subscription, identity validation and the *Certificate Profile Signer* role. Install the client tools (`Azure.CodeSigning.Dlib.dll`), run `az login` and create `metadata.json`:

```json
{
  "Endpoint": "https://<region>.codesigning.azure.net",
  "CodeSigningAccountName": "<account>",
  "CertificateProfileName": "<profile>"
}
```

```powershell
setx KsSignDlib "C:\path\to\Azure.CodeSigning.Dlib.dll"
setx KsSignMetadata "C:\path\to\metadata.json"
```

Variables set with `setx` are visible in a new terminal window. They can also be passed once: `dotnet build -p:KsSignThumbprint=...`. The default timestamp server is DigiCert, Microsoft's for Azure; set your own with `KsTimestampUrl`. To check the signature: `Get-AuthenticodeSignature .\publish\KeySwitcher.exe`.

## How it works

| Path | Contents |
|---|---|
| `Input/KeyboardHook.cs` | `WH_KEYBOARD_LL` + `WH_MOUSE_LL` on a dedicated thread |
| `Input/InputSender.cs` | `SendInput`; all our events are tagged so the hook skips them |
| `Input/WordBuffer.cs` | the word buffer stores the keys pressed (VK + Shift + Caps + layout), so a word can be re-rendered in the other layout |
| `Layout/` | layout discovery, switching (`WM_INPUTLANGCHANGEREQUEST`), key↔character tables via `ToUnicodeEx` |
| `Detection/` | frequency dictionaries (40k words per language) + a letter trigram model |
| `Core/SwitchEngine.cs` | automatic correction, double Shift and undo logic |
| `Core/SelectionService.cs` | selected text: UI Automation, falling back to the clipboard (its contents are restored) |
| `UI/`, `TrayContext.cs` | tray, icons, settings window |

Settings: `%AppData%\KeySwitcher\settings.json`, error log — `error.log` in the same folder (typed text is never written anywhere).

The dictionaries are rebuilt with `tools/fetch-dictionaries.ps1`.

## Limitations

- Without administrator rights it doesn't work in windows running as administrator (a Windows UIPI restriction), on the sign-in screen or in UAC prompts.
- In editors that copy the whole line when nothing is selected, double Shift without a word may not behave as expected. VS Code and Visual Studio are detected.
- **Smart App Control** blocks unsigned exe files — see "Code signing".

## Data licenses

The dictionaries are built from the [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) lists (OpenSubtitles 2018 data), licensed under CC-BY-SA 4.0.
