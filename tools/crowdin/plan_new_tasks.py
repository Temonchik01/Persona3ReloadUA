#!/usr/bin/env python3
"""Partition all source XML into non-overlapping proposed Crowdin tasks."""
import json
import re
import xml.etree.ElementTree as E
from collections import defaultdict
from pathlib import Path
from plan_task_scopes import SOCIAL

PEOPLE={v[1]:v[0] for v in SOCIAL.values()}
PEOPLE['007']='Аіґіс'
LABELS={'Battle':'Бій','Community':'Соціальні зв’язки','Dictionary':'Словник',
        'Help':'Описи й довідка','Movies':'Анімаційні сцени','Schedule':'Календар і розклад',
        'Tutorial':'Навчання','UI':'Інтерфейс','AstreaProgress':'Прогрес епізоду',
        'Npc':'НІПи','Obj':'Об’єкти','Init':'Ініціалізація локацій',
        'Follower':'Діалоги супроводження','Dungeon':'Тартар та підземелля',
        'AreaChange':'Перехід між локаціями','kfevAreaChange':'Події переходу',
        'FldUtility':'Спільні повідомлення локацій'}


def classify(p):
    parts=p.parts
    if parts[0]=='dt_xml':
        mode,world=parts[1:3]
        rest=parts[3:]
        if 'Field' in rest:section='Локації, НІПи та швидкі переходи'
        elif 'Blueprints' in rest:section='Назви персонажів, ворогів і вмінь'
        elif 'Tutorial' in rest:section='Назви навчальних матеріалів'
        elif 'Dictionary' in rest:section='Назви словникових статей'
        elif 'Community' in rest:section='Назви параметрів героя'
        elif 'SystemText' in rest:section='Системні повідомлення'
        elif 'Configuration' in rest:section='Налаштування'
        elif 'Battle' in rest:section='Бойові таблиці'
        elif p.name.startswith('DatItem'):section='Назви предметів'
        else:section='Таблиці інтерфейсу'
        return world,'DT',f'{mode} — {section}'
    world=parts[1];section=parts[2]
    if section=='Events':
        match=re.search(r'Event_(Main|Cmmu|Extr|Qest)_(\d{3})_',str(p))
        if match:
            kind,code=match.groups()
            if kind=='Cmmu':return world,'Соціальні події',f'{PEOPLE.get(code,"Спільні або додаткові події")} — Cmmu_{code}'
            if kind=='Main':return world,'Сюжет',f'Група Main_{code}' if world=='Xrd777' else 'Основні події епізоду'
            return world,'Додаткові події','Завдання — Qest' if kind=='Qest' else 'Додаткові сцени — Extr'
        return world,'Події','Інші сцени'
    if section=='Community':
        return world,'Соціальні зв’язки','NPC-діалоги' if 'Bf' in parts else 'Профілі й описи рангів'
    if section=='Field':
        if parts[3]=='Cinema':return world,'Локації','Постановочні сцени — Field/Cinema'
        if 'Bf' in parts:
            pos=parts.index('Bf');kind=parts[pos+1]
            if kind=='Obj':
                group=parts[pos+2]
                code=re.search(r'(\d{3})$',group)
                return world,'Об’єкти',f'Група локації {code.group(1)}' if code else 'Спільні об’єкти'
            if kind=='Npc':
                group=parts[pos+2];code=re.search(r'(\d{3})$',group)
                return world,'НІПи',f'Група {code.group(1)}' if code else 'Інші діалоги'
            if kind=='Dungeon':
                return world,'Підземелля',{'Hit':'Об’єкти й повідомлення','SupportTalk':'Репліки підтримки',
                    'EntranceNPC':'Діалоги біля входу','ExploreNPC':'Дослідження і переходи',
                    'MissingNPC':'Зниклі люди','BattleEnd':'Після бою'}.get(parts[pos+2],parts[pos+2])
            return world,'Локації',LABELS.get(kind,kind)
        return world,'Локації','Інші ресурси'
    if section=='UI':
        sub=parts[3]
        if sub in ('Mail','MailTitle'):return world,'Повідомлення','СМС і заголовки листів'
        return world,'Інтерфейс','Меню, заклади та системні вікна'
    if section=='Tutorial':return world,'Навчання',{'Battle':'Бій','Combine':'Злиття персон',
        'Daily':'Повсякденне життя','Dungeon':'Підземелля','System':'Система'}.get(parts[3],parts[3])
    if section=='Battle':return world,'Бій',{'Event':'Події та бойові діалоги','Help':'Довідка',
        'Result':'Результат бою та перетасування','Support':'Репліки підтримки'}.get(parts[3],parts[3])
    return world,LABELS.get(section,section),'Усі файли розділу'


def main():
    groups=defaultdict(list);counts={}
    sources=sorted([*Path('xml').rglob('*.xml'),*Path('dt_xml').rglob('*.xml')])
    for p in sources:
        root=E.parse(p).getroot()
        if p.parts[0]=='xml':nodes=[n for n in root.iter() if n.tag in ('Page','Option','Speaker')]
        else:nodes=[n for n in root.iter() if n.tag=='Translation']
        counts[str(p)]=len(nodes);groups[classify(p)].append(str(p))
    tasks=[]
    for number,(key,files) in enumerate(sorted(groups.items()),1):
        world,section,name=key;game='P3R' if world=='Xrd777' else 'Episode Aigis'
        tasks.append({'key':f'UA-{number:03d}','title':f'[{game}] {section}: {name}',
            'world':world,'section':section,'files':files,'file_count':len(files),
            'source_nodes':sum(counts[f] for f in files),'language':'uk','type':'translation',
            'status':'proposed','notes':'Групування за шляхами; це не твердження про точні сюжетні межі.' if section=='Сюжет' else ''})
    assigned=[f for t in tasks for f in t['files']]
    assert len(assigned)==len(set(assigned))==len(sources)
    assert set(assigned)==set(map(str,sources))
    output=Path('docs/crowdin-task-plan');output.mkdir(parents=True,exist_ok=True)
    (output/'tasks.json').write_text(json.dumps({'project':861800,'branch':70799,'source_files':len(sources),
        'tasks':tasks},ensure_ascii=False,indent=2)+'\n')
    lines=['# Нові завдання Crowdin за структурою файлів','',
        f'План: {len(tasks)} завдань, {len(sources)} XML. Кожен файл входить рівно в одне завдання.',
        'Це локальний план: завдання в Crowdin ще не створено й не змінено.',
        '', '## Принципи','',
        '- P3R (`Xrd777`) і Episode Aigis (`Astrea`) мають окремі завдання.',
        '- Сюжет розділено за групами `Main`, без неперевірених назв глав.',
        '- Персонажні події `Cmmu` відокремлено від спільних NPC-діалогів і СМС.',
        '- DT text і fstrings мають окремі завдання; службові записи потребують перевірки в парсері.',
        '- Поділ охоплює тільки джерельні XML. DDS і вже перекладені XML не входять до цього плану.',
        '- Кількість вузлів — оцінка обсягу XML, не статистика слів чи прогресу Crowdin.',
        '', '## План','', '| Ключ | Завдання | XML | Вузлів тексту |','|---|---|---:|---:|']
    for t in tasks:lines.append(f"| {t['key']} | {t['title']} | {t['file_count']} | {t['source_nodes']} |")
    lines+=['','## Перехід зі старих завдань','',
        'Спочатку створити або перевірити нові завдання. Старі не закривати автоматично.',
        'Наявне завдання №6 містить події Аіґіс, NPC-діалоги та листи. Його потрібно',
        'узгодити з новим поділом, щоб не залишити призначення тих самих файлів двічі.',
        'Для нових завдань слід свідомо вибрати, чи включати тільки неперекладені рядки.',
        'Рецензування вже перекладених матеріалів створюється окремими завданнями.',
        '', 'Повні шляхи кожного завдання записані в `tasks.json`. Ключі UA — локальні',
        'позначення плану, а не реальні ID завдань Crowdin.']
    (output/'PLAN.md').write_text('\n'.join(lines)+'\n')
    print(f'Validated {len(tasks)} tasks; {len(sources)} XML; zero duplicate or unassigned files.')
    print('World task counts:',{w:sum(t['world']==w for t in tasks) for w in ('Xrd777','Astrea')})


if __name__=='__main__':main()
