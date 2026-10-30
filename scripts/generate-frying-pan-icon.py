#!/usr/bin/env python3
"""Author the pan icon using the same illustrated UI palette as the item atlas."""
import importlib.util
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("item_icons", ROOT / "scripts/generate-item-icons.py")
icons = importlib.util.module_from_spec(spec)
spec.loader.exec_module(icons)
c = icons.Canvas()
c.line([(20,110),(61,67)], icons.INK, 19)
c.line([(20,108),(58,68)], "#a96c38", 12)
c.line([(20,103),(52,69)], "#e5b76c", 3)
c.line([(45,83),(61,65)], "#718e98", 10)
c.line([(48,81),(62,65)], "#b6c9c8", 3)
c.ellipse((40,18,115,85), "#304651", icons.INK, 3)
c.ellipse((41,14,115,79), "#7f9da5", icons.INK, 3)
c.ellipse((47,20,109,73), "#263f4b", icons.INK, 2)
c.ellipse((52,25,105,69), "#365361", None)
c.ellipse((57,30,101,66), "#2d4754", None)
c.curve([(51,42),(54,27),(71,22),(84,25)], "#b4cece", 2.5)
c.curve([(61,67),(75,75),(96,67),(104,52)], "#526e7a", 2)
c.ellipse((49,63,53,67), "#dae4d7", None)
c.ellipse((54,58,58,62), "#dae4d7", None)
c.line([(30,91),(37,98)], "#684c32", 2)
c.line([(35,85),(42,92)], "#684c32", 2)
c.ellipse((15,104,22,112), "#253f4a", "#d8b77a", 1)
dest = ROOT / "samples/TacticalGame.Unity/Assets/HookIsland/Resources/UI/FryingPanIcon.png"
c.finish().save(dest)
meta = dest.with_suffix(".png.meta")
if not meta.exists():
    template = dest.with_name("HudIcons.png.meta").read_text()
    template = template.replace("10624b2cb47ae47c29c27b4d7824ae8d", uuid.uuid5(uuid.NAMESPACE_URL,"Advanture/UI/FryingPanIcon").hex)
    meta.write_text(template)
print(dest)
