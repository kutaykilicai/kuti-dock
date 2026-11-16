[...document.querySelectorAll('img')].filter(i=>/Storage|Screenshot/.test(i.alt)).map(i=>i.alt+' '+i.currentSrc).join('\n')
