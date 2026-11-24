"""Import a Codex pet ZIP or unpacked folder without extracting archive paths."""

import hashlib
import io
import json
import re
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / ".vendor"))
from PIL import Image

PET_ROOT = Path(__import__("os").environ.get("LOCALAPPDATA", str(ROOT))) / "KiroPet" / "pets"
SHEET_SIZES = {1: (1536, 1872), 2: (1536, 2288)}
MAX_SHEET_BYTES = 20_000_000


def safe_relative_path(value):
    if not isinstance(value, str) or "\\" in value or ":" in value:
        raise ValueError("Invalid sprite path in pet.json.")
    path = PurePosixPath(value)
    if path.is_absolute() or any(part in ("", ".", "..") for part in value.split("/")):
        raise ValueError("Invalid sprite path in pet.json.")
    return path


def pack_from_zip(source):
    if source.stat().st_size > 50_000_000:
        raise ValueError("Pet ZIP is too large.")
    with zipfile.ZipFile(source) as archive:
        manifests = [
            entry
            for entry in archive.infolist()
            if PurePosixPath(entry.filename).name == "pet.json"
            and len(PurePosixPath(entry.filename).parts) <= 2
        ]
        if len(manifests) != 1:
            raise ValueError("Expected one pet.json at the ZIP root or one folder below it.")
        manifest_entry = manifests[0]
        safe_relative_path(manifest_entry.filename)
        if manifest_entry.file_size > 65_536:
            raise ValueError("pet.json is too large.")
        manifest = json.loads(archive.read(manifest_entry).decode("utf-8-sig"))
        if not isinstance(manifest, dict):
            raise ValueError("pet.json must contain an object.")
        relative_sheet = safe_relative_path(manifest.get("spritesheetPath"))
        prefix = PurePosixPath(manifest_entry.filename).parent
        sheet_name = str(prefix / relative_sheet)
        sheet_entry = archive.getinfo(sheet_name)
        if sheet_entry.file_size > MAX_SHEET_BYTES:
            raise ValueError("Sprite sheet is too large.")
        return manifest, archive.read(sheet_entry)


def pack_from_folder(source):
    manifest_path = source / "pet.json"
    if not manifest_path.is_file():
        matches = list(source.glob("*/pet.json"))
        if len(matches) != 1:
            raise ValueError("Choose the folder containing pet.json.")
        manifest_path = matches[0]
    if manifest_path.stat().st_size > 65_536:
        raise ValueError("pet.json is too large.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if not isinstance(manifest, dict):
        raise ValueError("pet.json must contain an object.")
    relative_sheet = safe_relative_path(manifest.get("spritesheetPath"))
    sheet_path = manifest_path.parent.joinpath(*relative_sheet.parts).resolve()
    if not sheet_path.is_relative_to(manifest_path.parent.resolve()):
        raise ValueError("Sprite path must stay inside the pet folder.")
    if not sheet_path.is_file() or sheet_path.stat().st_size > MAX_SHEET_BYTES:
        raise ValueError("Sprite sheet is missing or too large.")
    return manifest, sheet_path.read_bytes()


def import_pet(source):
    source = Path(source).expanduser().resolve()
    if source.is_dir():
        manifest, sheet_bytes = pack_from_folder(source)
    elif source.is_file() and source.name.lower().endswith(".zip"):
        manifest, sheet_bytes = pack_from_zip(source)
    else:
        raise ValueError("Choose a Codex pet ZIP or unpacked pet folder.")

    pet_id = manifest.get("id")
    display_name = manifest.get("displayName")
    version = manifest.get("spriteVersionNumber", 1)
    if not isinstance(pet_id, str) or not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}", pet_id):
        raise ValueError("pet.json has an invalid pet id.")
    if not isinstance(display_name, str) or not display_name.strip() or len(display_name) > 64:
        raise ValueError("pet.json needs a short displayName.")
    if type(version) is not int or version not in SHEET_SIZES:
        raise ValueError(f"Sprite version {version} is not supported. Supported versions: 1 and 2.")

    with Image.open(io.BytesIO(sheet_bytes)) as source_image:
        if source_image.size != SHEET_SIZES[version]:
            raise ValueError(
                f"Version {version} needs a {SHEET_SIZES[version][0]}x"
                f"{SHEET_SIZES[version][1]} sprite sheet; got "
                f"{source_image.width}x{source_image.height}."
            )
        picture = source_image.convert("RGBA")
    visible = picture.getchannel("A").point(lambda alpha: 255 if alpha > 16 else 0)
    frame_boxes = [
        [
            visible.crop(
                (column * 192, row * 208, (column + 1) * 192, (row + 1) * 208)
            ).getbbox()
            for column in range(8)
        ]
        for row in range(picture.height // 208)
    ]
    frame_columns = [
        [column for column, box in enumerate(row) if box is not None]
        for row in frame_boxes
    ]
    if not frame_columns[0]:
        raise ValueError("The sprite sheet has no visible idle frames.")
    highest_visible_pixel = min(
        box[1] for row in frame_boxes for box in row if box is not None
    )

    PET_ROOT.mkdir(parents=True, exist_ok=True)
    key = f"{pet_id}-{hashlib.sha256(sheet_bytes).hexdigest()[:10]}"
    destination = PET_ROOT / key
    if destination.exists():
        if not (destination / "pet.json").is_file() or not (destination / "spritesheet.png").is_file():
            raise ValueError(f"An incomplete import already uses {key}.")
        installed_path = destination / "pet.json"
        installed = json.loads(installed_path.read_text(encoding="utf-8"))
        if installed.get("highestVisiblePixel") != highest_visible_pixel:
            installed["highestVisiblePixel"] = highest_visible_pixel
            temporary_path = destination / ".pet.json.tmp"
            temporary_path.write_text(
                json.dumps(installed, ensure_ascii=False, indent=2), encoding="utf-8"
            )
            temporary_path.replace(installed_path)
        already_present = True
    else:
        stage = Path(tempfile.mkdtemp(prefix=".import-", dir=PET_ROOT))
        if stage.resolve().parent != PET_ROOT.resolve():
            raise RuntimeError("Import folder escaped the pet library.")
        try:
            picture.save(stage / "spritesheet.png", format="PNG")
            installed = {
                "key": key,
                "id": pet_id,
                "displayName": display_name.strip(),
                "spriteVersionNumber": version,
                "frameColumns": frame_columns,
                "highestVisiblePixel": highest_visible_pixel,
            }
            (stage / "pet.json").write_text(
                json.dumps(installed, ensure_ascii=False, indent=2), encoding="utf-8"
            )
            stage.rename(destination)
        finally:
            if stage.exists() and stage.resolve().parent == PET_ROOT.resolve():
                shutil.rmtree(stage)
        already_present = False

    return {
        "key": key,
        "displayName": display_name.strip(),
        "spriteVersionNumber": version,
        "alreadyPresent": already_present,
    }


if __name__ == "__main__":
    try:
        import argparse
        parser = argparse.ArgumentParser()
        parser.add_argument("source")
        parser.add_argument("--library-root", type=Path, default=PET_ROOT)
        arguments = parser.parse_args()
        PET_ROOT = arguments.library_root
        print(json.dumps(import_pet(arguments.source), ensure_ascii=True))
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
