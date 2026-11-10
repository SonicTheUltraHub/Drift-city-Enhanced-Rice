# Tabelas que o Offline Aprimorado modificou dentro dos pacotes do jogo, para o instalador aplicar em QUALQUER cliente
# (Instalador\Carros.cs, Tabelas): para cada tabela modificada guarda o md5 do conteudo ORIGINAL e o conteudo novo.
# O instalador procura, em todos os pacotes do cliente (Data, Data_US, Patch, subpastas), uma tabela com exatamente o
# conteudo original (o nome dentro do pacote nao importa: no openDC elas ficam em Data\World\data.agt e Data\Init.agt)
# e troca pela nossa. Tabela diferente da original nao e tocada.
# Gera Instalador\modificacoes\tabelas.lst (md5 original|tamanho|arquivo|md5 novo|de onde) e tab_<md5>.gz.
# Uso (na pasta do projeto): python3 Cliente/ferramentas/tabelas_referencia.py <pasta com os originais>
#   a pasta dos originais tem Patch\patch.003/008/009, Data_US\World.agt e Data_US\Init_us.agt sem modificacao.
import sys, os, gzip, hashlib
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.join(HERE, '..', '..')
OUT = os.path.join(ROOT, 'Instalador', 'modificacoes')
ORIG = sys.argv[1] if len(sys.argv) > 1 else '.'
PAIRS = [(os.path.join(ORIG, 'Patch', 'patch.003'), os.path.join(ROOT, 'Cliente', 'Patch', 'patch.003')),
         (os.path.join(ORIG, 'Patch', 'patch.008'), os.path.join(ROOT, 'Cliente', 'Patch', 'patch.008')),
         (os.path.join(ORIG, 'Patch', 'patch.009'), os.path.join(ROOT, 'Cliente', 'Patch', 'patch.009')),
         (os.path.join(ORIG, 'Data_US', 'World.agt'), os.path.join(ROOT, 'Cliente', 'Data_US', 'World.agt')),
         (os.path.join(ORIG, 'Data_US', 'Init_us.agt'), os.path.join(ROOT, 'Cliente', 'Init_us.agt'))]
# tabelas de carros/loja/cupons: o ajuste de carros (carros_*.ref) cuida delas em qualquer cliente
SKIP = ('VehicleDisplay', 'VehicleList', 'VehicleColorGroups', 'compound')

for f in os.listdir(OUT):
    if f.startswith('tab_') and f.endswith('.gz'): os.remove(os.path.join(OUT, f))
lines = []
for b, t in PAIRS:
    db = open(b, 'rb').read(); dt = open(t, 'rb').read()
    eb, _ = agt.parse(db); et, _ = agt.parse(dt); mb = {e['name']: e for e in eb}
    for e in et:
        if e['name'] not in mb or any(s in e['name'] for s in SKIP): continue
        new = agt.read_file(dt, e)[0]; old = agt.read_file(db, mb[e['name']])[0]
        if new == old: continue
        ho = hashlib.md5(old).hexdigest(); fn = 'tab_%s.gz' % ho[:12]
        with gzip.open(os.path.join(OUT, fn), 'wb', 9) as g: g.write(new)
        lines.append('%s|%d|%s|%s|%s' % (ho, len(old), fn, hashlib.md5(new).hexdigest(), os.path.basename(t) + ':' + e['name']))
        print(os.path.basename(t), e['name'], len(old), '->', len(new))
open(os.path.join(OUT, 'tabelas.lst'), 'w', encoding='utf-8', newline='\r\n').write('\n'.join(lines) + '\n')
print(len(lines), 'tabelas')
