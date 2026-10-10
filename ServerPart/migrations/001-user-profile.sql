-- Initial upgrade for the empty authentication database.
-- database.js checks that users is empty before adding required email.
ALTER TABLE users
    ADD COLUMN email VARCHAR(254) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ADD COLUMN phone VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD COLUMN balance DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (balance >= 0),
    ADD UNIQUE KEY uq_users_email (email);
