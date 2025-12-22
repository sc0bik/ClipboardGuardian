# Clipboard Guardian

Clipboard Guardian — локальный “DLP‑агент” для контроля буфера обмена на **Windows** и **Android**.
Он показывает пользователю окно “Разрешить/Запретить” при операциях с буфером и ведёт локальные логи (NDJSON).

на Android полноценная защита “во всех приложениях” возможна только через **LSPosed/Xposed** (из‑за ограничений Android 10+).

## Быстрый старт (готовые сборки)

- **Android APK**: `dist/android/ClipboardGuardian-debug.apk`
- **Windows portable EXE**: `dist/windows/ClipboardGuardianPortable/ClipboardGuardian.Win.exe`



## Как пользоваться

### Android: режим 1 — обычное приложение (ограниченный режим)

1) Установи APK и открой приложение.  
2) Нажми “Запустить защиту”.  
3) Появится foreground‑уведомление.

Ограничение: на Android 10+ система ограничивает доступ к буферу для фоновых приложений, поэтому в этом режиме защита может срабатывать только частично.

### Android: режим 2 — LSPosed/Xposed (глобальный контроль)

1) Должны быть установлены **Magisk + LSPosed**.  
2) Установи `dist/android/ClipboardGuardian-debug.apk`.  
3) Открой **LSPosed Manager → Modules** → включи модуль **Clipboard Guardian**.  
4) Зайди в **Scope** модуля и отметь приложения (Chrome/Telegram/и т.д.).  
5) Перезапусти выбранные приложения (Force stop) или перезагрузи телефон.

Проверка: после включения Scope при копировании/чтении буфера в выбранном приложении должно открываться окно разрешения.

### Windows: как пользоваться

1) Запусти `dist/windows/ClipboardGuardianPortable/ClipboardGuardian.Win.exe`.  
2) Откроется главное окно + иконка в трее.  
3) При операциях с буфером обмена будет появляться окно разрешения с предпросмотром текста/файлов.  
4) Доступ к буферу обмена будет блокироваться/разрешаться по кнопкам.

## Логи (NDJSON)

Логи пишутся в файл `logs/clipboard_log.ndjson` рядом с исполняемым файлом (Windows) или в директории приложения (Android).
NDJSON = “Newline‑Delimited JSON”: каждая строка — отдельное событие.

Пример записи: `{"timestamp":"...","action":"copy","decision":"allowed","sample":"...","note":"..."}`.

## История обращений

В обоих приложениях доступна история обращений к буферу обмена.
Формат строк: `HH:mm:ss <содержимое> ответ - разрешил/запретил`.
История читается из NDJSON‑лога и обновляется кнопкой “Обновить”.

## Структура репозитория

```
android/ClipboardGuardian.Android/          – Android проект (Gradle, Kotlin)
  app/src/main/java/com/clipboardguardian/android/
    MainActivity.kt                         – главный экран (старт/стоп сервиса, статус)
    GuardianService.kt                      – foreground service, “обычный” режим (без LSPosed)
    ApprovalActivity.kt                     – окно разрешения (используется сервисом и LSPosed)
    ClipboardLogWriter.kt                   – запись событий в NDJSON
    ClipboardGuardianApp.kt                 – Application (канал уведомлений, DynamicColors)
    XposedInit.kt                           – Xposed entrypoint, хуки ClipboardManager
    XposedDecisionGate.kt                   – ожидание решения (broadcast + timeout)
    XposedContract.kt                       – action/extra для Xposed-broadcast
  app/src/main/assets/xposed_init           – список классов Xposed entrypoint

windows/ClipboardGuardian.Win/              – Windows агент (C# WinForms)
  Program.cs                                – всё приложение в одном файле:
                                              ApplicationContext + UI + clipboard/paste hooks + логирование

dist/                                      – готовые сборки (APK/EXE)
```

## Как собрать из исходников

### Android

Требования: Android SDK + JDK **17** (на JDK 21+ могут возникать проблемы с Android toolchain).

```bash
cd android/ClipboardGuardian.Android
JAVA_HOME=/usr/lib/jvm/java-17-openjdk ./gradlew :app:assembleDebug --no-daemon
```

Результат: `android/ClipboardGuardian.Android/app/build/outputs/apk/debug/app-debug.apk`.

### Windows (portable EXE)

```bash
dotnet publish windows/ClipboardGuardian.Win/ClipboardGuardian.Win.csproj \
  -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true \
  -o dist/windows/ClipboardGuardianPortable
```

Результат: `dist/windows/ClipboardGuardianPortable/ClipboardGuardian.Win.exe`.
