'use strict';
const fs = require('node:fs');
const path = require('node:path');
const asset = fs.readFileSync(path.join(__dirname,'../../UnityPart/PancakeCooker/Assets/Blinopek/Content/Gameplay.asset'),'utf8');
const section = asset.split('  balance:')[1]?.split('  bodyFont:')[0];
if (!section) throw new Error('Balance section missing');
const rules = {};
for (const line of section.split('\n')) { const m = /^    (\w+): (\d+)\s*$/.exec(line); if (m) rules[m[1]] = Number(m[2]); }
if (Object.keys(rules).length < 30) throw new Error('Incomplete export');
fs.writeFileSync(path.join(__dirname,'../Game/config.json'), JSON.stringify(rules,null,2)+'\n');
