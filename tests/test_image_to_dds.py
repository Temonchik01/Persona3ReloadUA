import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT / 'dev/tools/dds_l10n/image_to_dds.py'
REF = ROOT / 'texture/dds/Xrd777/UI/Title/T_UI_Title_00_texture.dds'


class ConverterGuards(unittest.TestCase):
    def run_case(self, image, output):
        return subprocess.run([sys.executable, str(TOOL), str(image), str(REF), str(output)],
                              capture_output=True, text=True, timeout=15)

    def test_wrong_dimensions_do_not_create_output(self):
        with tempfile.TemporaryDirectory() as d:
            png, output = Path(d)/'edited.png', Path(d)/'result.dds'
            Image.new('RGBA', (16, 16)).save(png)
            result = self.run_case(png, output)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Image size', result.stderr)
            self.assertFalse(output.exists())

    def test_lost_transparency_is_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            png, output = Path(d)/'edited.png', Path(d)/'result.dds'
            with Image.open(REF) as ref:
                Image.new('RGB', ref.size, 'white').save(png)
            result = self.run_case(png, output)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('fully opaque', result.stderr)
            self.assertFalse(output.exists())

    def test_existing_output_is_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            output = Path(d)/'result.dds'
            output.write_bytes(b'keep me')
            result = self.run_case(REF, output)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output.read_bytes(), b'keep me')

    def test_unicode_input_and_output_paths(self):
        reference = ROOT / 'texture/originals/Xrd777/UI/Community/Arcana/Normal/T_UI_Community_Arcana_022.dds'
        with tempfile.TemporaryDirectory() as d:
            folder = Path(d) / 'Текстури художника'
            folder.mkdir()
            png, output = folder / 'Вічність.png', folder / 'Готова текстура.dds'
            with Image.open(reference) as im:
                im.convert('RGBA').save(png)
            result = subprocess.run(
                [sys.executable, str(TOOL), str(png), str(reference), str(output)],
                capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            with Image.open(output) as im:
                self.assertEqual(im.size, (244, 256))
            self.assertTrue(output.with_suffix('.dds.json').is_file())

    def test_resize_requires_opt_in_and_preserves_format(self):
        reference = ROOT / 'texture/originals/Xrd777/UI/Community/Arcana/Normal/T_UI_Community_Arcana_022.dds'
        with tempfile.TemporaryDirectory() as folder:
            png, output = Path(folder)/'wide.png', Path(folder)/'wide.dds'
            Image.new('L', (344, 256), 0).save(png)
            args = [sys.executable, str(TOOL), str(png), str(reference), str(output)]
            rejected = subprocess.run(args, capture_output=True, text=True, timeout=30)
            self.assertNotEqual(rejected.returncode, 0)
            self.assertIn('Image size', rejected.stderr)
            self.assertFalse(output.exists())
            accepted = subprocess.run(args + ['--allow-resize'], capture_output=True, text=True, timeout=30)
            self.assertEqual(accepted.returncode, 0, accepted.stdout + accepted.stderr)
            with Image.open(output) as im:
                self.assertEqual(im.size, (344, 256))
            import json
            report = json.loads(output.with_suffix('.dds.json').read_text())
            self.assertEqual(report['format'], 'BC4_UNORM')
            self.assertEqual(report['mips'], 1)
            self.assertTrue(report['experimental_resize'])


if __name__ == '__main__':
    unittest.main()
