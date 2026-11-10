# Gera as tabelas de referencia dos carros que o instalador usa para arrumar a loja de QUALQUER cliente do Drift City
# (Instalador\modificacoes\carros_*.ref) e o Rice\res\ShopCars.json, a partir do Cliente\Init_us.agt (o nosso, ja arrumado).
#   carros_VehicleDisplay.ref : tabela VehicleDisplay (V, nivel pedido, pontos, posicao na loja de cada carro)
#   carros_VehicleList.ref    : tabela VehicleList (pontos, preco e nivel pedido de cada V de cada carro)
#   carros_Loja.ref           : lista de carros da loja (a tabela com "Unique Id  New  Hot  GM  Category")
#   carros_Cupons.ref         : tabela de cupons de carro (Coupon Book: quantas vezes, carro, cupom, linha do V)
# O instalador (Instalador\Carros.cs; o mesmo algoritmo em adaptar_carros.py) copia esses valores para os carros que o
# cliente tem e tira da loja os que faltam.
# Uso (na pasta do projeto): python3 Cliente/ferramentas/carros_referencia.py   (rode de novo quando mudar o Init_us.agt)
import sys, os, json
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import agt
ROOT = os.path.join(HERE, '..', '..')
OUT = os.path.join(ROOT, 'Instalador', 'modificacoes')

is_vd = lambda t: '\tUnique Id\tSellable\t' in t and 'PlayerCar' in t
is_vl = lambda t: 'Game UI name' in t and '\tUpgradeMito\t' in t
is_cg = lambda t: '\tUnique Id\tNew\tHot\tGM\tCategory' in t
is_cp = lambda t: 'CouponStampDisaply' in t and '\ti_n_c' in t

def tables(path):
    d = open(path, 'rb').read(); ents, _ = agt.parse(d); found = {}
    for e in ents:
        try: t = agt.read_file(d, e)[0].decode('utf-16')
        except Exception: continue
        for k, f in (('vd', is_vd), ('vl', is_vl), ('cg', is_cg), ('cp', is_cp)):
            if k not in found and f(t): found[k] = t
    return found

def special(n):
    n = n.strip().upper()
    return any(n.endswith(x) for x in (' ST', ' BT', ' XMAS', ' MINI', ' CT'))

if __name__ == '__main__':
    t = tables(os.path.join(ROOT, 'Cliente', 'Init_us.agt'))
    for k, name in (('vd', 'VehicleDisplay'), ('vl', 'VehicleList'), ('cg', 'Loja'), ('cp', 'Cupons')):
        open(os.path.join(OUT, 'carros_%s.ref' % name), 'w', encoding='utf-8', newline='').write(t[k])
    # ShopCars.json do servidor: a loja na ordem do jogo (posicao, id, nome, V)
    vd = t['vd'].split('\r\n'); h = [l for l in vd[:6] if l.startswith('Index\t')][0].split('\t'); ix = {}
    for i, x in enumerate(h): ix.setdefault(x, i)
    rows = {}
    for l in vd:
        c = l.split('\t')
        if len(c) > ix['Display'] and c[ix['Desc']] == 'PlayerCar': rows.setdefault(c[ix['Unique Id']], c)
    cg = t['cg'].split('\r\n'); hc = [l for l in cg[:6] if '\tUnique Id\t' in l][0].split('\t'); uc = hc.index('Unique Id')
    shop = [l.split('\t')[uc] for l in cg[cg.index([l for l in cg[:6] if '\tUnique Id\t' in l][0]) + 1:] if len(l.split('\t')) > uc and l.split('\t')[uc].isdigit()]
    shop = sorted([u for u in shop if u in rows], key=lambda u: int(rows[u][ix['Display']]))
    sc = [dict(pos=i + 1, id=int(u), name=rows[u][ix['UI Name']].strip(), v=int(rows[u][ix['Lvl']])) for i, u in enumerate(shop)]
    json.dump(sc, open(os.path.join(ROOT, 'Rice', 'res', 'ShopCars.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print('referencia: %d carros na loja; VehicleDisplay %d linhas, VehicleList %d linhas' % (len(shop), len(vd), len(t['vl'].split('\r\n'))))
