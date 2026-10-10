'use strict';

const { randomBytes, scrypt, timingSafeEqual, createHash } = require('node:crypto');
const { promisify } = require('node:util');
const deriveKey = promisify(scrypt);
const options = { N: 32768, r: 8, p: 3, maxmem: 64 * 1024 * 1024 };

async function hashPassword(password) {
  const salt = randomBytes(16).toString('hex');
  const hash = await deriveKey(password, salt, 64, options);
  return `scrypt$32768$8$3$${salt}$${hash.toString('hex')}`;
}

async function verifyPassword(password, encoded) {
  const match = /^scrypt\$32768\$8\$3\$([a-f0-9]{32})\$([a-f0-9]{128})$/.exec(encoded);
  if (!match) return false;
  const actual = await deriveKey(password, match[1], 64, options);
  return timingSafeEqual(actual, Buffer.from(match[2], 'hex'));
}

function hashToken(token) {
  return createHash('sha256').update(token).digest('hex');
}

function newToken() {
  return randomBytes(32).toString('hex');
}

module.exports = { hashPassword, verifyPassword, hashToken, newToken };
