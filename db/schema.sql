-- Interview Assistant · schema v3
-- MySQL 8.0+ / utf8mb4
-- 与 docs/spec-v3.md §9 对应

CREATE DATABASE IF NOT EXISTS interview_assistant_dev
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

USE interview_assistant_dev;

-- 先删后建（开发库无数据，按 v3 重建）
DROP TABLE IF EXISTS question_scores;
DROP TABLE IF EXISTS questions;
DROP TABLE IF EXISTS sessions;
DROP TABLE IF EXISTS matches;
DROP TABLE IF EXISTS job_targets;
DROP TABLE IF EXISTS resumes;

CREATE TABLE resumes (
  id          CHAR(36)     PRIMARY KEY,
  user_id     VARCHAR(64)  NOT NULL DEFAULT 'local',
  title       VARCHAR(120) NOT NULL,
  raw_text    MEDIUMTEXT   NOT NULL,
  parsed_json JSON         NULL,          -- {profile, anchors[]}
  created_at  DATETIME(3)  NOT NULL,
  KEY idx_r_user (user_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE job_targets (
  id          CHAR(36)     PRIMARY KEY,
  user_id     VARCHAR(64)  NOT NULL DEFAULT 'local',
  title       VARCHAR(120) NOT NULL,
  level       VARCHAR(32)  NULL,
  jd_text     MEDIUMTEXT   NOT NULL,
  parsed_json JSON         NULL,          -- {job, anchors[]}
  created_at  DATETIME(3)  NOT NULL,
  KEY idx_jt_user (user_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE matches (
  id            CHAR(36)    PRIMARY KEY,
  resume_id     CHAR(36)    NOT NULL,
  job_target_id CHAR(36)    NOT NULL,
  input_hash    CHAR(64)    NOT NULL,     -- 简历/JD 解析结果指纹，变了就重算
  overall_match TINYINT     NOT NULL,
  hard_block    TINYINT(1)  NOT NULL DEFAULT 0,
  result_json   JSON        NOT NULL,     -- MatchResult（spec §5）
  created_at    DATETIME(3) NOT NULL,
  KEY idx_m_pair (resume_id, job_target_id, created_at),
  CONSTRAINT fk_m_resume FOREIGN KEY (resume_id) REFERENCES resumes(id),
  CONSTRAINT fk_m_jd FOREIGN KEY (job_target_id) REFERENCES job_targets(id)
) ENGINE=InnoDB;

CREATE TABLE sessions (
  id             CHAR(36)     PRIMARY KEY,
  job_target_id  CHAR(36)     NOT NULL,
  resume_id      CHAR(36)     NULL,
  match_id       CHAR(36)     NULL,
  user_id        VARCHAR(64)  NOT NULL DEFAULT 'local',
  status         ENUM('active','completed','abandoned') NOT NULL DEFAULT 'active',
  overall_score  DECIMAL(3,2) NULL,
  summary        TEXT         NULL,
  started_at     DATETIME(3)  NOT NULL,
  completed_at   DATETIME(3)  NULL,
  KEY idx_s_user (user_id, started_at),
  KEY idx_s_resume (resume_id),
  CONSTRAINT fk_s_jt FOREIGN KEY (job_target_id) REFERENCES job_targets(id),
  CONSTRAINT fk_s_resume FOREIGN KEY (resume_id) REFERENCES resumes(id),
  CONSTRAINT fk_s_match FOREIGN KEY (match_id) REFERENCES matches(id)
) ENGINE=InnoDB;

CREATE TABLE questions (
  id               CHAR(36)    PRIMARY KEY,
  session_id       CHAR(36)    NOT NULL,
  ordinal          INT         NOT NULL,
  type             ENUM('basic','project','scenario','behavior') NOT NULL,
  source           ENUM('resume','gap','general') NOT NULL DEFAULT 'general',
  content          TEXT        NOT NULL,
  intent           TEXT        NULL,
  anchor_ids       JSON        NULL,       -- 证据锚点 id 数组（spec §4）
  answer           MEDIUMTEXT  NULL,
  follow_up        TEXT        NULL,
  follow_up_answer MEDIUMTEXT  NULL,
  feedback         TEXT        NULL,
  assessment_json  JSON        NULL,       -- 原子分 + 展示分（spec §7）
  claim_checks     JSON        NULL,       -- 声明核验（spec §7）
  created_at       DATETIME(3) NOT NULL,
  UNIQUE KEY uk_q_ord (session_id, ordinal),
  CONSTRAINT fk_q_s FOREIGN KEY (session_id) REFERENCES sessions(id)
) ENGINE=InnoDB;

CREATE TABLE question_scores (
  id          CHAR(36)    PRIMARY KEY,
  question_id CHAR(36)    NOT NULL,
  dimension   ENUM('structure','depth','evidence','delivery') NOT NULL,
  score       TINYINT     NOT NULL,
  comment     VARCHAR(255) NULL,
  UNIQUE KEY uk_qs (question_id, dimension),
  CONSTRAINT fk_qs_q FOREIGN KEY (question_id) REFERENCES questions(id)
) ENGINE=InnoDB;
