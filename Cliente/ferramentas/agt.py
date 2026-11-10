import zlib,struct,sys,os
KEY=open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'key90b.bin'),'rb').read()
def xor(buf,start):  # start = absolute file offset of buf[0]
    return bytes(b^KEY[(start+i-20)%90] for i,b in enumerate(buf))
def parse(d):
    assert d[:8]==b'NayaPack'
    count=struct.unpack_from('<I',d,16)[0]
    p=44; ents=[]
    for e in range(count):
        L=struct.unpack('<I',xor(d[p:p+4],p))[0]
        name=xor(d[p+4:p+4+L],p+4).decode('latin-1')
        off,nch,usize=struct.unpack('<III',xor(d[p+4+L:p+4+L+12],p+4+L))
        if off+2*nch>len(d) or not name.isprintable(): break
        ents.append(dict(name=name,off=off,nch=nch,usize=usize,dirpos=p))
        p+=4+L+12
    return ents,p
def read_file(d,e):
    tab=struct.unpack('<%dH'%e['nch'],xor(d[e['off']:e['off']+2*e['nch']],e['off']))
    p=e['off']+2*e['nch']; out=b''
    for n in tab:
        out+=zlib.decompress(xor(d[p:p+n],p)); p+=n
    return out,p
if __name__=='__main__':
    d=open(sys.argv[1],'rb').read()
    ents,dend=parse(d)
    print('dir end',dend,'hdr20-44',xor(d[20:44],20))
    os.makedirs('out',exist_ok=True)
    for e in ents:
        data,end=read_file(d,e)
        ok=len(data)==e['usize']
        print(e['name'],e['off'],e['nch'],e['usize'],ok,end)
        open('out/'+e['name'].replace('\\','_'),'wb').write(data)
