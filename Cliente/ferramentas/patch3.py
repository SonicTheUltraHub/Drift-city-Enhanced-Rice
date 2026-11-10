import agt,zlib,struct,sys,collections,json
src,dst,reqf,outjson=sys.argv[1],sys.argv[2],sys.argv[3],sys.argv[4]
d=bytearray(open(src,'rb').read())
ents,_=agt.parse(bytes(d))
byn={e['name']:i for i,e in enumerate(ents)}
def load(n):
    e=ents[byn['Init\\'+n]]; raw,_=agt.read_file(bytes(d),e); return raw.decode('utf-16').split('\r\n')
def store(n,lines):
    i=byn['Init\\'+n]; e=ents[i]; nxt=ents[i+1]['off'] if i+1<len(ents) else len(d)
    new='\r\n'.join(lines).encode('utf-16')
    chunks=[zlib.compress(new[k:k+16384],9) for k in range(0,len(new),16384)]
    tab=struct.pack('<%dH'%len(chunks),*[len(c) for c in chunks])
    size=len(tab)+sum(map(len,chunks))
    print(n,'novo',size,'espaco',nxt-e['off'])
    assert size<=nxt-e['off'], 'nao cabe'
    pos=e['off']
    for part in [tab]+chunks:
        d[pos:pos+len(part)]=agt.xor(part,pos); pos+=len(part)
    p=e['dirpos']+4+len(e['name'])
    d[p:p+12]=agt.xor(struct.pack('<III',e['off'],len(chunks),len(new)),p)
    return new
def special(n):
    n=n.strip().upper(); return n.endswith(' ST') or n.endswith(' BT') or n.endswith(' XMAS') or n.endswith(' MINI')

req=[l.rstrip('\n').split('\t') for l in open(reqf,encoding='utf-8') if l.strip()]
STD={1:160,2:190,3:230,4:290,5:360,6:440,7:540,8:660,9:800}
def grades(q):
    v=int(q[1]); s=[int(x) for x in q[2:6]]   # speed, accel, crash, boost
    if v<=3:
        f=STD[v]/sum(s); s=[max(1,round(x*f)) for x in s]
    return {g:[round(x*STD[g]/STD[v]) for x in s] for g in range(v,10)}
reqmap={r[0].lower():r for r in req}
GR={k:grades(q) for k,q in reqmap.items()}

vd=load('VehicleDisplay.xlt'); hdr=vd[3].split('\t'); ix={}
for i,h in enumerate(hdr): ix.setdefault(h,i)
unlocked=set(); report=[]; newlvl={}; idbyname={}
for li in range(4,len(vd)):
    r=vd[li].split('\t')
    if len(r)<30 or r[ix['Desc']]!='PlayerCar': continue
    u=r[ix['Unique Id']]; nm=r[ix['UI Name']].strip(); sell=r[ix['Sellable']]; cond=r[ix['Req Cond']]
    want = (cond!='N/A' and sell in ('0','50','101','1')) or (sell in ('50','101')) or nm.lower() in reqmap
    if sell.endswith('0001'): want=False   # carros iniciais
    if want:
        if sell!='1' or cond!='N/A': report.append((u,nm,sell,cond,'especial' if special(nm) else ''))
        r[ix['Sellable']]='1'; r[ix['Req Cond']]='N/A'; unlocked.add(u)
    q=reqmap.get(nm.lower())
    if q and nm.lower() not in idbyname:
        idbyname[nm.lower()]=u
        v=q[1]; r[ix['Lvl']]=v; newlvl[u]=int(v)
        sv=GR[nm.lower()][int(v)]
        r[ix['Speed']]=str(sv[0]); r[ix['Accel']]=str(sv[1]); r[ix['Crash']]=str(sv[2]); r[ix['Boost']]=str(sv[3])
    if r[ix['Sellable']]=='1': unlocked.add(u)
    vd[li]='\t'.join(r)

vl=load('VehicleList.xlt'); flags=0; statset=[]
for li in range(3,len(vl)):
    r=vl[li].split('\t')
    if len(r)<20 or not r[1].strip().isdigit(): continue
    u=r[1].strip(); g=r[19].strip()
    if u in unlocked and r[5].strip() in ('1','4'): r[5]='0'; flags+=1
    for nm,uid in idbyname.items():
        q=reqmap[nm]
        if uid==u and g.isdigit() and int(g)>=int(q[1]):
            sv=GR[nm][int(g)]
            r[7]=str(sv[0]); r[6]=str(sv[1]); r[8]=str(sv[2]); r[9]=str(sv[3])
            if g==q[1]: r[10]=q[6]
            r[17]=str(sum(sv)); r[5]='0'
            statset.append((nm,u,g))
    vl[li]='\t'.join(r)

store('VehicleDisplay.xlt',vd); store('VehicleList.xlt',vl)
open(dst,'wb').write(bytes(d))
# verificacao
d2=open(dst,'rb').read(); e2,_=agt.parse(d2)
for a,b in zip(ents,e2):
    x,_=agt.read_file(bytes(open(src,'rb').read()),a); y,_=agt.read_file(d2,b)
    if a['name'] not in ('Init\\VehicleDisplay.xlt','Init\\VehicleList.xlt'): assert x==y
print('desbloqueados',len(report),'flags',flags,'stats',len(statset),'tamanho',len(d2))
json.dump(dict(gr={k:{str(g):v for g,v in x.items()} for k,x in GR.items()},report=report,newlvl=newlvl,ids=idbyname,stats=statset,missing=[r[0] for r in req if r[0].lower() not in idbyname]),open(outjson,'w'),indent=1)
