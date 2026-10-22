# /// script
# requires-python = ">=3.12,<3.14"
# dependencies = ["UnityPy==1.25.3", "Pillow==12.3.0"]
# ///
"""离线合成游戏总表内所有角色的默认立绘，按游戏显示比例输出透明 PNG。"""
from __future__ import annotations

import argparse
import gc
import math
import os
from pathlib import Path
import re
import sys
import tempfile

from PIL import Image

from unity_catalog import BinaryCatalog, CharacterResources, local_reference


# FullCharacterMaterialControl 的部件编号：0 睁眼、1 闭眼、2 眉毛、
# 3 闭嘴、4 半张嘴、5 张嘴、6 前发。FullCharacter shader 依次叠加嘴、眉、眼、前发。
# 默认表情固定睁眼、闭嘴，不叠加眨眼帧或表情过渡的另一套素材。
DEFAULT_LAYERS = (3, 2, 0, 6)
IDENTIFIER_FIELDS = ('Pose', 'OutfitPart', 'CharacterExpression', 'FaceExpressionSet')


def only(items: list, description: str):
    """要求引用链上的关键对象唯一，避免生成看似成功但混用角色的图片。"""
    if len(items) != 1:
        raise ValueError(f'{description} 应有一项，实际 {len(items)} 项')
    return items[0]


def default_parts(face: dict, character: int) -> tuple[dict, dict[int, dict]]:
    """按游戏 GetDefaultAtlasPart 的首项规则选择默认姿势、服装和表情组。"""
    if face['CharacterIdentifier'] != character:
        raise ValueError('角色总表与表情图集的角色编号不一致')
    identifiers, rectangles = face['IdentifierMapping'], face['RectMapping']
    if not identifiers or len(identifiers) != len(rectangles):
        raise ValueError('表情标识与坐标数组为空或长度不一致')
    default = identifiers[0]
    parts: dict[int, dict] = {}
    for identifier, rectangle in zip(identifiers, rectangles, strict=True):
        if all(identifier[key] == default[key] for key in IDENTIFIER_FIELDS):
            part = identifier['FaceExpressionPart']
            if part in parts:
                raise ValueError(f'默认表情存在重复部件：{part}')
            parts[part] = rectangle
    if any(part not in parts for part in DEFAULT_LAYERS):
        raise ValueError('默认表情缺少嘴、眉毛、眼睛或前发的坐标记录')
    return default, parts


def pixel_rect(rect: dict, size: tuple[int, int]) -> tuple[float, float, float, float]:
    """将 Unity 左下角原点的归一化 UV 转为图片左上角原点的浮点像素边界。"""
    x, y, width, height = (float(rect[key]) for key in ('x', 'y', 'width', 'height'))
    if (not all(math.isfinite(v) for v in (x, y, width, height))
            or min(x, y, width, height) < 0 or x + width > 1 or y + height > 1):
        raise ValueError(f'图集坐标超出归一化范围：{rect}')
    w, h = size
    return x * w, (1 - y - height) * h, (x + width) * w, (1 - y) * h


def composite_part(body: Image.Image, atlas: Image.Image, mapping: dict) -> None:
    """用像素中心映射采样表情部件，保留半像素坐标，避免取整造成五官错位。"""
    source = pixel_rect(mapping['AtlasRect'], atlas.size)
    target = pixel_rect(mapping['OriginalRect'], body.size)
    sw, sh = source[2] - source[0], source[3] - source[1]
    tw, th = target[2] - target[0], target[3] - target[1]
    # 幽灵角色的表情记录明确使用零面积矩形，身体本身就是完整立绘。
    if sw == sh == tw == th == 0:
        return
    if min(sw, sh, tw, th) <= 0:
        raise ValueError('表情部件的源矩形与目标矩形面积不匹配')
    left, top = math.ceil(target[0] - 0.5), math.ceil(target[1] - 0.5)
    right = min(body.width, math.floor(target[2] - 0.5) + 1)
    bottom = min(body.height, math.floor(target[3] - 0.5) + 1)
    if right <= left or bottom <= top:
        raise ValueError('表情部件在原始身体分辨率下没有可采样像素')
    extent = (
        source[0] + (left - target[0]) * sw / tw,
        source[1] + (top - target[1]) * sh / th,
        source[0] + (right - target[0]) * sw / tw,
        source[1] + (bottom - target[1]) * sh / th,
    )
    with atlas.transform((right - left, bottom - top), Image.Transform.EXTENT,
                         extent, Image.Resampling.BILINEAR) as layer:
        body.alpha_composite(layer, (left, top))


def portrait_size(control, source_size: tuple[int, int]) -> tuple[int, int]:
    """按预制体显示宽高比恢复画布，保留贴图高度，避免把存储比例当成人物比例。"""
    reference = control.read_typetree()['self']
    transform = local_reference(control, reference, 'MonoBehaviour').read_typetree()['constrained2DTransform']
    if any(transform['sizeInParentNormalized'][axis] != 0 for axis in ('x', 'y')):
        raise ValueError('角色画布依赖父容器尺寸，无法独立确定显示比例')
    display = []
    for axis in ('x', 'y'):
        values = [float(transform[field][axis])
                  for field in ('sizeInPixels', 'renderableScale', 'typeSpecificExtraScale')]
        if any(not math.isfinite(value) or value <= 0 for value in values):
            raise ValueError(f'角色画布的 {axis} 轴尺寸或缩放无效：{values}')
        display.append(math.prod(values))
    width = source_size[1] * display[0] / display[1]
    if not math.isfinite(width) or width < 1:
        raise ValueError('角色画布的显示宽高比无效')
    return round(width), source_size[1]


def save_png(image: Image.Image, path: Path) -> None:
    """先写同目录临时文件，再原子替换此角色的 PNG，失败不会留下半张目标图。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix='.portrait-', suffix='.png', delete=False) as file:
        temporary = Path(file.name)
    try:
        image.save(temporary, format='PNG')
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def export_character(root: Path, catalog: BinaryCatalog, entry: dict, output: Path) -> Path:
    """沿角色引用合成默认造型，恢复预制体显示比例，按角色名直接写入输出目录。"""
    resources = CharacterResources(root, catalog)
    character = entry['Character']
    if type(character) is not int or character < 0:
        raise ValueError(f'无效角色编号：{character}')
    prefab = resources.resolve(entry['Reference']['m_AssetGUID'], 'GameObject')
    prefab_data = prefab.read_typetree()
    containers = []
    for item in prefab_data['m_Component']:
        obj = local_reference(prefab, item['component'])
        if obj.type.name == 'MonoBehaviour':
            data = obj.read_typetree()
            if 'fullCharacterMaterialControl' in data:
                containers.append((obj, data))
    container, data = only(containers, '角色容器组件')
    if data['characterIdentifier'] != character:
        raise ValueError('角色总表与预制体的角色编号不一致')
    control = local_reference(container, data['fullCharacterMaterialControl'], 'MonoBehaviour')
    control_data = control.read_typetree()
    face = local_reference(control, control_data['faceAtlasData'], 'MonoBehaviour').read_typetree()
    outfit = local_reference(control, control_data['outfitMappingDetails'], 'MonoBehaviour').read_typetree()
    default, parts = default_parts(face, character)
    lengths = [len(outfit[key]) for key in ('Pose', 'OutfitPart', 'BodyRef')]
    if len(set(lengths)) != 1:
        raise ValueError('服装、姿势与身体引用数组长度不一致')
    body_ref = only([ref for pose, part, ref in zip(outfit['Pose'], outfit['OutfitPart'], outfit['BodyRef'], strict=True)
                     if pose == default['Pose'] and part == default['OutfitPart']], '默认身体引用')
    body_object = resources.resolve(body_ref['m_AssetGUID'], 'Texture2D')
    material = local_reference(control, control_data['originalMaterial'], 'Material')
    properties = material.read_typetree()['m_SavedProperties']
    atlas_ref = only([value['m_Texture'] for name, value in properties['m_TexEnvs']
                      if name == '_FaceTextureAtlas'], '表情图集纹理引用')
    atlas_object = local_reference(material, atlas_ref, 'Texture2D')
    name = prefab_data['m_Name'].removesuffix('CharacterContainer')
    name = re.sub(r'[^\w-]', '_', name).strip('_') or 'Character'
    path = output / f'{name}.png'
    with body_object.read().image as source, atlas_object.read().image as face_source:
        with source.convert('RGBA') as body, face_source.convert('RGBA') as atlas:
            for part in DEFAULT_LAYERS:
                composite_part(body, atlas, parts[part])
            with body.getchannel('A') as alpha:
                if alpha.getbbox() is None:
                    raise ValueError('合成结果完全透明')
            # 五官与身体先在同一 UV 空间合成，再整体恢复显示比例，避免分层缩放错位。
            with body.resize(portrait_size(control, body.size), Image.Resampling.LANCZOS) as portrait:
                save_png(portrait, path)
                print(f'已提取 {name}：{portrait.width}×{portrait.height} → {path}', flush=True)
    return path


def main() -> int:
    """只读取指定游戏目录，全部角色完成才返回成功退出码。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    game = args.game_dir.resolve(strict=True)
    output = args.output.resolve()
    if output.is_relative_to(game):
        raise ValueError('立绘输出目录不能位于游戏目录内')
    root = game / 'infalsus_Data' / 'StreamingAssets' / 'aa'
    catalog = BinaryCatalog(root / 'catalog.bin')
    registry = CharacterResources(root, catalog)
    table = registry.resolve('release/FullCharacterContainerAssets.asset', 'MonoBehaviour').read_typetree()
    entries = table['characterIdentifierToCharacterContainerPrefabReference']
    if not entries or len({entry['Character'] for entry in entries}) != len(entries):
        raise ValueError('角色资源总表为空或包含重复角色')
    del registry, table
    gc.collect()
    for entry in sorted(entries, key=lambda value: value['Character']):
        try:
            export_character(root, catalog, entry, output)
        except Exception as exc:
            raise RuntimeError(f'角色 {entry["Character"]} 提取失败：{exc}') from exc
        finally:
            # UnityPy 对象与资源包相互引用；逐角色回收，避免全部大纹理同时驻留。
            gc.collect()
    print(f'完成：已提取 {len(entries)} 个角色的默认立绘。输出目录：{output}', flush=True)
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print(f'立绘提取中断：{error}', file=sys.stderr)
        sys.exit(1)
