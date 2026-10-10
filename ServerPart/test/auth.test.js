'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const { once } = require('node:events');
const { createApp } = require('../app');
const { hashPassword, verifyPassword, hashToken } = require('../auth');
const { readConfig } = require('../config');
const { createStore } = require('../DataBase/DataBase');

// HTTP/crypto tests with a deliberately in-memory store, not a MariaDB substitute.
function memoryStore() {
  const users = new Map();
  const sessions = new Map();
  return {
    users, sessions,
    async createUser(login, password_hash, email, phone) {
      if (users.has(login) || [...users.values()].some(user => user.email === email)) throw Object.assign(new Error('duplicate'), { code: 'ER_DUP_ENTRY' });
      const user = { id: users.size + 1, login, password_hash, email, phone, balance: '0.00' };
      users.set(login, user);
      return user;
    },
    async findUserByLogin(login) { return users.get(login); },
    async createSession(userId, tokenHash, expiresAt) {
      sessions.set(tokenHash, { userId, expiresAt });
    },
    async findUserByToken(tokenHash) {
      const session = sessions.get(tokenHash);
      if (!session || session.expiresAt <= new Date()) return undefined;
      return [...users.values()].find(user => user.id === session.userId);
    },
    async deleteSession(tokenHash) { sessions.delete(tokenHash); }
  };
}

async function fixture(t, store = memoryStore()) {
  const server = createApp({ store, logger: { error() {} } }).listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => new Promise(resolve => server.close(resolve)));
  const url = `http://127.0.0.1:${server.address().port}`;
  async function request(path, { method = 'GET', body, token, raw, headers = {} } = {}) {
    const response = await fetch(url + path, {
      method,
      headers: {
        ...(body !== undefined || raw !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers
      },
      body: raw ?? (body === undefined ? undefined : JSON.stringify(body))
    });
    assert.match(response.headers.get('content-type'), /application\/json/);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    return { status: response.status, data: await response.json() };
  }
  return { store, request };
}

const credentials = { login: 'Player_1', password: 'example-password-123', email: 'player@example.com' };

test('registration, login, user isolation, expiry and logout over HTTP', async t => {
  const { request, store } = await fixture(t);
  const registered = await request('/auth/register', { method: 'POST', body: credentials });
  assert.equal(registered.status, 201);
  assert.deepEqual(registered.data, { user: { id: 1, login: 'player_1', email: credentials.email, phone: null, balance: '0.00' } });
  assert.notEqual(store.users.get('player_1').password_hash, credentials.password);
  assert.equal((await request('/auth/register', { method: 'POST', body: credentials })).status, 409);
  assert.equal((await request('/auth/login', { method: 'POST', body: { ...credentials, password: 'wrong-password' } })).status, 401);
  assert.equal((await request('/auth/login', { method: 'POST', body: { ...credentials, login: 'missing' } })).status, 401);
  const login = await request('/auth/login', { method: 'POST', body: credentials });
  assert.equal(login.status, 200);
  assert.deepEqual(login.data.user, registered.data.user);
  assert.match(login.data.token, /^[a-f0-9]{64}$/);
  assert.ok(new Date(login.data.expiresAt) > new Date());
  assert.ok(store.sessions.has(hashToken(login.data.token)));
  assert.ok(!store.sessions.has(login.data.token));
  const token = login.data.token;
  assert.deepEqual((await request('/auth/me', { token })).data, registered.data);
  assert.equal((await request('/auth/me')).status, 401);
  assert.equal((await request('/auth/me', { token: '0'.repeat(64) })).status, 401);
  assert.equal((await request('/auth/me', { token: 'malformed' })).status, 401);
  await request('/auth/register', { method: 'POST', body: { ...credentials, login: 'other', email: 'other@example.com' } });
  const second = await request('/auth/login', { method: 'POST', body: { ...credentials, login: 'other', email: 'other@example.com' } });
  const parallel = await Promise.all([
    request('/auth/me?ID=2', { token }), request('/auth/me', { token: second.data.token })
  ]);
  assert.equal(parallel[0].data.user.id, 1);
  assert.equal(parallel[1].data.user.id, 2);
  const anotherLogin = await request('/auth/login', { method: 'POST', body: credentials });
  assert.notEqual(anotherLogin.data.token, token);
  store.sessions.get(hashToken(anotherLogin.data.token)).expiresAt = new Date(0);
  assert.equal((await request('/auth/me', { token: anotherLogin.data.token })).status, 401);
  assert.equal((await request('/auth/logout', { method: 'POST', token })).status, 200);
  assert.equal((await request('/auth/me', { token })).status, 401);
  assert.equal((await request('/auth/logout', { method: 'POST', token })).status, 401);
  assert.equal((await request('/auth/me', { token: second.data.token })).status, 200);
});

test('validation and unknown routes return JSON errors', async t => {
  const { request } = await fixture(t);
  assert.equal((await request('/health')).status, 200);
  assert.equal((await request('/unknown')).status, 404);
  assert.equal((await request('/auth/register', { method: 'POST', body: { login: "x' OR 1=1", password: credentials.password } })).status, 400);
  assert.equal((await request('/auth/register', { method: 'POST', body: { login: 'abc', password: 'short' } })).status, 400);
  assert.equal((await request('/auth/register', { method: 'POST', body: [] })).status, 400);
  assert.equal((await request('/auth/login', { method: 'POST', raw: '{bad' })).status, 400);
  assert.equal((await request('/auth/login', { method: 'POST' })).status, 415);
  assert.equal((await request('/auth/login', { method: 'POST', raw: 'x'.repeat(17000) })).status, 413);
});

test('async database failure is JSON and does not expose internals', async t => {
  const { request } = await fixture(t, {
    async findUserByLogin() { throw new Error('private SQL details'); }
  });
  const result = await request('/auth/login', { method: 'POST', body: credentials });
  assert.equal(result.status, 500);
  assert.deepEqual(result.data, { error: { code: 'INTERNAL_ERROR', message: 'An internal server error occurred' } });
});

test('password hashes use independent salts and reject wrong passwords', async () => {
  const first = await hashPassword(credentials.password);
  const second = await hashPassword(credentials.password);
  assert.notEqual(first, second);
  assert.ok(await verifyPassword(credentials.password, first));
  assert.equal(await verifyPassword('another-password', first), false);
  assert.equal(await verifyPassword(credentials.password, 'invalid'), false);
});

test('configuration requires secrets and validates numeric bounds', () => {
  const env = { DB_USER: 'test', DB_PASSWORD: 'test-only', DB_NAME: 'test' };
  assert.equal(readConfig(env).port, 3005);
  assert.throws(() => readConfig({}), /DB_USER/);
  assert.throws(() => readConfig({ ...env, DB_PASSWORD: '' }), /DB_PASSWORD/);
  assert.throws(() => readConfig({ ...env, PORT: '0' }), /PORT/);
  assert.throws(() => readConfig({ ...env, DB_PORT: '3306bad' }), /DB_PORT/);
  assert.throws(() => readConfig({ ...env, SESSION_TTL_SECONDS: '-1' }), /SESSION_TTL_SECONDS/);
});

test('SQL store binds untrusted values rather than interpolating them', async () => {
  const calls = [];
  const store = createStore({ async query(sql, values) {
    calls.push({ sql, values });
    return sql.startsWith('INSERT INTO users') ? { insertId: 1n } : [];
  } });
  const hostile = "test' OR 1=1 --";
  const expiry = new Date();
  assert.deepEqual(await store.createUser(hostile, 'hash', 'test@example.com', null), { id: 1, login: hostile, email: 'test@example.com', phone: null, balance: '0.00' });
  await store.findUserByLogin(hostile);
  await store.createSession(1, hostile, expiry);
  await store.findUserByToken(hostile);
  await store.deleteSession(hostile);
  for (const { sql, values } of calls) {
    assert.ok(!sql.includes(hostile));
    assert.equal((sql.match(/\?/g) || []).length, values.length);
    assert.ok(values.includes(hostile));
  }
});


test('registration requires email, normalizes contact fields and protects balance', async t => {
  const { request, store } = await fixture(t);
  const register = body => request('/auth/register', { method: 'POST', body });
  assert.equal((await register({ login: 'first', password: credentials.password })).status, 400);
  for (const email of ['bad', 'a@b', '.a@example.com', 'a..b@example.com', 'a@-bad.com', 42]) {
    assert.equal((await register({ ...credentials, email })).status, 400);
  }
  for (const phone of [1234567, 'abc', '+123', '+1234567890123456']) {
    assert.equal((await register({ ...credentials, phone })).status, 400);
  }
  const blocked = await register({ ...credentials, balance: '100.00' });
  assert.equal(blocked.status, 400);
  assert.equal(blocked.data.error.code, 'BALANCE_READ_ONLY');
  assert.equal(store.users.size, 0);
  const created = await register({ ...credentials, email: ' Player@Example.COM ', phone: '+7 (999) 123-45-67' });
  assert.equal(created.status, 201);
  assert.deepEqual(created.data.user, { id: 1, login: 'player_1', email: 'player@example.com', phone: '+79991234567', balance: '0.00' });
  assert.equal((await register({ ...credentials, login: 'another' })).status, 409);
  const login = await request('/auth/login', { method: 'POST', body: { login: credentials.login, password: credentials.password } });
  assert.equal(login.status, 200);
  assert.deepEqual(login.data.user, created.data.user);
  assert.deepEqual((await request('/auth/me', { token: login.data.token })).data.user, created.data.user);
  assert.equal('password' in created.data.user, false);
  assert.equal('password_hash' in created.data.user, false);
  assert.equal((await register({ ...credentials, login: 'no_phone', email: 'no_phone@example.com', phone: null })).data.user.phone, null);
});
