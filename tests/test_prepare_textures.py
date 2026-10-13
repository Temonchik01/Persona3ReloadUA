import importlib.util
import subprocess
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('prepare_textures', ROOT/'dev/tools/dds_l10n/prepare_textures.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TextureInputSelection(unittest.TestCase):
    def fixture(self, root, with_png):
        for p in ('texture/dds/Xrd777', 'texture/png/Xrd777', 'dev/tools/dds_l10n/source/Xrd777'):
            (root/p).mkdir(parents=True)
        (root/'texture/dds/Xrd777/Test.dds').write_bytes(b'original edited DDS')
        (root/'dev/tools/dds_l10n/source/Xrd777/Test.uasset').write_bytes(b'game template')
        if with_png:
            (root/'texture/png/Xrd777/Test.png').write_bytes(b'editor export')

    def test_without_png_uses_dds(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            self.fixture(root, False)
            staged = module.prepare(root, root/'work')
            self.assertEqual((staged/'Xrd777/Test.dds').read_bytes(), b'original edited DDS')

    def fake_texture_module(self):
        def save(path):
            Path(path).write_bytes(b'reference extracted from game')
        texture = types.SimpleNamespace(get_dds=lambda: types.SimpleNamespace(save=save))
        return types.SimpleNamespace(_load_texture=lambda path: (None, texture))

    def test_png_overrides_dds_without_changing_original(self):
        def convert(args, **kwargs):
            self.assertEqual(Path(args[-2]).read_bytes(), b'reference extracted from game')
            Path(args[-1]).write_bytes(b'converted PNG')
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            self.fixture(root, True)
            with patch.dict(sys.modules, {'p3rtex':self.fake_texture_module()}), patch.object(module.subprocess, 'run', side_effect=convert):
                staged = module.prepare(root, root/'work')
            self.assertEqual((staged/'Xrd777/Test.dds').read_bytes(), b'converted PNG')
            self.assertEqual((root/'texture/dds/Xrd777/Test.dds').read_bytes(), b'original edited DDS')

    def test_failed_png_conversion_does_not_fall_back(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            self.fixture(root, True)
            with patch.dict(sys.modules, {'p3rtex':self.fake_texture_module()}), patch.object(module.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'convert')):
                with self.assertRaisesRegex(RuntimeError, 'Test.png'):
                    module.prepare(root, root/'work')
            self.assertFalse((root/'work/dds/Xrd777/Test.dds').exists())
            self.assertEqual((root/'texture/dds/Xrd777/Test.dds').read_bytes(), b'original edited DDS')


if __name__ == '__main__':
    unittest.main()
