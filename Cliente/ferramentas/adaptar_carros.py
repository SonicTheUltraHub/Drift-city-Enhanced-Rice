# Arruma os carros e a loja de QUALQUER cliente do Drift City com base nas tabelas de referencia (carros_*.ref, geradas
# por carros_referencia.py a partir do nosso Init_us.agt). E o mesmo algoritmo do instalador (Instalador\Carros.cs):
#  1. acha a tabela ATIVA de cada coisa: Data_US\Init_us.agt (se tiver a tabela) senao Data\Init.agt;
#  2. le os nomes dos modelos de carro (vehicle\player\pc_XXXX...) em todos os pacotes (Data, Data_US, Patch);
#  3. VehicleDisplay: para os carros que o cliente tem, copia V (Lvl), nivel pedido, condicao (Req Cond: EVENT trava o
#     carro; na referencia todos N/A = liberados, so o nivel e o cupom travam), pontos, Sellable e GradeType (e liga
#     o carro, Support 1, se a referencia usa e o modelo existe);
#  4. VehicleList: copia pontos, preco, venda, nivel pedido... de cada V (mesmo carro e mesmo V);
#  5. loja: todos os carros da referencia que o cliente tem (linha no VehicleDisplay com Support 1 e modelo), na ordem
#     da referencia (V1..V9 e os especiais depois do V9); carros que so o cliente tem vao no fim do V deles (especiais no
#     fim de tudo); posicao na loja (Display) = 1..N;
#  6. grava so o que mudou (o pacote cresce no fim se a tabela nova nao couber no lugar).
# Uso: python3 adaptar_carros.py <pasta do jogo> [--testar]   (--testar: so mostra o que faria)
import sys, os, struct, zlib, re, json
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
REF = os.path.join(HERE, '..', '..', 'Instalador', 'modificacoes')
VD_COPY = ['Lvl', 'Req Level', 'Req Cond', 'MakeType', 'closestage', 'Accel', 'Speed', 'Crash', 'Boost', 'Sellable', 'GradeType', 'Display']
VL_COPY = ['Coupon', 'Accel', 'Speed', 'Crash', 'Boost', 'Price', 'Sell', 'CloseSell', 'UpgradeMito', 'Efficiency',
           'Capacity', 'Req Level', 'Sum', 'GradeType']

def special(n):
    n = n.strip().upper()
    return any(n.endswith(x) for x in (' ST', ' BT', ' XMAS', ' MINI', ' CT'))

class Table:
    # linhas de uma tabela .xlt; hdr = linha do cabecalho; col = nome -> primeira coluna com esse nome
    def __init__(s, text, kind):
        s.lines = text.split('\r\n'); s.kind = kind
        test = (lambda l: l.startswith('Index\t') and '\tUnique Id\t' in l) if kind != 'vl' else (lambda l: l.startswith('Idx\t'))
        s.hdr = [i for i, l in enumerate(s.lines[:8]) if test(l)][0]
        s.col = {}
        for i, x in enumerate(s.lines[s.hdr].split('\t')): s.col.setdefault(x.strip(), i)
    def rows(s):
        for i in range(s.hdr + 1, len(s.lines)):
            c = s.lines[i].split('\t')
            if len(c) > 2: yield i, c
    def text(s): return '\r\n'.join(s.lines)

def is_vd(t): return '\tUnique Id\tSellable\t' in t and 'PlayerCar' in t
def is_vl(t): return 'Game UI name' in t and '\tUpgradeMito\t' in t
def is_cg(t): return '\tUnique Id\tNew\tHot\tGM\tCategory' in t
def is_cp(t): return 'CouponStampDisaply' in t and '\ti_n_c' in t

def cp_key(c): return (c[6].strip(), c[7].strip()) if len(c) > 8 and c[7].strip().startswith('i_n_c') else None

class Pack:
    def __init__(s, path):
        s.path = path; s.d = bytearray(open(path, 'rb').read()); s.ents = []
        if s.d[:8] == b'NayaPack': s.ents, _ = agt.parse(bytes(s.d))
        s.changed = False
    def find(s, test):
        for i, e in enumerate(s.ents):
            if e['usize'] < 200 or e['usize'] > 8000000: continue
            try: t = agt.read_file(bytes(s.d), e)[0].decode('utf-16')
            except Exception: continue
            if test(t): return i, t
        return None, None
    def store(s, i, text):
        e = s.ents[i]
        new = text.encode('utf-16')
        chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
        tab = struct.pack('<%dH' % len(chunks), *[len(c) for c in chunks])
        size = len(tab) + sum(map(len, chunks))
        nxt = min([x['off'] for x in s.ents if x['off'] > e['off']] + [len(s.d)])
        off = e['off'] if size <= nxt - e['off'] else len(s.d)       # nao cabe: vai para o fim do pacote
        if off == len(s.d): s.d.extend(b'\0' * size)
        pos = off
        for part in [tab] + chunks:
            s.d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
        p = e['dirpos'] + 4 + len(e['name'])
        s.d[p:p + 12] = agt.xor(struct.pack('<III', off, len(chunks), len(new)), p)
        e['off'], e['nch'], e['usize'] = off, len(chunks), len(new)
        s.changed = True
    def save(s): open(s.path, 'wb').write(bytes(s.d))

def pack_names(path):
    # so o diretorio do pacote (nomes), sem ler os dados
    with open(path, 'rb') as f: d = f.read(16 * 1024 * 1024)
    if d[:8] != b'NayaPack': return []
    count = struct.unpack_from('<I', d, 16)[0]; p = 44; out = []
    for _ in range(count):
        if p + 4 > len(d): break
        L = struct.unpack('<I', agt.xor(d[p:p + 4], p))[0]
        if L <= 0 or L > 512 or p + 16 + L > len(d): break
        out.append(agt.xor(d[p + 4:p + 4 + L], p + 4).decode('latin-1')); p += 4 + L + 12
    return out

def models(game):
    found = set()
    for sub in ('Data', 'Data_US', 'Patch'):
        folder = os.path.join(game, sub)
        if not os.path.isdir(folder): continue
        for fn in os.listdir(folder):
            full = os.path.join(folder, fn)
            if not os.path.isfile(full) or os.path.getsize(full) < 64: continue
            for n in pack_names(full):
                m = re.search(r'(?:^|\\)vehicle\\player\\(pc_[0-9]+[a-z]*)[_.]', n, re.I)   # Patch: "Data\vehicle\..."
                if m: found.add(m.group(1).lower())
    return found

def adapt(game, test=False, log=print):
    ref = {k: Table(open(os.path.join(REF, 'carros_%s.ref' % n), encoding='utf-8', newline='').read(), k)
           for k, n in (('vd', 'VehicleDisplay'), ('vl', 'VehicleList'), ('cg', 'Loja'))}
    packs = [Pack(p) for p in (os.path.join(game, 'Data_US', 'Init_us.agt'), os.path.join(game, 'Data', 'Init.agt')) if os.path.isfile(p)]
    act = {}
    for k, f in (('vd', is_vd), ('vl', is_vl), ('cg', is_cg)):
        for pk in packs:
            i, t = pk.find(f)
            if i is not None: act[k] = (pk, i, Table(t, k)); break
    if len(act) < 3: log('carros: tabelas nao encontradas no cliente'); return None
    vd, vl, cg = act['vd'][2], act['vl'][2], act['cg'][2]
    # cupons de carro (Coupon Book): linha do mesmo carro e mesmo cupom = a da referencia (vezes, linha do V...)
    ncp = 0; cpi = None
    for pk in packs:
        i, t = pk.find(is_cp)
        if i is not None: cpi = (pk, i, t.split('\r\n')); break
    if cpi:
        rcp = {}
        for l in open(os.path.join(REF, 'carros_Cupons.ref'), encoding='utf-8', newline='').read().split('\r\n'):
            k = cp_key(l.split('\t'))
            if k: rcp.setdefault(k, l)
        lines = cpi[2]
        for j, l in enumerate(lines):
            k = cp_key(l.split('\t'))
            if k in rcp and rcp[k] != l: lines[j] = rcp[k]; ncp += 1
    have = models(game)
    log('carros: %d modelos de carro nos pacotes do jogo' % len(have))
    # trava: busca de modelos que "nao acha" muitos carros da referencia falhou -> nao tira carro da loja por modelo
    if have:
        rc = [c for i, c in ref['vd'].rows() if len(c) > ref['vd'].col['File Name'] and c[ref['vd'].col['Desc']] == 'PlayerCar'
              and c[ref['vd'].col['Support']].strip() == '1' and c[ref['vd'].col['File Name']].strip().lower().startswith('pc_')]
        found = sum(1 for c in rc if c[ref['vd'].col['File Name']].strip().lower().replace('.nif', '') in have)
        if rc and found < len(rc) * 0.7: log('carros: busca de modelos falhou (%d de %d); nenhum carro sai da loja' % (found, len(rc))); have = set()

    # ---- VehicleDisplay
    rv = {}
    for i, c in ref['vd'].rows():
        if len(c) > ref['vd'].col['Display'] and c[ref['vd'].col['Desc']] == 'PlayerCar': rv.setdefault(c[ref['vd'].col['Unique Id']], c)
    cv = {}
    for i, c in vd.rows():
        if len(c) > vd.col['Display'] and c[vd.col['Desc']] == 'PlayerCar': cv.setdefault(c[vd.col['Unique Id']], i)
    def crow(u): return vd.lines[cv[u]].split('\t')
    def model_ok(u):
        f = crow(u)[vd.col['File Name']].strip().lower()
        base = f[:-4] if f.endswith('.nif') else f
        return not have or not base.startswith('pc_') or base in have
    sup = lambda u: crow(u)[vd.col['Support']].strip() == '1'
    rsup = lambda u: u in rv and rv[u][ref['vd'].col['Support']].strip() == '1'
    def available(u):
        # na loja: o cliente tem a linha e o modelo; ligado (Support 1) ou desligado tambem na referencia (fica na
        # lista como na referencia; o jogo nao mostra carro desligado)
        return u in cv and model_ok(u) and (sup(u) or (u in rv and not rsup(u)))
    nvd = 0
    for u, i in cv.items():
        if u not in rv: continue
        c = vd.lines[i].split('\t'); r = rv[u]; old = list(c)
        for name in VD_COPY:
            if name in vd.col and name in ref['vd'].col: c[vd.col[name]] = r[ref['vd'].col[name]]
        # carro desligado neste cliente (Support 0) que a referencia usa: liga, se o modelo existir
        if r[ref['vd'].col['Support']].strip() == '1' and c[vd.col['Support']].strip() != '1' and model_ok(u): c[vd.col['Support']] = '1'
        if c != old: vd.lines[i] = '\t'.join(c); nvd += 1

    # ---- VehicleList (mesmo carro e mesmo V)
    def vkey(t, c):
        g = c[t.col['GradeLvl']].strip() if len(c) > t.col['GradeLvl'] else ''
        return (c[t.col['ID']].strip(), g)
    rl = {}
    for i, c in ref['vl'].rows():
        if len(c) > ref['vl'].col['GradeLvl']: rl.setdefault(vkey(ref['vl'], c), c)
    nvl = 0
    for i, c in vl.rows():
        if len(c) <= vl.col['GradeLvl']: continue
        r = rl.get(vkey(vl, c))
        if r is None: continue
        old = list(c)
        for name in VL_COPY:
            if name in vl.col and name in ref['vl'].col and ref['vl'].col[name] < len(r): c[vl.col[name]] = r[ref['vl'].col[name]]
        if c != old: vl.lines[i] = '\t'.join(c); nvl += 1

    # ---- loja
    def shop_ids(t):
        u = t.col['Unique Id']; return [c[u].strip() for i, c in t.rows() if len(c) > u and c[u].strip().isdigit()]
    rdisp = lambda u: int(rv[u][ref['vd'].col['Display']])
    ref_order = sorted([u for u in shop_ids(ref['cg']) if u in rv], key=rdisp)
    pos = {u: k for k, u in enumerate(ref_order)}
    lvl = lambda u: int(crow(u)[vd.col['Lvl']]) if crow(u)[vd.col['Lvl']].strip().isdigit() else 9
    def group(u):
        if u in rv: return 10 if special(rv[u][ref['vd'].col['UI Name']]) else int(rv[u][ref['vd'].col['Lvl']])
        return 10 if special(crow(u)[vd.col['UI Name']]) else lvl(u)
    mine = [u for u in shop_ids(cg) if u not in pos and u in cv and sup(u) and model_ok(u)]
    cdisp = lambda u: int(crow(u)[vd.col['Display']]) if crow(u)[vd.col['Display']].strip().lstrip('-').isdigit() else 0
    final = sorted([u for u in ref_order if available(u)] + mine,
                   key=lambda u: (group(u), 0, pos[u]) if u in pos else (group(u), 1, cdisp(u)))
    seen = set(); final = [u for u in final if not (u in seen or seen.add(u))]
    missing = [u for u in ref_order if rsup(u) and not (u in cv and sup(u) and model_ok(u))]
    ndisp = 0
    rrow = {}
    for i, c in ref['cg'].rows():
        if len(c) > ref['cg'].col['Unique Id']: rrow.setdefault(c[ref['cg'].col['Unique Id']].strip(), c)
    crow_cg = {}
    for i, c in cg.rows():
        if len(c) > cg.col['Unique Id']: crow_cg.setdefault(c[cg.col['Unique Id']].strip(), c)
    same = ref['cg'].lines[ref['cg'].hdr] == cg.lines[cg.hdr]
    keep = set(final)
    if not mine:
        # so carros da referencia: posicao na loja (Display) e linhas da lista iguais as da referencia (ja copiadas)
        order = [u for u in (c[ref['cg'].col['Unique Id']].strip() for i, c in ref['cg'].rows() if len(c) > ref['cg'].col['Unique Id']) if u in keep]
        order = [u for k, u in enumerate(order) if u not in order[:k]]
        renum = len(order) != len(rrow)
    else:
        # carros que so este cliente tem: posicao 1..N na ordem final
        order = final; renum = True
        for k, u in enumerate(final):
            c = crow(u)
            if c[vd.col['Display']] != str(k + 1): c[vd.col['Display']] = str(k + 1); vd.lines[cv[u]] = '\t'.join(c); ndisp += 1
    body = []
    for k, u in enumerate(order):
        c = list(rrow[u]) if (same and u in rrow) else list(crow_cg.get(u) or [])
        if not c:
            c = [''] * len(cg.lines[cg.hdr].split('\t'))
            r = crow(u)
            c[cg.col['Car Name']] = r[vd.col['Car Name']]; c[cg.col['UI Name']] = r[vd.col['UI Name']]; c[cg.col['Unique Id']] = u
            for n in ('New', 'Hot', 'GM'): c[cg.col[n]] = '0'
            c[cg.col['Category']] = '7'
        if renum or not same: c[0] = str(k)
        body.append('\t'.join(c))
    final = order
    head = cg.lines[:cg.hdr + 1]
    for j, l in enumerate(head):
        if l.startswith('Value\t'):
            c = l.split('\t'); c[1] = str(len(final)); head[j] = '\t'.join(c)
    tail = [l for l in cg.lines[cg.hdr + 1:] if not l.split('\t')[0].strip().isdigit()]
    new_cg = head + body + (tail if tail else [''])
    ncg = new_cg != cg.lines
    cg.lines = new_cg

    log('carros: loja com %d carros (%d da referencia, %d so deste cliente); %d sem modelo ou sem linha ficam fora' %
        (len(final), len(final) - len(mine), len(mine), len(missing)))
    log('carros: VehicleDisplay %d carros, VehicleList %d linhas, posicao na loja %d, lista da loja %s, cupons %d' %
        (nvd, nvl, ndisp, 'nova' if ncg else 'igual', ncp))
    if not test:
        if nvd or ndisp: act['vd'][0].store(act['vd'][1], vd.text())
        if nvl: act['vl'][0].store(act['vl'][1], vl.text())
        if ncg: act['cg'][0].store(act['cg'][1], cg.text())
        if ncp: cpi[0].store(cpi[1], '\r\n'.join(cpi[2]))
        for pk in packs:
            if pk.changed: pk.save(); log('carros: gravado ' + pk.path)
    shop = sorted([u for u in final if u in cv], key=lambda u: cdisp(u))
    shop = [dict(pos=k + 1, id=int(u), name=crow(u)[vd.col['UI Name']].strip(), v=lvl(u)) for k, u in enumerate(shop)]
    return dict(shop=shop, missing=[int(u) for u in missing], changed=any(p.changed for p in packs))

if __name__ == '__main__':
    r = adapt(sys.argv[1], '--testar' in sys.argv)
    if r: print('fora da loja:', r['missing'][:50])
