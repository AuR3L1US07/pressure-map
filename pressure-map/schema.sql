-- 学期压力地图 数据库结构（首次运行自动执行，可重复执行）
-- 游客和登录账户分别使用独立的本地数据库。
CREATE TABLE IF NOT EXISTS semesters (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  name           TEXT NOT NULL,
  total_weeks    INTEGER NOT NULL DEFAULT 18,
  week1_monday   TEXT,
  threshold      REAL NOT NULL DEFAULT 7,
  hours_per_week REAL NOT NULL DEFAULT 20,
  type_hours     TEXT NOT NULL DEFAULT '{}',
  current_step   INTEGER NOT NULL DEFAULT 1,
  created_at     TEXT NOT NULL,
  updated_at     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS courses (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  semester_id INTEGER NOT NULL REFERENCES semesters(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  raw_text    TEXT NOT NULL DEFAULT '',
  position    INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS tasks (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  semester_id INTEGER NOT NULL REFERENCES semesters(id) ON DELETE CASCADE,
  course_id   INTEGER NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
  name        TEXT NOT NULL,
  type        TEXT NOT NULL CHECK (type IN ('考试','大作业','报告','测验','其他')),
  week        INTEGER,
  weight      REAL NOT NULL,
  est_hours   REAL,
  source      TEXT NOT NULL DEFAULT '',
  position    INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_courses_semester ON courses(semester_id);
CREATE INDEX IF NOT EXISTS idx_tasks_semester ON tasks(semester_id);

CREATE TABLE IF NOT EXISTS desktop_todos (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  due_date TEXT,
  important INTEGER NOT NULL DEFAULT 0 CHECK (important IN (0,1)),
  completed INTEGER NOT NULL DEFAULT 0 CHECK (completed IN (0,1)),
  source_key TEXT UNIQUE,
  source_label TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS course_calendars (
 name TEXT PRIMARY KEY COLLATE NOCASE,
 format TEXT NOT NULL CHECK(format IN ('ics','csv')),
 content TEXT NOT NULL,
 imported_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS todo_details (
 todo_id TEXT PRIMARY KEY REFERENCES desktop_todos(id) ON DELETE CASCADE,
 payload TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS todo_reminders (
 todo_id TEXT PRIMARY KEY REFERENCES desktop_todos(id) ON DELETE CASCADE,
 delivered_key TEXT,
 snoozed_until TEXT
);
