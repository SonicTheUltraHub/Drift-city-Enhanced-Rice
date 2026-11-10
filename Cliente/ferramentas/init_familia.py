# Para o instalador reconhecer um Data_US\Init_us.agt "nosso" que nao bate o md5 (mexido por uma instalacao anterior ou
# com a loja ajustada a este cliente): gera Instalador\modificacoes\init_familia.lst (T|tabela|md5 = a nossa de agora,
# B|tabela|md5 = a das versoes originais conhecidas) e Instalador\modificacoes\Init_us.agt.completo (o nosso inteiro).
# As tabelas de carros/loja/cupons nao contam (o ajuste de carros cuida delas).
# Uso: python3 Cliente/ferramentas/init_familia.py <Init_us original 1> [<Init_us original 2> ...]
import sys, os, hashlib, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.join(HERE, '..', '..')
OUT = os.path.join(ROOT, 'Instalador', 'modificacoes')
def tabs(f):
    d = open(f, 'rb').read(); ents, _ = agt.parse(d)
    return [(e['name'], hashlib.md5(agt.read_file(d, e)[0]).hexdigest()) for e in ents]
target = os.path.join(ROOT, 'Cliente', 'Init_us.agt')
lines = ['T|%s|%s' % t for t in tabs(target)]
for b in sys.argv[1:]: lines += ['B|%s|%s' % t for t in tabs(b)]
open(os.path.join(OUT, 'init_familia.lst'), 'w', encoding='utf-8', newline='\r\n').write('\n'.join(sorted(set(lines))) + '\n')
shutil.copyfile(target, os.path.join(OUT, 'Init_us.agt.completo'))
print(len(lines), 'linhas;', hashlib.md5(open(target, 'rb').read()).hexdigest())
