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
recv(1); send({"jsonrpc":"2.0","method":"notifications/initialized"})
calls=json.load(open(sys.argv[1])); res=[]
for n,(name,args) in enumerate(calls):
    send({"jsonrpc":"2.0","id":10+n,"method":"tools/call","params":{"name":name,"arguments":args}})
    r=recv(10+n); res.append({"tool":name,"args":args,"resp":r})
json.dump(res,open(sys.argv[2],"w"),indent=1)
p.kill()
