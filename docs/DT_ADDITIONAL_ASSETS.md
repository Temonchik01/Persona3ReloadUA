# Додаткові DT DataAsset: експорт і тестування

Джерела для перевірки: `dt test/` (44 uasset). Інструмент: вихідний код
`instruments/P3RDtTool/`, Linux DLL у `tools/dt/linux/`.

Підтримано:

- вкладені StrProperty у масивах структур, зокрема `DatSuggestionTextDataAsset`;
- tagged TextProperty з FText history Base: редагується лише source string,
  namespace і localization key зберігаються;
- повні масиви FString та FText, включно з порожніми значеннями;
- Unicode UTF-16LE, перерахунок розміру поля, зовнішніх ArrayProperty /
  StructProperty і SerialSize експорту;
- старі FString XML для розпізнаних повних масивів зіставляються за offset і Source.

`property` у нових text XML показує справжню назву поля. Старі атрибути
`a/b/c` збережені для сумісності; вони не є надійними назвами типів.
`TextLabel`, `Comment`, `Font` позначені `role="technical"` і захищені від зміни.
У прикладі `DNG_KF_0806_S` залишається ідентифікатором, а `Pursue the target`
редагується в Translation поля `Text`.

## Окрема робоча копія

З кореня репозиторію:

```bash
./scripts/linux/build_dt_tool.sh
DOTNET_GCHeapHardLimit=0x10000000 dotnet tools/dt/linux/P3RDtTool.dll \
  --batch-export-xml "$PWD/dt test" "$PWD/workspace/dt-test-review/xml"
```

Готовий експорт лежить у `workspace/dt-test-review/xml/text` і `fstrings`.
Змінюйте лише Translation у вузлах `role="value"`. Не редагуйте Source,
Offset, namespace, ключі або назви файлів. Результати в workspace не потрапляють
до Git; це робочий набір для перевірки, не нова конфігурація Crowdin.
Повторний експорт у той самий каталог перезапише переклади: для нового
експорту вибирайте новий каталог або спочатку робіть копію XML.

Імпорт після перекладу — в окремий каталог, без заміни робочого мода:

```bash
DOTNET_GCHeapHardLimit=0x10000000 dotnet tools/dt/linux/P3RDtTool.dll \
  --batch-import-xml "$PWD/workspace/dt-test-review/xml" \
  "$PWD/dt test" "$PWD/workspace/dt-test-review/imported" --verbose
```

За замовчуванням записуються лише змінені ресурси. Вихідні `dt test` і
наявні `dt_xml` залишаються незмінними. Перед пакуванням ресурси треба
перенести з `imported` зі збереженням відносного шляху в
`UnrealEssentials/P3R/Content/L10N/en/`. Перевіряти у грі краще невеликими групами: одна таблиця підказок,
потім назви предметів, потім назви персонажів. Поточні виключення
DT_SystemTextName залишаються чинними.

## Що не є перекладом

- `WordSortDataAsset`: числові PersonaID та значення сортування, тексту немає.
- `FontAdjustmentDataAsset`: таблиця символів і параметрів шрифту;
  експорт доступний для діагностики, редагування Font заблоковане.
- Додані поруч `_unwrapped.bmd` не обробляються DT-імпортером.

## Перевірки

```bash
python3 tests/dt_text_regression.py
python3 tests/dt_nested_regression.py
```

Перша перевірка охоплює попередні DT text; друга — 44 надіслані uasset,
побайтову тотожність без змін, Unicode, збереження решти текстів,
розміри вкладених контейнерів і 21 повний масив рядків.
Звіти перевірок: `build/tests/dt-nested/report.json` та `array-report.json`.
Це перевірка серіалізації; нові перекладені ресурси ще потребують запуску гри.
Windows EXE потрібно окремо перебудувати з оновлених C# джерел;
старий `tools/dt/P3RDtTool.exe` не оновлювався.
