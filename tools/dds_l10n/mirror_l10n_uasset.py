import sys


MESSAGE = '''
mirror_l10n_uasset.py is intentionally disabled.

Do not mirror or rewrite assets into other L10N cultures. Other cultures must stay
as vanilla dead weight, exactly as the game ships them. DDS imports are base
Content only; text tools may write only to L10N/en.
'''


def main():
    print(MESSAGE.strip(), file=sys.stderr)
    return 2


if __name__ == '__main__':
    raise SystemExit(main())
