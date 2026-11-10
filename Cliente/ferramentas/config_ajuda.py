# Gera Rice/res/config_ajuda.json (categoria e explicacao de cada configuracao) a partir dos comentarios do Rice/Config.cs.
# O menu (Rice.exe menu > Configuracoes) usa esse arquivo. Rode de novo quando o Config.cs mudar.
import re, json, os
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
src = open(os.path.join(ROOT, 'Rice', 'Config.cs'), encoding='utf-8-sig').read().split('\n')
PREFIX = [('Neo', 'Neo City'), ('Battle', 'Battle Zone'), ('Party', 'Party'), ('Bots', 'Bots'), ('Bot', 'Bots'), ('City', 'Cidade e trafego'),
          ('Dome', 'Cidade e trafego'), ('Traffic', 'Cidade e trafego'), ('Huv', 'HUV'), ('Chase', 'HUV'), ('Auction', 'Leilao'),
          ('Rush', 'Rush'), ('Crew', 'Crews'), ('License', 'Licencas'), ('Combo', 'Combos'), ('Upped', 'Perfil upado'),
          ('Shop', 'Lojas'), ('Part', 'Lojas'), ('Car', 'Carros'), ('Fuel', 'Combustivel'), ('Mito', 'Economia'), ('Exp', 'Economia'),
          ('Undercity', 'Undercity'), ('Mission', 'Missoes'), ('Quest', 'Missoes'), ('Alchemy', 'Itens'), ('Item', 'Itens'), ('Dock', 'Itens'),
          ('Option', 'Itens'), ('Visual', 'Visual e Drift shop'), ('Drift', 'Visual e Drift shop'), ('Coupon', 'Itens'), ('Weather', 'Cidade e trafego'),
          ('Close', 'Rede'), ('Port', 'Rede'), ('Auth', 'Rede'), ('Lobby', 'Rede'), ('Game', 'Rede'), ('Area', 'Rede'), ('Ranking', 'Rede'), ('Public', 'Rede'), ('Debug', 'Rede'), ('Admin', 'Contas'), ('Normal', 'Contas'), ('Max', 'Contas'), ('Police', 'Bots'), ('Evento', 'Eventos'), ('Events', 'Eventos')]
out = {}
section = 'Geral'
pending = []
group = ''          # comentario do bloco: vale para as opcoes seguidas (ate uma linha em branco ou outro comentario)
for line in src:
    s = line.strip()
    m = re.match(r'//\s*-{3,}\s*(.+?)\s*-{3,}', s)
    if m: section = m.group(1); pending = []; continue
    if s.startswith('//'):
        pending.append(s[2:].strip()); continue
    m = re.match(r'public\s+[\w\[\]<>, ]+?\s+(\w+)\s*=.*?;\s*(//\s*(.*))?$', s)
    if m and '(' not in s.split('=')[0]:
        name, inline = m.group(1), (m.group(3) or '').strip()
        if pending: group = ' '.join(pending[-3:])
        desc = inline or ' '.join(pending[-3:]) or group
        cat = section
        for p, c in PREFIX:
            if name.startswith(p): cat = c; break
        out[name] = [cat, desc]
    pending = [] if not s.startswith('//') else pending
    if s == '': pending = []; group = ''
MERGE = [('HUV', 'HUV'), ('Perf', 'Perfil upado'), ('Loja', 'Lojas'), ('Part Shop', 'Lojas'), ('Posicao', 'Contas'),
         ('Recompensas', 'Economia'), ('Geral', 'Rede'), ('Carros', 'Lojas'), ('Licencas', 'Crews')]
for k, v in out.items():
    for a, b in MERGE:
        if v[0].startswith(a): v[0] = b; break
json.dump(out, open(os.path.join(ROOT, 'Rice', 'res', 'config_ajuda.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(len(out), 'configuracoes;', len(set(v[0] for v in out.values())), 'categorias')
