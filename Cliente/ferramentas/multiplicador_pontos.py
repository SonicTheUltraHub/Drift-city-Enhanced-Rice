# Multiplicador escondido dos pontos de performance, direto no DriftCity.exe (nada aparece no Assist Option e os
# pontos do painel nao mudam). No exe (0x4929b0) cada atributo vira "pontos efetivos" assim:
#     (Total + extras) x taxa x 0.001
# e e isso que a fisica do carro usa (velocidade maxima, aceleracao, dano de batida, boost). Este script troca o 0.001
# de cada atributo por 0.001 x multiplicador (ex.: 1.2 = cada ponto vale 1,2 ponto).
# Uso: python3 multiplicador_pontos.py <DriftCity.exe> [speed] [dura] [accel] [boost]     (padrao 1.2 em todos)
#      python3 multiplicador_pontos.py <DriftCity.exe> 1 1 1 1                            (volta ao normal)
# Backup: DriftCity.exe.antes_multiplicador (na primeira vez).
import sys, os, struct, shutil
exe = sys.argv[1]
mult = [float(x) for x in sys.argv[2:6]] + [1.2] * (4 - len(sys.argv[2:6]))
d = bytearray(open(exe, 'rb').read())
FM = bytes.fromhex('d80d')                       # fmuls [endereco]
OLD = struct.pack('<I', 0x944f84)                # endereco do 0.001 original
SIGS = [('speed', '8b96740b0000', 0), ('dura', '8bbe780b0000', 1), ('accel', '8bbe7c0b0000', 2),
        ('boost', '8b96800b0000', 3), ('speed2', '8b8e740b0000', 0)]
TEXT_FILE, TEXT_VA = 0x400, 0x401000
CAVE = 0x1d00                                    # 16 bytes de preenchimento (0xCC) entre funcoes no .text
cave_va = struct.pack('<I', CAVE - TEXT_FILE + TEXT_VA)
spots = []
for name, sig, slot in SIGS:
    s = bytes.fromhex(sig)
    i = d.find(s, 0x91000, 0x93000)
    assert i > 0, 'nao achei ' + name
    j = d.find(FM, i, i + 48)
    while j > 0 and d[j + 2:j + 6] not in (OLD, struct.pack('<I', struct.unpack('<I', cave_va)[0] + 4 * slot)):
        j = d.find(FM, j + 1, i + 48)
    assert j > 0, 'nao achei o fmuls de ' + name
    spots.append((name, j, slot))
fresh = d[CAVE:CAVE + 16] == b'\xcc' * 16
if fresh and not os.path.exists(exe + '.antes_multiplicador'): shutil.copy2(exe, exe + '.antes_multiplicador')
assert fresh or all(d[j + 2:j + 6] != OLD for _, j, _ in spots), 'estado estranho: o espaco ja foi usado por outra coisa'
d[CAVE:CAVE + 16] = struct.pack('<4f', *[0.001 * m for m in mult])
cv = struct.unpack('<I', cave_va)[0]
for name, j, slot in spots:
    d[j + 2:j + 6] = struct.pack('<I', cv + 4 * slot)
    print(name, hex(j), '-> x%.2f' % mult[slot])
open(exe, 'wb').write(bytes(d))
print('ok:', exe)
