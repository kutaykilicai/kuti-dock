import json,subprocess,sys
p=subprocess.Popen(["npx","-y","rea-agents@3.1.0","mcp"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
def send(m): p.stdin.write(json.dumps(m)+"\n"); p.stdin.flush()
def recv(i):
    while True:
        l=p.stdout.readline()
        if not l: return None
        try: m=json.loads(l)
        except: continue
        if m.get("id")==i: return m
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"x","version":"1"}}})
recv(1)
send({"jsonrpc":"2.0","method":"notifications/initialized"})
name=sys.argv[1]; args=json.loads(open(sys.argv[2][1:]).read() if sys.argv[2].startswith("@") else sys.argv[2])
send({"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":name,"arguments":args}})
r=recv(2)
out=sys.argv[3] if len(sys.argv)>3 else None
s=json.dumps(r,indent=1)
if out: open(out,"w").write(s)
print(s[:3000])
p.kill()
