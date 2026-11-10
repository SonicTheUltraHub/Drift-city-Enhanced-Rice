# Gera Rice/res/AlchemyRecipes.json (Production Skill Book) a partir do Init_us.agt: receitas (livro, nivel, materiais,
# kit, resultado, chance, exp) e a tabela de nivel de fabricacao (EXP_MAX).
# Uso (na pasta do projeto): python3 Cliente/ferramentas/producao_receitas.py
import sys, os, json, csv, io
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
d = open(os.path.join(ROOT, 'Cliente', 'Init_us.agt'), 'rb').read()
ents, _ = agt.parse(d)
def table(word):
    for e in ents:
        rows = list(csv.reader(io.StringIO(agt.read_file(d, e)[0].decode('utf-16')), delimiter='\t'))
        for k, r in enumerate(rows[:6]):
            if word in r: return r, rows[k + 1:]
hdr, rows = table('SkillBookID')
rec = []
for r in rows:
    if len(r) < 28 or not r[0].isdigit() or not r[1]: continue
    def num(s, dflt=0):
        try: return int(float(s))
        except Exception: return dflt
    rec.append({'book': r[1], 'level': num(r[2], 1), 'group': r[3], 'name': r[4],
                'need': [[r[6 + 2 * k], num(r[7 + 2 * k], 1)] for k in range(3) if r[6 + 2 * k].strip()],
                'kit': r[12], 'kitCount': num(r[13], 1), 'result': r[14], 'resultCount': num(r[15], 1),
                'failChance': num(r[16]), 'failItem': r[17], 'failCount': num(r[18], 1),
                'chance': num(r[25], 100), 'exp': num(r[26]), 'failExp': num(r[27])})
hdr, rows = table('EXP_MAX')
levels = [int(r[1]) for r in rows if len(r) > 2 and r[0].isdigit()]
json.dump({'levels': levels, 'recipes': rec}, open(os.path.join(ROOT, 'Rice', 'res', 'AlchemyRecipes.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(len(rec), 'receitas, niveis', levels)
