"""v6 冻结模型推理：仅依赖 Python 标准库，不训练、不访问网络或游戏。"""
from __future__ import annotations

import math


def allowed_constants(level: int) -> tuple[float, ...]:
    """按官方整数等级选择档位；最高档始终属于同一级。"""
    if isinstance(level, bool) or not isinstance(level, int) or level < 0:
        raise ValueError('官方等级必须是非负整数')
    offsets = (0, 5) if level < 7 else (0, 5, 8) if level < 9 else tuple(range(10))
    return tuple((10 * level + offset) / 10 for offset in offsets)


def quantize(level: int, estimate: float) -> float:
    """取同级最近合法档位，浮点误差范围内等距时取较高档。"""
    if not math.isfinite(estimate):
        raise ValueError('定数估计必须是有限数')
    candidates = allowed_constants(level)
    bounded = min(level + .9, max(level, estimate))
    return min(candidates, key=lambda value: (round(abs(value - bounded), 12), -value))


def features(row: dict, columns: list[str]) -> list[float]:
    """特征口径沿用 v4/v5：对非负谱面特征应用 log1p。"""
    values = [float(row['f'][column]) for column in columns]
    if any(not math.isfinite(value) or value < 0 for value in values):
        raise ValueError('模型输入特征必须为有限非负数')
    return [math.log1p(value) for value in values]


def dot(left: list[float], right: list[float]) -> float:
    return sum(a * b for a, b in zip(left, right, strict=True))


def structure_estimate(model: dict, row: dict) -> float:
    """本难度结构等级相对同级中心的偏离；未知等级只取本级中点。"""
    level = row['official_level']
    score = model['intercept'] + dot(features(row, model['columns']), model['weights'])
    center = model['centers'].get(str(level), score)
    mean = model['means'].get(str(level), .45)
    return level + mean + model['beta'] * (score - center)


def ensemble_estimate(model: dict, row: dict) -> float:
    """独立难度的线性岭与 RBF 核岭各占一半，不输入其他难度标记。"""
    level = row['official_level']
    x = features(row, model['columns'])
    linear = model['linear']
    z = [(value - mean) / scale for value, mean, scale in
         zip(x, linear['mean'], linear['scale'], strict=True)]
    linear_delta = min(.9, max(0., dot([1., float(level), *z], linear['weights'])))
    kernel = model['kernel']
    z = [min(5., max(-5., (value - mean) / scale)) for value, mean, scale in
         zip([*x, float(level)], kernel['mean'], kernel['scale'], strict=True)]
    kernel_delta = kernel['target_mean']
    for center, weight in zip(kernel['centers'], kernel['weights'], strict=True):
        distance = sum((a - b) ** 2 for a, b in zip(z, center, strict=True))
        kernel_delta += math.exp(-kernel['gamma'] * distance / len(z)) * weight
    return level + (linear_delta + min(.9, max(0., kernel_delta))) / 2


def predict(model: dict, row: dict) -> tuple[float, float, str]:
    """返回限制后的连续值、分档值和实际使用的方法。"""
    if row['difficulty'] != model['difficulty']:
        raise ValueError('谱面难度与模型归属不一致')
    level = row['official_level']
    allowed_constants(level)
    method = '结构估计'
    estimate = structure_estimate(model['structure'], row)
    regression = model.get('ensemble')
    if regression is not None and regression['minimum_level'] <= level <= regression['maximum_level']:
        estimate = ensemble_estimate(regression, row)
        method = '独立线性与核岭'
    if not math.isfinite(estimate):
        raise ValueError('模型输出不是有限数')
    estimate = min(level + .9, max(level, estimate))
    return estimate, quantize(level, estimate), method
