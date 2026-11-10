# Liga o icone de IA (coluna AI da tabela de pistas) em todas as pistas normais das 4 cidades (Moon Palace, Koinonia,
# Cras, Oros): o jogo so deixa colocar IA na sala nas pistas com AI = 1, e so as de Moon Palace tinham.
# Uso (na pasta do projeto): python3 Cliente/ferramentas/pistas_ia.py
#   --original [arquivo.agt]: volta como o jogo original - icone de IA so nas 16 pistas que tem o caminho da IA gravado
#   (Rice/res/BattleAI.json, aiTracks); as outras pistas continuam na lista, sem o icone (nao da para por bot nelas)
import sys, os, struct, zlib, json
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
PATH = os.path.join(ROOT, 'Cliente', 'Init_us.agt')
ORIG = '--original' in sys.argv
extra = [a for a in sys.argv[1:] if not a.startswith('--')]
if extra: PATH = extra[0]
AIT = set(json.load(open(os.path.join(ROOT, 'Rice', 'res', 'BattleAI.json'), encoding='utf-8-sig'))['aiTracks'])
d = bytearray(open(PATH, 'rb').read())
ents, _ = agt.parse(bytes(d))
e = [x for x in ents if x['name'].endswith('OperatorMisrepresent.xlt')][0]
nxt = min([x['off'] for x in ents if x['off'] > e['off']] + [len(d)])
lines = agt.read_file(bytes(d), e)[0].decode('utf-16').split('\r\n')
hdr = lines[2].split('\t'); ia = hdr.index('AI'); ii = hdr.index('Id'); ic = 26; it = 25
ids = []
for k, l in enumerate(lines):
    c = l.split('\t')
    if len(c) <= 30 or not c[2] or not c[ii].isdigit(): continue
    if c[it] != '1' or c[ic] not in ('1', '2', '3', '4'): continue      # so pistas normais (CityType 1) das 4 cidades
    ids.append(int(c[ii]))
    want = '1' if (not ORIG or int(c[ii]) in AIT) else '0'
    if c[ia] != want: c[ia] = want; lines[k] = '\t'.join(c)
new = '\r\n'.join(lines).encode('utf-16')
chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
tab = struct.pack('<%dH' % len(chunks), *[len(x) for x in chunks])
size = len(tab) + sum(map(len, chunks))
print(len(ids) if not ORIG else len([x for x in ids if x in AIT]), 'pistas com IA; tabela', size, 'espaco', nxt - e['off'])
assert size <= nxt - e['off'], 'nao cabe'
pos = e['off']
for part in [tab] + chunks:
    d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
p = e['dirpos'] + 4 + len(e['name'])
d[p:p + 12] = agt.xor(struct.pack('<III', e['off'], len(chunks), len(new)), p)
open(PATH, 'wb').write(bytes(d))
# os desafios dos bots continuam sorteando so as 16 pistas originais com IA (Rice/res/BattleAI.json, aiTracks): nas
# outras a IA pode nao ter o caminho gravado. Para incluir todas: python3 pistas_ia.py --servidor
if '--servidor' in sys.argv:
    bp = os.path.join(ROOT, 'Rice', 'res', 'BattleAI.json')
    ai = json.load(open(bp, encoding='utf-8-sig'))
    ai['aiTracks'] = sorted(set(ai['aiTracks']) | set(ids))
    json.dump(ai, open(bp, 'w', encoding='utf-8'), ensure_ascii=False)
    print('aiTracks:', ai['aiTracks'])
