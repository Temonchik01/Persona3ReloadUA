#!/usr/bin/env python3
"""Persona3ReloadUA build interface (Windows/Linux)."""
from pathlib import Path
import json
import os
import queue
import re
import signal
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

import backend

ROOT = Path(__file__).resolve().parents[2]


class Builder(ttk.Frame):
    def __init__(self, master):
        super().__init__(master, padding=16)
        self.pack(fill='both', expand=True)
        self.proc = None
        self.events = queue.Queue()
        self.cancelled = False
        self.output = None
        self.entries = {}
        self.overrides = {}
        self.settings_file = ROOT/'workspace/gui-settings.json'
        settings = json.loads(self.settings_file.read_text()) if self.settings_file.exists() else {}
        self.mode = tk.StringVar(value='textures')
        self.status = tk.StringVar(value='Виберіть текстури для тесту або повне збирання.')
        self.fields = {}
        ttk.Label(self, text='Збирач Persona 3 Reload UA', font=('', 18, 'bold')).pack(anchor='w')
        ttk.Label(self, text='PNG має пріоритет над DDS. Вихідники PSD/XCF потрібно експортувати у PNG.').pack(anchor='w', pady=(4,12))
        form = ttk.Frame(self)
        form.pack(fill='x')
        defaults = {
            'root':str(ROOT),
            'game_paks':str(Path.home()/'.var/app/com.valvesoftware.Steam/data/Steam/steamapps/common/P3R/P3R/Content/Paks') if os.name != 'nt' else '',
            'retoc':str(ROOT/'dev/tools/iostore/bin'/('retoc.exe' if os.name == 'nt' else 'retoc')),
            'font_pak':str(ROOT/'dev/tools/iostore/fonts.pak')}
        for index, (key, label) in enumerate([('root','Папка репозиторія'),('game_paks','Папка P3R/Content/Paks'),('retoc','Пакувальник retoc'),('font_pak','Пакет шрифтів')]):
            ttk.Label(form, text=label).grid(row=index, column=0, sticky='w', pady=3)
            value = tk.StringVar(value=settings.get(key, defaults[key]))
            self.fields[key] = value
            ttk.Entry(form, textvariable=value).grid(row=index,column=1,sticky='ew',padx=10)
            ttk.Button(form,text='Обрати…',command=lambda k=key:self.browse(k)).grid(row=index,column=2)
        form.columnconfigure(1, weight=1)
        modes = ttk.Frame(self)
        modes.pack(fill='x', pady=12)
        ttk.Radiobutton(modes,text='Тест вибраних текстур',variable=self.mode,value='textures').pack(side='left')
        ttk.Radiobutton(modes,text='Повний мод',variable=self.mode,value='full').pack(side='left',padx=20)
        actions = ttk.Frame(self)
        actions.pack(fill='x')
        ttk.Button(actions,text='Оновити список',command=self.refresh).pack(side='left')
        ttk.Button(actions,text='Підставити PNG/DDS для вибраної текстури',command=self.override).pack(side='left',padx=8)
        ttk.Button(actions,text='Скинути підстановку',command=self.reset_override).pack(side='left')
        self.filter = tk.StringVar()
        ttk.Label(actions,text='Пошук:').pack(side='left',padx=(16,4))
        ttk.Entry(actions,textvariable=self.filter,width=22).pack(side='left')
        self.filter.trace_add('write',lambda *_:self.render())
        tree_frame = ttk.Frame(self)
        tree_frame.pack(fill='both',expand=True,pady=8)
        self.tree = ttk.Treeview(tree_frame,columns=('path','format'),show='headings',selectmode='extended',height=10)
        self.tree.heading('path',text='Текстура — вибирайте кілька з Ctrl або Shift')
        self.tree.heading('format',text='Вхідний файл')
        self.tree.column('path',width=650)
        self.tree.column('format',width=140)
        scrollbar = ttk.Scrollbar(tree_frame,orient='vertical',command=self.tree.yview)
        scrollbar.pack(side='right',fill='y')
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.pack(side='left',fill='both',expand=True)
        self.tree.bind('<<TreeviewSelect>>',self.selection_status)
        self.progress = ttk.Progressbar(self, mode='indeterminate')
        self.progress.pack(fill='x')
        ttk.Label(self,textvariable=self.status,wraplength=900).pack(anchor='w',pady=8)
        buttons = ttk.Frame(self)
        buttons.pack(fill='x')
        self.build_button = ttk.Button(buttons,text='Зібрати мод',command=self.start)
        self.build_button.pack(side='left')
        self.stop_button = ttk.Button(buttons,text='Скасувати',command=self.stop,state='disabled')
        self.stop_button.pack(side='left',padx=8)
        ttk.Button(buttons,text='Відкрити результат',command=self.open_output).pack(side='left')
        ttk.Button(buttons,text='Відкрити журнали',command=lambda:self.open_path(Path(self.fields['root'].get())/'build/gui')).pack(side='left',padx=8)
        self.log = tk.Text(self,height=8,wrap='word',state='disabled')
        self.log.pack(fill='both',expand=True,pady=(10,0))
        self.refresh()
        self.after(150,self.poll)
        master.protocol('WM_DELETE_WINDOW',self.close)

    def browse(self,key):
        value = filedialog.askdirectory() if key in ('root','game_paks') else filedialog.askopenfilename()
        if value:
            self.fields[key].set(value)
            if key == 'root':
                self.overrides.clear()
                self.refresh()

    def refresh(self):
        self.entries = backend.textures(Path(self.fields['root'].get()))
        self.render()

    def render(self):
        if not hasattr(self,'tree'):
            return
        selected = set(self.tree.selection())
        self.tree.delete(*self.tree.get_children())
        search = self.filter.get().casefold()
        for rel, source in self.entries.items():
            if search and search not in rel.casefold():
                continue
            source = self.overrides.get(rel,source)
            label = Path(source).suffix.upper()[1:] + (' · підстановка' if rel in self.overrides else '')
            self.tree.insert('', 'end', iid=rel, values=(rel,label))
            if rel in selected:
                self.tree.selection_add(rel)

    def override(self):
        selected = self.tree.selection()
        if len(selected) != 1:
            messagebox.showinfo('Вибір текстури','Виберіть один рядок, для якого підставити PNG або DDS.')
            return
        path = filedialog.askopenfilename(filetypes=[('Текстури','*.png *.dds'),('Усі файли','*.*')])
        if path:
            if Path(path).suffix.lower() not in ('.png','.dds'):
                messagebox.showerror('Формат','Потрібен PNG або DDS.')
                return
            self.overrides[selected[0]] = path
            self.render()

    def reset_override(self):
        for rel in self.tree.selection():
            self.overrides.pop(rel,None)
        self.render()

    def selection_status(self,*_):
        if self.proc is None:
            self.status.set(f'Вибрано {len(self.tree.selection())} із {len(self.entries)} текстур.')

    def start(self):
        if self.proc is not None:
            return
        job = {key:value.get().strip() for key,value in self.fields.items()}
        job.update(mode=self.mode.get(),textures={rel:self.overrides.get(rel,self.entries[rel]) for rel in self.tree.selection()})
        if job['mode'] == 'textures' and not job['textures']:
            messagebox.showinfo('Вибір текстур','Виберіть хоча б одну текстуру у списку.')
            return
        root = Path(job['root'])
        if not (root/'dev/gui/backend.py').is_file():
            messagebox.showerror('Репозиторій','Вкажіть папку Persona3ReloadUA з dev/gui/backend.py.')
            return
        log_dir = root/'build/gui'
        log_dir.mkdir(parents=True,exist_ok=True)
        stamp = time.strftime('%Y%m%d-%H%M%S')
        job_path = log_dir/(stamp+'-job.json')
        job_path.write_text(json.dumps(job,ensure_ascii=False,indent=2))
        self.settings_file.parent.mkdir(parents=True,exist_ok=True)
        self.settings_file.write_text(json.dumps({k:job[k] for k in self.fields},ensure_ascii=False,indent=2))
        self.output = None
        self.cancelled = False
        self.log.configure(state='normal');self.log.delete('1.0','end');self.log.configure(state='disabled')
        flags = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        env = dict(os.environ,PYTHONIOENCODING='utf-8')
        self.proc = subprocess.Popen([sys.executable,'-u',str(root/'dev/gui/backend.py'),str(job_path)],cwd=root,
                                    stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace',
                                    start_new_session=os.name!='nt',creationflags=flags,env=env)
        self.build_button.configure(state='disabled');self.stop_button.configure(state='normal')
        self.progress.start(12);self.status.set('Перевірка залежностей і запуск збирання…')
        proc = self.proc
        def reader():
            with (log_dir/(stamp+'.log')).open('w',encoding='utf-8') as log:
                for line in proc.stdout:
                    log.write(line);log.flush();self.events.put(('line',line))
            self.events.put(('done',proc.wait()))
        threading.Thread(target=reader,daemon=True).start()

    def poll(self):
        try:
            while True:
                kind,value = self.events.get_nowait()
                if kind == 'line':
                    self.log.configure(state='normal');self.log.insert('end',value);self.log.see('end');self.log.configure(state='disabled')
                    if value.startswith('Етап ') or value.startswith('=== '):
                        self.status.set(value.strip())
                    prefix='Completed: THREE mod files in '
                    if value.startswith(prefix):
                        self.output=Path(value[len(prefix):].strip()).parent
                else:
                    self.proc=None;self.progress.stop();self.build_button.configure(state='normal');self.stop_button.configure(state='disabled')
                    if self.cancelled:
                        self.status.set('Збирання скасовано. Незавершені файли не встановлюйте в гру.')
                    elif value == 0 and self.output:
                        self.status.set('Готово: '+str(self.output/'Persona3ReloadUA.zip'))
                        messagebox.showinfo('Збірка готова','Мод зібрано. Кнопка «Відкрити результат» покаже ZIP і три файли мода.')
                    else:
                        self.status.set('Помилка: '+self.status.get()+'. Докладніше у журналі.')
                        messagebox.showerror('Помилка збирання','Перевірте журнал нижче. Файли гри не змінювалися.')
        except queue.Empty:
            pass
        self.after(150,self.poll)

    def stop(self):
        if self.proc is None:
            return
        self.cancelled=True
        if os.name == 'nt':
            subprocess.run(['taskkill','/PID',str(self.proc.pid),'/T','/F'],capture_output=True,
                           creationflags=subprocess.CREATE_NO_WINDOW)
        else:
            try:os.killpg(self.proc.pid,signal.SIGTERM)
            except ProcessLookupError:pass
        self.status.set('Скасування…')

    def open_path(self,path):
        if not path.exists():
            messagebox.showinfo('Папка','Папка ще не створена.')
        elif os.name == 'nt':
            os.startfile(path)
        else:
            subprocess.Popen(['xdg-open',str(path)],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)

    def open_output(self):
        if self.output:
            self.open_path(self.output)
        else:
            messagebox.showinfo('Результат','Спочатку завершіть збирання.')

    def close(self):
        if self.proc is not None:
            if not messagebox.askyesno('Збирання триває','Скасувати збирання й закрити програму?'):
                return
            self.stop()
        self.master.destroy()


if __name__ == '__main__':
    window=tk.Tk();window.title('Persona3ReloadUA — збирання мода');window.geometry('1080x820')
    Builder(window)
    window.mainloop()
