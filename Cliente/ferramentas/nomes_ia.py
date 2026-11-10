# Da a cada bot do servidor uma linha so dele na tabela de IA do cliente (AICarParam, 1a entrada do Init_us.agt)
# com o nome igual ao do servidor. Atualiza: Init_us.agt, Init.agt, Rice/res/BattleAI.json e Rice/res/bots.default.json.
# Uso (na pasta do projeto): python3 Cliente/ferramentas/nomes_ia.py
import sys, os, struct, zlib, json
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import agt
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
CLI = os.path.join(ROOT, 'Cliente')
RES = os.path.join(ROOT, 'Rice', 'res')

def load_ai(path):
    d = bytearray(open(path, 'rb').read())
    off, nch, usize = struct.unpack('<III', agt.xor(bytes(d[32:44]), 32))   # 1a entrada: dados logo depois do diretorio
    ents, _ = agt.parse(bytes(d))
    limit = min(e['off'] for e in ents)                                       # comeco da proxima entrada
    tab = struct.unpack('<%dH' % nch, agt.xor(bytes(d[off:off + 2 * nch]), off))
    p = off + 2 * nch; out = b''
    for n in tab:
        out += zlib.decompress(agt.xor(bytes(d[p:p + n]), p)); p += n
    assert len(out) == usize
    return d, off, limit, out.decode('utf-16').split('\r\n')

def save_ai(path, d, off, limit, lines):
    # as linhas so de tabs no fim (sobra da planilha) nao sao lidas (Count = 220): saem para caber no espaco
    while len(lines) > 2 and lines[-2].strip('\t') == '' and lines[-1] == '':
        lines.pop(-2)
    new = '\r\n'.join(lines).encode('utf-16')
    chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
    tab = struct.pack('<%dH' % len(chunks), *[len(c) for c in chunks])
    size = len(tab) + sum(map(len, chunks))
    print(os.path.basename(path), 'tabela IA', size, 'bytes, espaco', limit - off)
    assert size <= limit - off, 'nao cabe'
    pos = off
    for part in [tab] + chunks:
        d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
    d[32:44] = agt.xor(struct.pack('<III', off, len(chunks), len(new)), 32)
    open(path, 'wb').write(bytes(d))

ai = json.load(open(os.path.join(RES, 'BattleAI.json'), encoding='utf-8-sig'))
bots = json.load(open(os.path.join(RES, 'bots.default.json'), encoding='utf-8-sig'))
bots.append({'Name': 'Joaopvbr', 'Level': 99, 'AiIdx': 218})   # bot fixo do codigo (Bots.cs)
player = set(ai['playerCars']); excluded = {54}
rows = {d['idx']: d for d in ai['drivers']}
special = {'Taxi Driver', 'Patrol Car', 'Road Prowler', 'Deliverer', 'Fogel Bakery', 'Instructor Duffy', 'Elio'}
def usable(d): return d['name'] not in special and any(c in player and c not in excluded for c in d['cars'])

botnames = {b['Name'] for b in bots}
taken = {}                       # linha -> bot
assign = {}
# 1) quem ja tem uma linha com o proprio nome fica com ela
for b in sorted(bots, key=lambda b: b['Name'] != 'Joaopvbr'):
    r = b.get('AiIdx', 0)
    if r in rows and rows[r]['name'] == b['Name'] and r not in taken and usable(rows[r]):
        taken[r] = b['Name']; assign[b['Name']] = r
# 2) os outros pegam a linha livre de nivel mais perto (linha livre = nome que nao e de bot)
for b in sorted(bots, key=lambda b: b['Level']):
    if b['Name'] in assign: continue
    free = [d for d in ai['drivers'] if d['idx'] not in taken and usable(d) and d['name'] not in botnames]
    best = min(free, key=lambda d: (abs(d['level'] - b['Level']), d['idx']))
    taken[best['idx']] = b['Name']; assign[b['Name']] = best['idx']
print(len(assign), 'bots com linha propria;', sum(1 for b in bots if b.get('AiIdx') != assign[b['Name']]), 'mudaram')

# tabela do cliente
for fn in ['Init_us.agt']:   # o Init.agt (coreano) nao tem espaco e o cliente US nao usa
    path = os.path.join(CLI, fn)
    d, off, limit, lines = load_ai(path)
    for i, l in enumerate(lines):
        c = l.split('\t')
        if len(c) > 2 and c[0].isdigit() and int(c[0]) in taken:
            c[1] = taken[int(c[0])]; lines[i] = '\t'.join(c)
    save_ai(path, d, off, limit, lines)
    _, _, _, chk = load_ai(path)
    for r, n in taken.items():
        assert any(x.split('\t')[:2] == [str(r), n] for x in chk), (fn, r, n)

# servidor
for r, n in taken.items(): rows[r]['name'] = n
json.dump(ai, open(os.path.join(RES, 'BattleAI.json'), 'w', encoding='utf-8'), ensure_ascii=False)
bd = json.load(open(os.path.join(RES, 'bots.default.json'), encoding='utf-8-sig'))
for b in bd: b['AiIdx'] = assign[b['Name']]
txt = open(os.path.join(RES, 'bots.default.json'), encoding='utf-8-sig').read()
json.dump(bd, open(os.path.join(RES, 'bots.default.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
for b in sorted(bots, key=lambda b: assign[b['Name']]):
    print('%3d %-14s nv %2d (linha nv %d)%s' % (assign[b['Name']], b['Name'], b['Level'], rows[assign[b['Name']]]['level'],
          '' if b.get('AiIdx') == assign[b['Name']] else '  <- era %s' % b.get('AiIdx')))
