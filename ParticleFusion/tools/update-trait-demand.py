#!/usr/bin/env python3
"""从已发布的惩罚2配队生成技能需求资源；只读报告，不运行求解器或游戏。"""
import hashlib
import json
import sys
from collections import Counter
from pathlib import Path


def main():
    """按成品身份和单队最大同时份数统计实际技能槽，再写入可审计资源。"""
    if len(sys.argv) != 3:
        raise SystemExit('usage: update-trait-demand.py report.json output.json')
    source = Path(sys.argv[1]).read_bytes()
    report = json.loads(source)
    catalog_path = Path(__file__).resolve().parents[2] / 'Shared/Data/traits.json'
    skills = [item for item in json.loads(catalog_path.read_text()) if item['Tier'] == 3]
    fields = ('recipe', 'power', 'fortitude', 'slots', 'left', 'right', 'strikes')
    required = Counter()
    for variants in report['encounters'].values():
        local = Counter()
        for card in variants['2']['cards']:
            template = report['templates'][card['template']]
            identity = tuple(template[field] for field in fields) + (card['color'], tuple(card['traits']))
            local[identity] += 1
        for identity, count in local.items():
            required[identity] = max(required[identity], count)
    counts = Counter()
    for identity, copies in required.items():
        counts.update({skill: quantity * copies for skill, quantity in Counter(identity[-1]).items()})
    result = {
        'SourceSha256': hashlib.sha256(source).hexdigest(),
        'CatalogId': report['catalog']['id'],
        'Penalty': 2,
        'EncounterCount': len(report['encounters']),
        'CardCount': sum(required.values()),
        'IdentityFields': list(fields) + ['color', 'ordered traits'],
        'Skills': [{'Id': skill['Id'], 'Name': skill['Name'], 'Count': counts[skill['Id']],
                    'Weight': counts[skill['Id']] // 4 + 1} for skill in skills],
        'Cards': [dict(zip(list(fields) + ['color', 'traits'], identity), Copies=copies)
                  for identity, copies in sorted(required.items())]
    }
    target = Path(sys.argv[2])
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(f'{result["CardCount"]} cards; {sum(s["Count"] for s in result["Skills"])} tier-III slots; {len(skills)} skills')


if __name__ == '__main__':
    main()
