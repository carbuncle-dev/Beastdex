# SPDX-License-Identifier: GPL-3.0-only
"""Python reference-policy checks, NOT execution of the C# implementation.
Run optionally with Python 3.10+. build.bat uses the C# regression suite instead.
"""
from dataclasses import dataclass, replace
from math import hypot, isclose

@dataclass(frozen=True)
class Source:
    beast: int
    level: int | None
    territory: int
    duty: int = 0
    encounter: int = 0
    reported: bool = True
    map_id: int = 100
    position: tuple[float, float] | None = (20, 30)

def rank(s):
    return (s.level if s.level is not None and s.level > 0 else float('inf'),
            bool(s.duty), s.encounter, s.beast)

def next_catches(sources, obtained, bst, capture_available=True):
    if not capture_available or bst is None or bst <= 0:
        return []
    result = []
    for beast in sorted({s.beast for s in sources} - set(obtained)):
        eligible = [s for s in sources if s.beast == beast and s.reported and
                    s.level is not None and 0 < s.level <= bst]
        if eligible:
            result.append(min(eligible, key=rank))
    return sorted(result, key=rank)

def hint_matches(s, territory, duty=0):
    return s.duty == duty if duty else not s.duty and s.territory == territory

@dataclass(frozen=True)
class Destination:
    id: int
    territory: int
    map_id: int
    position: tuple[float, float] | None

def destination(s, choices):
    if s.duty:
        return None
    choices = [d for d in choices if d.territory == s.territory]
    measured = [d for d in choices if s.position is not None and d.position is not None and
                s.map_id and s.map_id == d.map_id]
    if measured:
        return min(measured, key=lambda d: (hypot(d.position[0]-s.position[0], d.position[1]-s.position[1]), d.id))
    return choices[0] if len(choices) == 1 else None

def main():
    checks = 0
    def check(condition, message):
        nonlocal checks
        assert condition, message
        checks += 1
    high = Source(1, 45, 10)
    low = Source(1, 20, 20, duty=77, map_id=200, position=None)
    lookalike = replace(high, level=1, reported=False)
    other = Source(2, 6, 30, map_id=300)
    unknown = Source(3, None, 10)
    sources = [high, low, lookalike, other, unknown]
    check([s.beast for s in next_catches(sources, [], 26)] == [2, 1], 'global lowest-level queue')
    check(next_catches(sources, [2], 26) == [low], 'obtained removal and lower-level duty preferred')
    check(next_catches([low], [], 19) == [], 'future level excluded')
    check(next_catches([lookalike], [], 99) == [], 'candidate excluded')
    check(next_catches(sources, [], 99, False) == [], 'unknown capture state excluded')
    check(next_catches(sources, [], None) == [], 'unknown BST excluded')
    check(hint_matches(high, 10), 'hint attaches to matching territory')
    check(not hint_matches(low, 10), 'cross-area alternative retains separate hint')
    check(hint_matches(low, 0, 77), 'matching duty')
    check(not hint_matches(low, 0, 78), 'different duty not merged')
    same_world = replace(low, territory=10, duty=0)
    check(min([low, same_world], key=rank) == same_world, 'overworld wins on level tie')
    check(min([same_world, replace(same_world, encounter=1)], key=rank) == same_world, 'ordinary wins encounter tie')
    near = Destination(1, 10, 100, (22, 32))
    far = Destination(2, 10, 100, (500, 500))
    foreign = Destination(3, 30, 300, (20, 30))
    check(destination(high, [far, foreign, near]) == near, 'same-map nearest')
    check(destination(replace(high, position=None), [far, near]) is None, 'area-only asks for choice')
    check(destination(replace(high, position=None), [near]) == near, 'single destination direct')
    check(destination(low, [Destination(4,20,200,(0,0))]) is None, 'duty cannot become overworld TP')
    check(destination(high, [replace(near, position=None), replace(far, position=None)]) is None, 'missing coordinates not guessed')
    for size in (100, 200, 400):
        for offset in (-100, 0, 150):
            for world in (-512, 0, 650):
                coord = .02 * (world + offset) + 2048 / size + 1
                inverse = (coord - 1 - 2048 / size) / .02 - offset
                check(isclose(world, inverse, abs_tol=.00001), 'world/map inverse')
    check(isclose(.02 * 0 + 2048/100+1, 21.48), 'Dalamud map origin formula')
    print(f'PASS: {checks} Python reference-policy checks (C# not executed).')
    return checks

if __name__ == '__main__':
    main()
