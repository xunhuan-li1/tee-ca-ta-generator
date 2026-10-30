"""Fetch the pinned public clang-format binary for Linux builds; no pip/root required."""
import hashlib
import io
import json
from pathlib import Path
import platform
import subprocess
import urllib.request
import zipfile

VERSION = '18.1.8'
DESTINATION = Path(__file__).resolve().parents[1] / '.tools/formatter-linux-x64'


def main():
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise SystemExit('This package targets Linux x86_64.')
    binary = DESTINATION / 'clang-format'
    receipt = DESTINATION / 'download-source.json'
    if binary.is_file() and receipt.is_file():
        previous = json.loads(receipt.read_text())
        if previous.get('Version') == VERSION and previous.get('BinarySha256') == hashlib.sha256(binary.read_bytes()).hexdigest():
            binary.chmod(0o755)
            return
    metadata_url = f'https://pypi.org/pypi/clang-format/{VERSION}/json'
    with urllib.request.urlopen(metadata_url, timeout=60) as response:
        metadata = json.load(response)
    item = next(f for f in metadata['urls'] if f['filename'].endswith('manylinux_2_17_x86_64.manylinux2014_x86_64.whl'))
    print('Downloading clang-format ' + VERSION, flush=True)
    with urllib.request.urlopen(item['url'], timeout=120) as response:
        archive = response.read()
    checksum = hashlib.sha256(archive).hexdigest()
    if checksum != item['digests']['sha256']:
        raise RuntimeError('Formatter download checksum mismatch.')
    with zipfile.ZipFile(io.BytesIO(archive)) as wheel:
        data = wheel.read('clang_format/data/bin/clang-format')
    DESTINATION.mkdir(parents=True, exist_ok=True)
    temporary = binary.with_suffix('.tmp')
    temporary.write_bytes(data)
    temporary.chmod(0o755)
    subprocess.run([str(temporary), '--version'], check=True)
    temporary.replace(binary)
    receipt.write_text(json.dumps({'Version': VERSION, 'Url': item['url'], 'WheelSha256': checksum,
                                  'BinarySha256': hashlib.sha256(data).hexdigest()}, indent=2) + '\n')


if __name__ == '__main__':
    main()
