'use strict';

const path = require('node:path');
const { loadEnvFile } = require('node:process');

function readConfig(env = process.env) {
  function required(name) {
    if (typeof env[name] !== 'string' || !env[name].trim()) {
      throw new Error(`${name} must be set`);
    }
    return env[name];
  }
  function integer(name, fallback, max) {
    const value = env[name] ?? String(fallback);
    if (!/^\d+$/.test(value) || Number(value) < 1 || Number(value) > max) {
      throw new Error(`${name} must be an integer between 1 and ${max}`);
    }
    return Number(value);
  }
  return {
    host: env.HOST || '127.0.0.1',
    port: integer('PORT', 3005, 65535),
    sessionTtlSeconds: integer('SESSION_TTL_SECONDS', 86400, 2592000),
    database: {
      host: env.DB_HOST || '127.0.0.1',
      port: integer('DB_PORT', 3306, 65535),
      user: required('DB_USER'),
      password: required('DB_PASSWORD'),
      database: required('DB_NAME'),
      connectionLimit: 5,
      minimumIdle: 0,
      acquireTimeout: 5000,
      connectTimeout: 5000,
      timezone: '+00:00'
    }
  };
}

function loadConfig() {
  try {
    loadEnvFile(path.join(__dirname, '.env'));
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  return readConfig();
}

module.exports = { readConfig, loadConfig };
