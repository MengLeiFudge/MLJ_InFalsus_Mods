#!/usr/bin/env python3
"""将生图接口返回的高分辨率方图归一化并转换为 Thunderstore 发布图标。"""

from pathlib import Path

from PIL import Image

SOURCE_SIZE = (1024, 1024)
PUBLISH_SIZE = (256, 256)
PROJECTS = ("ParticleFusion", "SkillSelection")


def prepare_icon(project_root: Path, project: str) -> None:
    """校验一个模组的生图结果，归一化母图并以 Lanczos 生成发布 PNG。"""
    assets = project_root / project / "Assets"
    source_path = assets / "icon-source.png"
    target_path = assets / "icon.png"
    with Image.open(source_path) as source:
        if source.width != source.height or source.width < SOURCE_SIZE[0]:
            raise ValueError(
                f"{source_path} 必须为至少 1024 像素的正方形，当前为 {source.width}x{source.height}"
            )
        master = source.convert("RGB")
        if master.size != SOURCE_SIZE:
            master = master.resize(SOURCE_SIZE, Image.Resampling.LANCZOS)
            master.save(source_path, format="PNG", optimize=True)
        master.resize(PUBLISH_SIZE, Image.Resampling.LANCZOS).save(
            target_path,
            format="PNG",
            optimize=True,
        )
    print(f"已更新 {target_path}")


def main() -> None:
    """处理仓库内两个模组的最终图标。"""
    project_root = Path(__file__).resolve().parents[1]
    for project in PROJECTS:
        prepare_icon(project_root, project)


if __name__ == "__main__":
    main()
