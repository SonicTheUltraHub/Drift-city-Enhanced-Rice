# Carros especiais (ST, BT, Xmas) ganhos na gacha/evento: o jogo pedia nivel 90 em TODOS os V (V2..V9), entao quem
# ganhava um antes do 90 nao conseguia upar. Aqui o V2..V9 deles pede o nivel normal do V (CarTierLevels do servidor:
# V2 10, V3 20, V4 30, V5 40, V6 50, V7 60, V8 75, V9 80) e o V1 pede 1 (o jogo olha o nivel do V atual do carro para deixar upar). Comprar na loja continua
# no 90+: quem cobra e o servidor (SpecialCarsMinLevel).
# Tambem o VehicleDisplay (coluna Req Level do carro, 90 nos especiais): o jogo usa esse nivel para deixar usar o
# item de upgrade (V2 Car Upgrade...) no carro; vira 1 (o servidor continua cobrando o nivel 90+ para COMPRAR).
# Uso (na pasta do projeto): python3 Cliente/ferramentas/especiais_upgrade.py
import sys, os, struct, zlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
PATH = os.path.join(HERE, '..', 'Init_us.agt')
TIER = {1: 1, 2: 10, 3: 20, 4: 30, 5: 40, 6: 50, 7: 60, 8: 75, 9: 80}
d = bytearray(open(PATH, 'rb').read())
ents, _ = agt.parse(bytes(d))
def special(n): n = n.strip().upper(); return n.endswith(' ST') or n.endswith(' BT') or n.endswith(' XMAS')
def fix_list(lines):
    n = 0
    for k, l in enumerate(lines):
        c = l.split('\t')
        if len(c) < 20 or c[18] != 'V' or not c[19].isdigit(): continue
        g = int(c[19])
        if g >= 1 and c[16] == '90' and special(c[4].rsplit(' ', 1)[0]):
            c[16] = str(TIER[g]); lines[k] = '\t'.join(c); n += 1
    return n
def fix_display(lines):
    hdr = [l for l in lines[:6] if l.startswith('Index\t')][0].split('\t')
    ui, rq = hdr.index('UI Name'), hdr.index('Req Level')
    n = 0
    for k, l in enumerate(lines):
        c = l.split('\t')
        if len(c) > rq and c[rq] == '90' and special(c[ui]):
            c[rq] = '1'; lines[k] = '\t'.join(c); n += 1
    return n
for tname, fix in (('VehicleList.xlt', fix_list), ('VehicleDisplay.xlt', fix_display)):
  e = [x for x in ents if x['name'].endswith(tname)][0]
  nxt = min([x['off'] for x in ents if x['off'] > e['off']] + [len(d)])
  raw, _ = agt.read_file(bytes(d), e)
  lines = raw.decode('utf-16').split('\r\n')
  n = fix(lines)
  new = '\r\n'.join(lines).encode('utf-16')
  chunks = [zlib.compress(new[k:k + 16384], 9) for k in range(0, len(new), 16384)]
  tab = struct.pack('<%dH' % len(chunks), *[len(x) for x in chunks])
  size = len(tab) + sum(map(len, chunks))
  print(tname, n, 'linhas mudadas; tabela', size, 'espaco', nxt - e['off'])
  assert size <= nxt - e['off'], 'nao cabe'
  pos = e['off']
  for part in [tab] + chunks:
      d[pos:pos + len(part)] = agt.xor(part, pos); pos += len(part)
  p = e['dirpos'] + 4 + len(e['name'])
  d[p:p + 12] = agt.xor(struct.pack('<III', e['off'], len(chunks), len(new)), p)
open(PATH, 'wb').write(bytes(d))
