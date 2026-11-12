# KeySwitcher

[English](README.md) | **Русский**

Автопереключатель раскладки RU/EN для Windows 10/11. Живёт в области уведомлений, запускается вместе с Windows.

## Возможности

- **Автоисправление раскладки.** Набрали `ghbdtn` и нажали пробел — получится `привет`, а раскладка переключится на русскую. Работает в обе стороны (`руддщ` → `hello`). Отключается в меню трея или в настройках.
- **Ранний режим** (опция): слово исправляется прямо во время набора, после 3–8 букв, если начало явно не из текущего языка.
- **Двойной Shift** — принудительно сменить раскладку последнего слова (повторное нажатие возвращает обратно). Если слова нет (кликнули мышью, выделили текст) — конвертируется выделенный текст.
- **Отмена**: Backspace сразу после автоисправления возвращает исходный текст и раскладку. Слово запоминается как исключение (отключаемо).
- **Слова-исключения** и **приложения-исключения** (игры, терминалы, менеджеры паролей). В исключённых приложениях программа ничего не делает.
- **Пауза** — `Ctrl+Alt+P` (настраивается): полностью отключает программу.
- **Caps Lock**: `пРИВЕТ` → `Привет` с выключением Caps Lock; `Ctrl+Alt+U` меняет регистр выделения по кругу: строчные → ПРОПИСНЫЕ → Как В Заголовке.
- Значок в трее показывает текущую раскладку (EN синий, RU красный, серый — пауза/исключение), тихий щелчок при автоисправлении.

Нужны установленные русская и любая английская раскладки.

## Сборка

Требуется .NET SDK 10.

```bash
dotnet build
dotnet test
```

Один exe-файл (нужен установленный .NET Desktop Runtime 10):

```bash
dotnet publish src/KeySwitcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Подпись (Smart App Control)

Если включён Smart App Control, Windows запускает только подписанные exe/dll. Самоподписанный сертификат не подойдёт: нужен сертификат для подписи кода от доверенного удостоверяющего центра.

Подпись встроена в сборку и включается, когда задан сертификат. После этого `dotnet build`, `dotnet test` и `dotnet publish` подписывают `KeySwitcher.exe`, `KeySwitcher.dll` и тестовую сборку сами. `signtool.exe` скачивается из NuGet-пакета Microsoft, Windows SDK ставить не нужно.

**Вариант 1 — сертификат в хранилище Windows** (OV-сертификат от Certum, Sectigo, SSL.com и т.п.; с 2023 года ключ хранится только на токене или в облаке, например Certum SimplySign — сертификат при этом виден в хранилище):

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint
setx KsSignThumbprint <отпечаток>
```

**Вариант 2 — Azure Artifact Signing** (бывш. Trusted Signing): сертификат выпускает и хранит Microsoft. Нужны подписка Azure, проверка личности и роль *Certificate Profile Signer*. Установите клиентские инструменты (`Azure.CodeSigning.Dlib.dll`), выполните `az login` и создайте `metadata.json`:

```json
{
  "Endpoint": "https://<регион>.codesigning.azure.net",
  "CodeSigningAccountName": "<аккаунт>",
  "CertificateProfileName": "<профиль>"
}
```

```powershell
setx KsSignDlib "C:\путь\к\Azure.CodeSigning.Dlib.dll"
setx KsSignMetadata "C:\путь\к\metadata.json"
```

Переменные из `setx` видны в новом окне терминала. Их можно передать и разово: `dotnet build -p:KsSignThumbprint=...`. Сервер метки времени по умолчанию — DigiCert, для Azure — Microsoft. Свой сервер задаётся через `KsTimestampUrl`. Проверить подпись можно так: `Get-AuthenticodeSignature .\publish\KeySwitcher.exe`.

## Как устроено

| Каталог | Что внутри |
|---|---|
| `Input/KeyboardHook.cs` | `WH_KEYBOARD_LL` + `WH_MOUSE_LL` на отдельном потоке |
| `Input/InputSender.cs` | `SendInput`; все наши события помечены, хук их пропускает |
| `Input/WordBuffer.cs` | буфер слова хранит нажатые клавиши (VK + Shift + Caps + раскладка), поэтому слово можно «перерисовать» в другой раскладке |
| `Layout/` | поиск раскладок, переключение (`WM_INPUTLANGCHANGEREQUEST`), таблицы клавиша↔символ через `ToUnicodeEx` |
| `Detection/` | частотные словари (40 тыс. слов на язык) + триграммная модель букв |
| `Core/SwitchEngine.cs` | логика автоисправления, двойного Shift, отмены |
| `Core/SelectionService.cs` | выделенный текст: UI Automation, иначе через буфер обмена (содержимое восстанавливается) |
| `UI/`, `TrayContext.cs` | трей, иконки, окно настроек |

Настройки: `%AppData%\KeySwitcher\settings.json`, журнал ошибок — `error.log` там же (набранный текст не пишется никуда).

Словари пересобираются скриптом `tools/fetch-dictionaries.ps1`.

## Ограничения

- Без прав администратора не работает в окнах, запущенных от администратора (ограничение Windows UIPI), на экране входа и в окнах UAC.
- В редакторах, которые при пустом выделении копируют всю строку, двойной Shift без слова может сработать не так, как ожидается. VS Code и Visual Studio распознаются.
- **Smart App Control** блокирует неподписанные exe — см. раздел «Подпись».

## Лицензии данных

Словари построены на списках [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) (данные OpenSubtitles 2018), лицензия CC-BY-SA 4.0.
