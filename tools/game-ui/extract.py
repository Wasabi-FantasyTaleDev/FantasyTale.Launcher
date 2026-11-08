"""Extract the UI lab's bounded resource families using the repository archive tool."""
import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--v1-client', required=True, type=Path)
parser.add_argument('--v2-client', required=True, type=Path)
args = parser.parse_args()
command = ['dotnet', 'run', '--project', str(ROOT / 'tools/BubbleFighter.Fantasy.Archives.Tool'), '-c', 'Release', '--']
output = ROOT / 'artifacts/game-ui'
output.mkdir(parents=True, exist_ok=True)

def run(*arguments, capture=False):
    result = subprocess.run(command + list(map(str, arguments)), cwd=ROOT,
                            check=True, capture_output=capture, encoding='utf-8')
    return result.stdout if capture else None

families = ('Images/Ui/', 'Images/TaskBar/Option/', 'Images/DlgCreateRoom/', 'Images/QuickStart/')
index = run('index', args.v1_client, capture=True)
members = [line.split('\t')[1] for line in index.splitlines()
           if len(line.split('\t')) == 3 and line.split('\t')[1].startswith(families)]
if not members:
    raise ValueError('No v1 image resources found in the client archive index.')
listing = output / 'v1-assets.txt'
listing.write_text('\n'.join(members) + '\n', encoding='utf-8')
run('unpack', args.v1_client, output / 'v1-resources', '--from', listing)
run('extract', args.v1_client / 'Pack/Ui0000.pkn', output / 'v1', '--path', 'Pack/Ui0000.pkn')
for archive in ('Ui/Ui.bfz', 'Images/Ui/Ui.bfz', 'Images/TaskBar/Option/Option.bfz',
                'Images/DlgCreateRoom/DlgCreateRoom.bfz', 'Images/QuickStart/QuickStart.bfz'):
    run('extract', args.v2_client / archive, output / 'v2', '--path', archive)
print('Extraction complete. Run prepare.py with --font-data pointing to the v2 Data folder.')
