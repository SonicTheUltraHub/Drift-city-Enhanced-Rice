# Mesmo algoritmo do instalador (Carros.Tables): procura em todos os pacotes do cliente (Data, Data_US, Patch e
# subpastas) as tabelas com o conteudo ORIGINAL de Instalador\modificacoes\tabelas.lst e troca pela nossa.
# Uso: python3 aplicar_tabelas.py <pasta do jogo> [--testar]
import sys, os, gzip, hashlib, glob
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
from adaptar_carros import Pack
MOD = os.path.join(HERE, '..', '..', 'Instalador', 'modificacoes')

def apply(game, test=False, log=print):
    want = {}
    for l in open(os.path.join(MOD, 'tabelas.lst'), encoding='utf-8'):
        f = l.strip().split('|')
        if len(f) >= 4: want[f[0]] = (int(f[1]), f[2], f[3], f[4] if len(f) > 4 else '')
    sizes = set(v[0] for v in want.values())
    files = []
    for sub in ('Data', 'Data_US', 'Patch'):
        for p in glob.glob(os.path.join(game, sub, '**', '*'), recursive=True):
            if os.path.isfile(p) and 64 < os.path.getsize(p) < 120 * 1024 * 1024: files.append(p)
    done = []
    for p in files:
        with open(p, 'rb') as fh:
            if fh.read(8) != b'NayaPack': continue
        pk = Pack(p)
        for i, e in enumerate(pk.ents):
            if e['usize'] not in sizes: continue
            try: raw = agt.read_file(bytes(pk.d), e)[0]
            except Exception: continue
            h = hashlib.md5(raw).hexdigest()
            if h not in want: continue
            new = gzip.open(os.path.join(MOD, want[h][1])).read()
            if not test: store_raw(pk, i, new)
            done.append((os.path.relpath(p, game), e['name'], want[h][3]))
        if pk.changed and not test: pk.save()
    for d in done: log('tabela: %s | %s <- %s' % d)
    return done

def store_raw(pk, i, raw):
    import zlib, struct
    e = pk.ents[i]
    chunks = [zlib.compress(raw[k:k + 16384], 9) for k in range(0, len(raw), 16384)]
    tab = struct.pack('<%dH' % len(chunks), *[len(c) for c in chunks]); size = len(tab) + sum(map(len, chunks))
    nxt = min([x['off'] for x in pk.ents if x['off'] > e['off']] + [len(pk.d)])
    off = e['off'] if size <= nxt - e['off'] else len(pk.d)
    if off == len(pk.d): pk.d.extend(b'\0' * size)
    pos = off
    for part in [tab] + chunks:
        pk.d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
    p = e['dirpos'] + 4 + len(e['name'])
    pk.d[p:p + 12] = agt.xor(struct.pack('<III', off, len(chunks), len(raw)), p)
    e['off'], e['nch'], e['usize'] = off, len(chunks), len(raw); pk.changed = True

if __name__ == '__main__':
    apply(sys.argv[1], '--testar' in sys.argv)
