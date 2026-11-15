import json,subprocess,sys
p=subprocess.Popen(["npx","-y","rea-agents@3.1.0","mcp"],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
def send(m): p.stdin.write(json.dumps(m)+"\n"); p.stdin.flush()
def recv(i):
    while True:
        l=p.stdout.readline()
        m=json.loads(l)
        if m.get("id")==i: return m
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"x","version":"1"}}});recv(1)
send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}})
r=recv(2); json.dump(r,open('tools.json','w'))
for t in r['result']['tools']:
  if t['name'] in sys.argv[1:]:
    print('=====',t['name']); print(t['description']); print(json.dumps(t['inputSchema'])[:2500])
p.kill()
