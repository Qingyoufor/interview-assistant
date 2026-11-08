-- Qy · 面试陪练 MVP schema
-- MySQL 8.0+ / utf8mb4
-- 仅开发库：interview_assistant_dev

CREATE DATABASE IF NOT EXISTS interview_assistant_dev
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

USE interview_assistant_dev;

CREATE TABLE IF NOT EXISTS job_targets (
  id            CHAR(36)     PRIMARY KEY,
  user_id       VARCHAR(64)  NOT NULL DEFAULT 'local',
  title         VARCHAR(120) NOT NULL,
  level         VARCHAR(32)  NULL,
  jd_text       MEDIUMTEXT   NOT NULL,
  parsed_json   JSON         NULL,
  created_at    DATETIME(3)  NOT NULL,
  KEY idx_jt_user (user_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sessions (
  id             CHAR(36)     PRIMARY KEY,
  job_target_id  CHAR(36)     NOT NULL,
  user_id        VARCHAR(64)  NOT NULL DEFAULT 'local',
  status         ENUM('active','completed','abandoned') NOT NULL DEFAULT 'active',
  overall_score  DECIMAL(3,2) NULL,
  summary        TEXT         NULL,
  started_at     DATETIME(3)  NOT NULL,
  completed_at   DATETIME(3)  NULL,
  KEY idx_s_user (user_id, started_at),
  CONSTRAINT fk_s_jt FOREIGN KEY (job_target_id) REFERENCES job_targets(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS questions (
  id               CHAR(36)    PRIMARY KEY,
  session_id       CHAR(36)    NOT NULL,
  ordinal          INT         NOT NULL,
  type             ENUM('basic','project','scenario','behavior') NOT NULL,
  content          TEXT        NOT NULL,
  intent           TEXT        NULL,
  answer           MEDIUMTEXT  NULL,
  follow_up        TEXT        NULL,
  follow_up_answer MEDIUMTEXT  NULL,
  feedback         TEXT        NULL,
  created_at       DATETIME(3) NOT NULL,
  UNIQUE KEY uk_q_ord (session_id, ordinal),
  CONSTRAINT fk_q_s FOREIGN KEY (session_id) REFERENCES sessions(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS question_scores (
  id          CHAR(36)    PRIMARY KEY,
  question_id CHAR(36)    NOT NULL,
  dimension   ENUM('structure','depth','evidence','delivery') NOT NULL,
  score       TINYINT     NOT NULL,
  comment     VARCHAR(255) NULL,
  UNIQUE KEY uk_qs (question_id, dimension),
  CONSTRAINT fk_qs_q FOREIGN KEY (question_id) REFERENCES questions(id)
) ENGINE=InnoDB;
