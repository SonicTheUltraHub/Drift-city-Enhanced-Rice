# Coloca na loja (coluna partsshop = B1F) todas as armas, armaduras, Amplifiers e Fuses da Neo City na tabela de itens
# do cliente US (Data_US\World\data\ItemClient.tdf, dentro do Patch\patch.008). Faz backup em Patch\backup\patch.008.antes_loja_neo.
# Uso: python3 loja_neo.py <pasta do jogo>
import sys, os, struct, zlib, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
game = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..')
path = os.path.join(game, 'Patch', 'patch.008')
d = bytearray(open(path, 'rb').read())
ents, _ = agt.parse(bytes(d))
CATS = ('neo_weapon', 'neo_armor', 'neo_coil', 'neo_fuse')
SHOP = 'B1F'

def find_table():
    for k, e in enumerate(ents):
        if e['usize'] < 100000: continue
        try: data, _ = agt.read_file(bytes(d), e)
        except Exception: continue
        if 'N Cannon 40'.encode('utf-16-le') in data and 'neo_weapon'.encode('utf-16-le') in data: return k, e, data
    raise SystemExit('tabela de itens nao encontrada')

k, e, data = find_table()
# a tabela comeca depois de uma imagem BMP (tamanho no cabecalho "BM")
o = struct.unpack_from('<I', data, 2)[0] if data[:2] == b'BM' else 0
hdr = data[o:o + 24]
cols, rows = struct.unpack_from('<II', hdr, 16)      # cabecalho: ..., u32 tamanho da tabela @12, colunas @16, linhas @20
offs = list(struct.unpack_from('<%dI' % (rows * cols), data, o + 24))
def s_at(p):
    q = p
    while data[o + q:o + q + 2] != b'\0\0': q += 2
    return data[o + p:o + q].decode('utf-16-le')
cells = [[s_at(offs[r * cols + c]) for c in range(cols)] for r in range(rows)]
def build(cells):
    blob = bytearray(); newoffs = []
    base = 24 + 4 * rows * cols
    for row in cells:
        for v in row:
            newoffs.append(base + len(blob)); blob += v.encode('utf-16-le') + b'\0\0'
    h = bytearray(hdr); struct.pack_into('<I', h, 12, base + len(blob))
    return bytes(data[:o]) + bytes(h) + struct.pack('<%dI' % len(newoffs), *newoffs) + bytes(blob)
# confere: remontar sem mudar nada tem que dar o arquivo original (formato entendido certo)
assert build(cells) == data, 'remontagem diferente do original - nada foi alterado'
changed = 0
for row in cells:
    if row[0] in CATS and not row[2].endswith('9999') and not row[2].endswith('99998') and row[15] != SHOP:
        row[15] = SHOP; changed += 1
new = build(cells)
nxt = min([x['off'] for x in ents if x['off'] > e['off']] + [len(d)])
chunks = [zlib.compress(new[i:i + 16384], 9) for i in range(0, len(new), 16384)]
tab = struct.pack('<%dH' % len(chunks), *[len(x) for x in chunks])
size = len(tab) + sum(map(len, chunks))
print('itens na loja:', changed, '| tabela', len(data), '->', len(new), '| comprimido', size, 'espaco', nxt - e['off'])
assert size <= nxt - e['off'], 'nao cabe'
bk = os.path.join(game, 'Patch', 'backup'); os.makedirs(bk, exist_ok=True)
if not os.path.exists(os.path.join(bk, 'patch.008.antes_loja_neo')): shutil.copy2(path, os.path.join(bk, 'patch.008.antes_loja_neo'))
pos = e['off']
for part in [tab] + chunks:
    d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
p = e['dirpos'] + 4 + len(e['name'])
d[p:p + 12] = agt.xor(struct.pack('<III', e['off'], len(chunks), len(new)), p)
open(path, 'wb').write(bytes(d))
print('ok:', path)
