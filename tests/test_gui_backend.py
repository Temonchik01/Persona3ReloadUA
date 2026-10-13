import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT/path)
    m = importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    return m
backend = load('backend', 'dev/gui/backend.py')
portable = load('platform_process', 'dev/tools/iostore/platform_process.py')


class GuiBuildTests(unittest.TestCase):
    def test_png_precedence_and_psd_not_imported(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for p in ('texture/dds/Xrd777/A.dds','texture/png/Xrd777/A.png','texture/sources/Xrd777/B.psd'):
                target = root/p;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(b'test')
            result = backend.textures(root)
            self.assertEqual(set(result), {'Xrd777/A.dds'})
            self.assertTrue(result['Xrd777/A.dds'].endswith('A.png'))

    def test_windows_retoc_uses_no_unix_programs(self):
        self.assertEqual(portable.command('retoc.exe',['verify','mod.utoc'],1024,60,'nt'),
                         ['retoc.exe','verify','mod.utoc'])

    def test_linux_retains_limits(self):
        c = portable.command('retoc',['verify','mod.utoc'],1024,60,'posix')
        self.assertEqual(c[:3], ['prlimit','--as=1024','--cpu=60'])
        self.assertIn('timeout',c)

    def test_failed_tool_stops_worker(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(RuntimeError, '7'):
                backend.run(['python3','-c','raise SystemExit(7)'],Path(d),{})

    def test_error_summary_with_zero_exit_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaisesRegex(RuntimeError, 'import errors'):
                backend.run(['python3','-c','print("Done: 3 errors")'],Path(d),{})

    def test_windows_full_pipeline_commands(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for folder in ('translations/uk/xml','translations/uk/dt_xml'):
                (root/folder).mkdir(parents=True)
                (root/folder/'test.xml').write_text('<root/>')
            with patch.object(backend.shutil,'which',return_value='dotnet'), patch.object(backend,'run') as run:
                backend.full_windows(root,{})
            commands = [c.args[0] for c in run.call_args_list]
            self.assertEqual(len(commands),5)
            self.assertEqual(commands[0][:2],['dotnet','build'])
            self.assertTrue(str(commands[2][0]).endswith('P3RBmdTool.exe'))
            self.assertFalse(any(str(c[0]) in ('bash','wine','prlimit','timeout') for c in commands))


if __name__ == '__main__':unittest.main()
