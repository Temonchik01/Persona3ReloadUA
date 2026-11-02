import importlib.util
import subprocess
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('texture_cache_test',ROOT/'dev/tools/dds_l10n/texture_cache.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)


class CacheTests(unittest.TestCase):
    def fixture(self,root):
        png=root/'texture/png/Xrd777/Test.png';png.parent.mkdir(parents=True);png.write_bytes(b'PNG v1')
        asset=root/'dev/tools/dds_l10n/source/Xrd777/Test.uasset';asset.parent.mkdir(parents=True);asset.write_bytes(b'game source')
        return png,asset

    def fake_module(self):
        tex=types.SimpleNamespace(get_dds=lambda:types.SimpleNamespace(save=lambda p:Path(p).write_bytes(b'reference')))
        return types.SimpleNamespace(_load_texture=lambda p:(None,tex))

    def encode(self,args,**kwargs):
        self.assertEqual(args[args.index('--quality')+1], 'normal')
        Path(args[-1]).write_bytes(b'compiled '+Path(args[-3]).read_bytes())

    def test_hit_and_changed_png_invalidation(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);png,asset=self.fixture(root)
            with patch.dict(sys.modules,{'p3rtex':self.fake_module()}),patch.object(m.subprocess,'run',side_effect=self.encode) as encode:
                first=m.convert(root,png,'Xrd777/Test.dds')
                self.assertEqual(m.convert(root,png,'Xrd777/Test.dds'),first)
                self.assertEqual(encode.call_count,1)
                png.write_bytes(b'PNG v2')
                second=m.convert(root,png,'Xrd777/Test.dds')
                self.assertNotEqual(first,second)
                self.assertEqual(encode.call_count,2)

    def test_template_change_and_corrupt_cache_invalidate(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);png,asset=self.fixture(root)
            with patch.dict(sys.modules,{'p3rtex':self.fake_module()}),patch.object(m.subprocess,'run',side_effect=self.encode) as encode:
                first=m.convert(root,png,'Xrd777/Test.dds');first.write_bytes(b'corrupt')
                self.assertNotEqual(m.convert(root,png,'Xrd777/Test.dds').read_bytes(),b'corrupt')
                asset.write_bytes(b'new game source')
                m.convert(root,png,'Xrd777/Test.dds')
                self.assertEqual(encode.call_count,3)

    def test_precompile_failure_preserves_working_dds(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);png,asset=self.fixture(root)
            dest=root/'texture/dds/Xrd777/Test.dds';dest.parent.mkdir(parents=True);dest.write_bytes(b'keep')
            with patch.dict(sys.modules,{'p3rtex':self.fake_module()}),patch.object(m.subprocess,'run',side_effect=subprocess.CalledProcessError(1,'convert')):
                with self.assertRaises(RuntimeError):m.precompile(root)
            self.assertEqual(dest.read_bytes(),b'keep')


if __name__=='__main__':unittest.main()
