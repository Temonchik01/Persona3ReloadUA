# Persona 3 Reload — український переклад

Проєкт перекладу гри українською: тексти, таблиці, шрифти та текстури.
Український мод замінює англійську мову гри.

## Де що лежить

| Папка | Призначення |
|---|---|
| `xml/` | BMD XML — джерела текстів для Crowdin |
| `dt_xml/` | XML таблиць DT — джерела для Crowdin |
| `translations/` | Переклади, експортовані Crowdin |
| `texture/dds/` | Редаговані текстури DDS |
| `texture/png/` | PNG для збирання; мають пріоритет над однойменним DDS |
| `texture/sources/` | PSD, XCF та інші вихідники з шарами |
| `texture/originals/` | Збережені оригінальні DDS |
| `fonts/` | Матеріали шрифтів |
| `dev/tools/` | Компілятори, Python-інструменти й оригінальні шаблони |
| `dev/scripts/linux/` | Команди запуску для Linux |
| `dev/scripts/windows/` | Команди запуску для Windows |
| `dev/gui/` | Графічний застосунок для тесту текстур і повного збирання |
| `UnrealEssentials/` | Готові ресурси мода за ігровими шляхами |
| `docs/` | Інструкції та план подальшого впорядкування |
| `build/` | Тимчасові файли збирання, журнали й результати тестів |
| `dist/` | Готові комплекти мода, ZIP і звіти перевірки |
| `.cache/` | Wine-префікс, NuGet та кеші інструментів |
| `workspace/` | Робочі експорти XML/DDS та матеріали для Crowdin |

Команди збирання розміщені лише в dev/scripts/linux/ та dev/scripts/windows/.
Не переносіть tools або папки XML окремо: компілятори очікують їхні шляхи.

## Linux

Потрібні Python 3, Wine/winepath для BMD/DT, retoc, prlimit і timeout.

```bash
./dev/scripts/linux/repack_linux.sh bmd
./dev/scripts/linux/repack_linux.sh dt
./dev/scripts/linux/export_dds.sh
./dev/scripts/linux/repack_dds.sh /path/to/edited-dds
./dev/scripts/linux/pack_linux.sh
```

Закрийте гру перед експортом/імпортом. Не експортуйте оригінальні XML
поверх ручних перекладів. DDS-експорт у Linux створює нову папку в workspace/dds-exports/.
Retoc кладіть у dev/tools/iostore/bin/retoc або задайте RETOC. Ця локальна
бінарна папка ігнорується Git; після свіжого клону retoc потрібно встановити.
Персональні налаштування — у config.local.env (копія прикладу).
Готовий fonts.pak та еталонні ufont збережені в dev/tools/iostore/;
це поточні перевірені шрифти, не автоматичне збирання нових шрифтів.

Докладніше: [пакування на Linux](docs/PACK_LINUX.md),
[правила текстур](docs/DDS_L10N_PIPELINE.txt).

## Windows

Запускайте відповідний BAT із dev/scripts/windows/:
repack_bmd.bat, repack_dt.bat, repack_dds.bat.
export_* створюють джерела для редагування й можуть перезаписати їх.
BAT використовують наявні Windows-компілятори та вбудований Python.
Linux-pak-пакувальник потребує retoc для Linux; BAT-пакування retoc тут
не реалізоване.

## Crowdin

crowdin.yml лишається в корені. Шляхи xml/, dt_xml/ і translations/
не змінені. За наданими налаштуваннями інтеграції main є джерелом,
l10n_main — службовою гілкою. Не перезаписуйте службову гілку й не
реєструйте її як другу гілку перекладу.

Експортовані українські XML лежать у translations/uk/. Імпортери типово
читають translations/uk/xml/ та translations/uk/dt_xml/. Кореневі xml/
і dt_xml/ містять оригінальні джерела для Crowdin.

```bash
./dev/scripts/linux/repack_linux.sh bmd "$PWD/translations/uk/xml"
```

Не редагуйте керівні коди BMD та технічні ключі DT. Crowdin-файли
під час упорядкування не змінювалися.

## Результат збирання

Retoc створює компактні контейнери зі зміненими ресурсами та потрібними
записами таблиць. Незмінений вміст гри не копіюється. Скрипт перевіряє
цілісність і хеші підставлених ресурсів. Додаткові локалізовані копії збираються окремо зі збереженням їхнього вмісту.
Невідомий шаблон зупиняє збирання; список знаходиться в missing-assets.json.

Для встановлення переносіть лише три .pak/.utoc/.ucas з підпапки mod/
однієї завершеної папки збирання в P3R/Content/Paks/~mods. Приберіть попередній комплект
та запускайте гру з English.


Локальні `build/`, `dist/`, `.cache/`, `workspace/` виключені з Git.
Після успішної перевірки пакувальник публікує три файли в
`dist/<час>/mod/`, ZIP і звіти, а проміжні файли цієї збірки видаляє.
Невдалі збірки залишаються в `build/pak-builds/`. Резервні копії
скрипти не створюють. Робочі переклади в `workspace/` автоматично не очищаються.
Докладніше: [локальні результати й кеші](docs/LOCAL_OUTPUTS.md).


Українські назви місць збираються разом із відповідниками для старих
сейвів: [локалізація назв місць](docs/SAVE_PLACE_LOCALIZATION.md).


Повне автоматичне збирання на Linux із `translations/uk/` та `texture/dds/`:

```bash
./dev/scripts/linux/build_mod.sh
```

Порядок етапів, залежності й журнали помилок: [BUILD_ALL_LINUX.md](docs/BUILD_ALL_LINUX.md).
