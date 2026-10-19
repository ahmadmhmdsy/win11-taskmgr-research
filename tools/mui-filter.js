const fs=require('fs');
const t=fs.readFileSync('E:\\rea\\taskmanager\\tools\\taskmgr-mui.txt','utf8').split('\n');
const i=t.findIndex(l=>l.startsWith('===== STRINGTABLE'));
const rows=t.slice(i+2).filter(l=>l.includes('\t')).map(l=>{const k=l.indexOf('\t');return [l.slice(0,k),l.slice(k+1)];});
console.log('strings',rows.length);
const keep=rows.filter(([id,s])=>/[A-Za-z]{2}/.test(s)&&s.length<70&&!/^%|\n/.test(s));
fs.writeFileSync('E:\\rea\\taskmanager\\tools\\mui-ui-strings.txt',keep.map(r=>r[0]+'\t'+r[1]).join('\n'),'utf8');
console.log('kept',keep.length);
console.log(keep.slice(0,120).map(r=>r[0]+'\t'+r[1]).join('\n'));
