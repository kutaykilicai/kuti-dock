const [,, id, file] = process.argv;
const js = require('fs').readFileSync(file,'utf8');
const ws = new WebSocket(`ws://127.0.0.1:9333/devtools/page/${id}`);
ws.onopen=()=>ws.send(JSON.stringify({id:1,method:'Runtime.evaluate',params:{expression:js,returnByValue:true}}));
ws.onmessage=e=>{const m=JSON.parse(e.data); if(m.id===1){console.log(m.result.result.value ?? JSON.stringify(m.result)); process.exit(0)}};
