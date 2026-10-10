'use strict';

const express = require('express');
const { hashPassword, verifyPassword, hashToken, newToken } = require('./auth');

const asyncRoute = (handler) => (req, res, next) => {
  Promise.resolve(handler(req, res, next)).catch(next);
};

function createApp({ store, sessionTtlSeconds = 86400, logger = console }) {
  const app = express();
  app.disable('x-powered-by');
  app.disable('etag');
  app.use((req, res, next) => {
    res.set('Cache-Control', 'no-store');
    next();
  });
  app.use(express.json({ limit: '16kb' }));

  const fail = (res, status, code, message) => res.status(status).json({ error: { code, message } });
  const publicUser = (user) => ({ id: user.id, login: user.login });

  function credentials(req, res, next) {
    if (!req.is('application/json')) {
      return fail(res, 415, 'JSON_REQUIRED', 'Use Content-Type: application/json');
    }
    const { login, password } = req.body || {};
    if (typeof login !== 'string' || !/^[a-zA-Z0-9_]{3,32}$/.test(login) ||
        typeof password !== 'string' || password.length < 8 || password.length > 128) {
      return fail(res, 400, 'INVALID_CREDENTIALS_FORMAT',
        'Login: 3-32 Latin letters, digits or underscores; password: 8-128 characters');
    }
    req.credentials = { login: login.toLowerCase(), password };
    next();
  }

  const authenticated = asyncRoute(async (req, res, next) => {
    const match = /^Bearer ([a-f0-9]{64})$/i.exec(req.get('Authorization') || '');
    if (!match) return fail(res, 401, 'UNAUTHORIZED', 'A valid Bearer token is required');
    // Only the scheme is case-insensitive; token bytes are not normalized.
    req.tokenHash = hashToken(match[1]);
    const user = await store.findUserByToken(req.tokenHash);
    if (!user) return fail(res, 401, 'UNAUTHORIZED', 'Token is invalid or expired');
    req.user = publicUser(user);
    next();
  });

  // Liveness only: deliberately does not query the database.
  app.get('/health', (req, res) => res.json({ status: 'ok' }));

  app.post('/auth/register', credentials, asyncRoute(async (req, res) => {
    const { login, password } = req.credentials;
    const passwordHash = await hashPassword(password);
    try {
      const user = await store.createUser(login, passwordHash);
      res.status(201).json({ user: publicUser(user) });
    } catch (error) {
      if (error.code === 'ER_DUP_ENTRY') {
        return fail(res, 409, 'LOGIN_TAKEN', 'Login is already registered');
      }
      throw error;
    }
  }));

  app.post('/auth/login', credentials, asyncRoute(async (req, res) => {
    const { login, password } = req.credentials;
    const user = await store.findUserByLogin(login);
    if (!user || !(await verifyPassword(password, user.password_hash))) {
      return fail(res, 401, 'INVALID_CREDENTIALS', 'Incorrect login or password');
    }
    const token = newToken();
    const expiresAt = new Date(Date.now() + sessionTtlSeconds * 1000);
    await store.createSession(user.id, hashToken(token), expiresAt);
    res.json({ user: publicUser(user), token, expiresAt: expiresAt.toISOString() });
  }));

  app.get('/auth/me', authenticated, (req, res) => res.json({ user: req.user }));
  app.post('/auth/logout', authenticated, asyncRoute(async (req, res) => {
    await store.deleteSession(req.tokenHash);
    res.json({ status: 'ok' });
  }));

  app.use((req, res) => fail(res, 404, 'NOT_FOUND', 'Route not found'));
  app.use((error, req, res, next) => {
    if (res.headersSent) return next(error);
    if (error.type === 'entity.parse.failed') {
      return fail(res, 400, 'INVALID_JSON', 'Malformed JSON body');
    }
    if (error.type === 'entity.too.large') {
      return fail(res, 413, 'BODY_TOO_LARGE', 'Request body exceeds 16 KB');
    }
    if (error.status === 415) {
      return fail(res, 415, 'UNSUPPORTED_ENCODING', 'Unsupported request encoding');
    }
    // Do not log database messages: they can contain SQL, parameters or credentials.
    logger.error('Request failed', { code: error.code || 'INTERNAL_ERROR' });
    return fail(res, 500, 'INTERNAL_ERROR', 'An internal server error occurred');
  });
  return app;
}

module.exports = { createApp };
