'use strict';
const decode = value => typeof value === 'string' ? JSON.parse(value) : value;
class SqlGameRepository {
  constructor(pool) { this.pool = pool; }
  async ready() { await this.pool.query('SELECT user_id FROM game_players LIMIT 1'); await this.pool.query('SELECT id FROM game_days LIMIT 1'); await this.pool.query('SELECT operation_id FROM game_operations LIMIT 1'); await this.pool.query('SELECT id FROM game_wallet_entries LIMIT 1'); }
  async transaction(userId, action) {
    const connection = await this.pool.getConnection();
    try {
      await connection.beginTransaction();
      const owners = await connection.query('SELECT id FROM users WHERE id = ? FOR UPDATE', [userId]);
      if (!owners.length) throw Object.assign(new Error('Unauthorized'), { status: 401, code: 'UNAUTHORIZED' });
      const rows = await connection.query('SELECT state_json FROM game_players WHERE user_id = ?', [userId]);
      const ctx = {
        player: rows.length ? decode(rows[0].state_json) : null,
        getDay: async id => { const rows = await connection.query('SELECT state_json FROM game_days WHERE id = ? AND user_id = ?', [id, userId]); return rows.length ? decode(rows[0].state_json) : null; },
        setDay: day => connection.query('INSERT INTO game_days(id,user_id,state_json) VALUES(?,?,?) ON DUPLICATE KEY UPDATE state_json=VALUES(state_json)', [day.id, userId, JSON.stringify(day)]),
        getOperation: async id => { const rows = await connection.query('SELECT request_hash,response_json FROM game_operations WHERE user_id=? AND operation_id=?', [userId,id]); return rows.length ? { hash: rows[0].request_hash, response: decode(rows[0].response_json) } : null; },
        setOperation: (id,hash,response) => connection.query('INSERT INTO game_operations(user_id,operation_id,request_hash,response_json) VALUES(?,?,?,?)',[userId,id,hash,JSON.stringify(response)]),
        entry: (source,coins,honey) => connection.query('INSERT INTO game_wallet_entries(user_id,source_id,coins_delta,honey_delta) VALUES(?,?,?,?)',[userId,source,coins,honey])
      };
      const result = await action(ctx);
      if (ctx.player) await connection.query('INSERT INTO game_players(user_id,state_json) VALUES(?,?) ON DUPLICATE KEY UPDATE state_json=VALUES(state_json)', [userId,JSON.stringify(ctx.player)]);
      await connection.commit();
      return result;
    } catch (error) { await connection.rollback(); throw error; }
    finally { connection.release(); }
  }
}
module.exports = { SqlGameRepository };
