#!/usr/bin/env python3
"""Plan/create a new task set, then separately close the recorded old tasks."""
import argparse
import hashlib
import json
import sys
from pathlib import Path
from migrate_tasks import API, file_index

OLD_IDS=set(range(2,54,2)) | set(range(56,126,2))
MARKER='P3R-UA-task-set-v1'


def save(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_suffix(path.suffix+'.tmp')
    temp.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    temp.replace(path)


def identity(t):
    return (t.get('title'),t.get('targetLanguageId'),t.get('type'))


def verify(t,entry):
    if t.get('title')!=entry['title'] or t.get('type')!=0 or t.get('targetLanguageId')!='uk':
        raise ValueError(f"Unexpected identity for task {t.get('id')}")
    ids=t.get('fileIds') or []
    expected = set(entry['file_ids'])
    actual = set(ids)
    if actual - expected:
        raise ValueError(f"Unexpected file scope for task {t['id']}: foreign IDs {sorted(actual - expected)}; expected {sorted(expected)}, actual {sorted(actual)}")
    # Translation tasks can omit files with no remaining untranslated strings.
    if ids and actual != expected:
        print(f"Task #{t['id']}: Crowdin retained {len(actual)}/{len(expected)} planned files; omitted IDs {sorted(expected - actual)}", flush=True)
    if not ids and t.get('wordsCount',0):
        raise ValueError(f"Non-empty task {t['id']} has no reported file IDs")
    if entry['marker'] not in (t.get('description') or ''):
        raise ValueError(f"Missing marker in task {t['id']}")


def main():
    p=argparse.ArgumentParser(description=__doc__)
    sub=p.add_subparsers(dest='mode',required=True)
    plan=sub.add_parser('plan');plan.add_argument('--manifest',type=Path,default=Path('docs/crowdin-task-plan/tasks.json'))
    plan.add_argument('--output',type=Path,default=Path('build/crowdin-new-tasks-plan.json'))
    for mode in ('create','close-old'):
        cmd=sub.add_parser(mode);cmd.add_argument('--plan',type=Path,default=Path('build/crowdin-new-tasks-plan.json'))
        cmd.add_argument('--state',type=Path,default=Path('build/crowdin-new-tasks-state.json'))
        cmd.add_argument('--confirm',action='store_true',required=True)
        if mode=='create':cmd.add_argument('--limit',type=int,help='Maximum new tasks this run; omit for all')
    a=p.parse_args()
    if a.mode=='plan':
        if a.output.exists():raise ValueError('Plan exists; choose another output path')
        manifest=json.loads(a.manifest.read_text());api=API(manifest['project'])
        branches,files=file_index(api)
        if manifest['branch'] not in {b['id'] for b in branches}:raise ValueError('Missing branch')
        index={}
        for fid,f in files.items():
            if f['branch']==manifest['branch']:index.setdefault(f['path'],[]).append(fid)
        tasks=[];assigned=set();keys=set();titles=set()
        for entry in manifest['tasks']:
            if entry['key'] in keys or entry['title'] in titles:raise ValueError('Duplicate task key or title')
            keys.add(entry['key']);titles.add(entry['title']);ids=[]
            for path in entry['files']:
                matches=index.get(path,[])
                if len(matches)!=1:raise ValueError(f'{path}: {len(matches)} matches; no plan generated')
                fid=matches[0]
                if fid in assigned:raise ValueError(f'Duplicate file assignment: {path}')
                assigned.add(fid);ids.append(fid)
            if not ids:raise ValueError('Empty task file list')
            tasks.append({'key':entry['key'],'title':entry['title'],'file_ids':ids,'paths':entry['files'],
                          'marker':f"{MARKER}:{entry['key']}"})
        old=[]
        for item in api.all('tasks'):
            if item['id'] in OLD_IDS and item.get('status') not in ('done','closed'):
                task=api.get(f"tasks/{item['id']}")
                if task.get('type')==0 and task.get('targetLanguageId')=='uk':old.append(task)
        data={'project':manifest['project'],'branch':manifest['branch'],'tasks':tasks,'old_tasks':old}
        save(a.output,data)
        print(f'Read-only plan: {a.output}; new tasks: {len(tasks)}; old active tasks recorded: {len(old)}')
        return
    data=json.loads(a.plan.read_text());digest=hashlib.sha256(a.plan.read_bytes()).hexdigest()
    state=json.loads(a.state.read_text()) if a.state.exists() else {'plan_sha256':digest,'created':{},'closed':[]}
    if state['plan_sha256']!=digest:raise ValueError('State belongs to another plan')
    state.setdefault('skipped_complete', {})
    api=API(data['project'])
    if a.mode=='create':
        if a.limit is not None and a.limit<1:raise ValueError('--limit must be positive')
        _,files=file_index(api)
        for entry in data['tasks']:
            for fid,path in zip(entry['file_ids'],entry['paths'],strict=True):
                if files.get(fid)!={'branch':data['branch'],'path':path}:raise ValueError('File mapping changed; regenerate plan')
        existing=api.all('tasks');made=0
        for entry in data['tasks']:
            if entry['key'] in state['skipped_complete']:
                continue
            candidates=[t for t in existing if entry['marker'] in (t.get('description') or '')]
            if len(candidates)>1:raise ValueError(f"Duplicate marker: {entry['marker']}")
            saved=state['created'].get(entry['key'])
            if saved or candidates:
                tid=saved if saved else candidates[0]['id'];live=api.get(f'tasks/{tid}');verify(live,entry)
                state['created'][entry['key']]=tid;save(a.state,state);continue
            if any(t['title']==entry['title'] for t in existing):raise ValueError(f"Existing unmarked task has same title: {entry['title']}")
            if a.limit is not None and made>=a.limit:break
            payload={'title':entry['title'],'languageId':'uk','type':0,'status':'todo',
                     'fileIds':entry['file_ids'],'skipAssignedStrings':False,'splitContent':False,
                     'description':entry['marker']+'\nНове групування XML за файловою структурою. Виконавців буде призначено окремо.'}
            try:
                response=api.request('POST',f"/projects/{api.project}/tasks",payload)['data']
            except RuntimeError as error:
                message = str(error)
                if 'Crowdin HTTP 409:' in message and 'Language has no untranslated words' in message:
                    state['skipped_complete'][entry['key']] = {
                        'title': entry['title'], 'reason': 'Language has no untranslated words'}
                    save(a.state, state)
                    print(f"Skipped {entry['key']}: {entry['title']} — no untranslated words", flush=True)
                    continue
                raise
            # Persist before verification, so a failed read does not trigger a duplicate POST.
            state['created'][entry['key']]=response['id'];save(a.state,state)
            live=api.get(f"tasks/{response['id']}");verify(live,entry);existing.append(live);made+=1
            print(f"Created #{live['id']}: {entry['title']} ({live.get('wordsCount',0)} words)",flush=True)
        print(f"Created/recorded: {len(state['created'])}; skipped without untranslated words: {len(state['skipped_complete'])}; planned: {len(data['tasks'])}. Old tasks unchanged.")
        return
    accounted = set(state['created']) | set(state['skipped_complete'])
    if accounted != {entry['key'] for entry in data['tasks']}:
        raise ValueError('Process every planned task before closing old tasks')
    for entry in data['tasks']:
        if entry['key'] in state['skipped_complete']:
            continue
        live=api.get(f"tasks/{state['created'][entry['key']]}");verify(live,entry)
        if live.get('status')=='closed':raise ValueError('New task already closed; review before closing old tasks')
    for previous in data['old_tasks']:
        tid=previous['id'];live=api.get(f'tasks/{tid}')
        if identity(live)!=identity(previous):raise ValueError(f'Task #{tid} identity changed')
        if live.get('fileIds')!=previous.get('fileIds') or live.get('assignees')!=previous.get('assignees'):
            raise ValueError(f'Task #{tid} assignments changed; review before closing')
        if live.get('status') not in ('done','closed'):
            api.request('PATCH',f'/projects/{api.project}/tasks/{tid}',[{'op':'replace','path':'/status','value':'closed'}])
            if api.get(f'tasks/{tid}').get('status')!='closed':raise ValueError(f'Failed to verify closed task #{tid}')
        if tid not in state['closed']:state['closed'].append(tid)
        save(a.state,state);print(f'Closed old task #{tid}',flush=True)
    print('Replacement complete. No tasks or translations deleted.')


if __name__=='__main__':
    try:main()
    except (ValueError,RuntimeError,OSError,KeyError) as error:
        print(f'Error: {error}',file=sys.stderr);sys.exit(1)
