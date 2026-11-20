from pathlib import Path
root=Path(__file__).resolve().parents[1]
parts=['<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1160" viewBox="0 0 1600 1160"><style>text{font-family:Arial;fill:#273441} .mass{fill:#d9e0e4;stroke:#667887;stroke-width:2}.wall{fill:none;stroke:#667887;stroke-width:8}.infer{fill:none;stroke:#bd8356;stroke-dasharray:6 5;stroke-width:2}</style><rect width="1600" height="1160" fill="#f6f4ef"/><text x="40" y="45" font-size="28">FORTIFIED COMPOUND — MULTIVIEW STRUCTURAL SPECIFICATION</text><text x="40" y="78" font-size="17">Primary image: target.png | meters inferred | dashed amber: inferred hidden structures | before 3D construction</text>']
pts=[(-32,-55),(32,-55),(38,-20),(36,25),(30,54),(-30,54),(-36,25),(-38,-20)]
def p(x,y):return 340+x*5.2,410-y*5.2
parts+=['<text x="45" y="120" font-size="22">TOP / north up</text>','<path class="wall" d="M '+' L '.join(f'{p(x,y)[0]} {p(x,y)[1]}' for x,y in pts)+' Z"/>']
def rect(x,y,w,d,label):
 u,v=p(x-w/2,y+d/2); parts.append(f'<rect class="mass" x="{u}" y="{v}" width="{w*5.2}" height="{d*5.2}"/><text x="{u+6}" y="{v+20}" font-size="14">{label}</text>')
rect(-3,49,25,9,'A-1');rect(-5,7,30,34,'OPEN PLAZA');rect(-4,-48,21,12,'C-1');rect(21,14,14,12,'B-1');rect(-27,-4,11,12,'ARMORY');rect(-16,34,12,12,'EQUIP');rect(10,34,11,12,'MEDICAL');rect(-28,20,9,8,'SHEDS')
u,v=p(21,-22);parts.append(f'<circle class="mass" cx="{u}" cy="{v}" r="43"/><text x="{u-16}" y="{v+5}">B-2</text>')
for x,y in pts+[(-8,54),(2,54)]:
 u,v=p(x,y);parts.append(f'<circle class="mass" cx="{u}" cy="{v}" r="12"/>')
for x,y in [(-21,54),(37,5),(-23,-55)]:
 u,v=p(x,y);parts.append(f'<circle cx="{u}" cy="{v}" r="17" fill="#f6f4ef" stroke="#bb5a42" stroke-width="3"/><text x="{u+20}" y="{v}" font-size="12">BREACH</text>')
parts.append('<text x="700" y="120" font-size="22">FRONT / south — target camera geometry</text>')
parts.append('<rect class="mass" x="720" y="240" width="760" height="80"/>')
for x in [740,1425]:parts.append(f'<rect class="mass" x="{x}" y="185" width="45" height="135"/><rect class="mass" x="{x-10}" y="168" width="65" height="25"/>')
parts.append('<rect class="mass" x="1000" y="175" width="220" height="145"/><rect x="1070" y="220" width="74" height="100" fill="#858c7e"/><rect x="810" y="235" width="155" height="90" fill="#f6f4ef"/><text x="1040" y="160">C-1 / right wall continuous</text>')
parts.append('<text x="700" y="410" font-size="22">SIDE / east — depths and access inferred from viewer</text><rect class="mass" x="720" y="520" width="760" height="80"/>')
for x in [730,950,1230,1430]:parts.append(f'<rect class="mass" x="{x}" y="465" width="42" height="135"/><rect class="mass" x="{x-10}" y="449" width="62" height="24"/>')
parts.append('<rect x="1080" y="510" width="95" height="95" fill="#f6f4ef"/><path class="infer" d="M 780 520 L 780 492 L 1020 492 L 1020 600 M 1180 600 L 1180 545 L 1370 545 L 1370 600"/>')
parts.append('<text x="700" y="690" font-size="22">BACK / north — hidden surfaces are structural inference</text><rect class="mass" x="720" y="815" width="760" height="80"/>')
for x in [740,1040,1190,1430]:parts.append(f'<rect class="mass" x="{x}" y="760" width="42" height="135"/><rect class="mass" x="{x-10}" y="744" width="62" height="24"/>')
parts.append('<rect x="1305" y="810" width="100" height="90" fill="#f6f4ef"/><rect class="infer" x="920" y="800" width="330" height="95"/><text x="45" y="770" font-size="18">ACCESS CHECKS</text>')
for i,t in enumerate(['A-1: single story, straight stair to roof','B-1: clear upper landing, open parapet','C-1: turning stair entirely inside perimeter','Plaza: west ramp + south/east stairs','Three breaches: irregular collapse, no formal roads','Ten towers: connected perimeter, octagonal gallery','Depots: coherent concrete shells, restrained props']):parts.append(f'<text x="45" y="805" dy="{i*28}" font-size="16">{t}</text>')
parts.append('<text x="45" y="1090" font-size="16">No web mesh download. No hidden details claimed as observed. Final acceptance uses actual Blender multiview renders.</text></svg>')
(root/'ref/multiview-spec.svg').write_text('\n'.join(parts))
