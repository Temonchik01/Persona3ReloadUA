"""Run retoc with a time limit on Windows and Linux."""
import os
import subprocess


def command(tool, args, budget, seconds, platform=None):
    platform = platform or os.name
    base = [str(tool), *map(str, args)]
    if platform == 'nt':
        return base
    return ['prlimit', f'--as={budget}', f'--cpu={seconds}', '--',
            'timeout', '--kill-after=5s', str(seconds), *base]


def run(tool, args, budget, seconds, log):
    # Linux enforces memory and CPU limits. Windows enforces elapsed time;
    # its retoc process is terminated by subprocess on timeout.
    return subprocess.run(command(tool, args, budget, seconds),
                          stdout=log, stderr=subprocess.STDOUT, timeout=seconds+10)
