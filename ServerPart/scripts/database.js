'use strict';

// Project-local Windows development database. Never manages system services.
const fs = require('node:fs');
const fsp = require('node:fs/promises');
const path = require('node:path');
const net = require('node:net');
const { randomBytes, createHash } = require('node:crypto');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const { pipeline } = require('node:stream/promises');
const { parseEnv } = require('node:util');
const mariadb = require('mariadb');

const root = path.resolve(__dirname, '../..');
const local = path.join(root, '.local/database');
const data = path.join(local, 'data');
const stateFile = path.join(local, 'credentials.json');
const ini = path.join(local, 'server.ini');
const envFile = path.join(root, 'ServerPart/.env');
const version = '11.8.9';
const archiveName = `mariadb-${version}-winx64.zip`;
const archive = path.join(local, archiveName);
const runtime = path.join(local, `mariadb-${version}-winx64`);
const bin = name => path.join(runtime, 'bin', `${name}.exe`);
const checksum = '830c46727d9278eae212ae3eca44eeb9e71b2a68704e95f344a64fba7b1963f5';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const sqlPath = value => value.replaceAll('\\', '/');

async function run(executable, args, options = {}) {
  const child = spawn(executable, args, { windowsHide: true, stdio: 'inherit', ...options });
  const [code] = await once(child, 'exit');
  if (code !== 0) throw new Error(`${path.basename(executable)} exited with code ${code}`);
}

async function download() {
  if (fs.existsSync(bin('mariadbd'))) return;
  await fsp.mkdir(local, { recursive: true });
  if (!fs.existsSync(archive)) {
    console.log(`Downloading MariaDB ${version} from the official archive...`);
    const response = await fetch(`https://archive.mariadb.org/mariadb-${version}/winx64-packages/${archiveName}`);
    if (!response.ok) throw new Error(`Download failed: HTTP ${response.status}`);
    const temporary = `${archive}.partial`;
    await pipeline(response.body, fs.createWriteStream(temporary));
    await fsp.rename(temporary, archive);
  }
  const hash = createHash('sha256');
  for await (const chunk of fs.createReadStream(archive)) hash.update(chunk);
  if (hash.digest('hex') !== checksum) throw new Error(`SHA-256 mismatch. Remove only ${archive} and retry.`);
  console.log('SHA-256 verified. Extracting MariaDB...');
  const quote = value => "'" + value.replaceAll("'", "''") + "'";
  await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
    `$ErrorActionPreference = 'Stop'; Expand-Archive -LiteralPath ${quote(archive)} -DestinationPath ${quote(local)} -Force`]);
}

function readState() {
  if (!fs.existsSync(stateFile)) throw new Error('Database is not set up. Run npm run db:setup first.');
  return JSON.parse(fs.readFileSync(stateFile, 'utf8'));
}

function connectionOptions(state) {
  return { host: '127.0.0.1', port: state.port, user: 'root', password: state.rootPassword,
    connectTimeout: 1000, timezone: '+00:00' };
}

async function admin(state) {
  const connection = await mariadb.createConnection(connectionOptions(state));
  const [row] = await connection.query('SELECT @@datadir AS directory');
  const normalize = value => path.resolve(value).toLowerCase().replace(/[\\/]+$/, '');
  if (normalize(row.directory) !== normalize(data)) {
    await connection.end();
    throw new Error('Port belongs to a different database instance. Refusing to manage it.');
  }
  return connection;
}

async function portOpen(port) {
  return new Promise(resolve => {
    const socket = net.connect({ host: '127.0.0.1', port });
    let finished = false;
    const finish = value => { if (!finished) { finished = true; socket.destroy(); resolve(value); } };
    socket.setTimeout(500, () => finish(false));
    socket.once('connect', () => finish(true));
    socket.once('error', () => finish(false));
  });
}

async function start(state) {
  if (await portOpen(state.port)) {
    const connection = await admin(state);
    await connection.end();
    console.log(`Database already running on 127.0.0.1:${state.port}`);
    return;
  }
  if (!fs.existsSync(path.join(data, 'mysql'))) throw new Error('Database files are missing. Run db:setup.');
  // Generate absolute paths again so a stopped instance can be moved with the project.
  await fsp.writeFile(ini, `[mysqld]\nbasedir=${sqlPath(runtime)}\ndatadir=${sqlPath(data)}\nport=${state.port}\nbind-address=127.0.0.1\nlog-error=${sqlPath(path.join(local, 'server.log'))}\npid-file=${sqlPath(path.join(local, 'server.pid'))}\ncharacter-set-server=utf8mb4\n`, 'utf8');
  const child = spawn(bin('mariadbd'), [`--defaults-file=${ini}`], {
    windowsHide: true, detached: true, stdio: 'ignore'
  });
  await once(child, 'spawn');
  child.unref();
  for (let attempt = 0; attempt < 80; attempt++) {
    if (child.exitCode !== null) break;
    try {
      const connection = await admin(state);
      await connection.end();
      console.log(`Database running on 127.0.0.1:${state.port}`);
      return;
    } catch {
      await delay(250);
    }
  }
  throw new Error(`Database failed to start. Check ${path.join(local, 'server.log')}`);
}

function checkEnv(state) {
  if (!fs.existsSync(envFile)) return;
  const env = parseEnv(fs.readFileSync(envFile, 'utf8'));
  if (env.DB_HOST !== '127.0.0.1' || Number(env.DB_PORT) !== state.port ||
      env.DB_NAME !== 'mobiledeath' || env.DB_USER !== 'mobiledeath_app' || env.DB_PASSWORD !== state.appPassword) {
    throw new Error('ServerPart/.env points to a different database. It was not changed. Back it up and configure this instance explicitly.');
  }
}

async function migrateUserProfile(connection) {
  const columns = await connection.query('SHOW COLUMNS FROM users');
  const names = new Set(columns.map(column => column.Field));
  const added = ['email', 'phone', 'balance'].filter(name => names.has(name));
  if (added.length === 3) return;
  if (added.length !== 0) throw new Error('Partial user profile schema found. Inspect it before applying migration 001.');
  const [row] = await connection.query('SELECT COUNT(*) AS count FROM users');
  if (Number(row.count) !== 0) {
    throw new Error('Existing users need real email addresses. Migration 001 only upgrades an empty users table; no records were changed.');
  }
  const migration = await fsp.readFile(path.join(root, 'ServerPart/migrations/001-user-profile.sql'), 'utf8');
  await connection.query(migration);
  console.log('Applied migration 001: email, optional phone and balance.');
}

async function setup() {
  if (process.platform !== 'win32' || process.arch !== 'x64') throw new Error('Automatic installation supports Windows x64 only. See docs/DATABASE_SETUP.md.');
  if (!fs.existsSync(stateFile) && fs.existsSync(envFile)) {
    throw new Error('Existing ServerPart/.env found. Refusing to replace it; see docs/DATABASE_SETUP.md.');
  }
  if (!fs.existsSync(stateFile) && fs.existsSync(data)) {
    throw new Error('Existing data directory without credentials. Refusing to initialize it.');
  }
  await download();
  let state;
  if (fs.existsSync(stateFile)) {
    state = readState();
  } else {
    const port = Number(process.env.LOCAL_DB_PORT || 3307);
    if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('LOCAL_DB_PORT must be 1024-65535');
    if (await portOpen(port)) throw new Error(`Port ${port} is occupied; choose another LOCAL_DB_PORT before setup.`);
    state = { version, port, rootPassword: randomBytes(32).toString('hex'), appPassword: randomBytes(32).toString('hex'), initialized: false };
    await fsp.writeFile(stateFile, JSON.stringify(state, null, 2), { flag: 'wx', mode: 0o600 });
  }
  checkEnv(state);
  if (!fs.existsSync(path.join(data, 'mysql'))) {
    if (fs.existsSync(data)) throw new Error('Partial data directory exists. Inspect it before retrying; it was not removed.');
    console.log('Initializing a new local database...');
    // Arguments are passed directly, without shell interpolation or printing credentials.
    await run(bin('mariadb-install-db'), [`--datadir=${data}`, `--password=${state.rootPassword}`, `--port=${state.port}`, '--silent']);
  }
  await start(state);
  const connection = await admin(state);
  try {
    await connection.query('CREATE DATABASE IF NOT EXISTS mobiledeath CHARACTER SET utf8mb4');
    await connection.query('USE mobiledeath');
    const tables = await connection.query('SHOW TABLES');
    if (!state.initialized && tables.length !== 0) {
      throw new Error('Database already contains tables; refusing to apply the initial schema.');
    }
    if (!state.initialized) {
      const schema = await fsp.readFile(path.join(root, 'ServerPart/schema.sql'), 'utf8');
      for (const statement of schema.split(';').map(value => value.trim()).filter(Boolean)) {
        await connection.query(statement);
      }
      await connection.query("CREATE USER 'mobiledeath_app'@'127.0.0.1' IDENTIFIED BY ?", [state.appPassword]);
      await connection.query("GRANT SELECT, INSERT, DELETE ON mobiledeath.* TO 'mobiledeath_app'@'127.0.0.1'");
      state.initialized = true;
      await fsp.writeFile(stateFile, JSON.stringify(state, null, 2), { mode: 0o600 });
    }
    await migrateUserProfile(connection);
    await connection.query('SELECT id, login, password_hash, email, phone, balance FROM users LIMIT 0');
    await connection.query('SELECT token_hash, user_id, expires_at FROM sessions LIMIT 0');
  } finally {
    await connection.end();
  }
  if (!fs.existsSync(envFile)) {
    await fsp.writeFile(envFile, `HOST=127.0.0.1\nPORT=3005\nDB_HOST=127.0.0.1\nDB_PORT=${state.port}\nDB_NAME=mobiledeath\nDB_USER=mobiledeath_app\nDB_PASSWORD=${state.appPassword}\nSESSION_TTL_SECONDS=86400\n`, { flag: 'wx', mode: 0o600 });
  }
  const app = await mariadb.createConnection({ ...connectionOptions(state), user: 'mobiledeath_app', password: state.appPassword, database: 'mobiledeath' });
  try { await app.query('SELECT id FROM users LIMIT 0'); } finally { await app.end(); }
  console.log('Setup complete. ServerPart/.env is configured; database is running.');
}

async function main() {
  const command = process.argv[2];
  if (command === 'setup') return setup();
  const state = readState();
  if (command === 'start') return start(state);
  if (command === 'migrate') {
    await start(state);
    const connection = await admin(state);
    try {
      await connection.query('USE mobiledeath');
      await migrateUserProfile(connection);
      console.log('User profile schema is up to date.');
    } finally { await connection.end(); }
    return;
  }
  if (command === 'status') {
    if (!(await portOpen(state.port))) { console.log('Database is stopped.'); return; }
    const connection = await admin(state);
    try {
      const [row] = await connection.query('SELECT VERSION() AS version');
      console.log(`MariaDB ${row.version} is running at 127.0.0.1:${state.port}; database: mobiledeath`);
    } finally { await connection.end(); }
    return;
  }
  if (command === 'stop') {
    if (!(await portOpen(state.port))) { console.log('Database already stopped.'); return; }
    const connection = await admin(state);
    try { await connection.query('SHUTDOWN'); } finally { await connection.end(); }
    for (let i = 0; i < 80 && await portOpen(state.port); i++) await delay(250);
    if (await portOpen(state.port)) throw new Error('Database has not stopped yet. Check server.log.');
    console.log('Database stopped safely; data preserved.');
    return;
  }
  if (command === 'backup') {
    const connection = await admin(state);
    await connection.end();
    const backupDir = path.join(root, '.local/backups');
    await fsp.mkdir(backupDir, { recursive: true });
    const filename = path.join(backupDir, `mobiledeath-${new Date().toISOString().replaceAll(':', '-')}.sql`);
    const clientIni = path.join(local, 'backup-client.ini');
    await fsp.writeFile(clientIni, `[client]\nuser=root\npassword=${state.rootPassword}\nhost=127.0.0.1\nport=${state.port}\nprotocol=tcp\n`, { mode: 0o600 });
    try {
      await run(bin('mariadb-dump'), [`--defaults-extra-file=${clientIni}`, '--single-transaction', '--skip-lock-tables', `--result-file=${filename}`, 'mobiledeath']);
      console.log(`Backup saved: ${filename}`);
    } finally { await fsp.unlink(clientIni); }
    return;
  }
  throw new Error('Use setup, start, stop, status, backup or migrate.');
}

main().catch(error => {
  // SQL error messages can contain credentials. Never echo them.
  console.error(error.sql ? `Database command failed (${error.code}). Check local setup state.` : error.message);
  process.exitCode = 1;
});
