-- Apply manually to a NEW, EMPTY database selected by the SQL client.
-- No DROP, ALTER, automatic migration or connection to the legacy database.
CREATE TABLE users (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    login VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    password_hash VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uq_users_login (login)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE sessions (
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id INT UNSIGNED NOT NULL,
    expires_at DATETIME(3) NOT NULL,
    PRIMARY KEY (token_hash),
    KEY ix_sessions_expiry (expires_at),
    CONSTRAINT fk_sessions_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
