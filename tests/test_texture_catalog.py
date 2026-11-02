import importlib.util
from pathlib import Path
from datetime import datetime, timedelta
import tempfile
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('catalog',Path(__file__).resolve().parents[1]/'dev/gui/texture_catalog.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)


class CatalogTests(unittest.TestCase):
    def test_first_discovery_survives_refresh_and_new_file_is_dated_separately(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);a=root/'A.png';a.touch()
            with patch.object(m.time,'time',return_value=100):
                first=m.catalog(root,[str(a)])
            a.write_bytes(b'edit');b=root/'B.dds';b.touch()
            with patch.object(m.time,'time',return_value=200):
                second=m.catalog(root,[str(a),str(b)])
            self.assertEqual(first[str(a)]['seen'],100)
            self.assertEqual(second[str(a)]['seen'],100)
            self.assertEqual(second[str(b)]['seen'],200)
            self.assertEqual(second[str(a)]['modified'],a.stat().st_mtime)

    def test_format_override_search_date_and_sort(self):
        now=datetime(2026,10,27,12);old=(now-timedelta(days=40)).timestamp();new=now.timestamp()
        entries={'UI/A.dds':'A.png','UI/B.dds':'B.dds','Battle/C.dds':'C.png'}
        metadata={'A.png':{'seen':old,'modified':new},'B.dds':{'seen':new,'modified':old},
                  'C.png':{'seen':new,'modified':new},'override.dds':{'seen':new,'modified':new}}
        rows=m.select(entries,{},metadata,search='UI/',fmt='PNG',days=1,now=now)
        self.assertEqual([r[0] for r in rows],['UI/A.dds'])
        self.assertEqual(m.select(entries,{},metadata,search='UI/',fmt='PNG',days=7,date_kind='seen',now=now),[])
        rows=m.select(entries,{'UI/A.dds':'override.dds'},metadata,search='UI/',fmt='DDS',newest=True,now=now)
        self.assertEqual([r[0] for r in rows],['UI/A.dds','UI/B.dds'])
        self.assertEqual(len(m.select(entries,{},metadata,now=now)),3)


if __name__=='__main__':unittest.main()
