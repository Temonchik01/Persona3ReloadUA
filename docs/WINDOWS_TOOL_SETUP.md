# Windows Tool Setup

Інструменти, розроблені для Persona3ReloadUA, зберігаються у відповідних каталогах репозиторію. Окремо потрібно встановити сторонню утиліту `retoc`.

## retoc

`retoc` — сторонній інструмент командного рядка для пакування й перевірки Unreal IoStore контейнерів. Windows-скрипти очікують його за шляхом:

```text
dev/tools/iostore/bin/retoc.exe
```

### Завантаження та встановлення

1. Відкрий офіційну [сторінку релізів retoc](https://github.com/trumank/retoc/releases).
2. Завантаж ZIP для Windows x64 (`x86_64-pc-windows-msvc`) з потрібного релізу.
3. Перевір SHA-256 за checksum, опублікованою поруч із ZIP.
4. Розпакуй виконуваний файл у `dev/tools/iostore/bin/` і назви його `retoc.exe`.
5. Перевір запуск командою:

```powershell
.\dev\tools\iostore\bin\retoc.exe --version
```

Запиши версію та checksum у звіт локальної збірки. Перед оновленням retoc перевір сумісність нової версії з пакуванням P3R.

### Ліцензія

retoc не є розробкою Persona3ReloadUA. Upstream публікує його під MIT License: https://github.com/trumank/retoc/blob/master/LICENSE. Якщо копію retoc буде включено до tooling-пакета, збережи поруч copyright notice і текст ліцензійного дозволу.
