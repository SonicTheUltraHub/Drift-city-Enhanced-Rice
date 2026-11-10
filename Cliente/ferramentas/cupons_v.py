# Coupon Book: cada carro na linha do V dele. O livro mostrava Monster (V5) na linha V4, Impact (V5) na V6 e Compact (V5)
# na V8: a linha vem da coluna "car grade" da tabela de cupons (no Init_us.agt ela esta com o nome Init\compound.xlt).
# Pedido do Joao: Monster, Impact e Compact na linha V5; FondoX vira V6 (carro e linha); o Compact (sem pontos no jogo)
# com durabilidade e boost 20 pontos acima de velocidade e aceleracao, na soma de pontos de cada V.
# Muda a tabela de cupons, VehicleDisplay.xlt (V do carro, nivel pedido e pontos mostrados) e VehicleList.xlt (linhas de
# cada V: pontos, nivel pedido e o preco do cupom, que fica na linha do V inicial). Tambem poe o FondoX entre os V6 na loja.
# Uso (na pasta do projeto, com o Init_us.agt antes desta mudanca): python3 Cliente/ferramentas/cupons_v.py
import sys, os, struct, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
PATH = os.path.join(HERE, '..', 'Init_us.agt')
TIER = {1: 1, 2: 10, 3: 20, 4: 30, 5: 40, 6: 50, 7: 60, 8: 75, 9: 80}
SUMS = {1: 160, 2: 190, 3: 230, 4: 290, 5: 360, 6: 440, 7: 540, 8: 660, 9: 800}
NEW = {'84': 6}                                      # FondoX: o carro vira V6
ROWS = {'83': 5, '11': 5, '247': 5, '84': 6}         # linha do livro: Monster, Impact, Compact (V5) e FondoX (V6)
COMPACT = '247'
AFTER = {'84': '39'}                                 # loja de carros: FondoX logo depois do Fondo (V6), nao no meio dos V7
d = bytearray(open(PATH, 'rb').read())
ents, _ = agt.parse(bytes(d))

def compact_stats(v):
    x = (SUMS[v] - 40) // 4
    sp = ac = x
    du = bo = x + 20
    bo += SUMS[v] - (sp + ac + du + bo)                 # o que sobra da divisao vai no boost
    return sp, ac, du, bo

def fix_list(lines):
    # Idx ID CarName ShortName GameUIName Coupon Accel Speed Crash Boost Price Sell CloseSell UpgradeMito Eff Cap ReqLevel Sum GradeType GradeLvl
    rows = {}
    for k, l in enumerate(lines):
        c = l.split('\t')
        if len(c) > 20 and (c[1] in NEW or c[1] == COMPACT) and c[19].isdigit(): rows.setdefault(c[1], {})[int(c[19])] = k
    n = 0
    for cid, newv in list(NEW.items()) + [(COMPACT, None)]:
        r = rows.get(cid, {})
        old = [v for v, k in r.items() if lines[k].split('\t')[16] not in ('1', '')]
        oldv = old[0] if old else None
        if cid == COMPACT:
            for v, k in r.items():
                c = lines[k].split('\t')
                sp, ac, du, bo = compact_stats(v)
                c[6], c[7], c[8], c[9], c[17] = str(ac), str(sp), str(du), str(bo), str(SUMS[v])
                lines[k] = '\t'.join(c); n += 1
            if newv is None: continue
        if newv not in r: print('sem linha V%d para %s' % (newv, cid)); continue
        cn = lines[r[newv]].split('\t')
        if oldv is not None and oldv != newv:
            co = lines[r[oldv]].split('\t')
            cn[10], co[10] = co[10], cn[10]                   # preco do cupom vai para o V novo
            cn[11], co[11] = co[11], cn[11]
            co[16] = '1'
            lines[r[oldv]] = '\t'.join(co)
        elif oldv is None and cid == '11':
            cn[10], cn[11] = '80000', '40000'                 # Impact: a linha V5 (com o preco) nao existe no cliente
        cn[16] = str(TIER[newv])
        lines[r[newv]] = '\t'.join(cn); n += 1
    return n

def fix_display(lines):
    hdr = [l for l in lines[:6] if l.startswith('Index\t')][0].split('\t')
    col = {h: hdr.index(h) for h in ('Unique Id', 'Accel', 'Speed', 'Crash', 'Boost', 'Req Level', 'Lvl')}
    lst = table('VehicleList.xlt')
    n = 0
    for k, l in enumerate(lines):
        c = l.split('\t')
        if len(c) <= col['Lvl'] or (c[col['Unique Id']] not in NEW and c[col['Unique Id']] != COMPACT): continue
        cid = c[col['Unique Id']]; v = NEW.get(cid, int(c[col['Lvl']]))
        row = [x.split('\t') for x in lst if len(x.split('\t')) > 20 and x.split('\t')[1] == cid and x.split('\t')[19] == str(v)][0]
        c[col['Lvl']] = str(v); c[col['Req Level']] = str(TIER[v])
        c[col['Accel']], c[col['Speed']], c[col['Crash']], c[col['Boost']] = row[6], row[7], row[8], row[9]
        lines[k] = '\t'.join(c); n += 1
    return n

def fix_order(lines):
    # coluna Display = posicao na loja de carros (V5 72-91, V6 93-114, V7 115-137). Tira o carro da posicao antiga, poe logo
    # depois do carro de referencia e anda uma casa os que ficam no meio, sem repetir numero.
    hdr = [l for l in lines[:6] if l.startswith('Index\t')][0].split('\t')
    ui, dp = hdr.index('Unique Id'), hdr.index('Display')
    rows = [k for k, l in enumerate(lines) if len(l.split('\t')) > dp and l.split('\t')[dp].isdigit() and not l.startswith('Index')]
    def get(k): return lines[k].split('\t')
    def put(k, v):
        c = get(k); c[dp] = str(v); lines[k] = '\t'.join(c)
    n = 0
    for cid, ref in AFTER.items():
        me = [k for k in rows if get(k)[ui] == cid][0]; old = int(get(me)[dp])
        new = int(get([k for k in rows if get(k)[ui] == ref][0])[dp]) + 1
        if new == old: continue
        for k in rows:
            v = int(get(k)[dp])
            if k == me: continue
            if new <= v < old: put(k, v + 1); n += 1
            elif old < v < new: put(k, v - 1); n += 1
        put(me, new if new < old else new - 1); n += 1
    return n

def entry(tname): return [x for x in ents if x['name'].endswith(tname)][0]
def table(tname):
    raw, _ = agt.read_file(bytes(d), entry(tname))
    return raw.decode('utf-16').split('\r\n')
def write(tname, lines):
    e = entry(tname)
    nxt = min([x['off'] for x in ents if x['off'] > e['off']] + [len(d)])
    new = '\r\n'.join(lines).encode('utf-16')
    chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
    tab = struct.pack('<%dH' % len(chunks), *[len(x) for x in chunks])
    size = len(tab) + sum(map(len, chunks))
    assert size <= nxt - e['off'], '%s nao cabe (%d > %d)' % (tname, size, nxt - e['off'])
    pos = e['off']
    for part in [tab] + chunks:
        d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
    p = e['dirpos'] + 4 + len(e['name'])
    d[p:p + 12] = agt.xor(struct.pack('<III', e['off'], len(chunks), len(new)), p)
    e['nch'], e['usize'] = len(chunks), len(new)
    print(tname, 'ok', size, 'de', nxt - e['off'])

def fix_coupons(lines):
    # colunas: tipo, indice, mostra, vezes, minutos, nome, carro, item do cupom, V da linha, DB tipo, DB indice...
    n = 0
    for k, l in enumerate(lines):
        c = l.split('\t')
        if len(c) > 10 and c[6] in ROWS and c[7].startswith('i_n_c'):
            c[8] = str(ROWS[c[6]]); lines[k] = '\t'.join(c); n += 1
    return n

C = table('compound.xlt'); print('Cupons:', fix_coupons(C), 'linhas'); write('compound.xlt', C)
L = table('VehicleList.xlt'); print('VehicleList:', fix_list(L), 'linhas'); write('VehicleList.xlt', L)
D = table('VehicleDisplay.xlt'); print('VehicleDisplay:', fix_display(D), 'linhas'); print('Ordem da loja:', fix_order(D), 'linhas'); write('VehicleDisplay.xlt', D)
open(PATH, 'wb').write(bytes(d))
