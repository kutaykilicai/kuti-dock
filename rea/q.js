(()=>{const out=[];const els=[...document.querySelectorAll('*')].filter(e=>{const r=e.getBoundingClientRect();return r.top>735&&r.height>20&&r.width>20&&r.height<90});
for(const e of els.slice(0,80)){const r=e.getBoundingClientRect(),s=getComputedStyle(e);out.push([e.tagName,(typeof e.className==='string'?e.className:'').slice(0,50),Math.round(r.left),Math.round(r.top),Math.round(r.width),Math.round(r.height),s.borderRadius,s.backgroundColor,s.backdropFilter,s.borderTop,s.boxShadow.slice(0,90),s.fontFamily.slice(0,30),s.fontSize,s.fontWeight].join(' | '))}
return out.join('\n')})()
