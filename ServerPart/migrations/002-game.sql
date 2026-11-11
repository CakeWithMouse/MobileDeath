CREATE TABLE IF NOT EXISTS game_players (
 user_id INT UNSIGNED PRIMARY KEY,
 state_json LONGTEXT NOT NULL CHECK(JSON_VALID(state_json)),
 CONSTRAINT fk_game_player FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS game_days (
 id CHAR(36) PRIMARY KEY,
 user_id INT UNSIGNED NOT NULL,
 state_json LONGTEXT NOT NULL CHECK(JSON_VALID(state_json)),
 INDEX ix_game_days_owner(user_id),
 CONSTRAINT fk_game_day FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS game_operations (
 user_id INT UNSIGNED NOT NULL,
 operation_id CHAR(36) NOT NULL,
 request_hash CHAR(64) NOT NULL,
 response_json LONGTEXT NOT NULL CHECK(JSON_VALID(response_json)),
 PRIMARY KEY(user_id, operation_id),
 CONSTRAINT fk_game_operation FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS game_wallet_entries (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 user_id INT UNSIGNED NOT NULL,
 source_id VARCHAR(80) NOT NULL,
 coins_delta BIGINT NOT NULL,
 honey_delta BIGINT NOT NULL,
 created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
 UNIQUE KEY uq_game_source(user_id, source_id),
 CONSTRAINT fk_game_entry FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;
