import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'dev/tools/dt'))
from save_system_localization import build_system_converter, read_table, i32


class SystemSaveMappings(unittest.TestCase):
    def source(self, world):
        return ROOT / f'dev/tools/dt/source/{world}/UI/SystemText/DT_SystemTextName.uasset'

    def converter(self, world):
        return ROOT / f'dev/tools/dt/save-localize-source/{world}/UI/Tables/SaveDataLocalize/DT_SystemTextTranslationTable.uasset'

    def test_originals_remain_byte_identical(self):
        with tempfile.TemporaryDirectory() as folder:
            for world in ('Xrd777', 'Astrea'):
                out = Path(folder) / (world+'.uasset')
                report = build_system_converter(self.source(world), self.source(world), self.converter(world), out)
                self.assertEqual(report['translated_mappings_added'], 0)
                self.assertEqual(out.read_bytes(), self.converter(world).read_bytes())

    def test_changed_technical_comment_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            data = bytearray(self.source('Xrd777').read_bytes())
            _, _, _, rows = read_table(data)
            prop = rows[0]['props']['Comment']
            data[prop['payload']] ^= 1
            changed = Path(folder)/'changed.uasset'; changed.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'Technical system property changed'):
                build_system_converter(self.source('Xrd777'), changed, self.converter('Xrd777'), Path(folder)/'out.uasset')

    def test_existing_russian_label_gets_english_alias(self):
        dll = ROOT/'dev/tools/dt/linux/P3RDtTool.dll'
        if not dll.is_file():
            self.skipTest('Build native DT tool first')
        with tempfile.TemporaryDirectory() as folder:
            xml = Path(folder)/'input.xml'; changed = Path(folder)/'translated.uasset'; out = Path(folder)/'converter.uasset'
            env = dict(os.environ, DOTNET_GCHeapHardLimit='0x10000000')
            def run(*args):
                subprocess.run(['dotnet', str(dll), *map(str,args)], check=True, capture_output=True, timeout=30, env=env)
            run('--export-text-xml', self.source('Xrd777'), xml)
            tree = ET.parse(xml); matches = [n for n in tree.getroot() if n.findtext('Source') == '(Mo)' and n.get('role') == 'value']
            self.assertEqual(len(matches), 1)
            matches[0].find('Translation').text = '(Пн)'; tree.write(xml, encoding='utf-8')
            run('--import-text-xml', self.source('Xrd777'), xml, changed)
            report = build_system_converter(self.source('Xrd777'), changed, self.converter('Xrd777'), out)
            self.assertGreater(report['translated_mappings_added'], 0)
            _, _, _, before = read_table(self.converter('Xrd777').read_bytes())
            _, _, _, after = read_table(out.read_bytes())
            self.assertEqual([r['bytes'] for r in after[:len(before)]], [r['bytes'] for r in before])
            for row in after[len(before):]:
                self.assertTrue(i32(row['props']['LangBits']['raw'], 0) & 2)
            self.assertTrue(any(i32(r['props']['LangBits']['raw'],0) == 512 for r in before if i32(r['props']['Index']['raw'],0) == 111))


if __name__ == '__main__':
    unittest.main()
