"""Assemble a local review gallery from preserved design images, without editing them."""
from pathlib import Path
import html
import json

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'source-assets/creatures/eldritch-redesign'


def main():
    data = json.loads((SOURCE / 'designs.json').read_text())
    specs = json.loads((SOURCE / 'material-specs.json').read_text())
    paths = {'DeepOne': 'DeepOne/concept-v1.png', **{s['id']: s['image'] for s in data['species']}}
    roles = ['近战爪击', '直线冲锋', '重锤砸地', '蓄力光束', '水中牵引', '毒区喷吐', '支援治疗', '孵化幼体', '闪现突袭', '环形冲击']
    cards = []
    for i, (spec, role) in enumerate(zip(specs, roles)):
        title = html.escape(spec['name'])
        colors = ''.join(f'<i style="background:{spec[k]}" title="{spec[k]}"></i>' for k in ('base', 'accent', 'soft'))
        approval = '已确认的造型方向' if spec['id'] == 'DeepOne' else '新设定 · 待整套定稿'
        cards.append(f'''<article id="{spec['id']}">
          <div class="top"><span class="number">{i + 1:02}</span><div><h2>{title}</h2><p>{role} · {approval}</p></div><div class="swatches">{colors}</div></div>
          <button class="image" data-src="{paths[spec['id']]}" aria-label="放大{title}设定图"><img src="{paths[spec['id']]}" alt="{title}二维设定：整体、侧面与器官细节" loading="lazy"></button>
          <div class="details"><p><b>外形</b>{html.escape(spec['silhouette'])}</p><p><b>材质</b>{html.escape(spec['surface'])}</p></div>
        </article>''')
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>沉钟镇 · 十种怪物设定</title><style>
    *{box-sizing:border-box}body{margin:0;background:#171a1b;color:#e5dfd4;font-family:-apple-system,BlinkMacSystemFont,"PingFang SC",sans-serif;line-height:1.6}
    header{max-width:1500px;margin:auto;padding:52px 28px 28px}h1{font-size:clamp(26px,3vw,44px);margin:6px 0 14px;letter-spacing:.04em}.eyebrow{color:#a6b4b3;letter-spacing:.16em;font-size:13px}
    header p{max-width:900px;color:#b7bfbd}.status{display:inline-block;border:1px solid #705c46;padding:5px 12px;border-radius:5px;color:#ddbf94;font-size:13px}
    nav{display:flex;flex-wrap:wrap;gap:8px;margin-top:25px}a{color:#c7d3cf}nav a{border:1px solid #3b4544;border-radius:4px;padding:5px 12px;text-decoration:none;font-size:13px}nav a:hover{background:#35413f}
    main{max-width:1500px;margin:auto;padding:10px 28px 50px;display:grid;grid-template-columns:1fr 1fr;gap:24px}article{background:#252a2b;border:1px solid #3c4342;border-radius:9px;overflow:hidden;scroll-margin-top:15px}
    .top{display:flex;align-items:center;gap:14px;padding:18px 20px}.number{font-size:24px;color:#8c9a96;font-variant-numeric:tabular-nums}.top h2{font-size:22px;margin:0}.top p{margin:0;color:#9ea9a5;font-size:12px}.swatches{display:flex;margin-left:auto;gap:5px}.swatches i{width:19px;height:32px;border-radius:3px;border:1px solid #ffffff22}
    .image{display:block;width:100%;padding:0;border:0;cursor:zoom-in;background:#393b3a}.image img{display:block;width:100%;aspect-ratio:1672/940;object-fit:contain}.details{padding:10px 20px 17px}.details p{margin:8px 0;font-size:14px;color:#c7ceca}.details b{display:inline-block;margin-right:14px;color:#8fa09a;font-weight:500}
    footer{max-width:1500px;margin:auto;padding:0 28px 42px;color:#9aa6a0;font-size:13px}dialog{border:1px solid #59615e;padding:0;background:#1c2021;max-width:96vw;max-height:96vh;color:white}dialog::backdrop{background:#000d}dialog img{display:block;max-width:94vw;max-height:86vh;object-fit:contain}dialog button{display:block;margin:10px auto;background:#303d38;border:1px solid #83938b;color:white;padding:8px 22px;border-radius:4px;cursor:pointer}
    @media(max-width:850px){main{grid-template-columns:1fr;padding:10px 14px 30px}header{padding:26px 18px 18px}.top h2{font-size:20px}}
    </style><header><div class="eyebrow">沉钟镇 / 生物造型重设计</div><h1>十种怪物，十种身体与表面</h1><span class="status">二维设定图 · 新模型尚未完成</span>
    <p>保留潮壳猎手已确认的非人生物方向，其余物种分别从轮廓、器官、材质、配色与动作上区分。图片为建模参考，不是 Blender 模型渲染。点击每张图可放大。</p><nav>__NAV__</nav></header><main>__CARDS__</main>
    <footer>制作路径：整套设定定稿 → Blender 造型与关节结构 → 独立材质与纹理 → 动画 → 同角度还原检查 → 游戏资源整理与接入。<br>所有原图和生成记录均保存在本目录。设计中未使用 Tripo 或 Meshy；Blender 材质试样不代表游戏贴图已烘焙。</footer>
    <dialog id="viewer"><img id="large" alt="放大的怪物设定"><button id="close">关闭 / Esc</button></dialog><script>
    const viewer=document.getElementById('viewer'),large=document.getElementById('large');let trigger;
    document.querySelectorAll('[data-src]').forEach(button=>button.addEventListener('click',()=>{trigger=button;large.src=button.dataset.src;large.alt=button.querySelector('img').alt;viewer.showModal()}));
    document.getElementById('close').addEventListener('click',()=>viewer.close());viewer.addEventListener('close',()=>trigger?.focus());viewer.addEventListener('click',event=>{if(event.target===viewer)viewer.close()});
    </script></html>'''
    nav = ''.join(f'<a href="#{s["id"]}">{html.escape(s["name"])}</a>' for s in specs)
    (SOURCE / 'index.html').write_text(page.replace('__NAV__', nav).replace('__CARDS__', '\n'.join(cards)))
    print('Saved ten-species local review gallery.')


if __name__ == '__main__':
    main()
