# Precos dos carros da loja por V (faixas do usuario), ordem da loja por V, e o mesmo no servidor.
# uso: python3 precos_loja.py <Cliente> <Rice/res>
import agt, zlib, struct, sys, json, random, os

cli, res = sys.argv[1], sys.argv[2]
RANGES = {1: (1000, 6000), 2: (7500, 19000), 3: (25500, 38000), 4: (45000, 74000), 5: (99000, 180000),
          6: (250000, 340000), 7: (380000, 550000), 8: (700000, 1200000), 9: (1350000, 5300000)}
SPECIAL = (10000000, 105000000)
# especiais: BT e CT mais baratos, depois ST, MINI e Xmas (faixa dividida em 4)
SPTYPES = ['BT', 'ST', 'MINI', 'XMAS']
def sptype(n):
    n = n.strip().upper()
    for t, k in ((' BT', 'BT'), (' CT', 'BT'), (' ST', 'ST'), (' MINI', 'MINI'), (' XMAS', 'XMAS')):
        if n.endswith(t): return k
    return None
def spband(k):
    lo, hi = SPECIAL; w = (hi - lo) / 4.0; i = SPTYPES.index(k)
    return int(lo + w * i), int(lo + w * (i + 1))
COUPON_DISCOUNT = 0.30   # carro que exige cupom: 30% mais barato
STEP = {1: 100, 2: 500, 3: 500, 4: 500, 5: 1000, 6: 5000, 7: 5000, 8: 10000, 9: 50000}
SKIP = {'301'}   # ACROBAT: nao mexe

# quanto o carro de verdade e raro/caro (primeiro que bater no nome real)
LUX = [
    ('bugatti', 1), ('koenigsegg', 1), ('pagani', 1), ('egoista', 1), ('elemento', 1), ('reventon', .95),
    ('venero', 1), ('laferrari', 1), ('enzo', 1), ('f50', .95), ('250 testarossa', 1), ('f2003', .95),
    ('60f1', .95), ('mclaren f1', 1), ('p1 concept', .95), ('exelero', 1), ('lykan', 1), ('venom', .95),
    ('tuatara', .95), ('dartz', .9), ('918', .95), ('carrera gt', .9), ('slr', .9), ('clk gtr', .95),
    ('c-x75', .9), ('furai', .85), ('962c', .9), ('rolls', .9), ('maybach', .85), ('bentley', .8),
    ('aston', .8), ('lamborghini', .85), ('ferrari', .85), ('lfa', .85), ('gt40', .8), ('ac shelby', .8),
    ('2000gt', .8), ('8c2300', .85), ('i8', .75), ('r8', .75), ('gt3', .7), ('carrera gtr', .75),
    ('ruf', .75), ('batcar', .7), ('spirra', .7), ('gt-r', .65), ('gtr35', .65), ('viper', .65),
    ('corvette', .6), ('sls', .7), ('maserat', .65), ('panamera', .6), ('cayenne', .5), ('cayman', .5),
    ('m6', .6), ('bmw m1', .7), ('z8', .65), ('sl55', .55), ('shelby', .6), ('mustang', .55),
    ('nsx', .55), ('supra', .5), ('skyline gtr', .55), ('evora', .5), ('essence', .6), ('760li', .6),
    ('s8', .55), ('lotus', .45), ('tvr', .45), ('jaguar', .45), ('lexus', .45), ('mercedes', .45),
    ('benz', .45), ('bmw', .45), ('audi', .4), ('porsche', .6), ('camaro', .4), ('challenger', .45),
    ('charger', .4), ('hummer', .4), ('landrover', .4), ('peterbilt', .45), ('raptor', .4),
    ('lancer', .35), ('wrx', .35), ('impreza', .3), ('rx-7', .4), ('rx7', .35), ('rx8', .3), ('fairlady', .35),
    ('370z', .4), ('silvia', .3), ('skyline', .4), ('s2000', .35), ('celica', .25), ('ae-86', .25),
    ('hyundai', .1), ('kia', .1), ('daewoo', .05), ('damas', .02), ('daihatsu', .05), ('suzuki', .05),
    ('smart', .08), ('fiat', .1), ('avanza', .05), ('cube', .08), ('march', .05), ('peugeot', .12),
    ('opel', .08), ('clio', .2), ('vw', .15), ('volkswagen', .12), ('mazda2', .05), ('civic', .12),
    ('ssang', .08), ('kubel', .15), ('willys', .2), ('mini', .2), ('click', .05)]
def lux(real):
    n = real.lower()
    for k, v in LUX:
        if k in n: return v
    return .3

def special(n):
    n = n.strip().upper()
    return any(n.endswith(x) for x in (' ST', ' BT', ' XMAS', ' MINI', ' CT'))

class Pack:
    def __init__(s, path):
        s.path = path; s.d = bytearray(open(path, 'rb').read()); s.ents, _ = agt.parse(bytes(s.d))
    def find(s, test):
        for i, e in enumerate(s.ents):
            raw, _ = agt.read_file(bytes(s.d), e)
            try: t = raw.decode('utf-16')
            except Exception: continue
            if test(t): return i, t.split('\r\n')
        raise Exception('tabela nao achada em ' + s.path)
    def store(s, i, lines):
        e = s.ents[i]; nxt = s.ents[i + 1]['off'] if i + 1 < len(s.ents) else len(s.d)
        new = '\r\n'.join(lines).encode('utf-16')
        chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
        tab = struct.pack('<%dH' % len(chunks), *[len(c) for c in chunks])
        size = len(tab) + sum(map(len, chunks))
        assert size <= nxt - e['off'], ('nao cabe', e['name'], size, nxt - e['off'])
        pos = e['off']
        for part in [tab] + chunks:
            s.d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
        p = e['dirpos'] + 4 + len(e['name'])
        s.d[p:p + 12] = agt.xor(struct.pack('<III', e['off'], len(chunks), len(new)), p)
    def save(s): open(s.path, 'wb').write(bytes(s.d))

is_vd = lambda t: '\tUnique Id\tSellable\t' in t and 'PlayerCar' in t
is_vl = lambda t: 'Game UI name' in t and '\tUpgradeMito\t' in t
is_cg = lambda t: '\tUnique Id\tNew\tHot\tGM\tCategory' in t

us = Pack(os.path.join(cli, 'Init_us.agt'))
ivd, vd = us.find(is_vd); ivl, vl = us.find(is_vl); icg, cg = us.find(is_cg)
h = vd[3].split('\t'); ix = {}
for i, x in enumerate(h): ix.setdefault(x, i)
D = {}
for li in range(4, len(vd)):
    r = vd[li].split('\t')
    if len(r) > 30 and r[ix['Desc']] == 'PlayerCar' and r[ix['Unique Id']] not in D: D[r[ix['Unique Id']]] = li
row = lambda u: vd[D[u]].split('\t')
shop = [l.split('\t')[3] for l in cg[4:] if l.strip()]

# soma de atributos no V de venda (para ordenar dentro do V)
sums = {}
for l in vl[3:]:
    r = l.split('\t')
    if len(r) > 19 and r[1].strip().isdigit(): sums[(r[1].strip(), r[19].strip())] = int(r[17].strip() or 0)

groups = {}
for u in shop:
    r = row(u); lv = int(r[ix['Lvl']]); sp = special(r[ix['UI Name']])
    groups.setdefault(('E', sptype(r[ix['UI Name']])) if sp else lv, []).append(u)

price = {}
coupon_cars = set(str(c['car']) for c in json.load(open(os.path.join(res, 'Coupons.json'), encoding='utf-8-sig')) if c.get('item'))
STEP_C = lambda p: 100 if p < 10000 else 500 if p < 100000 else 5000 if p < 1000000 else 50000
rng = random.Random(2016)
for g, ids in groups.items():
    esp = isinstance(g, tuple)
    lo, hi = spband(g[1]) if esp else RANGES[g]
    step = 250000 if esp else STEP[g]
    ranked = sorted(ids, key=lambda u: sums.get((u, row(u)[ix['Lvl']]), 0))
    score = {}
    for k, u in enumerate(ranked):
        st = k / max(1, len(ranked) - 1)
        score[u] = .55 * lux(row(u)[ix['Car Name']]) + .30 * st + .15 * rng.random()
    # usa a faixa inteira: o de maior nota vai perto do maximo, o de menor perto do minimo
    byscore = sorted([u for u in ids if u not in SKIP], key=lambda u: score[u])
    for k, u in enumerate(byscore):
        pct = k / max(1, len(byscore) - 1)
        p = lo + (hi - lo) * pct
        p = int(min(hi, max(lo, round(p / step) * step)))
        if u in coupon_cars: p = int(round(p * (1 - COUPON_DISCOUNT) / STEP_C(p)) * STEP_C(p))
        price[u] = p

# ordem da loja: normais V1..V9 (ordem atual dentro do V), depois os especiais na ordem atual
disp = lambda u: int(row(u)[ix['Display']])
normal = sorted([u for u in shop if not special(row(u)[ix['UI Name']])], key=lambda u: (disp(u) < 0, int(row(u)[ix['Lvl']]), disp(u)))
espec = sorted([u for u in shop if special(row(u)[ix['UI Name']])], key=disp)
order = [u for u in normal if disp(u) >= 0] + espec
newdisp = {u: i + 1 for i, u in enumerate(order)}
moved = [(row(u)[ix['UI Name']], disp(u), newdisp[u]) for u in order if disp(u) != newdisp[u]]

def apply(pack, ivd, vd, ivl, vl, label):
    h = vd[3].split('\t'); jx = {}
    for i, x in enumerate(h): jx.setdefault(x, i)
    lv = {}
    for li in range(4, len(vd)):
        r = vd[li].split('\t')
        if len(r) > 30 and r[jx['Desc']] == 'PlayerCar':
            u = r[jx['Unique Id']]
            if u in newdisp and u not in lv:
                r[jx['Display']] = str(newdisp[u]); vd[li] = '\t'.join(r)
            lv.setdefault(u, r[jx['Lvl']])
    n = 0
    for li in range(3, len(vl)):
        r = vl[li].split('\t')
        # numeros com espaco no fim ("30000 "): o jogo le igual sem o espaco; libera lugar no arquivo
        r = [x.strip() if x.strip().replace('.', '', 1).isdigit() else x for x in r]
        vl[li] = '\t'.join(r)
        if len(r) > 19 and r[1].strip() in price and r[19].strip() == lv.get(r[1].strip()):
            p = price[r[1].strip()]; r[10] = str(p); r[11] = str(p // 2); vl[li] = '\t'.join(r); n += 1
    pack.store(ivd, vd); pack.store(ivl, vl); pack.save()
    print(label, 'precos', n, 'de', len(price))

apply(us, ivd, vd, ivl, vl, 'Init_us.agt')
da = Pack(os.path.join(cli, 'Init.agt'))
a, b = da.find(is_vd); c, e = da.find(is_vl)
apply(da, a, b, c, e, 'Init.agt')

# servidor
up = json.load(open(os.path.join(res, 'VehicleUpgrade.json'), encoding='utf-8-sig'))
vlj = json.load(open(os.path.join(res, 'VehicleList.json'), encoding='utf-8-sig'))
n = 0
for u, p in price.items():
    g = int(vlj[u]['lvl']); ent = up[u][g - 1]
    ent['price'] = str(p); ent['sell'] = str(p // 2); n += 1
json.dump(up, open(os.path.join(res, 'VehicleUpgrade.json'), 'w', encoding='utf-8'), ensure_ascii=False)
sc = [dict(pos=i + 1, id=int(u), name=row(u)[ix['UI Name']], v=int(row(u)[ix['Lvl']])) for i, u in enumerate(order)]
json.dump(sc, open(os.path.join(res, 'ShopCars.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print('servidor', n)

# lista para o usuario
out = ['# Precos dos carros da loja', '', 'Gerado por `ferramentas/precos_loja.py`. Venda do carro = metade do preco. Carro de cupom = 30% mais barato.', '']
TITLES = {'BT': 'Especial BT e CT', 'ST': 'Especial ST', 'MINI': 'Especial MINI', 'XMAS': 'Especial Xmas'}
for g in list(range(1, 10)) + SPTYPES:
    key = lambda u: sptype(row(u)[ix['UI Name']]) if special(row(u)[ix['UI Name']]) else int(row(u)[ix['Lvl']])
    ids = [u for u in order if key(u) == g]
    out += ['## ' + (TITLES[g] if g in TITLES else 'V%d' % g), '', '| Carro | Carro real | Preco |', '|---|---|---|']
    for u in ids:
        r = row(u)
        out.append('| %s | %s | %s |' % (r[ix['UI Name']].strip(), r[ix['Car Name']].strip(),
                   'nao mexido' if u not in price else '{:,}'.format(price[u]).replace(',', '.') + (' (cupom, -30%)' if u in coupon_cars else '')))
    out.append('')
open(os.path.join(cli, 'precos_carros.md'), 'w', encoding='utf-8').write('\n'.join(out))
print('reordenados', len(moved)); [print(' ', m) for m in moved if abs(m[1] - m[2]) > 20]
