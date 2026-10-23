"""将已生成的 v6 结果导出为 DLL 内置表，不训练或重新计算谱面。"""
from pathlib import Path
import json

HERE = Path(__file__).resolve().parent
rows = json.loads((HERE / 'results.json').read_text(encoding='utf-8'))['charts']
difficulties = {'MIN': 1, 'EVO': 2, 'ULT': 4, 'FBD': 8}
table = {}
for row in rows:
    chart_id = row['chart_id']
    if chart_id in table:
        raise ValueError(f'重复谱面：{chart_id}')
    table[chart_id] = {
        'Level': row['official_level'],
        'Difficulty': difficulties[row['difficulty']],
        'Constant': row['constant'],
    }
output = HERE.parent.parent / 'MengLeiConstants.json'
with output.open('x', encoding='utf-8', newline='\n') as stream:
    stream.write(json.dumps(table, ensure_ascii=False, indent=2) + '\n')
print(f'已导出 {len(table)} 个定数：{output}')
