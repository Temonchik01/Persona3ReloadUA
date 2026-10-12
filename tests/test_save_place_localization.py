"""Save-name conversion must preserve every original language and row identity."""
from pathlib import Path
import os
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as E

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools/dt'))
from save_place_localization import build_converter, read_place_table, read_conversion_table
PLACE=ROOT/'tools/dt/source/Xrd777/Field/Data/DataTable/Texts/DT_FldPlaceName.uasset'
CONVERTER=ROOT/'tools/dt/save-localize-source/Xrd777/UI/Tables/SaveDataLocalize/DT_FldPlaceNameConvert.uasset'
TRANSLATED=ROOT/'UnrealEssentials/P3R/Content/L10N/en/Xrd777/Field/Data/DataTable/Texts/DT_FldPlaceName.uasset'

class SavePlaceTest(unittest.TestCase):
    def test_no_translation_preserves_converter_bytes(self):
        with tempfile.TemporaryDirectory() as folder:
            target=Path(folder)/'converter.uasset'
            result=build_converter(PLACE,PLACE,CONVERTER,target)
            self.assertEqual(result['translated_mappings_added'],0)
            self.assertEqual(target.read_bytes(),CONVERTER.read_bytes())

    def test_original_languages_and_school_mapping_retained(self):
        with tempfile.TemporaryDirectory() as folder:
            target=Path(folder)/'converter.uasset'
            result=build_converter(PLACE,TRANSLATED,CONVERTER,target)
            _,_,before=read_conversion_table(CONVERTER.read_bytes())
            _,_,after=read_conversion_table(target.read_bytes())
            self.assertEqual([r['bytes'] for r in before],[r['bytes'] for r in after[:len(before)]])
            pairs={(r['number'],r['name']) for r in after}
            self.assertIn((2,'Будівля школи'),pairs)
            self.assertIn((3,'Будівля школи'),pairs)
            self.assertIn((24,'Станція «Іватодай»'),pairs)
            _,_,place_rows=read_place_table(TRANSLATED.read_bytes())
            self.assertEqual(next(r['value'] for r in place_rows if r['index']==2),'Будівля школи')
            self.assertTrue(result['original_rows_byte_identical'])
            self.assertEqual(len({r['key'] for r in after}),len(after))

    def test_scanner_recovers_school_building_and_noop_is_identical(self):
        with tempfile.TemporaryDirectory() as folder:
            xml=Path(folder)/'export.xml';output=Path(folder)/'noop.uasset'
            def run(*args):
                result=subprocess.run(['timeout','40s','dotnet',str(ROOT/'tools/dt/linux/P3RDtTool.dll'),*map(str,args)],capture_output=True,text=True,env={**os.environ,'DOTNET_GCHeapHardLimit':'0x10000000'})
                self.assertEqual(result.returncode,0,result.stdout+result.stderr)
            run('--export-text-xml',PLACE,xml)
            nodes=E.parse(xml).getroot().findall('Text')
            restored=next(e for e in nodes if e.get('offset')=='0xC2C')
            self.assertEqual(restored.findtext('Source'),'School Building')
            self.assertEqual(restored.get('role'),'value')
            run('--import-text-xml',PLACE,xml,output)
            self.assertEqual(output.read_bytes(),PLACE.read_bytes())

if __name__=='__main__':unittest.main()
