'use strict';

const { loadConfig } = require('./config');
const { createDatabase } = require('./DataBase/DataBase');
const { createApp } = require('./app');

function start() {
  const config = loadConfig();
  const database = createDatabase(config.database);
  const app = createApp({ store: database.store, sessionTtlSeconds: config.sessionTtlSeconds });
  const server = app.listen(config.port, config.host, () => {
    console.log(`MobileDeath API listening on http://${config.host}:${config.port}`);
  });
  server.on('error', async (error) => {
    console.error('Server failed to listen:', error.code);
    process.exitCode = 1;
    try {
      await database.close();
    } catch {
      console.error('Failed to close database pool');
    }
  });
  let stopping = false;
  function stop() {
    if (stopping) return;
    stopping = true;
    const deadline = setTimeout(() => process.exit(1), 10000);
    deadline.unref();
    server.close(async () => {
      try {
        await database.close();
      } catch {
        process.exitCode = 1;
      } finally {
        clearTimeout(deadline);
      }
    });
  }
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);
  return server;
}

if (require.main === module) {
  try {
    start();
  } catch (error) {
    console.error('Startup failed:', error.message);
    process.exitCode = 1;
  }
}

module.exports = { start };
