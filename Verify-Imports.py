"""Verify compatible imports and path rejection using generated fixtures."""

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("pet_import", ROOT / "Import-CodexPet.py")
pet_import = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pet_import)
from PIL import Image

with tempfile.TemporaryDirectory(prefix="kiro-pet-import-") as directory:
    root = Path(directory)
    pet_import.PET_ROOT = root / "library"
    for version, rows in ((1, 9), (2, 11)):
        image = Image.new("RGBA", (1536, rows * 208))
        for row in range(rows):
            for column in range(8):
                image.paste((100, 160, 220, 255), (column * 192 + 60, row * 208 + 45, column * 192 + 120, row * 208 + 165))
        sheet = io.BytesIO()
        image.save(sheet, "WEBP", lossless=True)
        manifest = {
            "id": f"fixture-{version}", "displayName": "貓咪",
            "spriteVersionNumber": version, "spritesheetPath": "spritesheet.webp",
        }
        pack = root / f"fixture-{version}.zip"
        with zipfile.ZipFile(pack, "w") as archive:
            archive.writestr("pet.json", json.dumps(manifest))
            archive.writestr("spritesheet.webp", sheet.getvalue())
        result = pet_import.import_pet(pack)
        installed = pet_import.PET_ROOT / result["key"]
        metadata = json.loads((installed / "pet.json").read_text(encoding="utf-8"))
        assert metadata["highestVisiblePixel"] == 45
        with Image.open(installed / "spritesheet.png") as decoded:
            assert decoded.size == (1536, rows * 208)
            assert decoded.mode == "RGBA" and decoded.getpixel((0, 0))[3] == 0
        assert pet_import.import_pet(pack)["alreadyPresent"]
        folder = root / f"folder-{version}"
        folder.mkdir()
        (folder / "pet.json").write_text(json.dumps(manifest), encoding="utf-8")
        (folder / "spritesheet.webp").write_bytes(sheet.getvalue())
        assert pet_import.import_pet(folder)["key"] == result["key"]
        original_keys = set(pet_import.PET_ROOT.iterdir())
        manifest["spritesheetPath"] = "../outside.webp"
        (folder / "pet.json").write_text(json.dumps(manifest), encoding="utf-8")
        try:
            pet_import.import_pet(folder)
        except ValueError:
            assert set(pet_import.PET_ROOT.iterdir()) == original_keys
        else:
            raise AssertionError("Import accepted a path outside the pet folder.")
print("Imports PASS: v1/v2, ZIP/folder, transparency, Unicode, cached bounds, reuse, path rejection.")
