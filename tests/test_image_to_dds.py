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


if __name__ == '__main__':
    unittest.main()
