"""Require a single-file Linux publish without deleting pre-existing user files."""
import argparse
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    parser.add_argument('--published', action='store_true')
    args = parser.parse_args()
    directory = args.directory
    if not directory.exists():
        if args.published:
            parser.exit(1, 'Publish output directory is missing.\n')
        return
    if not directory.is_dir():
        parser.exit(1, 'Choose a directory for --output.\n')
    entries = {p.name for p in directory.iterdir()}
    expected = {'gp-project-generator', 'AI_GUIDE.md'}
    if entries - expected:
        parser.exit(1, 'Output contains extra files: ' + ', '.join(sorted(entries - expected)) +
                    '. Use --output with a new directory; existing files were preserved.\n')
    for name in expected:
        path = directory / name
        if path.is_symlink() or (path.exists() and not path.is_file()):
            parser.exit(1, 'Output entries must be regular files: ' + name + '\n')
    if args.published and (entries != expected or any((directory / name).stat().st_size == 0 for name in expected)):
        parser.exit(1, 'Publish must contain the standalone executable and AI_GUIDE.md.\n')


if __name__ == '__main__':
    main()
