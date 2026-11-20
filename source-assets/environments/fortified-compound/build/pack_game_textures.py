"""Pack source scans for the Unity surface shader. Run with Python and Pillow."""
from pathlib import Path
from PIL import Image

source = Path(__file__).resolve().parents[1]
out = source.parents[2]/'samples/TacticalGame.Unity/Assets/TacticalGame/Art/FortifiedCompound'
out.mkdir(parents=True,exist_ok=True)
for folder in sorted((source/'textures').iterdir()):
    channels = []
    for channel in ('Diffuse','Rough','Displacement'):
        with Image.open(folder/(channel+'.jpg')) as image:
            channels.append(image.convert('L').resize((1024,1024),Image.Resampling.LANCZOS))
    Image.merge('RGB',channels).save(out/(folder.name+'.png'),optimize=True)
