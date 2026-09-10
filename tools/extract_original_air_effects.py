import hashlib
import io
import re
from pathlib import Path

from PIL import Image, ImageDraw


SOURCE = Path(r"F:\zhongermate\data\acroarts")
OUTPUT = Path(r"F:\zhongermate\chunithm-mobile-unity\Build\inspection\air-effects")
FILES = (
    "chu_ef_air_bomb_04.afb",
    "chu_ef_airdown_bomb_04.afb",
    "CHU_EF_AirBounce_BombFin_v10.afb",
    "CHU_EF_AirBounce_KeyBeam_v10.afb",
    "CHU_EF_Airhold_Float_v10.afb",
    "CHU_EF_Airhold_Keep_v10.afb",
    "chu_ef_airhold_section_04.afb",
    "CHU_EF_Airhold_Sonicwave01_v10.afb",
    "CHU_EF_Airhold_Success_Goalline_v10.afb",
    "CHU_EF_Airhold_Success_v10.afb",
    "chu_ef_airlad_bombfin.afb",
    "CHU_EF_Tap_textAirAction_v10.afb",
)


def extract():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    hashes = set()
    paths = []
    for filename in FILES:
        payload = (SOURCE / filename).read_bytes()
        for index, match in enumerate(re.finditer(b"DDS ", payload)):
            image = Image.open(io.BytesIO(payload[match.start():])).convert("RGBA")
            digest = hashlib.sha256(image.tobytes()).hexdigest()
            if digest in hashes:
                continue
            hashes.add(digest)
            name = f"{Path(filename).stem}_{index:02d}_{image.width}x{image.height}.png"
            path = OUTPUT / name
            image.save(path)
            paths.append(path)
    return paths


def contact_sheet(paths):
    thumb_size = 180
    caption = 38
    columns = 5
    rows = (len(paths) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * thumb_size, rows * (thumb_size + caption)), (22, 25, 30, 255))
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(paths):
        image = Image.open(path).convert("RGBA")
        checker = Image.new("RGBA", image.size, (45, 48, 54, 255))
        checker.alpha_composite(image)
        checker.thumbnail((thumb_size - 12, thumb_size - 12))
        x = index % columns * thumb_size + (thumb_size - checker.width) // 2
        y = index // columns * (thumb_size + caption) + (thumb_size - checker.height) // 2
        sheet.alpha_composite(checker, (x, y))
        label = f"{index:02d} {path.stem[:23]}"
        draw.text((index % columns * thumb_size + 5, index // columns * (thumb_size + caption) + thumb_size + 4),
                  label, fill=(235, 239, 244, 255))
    sheet.save(OUTPUT / "contact-sheet.png")


if __name__ == "__main__":
    extracted = extract()
    contact_sheet(extracted)
    for number, path in enumerate(extracted):
        print(f"{number:02d} {path.name}")
