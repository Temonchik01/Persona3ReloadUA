#!/usr/bin/env python3
"""Draft reconstructed task scopes from BMD paths; never calls Crowdin."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as E
from collections import defaultdict

SOCIAL = {
8:('Акінарі','117','Akinari Kamiki'),10:('Бебе','112','Bebe'),
12:('Бункічі й Міцуко','103','Bunkichi'),14:('Чіхіро','109','Chihiro Fushimi'),
16:('Фуука','006','Fuuka Yamagishi'),18:('Хідетоші','102','Hidetoshi Odagiri'),
20:('Кадзуші','105','Kazushi Miyamoto'),22:('Кейсуке','108','Keisuke Hiraga'),
24:('Кенджі','101','Kenji Tomochika'),26:('Маіко','110','Maiko'),
28:('Мамору','115','Mamoru Hayase'),30:('Мая','107','Maya'),
32:('Міцуру','005','Mitsuru Kirijo'),34:('Мутацу','114','Mutatsu'),
36:('Нодзомі','116','Nozomi Suemitsu'),38:('Танака','113','President Tanaka'),
40:('Юкарі','002','Yukari Takeba'),42:('Юко','106','Yuko Nishiwaki'),
44:('Акіхіко','004','Akihiko Sanada'),46:('Араґакі','010','Shinjiro Aragaki'),
48:('Джюнпей','003','Junpei Iori'),50:('Кен','008','Ken Amada'),
52:('Коромару','009','Koromaru')}
STORY={2:('Пролог','100'),4:('Перший повний місяць','110'),
58:('Другий повний місяць та Фуука','120'),60:('Третій повний місяць','130'),
62:('Якушіма та Аіґіс','140'),64:('Четвертий повний місяць','150'),
66:('П’ятий повний місяць','160'),68:('Смерть Араґакі','170'),
70:('«Фінальний» бос','180'),72:('Зрада Ікуцукі та Кіото','190'),
74:('Смерть Чідорі','200'),76:('Інцидент 31 січня','210'),
78:('Хороша кінцівка','220'),80:('Погана кінцівка','230')}
OTHER={56:'Соціальні зв’язки: різне',82:'Додаткові діалоги до гравця',84:'Елізабет',
86:'Записки/журнали',88:'Описи',90:'Анімаційні вставки',92:'Календар',
94:'Бойові діалоги',96:'Інформація під час битви',98:'Результат битви',
100:'Підтримка Міцуру та Фууки',102:'Соціальні зв’язки як НІПи',104:'Опис соціальних зв’язків',
106:'Навчання',108:'Активності в Тартарі',110:'Додаткові бойові діалоги',
112:'Супроводження гравця',114:'Ініціалізація подій',116:'НІПи та герої СПВЗ',
118:'Об’єкти',120:'Системний текст',122:'Опис мап',124:'СМС'}


def main():
    files = sorted(Path('xml/Xrd777').rglob('*.xml'))
    speakers = {str(p): {n.text for n in E.parse(p).getroot().findall('Speakers/Speaker')} for p in files}
    result=[]
    def add(tid,title,selected,confidence,evidence):
        result.append({'task_id':tid,'title':title,'confidence':confidence,'evidence':evidence,
                       'files':sorted(set(map(str,selected))), 'approved':False})
    for tid,(name,group,speaker) in SOCIAL.items():
        selected=[p for p in files if re.search(rf'/Event_Cmmu_{group}_',str(p))]
        matches=sum(speaker in speakers[str(p)] for p in selected)
        assert selected and matches>0,(tid,group)
        add(tid,name,selected,'confirmed_group_partial_scope',
            f'Група Cmmu_{group}: {speaker} є серед мовців у {matches}/{len(selected)} файлах. NPC та листи ще потребують розподілу.')
    for tid,(name,group) in STORY.items():
        selected=[p for p in files if re.search(rf'Event_Main_{group}_',str(p))]
        add(tid,name,selected,'tentative_story_phase',
            'Чернетка за послідовністю груп Main. Межі глав і кінцівки потрібно перевірити за репліками; номер групи не гарантує відповідність назві завдання.')
    for tid,title in OTHER.items():
        selected=[]
        if tid==90:selected=[p for p in files if '/Movies/BMD/' in str(p)]
        elif tid==92:selected=[p for p in files if p.name=='BMD_CalendarDateMessages.xml']
        elif tid==96:selected=[p for p in files if '/Battle/Help/' in str(p)]
        elif tid==98:selected=[p for p in files if '/Battle/Result/' in str(p)]
        elif tid==100:selected=[p for p in files if p.name in ('BMD_BtlSupportMituru.xml','BMD_BtlSupportFuka.xml')]
        elif tid==104:selected=[p for p in files if '/Community/Help/' in str(p)]
        elif tid==106:selected=[p for p in files if '/Tutorial/' in str(p) or 'Tutorial' in p.name]
        elif tid==124:selected=[p for p in files if '/UI/Mail/' in str(p) or '/UI/MailTitle/' in str(p)]
        elif tid==88:selected=[p for p in files if '/Help/' in str(p) and '/Community/' not in str(p) and '/Battle/' not in str(p)]
        elif tid==94:selected=[p for p in files if '/Battle/Event/' in str(p)]
        elif tid==108:selected=[p for p in files if '/Dungeon/Hit/' in str(p) and 'Tutorial' not in p.name]
        elif tid==110:selected=[p for p in files if '/Dungeon/BattleEnd/' in str(p)]
        elif tid==112:selected=[p for p in files if '/Dungeon/SupportTalk/' in str(p)]
        elif tid==118:selected=[p for p in files if '/Bf/Obj/' in str(p) and 'Tutorial' not in p.name]
        elif tid==120:selected=[p for p in files if '/UI/' in str(p) and '/Mail' not in str(p)]
        elif tid==56:selected=[p for p in files if 'Event_Cmmu_' in str(p) and not re.search(r'Event_Cmmu_(?:'+ '|'.join(v[1] for v in SOCIAL.values()) +r'|007)_',str(p))]
        add(tid,title,selected,'candidate_category' if selected else 'needs_manual_mapping',
            'Кандидати за призначенням папки; перевірити межі з іншими завданнями. Порожній список означає, що надійного автоматичного правила поки немає.')
    owners=defaultdict(list)
    for task in result:
        for path in task['files']:owners[path].append(task['task_id'])
    overlaps={path:ids for path,ids in owners.items() if len(ids)>1}
    output=Path('build/crowdin-task-reconstruction');output.mkdir(parents=True,exist_ok=True)
    data={'project':861800,'target_branch':70799,'tasks':sorted(result,key=lambda x:x['task_id']),
          'overlaps':overlaps,'excluded_existing_task':6}
    (output/'draft-scopes.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    lines=['# Чернетка відновлення завдань Crowdin','',
           'Завдання №6 вже відновлено. Інші списки не застосовано до Crowdin.',
           'Усі набори потребують перевірки; сюжетні групи є припущеннями.',
           'Соціальні групи підтверджені мовцями, але не включають автоматично всі NPC та листи.',
           '', '| № | Завдання | Файлів | Статус |','|---|---|---:|---|']
    for t in data['tasks']:lines.append(f"| {t['task_id']} | {t['title']} | {len(t['files'])} | {t['confidence']} |")
    lines+=['',f'Перетини: {len(overlaps)} файлів. Повні шляхи та докази — у draft-scopes.json.',
            'Широке завдання СМС перетинається за призначенням із листами персонажів;',
            'завдання Навчання також може перетинатися зі Системним текстом.']
    (output/'PLAN.md').write_text('\n'.join(lines)+'\n')
    print(f'Drafted {len(result)} tasks; {len(overlaps)} overlapping files. Output: {output}')


if __name__=='__main__':main()
