"""Exercise the published Linux package without requiring a .NET installation."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, required=True)
    args = parser.parse_args()
    executable = args.exe.resolve()
    environment = dict(os.environ)
    for key in ['DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT']:
        environment.pop(key, None)
    environment['PATH'] = '/usr/bin:/bin'
    checks = 0
    with tempfile.TemporaryDirectory(prefix='gp cli 中文 ') as temporary:
        directory = Path(temporary)
        # A copy of the executable alone must work, even after renaming it.
        single = directory / 'single executable'
        single.mkdir()
        copied = single / 'renamed-generator'
        shutil.copy2(executable, copied)
        executable = copied
        single.chmod(0o555)
        environment['XDG_CACHE_HOME'] = str(directory / '工具缓存')
        environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = str(directory / 'dotnet-cache')

        def run(*arguments, success=True):
            nonlocal checks
            result = subprocess.run([str(executable), *map(str, arguments)], cwd=directory,
                                    env=environment, capture_output=True, text=True, timeout=60)
            assert (result.returncode == 0) == success, result.stderr
            checks += 1
            return result

        assert 'Usage:' in run().stdout
        assert 'Usage:' in run('--help').stdout
        assert '1.3.1' in run('--version').stdout
        assert 'Error:' in run('--unknown', success=False).stderr
        assert 'Error:' in run('--generate', success=False).stderr
        run('--validate', 'missing.json', 'missing-report.json', success=False)
        assert not json.loads((directory / 'missing-report.json').read_text())['Success']
        run('--describe', 'catalog.json')
        config = json.loads(run('--example').stdout)
        assert 'Error:' in run('--ai-guide', success=False).stderr
        notices = run('--licenses').stdout
        assert 'LLVM-LICENSE.md' in notices and 'DOTNET-ThirdPartyNotices.txt' in notices
        config['Description'] = '中文 Unicode description'
        source = directory / 'project.json'
        source.write_text(json.dumps(config, ensure_ascii=False))
        before = source.read_bytes()
        run('--validate', source, source, success=False)
        assert source.read_bytes() == before
        run('--validate', source, 'validated.json')
        run('--generate', source, 'generated', 'generated/report.json', success=False)
        run('--generate', source, 'generated', 'report.json')
        assert (directory / 'generated/ta/ta_entry.c').is_file()
        run('--generate', source, 'generated', 'repeat.json', success=False)
        # Case-sensitive Linux paths must not be mistaken for the input file.
        run('--validate', source, 'PROJECT.json')
        assert source.read_bytes() == before
        assert list(single.iterdir()) == [executable]
        cache = Path(environment['XDG_CACHE_HOME'])
        formatter = next(cache.rglob('clang-format'))
        original = formatter.read_bytes()
        formatter.write_bytes(b'corrupted cache')
        formatter.chmod(0o600)
        run('--validate', source, 'repaired.json')
        assert formatter.read_bytes() == original and os.access(formatter, os.X_OK)
        formatter.unlink()
        # Two first-use processes must be able to initialize the same cache.
        children = [subprocess.Popen([str(executable), '--validate', str(source), str(directory / f'parallel-{i}.json')],
                                     cwd=directory, env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                    for i in range(2)]
        for child in children:
            stdout, stderr = child.communicate(timeout=60)
            assert child.returncode == 0, stderr
            checks += 1
        assert list(single.iterdir()) == [executable]
        single.chmod(0o755)
    print(json.dumps({'Passed': True, 'Commands': checks, 'UnicodeAndSpacePaths': True,
                      'NoExternalDotnetRequired': True, 'ExecutableOnly': True,
                      'CacheRepairAndConcurrency': True}))


if __name__ == '__main__':
    main()
