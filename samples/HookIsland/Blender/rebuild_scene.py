"""Run in a new Blender file. Builds the complete island without replacing existing work."""
from pathlib import Path
import importlib.util
import sys
import bpy

ROOT=Path(__file__).resolve().parent


def load(name,filename):
    spec=importlib.util.spec_from_file_location(name,ROOT/filename)
    module=importlib.util.module_from_spec(spec)
    sys.modules[name]=module
    spec.loader.exec_module(module)
    return module


def rebuild():
    if bpy.data.scenes.get("Hook Island | v1"):
        raise RuntimeError("An island scene already exists. Open a new Blender file to rebuild; existing edits are preserved.")
    H=load("hook_island","build_hook_island.py")
    H.init_scene()
    H.build_terrain()
    H.setup_cameras_lighting()
    A=load("hook_island_assets","build_assets.py")
    A.build_shop()
    A.build_palm()
    D=load("hook_island_details","build_details.py")
    D.build_pier()
    D.build_ground_dressing()
    P=load("hook_island_polish","polish_scene.py")
    P.apply()
    bpy.context.view_layer.update()
    V=load("hook_island_verify","verify_scene.py")
    V.verify()
    if H.REFERENCE.exists():
        image=bpy.data.images.load(str(H.REFERENCE),check_existing=True)
        image.pack()
        image.use_fake_user=True
    readme=bpy.data.texts.new("READ ME - Hook Island")
    readme.write((ROOT/"README.md").read_text())
    readme.use_fake_user=True
    H.save_scene()
    print("Complete editable island saved:",ROOT/"hook_island_v1.blend")


if __name__=="__main__":
    rebuild()
