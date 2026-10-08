# Виправлення імпортера DT text

Вихідний код: instruments/P3RDtTool/P3RDtTextScanner.cs.

## Причина

Старий імпортер записував UTF-8 FString та змінював лише розмір експорту.
У tagged StrProperty лишався старий Size. У DT_FldShortcutName після
Town Map -> Мапа міста гра читала частину UTF-8 тексту як FName й падала
з Bad name index. Це окрема проблема від DT_SystemTextName/fstrings.

Старий сканер також трактував Size як третій FName і пропускав UTF-16
рядки. Через це довгі переклади могли зникати з повторного експорту,
а додавання підтримки Unicode змінювало нумерацію рядків.

## Зміни

- Перерахунок Size StrProperty та SerialSize єдиного Zen-експорту.
- ASCII зберігається вузьким FString; не-ASCII — UTF-16LE, від’ємна
  довжина у UTF-16 кодових одиницях, включно з термінатором.
- Незмінені та пропущені в XML значення копіюються без зміни байтів.
- Старі XML читаються за початковим offset і перевіреним Source.
- Для розпізнаних tagged StrProperty Size більше не вважається FName.
- Експорт підтримує Unicode, довгі значення та переноси рядків;
  role=value/technical дозволяє відрізняти підтримувані поля.
- Редагування випадкових/технічних збігів відхиляється. Невідомі offsets,
  змінений Source, некоректні заголовки й кілька експортів при зміні
  блокуються, щоб не створювати пошкоджений ресурс.

Формат обмежений перевіреними P3R UE4.27 Zen-таблицями. Це не загальний
редактор FText або довільних властивостей Unreal Engine.

## Linux

Потрібен .NET 8 SDK або новіший для збирання:

```bash
./scripts/linux/build_dt_tool.sh
python3 tests/dt_text_regression.py
```

Linux-проєкт компілює ті самі C#-файли під net8.0. Оригінальний Windows
проєкт net10.0 залишений для колеги; старий Windows EXE не замінений.
Linux DLL з’являється у tools/dt/linux/ (не додається в Git), repack_linux.sh
автоматично використовує її для DT. Fstrings-імпортер тут не виправлявся.
Тому повний імпорт dt_xml може зупинятися на його старих захистах.

Для збирання тільки text, без проблемних fstrings:

```bash
./scripts/linux/repack_dt_text.sh
```

Це збере готові uasset у UnrealEssentials без створення резервних копій.
Тимчасовий вхід у build/dt-text-input.* видаляється після запуску. Стандартні виключення
пакувальника залишаються активними; робочі пакети гри не змінюються.

Для одиничного DT:

```bash
DOTNET_GCHeapHardLimit=0x10000000 dotnet tools/dt/linux/P3RDtTool.dll \
  --import-text-xml \
  tools/dt/source/Xrd777/Field/Data/DataTable/Texts/DT_FldShortcutName.uasset \
  dt_xml/text/Xrd777/Field/Data/DataTable/Texts/DT_FldShortcutName.xml \
  build/dt-test/DT_FldShortcutName.uasset
```

## Перевірки

tests/dt_text_regression.py перевіряє всі 13 наявних text XML:
імпорт без змін є побайтово ідентичним, переклади повторно читаються,
Size/Unicode та наступні FName не пошкоджені. Окремо перевіряються
короткі/довгі рядки, українські літери, emoji, переноси, пунктуація,
відхилення технічних правок, зміни Source й невідомого offset.
Звіт: build/tests/dt-text-regression/report.json.

## Ігровий тест ще потрібний

Робочий мод автоматично не замінюється. config/build-exclusions.json
залишається активним. Спочатку перевірте один виправлений DT у грі.
Після збирання саме виправлених ресурсів можна тестово задати
P3R_INCLUDE_DT_TEXT=1 при пакуванні: це включає тільки ресурси text,
а DT_SystemTextName залишається виключеним, щоб не зникали сейви.
Поки ігровий тест не пройдено, не прибирайте стандартні виключення.
