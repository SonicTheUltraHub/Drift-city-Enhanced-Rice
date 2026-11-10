# Precos da Drift Shop (Init\VShopCoolStuff.xlt do Init_us.agt) e o mesmo no servidor (Rice/res/VisualShop.json).
# Todos "para sempre" (o servidor so vende permanente): Mito Price / Mito Price0D ou $Price 0D (CC).
# uso: python3 vshop_precos.py <Cliente> <Rice/res>
import agt, zlib, struct, sys, json, os

cli, res = sys.argv[1], sys.argv[2]
# UniqueId: (moeda, preco)   moeda 'm' = mito, 'c' = CC
PRECOS = {
    '20508': ('m', 15000), '20509': ('m', 15000),            # Paint S15 / C15
    '20452': ('m', 5000), '20453': ('m', 5000),              # Window Tint S15 / C15
    '20448': ('m', 16000), '20449': ('m', 16000),            # Neon S15 / C15
    '20212': ('m', 1000),                                    # Sticker Glue L1
    '20470': ('c', 2000),                                    # AeroMK2+
    '20468': ('m', 20000), '20469': ('m', 50000),            # Aero / Aero2
    '20309': ('m', 10000), '20505': ('m', 35000),            # SuperBooster B10 / B30+
    '20512': ('m', 15000), '20513': ('m', 30000),            # Tire S5 / SC15
    '20471': ('m', 15000), '20472': ('m', 30000),            # AeroWing S5 / SB15
    '20475': ('m', 25000), '20476': ('m', 40000),            # BoosterWing S5 / SB20
    '20613': ('c', 500), '20614': ('m', 55000),              # Air Duct+ / Air Duct
}

class Pack:
    def __init__(s, path):
        s.path = path; s.d = bytearray(open(path, 'rb').read()); s.ents, s.dirend = agt.parse(bytes(s.d))
    def find(s, test):
        for i, e in enumerate(s.ents):
            raw, _ = agt.read_file(bytes(s.d), e)
            try: t = raw.decode('utf-16')
            except Exception: continue
            if test(t): return i, t.split('\r\n')
        raise Exception('tabela nao achada em ' + s.path)
    def store(s, i, lines):
        e = s.ents[i]
        others = sorted(x['off'] for x in s.ents if x['off'] > e['off'])
        nxt = others[0] if others else len(s.d)
        new = '\r\n'.join(lines).encode('utf-16')
        chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
        tab = struct.pack('<%dH' % len(chunks), *[len(c) for c in chunks])
        size = len(tab) + sum(map(len, chunks))
        pos = e['off'] if size <= nxt - e['off'] else len(s.d)      # nao cabe: vai para o fim do arquivo
        if pos == len(s.d): s.d.extend(b'\0' * size)
        off = pos
        for part in [tab] + chunks:
            s.d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
        p = e['dirpos'] + 4 + len(e['name'])
        s.d[p:p + 12] = agt.xor(struct.pack('<III', off, len(chunks), len(new)), p)
        e['off'] = off
    def save(s): open(s.path, 'wb').write(bytes(s.d))

us = Pack(os.path.join(cli, 'Init_us.agt'))
i, L = us.find(lambda t: 'MarketName\tDesc (Tooltip)' in t and 'Mito Price0D' in t)
h = L[2].split('\t'); ix = {x: k for k, x in reversed(list(enumerate(h)))}
mito_cols = ['Mito Price', 'Mito Price7D', 'Mito Price30D', 'Mito Price90D', 'Mito Price365D', 'Mito Price0D']
cc_cols = ['$Price 7D', '$Price 30D', '$Price 90D', '$Price 365D', '$Price 0D']
done = {}
for li in range(3, len(L)):
    r = L[li].split('\t')
    if len(r) < len(h) - 5 or r[ix['UniqueId']] not in PRECOS: continue
    moeda, p = PRECOS[r[ix['UniqueId']]]
    for c in mito_cols + cc_cols: r[ix[c]] = '0'
    for c in ['Period 7D', 'Period 30D', 'Period 90D', 'Period 365D']: r[ix[c]] = '0'
    r[ix['Period 0D']] = '0'
    if moeda == 'm':
        r[ix['Use Mito']] = '1'; r[ix['UseHancoin']] = '0'; r[ix['Mito Price']] = str(p); r[ix['Mito Price0D']] = str(p)
    else:
        r[ix['Use Mito']] = '0'; r[ix['UseHancoin']] = '1'; r[ix['$Price 0D']] = str(p)
    L[li] = '\t'.join(r); done[r[ix['UniqueId']]] = (r[ix['MarketName']], moeda, p)
us.store(i, L); us.save()
for u, v in sorted(done.items()): print(u, v)
print(len(done), 'de', len(PRECOS))

p = os.path.join(res, 'VisualShop.json')
vs = json.load(open(p, encoding='utf-8-sig'))
for u, (moeda, pr) in PRECOS.items():
    x = vs.get(u)
    if x is None: print('servidor sem', u); continue
    for k in ('mito', 'm7D', 'm30D', 'm90D', 'm365D', 'm0D', 'c7D', 'c30D', 'c90D', 'c365D', 'c0D'): x[k] = 0
    if moeda == 'm': x['useMito'] = 1; x['useCC'] = 0; x['m0D'] = pr; x['mito'] = pr
    else: x['useMito'] = 0; x['useCC'] = 1; x['c0D'] = pr
json.dump(vs, open(p, 'w', encoding='utf-8'), ensure_ascii=False)
