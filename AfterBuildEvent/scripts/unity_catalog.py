"""只读解析游戏 Addressables 二进制目录，按主资源包定位 Unity 对象。"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import struct

import UnityPy


@dataclass(frozen=True)
class Location:
    """目录中的资源位置；dependencies 保留游戏加载器使用的原始顺序。"""

    key: str
    internal_id: str
    dependencies: tuple[int, ...]


class BinaryCatalog:
    """读取 Addressables v2/v3 的键、字符串和依赖，不扫描全部游戏资源包。"""

    NULL = 0xFFFFFFFF
    UNICODE = 0x80000000
    DYNAMIC = 0x40000000
    OFFSET_MASK = 0x3FFFFFFF

    def __init__(self, path: Path):
        """验证目录头，并建立资源键到位置偏移的索引。"""
        self.data = path.read_bytes()
        magic, version, keys, *_ = self.unpack('<8I', 0)
        if magic != 0x0DE38942 or version not in (2, 3):
            raise ValueError(f'不支持的 Addressables 目录：{path}，版本 {version}')
        self.keys: dict[str, tuple[int, ...]] = {}
        payload = self.array(keys)
        if len(payload) % 8:
            raise ValueError('Addressables 键数组长度无效')
        for key_id, locations_id in struct.iter_unpack('<2I', payload):
            _, object_id = self.unpack('<2I', key_id)
            string_id, separator = self.unpack('<IH', object_id)
            key = self.string(string_id, chr(separator) if separator else '/')
            offsets = self.offsets(locations_id)
            if key in self.keys and self.keys[key] != offsets:
                raise ValueError(f'Addressables 资源键重复：{key}')
            self.keys[key] = offsets

    def unpack(self, fmt: str, offset: int) -> tuple:
        """所有二进制字段读取先检查边界，拒绝损坏或不兼容的目录。"""
        size = struct.calcsize(fmt)
        self.check(offset, size)
        return struct.unpack_from(fmt, self.data, offset)

    def check(self, offset: int, size: int) -> None:
        """检查字节区间，不允许负偏移和越界切片。"""
        if offset < 0 or size < 0 or offset + size > len(self.data):
            raise ValueError(f'Addressables 目录越界：{offset} + {size}')

    def array(self, offset: int) -> bytes:
        """读取以字节数为前缀的数组；NULL 表示空数组。"""
        if offset == self.NULL:
            return b''
        size, = self.unpack('<I', offset - 4)
        self.check(offset, size)
        return self.data[offset:offset + size]

    def offsets(self, offset: int) -> tuple[int, ...]:
        """读取资源位置偏移数组。"""
        payload = self.array(offset)
        if len(payload) % 4:
            raise ValueError('Addressables 位置数组长度无效')
        return tuple(item[0] for item in struct.iter_unpack('<I', payload))

    def string(self, identifier: int, separator: str = '') -> str:
        """还原 ASCII、UTF-16 或由逆向链表压缩的分段字符串。"""
        if identifier == self.NULL:
            return ''
        if separator and identifier & self.DYNAMIC:
            offset = identifier & self.OFFSET_MASK
            parts: list[str] = []
            seen: set[int] = set()
            while offset != self.NULL:
                if offset in seen:
                    raise ValueError('Addressables 字符串链存在循环')
                seen.add(offset)
                part, next_id = self.unpack('<2I', offset)
                parts.append(self.string(part))
                offset = next_id if next_id == self.NULL else next_id & self.OFFSET_MASK
            return separator.join(reversed(parts))
        unicode = bool(identifier & self.UNICODE)
        offset = identifier & self.OFFSET_MASK if unicode else identifier
        return self.array(offset).decode('utf-16-le' if unicode else 'ascii')

    def location(self, offset: int) -> Location:
        """解析资源位置中提取所需的字段。"""
        key, internal, _, dependencies, *_ = self.unpack('<7I', offset)
        return Location(self.string(key, '/'), self.string(internal, '/'), self.offsets(dependencies))

    def find(self, key: str) -> Location:
        """要求资源键唯一，避免误选同键的其他类型或子资源。"""
        offsets = self.keys.get(key, ())
        if len(offsets) != 1:
            raise ValueError(f'资源键应唯一：{key}，实际找到 {len(offsets)} 项')
        return self.location(offsets[0])


class CharacterResources:
    """持有一个角色所需的资源包；调用方每个角色结束后释放此实例。"""

    def __init__(self, root: Path, catalog: BinaryCatalog):
        """root 是 StreamingAssets/aa，catalog 在全部角色之间共享。"""
        self.root = root / 'StandaloneWindows64'
        self.catalog = catalog
        self.bundles: dict[str, UnityPy.Environment] = {}

    def resolve(self, key: str, expected_type: str):
        """按依赖首项加载主包；短 internal_id 只在该包内有效，不能跨包匹配。"""
        location = self.catalog.find(key)
        if not location.dependencies:
            raise ValueError(f'资源没有主包依赖：{key}')
        bundle = self.catalog.location(location.dependencies[0]).key
        if Path(bundle).name != bundle or not bundle.endswith('.bundle'):
            raise ValueError(f'无效的主资源包名称：{bundle}')
        if bundle not in self.bundles:
            self.bundles[bundle] = UnityPy.load(str(self.root / bundle))
        env = self.bundles[bundle]
        # Unity 容器路径可能统一转小写；数值短键保持不变。
        matches = [p for name, p in env.container.items()
                   if name.casefold() == location.internal_id.casefold()]
        if len(matches) != 1:
            raise ValueError(f'主包 {bundle} 中未唯一找到资源 {key} ({location.internal_id})')
        obj = matches[0].deref()
        if obj.type.name != expected_type:
            raise ValueError(f'资源 {key} 类型为 {obj.type.name}，预期 {expected_type}')
        return obj


def local_reference(owner, reference: dict, expected_type: str | None = None):
    """跟随角色预制体包内的对象引用；包结构变化时明确报错，不猜测同名对象。"""
    if reference['m_FileID'] != 0:
        raise ValueError(f'角色资源出现不支持的跨包引用：{reference}')
    obj = owner.assets_file.objects.get(reference['m_PathID'])
    if obj is None or (expected_type is not None and obj.type.name != expected_type):
        raise ValueError(f'角色资源缺少 {expected_type or "对象"} 引用：{reference}')
    return obj
