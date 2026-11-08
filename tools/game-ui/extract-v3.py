"""Extract cn-774 UI resources and Chinese string tables with archive provenance."""
import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--client', type=Path, required=True)
parser.add_argument('--tool', type=Path, default=ROOT / 'tools/BubbleFighter.Fantasy.Archives.Tool/bin/Release/net10.0/bffarchive.exe')
args = parser.parse_args()
output = ROOT / 'artifacts/game-ui'
output.mkdir(parents=True, exist_ok=True)
command = [str(args.tool)]
index = subprocess.run(command + ['index', str(args.client)], check=True, capture_output=True, encoding='utf-8').stdout
(output / 'cn2019-index.tsv').write_text(index, encoding='utf-8')
families = ('Ui/', 'Images/Ui/', 'Images/TaskBar/Option/', 'Images/DlgCreateRoom/', 'Images/QuickStart/')
tables = ('Config/Fonts_cn.csv', 'StringTable/TranslatedStringTable.csv', 'StringTable/TranslatedXmlTable.csv', 'StringTable/ui.yaml', 'StringTable/uicustom.yaml')
members = [row[1] for line in index.splitlines() if len(row := line.split('\t')) == 3 and (row[1].startswith(families) or row[1] in tables)]
if not members or len(members) > 5000:
    raise ValueError('Unexpected UI resource count.')
listing = output / 'cn2019-assets.txt'
listing.write_text('\n'.join(members) + '\n', encoding='utf-8')
subprocess.run(command + ['unpack', str(args.client), str(output / 'v3'), '--from', str(listing)], check=True)
