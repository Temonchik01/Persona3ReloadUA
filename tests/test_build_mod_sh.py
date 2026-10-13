"""Verify stage orchestration without compiling assets or starting Wine."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class FullBuildScript(unittest.TestCase):
    def fixture(self, folder, failure):
        root = Path(folder)/'project with spaces'
        scripts = root/'dev/scripts/linux'; scripts.mkdir(parents=True)
        shutil.copyfile(ROOT/'dev/scripts/linux/build_mod.sh', scripts/'build_mod.sh')
        for p in ('translations/uk/xml','translations/uk/dt_xml','texture/dds','dev/tools/dt/source','dev/tools/dds_l10n/source'):
            (root/p).mkdir(parents=True)
        for p in ('translations/uk/xml/a.xml','translations/uk/dt_xml/a.xml'):
            (root/p).write_text('<root/>')
        (root/'texture/dds/a.dds').write_bytes(b'DDS ')
        bin_dir = root/'test-bin'; bin_dir.mkdir()
        for command in ('dotnet','wine','winepath','timeout','prlimit'):
            p = bin_dir/command; p.write_text('#!/bin/sh\nexit 0\n'); p.chmod(0o755)
        p = bin_dir/'pgrep'; p.write_text('#!/bin/sh\nexit 1\n'); p.chmod(0o755)
        def stub(name, body):
            p = scripts/name
            p.write_text('#!/usr/bin/env bash\nset -eu\nroot=$(cd "$(dirname "$0")/../../.." && pwd)\n'+body+'\n')
            p.chmod(0o755)
        stub('build_dt_tool.sh', 'echo tool >> "$root/order"')
        stub('repack_linux.sh', 'echo "$1" >> "$root/order"\n'+
             ('if [[ "$1" == dt ]]; then echo "Done: 3 errors"; exit 0; fi' if failure=='summary' else
              'if [[ "$1" == bmd ]]; then exit 17; fi' if failure=='exit' else 'true'))
        stub('repack_dds.sh','echo dds >> "$root/order"')
        stub('pack_linux.sh', '''echo pack >> "$root/order"
mkdir -p "$root/dist/test/mod"
python3 - "$root" <<'EOF'
from pathlib import Path
import zipfile,sys
p=Path(sys.argv[1])/'dist/test'
with zipfile.ZipFile(p/'Persona3ReloadUA.zip','w') as z:
 for suffix in ('pak','ucas','utoc'):
  f=p/'mod'/('mod.'+suffix);f.write_bytes(b'test');z.write(f,f.name)
EOF
echo "Completed: THREE mod files in $root/dist/test/mod"''')
        env = dict(os.environ, PATH=str(bin_dir)+os.pathsep+os.environ['PATH'],
                   P3R_DIAGNOSTIC_ASSETS_FILE='must-be-unset')
        result = subprocess.run(['bash', str(scripts/'build_mod.sh')], env=env,
                                capture_output=True, text=True, timeout=15, cwd='/tmp')
        return root, result

    def test_success_order_and_three_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root, r = self.fixture(folder, None)
            self.assertEqual(r.returncode,0,r.stdout+r.stderr)
            self.assertEqual((root/'order').read_text().splitlines(),['tool','dt','bmd','dds','pack'])
            self.assertIn('ГОТОВО',r.stdout)

    def test_zero_exit_with_reported_errors_stops(self):
        with tempfile.TemporaryDirectory() as folder:
            root,r=self.fixture(folder,'summary')
            self.assertNotEqual(r.returncode,0)
            self.assertEqual((root/'order').read_text().splitlines(),['tool','dt'])
            self.assertIn('2/5 — Імпорт DT XML',r.stderr)
            self.assertIn('Журнал:',r.stderr)

    def test_nonzero_exit_stops_before_dds(self):
        with tempfile.TemporaryDirectory() as folder:
            root,r=self.fixture(folder,'exit')
            self.assertEqual(r.returncode,17)
            self.assertEqual((root/'order').read_text().splitlines(),['tool','dt','bmd'])
            self.assertIn('3/5 — Імпорт BMD XML',r.stderr)


if __name__=='__main__':unittest.main()
