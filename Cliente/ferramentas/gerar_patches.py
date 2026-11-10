# Gera os patches do instalador: so as DIFERENCAS entre os arquivos originais do jogo (de quem instala) e os do
# projeto (Cliente/). O instalador leva so isso; os arquivos do jogo tem que ser do usuario.
#
# Uso: python3 Cliente/ferramentas/gerar_patches.py <pasta(s) com o jogo original> ...
#   cada pasta e uma copia do jogo limpo (DriftCity.exe, Data, Data_US, Patch); pode passar mais de uma (versoes
#   diferentes do jogo: o instalador aceita qualquer uma delas). Arquivos soltos tambem valem: caminho=arquivo
#   (ex.: Data_US/Init_us.agt=/backup/Init_us.agt.original).
# Saida: Instalador/modificacoes/*.dcp, manifest.lst e DriftCity.exe.completo (vao dentro do Instalador.exe)
#
# Formato .dcp: "DCP1" + zlib( operacoes ): 'C' u32 inicio u32 tamanho = copia do original;
#                                           'A' u32 tamanho + bytes = bytes novos.
import sys, os, json, hashlib, zlib, struct

HERE = os.path.dirname(os.path.abspath(__file__))
CLI = os.path.join(HERE, '..')
OUT = os.path.join(CLI, '..', 'Instalador', 'modificacoes')

# arquivo do projeto -> lugar no jogo; base = arquivo do jogo de onde sai a diferenca (padrao: o mesmo lugar)
TARGETS = [
    # DriftCity.exe: vai inteiro (o do mini-Rice ja modificado; o original e protegido com ASProtect)
    ('DriftCity.exe', 'DriftCity.exe', None),
    ('Init.agt', 'Data/Init.agt', None),
    ('Init_us.agt', 'Data_US/Init_us.agt', None),
    ('Data_US/World.agt', 'Data_US/World.agt', None),
    ('Patch/patch.003', 'Patch/patch.003', None),
    ('Patch/patch.008', 'Patch/patch.008', None),
    ('Patch/patch.009', 'Patch/patch.009', None),
    # musica de Natal da cidade em loop (TurnOver = a propria faixa)
    ('Data_US/audio/bgm/music_natal.lst', 'Data_US/audio/bgm/music.txt', None),
] + [('Data/GameUI/teammark/%02d' % i, 'Data/GameUI/teammark/%02d' % i, 'Data/GameUI/teammark/00') for i in range(1, 11)]

def md5(b): return hashlib.md5(b).hexdigest()

def diff(base, target, B=48):
    index = {}
    for off in range(0, len(base) - B + 1, B):
        index.setdefault(base[off:off + B], off)
    ops = []; add = bytearray(); i = 0; n = len(target); nb = len(base)
    def flush():
        if add: ops.append((b'A', bytes(add))); add.clear()
    while i < n:
        off = None
        if i + B <= nb and target[i:i + B] == base[i:i + B]: off = i
        elif i + B <= n: off = index.get(target[i:i + B])
        if off is None or i + B > n:
            add.append(target[i]); i += 1; continue
        L = B
        for step in (1 << 20, 1 << 14, 1 << 10, 64, 1):
            while i + L + step <= n and off + L + step <= nb and target[i + L:i + L + step] == base[off + L:off + L + step]:
                L += step
        back = 0
        while back < len(add) and off - back - 1 >= 0 and add[-1 - back] == base[off - back - 1]: back += 1
        if back: del add[-back:]
        flush()
        ops.append((b'C', off - back, L + back))
        i += L
    flush()
    raw = bytearray()
    for op in ops:
        if op[0] == b'C': raw += b'C' + struct.pack('<II', op[1], op[2])
        else: raw += b'A' + struct.pack('<I', len(op[1])) + op[1]
    return b'DCP1' + zlib.compress(bytes(raw), 9)

def apply(base, patch):
    assert patch[:4] == b'DCP1'
    raw = zlib.decompress(patch[4:]); out = bytearray(); p = 0
    while p < len(raw):
        t = raw[p:p + 1]; p += 1
        if t == b'C':
            o, l = struct.unpack_from('<II', raw, p); p += 8; out += base[o:o + l]
        else:
            l, = struct.unpack_from('<I', raw, p); p += 4; out += raw[p:p + l]; p += l
    return bytes(out)

def main():
    srcs = sys.argv[1:]
    if not srcs: print(__doc__ or 'uso: gerar_patches.py <pasta do jogo original> ...'); return
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith('.dcp'): os.remove(os.path.join(OUT, f))
    singles = {}
    folders = []
    for s in srcs:
        if '=' in s: k, v = s.split('=', 1); singles.setdefault(k.replace('\\', '/'), []).append(v)
        else: folders.append(s)
    manifest = []
    total = 0
    for proj, dest, basepath in TARGETS:
        tp = os.path.join(CLI, proj)
        if not os.path.exists(tp): print('falta no projeto:', proj); continue
        target = open(tp, 'rb').read()
        bp = basepath or dest
        cands = [os.path.join(f, bp) for f in folders] + singles.get(bp, [])
        # DriftCity.exe vai inteiro (o do mini-Rice ja modificado): sem diferenca
        if dest == 'DriftCity.exe':
            import shutil
            shutil.copyfile(tp, os.path.join(OUT, 'DriftCity.exe.completo'))
            manifest.append({'path': dest, 'base': bp, 'md5': md5(target), 'size': len(target),
                             'from': [{'md5': 'completo', 'patch': 'DriftCity.exe.completo'}]})
            print('DriftCity.exe inteiro: %d bytes' % len(target))
            continue
        bases = {}
        for c in cands:
            if os.path.exists(c):
                b = open(c, 'rb').read(); bases.setdefault(md5(b), b)
        entry = {'path': dest, 'base': bp, 'md5': md5(target), 'size': len(target), 'from': []}
        for bm, b in bases.items():
            if bm == entry['md5']: continue
            p = diff(b, target)
            assert apply(b, p) == target, 'patch nao confere: ' + dest
            name = dest.replace('/', '_') + '.' + bm[:8] + '.dcp'
            open(os.path.join(OUT, name), 'wb').write(p)
            entry['from'].append({'md5': bm, 'patch': name})
            total += len(p)
            print('%-32s base %s -> %7d bytes (%.2f%% do arquivo)' % (dest, bm[:8], len(p), 100.0 * len(p) / max(1, len(target))))
        if not entry['from']: print('sem original para', dest)
        manifest.append(entry)
    json.dump(manifest, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1)
    # o mesmo em texto simples para o Instalador.exe: destino|base|md5|tamanho|md5base:patch,md5base:patch
    with open(os.path.join(OUT, 'manifest.lst'), 'w', newline='\r\n') as f:
        for e in manifest:
            f.write('%s|%s|%s|%d|%s\n' % (e['path'], e['base'], e['md5'], e['size'], ','.join(x['md5'] + ':' + x['patch'] for x in e['from'])))
    print('total dos patches: %.1f KB' % (total / 1024.0))

if __name__ == '__main__': main()
