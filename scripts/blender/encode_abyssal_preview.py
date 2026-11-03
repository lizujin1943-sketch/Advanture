"""将真实 Blender 渲染序列编码成视频，并完整解码一次确认文件可播放。用系统 Python 执行。"""
from pathlib import Path
import json
import math
import re
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/abyssal-watcher'
MANIFEST=ROOT/'source-assets/creatures/abyssal-watcher/exports/animation-manifest.json'


def main():
    expected=math.ceil((json.loads(MANIFEST.read_text())['showcase']['frame_end']-1)/2)
    frames=OUT/'animation-frames'
    assert all((frames/f'{i:04d}.png').is_file() for i in range(expected)),'动画帧尚未全部渲染'
    encoder=shutil.which('ffmpeg')
    if not encoder:
        sys.path.insert(0,str(OUT/'python-tools'))
        import imageio_ffmpeg
        encoder=imageio_ffmpeg.get_ffmpeg_exe()
    video=OUT/'animation-preview.mp4'
    result=subprocess.run([encoder,'-hide_banner','-y','-framerate','15','-i',str(frames/'%04d.png'),
        '-frames:v',str(expected),'-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p',
        '-movflags','+faststart',str(video)],capture_output=True,text=True)
    (OUT/'encode-animation.log').write_text(result.stderr);result.check_returncode()
    decoded=subprocess.run([encoder,'-hide_banner','-i',str(video),'-f','null','-'],capture_output=True,text=True)
    (OUT/'decode-animation.log').write_text(decoded.stderr);decoded.check_returncode()
    counts=re.findall(r'frame=\s*(\d+)',decoded.stderr)
    assert counts and int(counts[-1])==expected,('视频解码帧数不符',counts[-1:] if counts else [])
    report={'file':str(video),'decode':'PASS','frames':expected,'fps':15,'duration_seconds':expected/15,
            'resolution':[960,640],'encoding':'H.264 / yuv420p','bytes':video.stat().st_size}
    (OUT/'video-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False))


if __name__=='__main__':main()
