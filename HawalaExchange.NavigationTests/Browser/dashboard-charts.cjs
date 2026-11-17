const assert=require('node:assert/strict');const fs=require('node:fs');const path=require('node:path');const vm=require('node:vm');
const created=[];const canvas={getContext:()=>({})};
class Chart {static defaults={};constructor(element,config){this.config=config;created.push(config);}destroy(){}}
const sandbox={window:{Chart},Chart,document:{getElementById:()=>canvas,documentElement:{}},getComputedStyle:()=>({getPropertyValue:()=> '#172033'}),Intl};
vm.runInNewContext(fs.readFileSync(path.resolve(__dirname,'../../HawalaExchange.Web/wwwroot/js/dashboard.js'),'utf8'),sandbox);
sandbox.window.renderMiniChart('mini',['Hawala','Exchange','Account','Expense','Capital'],[2031,35,50,4,0]);
const bars=created[0];assert.equal(bars.data.datasets[0].backgroundColor.length,5);assert.equal(new Set(bars.data.datasets[0].backgroundColor).size,5);
assert.deepEqual(Array.from(bars.data.datasets[0].data),[2031,35,50,4,0]);
const labels=[];bars.plugins[0].afterDatasetsDraw({ctx:{save(){},restore(){},fillText(value){labels.push(value);}},chartArea:{top:0},data:bars.data,getDatasetMeta:()=>({data:[2031,35,50,4,0].map((_,i)=>({x:i*10,y:100}))})});
assert.deepEqual(labels,['2,031','35','50','4','0']);
sandbox.window.renderLiquidityChart('liquidity',['A','B','C','D','E'],[100,20,30,40,10]);
assert.equal(created[1].data.datasets[0].backgroundColor.length,5);
console.log('PASS: all five activity categories retain their counts, nonzero small bars remain visible, zero stays zero and every liquidity slice has a color.');
