"""Place a reference and a matching render side by side for visual review."""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def crop_box(value):
    try:
        left, top, right, bottom = (int(part) for part in value.split(","))
    except ValueError as error:
        raise argparse.ArgumentTypeError("Use left,top,right,bottom in pixels") from error
    if not (0 <= left < right and 0 <= top < bottom):
        raise argparse.ArgumentTypeError("Crop must have positive area and non-negative coordinates")
    return left, top, right, bottom


def load_panel(path, box, width, mask_path):
    with Image.open(path) as source:
        original_size = source.size
        box = box or (0, 0, source.width, source.height)
        if box[2] > source.width or box[3] > source.height:
            raise ValueError(f"Crop {box} exceeds {path} at {source.size}")
        crop = source.convert("RGBA").crop(box)

    height = max(1, round(crop.height * width / crop.width))
    if height > 8192:
        raise ValueError(f"Scaled panel is too tall ({height} px). Check the crop and --width.")
    size = (width, height)
    view = crop.resize(size, Image.Resampling.LANCZOS)
    panel = Image.new("RGB", size, "#34383b")
    panel.paste(view, mask=view.getchannel("A"))
    if mask_path is None:
        return panel, None

    with Image.open(mask_path) as source_mask:
        if source_mask.size != original_size:
            raise ValueError(f"Mask {mask_path} must match {path} at {original_size}")
        mask = source_mask.convert("L").crop(box).point(lambda value: 255 if value >= 128 else 0)
    return panel, mask.resize(size, Image.Resampling.NEAREST)


def compare(args):
    paths = [args.reference, args.model, args.reference_mask, args.model_mask]
    if any(path and path.resolve() == args.out.resolve() for path in paths):
        raise ValueError("Choose an output other than an input image")
    if bool(args.reference_mask) != bool(args.model_mask):
        raise ValueError("Provide both --reference-mask and --model-mask")
    if not 64 <= args.width <= 2048:
        raise ValueError("--width must be between 64 and 2048 pixels per panel")
    panels = [
        load_panel(args.reference, args.reference_box, args.width, args.reference_mask),
        load_panel(args.model, args.model_box, args.width, args.model_mask),
    ]
    gap, margin, label_height = 12, 12, 24
    row_height = max(panel.height for panel, _ in panels)
    titles = ["Reference", "Model"]
    rows = 2 if args.reference_mask else 1
    board = Image.new("RGB", (2 * (args.width + margin) + gap,
                              margin * (rows + 1) + rows * (row_height + label_height)), "#25282b")
    draw = ImageDraw.Draw(board)
    for row in range(rows):
        top = margin + row * (row_height + label_height + margin)
        for column, panel in enumerate(panels):
            left = margin + column * (args.width + gap)
            draw.text((left, top), titles[column] + (" | silhouette" if row else ""), fill="#eeeeee")
            content = panel[row]
            board.paste(content, (left, top + label_height + (row_height - content.height) // 2))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    board.save(args.out, format="PNG")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("model", type=Path)
    parser.add_argument("--out", type=Path, required=True, help="Output PNG")
    parser.add_argument("--width", type=int, default=640, help="Display width per crop in pixels")
    parser.add_argument("--reference-box", type=crop_box, help="Reference crop: left,top,right,bottom")
    parser.add_argument("--model-box", type=crop_box, help="Model crop: left,top,right,bottom")
    parser.add_argument("--reference-mask", type=Path, help="Optional full-size white-on-black mask")
    parser.add_argument("--model-mask", type=Path, help="Optional full-size white-on-black mask")
    args = parser.parse_args()
    try:
        compare(args)
    except (OSError, ValueError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    main()
