'use strict';

const mariadb = require('mariadb');

function createStore(pool) {
  return {
    async createUser(login, passwordHash, email, phone) {
      const result = await pool.query(
        'INSERT INTO users (login, password_hash, email, phone) VALUES (?, ?, ?, ?)',
        [login, passwordHash, email, phone]
      );
      return { id: Number(result.insertId), login, email, phone, balance: '0.00' };
    },
    async findUserByLogin(login) {
      const rows = await pool.query(
        'SELECT id, login, password_hash, email, phone, balance FROM users WHERE login = ?', [login]
      );
      return rows[0];
    },
    async createSession(userId, tokenHash, expiresAt) {
      await pool.query(
        'INSERT INTO sessions (token_hash, user_id, expires_at) VALUES (?, ?, ?)',
        [tokenHash, userId, expiresAt]
      );
    },
    async findUserByToken(tokenHash) {
      const rows = await pool.query(
        `SELECT u.id, u.login, u.email, u.phone, u.balance FROM sessions s JOIN users u ON u.id = s.user_id
         WHERE s.token_hash = ? AND s.expires_at > UTC_TIMESTAMP(3)`, [tokenHash]
      );
      return rows[0];
    },
    async deleteSession(tokenHash) {
      await pool.query('DELETE FROM sessions WHERE token_hash = ?', [tokenHash]);
    }
  };
}

function createDatabase(config) {
  const pool = mariadb.createPool(config);
  return { store: createStore(pool), close: () => pool.end() };
}

module.exports = { createDatabase, createStore };
