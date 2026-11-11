'use strict';
const { spawn } = require('node:child_process');
const path = require('node:path');
class SimulationWorker {
  constructor() { this.running = 0; }
  replay(input) {
    if (this.running >= 2) return Promise.reject(Object.assign(new Error('Worker busy'), { status: 503, code: 'WORKER_BUSY' }));
    this.running++;
    return new Promise((resolve, reject) => {
      const dll = process.env.SIMULATION_WORKER_DLL || path.join(__dirname, '../SimulationWorker/bin/Release/net10.0/SimulationWorker.dll');
      const child = spawn('dotnet', [dll], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
      let output = '', done = false;
      const finish = (error, result) => { if (done) return; done = true; clearTimeout(timer); this.running--; child.kill(); error ? reject(error) : resolve(result); };
      const unavailable = () => Object.assign(new Error('Simulation unavailable'), { status: 503, code: 'WORKER_UNAVAILABLE' });
      const timer = setTimeout(() => finish(unavailable()), 15000);
      child.on('error', () => finish(unavailable()));
      child.stdin.on('error', () => finish(unavailable()));
      child.stderr.on('data', () => {});
      child.stdout.on('data', data => { output += data; if (output.length > 65536) finish(unavailable()); });
      child.on('close', code => { if (done) return; try { if (code !== 0) throw new Error(); const result = JSON.parse(output); if (typeof result.ok !== 'boolean') throw new Error(); finish(null, result); } catch { finish(unavailable()); } });
      child.stdin.end(JSON.stringify(input));
    });
  }
}
module.exports = { SimulationWorker };
