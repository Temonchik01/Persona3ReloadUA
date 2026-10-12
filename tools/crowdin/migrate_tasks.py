#!/usr/bin/env python3
"""Crowdin file-based task migration: inventory, read-only plan, explicit apply."""
import argparse
import getpass
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path


class API:
    def __init__(self, project):
        self.project = project
        self.token = os.environ.get('CROWDIN_TOKEN') or getpass.getpass('Crowdin API token (hidden): ')
        if not self.token:
            raise ValueError('Empty token')

    def request(self, method, path, data=None):
        request = urllib.request.Request('https://api.crowdin.com/api/v2' + path,
            data=None if data is None else json.dumps(data).encode(), method=method,
            headers={'Authorization': 'Bearer ' + self.token, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(request, timeout=40) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            # Print only structured API error messages, never headers or raw bodies.
            detail = ''
            try:
                payload = json.loads(error.read(32768))
                messages = []
                def collect(value):
                    if isinstance(value, dict):
                        for key, child in value.items():
                            if key == 'message' and isinstance(child, str):
                                messages.append(child)
                            elif isinstance(child, (dict, list)):
                                collect(child)
                    elif isinstance(value, list):
                        for child in value:
                            collect(child)
                collect(payload)
                detail = '; '.join(messages).replace(self.token, '[REDACTED]')[:1200]
            except (ValueError, OSError):
                pass
            suffix = f' — {detail}' if detail else ''
            raise RuntimeError(f'Crowdin HTTP {error.code}: {method} {path}{suffix}') from None

    def get(self, suffix):
        return self.request('GET', f'/projects/{self.project}/{suffix}')['data']

    def all(self, suffix):
        result = []
        offset = 0
        while True:
            separator = '&' if '?' in suffix else '?'
            page = self.request('GET', f'/projects/{self.project}/{suffix}{separator}limit=500&offset={offset}')
            items = [item['data'] for item in page['data']]
            result.extend(items)
            if len(items) < 500:
                return result
            offset += len(items)


def file_index(api):
    branches = api.all('branches')
    dirs = {d['id']: d for d in api.all('directories')}
    files = {}
    for f in api.all('files'):
        parts = [f['name']]
        directory = f.get('directoryId')
        branch = f.get('branchId')
        seen = set()
        while directory:
            if directory in seen or directory not in dirs:
                raise ValueError('Invalid directory hierarchy')
            seen.add(directory)
            d = dirs[directory]
            parts.insert(0, d['name'])
            branch = branch or d.get('branchId')
            directory = d.get('directoryId')
        files[f['id']] = {'branch': branch, 'path': '/'.join(parts)}
    return branches, files


def map_task(task, files, source, target):
    old = task.get('fileIds')
    if not isinstance(old, list) or not old:
        return None  # Directory-, branch-, or string-scoped tasks need separate handling.
    if not any(files.get(i, {}).get('branch') == source for i in old):
        return None
    if task.get('resolvedAt') or task.get('status') in ('done', 'closed'):
        return None
    matches = {}
    for fid, f in files.items():
        if f['branch'] == target:
            matches.setdefault(f['path'], []).append(fid)
    new = []
    mapping = []
    errors = []
    for fid in old:
        f = files.get(fid)
        if not f:
            errors.append(f'Missing original file ID {fid}')
            continue
        replacement = fid
        if f['branch'] == source:
            candidates = matches.get(f['path'], [])
            if len(candidates) != 1:
                errors.append(f"{f['path']}: {len(candidates)} matches")
                continue
            replacement = candidates[0]
            mapping.append({'path': f['path'], 'old_id': fid, 'new_id': replacement})
        new.append(replacement)
    if len(set(new)) != len(new):
        errors.append('Replacement would merge duplicate file assignments')
    return {'task_id': task['id'], 'title': task['title'], 'old_file_ids': old,
            'new_file_ids': new, 'mapping': mapping, 'errors': errors,
            'snapshot': task}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest='mode', required=True)
    inv = sub.add_parser('inventory')
    identity = inv.add_mutually_exclusive_group(required=True)
    identity.add_argument('--project', type=int)
    identity.add_argument('--project-slug')
    plan = sub.add_parser('plan'); plan.add_argument('--project', type=int, required=True)
    plan.add_argument('--from-branch', type=int, required=True)
    plan.add_argument('--to-branch', type=int, required=True)
    plan.add_argument('--output', type=Path, required=True)
    plan.add_argument('--tasks', help='Optional comma-separated task IDs')
    rebuild = sub.add_parser('rebuild-plan')
    rebuild.add_argument('manifest', type=Path)
    rebuild.add_argument('--output', type=Path, required=True)
    verify = sub.add_parser('verify')
    verify.add_argument('plan', type=Path)
    apply = sub.add_parser('apply'); apply.add_argument('plan', type=Path)
    apply.add_argument('--confirm', action='store_true', required=True)
    args = p.parse_args()
    if args.mode == 'verify':
        data = json.loads(args.plan.read_text())
        api = API(data['project'])
        for planned in data['tasks']:
            task = api.get(f"tasks/{planned['task_id']}")
            expected = set(planned['new_file_ids'])
            print(f"Task #{task['id']}: {task['title']}")
            print('status:', task.get('status'))
            print('fileIds:', task.get('fileIds'))
            print('filesCount:', task.get('filesCount'))
            print('wordsCount:', task.get('wordsCount'))
            print('originalWordsCount:', task.get('originalWordsCount'))
            print('Expected files:', len(expected))
            actual = task.get('fileIds') or []
            if all(isinstance(i, int) for i in actual):
                print('Missing from fileIds:', sorted(expected - set(actual)))
                print('Unexpected in fileIds:', sorted(set(actual) - expected))
            try:
                rows = api.all(f"strings?taskId={task['id']}")
                scope = {r['fileId'] for r in rows}
                print('Actual strings:', len(rows))
                print('Files represented in strings:', len(scope))
                print('Unexpected string file IDs:', sorted(scope - expected))
                print('Expected files without task strings:', sorted(expected - scope))
            except RuntimeError as error:
                print(f'String scope unavailable: {error}')
        return
    if args.mode == 'rebuild-plan':
        manifest = json.loads(args.manifest.read_text())
        if args.output.exists():
            raise ValueError('Plan exists; use a new output path')
        api = API(manifest['project'])
        branches, files = file_index(api)
        if manifest['target_branch'] not in {b['id'] for b in branches}:
            raise ValueError('Target branch does not exist')
        task = api.get(f"tasks/{manifest['task_id']}")
        if manifest['title_contains'] not in task['title']:
            raise ValueError('Task title does not match the manifest')
        if task.get('fileIds') or task.get('wordsCount'):
            raise ValueError('Task is no longer empty; stop to preserve its current scope')
        paths = manifest['files']
        if not paths or len(set(paths)) != len(paths):
            raise ValueError('Empty or duplicate manifest paths')
        mapping = []
        for path in paths:
            matches = [fid for fid, f in files.items() if f['branch'] == manifest['target_branch'] and f['path'] == path]
            if len(matches) != 1:
                raise ValueError(f'{path}: {len(matches)} matching files; no plan generated')
            mapping.append({'path': path, 'new_id': matches[0]})
        data = {'project': api.project, 'target_branch': manifest['target_branch'], 'reconstruction': True,
                'tasks': [{'task_id': task['id'], 'title': task['title'], 'old_file_ids': [],
                           'new_file_ids': [f['new_id'] for f in mapping], 'mapping': mapping,
                           'errors': [], 'snapshot': task}]}
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2))
        print(f"Read-only reconstruction plan: {args.output}; {len(mapping)} files for task #{task['id']}")
        return
    if args.mode == 'apply':
        data = json.loads(args.plan.read_text())
        api = API(data['project'])
        if not data['tasks'] or any(t['errors'] for t in data['tasks']):
            raise ValueError('Empty plan or blocked tasks; generate a new plan with selected task IDs')
        _, files = file_index(api)
        # Check every task before making any changes.
        for t in data['tasks']:
            live = api.get(f"tasks/{t['task_id']}")
            if live != t['snapshot']:
                raise ValueError(f"Task {t['task_id']} changed since planning; regenerate plan")
            if data.get('reconstruction'):
                for entry in t['mapping']:
                    expected = {'branch': data['target_branch'], 'path': entry['path']}
                    if files.get(entry['new_id']) != expected:
                        raise ValueError('Reconstruction file mapping changed; regenerate plan')
                if [entry['new_id'] for entry in t['mapping']] != t['new_file_ids']:
                    raise ValueError('Inconsistent reconstruction plan')
                continue
            fresh = map_task(live, files, data['source_branch'], data['target_branch'])
            if not fresh or fresh['errors'] or fresh['new_file_ids'] != t['new_file_ids']:
                raise ValueError(f"Mapping changed for task {t['task_id']}; regenerate plan")
        for t in data['tasks']:
            # A second scope check reduces concurrent-edit risk. API updates are not atomic across tasks.
            live = api.get(f"tasks/{t['task_id']}")
            if live != t['snapshot']:
                raise ValueError(f"Task {t['task_id']} changed; stopping. Earlier tasks may be migrated.")
            api.request('PATCH', f"/projects/{api.project}/tasks/{t['task_id']}",
                        [{'op': 'replace', 'path': '/fileIds', 'value': t['new_file_ids']}])
            after = api.get(f"tasks/{t['task_id']}")
            if sorted(after.get('fileIds', [])) != sorted(t['new_file_ids']):
                raise RuntimeError(f"Task {t['task_id']}: PATCH completed, but fileIds differs. Do not retry apply; run verify on this plan.")
            print(f"Migrated task #{t['task_id']}: {t['title']}", flush=True)
        return
    api = API(args.project)
    if args.mode == 'inventory' and args.project_slug:
        matches = []
        offset = 0
        while True:
            page = api.request('GET', f'/projects?limit=500&offset={offset}')
            projects = [item['data'] for item in page['data']]
            matches.extend(x for x in projects if x.get('identifier') == args.project_slug)
            if len(projects) < 500:
                break
            offset += len(projects)
        if len(matches) != 1:
            raise ValueError('Project not found uniquely; use numeric --project from Crowdin Settings')
        api.project = matches[0]['id']
    print(f'Project ID: {api.project}')
    branches, files = file_index(api)
    tasks = api.all('tasks')
    print('Branches:')
    for b in branches:
        print(f"  {b['id']}: {b['name']}")
    print('Tasks:')
    for t in tasks:
        print(f"  #{t['id']}: {t['title']}")
    if args.mode == 'inventory':
        print('Root directories (may include the old draft folder):')
        for d in api.all('directories'):
            if not d.get('directoryId'):
                print(f"  {d['id']}: {d['name']} (branchId={d.get('branchId')})")
        print('Task #6 file scope:')
        task = api.get('tasks/6')
        print('  fileIds:', task.get('fileIds'))
        print('  directoryIds:', task.get('directoryIds'))
        print('  branchIds:', task.get('branchIds'))
        print('  response fields:', ', '.join(sorted(task)))
        for key in ('type', 'status', 'sourceLanguageId', 'languageId', 'wordsCount', 'scope', 'branchId', 'content', 'splitContent'):
            if key in task:
                print(f'  {key}: {task[key]}')
        print('  stringIds count:', len(task.get('stringIds') or []))
        try:
            strings = api.all('strings?taskId=6')
            counts = {}
            for row in strings:
                fid = row.get('fileId')
                counts[fid] = counts.get(fid, 0) + 1
            print('  task strings total:', len(strings))
            for fid, count in sorted(counts.items(), key=lambda pair: str(pair[0])):
                print(f'  task file {fid}: {count} strings; {files.get(fid, "not in file inventory")}')
        except RuntimeError as error:
            print(f'  String diagnostic unavailable: {error}. Requires source-string read scope.')
        for fid in task.get('fileIds') or []:
            item = files.get(fid)
            print(f"  {fid}: {item}")
        return
    ids = {b['id'] for b in branches}
    if args.from_branch == args.to_branch or not {args.from_branch, args.to_branch} <= ids:
        raise ValueError('Select two different existing branch IDs')
    if args.output.exists():
        raise ValueError('Plan already exists; choose a new output path')
    chosen = None if not args.tasks else {int(x) for x in args.tasks.split(',')}
    result = []
    skipped = []
    for listed in tasks:
        if chosen is not None and listed['id'] not in chosen:
            continue
        t = api.get(f"tasks/{listed['id']}")
        mapped = map_task(t, files, args.from_branch, args.to_branch)
        if mapped:
            result.append(mapped)
            print(f"  PLAN #{t['id']}: {len(mapped['mapping'])} replacements; errors: {mapped['errors']}")
        else:
            skipped.append({'task_id': t['id'], 'title': t['title']})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'project': api.project, 'source_branch': args.from_branch,
        'target_branch': args.to_branch, 'tasks': result, 'skipped_tasks': skipped}, ensure_ascii=False, indent=2))
    print(f'Read-only plan saved: {args.output}. Tasks to migrate: {len(result)}; skipped: {len(skipped)}')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, RuntimeError, OSError, KeyError) as error:
        print(f'Error: {error}', file=sys.stderr)
        sys.exit(1)
