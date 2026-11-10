# Tabela "Complete Bonus Reward" do fim da corrida (estrelas, Exp e Mito): o jogo so conta os corredores de verdade
# (serial <= 0xffff) e com 1 jogador ela fica zerada. Este patch faz o jogo contar tambem os bots (IA) da sala
# (DriftCity.exe 0x5c98ac: "ja +1" -> 2 NOP). O servidor paga o mesmo bonus (Battle.cs, CompleteBonus).
# Uso: python3 Cliente/ferramentas/bonus_ia.py [DriftCity.exe]      (--desfazer volta o original)
import sys, os
HERE = os.path.dirname(os.path.abspath(__file__))
args = [a for a in sys.argv[1:] if not a.startswith('--')]
path = args[0] if args else os.path.join(HERE, '..', 'DriftCity.exe')
d = bytearray(open(path, 'rb').read())
o = 0x5c98a6 - 0x401000 + 0x400
orig, patched = bytes.fromhex('813affff00007701'), bytes.fromhex('813affff00009090')
cur = bytes(d[o:o + 8])
want = orig if '--desfazer' in sys.argv else patched
if cur == want: print('ja esta assim'); sys.exit(0)
assert cur in (orig, patched), 'exe diferente: ' + cur.hex()
d[o:o + 8] = want
open(path, 'wb').write(bytes(d))
print('ok:', 'original' if want == orig else 'bots contam no bonus')
