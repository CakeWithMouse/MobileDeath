'use strict';
const {spawnSync}=require('node:child_process');
const result=spawnSync(process.execPath,['--test','--test-isolation=none','test/database.integration.test.js'],{stdio:'inherit',windowsHide:true,env:{...process.env,GAME_DB_INTEGRATION:'1'}});
process.exitCode=result.status??1;
