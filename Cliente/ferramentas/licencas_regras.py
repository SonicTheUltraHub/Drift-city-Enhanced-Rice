# Gera Rice/res/LicenseRules.json a partir do Init_us.agt: as condicoes (GC_...) de cada licenca, se basta uma ("any")
# ou precisa de todas, os numeros das condicoes (o 814 do jogo manda "numero da condicao, valor") e a cidade de cada pista.
# Uso (na pasta do projeto): python3 Cliente/ferramentas/licencas_regras.py
import sys, os, json, csv, io
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
d = open(os.path.join(ROOT, 'Cliente', 'Init_us.agt'), 'rb').read()
ents, _ = agt.parse(d)
def rows_with(header_word):
    for e in ents:
        t = agt.read_file(d, e)[0].decode('utf-16')
        rows = list(csv.reader(io.StringIO(t), delimiter='\t'))
        for k, r in enumerate(rows[:6]):
            if header_word in r: return r, rows[k + 1:]
    raise Exception('tabela nao achada: ' + header_word)
hdr, rows = rows_with('GC_IDNAME')
gc = {}
for r in rows:
    if len(r) > 2 and r[1].isdigit() and r[2].startswith('GC_'): gc[r[2]] = int(r[1])
hdr, rows = rows_with('Condition_1')
ix = {h: i for i, h in enumerate(hdr) if h}
lic = []
for r in rows:
    if len(r) <= ix['Condition_12'] or not r[ix['LicenseID']].isdigit(): continue
    conds = []
    for k in range(1, 13):
        v = r[ix['Condition_%d' % k]].strip()
        if ':' in v and v.startswith('GC_'):
            n, need = v.rsplit(':', 1)
            conds.append([n, int(need) if need.strip().isdigit() else 0])
    post = (r[ix['Postfix']] + ' ' + r[ix['GainConditionText']]).lower()
    lic.append({'id': int(r[ix['LicenseID']]), 'name': r[ix['LicenseName']].strip(), 'support': r[ix['Support']] == '1',
                'any': 'any one' in post, 'conds': conds})
hdr, rows = rows_with('pos_start')
ix = {h: i for i, h in enumerate(hdr) if h}
city = {'moonpalace': 1, 'koinonia': 2, 'kononia': 2, 'cras': 3, 'oros': 4, 'taipei': 5}
tracks = {}
for r in rows:
    if len(r) > 4 and r[ix['Id']].isdigit() and r[2]:
        tracks.setdefault(r[ix['Id']], city.get(r[2].strip().lower(), 0))
out = {'gc': gc, 'licenses': lic, 'tracks': tracks}
json.dump(out, open(os.path.join(ROOT, 'Rice', 'res', 'LicenseRules.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(len(gc), 'condicoes,', len(lic), 'licencas,', len(tracks), 'pistas')
