"""Publishing must preserve final bytes and never clean an unverified build."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('build_outputs', Path(__file__).resolve().parents[1]/'tools/iostore/build_outputs.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PublicationTest(unittest.TestCase):
    def make_build(self, root):
        work = root/'build'/'20261008-170000'
        (work/'mod').mkdir(parents=True)
        for name in module.MOD_FILES:
            (work/'mod'/name).write_bytes(('verified-'+name).encode()*100)
        (work/'BUILD_COMPLETE.json').write_text(json.dumps({'mode':'compact-merged'}))
        (work/'MERGED_BUILD.json').write_text(json.dumps({'file_count':3}))
        (work/'raw-temporary').mkdir()
        (work/'raw-temporary'/'payload.bin').write_bytes(b'generated')
        (work/'verify.log').write_text('passed')
        return work

    def test_verified_publication(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);work=self.make_build(root)
            expected={p.name:p.read_bytes() for p in (work/'mod').iterdir()}
            published=module.publish_build(work,root/'dist')
            self.assertFalse(work.exists())
            self.assertEqual(expected,{p.name:p.read_bytes() for p in published.iterdir()})
            with zipfile.ZipFile(published.parent/'Persona3ReloadUA.zip') as package:
                self.assertEqual(expected,{name:package.read(name) for name in package.namelist()})
            self.assertEqual((published.parent/'reports/verify.log').read_text(),'passed')
            self.assertFalse((published.parent/'raw-temporary').exists())

    def test_failed_build_is_retained(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);work=self.make_build(root)
            (work/'BUILD_COMPLETE.json').unlink()
            with self.assertRaises(ValueError): module.publish_build(work,root/'dist')
            self.assertTrue((work/'raw-temporary/payload.bin').exists())
            self.assertTrue((work/'mod').is_dir())

    def test_existing_release_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);work=self.make_build(root)
            (root/'dist'/work.name).mkdir(parents=True)
            with self.assertRaises(FileExistsError): module.publish_build(work,root/'dist')
            self.assertTrue((work/'mod').is_dir())

if __name__=='__main__': unittest.main()
